namespace TrueBIM.App.Modules.BimTools.TitleBlockFill.Models;

public sealed record TitleBlockPreviewRow(
    int RuleIndex,
    long SheetElementId,
    string SheetNumber,
    string SheetName,
    string Target,
    string ParameterName,
    string CurrentValue,
    string NewValue,
    string Status,
    string Message,
    bool CanApply);
