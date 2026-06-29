using TrueBIM.App.Modules.SheetNumbering.Models;
using TrueBIM.App.Modules.SheetNumbering.Rules;

namespace TrueBIM.App.Modules.SheetNumbering.Services;

public sealed class SheetNumberPreviewService
{
    public IReadOnlyList<SheetNumberPreview> GeneratePreviews(
        IReadOnlyList<SheetInfo> sheets,
        NumberingRules rules)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        ArgumentNullException.ThrowIfNull(rules);

        return sheets
            .Select((sheet, index) => new SheetNumberPreview(
                sheet,
                rules.FormatNumber(index)))
            .ToList();
    }
}
