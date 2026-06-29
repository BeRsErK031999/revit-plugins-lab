using TrueBIM.App.Modules.SheetNumbering.Models;

namespace TrueBIM.App.Modules.SheetNumbering.Services;

public static class SheetNumberingPreviewSelection
{
    public static IReadOnlyList<SheetInfo> FilterSheetsForPreview(
        IReadOnlyList<SheetInfo> selectedSheets,
        bool includePlaceholders)
    {
        ArgumentNullException.ThrowIfNull(selectedSheets);

        return selectedSheets
            .Where(sheet => includePlaceholders || !sheet.IsPlaceholder)
            .ToList();
    }
}
