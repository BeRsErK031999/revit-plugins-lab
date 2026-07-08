using System.Text;
using TrueBIM.App.Modules.BimTools.Common.Models;
using TrueBIM.App.Modules.BimTools.Common.Services.Export;
using TrueBIM.App.Modules.BimTools.Common.Services.Reports;
using TrueBIM.App.Modules.BimTools.Common.Services.Storage;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.Common;

public sealed class BimToolCommonServicesTests
{
    [Fact]
    public void JsonSettingsStorage_SaveAndLoad_RoundTripsState()
    {
        using TempDirectory temp = new();
        string settingsPath = Path.Combine(temp.Path, "settings.json");
        JsonSettingsStorage storage = new(new TestLogger());
        BimToolShellState state = new()
        {
            ToolTitle = "Экспорт PDF/DWG",
            DocumentTitle = "Project.rvt",
            LastOpenedAtUtc = DateTimeOffset.Parse("2026-07-08T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            PreviewRequestCount = 2
        };

        storage.Save(settingsPath, state);
        BimToolShellState reloaded = storage.LoadOrDefault(settingsPath, () => new BimToolShellState());

        Assert.Equal("Экспорт PDF/DWG", reloaded.ToolTitle);
        Assert.Equal("Project.rvt", reloaded.DocumentTitle);
        Assert.Equal(2, reloaded.PreviewRequestCount);
    }

    [Fact]
    public void JsonSettingsStorage_CreateDefaultSettingsPath_UsesBimToolsFolder()
    {
        string settingsPath = JsonSettingsStorage.CreateDefaultSettingsPath("batch-export");

        Assert.EndsWith(Path.Combine("TrueBIM", "BimTools", "batch-export", "settings.json"), settingsPath);
    }

    [Fact]
    public void CsvExportService_Format_EscapesDelimiterQuotesAndLineBreaks()
    {
        CsvExportService service = new();

        string csv = service.Format(
            ["Name", "Message"],
            [
                ["A-101", "Plan; \"Main\""],
                ["A-102", "Line 1\nLine 2"]
            ]);

        Assert.Contains("Name;Message", csv);
        Assert.Contains("A-101;\"Plan; \"\"Main\"\"\"", csv);
        Assert.Contains("A-102;\"Line 1\nLine 2\"", csv);
    }

    [Fact]
    public void CsvExportService_WriteUtf8WithBom_WritesBom()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "report.csv");
        CsvExportService service = new();

        service.WriteUtf8WithBom(path, "Name;Status");

        byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
        Assert.Equal("Name;Status", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
    }

    [Fact]
    public void ReportService_FormatCsv_IncludesScaffoldReport()
    {
        ReportService reportService = new();
        CsvExportService csvExportService = new();

        BimReport report = reportService.CreateScaffoldReport("Автомарки", "Project.rvt", "Предпросмотр");
        string csv = reportService.FormatCsv(report, csvExportService);

        Assert.Contains("CreatedAtUtc;Title;Scope;Status;Message", csv);
        Assert.Contains("Автомарки;Предпросмотр;Каркас", csv);
        Assert.Contains("Project.rvt", csv);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "truebim-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class TestLogger : ITrueBimLogger
    {
        public void Info(string message)
        {
        }

        public void Warning(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
