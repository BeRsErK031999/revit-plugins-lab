using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldRebarRuleOverrideService
{
    private readonly IsoFieldReinforcementCombinationService combinationService = new();
    private readonly RebarRuleValidationService ruleValidationService = new();
    private readonly IsoFieldSlabRebarLayoutService layoutService = new();

    public IsoFieldRebarRuleOverrideValidation Validate(
        RebarRulePreviewItem item,
        IsoFieldEngineeringSettings settings,
        bool isIncluded,
        string reinforcementLabel)
    {
        if (item is null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (!isIncluded)
        {
            return new IsoFieldRebarRuleOverrideValidation(item.Rule, Array.Empty<string>());
        }

        List<string> diagnostics = item.EffectiveBaseDiagnostics.ToList();
        if (!combinationService.TryParse(
            reinforcementLabel,
            out IsoFieldReinforcementCombination? combination,
            out string combinationDiagnostic))
        {
            diagnostics.Add(combinationDiagnostic);
            return new IsoFieldRebarRuleOverrideValidation(
                null,
                diagnostics.Distinct(StringComparer.Ordinal).ToArray());
        }

        IsoFieldReinforcementCombination parsedCombination = combination!;
        IReadOnlyList<IsoFieldRebarComponent> selectedComponents = settings.Mode
            == IsoFieldReinforcementMode.AdditionalOverBase
            ? parsedCombination.Components.Where(component => !component.IsBase).ToArray()
            : parsedCombination.Components;
        if (selectedComponents.Count == 0)
        {
            diagnostics.Add("Для режима дополнительного усиления сочетание должно содержать компонент сверх базовой сетки.");
        }

        IsoFieldRebarComponent? firstComponent = selectedComponents.FirstOrDefault();
        RebarRule rule = item.Rule with
        {
            BarTypeName = firstComponent?.BarTypeName ?? string.Empty,
            SpacingMillimeters = firstComponent?.SpacingMillimeters ?? 0,
            Note = "Инженерное правило переопределено пользователем до сравнения с моделью.",
            ProvidedAreaSquareCentimetersPerMeter = parsedCombination.AreaSquareCentimetersPerMeter,
            ReinforcementLabel = parsedCombination.SourceLabel,
            Components = selectedComponents,
            ReinforcementMode = settings.Mode
        };
        diagnostics.AddRange(ruleValidationService.ValidateRule(rule));
        return new IsoFieldRebarRuleOverrideValidation(
            rule,
            diagnostics.Distinct(StringComparer.Ordinal).ToArray());
    }

    public RebarRulePreviewResult Apply(
        RebarRulePreviewResult calculatedPreview,
        IReadOnlyDictionary<string, IsoFieldRebarRuleOverride> overrides)
    {
        if (calculatedPreview is null)
        {
            throw new ArgumentNullException(nameof(calculatedPreview));
        }

        if (overrides is null)
        {
            throw new ArgumentNullException(nameof(overrides));
        }

        if (calculatedPreview.EngineeringSettings is null)
        {
            throw new InvalidOperationException("Ручные настройки зон доступны только для инженерной раскладки host.");
        }

        IsoFieldEngineeringSettings settings = calculatedPreview.EngineeringSettings;
        List<RebarRulePreviewItem> items = new(calculatedPreview.Items.Count);
        foreach (RebarRulePreviewItem sourceItem in calculatedPreview.Items)
        {
            if (!overrides.TryGetValue(sourceItem.ZoneId, out IsoFieldRebarRuleOverride? zoneOverride))
            {
                items.Add(sourceItem with
                {
                    IsIncluded = true,
                    IsManuallyOverridden = false
                });
                continue;
            }

            IsoFieldRebarRuleOverrideValidation validation = Validate(
                sourceItem,
                settings,
                zoneOverride.IsIncluded,
                zoneOverride.ReinforcementLabel);
            items.Add(sourceItem with
            {
                Rule = validation.Rule ?? sourceItem.Rule,
                Diagnostics = validation.Diagnostics,
                EstimatedBarCount = 0,
                IsIncluded = zoneOverride.IsIncluded,
                IsManuallyOverridden = true
            });
        }

        List<string> diagnostics = calculatedPreview.EffectiveBaseDiagnostics.ToList();
        if (!items.Any(item => item.IsIncluded))
        {
            diagnostics.Add("Все зоны исключены из раскладки. Включите минимум одну зону.");
        }

        IReadOnlyList<IsoFieldSlabRebarSegment> segments = Array.Empty<IsoFieldSlabRebarSegment>();
        if (items.Any(item => item.IsIncluded && item.HasValidRule))
        {
            try
            {
                segments = layoutService.BuildSegments(items, settings);
            }
            catch (InvalidOperationException exception)
            {
                diagnostics.Add(exception.Message);
            }
        }

        Dictionary<string, int> countByZone = segments
            .GroupBy(segment => segment.ZoneId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        items = items
            .Select(item => item with
            {
                EstimatedBarCount = item.IsIncluded
                    && countByZone.TryGetValue(item.ZoneId, out int count)
                        ? count
                        : 0
            })
            .ToList();

        return new RebarRulePreviewResult(
            items,
            diagnostics.Distinct(StringComparer.Ordinal).ToArray(),
            settings,
            segments.Count,
            calculatedPreview.EffectiveBaseDiagnostics);
    }
}
