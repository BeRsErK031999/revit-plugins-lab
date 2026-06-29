using TrueBIM.App.Modules.SheetNumbering.Models;
using TrueBIM.App.Modules.SheetNumbering.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.SheetNumbering.Services;

public sealed class SheetNumberingPreviewSelectionTests
{
    [Fact]
    public void FilterSheetsForPreview_ExcludesPlaceholdersWhenDisabled()
    {
        SheetInfo[] sheets =
        [
            new(1, "A-01", "Sheet 1", false),
            new(2, "P-01", "Placeholder", true)
        ];

        IReadOnlyList<SheetInfo> result = SheetNumberingPreviewSelection.FilterSheetsForPreview(sheets, includePlaceholders: false);

        SheetInfo sheet = Assert.Single(result);
        Assert.Equal(1, sheet.ElementId);
    }

    [Fact]
    public void FilterSheetsForPreview_IncludesPlaceholdersWhenEnabled()
    {
        SheetInfo[] sheets =
        [
            new(1, "A-01", "Sheet 1", false),
            new(2, "P-01", "Placeholder", true)
        ];

        IReadOnlyList<SheetInfo> result = SheetNumberingPreviewSelection.FilterSheetsForPreview(sheets, includePlaceholders: true);

        Assert.Equal([1, 2], result.Select(sheet => sheet.ElementId));
    }
}
