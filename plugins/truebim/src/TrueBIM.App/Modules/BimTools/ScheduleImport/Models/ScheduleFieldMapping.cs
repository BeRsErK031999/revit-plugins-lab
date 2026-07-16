namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ScheduleFieldMapping(
    string SourceColumnName,
    string TargetFieldKey,
    string TargetFieldName,
    long TargetParameterId,
    int TargetFieldTypeValue,
    ScheduleFilterRule FilterRule,
    string? FilterValue);
