using TrueBIM.App.Modules.BimTools.ScheduleImport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleImport;

public sealed class JsonOrSamplePdfTableParserTests
{
    [Fact]
    public async Task ParseAsync_ReturnsDwgPreflightWarningWithoutParserError()
    {
        JsonOrSamplePdfTableParser parser = new(new ScheduleTableJsonReader());

        var result = await parser.ParseAsync(@"C:\Project\schedule.dwg", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Tables);
        Assert.Empty(result.Errors);
        string warning = Assert.Single(result.Warnings);
        Assert.Contains("DWG", warning, StringComparison.Ordinal);
        Assert.Contains("PDF", warning, StringComparison.Ordinal);
        Assert.Contains("JSON", warning, StringComparison.Ordinal);
    }
}
