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
                if (Environment.GetEnvironmentVariable("TRUEBIM_PRINT_ACCEPTANCE") == "state")
                    RunStateProbe();
                else if (Environment.GetEnvironmentVariable("TRUEBIM_PRINT_ACCEPTANCE") == "1")
                    RunAcceptance();
                else ShowWindow(app);
                File.WriteAllText(Path.Combine(root, "ready"), "ready");
                return;
            }
            string request = Path.Combine(root, "request.txt");
            if (!File.Exists(request)) return;
            string action = File.ReadAllText(request).Trim();
            File.Delete(request);
            if (action == "native") RunNative();
            if (action == "acceptance") RunAcceptance();
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
            .Take(3).Select(s => new PrintPdfExportItem(IdValue(s.Id), s.SheetNumber + "_" + s.Name)).ToList();
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

    private void RunAcceptance()
    {
        Info("ACCEPTANCE START Revit " + document!.Application.VersionNumber +
            " app " + typeof(PrintDriverPdfService).Assembly.FullName);
        var sheets = new FilteredElementCollector(document).OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>().Where(s => !s.IsPlaceholder && s.CanBePrinted)
            .OrderBy(s => s.SheetNumber, new PrintSheetNumberComparer()).ToList();
        File.WriteAllLines(Path.Combine(root, "sheets.tsv"),
            new PrintSheetCollectorService().Collect(document).Select(s => $"{s.ElementId}\t{s.SheetNumber}\t{s.SheetName}\t{s.SheetFormat}"));
        var items = sheets.Select(s => new PrintPdfExportItem(IdValue(s.Id),
            PrintPdfExportService.BuildCombinedPdfFileName(s.SheetNumber + "_" + s.Name))).ToList();
        string originalSettings = NamedPrintState();
        Info("SAVED SETTINGS BEFORE " + originalSettings);
        foreach (bool combined in new[] { false, true })
        {
            string folder = Path.Combine(root, combined ? "PDFCreator combined" : "PDFCreator separate");
            var result = new PrintDriverPdfService().Print(document, folder, items, combined, "Комплект проверки",
                "PDFCreator", null, true, PrintPdfExportService.DefaultSettings, this, step => Info("PROGRESS " + step));
            if (result.Failures.Count > 0 || result.ExportedFiles.Count != (combined ? 1 : items.Count))
                throw new InvalidOperationException("PDFCreator route failed: " + string.Join("; ", result.Failures.Select(f => f.Message)));
            int pageCount = 0;
            foreach (string file in result.ExportedFiles)
            {
                using var pdf = UglyToad.PdfPig.PdfDocument.Open(file);
                foreach (var page in pdf.GetPages())
                {
                    var sheet = sheets[pageCount++];
                    var block = new FilteredElementCollector(document, sheet.Id)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().FirstElement();
                    if (block is null) throw new InvalidOperationException("Missing titleblock: " + sheet.SheetNumber);
                    double width = block.get_Parameter(BuiltInParameter.SHEET_WIDTH).AsDouble() * 304.8;
                    double height = block.get_Parameter(BuiltInParameter.SHEET_HEIGHT).AsDouble() * 304.8;
                    if (Math.Abs(page.Width * 25.4 / 72 - width) > 1 || Math.Abs(page.Height * 25.4 / 72 - height) > 1)
                        throw new InvalidOperationException("Incorrect PDF size: " + sheet.SheetNumber);
                    Info($"PAGE {sheet.SheetNumber}: {page.Width * 25.4 / 72:F1} x {page.Height * 25.4 / 72:F1} mm; letters {page.Letters.Count}");
                    if (page.Letters.Count == 0) throw new InvalidOperationException("Empty PDF page: " + sheet.SheetNumber);
                }
            }
            string afterSettings = NamedPrintState();
            Info("SAVED SETTINGS AFTER " + afterSettings);
            if (pageCount != items.Count || afterSettings != originalSettings)
                throw new InvalidOperationException("Page count or saved print settings mismatch.");
            Info("PASS PDFCreator " + (combined ? "combined" : "separate") + ": " + pageCount + " pages; saved settings unchanged");
        }
        RunPaperRecurrence();
        File.WriteAllText(Path.Combine(root, "acceptance-done"), "PASS " + items.Count + " separate pages, combined pages, exact sizes, text, saved settings preserved, A4/A3/A4");
    }

    private void RunPaperRecurrence()
    {
        // Regress the actual A4 -> A3 -> A4 preset switch that failed in 0.2.52.
        using (var group = new TransactionGroup(document, "QA paper recurrence"))
        {
            group.Start();
            var manager = document!.PrintManager;
            manager.SelectNewPrintDriver("Microsoft Print to PDF");
            var type = typeof(PrintManagerService).Assembly.GetType("TrueBIM.App.Modules.Print.Services.AutoSheetPrintLayout")!;
            var layout = Activator.CreateInstance(type, document, manager);
            var configure = type.GetMethod("Configure")!;
            foreach (var size in new[] { new PrintPaperSize(210, 297), new PrintPaperSize(297, 420), new PrintPaperSize(210, 297) })
            {
                configure.Invoke(layout, new object[] { size, PrintPdfExportService.DefaultSettings });
                manager.Apply();
                Info("PASS paper recurrence: " + size);
            }
            group.RollBack();
        }
        if (new FilteredElementCollector(document).OfClass(typeof(PrintSetting))
            .Cast<PrintSetting>().Any(s => s.Name.StartsWith("TrueBIM-")))
            throw new InvalidOperationException("Temporary presets leaked.");
    }

    private void RunStateProbe()
    {
        // Revit resolves a saved setting for a missing driver on first access.
        Info("PROBE INITIAL " + PrintState());
        string before = PrintState();
        Info("PROBE BEFORE " + before);
        string presets = NamedPrintState();
        Info("PROBE PRESETS BEFORE " + presets);
        var sheet = new FilteredElementCollector(document).OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>().First(s => s.SheetNumber == "00");
        var result = new PrintDriverPdfService().Print(document!, Path.Combine(root, "State probe"),
            new[] { new PrintPdfExportItem(IdValue(sheet.Id), "Проверка состояния") }, false, null,
            "PDFCreator", null, true, PrintPdfExportService.DefaultSettings, this, null);
        string after = PrintState();
        Info("PROBE AFTER " + after);
        Info("PROBE PRESETS AFTER " + NamedPrintState());
        if (result.Failures.Count > 0 || NamedPrintState() != presets)
            throw new InvalidOperationException("Probe failed or saved presets changed: " + string.Join("; ", result.Failures.Select(f => f.Message)));
        RunNamedStateProbe(sheet);
        RunPaperRecurrence();
        File.WriteAllText(Path.Combine(root, "state-done"), "Native PDF and saved presets PASS; native export changes in-session settings: " + (before != after) + "; A4/A3/A4 PASS");
    }

    private void RunNamedStateProbe(ViewSheet sheet)
    {
        using var group = new TransactionGroup(document, "QA named print state");
        group.Start();
        var manager = document!.PrintManager;
        using (var transaction = new Transaction(document, "QA saved print setup"))
        {
            transaction.Start();
            manager.PrintSetup.CurrentPrintSetting = manager.PrintSetup.InSession;
            if (!manager.PrintSetup.SaveAs("TrueBIM-QA-native-state")) throw new InvalidOperationException("Cannot create QA setup");
            transaction.Commit();
        }
        using (var transaction = new Transaction(document, "QA named print parameters"))
        {
            transaction.Start();
            manager.PrintSetup.CurrentPrintSetting = new FilteredElementCollector(document).OfClass(typeof(PrintSetting))
                .Cast<PrintSetting>().Single(s => s.Name == "TrueBIM-QA-native-state");
            var p = manager.PrintSetup.CurrentPrintSetting.PrintParameters;
            p.PageOrientation = PageOrientationType.Portrait;
            p.ZoomType = ZoomType.Zoom;
            p.Zoom = 77;
            p.RasterQuality = RasterQualityType.Presentation;
            p.ColorDepth = ColorDepthType.GrayScale;
            p.HideScopeBoxes = false;
            if (!manager.PrintSetup.Save()) throw new InvalidOperationException("Cannot configure QA setup");
            transaction.Commit();
        }
        manager.Apply();
        string before = NamedPrintState();
        Info("NAMED BEFORE " + before);
        var result = new PrintDriverPdfService().Print(document, Path.Combine(root, "Named state probe"),
            new[] { new PrintPdfExportItem(IdValue(sheet.Id), "Проверка именованной настройки") }, true, "Проверка именованной настройки",
            "PDFCreator", null, true, PrintPdfExportService.DefaultSettings, this, null);
        string after = NamedPrintState();
        Info("NAMED AFTER " + after);
        if (result.Failures.Count > 0 || before != after) throw new InvalidOperationException("Named print state changed after native PDF");
        group.RollBack();
        Info("PASS named print state");
    }

    private string NamedPrintState() => string.Join(";", new FilteredElementCollector(document).OfClass(typeof(PrintSetting))
        .Cast<PrintSetting>().OrderBy(s => IdValue(s.Id)).Select(s =>
        {
            try
            {
                var p = s.PrintParameters;
                return string.Join("|", IdValue(s.Id), s.Name, p.PaperSize.Name, p.PageOrientation, p.ZoomType,
                    p.ColorDepth, p.RasterQuality, p.HiddenLineViews);
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // Missing driver: Revit cannot expose paper parameters of this saved setting.
                return string.Join("|", IdValue(s.Id), s.Name, "driver parameters unavailable");
            }
        }));

    private string PrintState()
    {
        var manager = document!.PrintManager;
        string printer = manager.PrinterName;
        var setup = manager.PrintSetup.CurrentPrintSetting;
        var parameters = setup.PrintParameters;
        return string.Join("|", printer, (setup as PrintSetting)?.Name ?? "in-session", parameters.PaperSize.Name, parameters.PageOrientation,
            parameters.ZoomType, parameters.ColorDepth, parameters.RasterQuality, parameters.HiddenLineViews,
            parameters.ZoomType == ZoomType.Zoom ? parameters.Zoom : 0, parameters.PaperPlacement,
            parameters.MaskCoincidentLines, parameters.ReplaceHalftoneWithThinLines, parameters.HideCropBoundaries,
            parameters.HideScopeBoxes, parameters.HideUnreferencedViewTags, parameters.HideReforWorkPlanes, parameters.ViewLinksinBlue,
            new FilteredElementCollector(document).OfClass(typeof(PrintSetting)).GetElementCount());
    }

    private static long IdValue(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}
