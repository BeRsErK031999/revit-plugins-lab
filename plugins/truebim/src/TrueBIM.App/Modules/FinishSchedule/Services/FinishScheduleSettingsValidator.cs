using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishScheduleSettingsValidator
{
    private static readonly ParameterStorageKind[] TextStorage = [ParameterStorageKind.String];
    private static readonly ParameterStorageKind[] FilterStorage =
    [
        ParameterStorageKind.String,
        ParameterStorageKind.Integer,
        ParameterStorageKind.Double,
        ParameterStorageKind.ElementId
    ];

    private readonly ParameterCatalogMatcher matcher;

    public FinishScheduleSettingsValidator(ParameterCatalogMatcher matcher)
    {
        this.matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
    }

    public FinishScheduleValidationResult Validate(
        FinishScheduleSettings settings,
        ParameterCatalog catalog,
        FinishScheduleParameterCategories categories)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (categories is null)
        {
            throw new ArgumentNullException(nameof(categories));
        }

        List<FinishScheduleValidationIssue> issues = [];
        CategoryContext[] categoryContexts =
        [
            new(
                "walls",
                "Стены",
                settings.Walls,
                categories.Walls,
                FinishSchedulePreferredParameterNames.WallsOwnership,
                FinishSchedulePreferredParameterNames.WallsDescription,
                FinishSchedulePreferredParameterNames.WallsArea),
            new(
                "floors",
                "Полы",
                settings.Floors,
                categories.Floors,
                FinishSchedulePreferredParameterNames.FloorsOwnership,
                FinishSchedulePreferredParameterNames.FloorsDescription,
                FinishSchedulePreferredParameterNames.FloorsArea),
            new(
                "ceilings",
                "Потолки",
                settings.Ceilings,
                categories.Ceilings,
                FinishSchedulePreferredParameterNames.CeilingsOwnership,
                FinishSchedulePreferredParameterNames.CeilingsDescription,
                FinishSchedulePreferredParameterNames.CeilingsArea)
        ];
        CategoryContext[] enabledCategories = categoryContexts
            .Where(context => context.Settings.IsEnabled)
            .ToArray();

        if (enabledCategories.Length == 0)
        {
            issues.Add(new FinishScheduleValidationIssue(
                "categories.none_enabled",
                "categories",
                "Включите хотя бы одну категорию отделки."));
        }

        foreach (CategoryContext context in enabledCategories)
        {
            if (string.IsNullOrWhiteSpace(context.Settings.ClassificationValue))
            {
                issues.Add(new FinishScheduleValidationIssue(
                    $"{context.Code}.classification.empty",
                    $"{context.Code}.classification",
                    $"{context.DisplayName}: задайте значение параметра «{FinishScheduleSettings.ClassificationParameterName}»."));
            }
        }

        ValidateDescriptionParameter(settings, catalog, categories, enabledCategories, issues);
        ValidateRoomIdentifier(settings.RoomIdentifier, catalog, categories, issues);
        ValidateOutputParameter(
            "room_list_output",
            "Список помещений",
            settings.RoomListOutputParameter,
            catalog,
            categories,
            FinishSchedulePreferredParameterNames.RoomListOutput,
            issues);

        foreach (CategoryContext context in enabledCategories)
        {
            ValidateOutputParameter(
                $"{context.Code}.output_description",
                $"{context.DisplayName}: описание",
                context.Settings.OutputDescriptionParameter,
                catalog,
                categories,
                context.PreferredDescriptionName,
                issues);
            ValidateOutputParameter(
                $"{context.Code}.output_area",
                $"{context.DisplayName}: площадь",
                context.Settings.OutputAreaParameter,
                catalog,
                categories,
                context.PreferredAreaName,
                issues);

            if (settings.WriteOwnership)
            {
                ValidateRequiredReference(
                    $"{context.Code}.ownership",
                    $"{context.DisplayName}: принадлежность помещению",
                    context.Settings.OwnershipParameter,
                    catalog,
                    new ParameterCatalogRequirement(
                        $"{context.DisplayName}: принадлежность помещению",
                        ParameterBindingKind.Instance,
                        TextStorage,
                        [context.PhysicalCategory],
                        requireWritable: true),
                    context.PreferredOwnershipName,
                    issues);
            }
        }

        ValidateScope(settings.Scope, catalog, categories, issues);
        if (string.IsNullOrWhiteSpace(settings.ScheduleName))
        {
            issues.Add(new FinishScheduleValidationIssue(
                "schedule_name.empty",
                "schedule_name",
                "Задайте название спецификации."));
        }

        ValidateColumnWidths(settings.EffectiveColumnWidths, issues);
        IReadOnlyList<NamedParameterReference> outputParameters = CollectOutputParameters(
            settings,
            enabledCategories);
        ValidateDistinctOutputParameters(outputParameters, issues);
        ValidateSourceOutputConflicts(settings, outputParameters, issues);

        return new FinishScheduleValidationResult(issues);
    }

    private static void ValidateColumnWidths(
        FinishScheduleColumnWidths widths,
        List<FinishScheduleValidationIssue> issues)
    {
        ValidateColumnWidth(
            widths.RoomListMillimeters,
            "schedule_width.room_list.invalid",
            "schedule_width.room_list",
            "перечня помещений",
            issues);
        ValidateColumnWidth(
            widths.DescriptionMillimeters,
            "schedule_width.description.invalid",
            "schedule_width.description",
            "описания отделки",
            issues);
        ValidateColumnWidth(
            widths.AreaMillimeters,
            "schedule_width.area.invalid",
            "schedule_width.area",
            "площади",
            issues);
    }

    private static void ValidateColumnWidth(
        double width,
        string code,
        string field,
        string role,
        List<FinishScheduleValidationIssue> issues)
    {
        if (!double.IsNaN(width)
            && !double.IsInfinity(width)
            && width >= FinishScheduleColumnWidths.MinimumMillimeters
            && width <= FinishScheduleColumnWidths.MaximumMillimeters)
        {
            return;
        }

        issues.Add(new FinishScheduleValidationIssue(
            code,
            field,
            $"Ширина столбца {role} должна быть от "
                + $"{FinishScheduleColumnWidths.MinimumMillimeters:0} до "
                + $"{FinishScheduleColumnWidths.MaximumMillimeters:0} мм."));
    }

    private void ValidateDescriptionParameter(
        FinishScheduleSettings settings,
        ParameterCatalog catalog,
        FinishScheduleParameterCategories categories,
        IReadOnlyList<CategoryContext> enabledCategories,
        List<FinishScheduleValidationIssue> issues)
    {
        if (enabledCategories.Count == 0)
        {
            return;
        }

        List<ParameterCategoryReference> requiredCategories = [];
        if (settings.Walls.IsEnabled)
        {
            requiredCategories.Add(categories.Walls);
        }

        if (settings.Floors.IsEnabled)
        {
            requiredCategories.Add(categories.Floors);
        }

        if (settings.Ceilings.IsEnabled)
        {
            requiredCategories.Add(categories.Ceilings);
        }

        ValidateRequiredReference(
            "description_parameter",
            "Источник описания отделки",
            settings.DescriptionParameter,
            catalog,
            new ParameterCatalogRequirement(
                "Источник описания отделки",
                ParameterBindingKind.Type,
                TextStorage,
                requiredCategories,
                requireWritable: false),
            FinishSchedulePreferredParameterNames.Description,
            issues);
    }

    private void ValidateRoomIdentifier(
        RoomIdentifierSettings settings,
        ParameterCatalog catalog,
        FinishScheduleParameterCategories categories,
        List<FinishScheduleValidationIssue> issues)
    {
        if (settings.Mode == RoomIdentifierMode.CustomParameter)
        {
            ValidateRequiredReference(
                "room_identifier.custom_parameter",
                "Идентификатор помещения",
                settings.CustomParameter,
                catalog,
                new ParameterCatalogRequirement(
                    "Идентификатор помещения",
                    ParameterBindingKind.Instance,
                    TextStorage,
                    [categories.Rooms],
                    requireWritable: false),
                null,
                issues);
        }
        else if (settings.CustomParameter is not null)
        {
            issues.Add(new FinishScheduleValidationIssue(
                "room_identifier.unexpected_parameter",
                "room_identifier.custom_parameter",
                "Пользовательский параметр помещения задан для системного режима идентификатора."));
        }
    }

    private void ValidateOutputParameter(
        string field,
        string roleName,
        ParameterReference? reference,
        ParameterCatalog catalog,
        FinishScheduleParameterCategories categories,
        string preferredName,
        List<FinishScheduleValidationIssue> issues)
    {
        ValidateRequiredReference(
            field,
            roleName,
            reference,
            catalog,
            new ParameterCatalogRequirement(
                roleName,
                ParameterBindingKind.Instance,
                TextStorage,
                [categories.Rooms],
                requireWritable: true),
            preferredName,
            issues);
    }

    private void ValidateScope(
        ReportScopeSettings scope,
        ParameterCatalog catalog,
        FinishScheduleParameterCategories categories,
        List<FinishScheduleValidationIssue> issues)
    {
        switch (scope.Kind)
        {
            case ReportScopeKind.Level:
                if (!scope.LevelId.HasValue || scope.LevelId.Value <= 0)
                {
                    issues.Add(new FinishScheduleValidationIssue(
                        "scope.level.missing",
                        "scope.level",
                        "Выберите уровень для расчёта."));
                }

                break;
            case ReportScopeKind.Section:
                ValidateRequiredReference(
                    "scope.section_parameter",
                    "Параметр секции или корпуса",
                    scope.SectionParameter,
                    catalog,
                    new ParameterCatalogRequirement(
                        "Параметр секции или корпуса",
                        ParameterBindingKind.Instance,
                        FilterStorage,
                        [categories.Rooms],
                        requireWritable: false),
                    null,
                    issues);
                if (string.IsNullOrWhiteSpace(scope.SectionValue))
                {
                    issues.Add(new FinishScheduleValidationIssue(
                        "scope.section_value.empty",
                        "scope.section_value",
                        "Выберите значение секции или корпуса."));
                }

                break;
            case ReportScopeKind.EntireProject:
                break;
            default:
                issues.Add(new FinishScheduleValidationIssue(
                    "scope.unsupported",
                    "scope",
                    "Выбран неподдерживаемый режим области расчёта."));
                break;
        }
    }

    private void ValidateRequiredReference(
        string field,
        string roleName,
        ParameterReference? reference,
        ParameterCatalog catalog,
        ParameterCatalogRequirement requirement,
        string? preferredName,
        List<FinishScheduleValidationIssue> issues)
    {
        if (reference is null)
        {
            issues.Add(new FinishScheduleValidationIssue(
                $"{field}.missing",
                field,
                string.IsNullOrWhiteSpace(preferredName)
                    ? $"{roleName}: выберите параметр."
                    : $"{roleName}: выберите параметр. Рекомендуемое имя — «{preferredName}»."));
            return;
        }

        ParameterCatalogItem? item = catalog.Find(reference);
        if (item is null)
        {
            issues.Add(new FinishScheduleValidationIssue(
                $"{field}.not_found",
                field,
                $"{roleName}: параметр «{reference.Name}» не найден в текущем документе."));
            return;
        }

        ParameterCompatibilityResult compatibility = matcher.Evaluate(item, requirement);
        foreach (ParameterCompatibilityIssue compatibilityIssue in compatibility.Issues)
        {
            issues.Add(new FinishScheduleValidationIssue(
                $"{field}.{compatibilityIssue.Code}",
                field,
                compatibilityIssue.Message));
        }
    }

    private static IReadOnlyList<NamedParameterReference> CollectOutputParameters(
        FinishScheduleSettings settings,
        IEnumerable<CategoryContext> enabledCategories)
    {
        List<NamedParameterReference> parameters = [];
        AddIfNotNull(parameters, "Список помещений", settings.RoomListOutputParameter);
        foreach (CategoryContext context in enabledCategories)
        {
            AddIfNotNull(
                parameters,
                $"{context.DisplayName}: описание",
                context.Settings.OutputDescriptionParameter);
            AddIfNotNull(
                parameters,
                $"{context.DisplayName}: площадь",
                context.Settings.OutputAreaParameter);
        }

        return parameters;
    }

    private static void ValidateDistinctOutputParameters(
        IReadOnlyList<NamedParameterReference> outputParameters,
        List<FinishScheduleValidationIssue> issues)
    {
        foreach (IGrouping<string, NamedParameterReference> duplicate in outputParameters
                     .GroupBy(parameter => parameter.Reference.StableKey, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            issues.Add(new FinishScheduleValidationIssue(
                "outputs.duplicate_parameter",
                "outputs",
                $"Один параметр нельзя использовать для нескольких выходных значений: {string.Join(", ", duplicate.Select(parameter => parameter.RoleName))}."));
        }
    }

    private static void ValidateSourceOutputConflicts(
        FinishScheduleSettings settings,
        IReadOnlyList<NamedParameterReference> outputParameters,
        List<FinishScheduleValidationIssue> issues)
    {
        HashSet<string> outputKeys = outputParameters
            .Select(parameter => parameter.Reference.StableKey)
            .ToHashSet(StringComparer.Ordinal);

        if (settings.RoomIdentifier.Mode == RoomIdentifierMode.CustomParameter
            && settings.RoomIdentifier.CustomParameter is not null
            && outputKeys.Contains(settings.RoomIdentifier.CustomParameter.StableKey))
        {
            issues.Add(new FinishScheduleValidationIssue(
                "room_identifier.output_conflict",
                "room_identifier.custom_parameter",
                "Идентификатор помещения нельзя записывать в один из выходных параметров ведомости."));
        }

        if (settings.Scope.Kind == ReportScopeKind.Section
            && settings.Scope.SectionParameter is not null
            && outputKeys.Contains(settings.Scope.SectionParameter.StableKey))
        {
            issues.Add(new FinishScheduleValidationIssue(
                "scope.output_conflict",
                "scope.section_parameter",
                "Параметр секции нельзя использовать как выходной параметр: запись ведомости изменила бы область расчёта."));
        }
    }

    private static void AddIfNotNull(
        List<NamedParameterReference> parameters,
        string roleName,
        ParameterReference? reference)
    {
        if (reference is not null)
        {
            parameters.Add(new NamedParameterReference(roleName, reference));
        }
    }

    private sealed record CategoryContext(
        string Code,
        string DisplayName,
        FinishCategorySettings Settings,
        ParameterCategoryReference PhysicalCategory,
        string PreferredOwnershipName,
        string PreferredDescriptionName,
        string PreferredAreaName);

    private sealed record NamedParameterReference(
        string RoleName,
        ParameterReference Reference);
}
