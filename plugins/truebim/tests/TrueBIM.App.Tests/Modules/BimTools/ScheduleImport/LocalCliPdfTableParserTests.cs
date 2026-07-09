using TrueBIM.App.Modules.BimTools.ScheduleImport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleImport;

public sealed class LocalCliPdfTableParserTests
{
    [Fact]
    public async Task ParseAsync_ReadsWorkerOutputJson()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "source.pdf");
        string scriptPath = Path.Combine(tempDirectory, "worker.ps1");
        File.WriteAllText(sourcePath, "%PDF-1.4 fake");
        File.WriteAllText(
            scriptPath,
            """
            param([string]$InputPath, [string]$OutputPath)
            if (-not (Test-Path -LiteralPath $InputPath)) { exit 3 }
            Set-Content -LiteralPath $OutputPath -Encoding UTF8 -Value @'
            {
              "tables": [
                {
                  "sourceFilePath": "source.pdf",
                  "pageNumber": 2,
                  "columns": ["Mark", "Count"],
                  "rows": [
                    ["Mark", "Count"],
                    ["A-1", "4"]
                  ],
                  "confidence": 0.91,
                  "warnings": ["check count"]
                }
              ],
              "warnings": ["fake worker"],
              "errors": []
            }
            '@
            """);

        try
        {
            LocalCliPdfTableParser runner = CreateRunner(scriptPath, tempDirectory);

            var result = await runner.ParseAsync(sourcePath, CancellationToken.None);

            Assert.Empty(result.Errors);
            Assert.Equal("fake worker", Assert.Single(result.Warnings));
            var table = Assert.Single(result.Tables);
            Assert.Equal(2, table.PageNumber);
            Assert.Equal(2, table.RowCount);
            Assert.Equal("check count", Assert.Single(table.Warnings));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ParseAsync_ReturnsErrorWhenWorkerFails()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "source.pdf");
        string scriptPath = Path.Combine(tempDirectory, "worker-fail.ps1");
        File.WriteAllText(sourcePath, "%PDF-1.4 fake");
        File.WriteAllText(
            scriptPath,
            """
            param([string]$InputPath, [string]$OutputPath)
            Write-Error "fake failure"
            exit 7
            """);

        try
        {
            LocalCliPdfTableParser runner = CreateRunner(scriptPath, tempDirectory);

            var result = await runner.ParseAsync(sourcePath, CancellationToken.None);

            string error = Assert.Single(result.Errors);
            Assert.Contains("ExitCode=7", error, StringComparison.Ordinal);
            Assert.Empty(result.Tables);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ParseAsync_ReturnsErrorWhenWorkerTimesOut()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "source.pdf");
        string scriptPath = Path.Combine(tempDirectory, "worker-timeout.ps1");
        File.WriteAllText(sourcePath, "%PDF-1.4 fake");
        File.WriteAllText(
            scriptPath,
            """
            param([string]$InputPath, [string]$OutputPath)
            Start-Sleep -Seconds 5
            """);

        try
        {
            LocalCliPdfTableParser runner = CreateRunner(
                scriptPath,
                tempDirectory,
                TimeSpan.FromMilliseconds(200));

            var result = await runner.ParseAsync(sourcePath, CancellationToken.None);

            string error = Assert.Single(result.Errors);
            Assert.Contains("timed out", error, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(result.Tables);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static LocalCliPdfTableParser CreateRunner(
        string scriptPath,
        string tempDirectory,
        TimeSpan? timeout = null)
    {
        return new LocalCliPdfTableParser(
            new LocalCliPdfTableParserOptions
            {
                PythonExecutablePath = ResolvePowerShellPath(),
                WorkerScriptPath = scriptPath,
                ArgumentsTemplate = "-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -InputPath \"{source}\" -OutputPath \"{output}\"",
                Timeout = timeout ?? TimeSpan.FromSeconds(10),
                TempRootDirectory = tempDirectory
            },
            new ScheduleTableJsonReader());
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TrueBIM-ScheduleImport-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ResolvePowerShellPath()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        return File.Exists(path)
            ? path
            : "powershell.exe";
    }
}
