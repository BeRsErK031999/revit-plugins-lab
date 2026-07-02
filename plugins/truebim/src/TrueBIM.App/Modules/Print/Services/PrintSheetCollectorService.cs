using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintSheetCollectorService
{
    private const double MillimetersPerFoot = 304.8;

    public IReadOnlyList<PrintSheetInfo> Collect(Document document)
    {
        Guard.NotNull(document, nameof(document));

        string sourceName = string.IsNullOrWhiteSpace(document.Title)
            ? "Активный документ"
            : document.Title;

        return new FilteredElementCollector(document)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Select(sheet => new PrintSheetInfo(
                RevitElementIds.GetValue(sheet.Id),
                sourceName,
                sheet.SheetNumber,
                sheet.Name,
                ResolveSheetFormat(sheet),
                sheet.IsPlaceholder,
                !sheet.IsPlaceholder && sheet.CanBePrinted))
            .OrderBy(sheet => sheet.SheetNumber, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(sheet => sheet.SheetName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string ResolveSheetFormat(ViewSheet sheet)
    {
        if (sheet.IsPlaceholder)
        {
            return "Заглушка";
        }

        try
        {
            BoundingBoxUV outline = sheet.Outline;
            double width = Math.Abs(outline.Max.U - outline.Min.U) * MillimetersPerFoot;
            double height = Math.Abs(outline.Max.V - outline.Min.V) * MillimetersPerFoot;
            if (width <= 0 || height <= 0)
            {
                return "Не определен";
            }

            return $"{Math.Round(width)} x {Math.Round(height)} мм";
        }
        catch (Exception)
        {
            return "Не определен";
        }
    }
}
