using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishRoomSchedulePlanBuilder
{
    public const long RoomCommentsBuiltInParameterId = -1010106;
    public const double NoteWidthMillimeters = 30;

    public FinishRoomSchedulePlan Build(
        FinishScheduleSettings settings,
        IReadOnlyList<FinishRoomCandidateSnapshot> selectedRooms)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (selectedRooms is null)
        {
            throw new ArgumentNullException(nameof(selectedRooms));
        }

        FinishScheduleColumnWidths widths = settings.EffectiveColumnWidths;
        ValidateWidths(widths);
        List<FinishRoomScheduleColumn> columns =
        [
            Column(
                settings.RoomListOutputParameter,
                FinishRoomScheduleStyleRules.RoomHeaderText,
                widths.RoomListMillimeters,
                "перечень помещений",
                FinishRoomScheduleColumnKind.RoomList)
        ];
        AddCategory(columns, settings.Walls, "Стены или перегородки", widths);
        AddCategory(columns, settings.Ceilings, "Потолок", widths);
        AddCategory(columns, settings.Floors, "Пол", widths);
        columns.Add(new FinishRoomScheduleColumn(
            ParameterReference.BuiltIn(
                "Комментарии",
                RoomCommentsBuiltInParameterId,
                ParameterBindingKind.Instance,
                ParameterStorageKind.String),
            FinishRoomScheduleStyleRules.NoteHeaderText,
            NoteWidthMillimeters,
            FinishRoomScheduleColumnKind.Note));

        FinishRoomScheduleScopeFilter scopeFilter = BuildScopeFilter(settings.Scope, selectedRooms);
        string[] parameterIdentities = columns
            .Select(column => column.Parameter.StableKey)
            .Concat(scopeFilter.Parameter is null ? [] : [scopeFilter.Parameter.StableKey])
            .ToArray();
        string settingsHash = ComputeHash(settings.ScheduleName, columns, scopeFilter);
        return new FinishRoomSchedulePlan(
            settings.ScheduleName,
            columns,
            scopeFilter,
            settingsHash,
            parameterIdentities);
    }

    private static void AddCategory(
        List<FinishRoomScheduleColumn> columns,
        FinishCategorySettings settings,
        string heading,
        FinishScheduleColumnWidths widths)
    {
        if (!settings.IsEnabled)
        {
            return;
        }

        columns.Add(Column(
            settings.OutputDescriptionParameter,
            heading,
            widths.DescriptionMillimeters,
            $"{heading}: описание",
            FinishRoomScheduleColumnKind.Description));
        columns.Add(Column(
            settings.OutputAreaParameter,
            "Площадь, м²",
            widths.AreaMillimeters,
            $"{heading}: площадь",
            FinishRoomScheduleColumnKind.Area));
    }

    private static void ValidateWidths(FinishScheduleColumnWidths widths)
    {
        ValidateWidth(widths.RoomListMillimeters, "перечня помещений");
        ValidateWidth(widths.DescriptionMillimeters, "описания отделки");
        ValidateWidth(widths.AreaMillimeters, "площади");
    }

    private static void ValidateWidth(double width, string role)
    {
        if (double.IsNaN(width)
            || double.IsInfinity(width)
            || width < FinishScheduleColumnWidths.MinimumMillimeters
            || width > FinishScheduleColumnWidths.MaximumMillimeters)
        {
            throw new InvalidOperationException(
                $"Ширина столбца {role} должна быть от "
                    + $"{FinishScheduleColumnWidths.MinimumMillimeters:0} до "
                    + $"{FinishScheduleColumnWidths.MaximumMillimeters:0} мм.");
        }
    }

    private static FinishRoomScheduleColumn Column(
        ParameterReference? parameter,
        string heading,
        double width,
        string role,
        FinishRoomScheduleColumnKind kind)
    {
        return new FinishRoomScheduleColumn(
            parameter ?? throw new InvalidOperationException($"Не выбран параметр «{role}»."),
            heading,
            width,
            kind);
    }

    private static FinishRoomScheduleScopeFilter BuildScopeFilter(
        ReportScopeSettings scope,
        IReadOnlyList<FinishRoomCandidateSnapshot> selectedRooms)
    {
        return scope.Kind switch
        {
            ReportScopeKind.EntireProject => FinishRoomScheduleScopeFilter.EntireProject(),
            ReportScopeKind.Level when scope.LevelId.HasValue => new FinishRoomScheduleScopeFilter(
                ReportScopeKind.Level,
                null,
                ParameterStorageKind.ElementId,
                scope.LevelId.Value.ToString(CultureInfo.InvariantCulture)),
            ReportScopeKind.Section when scope.SectionParameter is not null => new FinishRoomScheduleScopeFilter(
                ReportScopeKind.Section,
                scope.SectionParameter,
                scope.SectionParameter.StorageKind,
                ReadSectionRawValue(scope, selectedRooms)),
            _ => throw new InvalidOperationException("Область ведомости отделки настроена не полностью.")
        };
    }

    private static string ReadSectionRawValue(
        ReportScopeSettings scope,
        IReadOnlyList<FinishRoomCandidateSnapshot> selectedRooms)
    {
        string[] values = selectedRooms
            .Select(room => room.TryGetParameterValue(scope.SectionParameter!, out FinishParameterValueSnapshot? value)
                ? value
                : null)
            .Where(value => value is not null && value.Matches(scope.SectionValue))
            .Select(value => value!.RawValue)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (values.Length != 1)
        {
            throw new InvalidOperationException(
                "Не удалось получить одно однозначное значение параметра раздела для фильтра спецификации.");
        }

        return values[0];
    }

    private static string ComputeHash(
        string scheduleName,
        IReadOnlyList<FinishRoomScheduleColumn> columns,
        FinishRoomScheduleScopeFilter scopeFilter)
    {
        StringBuilder canonical = new();
        canonical.Append(FinishRoomScheduleStyleRules.LayoutRevision)
            .Append('|')
            .Append(scheduleName.Trim())
            .Append('|');
        foreach (FinishRoomScheduleColumn column in columns)
        {
            canonical.Append(column.Parameter.StableKey)
                .Append('|')
                .Append(column.Heading)
                .Append('|')
                .Append(column.WidthMillimeters.ToString("R", CultureInfo.InvariantCulture))
                .Append('|')
                .Append(column.Kind)
                .Append('|');
        }

        canonical.Append(scopeFilter.Kind)
            .Append('|')
            .Append(scopeFilter.Parameter?.StableKey ?? string.Empty)
            .Append('|')
            .Append(scopeFilter.StorageKind)
            .Append('|')
            .Append(scopeFilter.RawValue);
        using SHA256 sha256 = SHA256.Create();
        byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
        return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
    }
}
