using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishSchedulePreviewValidator
{
    public FinishScheduleValidationResult Validate(FinishScheduleSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        List<FinishScheduleValidationIssue> issues = [];
        ValidateCategory("walls", "Стены", settings.Walls, issues);
        ValidateCategory("floors", "Полы", settings.Floors, issues);
        ValidateCategory("ceilings", "Потолки", settings.Ceilings, issues);
        if (!settings.Walls.IsEnabled && !settings.Floors.IsEnabled && !settings.Ceilings.IsEnabled)
        {
            issues.Add(new FinishScheduleValidationIssue(
                "categories.none_enabled",
                "categories",
                "Включите хотя бы одну категорию отделки для предпросмотра."));
        }

        switch (settings.Scope.Kind)
        {
            case ReportScopeKind.Level when settings.Scope.LevelId is null or <= 0:
                issues.Add(new FinishScheduleValidationIssue(
                    "scope.level.missing",
                    "scope.level",
                    "Выберите уровень для предпросмотра."));
                break;
            case ReportScopeKind.Section:
                if (settings.Scope.SectionParameter is null)
                {
                    issues.Add(new FinishScheduleValidationIssue(
                        "scope.section_parameter.missing",
                        "scope.section_parameter",
                        "Выберите параметр секции или корпуса."));
                }

                if (string.IsNullOrWhiteSpace(settings.Scope.SectionValue))
                {
                    issues.Add(new FinishScheduleValidationIssue(
                        "scope.section_value.empty",
                        "scope.section_value",
                        "Введите значение секции или корпуса."));
                }

                break;
        }

        return new FinishScheduleValidationResult(issues);
    }

    private static void ValidateCategory(
        string code,
        string displayName,
        FinishCategorySettings category,
        List<FinishScheduleValidationIssue> issues)
    {
        if (category.IsEnabled && string.IsNullOrWhiteSpace(category.ClassificationValue))
        {
            issues.Add(new FinishScheduleValidationIssue(
                $"{code}.classification.empty",
                $"{code}.classification",
                $"{displayName}: задайте значение классификации для предпросмотра."));
        }
    }
}
