using TrueBIM.App.Modules.SheetNumbering.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.SheetNumbering.Services;

public sealed class SheetNumberingApplyValidatorTests
{
    [Fact]
    public void Validate_RequiresSelectedSheets()
    {
        var result = SheetNumberingApplyValidator.Validate(0, isPreviewCurrent: true, duplicateIssueCount: 0, changedPreviewRowCount: 1);

        Assert.False(result.CanApply);
        Assert.Equal("Select at least one sheet.", result.Reason);
    }

    [Fact]
    public void Validate_RequiresCurrentPreview()
    {
        var result = SheetNumberingApplyValidator.Validate(1, isPreviewCurrent: false, duplicateIssueCount: 0, changedPreviewRowCount: 1);

        Assert.False(result.CanApply);
        Assert.Equal("Run Preview before Apply.", result.Reason);
    }

    [Fact]
    public void Validate_BlocksDuplicateIssues()
    {
        var result = SheetNumberingApplyValidator.Validate(1, isPreviewCurrent: true, duplicateIssueCount: 1, changedPreviewRowCount: 1);

        Assert.False(result.CanApply);
        Assert.Equal("Resolve duplicate conflicts before Apply.", result.Reason);
    }

    [Fact]
    public void Validate_RequiresChangedRows()
    {
        var result = SheetNumberingApplyValidator.Validate(1, isPreviewCurrent: true, duplicateIssueCount: 0, changedPreviewRowCount: 0);

        Assert.False(result.CanApply);
        Assert.Equal("No sheet numbers will change.", result.Reason);
    }

    [Fact]
    public void Validate_AllowsReadyState()
    {
        var result = SheetNumberingApplyValidator.Validate(1, isPreviewCurrent: true, duplicateIssueCount: 0, changedPreviewRowCount: 1);

        Assert.True(result.CanApply);
        Assert.Equal("Ready to apply.", result.Reason);
    }
}
