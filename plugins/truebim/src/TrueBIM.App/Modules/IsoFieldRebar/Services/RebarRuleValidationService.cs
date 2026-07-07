using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class RebarRuleValidationService
{
    private const string WallHostKind = "Wall";
    private const string SlabHostKind = "Slab";

    public RebarRulePreviewResult BuildPreview(
        IsoFieldRecognitionResult recognitionResult,
        IsoFieldHostElement? hostElement)
    {
        if (recognitionResult is null)
        {
            throw new ArgumentNullException(nameof(recognitionResult));
        }

        List<string> diagnostics = new();
        if (hostElement is null)
        {
            diagnostics.Add("Выберите стену или плиту перед расчетом правил армирования.");
        }

        if (recognitionResult.Polylines.Count == 0)
        {
            diagnostics.Add("Нет зон изополей для расчета правил армирования.");
        }

        if (diagnostics.Count > 0)
        {
            return new RebarRulePreviewResult(Array.Empty<RebarRulePreviewItem>(), diagnostics);
        }

        List<RebarRulePreviewItem> items = new();
        foreach (IsoFieldPolyline polyline in recognitionResult.Polylines)
        {
            RebarRule rule = CreateRule(polyline, hostElement!);
            List<string> itemDiagnostics = ValidateRule(rule).ToList();
            if (polyline.Points.Count < 2)
            {
                itemDiagnostics.Add($"Зона '{polyline.Id}' содержит меньше двух точек.");
            }

            items.Add(new RebarRulePreviewItem(
                polyline.Id,
                ResolveZoneName(polyline),
                rule,
                itemDiagnostics));
        }

        return new RebarRulePreviewResult(items, Array.Empty<string>());
    }

    public IReadOnlyList<string> ValidateRule(RebarRule rule)
    {
        if (rule is null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        List<string> diagnostics = new();
        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            diagnostics.Add("Название правила армирования не задано.");
        }

        if (!IsSupportedHostKind(rule.HostKind))
        {
            diagnostics.Add($"HostKind '{rule.HostKind}' не поддерживается. Ожидается Wall или Slab.");
        }

        if (string.IsNullOrWhiteSpace(rule.BarTypeName))
        {
            diagnostics.Add("Тип арматуры не задан.");
        }

        if (!IsFinite(rule.SpacingMillimeters) || rule.SpacingMillimeters < 50 || rule.SpacingMillimeters > 400)
        {
            diagnostics.Add("Шаг армирования должен быть в диапазоне 50-400 мм.");
        }

        return diagnostics;
    }

    private static RebarRule CreateRule(IsoFieldPolyline polyline, IsoFieldHostElement hostElement)
    {
        string zoneName = ResolveZoneName(polyline);
        double spacing = ResolveSpacingMillimeters(polyline.Confidence);
        string barTypeName = string.Equals(hostElement.HostKind, WallHostKind, StringComparison.Ordinal)
            ? "Ø12 A500"
            : "Ø10 A500";

        return new RebarRule(
            $"Правило {zoneName}",
            hostElement.HostKind,
            barTypeName,
            spacing,
            $"Preview rule. Confidence={FormatConfidence(polyline.Confidence)}; Host={hostElement.DisplayName}.");
    }

    private static string ResolveZoneName(IsoFieldPolyline polyline)
    {
        return string.IsNullOrWhiteSpace(polyline.ZoneName)
            ? polyline.Id
            : polyline.ZoneName!;
    }

    private static double ResolveSpacingMillimeters(double? confidence)
    {
        double value = confidence.GetValueOrDefault(0.75);
        if (!IsFinite(value))
        {
            value = 0.75;
        }

        if (value >= 0.85)
        {
            return 100;
        }

        return value >= 0.65 ? 150 : 200;
    }

    private static string FormatConfidence(double? confidence)
    {
        return confidence.HasValue && IsFinite(confidence.Value)
            ? confidence.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
            : "n/a";
    }

    private static bool IsSupportedHostKind(string hostKind)
    {
        return string.Equals(hostKind, WallHostKind, StringComparison.Ordinal)
            || string.Equals(hostKind, SlabHostKind, StringComparison.Ordinal);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
