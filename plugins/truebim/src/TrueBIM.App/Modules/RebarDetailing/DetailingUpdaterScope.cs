using Autodesk.Revit.DB;
using System.Reflection;

namespace TrueBIM.App.Modules.RebarDetailing;

/// <summary>Temporarily pauses ModPlus dimension processing without changing user preferences.</summary>
internal sealed class DetailingUpdaterScope : IDisposable
{
    private readonly List<(PropertyInfo Property, bool Value)> suspended = new();

    public DetailingUpdaterScope(Document document)
    {
        try
        {
            var known = new HashSet<string>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a =>
                a.GetName().Name == "mprDimBias" || a.GetName().Name?.StartsWith("mprDimBias_", StringComparison.Ordinal) == true))
            {
                var app = assembly.GetType("mprDimBias.Application.App", false);
                var updaters = new[] { "CreateUpdater", "ModifyUpdater" }
                    .Select(name => app?.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IUpdater)
                    .Where(u => u != null).Cast<IUpdater>().ToList();
                bool active = updaters.Any(u => UpdaterRegistry.IsUpdaterRegistered(u.GetUpdaterId())
                    && UpdaterRegistry.IsUpdaterEnabled(u.GetUpdaterId()));
                foreach (var updater in updaters) known.Add(updater.GetUpdaterName());
                if (!active) continue;

                // Verified against official mprDimBias 6.3.9. Both Execute methods
                // honor this runtime guard during synchronization. Revit forbids
                // Enable/DisableUpdater for another add-in; never impersonate it.
                // This optional vendor-internal contract is checked before any
                // transaction. User settings and updater triggers stay intact.
                var pause = app?.GetProperty("IsSyncInWork", BindingFlags.Public | BindingFlags.Static);
                if (pause?.PropertyType != typeof(bool) || pause.GetSetMethod(true) == null)
                    throw Unsupported();
                bool previous = (bool)pause.GetValue(null)!;
                if (previous) continue;
                suspended.Add((pause, previous));
                pause.SetValue(null, true);
                if (!(bool)pause.GetValue(null)!) throw Unsupported();
            }
            if (UpdaterRegistry.GetRegisteredUpdaterInfos(document).Any(info =>
                (info.UpdaterName == "DimBiasUpdater" || info.UpdaterName == "ModifiedDimBiasUpdater") && !known.Contains(info.UpdaterName)))
                throw Unsupported();
        }
        catch { Dispose(); throw; }
    }

    private static InvalidOperationException Unsupported() => new(
        "Версия ModPlus не поддерживает временную приостановку. Отключите автоматическое смещение размеров в ModPlus перед оформлением.");

    public void Dispose()
    {
        var failures = new List<Exception>();
        foreach (var state in suspended.AsEnumerable().Reverse())
        {
            try { state.Property.SetValue(null, state.Value); }
            catch (Exception ex) { failures.Add(ex); }
        }
        suspended.Clear();
        if (failures.Count > 0)
            throw new AggregateException("Не удалось восстановить автоматическое смещение размеров ModPlus.", failures);
    }
}
