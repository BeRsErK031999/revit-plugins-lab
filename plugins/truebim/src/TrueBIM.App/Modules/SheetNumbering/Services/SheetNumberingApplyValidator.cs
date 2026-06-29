using TrueBIM.App.Modules.SheetNumbering.Models;

namespace TrueBIM.App.Modules.SheetNumbering.Services;

public static class SheetNumberingApplyValidator
{
    public static SheetNumberingApplyValidationResult Validate(
        int selectedSheetCount,
        bool isPreviewCurrent,
        int duplicateIssueCount,
        int changedPreviewRowCount)
    {
        if (selectedSheetCount == 0)
        {
            return new SheetNumberingApplyValidationResult(false, "Select at least one sheet.");
        }

        if (!isPreviewCurrent)
        {
            return new SheetNumberingApplyValidationResult(false, "Run Preview before Apply.");
        }

        if (duplicateIssueCount > 0)
        {
            return new SheetNumberingApplyValidationResult(false, "Resolve duplicate conflicts before Apply.");
        }

        if (changedPreviewRowCount == 0)
        {
            return new SheetNumberingApplyValidationResult(false, "No sheet numbers will change.");
        }

        return new SheetNumberingApplyValidationResult(true, "Ready to apply.");
    }
}
