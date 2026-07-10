using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleImport;

public sealed class ScheduleSourceTableParserTests
{
    [Fact]
    public async Task ParseAsync_RoutesDwgToDwgParser()
    {
        StubParser pdf = new("pdf");
        StubParser dwg = new("dwg");
        ScheduleSourceTableParser parser = new(pdf, dwg, new ScheduleTableJsonReader());

        var result = await parser.ParseAsync(@"C:\Project\schedule.dwg", CancellationToken.None);

        Assert.Equal("dwg", Assert.Single(result.Warnings));
        Assert.Equal(0, pdf.CallCount);
        Assert.Equal(1, dwg.CallCount);
    }

    [Fact]
    public async Task ParseAsync_RejectsUnsupportedExtension()
    {
        ScheduleSourceTableParser parser = new(
            new StubParser("pdf"),
            new StubParser("dwg"),
            new ScheduleTableJsonReader());

        var result = await parser.ParseAsync(@"C:\Project\schedule.xlsx", CancellationToken.None);

        Assert.Empty(result.Tables);
        Assert.Contains("PDF", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    private sealed class StubParser : IPdfTableParser
    {
        private readonly string marker;

        public StubParser(string marker)
        {
            this.marker = marker;
        }

        public int CallCount { get; private set; }

        public Task<PdfParserResult> ParseAsync(string filePath, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new PdfParserResult(
                Array.Empty<ParsedTable>(),
                [marker],
                Array.Empty<string>()));
        }
    }
}
