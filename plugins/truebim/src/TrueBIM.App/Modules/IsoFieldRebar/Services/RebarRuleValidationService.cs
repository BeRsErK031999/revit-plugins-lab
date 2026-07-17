using System.Globalization;
using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class RebarRuleValidationService
{
    private const string WallHostKind = "Wall";
    private const string SlabHostKind = "Slab";
    private const double AreaToleranceSquareCentimetersPerMeter = 0.02;
    private readonly IsoFieldReinforcementCombinationService combinationService = new();
    private readonly IsoFieldSlabRebarLayoutService layoutService = new();

    public RebarRulePreviewResult BuildPreview(
        IsoFieldRecognitionResult recognitionResult,
        IsoFieldHostElement? hostElement)
    {
        return BuildLegacyPreview(recognitionResult, hostElement);
    }

    public RebarRulePreviewResult BuildPreview(
        IsoFieldRecognitionResult recognitionResult,
        IsoFieldHostElement? hostElement,
        IsoFieldSourceSet? sourceSet,
        IsoFieldSlabBindingAnalysis? slabBinding,
        IsoFieldEngineeringSettings? engineeringSettings)
    {
        if (recognitionResult is null)
        {
            throw new ArgumentNullException(nameof(recognitionResult));
        }

        bool supportsEngineeringLayout = hostElement?.Geometry is not null
            && (hostElement.IsSlab
                || hostElement.GeometryProfile == IsoFieldHostGeometryProfile.StraightBasicWall);
        if (!supportsEngineeringLayout)
        {
            return BuildLegacyPreview(recognitionResult, hostElement);
        }

        return BuildPlanarEngineeringPreview(
            recognitionResult,
            hostElement!,
            sourceSet,
            slabBinding,
            engineeringSettings);
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

        if (!IsSupportedPlacementDirection(rule.PlacementDirection))
        {
            diagnostics.Add($"Направление армирования '{rule.PlacementDirection}' не поддерживается. Ожидается Auto, X, Y, AlongHost или Vertical.");
        }

        if (rule.IsEngineeringRule
            && rule.ProvidedAreaSquareCentimetersPerMeter
                + AreaToleranceSquareCentimetersPerMeter
                < rule.RequiredAreaSquareCentimetersPerMeter)
        {
            diagnostics.Add(
                $"Принятая площадь {rule.ProvidedAreaSquareCentimetersPerMeter:0.###} см²/м "
                + $"меньше требуемой {rule.RequiredAreaSquareCentimetersPerMeter:0.###} см²/м.");
        }

        return diagnostics;
    }

    private RebarRulePreviewResult BuildPlanarEngineeringPreview(
        IsoFieldRecognitionResult recognitionResult,
        IsoFieldHostElement hostElement,
        IsoFieldSourceSet? sourceSet,
        IsoFieldSlabBindingAnalysis? slabBinding,
        IsoFieldEngineeringSettings? settings)
    {
        List<string> diagnostics = new();
        if (recognitionResult.Polylines.Count == 0)
        {
            diagnostics.Add("Нет зон изополей для расчёта инженерных правил.");
        }

        if (sourceSet is null || !sourceSet.IsComplete)
        {
            diagnostics.Add("Для инженерной раскладки host нужен полный комплект из четырёх карт, а не одиночный JSON.");
        }
        else if (!sourceSet.HasConfirmedLayerMappings)
        {
            diagnostics.AddRange(sourceSet.LayerMappingValidationMessages);
        }

        if (slabBinding?.CanProceed != true)
        {
            diagnostics.Add("Перед расчётом раскладки выполните проверенную трёхточечную привязку и отсечение зон по контуру host.");
        }

        diagnostics.AddRange(layoutService.ValidateSettings(settings));
        if (diagnostics.Count > 0)
        {
            return new RebarRulePreviewResult(
                Array.Empty<RebarRulePreviewItem>(),
                diagnostics,
                settings);
        }

        Dictionary<string, IsoFieldClippedZone> clippedByZoneId = slabBinding!.ClippedZones
            .Where(zone => !zone.IsEmpty)
            .ToDictionary(zone => zone.SourceZoneId, StringComparer.Ordinal);
        IsoFieldEngineeringSettings activeSettings = settings!;
        List<RebarRulePreviewItem> items = new();
        foreach (IsoFieldPolyline polyline in recognitionResult.Polylines)
        {
            List<string> baseDiagnostics = new();
            List<string> ruleDiagnostics = new();
            if (polyline.LayerRole is null)
            {
                baseDiagnostics.Add("Для зоны не определён расчётный слой As1X/As2X/As3Y/As4Y.");
            }

            if (polyline.LegendBandIndex is null)
            {
                baseDiagnostics.Add("Для зоны не определён уровень цветовой шкалы.");
            }

            if (!clippedByZoneId.TryGetValue(polyline.Id, out IsoFieldClippedZone? clippedZone))
            {
                baseDiagnostics.Add("После отсечения по контуру host зона не содержит допустимой геометрии.");
            }

            IsoFieldLayerMapping? mapping = polyline.LayerRole.HasValue
                ? sourceSet!.GetLayerMapping(polyline.LayerRole.Value)
                : null;
            IsoFieldLegend? legend = polyline.LayerRole.HasValue
                ? recognitionResult.EffectiveLegends.FirstOrDefault(candidate =>
                    candidate.LayerRole == polyline.LayerRole)
                : null;
            IsoFieldLegendBand? band = legend is not null && polyline.LegendBandIndex.HasValue
                ? legend.Bands.FirstOrDefault(candidate => candidate.Index == polyline.LegendBandIndex.Value)
                : null;
            if (band?.MaximumValue is null)
            {
                baseDiagnostics.Add("Уровень зоны не содержит верхнюю числовую границу в см²/м.");
            }

            IsoFieldLegendBoundary? upperBoundary = band is not null && legend is not null
                ? legend.EffectiveBoundaries.FirstOrDefault(boundary => boundary.Index == band.Index + 1)
                : null;
            IsoFieldReinforcementCombination? combination = null;
            if (!combinationService.TryParse(
                upperBoundary?.ReinforcementLabel,
                out combination,
                out string combinationDiagnostic))
            {
                ruleDiagnostics.Add(combinationDiagnostic);
            }

            IReadOnlyList<IsoFieldRebarComponent> selectedComponents = combination is null
                ? Array.Empty<IsoFieldRebarComponent>()
                : activeSettings.Mode == IsoFieldReinforcementMode.AdditionalOverBase
                    ? combination.Components.Where(component => !component.IsBase).ToArray()
                    : combination.Components;
            if (combination is not null && selectedComponents.Count == 0)
            {
                ruleDiagnostics.Add("Для режима дополнительного усиления сочетание не содержит арматуру сверх базовой сетки.");
            }

            double? requiredArea = band?.MaximumValue;
            double? providedArea = combination?.AreaSquareCentimetersPerMeter;
            IsoFieldRebarComponent? firstComponent = selectedComponents.FirstOrDefault();
            RebarRule rule = new(
                $"Правило {ResolveZoneName(polyline)}",
                hostElement.HostKind,
                firstComponent?.BarTypeName ?? string.Empty,
                firstComponent?.SpacingMillimeters ?? 0,
                BuildEngineeringNote(activeSettings, requiredArea, providedArea, combination?.SourceLabel),
                mapping?.Direction.ToString() ?? "Auto",
                requiredArea,
                providedArea,
                combination?.SourceLabel,
                polyline.LayerRole,
                mapping?.Face,
                selectedComponents,
                activeSettings.Mode);
            ruleDiagnostics.AddRange(ValidateRule(rule));
            string[] effectiveBaseDiagnostics = baseDiagnostics
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            string[] itemDiagnostics = effectiveBaseDiagnostics
                .Concat(ruleDiagnostics)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            items.Add(new RebarRulePreviewItem(
                polyline.Id,
                ResolveZoneName(polyline),
                rule,
                itemDiagnostics,
                clippedZone?.Regions,
                BaseDiagnostics: effectiveBaseDiagnostics));
        }

        if (items.Count == 0)
        {
            diagnostics.Add("После инженерной проверки не осталось зон для раскладки.");
            return new RebarRulePreviewResult(items, diagnostics, settings);
        }

        IReadOnlyList<IsoFieldSlabRebarSegment> segments;
        string[] basePreviewDiagnostics = diagnostics.ToArray();
        try
        {
            segments = layoutService.BuildSegments(items, activeSettings);
        }
        catch (InvalidOperationException exception)
        {
            diagnostics.Add(exception.Message);
            return new RebarRulePreviewResult(
                items,
                diagnostics,
                settings,
                BaseDiagnostics: basePreviewDiagnostics);
        }

        Dictionary<string, int> countByZone = segments
            .GroupBy(segment => segment.ZoneId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        items = items
            .Select(item => item with
            {
                EstimatedBarCount = countByZone.TryGetValue(item.ZoneId, out int count)
                    ? count
                    : 0
            })
            .ToList();
        foreach (RebarRulePreviewItem emptyItem in items.Where(item =>
            item.IsValid && item.EstimatedBarCount == 0))
        {
            int index = items.IndexOf(emptyItem);
            items[index] = emptyItem with
            {
                Diagnostics = [.. emptyItem.Diagnostics, "После отступов и проверки минимальной длины в зоне не осталось стержней."]
            };
        }

        return new RebarRulePreviewResult(
            items,
            diagnostics,
            settings,
            segments.Count,
            basePreviewDiagnostics);
    }

    private RebarRulePreviewResult BuildLegacyPreview(
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
            RebarRule rule = CreateLegacyRule(polyline, hostElement!);
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

    private static RebarRule CreateLegacyRule(
        IsoFieldPolyline polyline,
        IsoFieldHostElement hostElement)
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
            $"Preview rule. Confidence={FormatConfidence(polyline.Confidence)}; Host={hostElement.DisplayName}.",
            ResolvePlacementDirection(hostElement.HostKind));
    }

    private static string BuildEngineeringNote(
        IsoFieldEngineeringSettings settings,
        double? requiredArea,
        double? providedArea,
        string? label)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("ru-RU");
        string mode = settings.Mode == IsoFieldReinforcementMode.AdditionalOverBase
            ? "дополнительное усиление поверх базовой сетки"
            : "полное сочетание в зоне";
        return $"{mode}; требуется {requiredArea?.ToString("0.###", culture) ?? "?"} см²/м; "
            + $"принято {providedArea?.ToString("0.###", culture) ?? "?"} см²/м; {label ?? "без подписи"}.";
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
            ? confidence.Value.ToString("0.###", CultureInfo.InvariantCulture)
            : "n/a";
    }

    private static bool IsSupportedHostKind(string hostKind)
    {
        return string.Equals(hostKind, WallHostKind, StringComparison.Ordinal)
            || string.Equals(hostKind, SlabHostKind, StringComparison.Ordinal);
    }

    private static string ResolvePlacementDirection(string hostKind)
    {
        return string.Equals(hostKind, SlabHostKind, StringComparison.Ordinal)
            ? "Auto"
            : "AlongHost";
    }

    private static bool IsSupportedPlacementDirection(string placementDirection)
    {
        return string.Equals(placementDirection, "Auto", StringComparison.OrdinalIgnoreCase)
            || string.Equals(placementDirection, "X", StringComparison.OrdinalIgnoreCase)
            || string.Equals(placementDirection, "Y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(placementDirection, "AlongHost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(placementDirection, "Vertical", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
