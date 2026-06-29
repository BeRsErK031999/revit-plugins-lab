using TrueBIM.App.Modules.SheetNumbering.Models;
using TrueBIM.App.Modules.SheetNumbering.Rules;
using TrueBIM.App.Modules.SheetNumbering.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.SheetNumbering.Services;

public sealed class SheetNumberPreviewServiceTests
{
    [Fact]
    public void GeneratePreviews_UsesInputOrder()
    {
        SheetNumberPreviewService service = new();
        SheetInfo[] sheets =
        [
            new(20, "B-02", "Second", false),
            new(10, "B-01", "First", false)
        ];
        NumberingRules rules = new("A-", string.Empty, 1, 1, 2);

        IReadOnlyList<SheetNumberPreview> previews = service.GeneratePreviews(sheets, rules);

        Assert.Equal(20, previews[0].Sheet.ElementId);
        Assert.Equal("A-01", previews[0].PreviewNumber);
        Assert.Equal(10, previews[1].Sheet.ElementId);
        Assert.Equal("A-02", previews[1].PreviewNumber);
    }

    [Fact]
    public void GeneratePreviews_PreservesSheetMetadata()
    {
        SheetNumberPreviewService service = new();
        SheetInfo[] sheets =
        [
            new(42, "S-001", "Placeholder Sheet", true)
        ];
        NumberingRules rules = new("S-", string.Empty, 10, 1, 3);

        SheetNumberPreview preview = service.GeneratePreviews(sheets, rules).Single();

        Assert.Equal(42, preview.Sheet.ElementId);
        Assert.Equal("S-001", preview.Sheet.CurrentNumber);
        Assert.Equal("Placeholder Sheet", preview.Sheet.Name);
        Assert.True(preview.Sheet.IsPlaceholder);
        Assert.Equal("S-010", preview.PreviewNumber);
    }

    [Fact]
    public void IsChanged_ReturnsFalseWhenPreviewMatchesCurrentNumber()
    {
        SheetInfo sheet = new(1, "A-01", "Sheet", false);
        SheetNumberPreview preview = new(sheet, "A-01");

        Assert.False(preview.IsChanged);
    }

    [Fact]
    public void IsChanged_ReturnsTrueWhenPreviewDiffersFromCurrentNumber()
    {
        SheetInfo sheet = new(1, "A-01", "Sheet", false);
        SheetNumberPreview preview = new(sheet, "A-02");

        Assert.True(preview.IsChanged);
    }

    [Fact]
    public void GeneratePreviews_RejectsNullSheets()
    {
        SheetNumberPreviewService service = new();
        NumberingRules rules = new("A-", string.Empty, 1, 1, 2);

        Assert.Throws<ArgumentNullException>(() => service.GeneratePreviews(null!, rules));
    }

    [Fact]
    public void GeneratePreviews_RejectsNullRules()
    {
        SheetNumberPreviewService service = new();
        SheetInfo[] sheets =
        [
            new(1, "A-01", "Sheet", false)
        ];

        Assert.Throws<ArgumentNullException>(() => service.GeneratePreviews(sheets, null!));
    }
}
