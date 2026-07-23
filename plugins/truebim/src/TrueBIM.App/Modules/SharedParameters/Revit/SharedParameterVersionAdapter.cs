using System.Globalization;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.SharedParameters.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.SharedParameters.Revit;

public sealed class SharedParameterVersionAdapter : ISharedParameterVersionAdapter
{
    public string GetDataTypeName(Definition definition)
    {
        Guard.NotNull(definition, nameof(definition));
#if REVIT2022_OR_GREATER
        ForgeTypeId dataType = definition.GetDataType();
        return GetKnownDataTypeName(dataType);
#else
        return definition.ParameterType.ToString();
#endif
    }

    public string GetParameterGroupName(Definition definition)
    {
        Guard.NotNull(definition, nameof(definition));
#if REVIT2022_OR_GREATER
        ForgeTypeId groupTypeId = definition.GetGroupTypeId();
        return string.IsNullOrWhiteSpace(groupTypeId.TypeId) ? "Без группы" : groupTypeId.TypeId;
#else
        return definition.ParameterGroup.ToString();
#endif
    }

    public FamilyParameter? FindFamilyParameter(FamilyManager familyManager, Guid guid)
    {
        Guard.NotNull(familyManager, nameof(familyManager));
        return familyManager.get_Parameter(guid);
    }

    public string GetFamilyParameterDataTypeName(FamilyParameter parameter)
    {
        Guard.NotNull(parameter, nameof(parameter));
#if REVIT2022_OR_GREATER
        ForgeTypeId dataType = parameter.Definition.GetDataType();
        return GetKnownDataTypeName(dataType);
#else
        return parameter.Definition.ParameterType.ToString();
#endif
    }

    public string GetFamilyParameterGroupName(FamilyParameter parameter)
    {
        Guard.NotNull(parameter, nameof(parameter));
        return GetParameterGroupName(parameter.Definition);
    }

    public IReadOnlyList<FilterRuleDescriptor> ExtractFilterRules(
        Document document,
        ElementFilter filter,
        long targetParameterId)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(filter, nameof(filter));
        List<FilterRuleDescriptor> rules = [];
        ExtractFilterRules(document, filter, targetParameterId, rules);
        return rules;
    }

    private static void ExtractFilterRules(
        Document document,
        ElementFilter filter,
        long targetParameterId,
        ICollection<FilterRuleDescriptor> output)
    {
        if (filter is ElementLogicalFilter logicalFilter)
        {
            foreach (ElementFilter child in logicalFilter.GetFilters())
            {
                ExtractFilterRules(document, child, targetParameterId, output);
            }

            return;
        }

        if (filter is not ElementParameterFilter parameterFilter)
        {
            return;
        }

        foreach (FilterRule rule in parameterFilter.GetRules())
        {
            output.Add(DescribeRule(document, rule, targetParameterId));
        }
    }

    private static FilterRuleDescriptor DescribeRule(
        Document document,
        FilterRule rule,
        long targetParameterId)
    {
        bool isInverted = rule is FilterInverseRule;
        FilterRule effectiveRule = rule is FilterInverseRule inverseRule
            ? inverseRule.GetInnerRule()
            : rule;
        ElementId parameterId = effectiveRule.GetRuleParameter();
        long parameterValue = RevitElementIds.GetValue(parameterId);
        string parameterName = document.GetElement(parameterId)?.Name ?? parameterValue.ToString(CultureInfo.InvariantCulture);
        string operatorName;
        string value;
        string valueType;

        switch (effectiveRule)
        {
            case FilterStringRule stringRule:
                operatorName = stringRule.GetEvaluator().GetType().Name;
                value = stringRule.RuleString;
                valueType = "String";
                break;
            case FilterIntegerRule integerRule:
                operatorName = integerRule.GetEvaluator().GetType().Name;
                value = integerRule.RuleValue.ToString(CultureInfo.InvariantCulture);
                valueType = "Integer";
                break;
            case FilterDoubleRule doubleRule:
                operatorName = doubleRule.GetEvaluator().GetType().Name;
                value = doubleRule.RuleValue.ToString("R", CultureInfo.InvariantCulture);
                valueType = "Double";
                break;
            case FilterElementIdRule elementIdRule:
                operatorName = elementIdRule.GetEvaluator().GetType().Name;
                value = RevitElementIds.GetValue(elementIdRule.RuleValue).ToString(CultureInfo.InvariantCulture);
                valueType = "ElementId";
                break;
            default:
                operatorName = effectiveRule.GetType().Name;
                value = string.Empty;
                valueType = "Unknown";
                break;
        }

        DetectionConfidence confidence = parameterValue == targetParameterId
            ? DetectionConfidence.Exact
            : valueType == "Unknown"
                ? DetectionConfidence.Partial
                : DetectionConfidence.Exact;
        return new FilterRuleDescriptor(
            parameterValue,
            parameterName,
            operatorName,
            value,
            valueType,
            isInverted,
            confidence,
            rule.GetType().Name);
    }

#if REVIT2022_OR_GREATER
    private static string GetKnownDataTypeName(ForgeTypeId dataType)
    {
        if (dataType == SpecTypeId.String.Text)
        {
            return "Text";
        }

        if (dataType == SpecTypeId.Int.Integer)
        {
            return "Integer";
        }

        if (dataType == SpecTypeId.Boolean.YesNo)
        {
            return "YesNo";
        }

        if (dataType == SpecTypeId.Number)
        {
            return "Number";
        }

        if (dataType == SpecTypeId.Length)
        {
            return "Length";
        }

        if (dataType == SpecTypeId.Area)
        {
            return "Area";
        }

        if (dataType == SpecTypeId.Volume)
        {
            return "Volume";
        }

        return string.IsNullOrWhiteSpace(dataType.TypeId) ? "Unknown" : dataType.TypeId;
    }
#endif
}
