using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleImport;

public sealed class ParameterMappingServiceTests
{
    [Fact]
    public void SuggestMappings_InfersPipeColumnTypes()
    {
        var table = ScheduleImportSampleTables.CreatePipeSchedule("sample.pdf");

        var mappings = new ParameterMappingService().SuggestMappings(table, ["Диаметр", "Длина"]);

        Assert.Contains(mappings, mapping => mapping.SourceColumnName == "Диаметр" && mapping.DataType == ScheduleImportDataType.Length);
        Assert.Contains(mappings, mapping => mapping.SourceColumnName == "Длина, м" && mapping.DataType == ScheduleImportDataType.Length && mapping.UnitSource == "m");
        Assert.Contains(mappings, mapping => mapping.SourceColumnName == "Количество" && mapping.DataType == ScheduleImportDataType.Count);
    }

    [Fact]
    public void SuggestMappings_UsesAvailableScheduleFields()
    {
        ParsedTable table = new(
            "source.pdf",
            1,
            Array.Empty<ParsedRow>(),
            ["System Name", "Length, m", "Unknown"],
            Array.Empty<ParsedCell>(),
            1,
            Array.Empty<string>());

        var mappings = new ParameterMappingService().SuggestMappings(table, ["Length", "System Name"]);

        Assert.Contains(mappings, mapping => mapping.SourceColumnName == "System Name" && mapping.TargetRevitParameterName == "System Name");
        Assert.Contains(mappings, mapping => mapping.SourceColumnName == "Length, m" && mapping.TargetRevitParameterName == "Length");
        Assert.Contains(mappings, mapping => mapping.SourceColumnName == "Unknown" && mapping.TargetRevitParameterName is null);
    }
}
