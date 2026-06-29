using TrueBIM.App.Modules.SheetNumbering.Models;
using TrueBIM.App.Modules.SheetNumbering.Rules;
using TrueBIM.App.Modules.SheetNumbering.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.SheetNumbering.Services;

public sealed class SheetNumberingPreviewWorkflowTests
{
    [Fact]
    public void GeneratePreview_EmptySelectedSheetsReturnsEmptyPreviewsAndNoIssues()
    {
        SheetNumberingPreviewWorkflow workflow = CreateWorkflow();
        SheetNumberingPreviewRequest request = new([], [], Rules());

        SheetNumberingPreviewResult result = workflow.GeneratePreview(request);

        Assert.Empty(result.Previews);
        Assert.Empty(result.DuplicateIssues);
        Assert.False(result.HasBlockingIssues);
    }

    [Fact]
    public void GeneratePreview_GeneratesPreviewsInSelectedOrder()
    {
        SheetNumberingPreviewWorkflow workflow = CreateWorkflow();
        SheetInfo[] selectedSheets =
        [
            Sheet(20, "B-02"),
            Sheet(10, "B-01")
        ];
        SheetNumberingPreviewRequest request = new(selectedSheets, selectedSheets, Rules());

        SheetNumberingPreviewResult result = workflow.GeneratePreview(request);

        Assert.Equal([20, 10], result.Previews.Select(preview => preview.Sheet.ElementId));
        Assert.Equal(["A-01", "A-02"], result.Previews.Select(preview => preview.PreviewNumber));
    }

    [Fact]
    public void GeneratePreview_ReportsDuplicatePreviewIssue()
    {
        SheetNumberingPreviewWorkflow workflow = new(
            new SheetNumberPreviewService(),
            new DuplicatePreviewIssueDetector());
        SheetInfo[] selectedSheets =
        [
            Sheet(1, "A-01"),
            Sheet(2, "A-02")
        ];
        SheetNumberingPreviewRequest request = new(selectedSheets, selectedSheets, Rules());

        SheetNumberingPreviewResult result = workflow.GeneratePreview(request);

        DuplicateSheetNumberIssue issue = Assert.Single(result.DuplicateIssues);
        Assert.Equal(DuplicateSheetNumberIssueKind.Preview, issue.Kind);
        Assert.True(result.HasBlockingIssues);
    }

    [Fact]
    public void GeneratePreview_ReportsExistingDocumentConflict()
    {
        SheetNumberingPreviewWorkflow workflow = CreateWorkflow();
        SheetInfo[] selectedSheets =
        [
            Sheet(1, "A-01")
        ];
        SheetInfo[] existingSheets =
        [
            Sheet(1, "A-01"),
            Sheet(2, "A-03")
        ];
        SheetNumberingPreviewRequest request = new(selectedSheets, existingSheets, new NumberingRules("A-", string.Empty, 3, 1, 2));

        SheetNumberingPreviewResult result = workflow.GeneratePreview(request);

        DuplicateSheetNumberIssue issue = Assert.Single(result.DuplicateIssues);
        Assert.Equal(DuplicateSheetNumberIssueKind.ExistingDocument, issue.Kind);
        Assert.Equal([1, 2], issue.Sheets.Select(sheet => sheet.ElementId));
    }

    [Fact]
    public void HasBlockingIssuesReturnsFalseWhenNoIssuesExist()
    {
        SheetNumberingPreviewResult result = new([], []);

        Assert.False(result.HasBlockingIssues);
    }

    [Fact]
    public void HasBlockingIssuesReturnsTrueWhenDuplicateIssuesExist()
    {
        SheetNumberingPreviewResult result = new(
            [],
            [new DuplicateSheetNumberIssue("A-01", DuplicateSheetNumberIssueKind.Preview, [Sheet(1, "A-01"), Sheet(2, "A-02")])]);

        Assert.True(result.HasBlockingIssues);
    }

    [Fact]
    public void GeneratePreview_RejectsNullRequest()
    {
        SheetNumberingPreviewWorkflow workflow = CreateWorkflow();

        Assert.Throws<ArgumentNullException>(() => workflow.GeneratePreview(null!));
    }

    [Fact]
    public void Constructor_RejectsNullPreviewService()
    {
        Assert.Throws<ArgumentNullException>(() => new SheetNumberingPreviewWorkflow(null!, new DuplicateSheetNumberDetector()));
    }

    [Fact]
    public void Constructor_RejectsNullDuplicateDetector()
    {
        Assert.Throws<ArgumentNullException>(() => new SheetNumberingPreviewWorkflow(new SheetNumberPreviewService(), null!));
    }

    [Fact]
    public void GeneratePreview_RejectsNullRequestMembers()
    {
        SheetNumberingPreviewWorkflow workflow = CreateWorkflow();

        Assert.Throws<ArgumentNullException>(() => workflow.GeneratePreview(new SheetNumberingPreviewRequest(null!, [], Rules())));
        Assert.Throws<ArgumentNullException>(() => workflow.GeneratePreview(new SheetNumberingPreviewRequest([], null!, Rules())));
        Assert.Throws<ArgumentNullException>(() => workflow.GeneratePreview(new SheetNumberingPreviewRequest([], [], null!)));
    }

    private static SheetNumberingPreviewWorkflow CreateWorkflow()
    {
        return new SheetNumberingPreviewWorkflow(
            new SheetNumberPreviewService(),
            new DuplicateSheetNumberDetector());
    }

    private static NumberingRules Rules()
    {
        return new NumberingRules("A-", string.Empty, 1, 1, 2);
    }

    private static SheetInfo Sheet(long elementId, string currentNumber)
    {
        return new SheetInfo(elementId, currentNumber, "Sheet " + elementId, false);
    }

    private sealed class DuplicatePreviewIssueDetector : IDuplicateSheetNumberDetector
    {
        public IReadOnlyList<DuplicateSheetNumberIssue> Detect(
            IReadOnlyList<SheetNumberPreview> previews,
            IReadOnlyList<SheetInfo> existingSheets)
        {
            return
            [
                new DuplicateSheetNumberIssue(
                    previews[0].PreviewNumber,
                    DuplicateSheetNumberIssueKind.Preview,
                    previews.Select(preview => preview.Sheet).ToList())
            ];
        }
    }
}
