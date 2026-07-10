using TrueBIM.App.Modules.BimTools.ScheduleImport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleImport;

public sealed class PdfGridTableParserTests
{
    [Fact]
    public async Task ParseAsync_ExtractsConfiguredVectorScheduleSample()
    {
        string? path = Environment.GetEnvironmentVariable("TRUEBIM_TEST_SCHEDULE_PDF");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        PdfGridTableParser parser = new(new GridTableExtractionService());

        var result = await parser.ParseAsync(path, CancellationToken.None);

        Assert.Empty(result.Errors);
        var table = Assert.Single(result.Tables);
        Assert.Equal(9, table.ColumnCount);
        Assert.Equal(17, table.RowCount);
        Assert.Contains("Наименование", table.Rows[0].Values[1], StringComparison.CurrentCulture);
        Assert.Contains("Трубопроводы", table.Rows[1].Values[1], StringComparison.CurrentCulture);
        Assert.Contains("Ø18х1.8", table.Rows[^1].Values[1], StringComparison.CurrentCulture);
    }
}
