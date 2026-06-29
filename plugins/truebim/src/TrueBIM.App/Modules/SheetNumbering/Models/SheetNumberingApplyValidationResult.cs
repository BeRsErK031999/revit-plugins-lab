namespace TrueBIM.App.Modules.SheetNumbering.Models;

public sealed record SheetNumberingApplyValidationResult(
    bool CanApply,
    string Reason);
