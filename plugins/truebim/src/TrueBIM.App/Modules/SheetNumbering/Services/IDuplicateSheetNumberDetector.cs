using TrueBIM.App.Modules.SheetNumbering.Models;

namespace TrueBIM.App.Modules.SheetNumbering.Services;

public interface IDuplicateSheetNumberDetector
{
    IReadOnlyList<DuplicateSheetNumberIssue> Detect(
        IReadOnlyList<SheetNumberPreview> previews,
        IReadOnlyList<SheetInfo> existingSheets);
}
