namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ScheduleFieldOption(
    string Key,
    string Name,
    string DisplayName,
    long ParameterId,
    int FieldTypeValue,
    string FieldTypeName);
