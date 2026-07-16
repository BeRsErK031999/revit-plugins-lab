using System.Globalization;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed class ParametricScheduleService
{
    private const int PreviewRowLimit = 100;

    private readonly ParsedTableValidationService tableValidationService;
    private readonly ScheduleMappingConfigurationService mappingConfigurationService;
    private readonly ITrueBimLogger logger;

    public ParametricScheduleService(
        ParsedTableValidationService tableValidationService,
        ScheduleMappingConfigurationService mappingConfigurationService,
        ITrueBimLogger logger)
    {
        this.tableValidationService = tableValidationService ?? throw new ArgumentNullException(nameof(tableValidationService));
        this.mappingConfigurationService = mappingConfigurationService ?? throw new ArgumentNullException(nameof(mappingConfigurationService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ScheduleImportCreationResult Execute(Document document, ScheduleImportRequest request)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(request, nameof(request));

        ParsedTableValidationResult tableValidation = tableValidationService.Validate(request.Table);
        ScheduleMappingValidationResult mappingValidation = mappingConfigurationService.Validate(
            request.Table,
            request.CategoryId,
            request.Mappings);
        List<string> warnings =
        [
            .. tableValidation.Warnings,
            .. request.Table.Warnings,
            .. mappingValidation.Warnings
        ];
        List<string> errors = [.. tableValidation.Errors, .. mappingValidation.Errors];
        if (!string.Equals(
                mappingValidation.ConfigurationFingerprint,
                request.ConfigurationFingerprint,
                StringComparison.Ordinal))
        {
            errors.Add("Конфигурация полей изменилась после отправки запроса. Повторите предпросмотр.");
        }

        if (errors.Count > 0)
        {
            return CreateFailure(request, warnings, errors);
        }

        ViewSchedule? schedule = null;
        SchedulePreviewTable preview = SchedulePreviewTable.Empty;
        int matchedElementCount = 0;
        int filterCount = request.Mappings.Count(mapping => mapping.FilterRule != ScheduleFilterRule.None);
        using Transaction transaction = new(
            document,
            request.PreviewOnly
                ? "TrueBIM: предпросмотр параметрической спецификации"
                : "TrueBIM: создание параметрической спецификации");
        transaction.Start();
        try
        {
            ElementId categoryId = RevitElementIds.Create(request.CategoryId);
            if (!ViewSchedule.IsValidCategoryForSchedule(categoryId))
            {
                throw new InvalidOperationException("Выбранная категория больше недоступна для спецификации Revit.");
            }

            schedule = ViewSchedule.CreateSchedule(document, categoryId);
            schedule.Name = CreateUniqueScheduleName(document, request.Table.SourceFilePath, request.CategoryName);
            ConfigureDefinition(document, schedule.Definition, request.Mappings);
            document.Regenerate();

            using FilteredElementCollector collector = new(document, schedule.Id);
            matchedElementCount = collector
                .WhereElementIsNotElementType()
                .GetElementCount();
            preview = ReadPreview(schedule, request.Mappings, warnings);

            if (matchedElementCount == 0)
            {
                warnings.Add("По выбранной категории и условиям в модели не найдено элементов. Спецификация будет пустой.");
            }

            if (request.PreviewOnly)
            {
                transaction.RollBack();
            }
            else
            {
                transaction.Commit();
            }
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw;
        }

        warnings.Add("Значения из PDF не записываются в модель: PDF используется для выбора структуры и заголовков, а строки спецификации формируются элементами Revit.");
        string action = request.PreviewOnly ? "previewed" : "created";
        logger.Info($"Schedule Import {action} parametric ViewSchedule '{(request.PreviewOnly ? request.CategoryName : schedule!.Name)}'. Category='{request.CategoryName}'; Elements={matchedElementCount}; Fields={request.Mappings.Count}; Filters={filterCount}.");
        return new ScheduleImportCreationResult(
            request.PreviewOnly ? request.CategoryName : schedule!.Name,
            request.PreviewOnly ? null : RevitElementIds.GetValue(schedule!.Id),
            false,
            matchedElementCount,
            request.Mappings.Count,
            filterCount,
            request.PreviewOnly,
            request.ConfigurationFingerprint,
            preview,
            warnings.Distinct(StringComparer.CurrentCulture).ToList(),
            Array.Empty<string>());
    }

    private static void ConfigureDefinition(
        Document document,
        ScheduleDefinition definition,
        IReadOnlyList<ScheduleFieldMapping> mappings)
    {
        definition.ShowTitle = true;
        definition.ShowHeaders = true;
#if REVIT2022_OR_GREATER
        definition.ShowGridLines = true;
#endif
        definition.IsItemized = true;

        IList<SchedulableField> availableFields = definition.GetSchedulableFields();
        foreach (ScheduleFieldMapping mapping in mappings)
        {
            SchedulableField? schedulableField = availableFields.FirstOrDefault(field =>
                RevitElementIds.GetValue(field.ParameterId) == mapping.TargetParameterId
                && (int)field.FieldType == mapping.TargetFieldTypeValue);
            if (schedulableField is null)
            {
                throw new InvalidOperationException(
                    $"Поле Revit «{mapping.TargetFieldName}» больше недоступно для выбранной категории.");
            }

            ScheduleField scheduleField = definition.AddField(schedulableField);
            scheduleField.ColumnHeading = mapping.SourceColumnName;
            AddFilter(document, definition, scheduleField, mapping);
        }
    }

    private static void AddFilter(
        Document document,
        ScheduleDefinition definition,
        ScheduleField field,
        ScheduleFieldMapping mapping)
    {
        if (mapping.FilterRule == ScheduleFilterRule.None)
        {
            return;
        }

        if (!definition.CanFilter())
        {
            throw new InvalidOperationException("Revit не разрешает фильтровать выбранную спецификацию.");
        }

        try
        {
            if (mapping.FilterRule is ScheduleFilterRule.HasValue or ScheduleFilterRule.HasNoValue)
            {
#if REVIT2022_OR_GREATER
                if (!definition.CanFilterByValuePresence(field.FieldId))
                {
                    throw new InvalidOperationException("поле не поддерживает проверку наличия значения");
                }

                using ScheduleFilter presenceFilter = new(field.FieldId, MapFilterType(mapping.FilterRule));
                definition.AddFilter(presenceFilter);
                return;
#else
                throw new InvalidOperationException("условия «Есть значение» и «Нет значения» доступны в Revit 2022 и новее");
#endif
            }

            if (IsSubstringRule(mapping.FilterRule)
                && !definition.CanFilterBySubstring(field.FieldId))
            {
                throw new InvalidOperationException("поле не поддерживает текстовое условие");
            }

            if (!IsSubstringRule(mapping.FilterRule)
                && !definition.CanFilterByValue(field.FieldId))
            {
                throw new InvalidOperationException("поле не поддерживает сравнение по значению");
            }

            string value = mapping.FilterValue?.Trim() ?? string.Empty;
            ScheduleFilterType filterType = MapFilterType(mapping.FilterRule);
            using ScheduleFilter filter = CreateValueFilter(document, field, filterType, value);
            definition.AddFilter(filter);
        }
        catch (Exception exception) when (exception is not InvalidOperationException
                                          || !exception.Message.StartsWith("Условие для поля", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Условие для поля «{mapping.TargetFieldName}» не принято Revit: {exception.Message}",
                exception);
        }
    }

    private static ScheduleFilter CreateValueFilter(
        Document document,
        ScheduleField field,
        ScheduleFilterType filterType,
        string value)
    {
#if REVIT2022_OR_GREATER
        ForgeTypeId specTypeId = field.GetSpecTypeId();
        if (UnitUtils.IsMeasurableSpec(specTypeId))
        {
            if (!UnitFormatUtils.TryParse(document.GetUnits(), specTypeId, value, out double parsedValue))
            {
                throw new FormatException($"«{value}» не удалось прочитать в единицах проекта");
            }

            return new ScheduleFilter(field.FieldId, filterType, parsedValue);
        }

        if (specTypeId == SpecTypeId.Boolean.YesNo)
        {
            return new ScheduleFilter(field.FieldId, filterType, ParseBoolean(value));
        }

        if (specTypeId == SpecTypeId.Int.Integer)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out int parsedInteger)
                && !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInteger))
            {
                throw new FormatException($"«{value}» не является целым числом");
            }

            return new ScheduleFilter(field.FieldId, filterType, parsedInteger);
        }

        return new ScheduleFilter(field.FieldId, filterType, value);
#else
#pragma warning disable CS0618 // Revit 2019-2020 expose schedule units only through UnitType; Revit 2021 keeps it for compatibility.
        if (field.UnitType != UnitType.UT_Undefined
            && UnitFormatUtils.TryParse(document.GetUnits(), field.UnitType, value, out double parsedValue))
        {
            return new ScheduleFilter(field.FieldId, filterType, parsedValue);
        }
#pragma warning restore CS0618

        return new ScheduleFilter(field.FieldId, filterType, value);
#endif
    }

    private static int ParseBoolean(string value)
    {
        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "1" or "да" or "yes" or "true" => 1,
            "0" or "нет" or "no" or "false" => 0,
            _ => throw new FormatException("для параметра Да/Нет используйте Да, Нет, 1 или 0")
        };
    }

    private static bool IsSubstringRule(ScheduleFilterRule rule)
    {
        return rule is ScheduleFilterRule.Contains
            or ScheduleFilterRule.NotContains
            or ScheduleFilterRule.BeginsWith
            or ScheduleFilterRule.EndsWith;
    }

    private static ScheduleFilterType MapFilterType(ScheduleFilterRule rule)
    {
        return rule switch
        {
            ScheduleFilterRule.Equal => ScheduleFilterType.Equal,
            ScheduleFilterRule.NotEqual => ScheduleFilterType.NotEqual,
            ScheduleFilterRule.Contains => ScheduleFilterType.Contains,
            ScheduleFilterRule.NotContains => ScheduleFilterType.NotContains,
            ScheduleFilterRule.BeginsWith => ScheduleFilterType.BeginsWith,
            ScheduleFilterRule.EndsWith => ScheduleFilterType.EndsWith,
            ScheduleFilterRule.GreaterThan => ScheduleFilterType.GreaterThan,
            ScheduleFilterRule.GreaterThanOrEqual => ScheduleFilterType.GreaterThanOrEqual,
            ScheduleFilterRule.LessThan => ScheduleFilterType.LessThan,
            ScheduleFilterRule.LessThanOrEqual => ScheduleFilterType.LessThanOrEqual,
#if REVIT2022_OR_GREATER
            ScheduleFilterRule.HasValue => ScheduleFilterType.HasValue,
            ScheduleFilterRule.HasNoValue => ScheduleFilterType.HasNoValue,
#endif
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, null)
        };
    }

    private static SchedulePreviewTable ReadPreview(
        ViewSchedule schedule,
        IReadOnlyList<ScheduleFieldMapping> mappings,
        List<string> warnings)
    {
        try
        {
            TableSectionData body = schedule.GetTableData().GetSectionData(SectionType.Body);
            List<IReadOnlyList<string>> rows = [];
            int rowCount = Math.Min(body.NumberOfRows, PreviewRowLimit);
            for (int rowOffset = 0; rowOffset < rowCount; rowOffset++)
            {
                int row = body.FirstRowNumber + rowOffset;
                List<string> values = [];
                for (int columnOffset = 0; columnOffset < mappings.Count; columnOffset++)
                {
                    int column = body.FirstColumnNumber + columnOffset;
                    values.Add(schedule.GetCellText(SectionType.Body, row, column) ?? string.Empty);
                }

                rows.Add(values);
            }

            if (body.NumberOfRows > PreviewRowLimit)
            {
                warnings.Add($"В окне показаны первые {PreviewRowLimit} строк спецификации из {body.NumberOfRows}.");
            }

            return new SchedulePreviewTable(
                mappings.Select(mapping => mapping.SourceColumnName).ToList(),
                rows);
        }
        catch (Exception exception)
        {
            warnings.Add($"Спецификация проверена, но строки предпросмотра не удалось прочитать: {exception.Message}");
            return new SchedulePreviewTable(
                mappings.Select(mapping => mapping.SourceColumnName).ToList(),
                Array.Empty<IReadOnlyList<string>>());
        }
    }

    private static ScheduleImportCreationResult CreateFailure(
        ScheduleImportRequest request,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors)
    {
        return new ScheduleImportCreationResult(
            request.PreviewOnly ? request.CategoryName : "Спецификация не создана",
            null,
            false,
            0,
            request.Mappings.Count,
            request.Mappings.Count(mapping => mapping.FilterRule != ScheduleFilterRule.None),
            request.PreviewOnly,
            request.ConfigurationFingerprint,
            SchedulePreviewTable.Empty,
            warnings,
            errors);
    }

    private static string CreateUniqueScheduleName(
        Document document,
        string sourceFilePath,
        string categoryName)
    {
        string sourceName = System.IO.Path.GetFileNameWithoutExtension(sourceFilePath);
        string rawName = string.IsNullOrWhiteSpace(sourceName)
            ? $"TrueBIM_{categoryName}"
            : $"TrueBIM_{sourceName}_{categoryName}";
        string baseName = NormalizeScheduleName(rawName);
        HashSet<string> names = new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .Select(view => view.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!names.Contains(baseName))
        {
            return baseName;
        }

        for (int index = 2; index < 1000; index++)
        {
            string candidate = $"{baseName} ({index})";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{baseName} {Guid.NewGuid():N}";
    }

    private static string NormalizeScheduleName(string value)
    {
        char[] forbiddenCharacters = ['\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~'];
        string normalized = forbiddenCharacters.Aggregate(value, (current, character) => current.Replace(character, '_'));
        normalized = string.Join(" ", normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > 120)
        {
            normalized = normalized.Substring(0, 120).TrimEnd();
        }

        return string.IsNullOrWhiteSpace(normalized) ? "TrueBIM_Спецификация" : normalized;
    }
}
