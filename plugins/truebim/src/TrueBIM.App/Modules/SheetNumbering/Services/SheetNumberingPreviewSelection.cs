using TrueBIM.App.Modules.SheetNumbering.Models;

namespace TrueBIM.App.Modules.SheetNumbering.Services;

public static class SheetNumberingPreviewSelection
{
    public static IReadOnlyList<SheetInfo> FilterSheetsForPreview(
        IReadOnlyList<SheetInfo> selectedSheets,
        bool includePlaceholders)
    {
        Guard.NotNull(selectedSheets, nameof(selectedSheets));

        return selectedSheets
            .Where(sheet => includePlaceholders || !sheet.IsPlaceholder)
            .ToList();
    }
}
