using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Print.Models;
using WindowsPrinting = System.Drawing.Printing;

namespace TrueBIM.App.Modules.Print.Services;

internal sealed class AutoSheetPrintLayout
{
    private readonly Document document;
    private readonly PrintSetup setup;
    private readonly IPrintSetting original;
    private readonly Dictionary<(string Name, bool Rotate), IPrintSetting> settingsByPaper = new();
    private readonly Dictionary<string, PaperSize> revitPapers;
    private readonly List<PrinterPaper> driverPapers;
    public AutoSheetPrintLayout(Document document, PrintManager manager)
    {
        this.document = document;
        revitPapers = manager.PaperSizes.Cast<PaperSize>()
            .GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var driver = new WindowsPrinting.PrinterSettings { PrinterName = manager.PrinterName };
        driverPapers = driver.PaperSizes.Cast<WindowsPrinting.PaperSize>()
            .Where(p => p.Width > 0 && p.Height > 0 && revitPapers.ContainsKey(p.PaperName.Trim()))
            .Select(p => new PrinterPaper(p.PaperName.Trim(), new PrintPaperSize(p.Width * 0.254, p.Height * 0.254)))
            .ToList();

        setup = manager.PrintSetup;
        original = setup.CurrentPrintSetting;
    }

    public PrintPaperMatch Configure(PrintPaperSize sheetSize, PrintPdfExportSettings? settings)
    {
        var match = PrintPaperMatcher.Find(sheetSize, driverPapers)
            ?? throw new InvalidOperationException($"Лист {sheetSize} не помещается на доступную бумагу при 100%. Добавьте формат в драйвер или выберите экспорт в PDF.");
        var key = (match.Paper.Name, match.Rotate);
        if (settingsByPaper.TryGetValue(key, out var existing))
        {
            setup.CurrentPrintSetting = existing;
            return match;
        }

        string name = "TrueBIM-" + Guid.NewGuid().ToString("N");
        using (var tx = new Transaction(document, "TrueBIM — временная настройка"))
        {
            tx.Start();
            setup.CurrentPrintSetting = original;
            if (!setup.SaveAs(name) || tx.Commit() != TransactionStatus.Committed)
                throw new InvalidOperationException("Revit не создал временную настройку печати.");
        }

        var saved = new FilteredElementCollector(document).OfClass(typeof(PrintSetting))
            .Cast<PrintSetting>().Single(setting => setting.Name == name);
        using (var tx = new Transaction(document, "TrueBIM — формат листа"))
        {
            tx.Start();
            setup.CurrentPrintSetting = saved;
            var parameters = setup.CurrentPrintSetting.PrintParameters;
            var before = PrintManagerService.LayoutSnapshot.Capture(parameters);
            parameters.PaperSize = revitPapers[match.Paper.Name];
            parameters.PageOrientation = match.Rotate ? PageOrientationType.Landscape : PageOrientationType.Portrait;
            parameters.ZoomType = ZoomType.Zoom;
            parameters.Zoom = 100;
            parameters.PaperPlacement = PaperPlacementType.Center;
            if (settings is not null) PrintManagerService.ApplyRendering(parameters, settings);
            bool changed = before != PrintManagerService.LayoutSnapshot.Capture(parameters);
            if ((changed && !setup.Save()) || tx.Commit() != TransactionStatus.Committed)
                throw new InvalidOperationException("Revit не применил формат бумаги.");
        }
        setup.CurrentPrintSetting = saved;
        var effective = saved.PrintParameters;
        if (effective.PaperSize.Name != match.Paper.Name || effective.ZoomType != ZoomType.Zoom ||
            effective.Zoom != 100 || effective.PaperPlacement != PaperPlacementType.Center ||
            effective.PageOrientation != (match.Rotate ? PageOrientationType.Landscape : PageOrientationType.Portrait))
            throw new InvalidOperationException($"Revit не подтвердил настройки листа {sheetSize} ({effective.PaperSize.Name}, {effective.ZoomType}).");
        settingsByPaper.Add(key, saved);
        return match;
    }
}
