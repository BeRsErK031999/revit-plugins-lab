using TrueBIM.App.Modules.SheetNumbering.Models;

namespace TrueBIM.App.Modules.SheetNumbering.Services;

public sealed class SheetNumberingPreviewWorkflow
{
    private readonly SheetNumberPreviewService previewService;
    private readonly IDuplicateSheetNumberDetector duplicateDetector;

    public SheetNumberingPreviewWorkflow(
        SheetNumberPreviewService previewService,
        IDuplicateSheetNumberDetector duplicateDetector)
    {
        this.previewService = previewService ?? throw new ArgumentNullException(nameof(previewService));
        this.duplicateDetector = duplicateDetector ?? throw new ArgumentNullException(nameof(duplicateDetector));
    }

    public SheetNumberingPreviewResult GeneratePreview(SheetNumberingPreviewRequest request)
    {
        Guard.NotNull(request, nameof(request));
        Guard.NotNull(request.SelectedSheets, nameof(request.SelectedSheets));
        Guard.NotNull(request.ExistingSheets, nameof(request.ExistingSheets));
        Guard.NotNull(request.Rules, nameof(request.Rules));

        IReadOnlyList<SheetNumberPreview> previews = previewService.GeneratePreviews(
            request.SelectedSheets,
            request.Rules);

        IReadOnlyList<DuplicateSheetNumberIssue> duplicateIssues = duplicateDetector.Detect(
            previews,
            request.ExistingSheets);

        return new SheetNumberingPreviewResult(previews, duplicateIssues);
    }
}
