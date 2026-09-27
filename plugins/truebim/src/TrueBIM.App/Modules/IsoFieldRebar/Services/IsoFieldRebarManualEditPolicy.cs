using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public static class IsoFieldRebarManualEditPolicy
{
    public const string PlannedPatchMessage =
        "Массивы по общим пятнам нельзя менять или исключать после проверки анкеровки. "
        + "Измените исходные зоны в окне «Проверка и исправление зон» и рассчитайте раскладку заново.";

    public static bool IsPlannedPatch(RebarRulePreviewItem item)
    {
        return item.IsArrayEnvelope || item.ZoneId.StartsWith("zone-patch-", StringComparison.Ordinal);
    }
}
