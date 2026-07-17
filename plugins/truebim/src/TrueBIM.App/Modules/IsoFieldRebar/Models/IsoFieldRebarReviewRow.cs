using System.Globalization;

namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public enum IsoFieldRebarReviewStatus
{
    NotCompared,
    Add,
    Update,
    Delete,
    Unchanged,
    Mixed,
    Invalid,
    Excluded
}

public sealed record IsoFieldRebarReviewRow(
    string ZoneId,
    string ZoneName,
    IsoFieldLayerRole? LayerRole,
    IsoFieldRebarReviewStatus Status,
    string FaceDirectionText,
    string ReinforcementText,
    string AreaText,
    int EstimatedBarCount,
    double? Confidence,
    IReadOnlyList<double> DiametersMillimeters,
    IReadOnlyList<double> SpacingsMillimeters,
    int AddCount,
    int UpdateCount,
    int DeleteCount,
    int UnchangedCount,
    IReadOnlyList<string> Diagnostics,
    bool IsIncluded = true,
    bool IsManuallyOverridden = false,
    IReadOnlyList<string>? SourceZoneIds = null)
{
    public IReadOnlyList<string> EffectiveSourceZoneIds => SourceZoneIds ?? [ZoneId];

    public bool IsMerged => EffectiveSourceZoneIds.Count > 1;

    public string LayerText => LayerRole?.ToString() ?? "—";

    public string StatusText => Status switch
    {
        IsoFieldRebarReviewStatus.NotCompared => "Не сравнено",
        IsoFieldRebarReviewStatus.Add => "Добавить",
        IsoFieldRebarReviewStatus.Update => "Обновить",
        IsoFieldRebarReviewStatus.Delete => "Удалить",
        IsoFieldRebarReviewStatus.Unchanged => "Без изменений",
        IsoFieldRebarReviewStatus.Mixed => "Смешано",
        IsoFieldRebarReviewStatus.Invalid => "Ошибка",
        IsoFieldRebarReviewStatus.Excluded => "Исключена",
        _ => Status.ToString()
    };

    public string ConfidenceText => Confidence.HasValue
        ? (Confidence.Value * 100).ToString("0", CultureInfo.GetCultureInfo("ru-RU")) + "%"
        : "—";

    public string EstimatedBarCountText => EstimatedBarCount > 0
        ? EstimatedBarCount.ToString(CultureInfo.GetCultureInfo("ru-RU"))
        : "—";

    public string SettingText => !IsIncluded
        ? "Исключена вручную"
        : IsMerged && IsManuallyOverridden
            ? $"{FormatZoneCount(EffectiveSourceZoneIds.Count)} · правило изменено"
        : IsMerged
            ? $"Объединены {FormatZoneCount(EffectiveSourceZoneIds.Count)}"
        : IsManuallyOverridden
            ? "Правило изменено"
            : "Расчётное правило";

    public string ChangeSummary => Status switch
    {
        IsoFieldRebarReviewStatus.NotCompared => "Сначала сравните с моделью",
        IsoFieldRebarReviewStatus.Invalid => Diagnostics.Count > 0
            ? string.Join(" ", Diagnostics)
            : "Правило содержит ошибку",
        IsoFieldRebarReviewStatus.Excluded => "Не войдёт в раскладку",
        _ => $"+{AddCount} · ~{UpdateCount} · −{DeleteCount} · ={UnchangedCount}"
    };

    private static string FormatZoneCount(int count)
    {
        int modulo100 = count % 100;
        int modulo10 = count % 10;
        string suffix = modulo100 is >= 11 and <= 14
            ? "зон"
            : modulo10 switch
            {
                1 => "зона",
                2 or 3 or 4 => "зоны",
                _ => "зон"
            };
        return $"{count} {suffix}";
    }
}

public sealed record IsoFieldRebarReviewFilter(
    string SearchText = "",
    IsoFieldLayerRole? LayerRole = null,
    IsoFieldRebarReviewStatus? Status = null,
    double? DiameterMillimeters = null,
    double? SpacingMillimeters = null,
    double? MinimumConfidence = null);
