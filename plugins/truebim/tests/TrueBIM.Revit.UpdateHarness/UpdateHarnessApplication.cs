using System.IO;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using TrueBIM.App.Modules.Updates;
using TrueBIM.App.Services.Logging;
using TrueBIM.Updates;

namespace TrueBIM.Revit.UpdateHarness;

public sealed class UpdateHarnessApplication : IExternalApplication
{
    private string? root;
    private bool ran;
    public Result OnStartup(UIControlledApplication app)
    {
        root = Environment.GetEnvironmentVariable("TRUEBIM_UPDATE_QA");
        if (!string.IsNullOrWhiteSpace(root)) app.Idling += Run;
        return Result.Succeeded;
    }
    public Result OnShutdown(UIControlledApplication app) { app.Idling -= Run; return Result.Succeeded; }
    private void Run(object? sender, IdlingEventArgs args)
    {
        if (ran) return;
        ran = true;
        try
        {
            UIApplication app = (UIApplication)sender!;
            PushButton button = app.GetRibbonPanels("TrueBIM").SelectMany(panel => panel.GetItems()).OfType<PushButton>()
                .Single(item => item.Name == "TrueBIM_CheckForUpdates");
            if (!button.Enabled || app.ActiveUIDocument != null) throw new InvalidOperationException("Update button is unavailable without a model.");
            List<object> checks = new() { new { Case = "Native ribbon without document", Passed = true, button.ItemText } };
            string shared = Path.Combine(root!, "share");
            Directory.CreateDirectory(shared);
            foreach (string mode in new[] { "current", "available", "offline", "malformed", "production" })
            foreach (double width in new[] { 580.0, 520.0 })
            {
                PrepareFeed(shared, mode);
                SharedFolderUpdateService service = mode == "production" ? new() : new(
                    mode == "offline" ? Path.Combine(root!, "not-connected") : shared,
                    Path.Combine(root!, "cache"), Path.Combine(root!, "TrueBIM.Updater.exe"));
                UpdateWindow window = new("2024", new TestLogger(root!), service) { Width = width };
                new System.Windows.Interop.WindowInteropHelper(window).Owner = app.MainWindowHandle;
                Exception? failure = null;
                window.Loaded += async (_, _) =>
                {
                    try
                    {
                        Button check = Find<Button>(window, "CheckUpdates");
                        Button update = Find<Button>(window, "PrepareUpdate");
                        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
                        while (!check.IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(50);
                        if (!check.IsEnabled) throw new TimeoutException("Update window did not complete its check.");
                        if (update.IsEnabled != (mode == "available")) throw new InvalidOperationException("Wrong update availability: " + mode);
                        window.UpdateLayout();
                        foreach (Button control in Descendants(window).OfType<Button>())
                        {
                            Point end = control.TranslatePoint(new Point(control.ActualWidth, control.ActualHeight), window);
                            if (end.X > window.ActualWidth + 1 || end.Y > window.ActualHeight + 1) throw new InvalidOperationException("Update button overflows window.");
                        }
                        string text = Find<TextBlock>(window, "UpdateStatus").Text;
                        Capture(window, mode + "-" + width);
                        checks.Add(new { Case = mode + "-" + width, Passed = true, Status = text, Width = window.ActualWidth, Height = window.ActualHeight });
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { window.Close(); }
                };
                window.ShowDialog();
                if (failure != null) throw failure;
            }
            // Check the real Inno resource format (including its padded strings).
            string realInstaller = Path.Combine(root!, "TrueBIM-Setup-0.2.51.exe");
            string realHash;
            using (FileStream stream = File.OpenRead(realInstaller)) realHash = UpdatePackage.Hash(stream);
            using (new UpdatePackage("0.2.51", Path.GetFileName(realInstaller), realHash).OpenVerifiedInstaller(realInstaller)) { }
            checks.Add(new { Case = "Real Inno package metadata and SHA256", Passed = true });
            Write("native-ui.json", checks);
            File.WriteAllText(Path.Combine(root!, "done"), "Native update checks passed; close test via stop file.");
        }
        catch (Exception exception) { File.WriteAllText(Path.Combine(root!, "failed"), exception.ToString()); }
    }

    private void PrepareFeed(string folder, string mode)
    {
        string version = mode == "available" ? "99.0.0" : SharedFolderUpdateService.CurrentVersion.ToString(3);
        object manifest = new { schemaVersion = 1, product = "TrueBIM", latest = new { version, file = "TrueBIM-Setup-" + version + ".exe", sha256 = new string('a', 64) } };
        File.WriteAllText(Path.Combine(folder, SharedFolderUpdateService.ManifestFileName), mode == "malformed" ? "{broken}" : new JavaScriptSerializer().Serialize(manifest));
    }
    private void Capture(Window window, string name)
    {
        RenderTargetBitmap bitmap = new((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream file = File.Create(Path.Combine(root!, name + ".png"));
        encoder.Save(file);
    }
    private void Write(string name, object value) => File.WriteAllText(Path.Combine(root!, name), new JavaScriptSerializer().Serialize(value));
    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement => Descendants(root).OfType<T>().Single(control => control.Name == name);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject item)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(item); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(item, index);
            yield return child;
            foreach (DependencyObject nested in Descendants(child)) yield return nested;
        }
    }
    private sealed class TestLogger(string folder) : ITrueBimLogger
    {
        public void Info(string message) => File.AppendAllText(Path.Combine(folder, "events.log"), message + Environment.NewLine);
        public void Warning(string message) => Info(message);
        public void Error(string message, Exception? exception = null) => Info(message + exception);
    }
}
