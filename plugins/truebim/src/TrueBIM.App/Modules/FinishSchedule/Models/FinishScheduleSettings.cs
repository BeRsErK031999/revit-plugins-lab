namespace TrueBIM.App.Modules.FinishSchedule.Models;

public enum RoomIdentifierMode
{
    Number,
    Name,
    CustomParameter
}

public enum ReportScopeKind
{
    Level,
    Section,
    EntireProject
}

public sealed record FinishCategorySettings(
    bool IsEnabled,
    string ClassificationValue,
    ParameterReference? OwnershipParameter,
    ParameterReference? OutputDescriptionParameter,
    ParameterReference? OutputAreaParameter);

public sealed record RoomIdentifierSettings(
    RoomIdentifierMode Mode,
    ParameterReference? CustomParameter)
{
    public static RoomIdentifierSettings ByNumber()
    {
        return new RoomIdentifierSettings(RoomIdentifierMode.Number, null);
    }
}

public sealed record ReportScopeSettings(
    ReportScopeKind Kind,
    long? LevelId,
    ParameterReference? SectionParameter,
    string SectionValue)
{
    public static ReportScopeSettings EntireProject()
    {
        return new ReportScopeSettings(ReportScopeKind.EntireProject, null, null, string.Empty);
    }
}

public sealed record FinishScheduleSettings(
    ParameterReference? DescriptionParameter,
    RoomIdentifierSettings RoomIdentifier,
    bool WriteOwnership,
    FinishCategorySettings Walls,
    FinishCategorySettings Floors,
    FinishCategorySettings Ceilings,
    ParameterReference? RoomListOutputParameter,
    ReportScopeSettings Scope,
    string ScheduleName)
{
    public const string ClassificationParameterName = "Группа модели";
    public const string DefaultScheduleName = "Помещения • Ведомость отделки помещений";

    public static FinishScheduleSettings CreateDefault()
    {
        return new FinishScheduleSettings(
            DescriptionParameter: null,
            RoomIdentifier: RoomIdentifierSettings.ByNumber(),
            WriteOwnership: false,
            Walls: new FinishCategorySettings(
                IsEnabled: true,
                ClassificationValue: "Внутренняя отделка",
                OwnershipParameter: null,
                OutputDescriptionParameter: null,
                OutputAreaParameter: null),
            Floors: new FinishCategorySettings(
                IsEnabled: true,
                ClassificationValue: "Пол",
                OwnershipParameter: null,
                OutputDescriptionParameter: null,
                OutputAreaParameter: null),
            Ceilings: new FinishCategorySettings(
                IsEnabled: true,
                ClassificationValue: "Потолки",
                OwnershipParameter: null,
                OutputDescriptionParameter: null,
                OutputAreaParameter: null),
            RoomListOutputParameter: null,
            Scope: ReportScopeSettings.EntireProject(),
            ScheduleName: DefaultScheduleName);
    }
}
