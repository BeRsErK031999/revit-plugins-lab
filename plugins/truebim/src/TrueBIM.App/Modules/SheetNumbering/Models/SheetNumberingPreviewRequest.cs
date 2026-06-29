using TrueBIM.App.Modules.SheetNumbering.Rules;

namespace TrueBIM.App.Modules.SheetNumbering.Models;

public sealed record SheetNumberingPreviewRequest(
    IReadOnlyList<SheetInfo> SelectedSheets,
    IReadOnlyList<SheetInfo> ExistingSheets,
    NumberingRules Rules);
