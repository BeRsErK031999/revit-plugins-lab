using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintSheetCollectorService
{
    private const double MillimetersPerFoot = 304.8;

    public IReadOnlyList<PrintSheetInfo> Collect(Document document)
    {
        return Collect(document, CreateDefaultSourceId(document), ResolveSourceName(document));
    }

    public IReadOnlyList<PrintSheetInfo> Collect(
        Document document,
        string sourceId,
        string sourceName)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNullOrWhiteSpace(sourceId, nameof(sourceId));
        Guard.NotNullOrWhiteSpace(sourceName, nameof(sourceName));

        return new FilteredElementCollector(document)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Select(sheet => new PrintSheetInfo(
                RevitElementIds.GetValue(sheet.Id),
                sourceId,
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

    private static string CreateDefaultSourceId(Document document)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(document.PathName))
            {
                return document.PathName;
            }
        }
        catch (Exception)
        {
        }

        return ResolveSourceName(document);
    }

    private static string ResolveSourceName(Document document)
    {
        return string.IsNullOrWhiteSpace(document.Title)
            ? "Активный документ"
            : document.Title;
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
