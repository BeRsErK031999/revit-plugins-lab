using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Interop;
using TextBox = System.Windows.Controls.TextBox;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Modules.Print.Services;
using TrueBIM.App.Modules.Print.UI;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.Revit.PrintHarness;

/// <summary>Opt-in verification in a second Revit instance using a disposable, detached RVT copy.</summary>
public sealed class PrintHarnessApplication : IExternalApplication, ITrueBimLogger
{
    private string root = "";
    private bool opened;
    private PrintWindow? window;
    private Document? document;
    public Result OnStartup(UIControlledApplication application)
    {
        root = Environment.GetEnvironmentVariable("TRUEBIM_PRINT_QA") ?? "";
        if (root.Length > 0)
        {
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            application.Idling += Run;
            application.DialogBoxShowing += Dialog;
        }
        return Result.Succeeded;
    }
    private Assembly? Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name + ".dll";
        string path = Path.Combine(root, "app", name);
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }
    public Result OnShutdown(UIControlledApplication application)
    {
        application.Idling -= Run;
        application.DialogBoxShowing -= Dialog;
        AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
        return Result.Succeeded;
    }
    private void Dialog(object? sender, DialogBoxShowingEventArgs args)
    {
        Info("DIALOG " + args.DialogId);
        if (args.DialogId == "TaskDialog_Unresolved_References") args.OverrideResult(1002);
    }
    private void Run(object? sender, IdlingEventArgs args)
    {
        UIApplication app = (UIApplication)sender!;
        try
        {
            if (!opened)
            {
                opened = true;
                string path = Path.Combine(root, "models", "print-qa.rvt");
                ModelPath model = ModelPathUtils.ConvertUserVisiblePathToModelPath(path);
                TransmissionData? data = TransmissionData.ReadTransmissionData(model);
                if (data != null)
                {
                    foreach (ElementId id in data.GetAllExternalFileReferenceIds())
                    {
                        ExternalFileReference reference = data.GetLastSavedReferenceData(id);
                        if (reference.ExternalFileReferenceType == ExternalFileReferenceType.RevitLink)
                            data.SetDesiredReferenceData(id, reference.GetPath(), reference.PathType, false);
                    }
                    data.IsTransmitted = true;
                    TransmissionData.WriteTransmissionData(model, data);
                }
                using OpenOptions options = new() { DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets };
                document = app.OpenAndActivateDocument(model, options, false).Document;
                Info("OPENED " + document.PathName);
                ShowWindow(app);
                File.WriteAllText(Path.Combine(root, "ready"), "ready");
                return;
            }
            string request = Path.Combine(root, "request.txt");
            if (!File.Exists(request)) return;
            string action = File.ReadAllText(request).Trim();
            File.Delete(request);
            if (action == "native") RunNative();
            if (action == "ui") ShowWindow(app);
            if (action == "close-ui") { window?.Close(); window = null; }
        }
        catch (Exception exception)
        {
            Error("QA failed", exception);
            File.WriteAllText(Path.Combine(root, "failed.txt"), exception.ToString());
        }
    }
    private void ShowWindow(UIApplication app)
    {
        window?.Close();
        var sheets = new PrintSheetCollectorService().Collect(document!);
        File.WriteAllLines(Path.Combine(root, "sheets.tsv"), sheets.Select(s => $"{s.ElementId}\t{s.GroupName}\t{s.SheetNumber}\t{s.SheetName}\t{s.SheetFormat}"));
        window = new PrintWindow(document!, sheets, this);
        new WindowInteropHelper(window).Owner = app.MainWindowHandle;
        Field<TextBox>("exportFolderInput").Text = Path.Combine(root, "UI PDF");
        Field<TextBox>("fileNameMaskInput").Text = "{Номер листа}_{Имя листа}";
        Field<TextBox>("combinedPdfNameInput").Text = "Комплект проверки";
        Field<CheckBox>("pdfInput").IsChecked = true;
        Field<CheckBox>("combinePdfInput").IsChecked = false;
        Field<CheckBox>("dwgInput").IsChecked = false;
        Field<CheckBox>("dxfInput").IsChecked = false;
        Field<CheckBox>("dwfInput").IsChecked = false;
        window.Show();
        Info("UI ready: " + sheets.Count + " sheets");
    }
    private T Field<T>(string name) => (T)typeof(PrintWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
    private void RunNative()
    {
        Info("NATIVE START");
        List<PrintPdfExportItem> items = new FilteredElementCollector(document).OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>().Where(s => !s.IsPlaceholder && s.CanBePrinted).OrderBy(s => s.SheetNumber)
            .Take(3).Select(s => new PrintPdfExportItem(s.Id.Value, s.SheetNumber + "_" + s.Name)).ToList();
        var service = new PrintPdfExportService();
        foreach (bool combined in new[] { false, true })
        {
            string folder = Path.Combine(root, combined ? "Native combined" : "Native separate");
            PrintPdfExportSettings settings = new(combined ? PrintPdfColorMode.Color : PrintPdfColorMode.BlackLine,
                PrintPdfRasterQuality.High, false);
            var result = service.Export(document!, folder, items,
                combined ? PrintPdfExportMode.CombinedFile : PrintPdfExportMode.SeparateFiles,
                "Проверка объединения", settings, this, step => Info("PROGRESS " + step));
            if (result.Failures.Count != 0) throw new InvalidOperationException(string.Join("; ", result.Failures.Select(f => f.Message)));
            if (result.ExportedFiles.Count != (combined ? 1 : items.Count)) throw new InvalidOperationException("Unexpected output count");
            foreach (string file in result.ExportedFiles)
                if (!File.Exists(file) || new FileInfo(file).Length < 100) throw new InvalidOperationException("Missing PDF: " + file);
            Info("PASS " + (combined ? "combined PDF" : "separate PDF") + ": " + string.Join("; ", result.ExportedFiles));
        }
        File.WriteAllText(Path.Combine(root, "native-done"), "Native export passed");
    }
    public void Info(string message) => File.AppendAllText(Path.Combine(root, "report.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
    public void Warning(string message) => Info("WARNING " + message);
    public void Error(string message, Exception? exception = null) => Info("ERROR " + message + " " + exception);
}
