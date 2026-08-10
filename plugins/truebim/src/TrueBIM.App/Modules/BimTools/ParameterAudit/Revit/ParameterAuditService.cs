using System.Globalization;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.ParameterAudit.Models;
using TrueBIM.App.Modules.BimTools.ParameterAudit.Services;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.ParameterAudit.Revit;

public sealed class ParameterAuditService
{
    private readonly ParameterAuditRuleEvaluator evaluator;
    private readonly ITrueBimLogger logger;

    public ParameterAuditService(
        ParameterAuditRuleEvaluator evaluator,
        ITrueBimLogger logger)
    {
        this.evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ParameterAuditReport Analyze(
        Document document,
        IReadOnlyList<ParameterAuditRule> rules,
        bool includeLinks)
    {
        Guard.NotNull(document, nameof(document));
        rules ??= [];
        IReadOnlyList<ParameterAuditRule> enabledRules = rules.Where(rule => rule.Enabled).ToList();
        List<ParameterAuditResultRow> rows = [];
        int checkedCount = 0;
        int passedCount = 0;

        AnalyzeDocument(
            document,
            document.Title,
            false,
            null,
            enabledRules,
            rows,
            ref checkedCount,
            ref passedCount);

        if (includeLinks)
        {
            foreach (RevitLinkInstance link in new FilteredElementCollector(document)
                         .OfClass(typeof(RevitLinkInstance))
                         .Cast<RevitLinkInstance>())
            {
                Document? linkDocument = link.GetLinkDocument();
                if (linkDocument is null)
                {
                    rows.Add(new ParameterAuditResultRow(
                        string.Empty,
                        ParameterAuditSeverity.Warning,
                        ParameterAuditIssueCode.LinkUnavailable,
                        link.Name,
                        true,
                        null,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        "Связь не загружена или недоступна для чтения."));
                    continue;
                }

                AnalyzeDocument(
                    linkDocument,
                    link.Name,
                    true,
                    RevitElementIds.GetValue(link.Id),
                    enabledRules,
                    rows,
                    ref checkedCount,
                    ref passedCount);
            }
        }

        ParameterAuditReport report = new(
            DateTimeOffset.Now,
            enabledRules.Count,
            checkedCount,
            passedCount,
            rows
                .OrderByDescending(row => row.Severity)
                .ThenBy(row => row.RuleId, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row => row.SourceModel, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row => row.CategoryName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row => row.ElementId)
                .ToList());
        logger.Info(
            $"Parameter Audit completed. Rules={report.RuleCount}; Checked={report.CheckedCount}; "
            + $"Passed={report.PassedCount}; Errors={report.ErrorCount}; Warnings={report.WarningCount}; "
            + $"IncludeLinks={includeLinks}.");
        return report;
    }

    private void AnalyzeDocument(
        Document document,
        string sourceModel,
        bool isLinked,
        long? hostLinkInstanceId,
        IReadOnlyList<ParameterAuditRule> rules,
        ICollection<ParameterAuditResultRow> resultRows,
        ref int checkedCount,
        ref int passedCount)
    {
        IReadOnlyList<Category> categories = document.Settings.Categories
            .Cast<Category>()
            .Where(category => category is not null && category.Id != ElementId.InvalidElementId)
            .GroupBy(category => RevitElementIds.GetValue(category.Id))
            .Select(group => group.First())
            .ToList();
        Dictionary<string, IReadOnlyList<Element>> elementCache = new(StringComparer.Ordinal);

        IEnumerable<IGrouping<ParameterAuditTargetKey, ParameterAuditRule>> ruleGroups = rules.GroupBy(rule =>
            new ParameterAuditTargetKey(
                rule.Scope,
                rule.CategoryPattern,
                rule.FamilyPattern,
                rule.TypePattern,
                rule.SelectionParameterName,
                rule.SelectionExpectedValue));
        foreach (IGrouping<ParameterAuditTargetKey, ParameterAuditRule> ruleGroup in ruleGroups)
        {
            List<ParameterAuditRule> validRules = [];
            foreach (ParameterAuditRule rule in ruleGroup)
            {
                if (!string.IsNullOrWhiteSpace(rule.BuiltInParameter)
                    && !TryResolveBuiltInParameter(rule.BuiltInParameter, out _))
                {
                    resultRows.Add(CreateRuleLevelResult(
                        rule,
                        sourceModel,
                        isLinked,
                        ParameterAuditIssueCode.RuleError,
                        $"BuiltInParameter «{rule.BuiltInParameter}» не распознан.",
                        ParameterAuditSeverity.Error));
                    continue;
                }

                validRules.Add(rule);
            }

            if (validRules.Count == 0)
            {
                continue;
            }

            ParameterAuditRule representativeRule = validRules[0];
            IReadOnlyList<Category> matchingCategories = categories
                .Where(category => evaluator.MatchesSelector(category.Name, representativeRule.CategoryPattern))
                .ToList();
            if (matchingCategories.Count == 0)
            {
                foreach (ParameterAuditRule rule in validRules)
                {
                    resultRows.Add(CreateRuleLevelResult(
                        rule,
                        sourceModel,
                        isLinked,
                        ParameterAuditIssueCode.CategoryNotFound,
                        $"Категория «{rule.CategoryPattern}» не найдена в модели."));
                }

                continue;
            }

            int matchingElementCount = 0;
            foreach (Category category in matchingCategories)
            {
                IReadOnlyList<Element> elements = CollectElements(
                    document,
                    category,
                    representativeRule.Scope,
                    elementCache);
                foreach (Element element in elements)
                {
                    ParameterAuditElementSnapshot snapshot = CreateElementSnapshot(
                        document,
                        element,
                        sourceModel,
                        isLinked,
                        representativeRule.Scope,
                        hostLinkInstanceId);
                    if (!evaluator.MatchesTarget(representativeRule, snapshot)
                        || !MatchesSelectionParameter(document, element, representativeRule))
                    {
                        continue;
                    }

                    matchingElementCount++;
                    foreach (ParameterAuditRule rule in validRules)
                    {
                        checkedCount++;
                        try
                        {
                            ParameterAuditValueSnapshot value = ResolveParameter(document, element, rule);
                            ParameterAuditResultRow? result = evaluator.Evaluate(rule, snapshot, value);
                            if (result is null)
                            {
                                passedCount++;
                            }
                            else
                            {
                                resultRows.Add(result);
                            }
                        }
                        catch (Exception exception)
                        {
                            resultRows.Add(new ParameterAuditResultRow(
                                rule.RuleId,
                                ParameterAuditSeverity.Error,
                                ParameterAuditIssueCode.RuleError,
                                sourceModel,
                                isLinked,
                                snapshot.ElementId,
                                snapshot.CategoryName,
                                snapshot.FamilyName,
                                snapshot.TypeName,
                                rule.ParameterDisplay,
                                string.Empty,
                                rule.ExpectedDescription,
                                $"Не удалось проверить параметр: {exception.Message}",
                                snapshot.UniqueId,
                                snapshot.HostLinkInstanceId));
                            logger.Error(
                                $"Parameter Audit failed for rule '{rule.RuleId}', element {snapshot.ElementId}.",
                                exception);
                        }
                    }
                }
            }

            if (matchingElementCount == 0)
            {
                string message = representativeRule.HasSelectionFilter
                    ? $"Не найдено элементов, где «{representativeRule.SelectionParameterName}» = «{representativeRule.SelectionExpectedValue}»."
                    : "Для правила не найдено элементов, подходящих по категории, семейству и типу.";
                foreach (ParameterAuditRule rule in validRules)
                {
                    resultRows.Add(CreateRuleLevelResult(
                        rule,
                        sourceModel,
                        isLinked,
                        ParameterAuditIssueCode.NoMatchingElements,
                        message));
                }
            }
        }
    }

    private static IReadOnlyList<Element> CollectElements(
        Document document,
        Category category,
        ParameterAuditScope scope,
        IDictionary<string, IReadOnlyList<Element>> cache)
    {
        string cacheKey = $"{RevitElementIds.GetValue(category.Id)}:{scope}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<Element>? cached))
        {
            return cached;
        }

        try
        {
            FilteredElementCollector collector = new FilteredElementCollector(document)
                .OfCategoryId(category.Id);
            collector = scope == ParameterAuditScope.Type
                ? collector.WhereElementIsElementType()
                : collector.WhereElementIsNotElementType();
            IReadOnlyList<Element> elements = collector
                .Where(element => element is not RevitLinkType)
                .ToList();
            cache[cacheKey] = elements;
            return elements;
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            cache[cacheKey] = [];
            return [];
        }
    }

    private static ParameterAuditElementSnapshot CreateElementSnapshot(
        Document document,
        Element element,
        string sourceModel,
        bool isLinked,
        ParameterAuditScope scope,
        long? hostLinkInstanceId)
    {
        string familyName = string.Empty;
        string typeName = string.Empty;
        if (element is FamilyInstance instance)
        {
            familyName = instance.Symbol?.FamilyName ?? string.Empty;
            typeName = instance.Symbol?.Name ?? string.Empty;
        }
        else if (element is ElementType elementType)
        {
            familyName = elementType.FamilyName ?? string.Empty;
            typeName = elementType.Name ?? string.Empty;
        }
        else
        {
            ElementId typeId = element.GetTypeId();
            if (typeId != ElementId.InvalidElementId && document.GetElement(typeId) is ElementType type)
            {
                familyName = type.FamilyName ?? string.Empty;
                typeName = type.Name ?? string.Empty;
            }
        }

        return new ParameterAuditElementSnapshot(
            sourceModel,
            isLinked,
            RevitElementIds.GetValue(element.Id),
            element.UniqueId ?? string.Empty,
            element.Category?.Name ?? string.Empty,
            familyName,
            typeName,
            scope,
            hostLinkInstanceId);
    }

    private bool MatchesSelectionParameter(
        Document document,
        Element element,
        ParameterAuditRule rule)
    {
        if (!rule.HasSelectionFilter)
        {
            return true;
        }

        IReadOnlyList<Parameter> parameters = GetNamedParameters(element, rule.SelectionParameterName);
        if (parameters.Count == 0
            && element is not ElementType
            && document.GetElement(element.GetTypeId()) is ElementType elementType)
        {
            parameters = GetNamedParameters(elementType, rule.SelectionParameterName);
        }

        return parameters.Any(parameter =>
            evaluator.MatchesSelection(rule, CreateValueSnapshot(parameter)));
    }

    private static ParameterAuditValueSnapshot ResolveParameter(
        Document document,
        Element element,
        ParameterAuditRule rule)
    {
        ParameterAuditValueSnapshot value = ResolveParameterOnElement(element, rule);
        if (value.Exists
            || !rule.HasSelectionFilter
            || element is ElementType
            || document.GetElement(element.GetTypeId()) is not ElementType elementType)
        {
            return value;
        }

        return ResolveParameterOnElement(elementType, rule);
    }

    private static ParameterAuditValueSnapshot ResolveParameterOnElement(
        Element element,
        ParameterAuditRule rule)
    {
        Parameter? parameter;
        if (rule.ParameterGuid.HasValue)
        {
            parameter = element.get_Parameter(rule.ParameterGuid.Value);
        }
        else if (!string.IsNullOrWhiteSpace(rule.BuiltInParameter))
        {
            if (!TryResolveBuiltInParameter(rule.BuiltInParameter, out BuiltInParameter builtInParameter))
            {
                throw new InvalidOperationException(
                    $"BuiltInParameter «{rule.BuiltInParameter}» не распознан.");
            }

            parameter = element.get_Parameter(builtInParameter);
        }
        else
        {
            IReadOnlyList<Parameter> parameters = GetNamedParameters(element, rule.ParameterName);
            if (parameters.Count > 1)
            {
                return new ParameterAuditValueSnapshot(
                    true,
                    true,
                    false,
                    string.Empty,
                    string.Empty,
                    null);
            }

            parameter = parameters.SingleOrDefault();
        }

        return parameter is null ? ParameterAuditValueSnapshot.Missing : CreateValueSnapshot(parameter);
    }

    private static IReadOnlyList<Parameter> GetNamedParameters(Element element, string parameterName)
    {
        return element.GetParameters(parameterName)
            .Cast<Parameter>()
            .Where(candidate => candidate is not null)
            .ToList();
    }

    private static ParameterAuditValueSnapshot CreateValueSnapshot(Parameter parameter)
    {
        string displayValue;
        try
        {
            displayValue = parameter.AsValueString() ?? string.Empty;
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            displayValue = string.Empty;
        }

        string rawValue;
        double? numericValue = null;
        switch (parameter.StorageType)
        {
            case StorageType.String:
                rawValue = parameter.AsString() ?? string.Empty;
                break;
            case StorageType.Integer:
                int integer = parameter.AsInteger();
                rawValue = integer.ToString(CultureInfo.InvariantCulture);
                numericValue = integer;
                break;
            case StorageType.Double:
                double number = parameter.AsDouble();
                rawValue = number.ToString("R", CultureInfo.InvariantCulture);
                numericValue = TryParseDisplayNumber(displayValue, out double displayNumber)
                    ? displayNumber
                    : ConvertFromInternalUnits(parameter, number);
                break;
            case StorageType.ElementId:
                rawValue = RevitElementIds.GetValue(parameter.AsElementId()).ToString(CultureInfo.InvariantCulture);
                break;
            default:
                rawValue = string.Empty;
                break;
        }

        return new ParameterAuditValueSnapshot(
            true,
            false,
            parameter.HasValue,
            displayValue,
            rawValue,
            numericValue);
    }

    private static bool TryResolveBuiltInParameter(string value, out BuiltInParameter parameter)
    {
        string normalized = value.Trim();
        if (Enum.TryParse(normalized, true, out parameter))
        {
            return true;
        }

        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer))
        {
            parameter = (BuiltInParameter)integer;
            return true;
        }

        parameter = default;
        return false;
    }

    private static bool TryParseDisplayNumber(string value, out double number)
    {
        string normalized = value.Trim().Replace(" ", string.Empty).Replace(" ", string.Empty);
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out number)
            || double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            || double.TryParse(normalized.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static double ConvertFromInternalUnits(Parameter parameter, double value)
    {
        try
        {
#if REVIT2021_OR_GREATER
            return UnitUtils.ConvertFromInternalUnits(value, parameter.GetUnitTypeId());
#else
            return UnitUtils.ConvertFromInternalUnits(value, parameter.DisplayUnitType);
#endif
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            return value;
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            return value;
        }
    }

    private static ParameterAuditResultRow CreateRuleLevelResult(
        ParameterAuditRule rule,
        string sourceModel,
        bool isLinked,
        ParameterAuditIssueCode code,
        string message,
        ParameterAuditSeverity severity = ParameterAuditSeverity.Warning)
    {
        return new ParameterAuditResultRow(
            rule.RuleId,
            severity,
            code,
            sourceModel,
            isLinked,
            null,
            rule.CategoryPattern,
            rule.FamilyPattern,
            rule.TypePattern,
            rule.ParameterDisplay,
            string.Empty,
            rule.ExpectedDescription,
            message);
    }

    private sealed record ParameterAuditTargetKey(
        ParameterAuditScope Scope,
        string CategoryPattern,
        string FamilyPattern,
        string TypePattern,
        string SelectionParameterName,
        string SelectionExpectedValue);
}
