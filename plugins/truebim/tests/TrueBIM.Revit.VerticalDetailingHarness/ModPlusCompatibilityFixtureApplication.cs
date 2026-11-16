using System.IO;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.RebarDetailing;

namespace TrueBIM.Revit.VerticalDetailingHarness;

// Optional, isolated compatibility fixture. Loads vendor binaries from a supplied
// scratch directory, never installs ModPlus or reads/writes its user settings.
public sealed class ModPlusCompatibilityFixtureApplication : IExternalApplication
{
    private static Type? appType;
    private static readonly List<IUpdater> updaters = new();
    private static string? error;
    private static string? binary;

    public Result OnStartup(UIControlledApplication application)
    {
        string root = Environment.GetEnvironmentVariable("TRUEBIM_VERTICAL_MODPLUS") ?? "";
        if (!Directory.Exists(root)) return Result.Succeeded;
        try
        {
            foreach (string dependency in new[] { "ModPlusAPI.dll", "ModPlusStyle.dll", "ModPlus_Revit_2024.dll" })
                Assembly.LoadFrom(Path.Combine(root, "Extensions", dependency));
            binary = Path.Combine(root, "Functions", "Revit", "mprDimBias", "mprDimBias_2024.dll");
            var assembly = Assembly.LoadFrom(binary);
            appType = assembly.GetType("mprDimBias.Application.App", true)!;
            appType.GetProperty("DimsModifiedByUpdater")!.SetValue(null, new Dictionary<ElementId, bool>());
            appType.GetProperty("IsSyncInWork")!.SetValue(null, true);
            foreach (string name in new[] { "CreateUpdater", "ModifyUpdater" })
            {
                var updater = (IUpdater)Activator.CreateInstance(assembly.GetType("mprDimBias.Updater." + name, true)!)!;
                appType.GetProperty(name)!.SetValue(null, updater);
                UpdaterRegistry.RegisterUpdater(updater, true);
                UpdaterRegistry.AddTrigger(updater.GetUpdaterId(), new ElementClassFilter(typeof(Dimension)),
                    name == "CreateUpdater" ? Element.GetChangeTypeElementAddition() : Element.GetChangeTypeAny());
                updaters.Add(updater);
            }
        }
        catch (Exception ex) { error = ex.ToString(); }
        return Result.Succeeded;
    }

    internal static void InitializeWindow(UIApplication ui)
    {
        if (appType != null)
            appType.GetProperty("RevitWindowUtils")!.SetValue(null,
                Activator.CreateInstance(appType.Assembly.GetType("mprDimBias.Utils.RevitWindowUtils", true)!, ui));
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        foreach (var updater in updaters)
            if (UpdaterRegistry.IsUpdaterRegistered(updater.GetUpdaterId())) UpdaterRegistry.UnregisterUpdater(updater.GetUpdaterId());
        updaters.Clear(); return Result.Succeeded;
    }

    internal static object Check(UIDocument ui, View view, RebarDetailingService service, DetailingOptions options)
    {
        if (error != null || appType == null || updaters.Count != 2)
            return new { Passed = false, Error = error ?? "Official ModPlus fixture was not loaded" };
        var assembly = appType.Assembly;
        appType.GetProperty("RevitWindowUtils")!.SetValue(null,
            Activator.CreateInstance(assembly.GetType("mprDimBias.Utils.RevitWindowUtils", true)!, ui.Application));
        bool[] State() => updaters.Select(u => UpdaterRegistry.IsUpdaterEnabled(u.GetUpdaterId())).ToArray();
        var before = State();
        var pause = appType.GetProperty("IsSyncInWork")!;
        bool originalPause = (bool)pause.GetValue(null)!;
        var commits = new List<bool>();
        void OnChanged(object sender, DocumentChangedEventArgs e)
        { if (e.GetDocument().Equals(ui.Document)) commits.Add((bool)pause.GetValue(null)!); }
        ui.Application.Application.DocumentChanged += OnChanged;
        try
        {
            pause.SetValue(null, false);
            var result = service.Apply(ui.Document, new[] { view }, options).Single();
            bool during = commits.Count > 0 && commits.All(paused => paused);
            bool restored = !(bool)pause.GetValue(null)! && State().SequenceEqual(before);
            commits.Clear();
            bool exception = false;
            IEnumerable<View> FailingViews()
            { yield return view; throw new InvalidOperationException("Injected batch failure after a committed view"); }
            try { service.Apply(ui.Document, FailingViews(), options); }
            catch (InvalidOperationException ex) { exception = ex.Message.StartsWith("Injected batch failure", StringComparison.Ordinal); }
            bool restoredAfterException = exception && !(bool)pause.GetValue(null)! && State().SequenceEqual(before);
            pause.SetValue(null, true);
            var disabled = service.Apply(ui.Document, new[] { view }, options).Single();
            bool offPreserved = (bool)pause.GetValue(null)! && State().SequenceEqual(before);
            return new { Passed = result.Succeeded && disabled.Succeeded && during && restored && restoredAfterException && offPreserved,
                Assembly = binary, Version = assembly.GetName().Version?.ToString(), Result = result,
                PausedDuringCommits = during, RuntimeStateRestored = restored, RestoredAfterException = restoredAfterException,
                InitiallyPausedPreserved = offPreserved, UpdaterRegistryUnchanged = State().SequenceEqual(before), VendorSettingsTouched = false };
        }
        catch (Exception ex) { return new { Passed = false, Error = ex.ToString(), Assembly = binary }; }
        finally
        {
            ui.Application.Application.DocumentChanged -= OnChanged;
            pause.SetValue(null, originalPause);
        }
    }
}
