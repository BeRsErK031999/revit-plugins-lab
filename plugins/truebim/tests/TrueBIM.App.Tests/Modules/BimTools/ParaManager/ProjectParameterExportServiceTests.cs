using TrueBIM.App.Modules.BimTools.ParaManager.Models;
using TrueBIM.App.Modules.BimTools.ParaManager.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ParaManager;

public sealed class ProjectParameterExportServiceTests
{
    [Fact]
    public void BuildCsvIncludesHeadersAndRows()
    {
        ProjectParameterExportService service = new();
        ProjectParameterRow row = new(
            "BIM_Раздел",
            "Text",
            "Instance",
            "Walls, Doors",
            "Identity Data",
            isShared: true,
            "d4e8a6d3-7d91-4f82-a8bf-4dbdf05f6b25");

        string csv = service.BuildCsv([row]);

        Assert.Contains("ParameterName;BindingType;Categories;GroupUnder;DataType;IsShared;Guid", csv);
        Assert.Contains("BIM_Раздел;Instance;Walls, Doors;Identity Data;Text;true;d4e8a6d3-7d91-4f82-a8bf-4dbdf05f6b25", csv);
    }

    [Fact]
    public void BuildCsvEscapesSemicolonsQuotesAndLineBreaks()
    {
        ProjectParameterExportService service = new();
        ProjectParameterRow row = new(
            "BIM_Параметр; \"тест\"",
            "Text",
            "Instance",
            "Walls\r\nDoors",
            "Identity Data",
            isShared: false,
            string.Empty);

        string csv = service.BuildCsv([row]);

        Assert.Contains("\"BIM_Параметр; \"\"тест\"\"\"", csv);
        Assert.Contains("\"Walls\r\nDoors\"", csv);
        Assert.Contains(";false;", csv);
    }
}
