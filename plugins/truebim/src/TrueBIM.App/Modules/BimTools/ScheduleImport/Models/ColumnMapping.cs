namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ColumnMapping(
    string SourceColumnName,
    string? TargetRevitParameterName,
    long? TargetParameterId,
    ScheduleImportDataType DataType,
    string? UnitSource,
    string? UnitTarget,
    bool IsRequired);
