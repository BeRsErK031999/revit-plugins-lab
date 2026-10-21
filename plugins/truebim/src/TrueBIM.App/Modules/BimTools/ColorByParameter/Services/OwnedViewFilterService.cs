using System.Globalization;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.ColorByParameter.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.ColorByParameter.Services;

public sealed class OwnedViewFilterService
{
    private readonly FilterNameBuilder names = new();

    public IReadOnlyList<OwnedViewFilterRow> Collect(Document document)
    {
        return new FilteredElementCollector(document).OfClass(typeof(ParameterFilterElement))
            .Cast<ParameterFilterElement>().Where(filter => names.IsOwnedFilterName(filter.Name))
            .OrderBy(filter => filter.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(filter => new OwnedViewFilterRow(RevitElementIds.GetValue(filter.Id), filter.Name,
                string.Join(", ", filter.GetCategories().Select(id => Category.GetCategory(document, id)?.Name ?? id.ToString())
                    .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)),
                Describe(document, filter.GetElementFilter()))).ToArray();
    }

    public int Delete(Document document, IEnumerable<long> selectedIds)
    {
        // Revalidate ownership at execution time: the modeless table may be stale.
        ElementId[] ids = selectedIds.Distinct().Select(RevitElementIds.Create)
            .Where(id => document.GetElement(id) is ParameterFilterElement filter && names.IsOwnedFilterName(filter.Name))
            .ToArray();
        if (ids.Length == 0) return 0;
        using Transaction transaction = new(document, "TrueBIM: удалить выбранные фильтры окраски");
        if (transaction.Start() != TransactionStatus.Started)
            throw new InvalidOperationException("Не удалось начать удаление фильтров.");
        document.Delete(ids);
        if (transaction.Commit() != TransactionStatus.Committed)
            throw new InvalidOperationException("Revit отменил удаление фильтров.");
        return ids.Length;
    }

    private static string Describe(Document document, ElementFilter filter)
    {
        return filter switch
        {
            LogicalAndFilter and => "(" + string.Join(" И ", and.GetFilters().Select(child => Describe(document, child))) + ")",
            LogicalOrFilter or => "(" + string.Join(" ИЛИ ", or.GetFilters().Select(child => Describe(document, child))) + ")",
            ElementParameterFilter parameter => (parameter.Inverted ? "НЕ " : "")
                + string.Join(" И ", parameter.GetRules().Select(rule => DescribeRule(document, rule))),
            _ => $"Специальный фильтр: {filter.GetType().Name}"
        };
    }

    private static string DescribeRule(Document document, FilterRule rule)
    {
        if (rule is FilterInverseRule inverse) return "НЕ (" + DescribeRule(document, inverse.GetInnerRule()) + ")";
        ElementId parameterId = rule.GetRuleParameter();
        string parameter = document.GetElement(parameterId)?.Name ?? parameterId.ToString();
        long value = RevitElementIds.GetValue(parameterId);
        if (value < -1)
        {
            try { parameter = LabelUtils.GetLabelFor((BuiltInParameter)value); }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException) { }
        }
        string comparison = rule switch
        {
            FilterStringRule text => $"{Operator(text.GetEvaluator().GetType().Name)} «{text.RuleString}»",
            FilterDoubleRule number => $"{Operator(number.GetEvaluator().GetType().Name)} {number.RuleValue.ToString("G", CultureInfo.CurrentCulture)} (внутренние единицы Revit)",
            FilterIntegerRule integer => $"{Operator(integer.GetEvaluator().GetType().Name)} {integer.RuleValue}",
            FilterElementIdRule element => $"{Operator(element.GetEvaluator().GetType().Name)} {document.GetElement(element.RuleValue)?.Name ?? element.RuleValue.ToString()}",
            _ => rule.GetType().Name switch
            {
                "HasValueFilterRule" => "имеет значение",
                "HasNoValueFilterRule" => "не имеет значения",
                _ => $"правило {rule.GetType().Name}"
            }
        };
        return $"{parameter} {comparison}";
    }

    private static string Operator(string name) => name switch
    {
        "FilterStringEquals" or "FilterNumericEquals" => "=",
        "FilterStringContains" => "содержит",
        "FilterStringBeginsWith" => "начинается с",
        "FilterStringEndsWith" => "заканчивается на",
        "FilterStringGreater" or "FilterNumericGreater" => ">",
        "FilterStringGreaterOrEqual" or "FilterNumericGreaterOrEqual" => ">=",
        "FilterStringLess" or "FilterNumericLess" => "<",
        "FilterStringLessOrEqual" or "FilterNumericLessOrEqual" => "<=",
        _ => name
    };
}
