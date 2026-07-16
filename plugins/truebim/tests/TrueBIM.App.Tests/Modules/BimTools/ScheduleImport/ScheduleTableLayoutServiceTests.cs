using TrueBIM.App.Modules.BimTools.ScheduleImport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleImport;

public sealed class ScheduleTableLayoutServiceTests
{
    [Fact]
    public void CreateLayout_UsesReadableMinimumSizes()
    {
        var table = ScheduleImportSampleTables.CreatePipeSchedule("sample.pdf");

        var layout = new ScheduleTableLayoutService().CreateLayout(table, 1.0);

        Assert.Equal(table.ColumnCount, layout.ColumnWidthsFeet.Count);
        Assert.Equal(table.RowCount, layout.RowHeightsFeet.Count);
        Assert.All(layout.ColumnWidthsFeet, width => Assert.True(width >= ScheduleTableLayoutService.ToFeet(24)));
        Assert.True(layout.WidthFeet > 0);
        Assert.True(layout.HeightFeet > 0);
    }

    [Fact]
    public void CreateLayout_ClampsInvalidScaleToDefault()
    {
        var table = ScheduleImportSampleTables.CreatePipeSchedule("sample.pdf");
        var service = new ScheduleTableLayoutService();

        var defaultLayout = service.CreateLayout(table, 1.0);
        var invalidLayout = service.CreateLayout(table, double.NaN);

        Assert.Equal(defaultLayout.WidthFeet, invalidLayout.WidthFeet, 6);
        Assert.Equal(defaultLayout.HeightFeet, invalidLayout.HeightFeet, 6);
    }
}
