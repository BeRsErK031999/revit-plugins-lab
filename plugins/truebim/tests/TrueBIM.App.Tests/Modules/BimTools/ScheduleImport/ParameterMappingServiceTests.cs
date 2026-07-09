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
}
