using TrueBIM.App.Modules.SheetNumbering.Models;
using TrueBIM.App.Modules.SheetNumbering.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.SheetNumbering.Services;

public sealed class DuplicateSheetNumberDetectorTests
{
    [Fact]
    public void Detect_ReturnsNoIssuesWhenPreviewNumbersAreUnique()
    {
        DuplicateSheetNumberDetector detector = new();
        SheetNumberPreview[] previews =
        [
            Preview(1, "A-01", "A-11"),
            Preview(2, "A-02", "A-12")
        ];

        IReadOnlyList<DuplicateSheetNumberIssue> issues = detector.Detect(previews, []);

        Assert.Empty(issues);
    }

    [Fact]
    public void Detect_FindsDuplicatePreviewNumbers()
    {
        DuplicateSheetNumberDetector detector = new();
        SheetNumberPreview[] previews =
        [
            Preview(1, "A-01", "A-10"),
            Preview(2, "A-02", "A-10")
        ];

        DuplicateSheetNumberIssue issue = detector.Detect(previews, []).Single();

        Assert.Equal(DuplicateSheetNumberIssueKind.Preview, issue.Kind);
        Assert.Equal("A-10", issue.SheetNumber);
        Assert.Equal([1, 2], issue.Sheets.Select(sheet => sheet.ElementId));
    }

    [Fact]
    public void Detect_FindsExistingDocumentConflict()
    {
        DuplicateSheetNumberDetector detector = new();
        SheetNumberPreview[] previews =
        [
            Preview(1, "A-01", "A-03")
        ];
        SheetInfo[] existingSheets =
        [
            Sheet(1, "A-01"),
            Sheet(3, "A-03")
        ];

        DuplicateSheetNumberIssue issue = detector.Detect(previews, existingSheets).Single();

        Assert.Equal(DuplicateSheetNumberIssueKind.ExistingDocument, issue.Kind);
        Assert.Equal("A-03", issue.SheetNumber);
        Assert.Equal([1, 3], issue.Sheets.Select(sheet => sheet.ElementId));
    }

    [Fact]
    public void Detect_IgnoresExistingDocumentMatchForSameSheet()
    {
        DuplicateSheetNumberDetector detector = new();
        SheetNumberPreview[] previews =
        [
            Preview(1, "A-01", "A-01")
        ];
        SheetInfo[] existingSheets =
        [
            Sheet(1, "A-01")
        ];

        IReadOnlyList<DuplicateSheetNumberIssue> issues = detector.Detect(previews, existingSheets);

        Assert.Empty(issues);
    }

    [Fact]
    public void Detect_UsesCaseInsensitiveSheetNumberComparison()
    {
        DuplicateSheetNumberDetector detector = new();
        SheetNumberPreview[] previews =
        [
            Preview(1, "a-01", "a-10"),
            Preview(2, "A-02", "A-10")
        ];

        DuplicateSheetNumberIssue issue = detector.Detect(previews, []).Single();

        Assert.Equal(DuplicateSheetNumberIssueKind.Preview, issue.Kind);
        Assert.Equal([1, 2], issue.Sheets.Select(sheet => sheet.ElementId));
    }

    [Fact]
    public void Detect_ReturnsPreviewAndExistingDocumentIssuesTogether()
    {
        DuplicateSheetNumberDetector detector = new();
        SheetNumberPreview[] previews =
        [
            Preview(1, "A-01", "A-10"),
            Preview(2, "A-02", "A-10")
        ];
        SheetInfo[] existingSheets =
        [
            Sheet(3, "A-10")
        ];

        IReadOnlyList<DuplicateSheetNumberIssue> issues = detector.Detect(previews, existingSheets);

        DuplicateSheetNumberIssue previewIssue = Assert.Single(
            issues,
            issue => issue.Kind == DuplicateSheetNumberIssueKind.Preview);
        DuplicateSheetNumberIssue existingIssue = Assert.Single(
            issues,
            issue => issue.Kind == DuplicateSheetNumberIssueKind.ExistingDocument);

        Assert.Equal([1, 2], previewIssue.Sheets.Select(sheet => sheet.ElementId));
        Assert.Equal([1, 2, 3], existingIssue.Sheets.Select(sheet => sheet.ElementId));
    }

    [Fact]
    public void Detect_RejectsNullPreviews()
    {
        DuplicateSheetNumberDetector detector = new();

        Assert.Throws<ArgumentNullException>(() => detector.Detect(null!, []));
    }

    [Fact]
    public void Detect_RejectsNullExistingSheets()
    {
        DuplicateSheetNumberDetector detector = new();
        SheetNumberPreview[] previews =
        [
            Preview(1, "A-01", "A-01")
        ];

        Assert.Throws<ArgumentNullException>(() => detector.Detect(previews, null!));
    }

    private static SheetNumberPreview Preview(long elementId, string currentNumber, string previewNumber)
    {
        return new SheetNumberPreview(Sheet(elementId, currentNumber), previewNumber);
    }

    private static SheetInfo Sheet(long elementId, string currentNumber)
    {
        return new SheetInfo(elementId, currentNumber, "Sheet " + elementId, false);
    }
}
