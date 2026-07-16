using TrueBIM.App.Modules.BimTools.ScheduleImport.Services;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleImport;

public sealed class ScheduleImportParserFactoryTests
{
    [Fact]
    public void Create_ConfiguresInProcessPdfAndDwgParsers()
    {
        TestLogger logger = new();

        IPdfTableParser parser = ScheduleImportParserFactory.Create(logger);

        Assert.IsType<ScheduleSourceTableParser>(parser);
        Assert.Contains(
            "Schedule Import in-process PDF/DWG parsers configured.",
            logger.InfoMessages);
    }

    private sealed class TestLogger : ITrueBimLogger
    {
        public List<string> InfoMessages { get; } = [];

        public void Info(string message)
        {
            InfoMessages.Add(message);
        }

        public void Warning(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
