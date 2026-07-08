using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.TitleBlockFill.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.TitleBlockFill.Services;

public sealed class TitleBlockFillService
{
    private readonly TitleBlockValueResolver valueResolver;
    private readonly TitleBlockParameterWriter parameterWriter;

    public TitleBlockFillService(
        TitleBlockValueResolver valueResolver,
        TitleBlockParameterWriter parameterWriter)
    {
        this.valueResolver = valueResolver ?? throw new ArgumentNullException(nameof(valueResolver));
        this.parameterWriter = parameterWriter ?? throw new ArgumentNullException(nameof(parameterWriter));
    }

    public IReadOnlyList<TitleBlockPreviewRow> Preview(
        Document document,
        IReadOnlyList<TitleBlockSheetRow> sheets,
        IReadOnlyList<TitleBlockParameterRule> rules,
        TitleBlockFinderService titleBlockFinder)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(sheets, nameof(sheets));
        Guard.NotNull(rules, nameof(rules));
        Guard.NotNull(titleBlockFinder, nameof(titleBlockFinder));

        List<TitleBlockPreviewRow> rows = new();
        foreach (TitleBlockSheetRow sheetRow in sheets.Where(row => row.IsSelected && !row.IsPlaceholder))
        {
            if (document.GetElement(RevitElementIds.Create(sheetRow.ElementId)) is not ViewSheet sheet)
            {
                rows.Add(CreateRow(-1, sheetRow, TitleBlockRuleTargets.Sheet, string.Empty, string.Empty, string.Empty, "Ошибка", "Лист не найден.", canApply: false));
                continue;
            }

            IReadOnlyList<FamilyInstance> titleBlocks = titleBlockFinder.Find(document, sheet);
            for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
            {
                TitleBlockParameterRule rule = rules[ruleIndex];
                if (!rule.IsEnabled)
                {
                    continue;
                }

                rows.AddRange(PreviewRule(document, sheetRow, sheet, titleBlocks, rule, ruleIndex));
            }
        }

        return rows;
    }

    public TitleBlockApplyResult Apply(
        Document document,
        IReadOnlyList<TitleBlockSheetRow> sheets,
        IReadOnlyList<TitleBlockParameterRule> rules,
        TitleBlockFinderService titleBlockFinder,
        ITrueBimLogger logger)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(logger, nameof(logger));

        List<TitleBlockPreviewRow> previewRows = Preview(document, sheets, rules, titleBlockFinder).ToList();
        List<TitleBlockPreviewRow> resultRows = new();
        using Transaction transaction = new(document, "TrueBIM Fill Title Blocks");
        transaction.Start();

        foreach (TitleBlockPreviewRow row in previewRows)
        {
            if (!row.CanApply)
            {
                resultRows.Add(row with { Status = "Пропущено" });
                continue;
            }

            try
            {
                TitleBlockParameterRule? rule = row.RuleIndex >= 0 && row.RuleIndex < rules.Count
                    ? rules[row.RuleIndex]
                    : null;
                if (rule is null || document.GetElement(RevitElementIds.Create(row.SheetElementId)) is not ViewSheet sheet)
                {
                    resultRows.Add(row with { Status = "Ошибка", Message = "Правило или лист не найдены." });
                    continue;
                }

                Element? target = ResolveTarget(document, sheet, rule, titleBlockFinder);
                Parameter? parameter = target?.LookupParameter(rule.ParameterName.Trim());
                if (parameter is null)
                {
                    resultRows.Add(row with { Status = "Ошибка", Message = "Параметр не найден." });
                    continue;
                }

                string newValue = valueResolver.Resolve(document, sheet, rule);
                if (parameterWriter.TryWrite(parameter, newValue, out string message))
                {
                    resultRows.Add(row with { NewValue = newValue, Status = "Готово", Message = message });
                }
                else
                {
                    resultRows.Add(row with { NewValue = newValue, Status = "Ошибка", Message = message });
                }
            }
            catch (Exception exception)
            {
                logger.Error($"Failed to fill title block parameter '{row.ParameterName}' on sheet '{row.SheetNumber}'.", exception);
                resultRows.Add(row with { Status = "Ошибка", Message = exception.Message });
            }
        }

        transaction.Commit();
        return new TitleBlockApplyResult(resultRows);
    }

    private IEnumerable<TitleBlockPreviewRow> PreviewRule(
        Document document,
        TitleBlockSheetRow sheetRow,
        ViewSheet sheet,
        IReadOnlyList<FamilyInstance> titleBlocks,
        TitleBlockParameterRule rule,
        int ruleIndex)
    {
        if (string.IsNullOrWhiteSpace(rule.ParameterName))
        {
            yield return CreateRow(ruleIndex, sheetRow, rule.Target, string.Empty, string.Empty, string.Empty, "Пропущено", "У правила не указан параметр.", canApply: false);
            yield break;
        }

        Element? target = ResolveTarget(sheet, titleBlocks, rule);
        if (target is null)
        {
            yield return CreateRow(ruleIndex, sheetRow, rule.Target, rule.ParameterName, string.Empty, string.Empty, "Пропущено", "Штамп на листе не найден.", canApply: false);
            yield break;
        }

        Parameter? parameter = target.LookupParameter(rule.ParameterName.Trim());
        string newValue = valueResolver.Resolve(document, sheet, rule);
        if (parameter is null)
        {
            yield return CreateRow(ruleIndex, sheetRow, rule.Target, rule.ParameterName, string.Empty, newValue, "Пропущено", "Параметр не найден.", canApply: false);
            yield break;
        }

        string currentValue = parameterWriter.Read(parameter);
        if (parameter.IsReadOnly)
        {
            yield return CreateRow(ruleIndex, sheetRow, rule.Target, rule.ParameterName, currentValue, newValue, "Пропущено", "Параметр доступен только для чтения.", canApply: false);
            yield break;
        }

        if (parameter.StorageType is StorageType.ElementId or StorageType.None)
        {
            yield return CreateRow(ruleIndex, sheetRow, rule.Target, rule.ParameterName, currentValue, newValue, "Пропущено", $"Тип параметра {parameter.StorageType} не поддержан в MVP.", canApply: false);
            yield break;
        }

        string status = string.Equals(currentValue, newValue, StringComparison.CurrentCulture)
            ? "Без изменений"
            : "Будет записано";
        yield return CreateRow(ruleIndex, sheetRow, rule.Target, rule.ParameterName, currentValue, newValue, status, "Готово к применению.", canApply: true);
    }

    private Element? ResolveTarget(
        Document document,
        ViewSheet sheet,
        TitleBlockParameterRule rule,
        TitleBlockFinderService titleBlockFinder)
    {
        return string.Equals(rule.Target, TitleBlockRuleTargets.TitleBlock, StringComparison.CurrentCultureIgnoreCase)
            ? titleBlockFinder.Find(document, sheet).FirstOrDefault()
            : sheet;
    }

    private static Element? ResolveTarget(
        ViewSheet sheet,
        IReadOnlyList<FamilyInstance> titleBlocks,
        TitleBlockParameterRule rule)
    {
        return string.Equals(rule.Target, TitleBlockRuleTargets.TitleBlock, StringComparison.CurrentCultureIgnoreCase)
            ? titleBlocks.FirstOrDefault()
            : sheet;
    }

    private static TitleBlockPreviewRow CreateRow(
        int ruleIndex,
        TitleBlockSheetRow sheet,
        string target,
        string parameterName,
        string currentValue,
        string newValue,
        string status,
        string message,
        bool canApply)
    {
        return new TitleBlockPreviewRow(
            ruleIndex,
            sheet.ElementId,
            sheet.SheetNumber,
            sheet.SheetName,
            target,
            parameterName,
            currentValue,
            newValue,
            status,
            message,
            canApply);
    }
}
