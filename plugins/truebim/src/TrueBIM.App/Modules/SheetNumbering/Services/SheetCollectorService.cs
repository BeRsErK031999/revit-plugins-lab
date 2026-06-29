using Autodesk.Revit.DB;
using TrueBIM.App.Modules.SheetNumbering.Models;

namespace TrueBIM.App.Modules.SheetNumbering.Services;

public sealed class SheetCollectorService
{
    public IReadOnlyList<SheetInfo> Collect(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new FilteredElementCollector(document)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Select(sheet => new SheetInfo(
                sheet.Id.Value,
                sheet.SheetNumber,
                sheet.Name,
                sheet.IsPlaceholder))
            .OrderBy(sheet => sheet.CurrentNumber, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(sheet => sheet.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
