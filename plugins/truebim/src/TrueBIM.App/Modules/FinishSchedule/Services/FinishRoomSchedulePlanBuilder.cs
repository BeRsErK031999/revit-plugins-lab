using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishRoomSchedulePlanBuilder
{
    private const double RoomListWidth = 40;
    private const double DescriptionWidth = 80;
    private const double AreaWidth = 25;

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

        List<FinishRoomScheduleColumn> columns =
        [
            Column(settings.RoomListOutputParameter, "Перечень помещений", RoomListWidth, "перечень помещений")
        ];
        AddCategory(columns, settings.Walls, "Стены или перегородки");
        AddCategory(columns, settings.Ceilings, "Потолок");
        AddCategory(columns, settings.Floors, "Пол");

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
        string heading)
    {
        if (!settings.IsEnabled)
        {
            return;
        }

        columns.Add(Column(settings.OutputDescriptionParameter, heading, DescriptionWidth, $"{heading}: описание"));
        columns.Add(Column(settings.OutputAreaParameter, "Площадь, м²", AreaWidth, $"{heading}: площадь"));
    }

    private static FinishRoomScheduleColumn Column(
        ParameterReference? parameter,
        string heading,
        double width,
        string role)
    {
        return new FinishRoomScheduleColumn(
            parameter ?? throw new InvalidOperationException($"Не выбран параметр «{role}»."),
            heading,
            width);
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
        canonical.Append("v1|").Append(scheduleName.Trim()).Append('|');
        foreach (FinishRoomScheduleColumn column in columns)
        {
            canonical.Append(column.Parameter.StableKey)
                .Append('|')
                .Append(column.Heading)
                .Append('|')
                .Append(column.WidthMillimeters.ToString("R", CultureInfo.InvariantCulture))
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
