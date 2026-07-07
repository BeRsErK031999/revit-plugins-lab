using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldRecognitionRunnerTests
{
    [Fact]
    public void StubRecognitionRunner_ReturnsEmptyResult()
    {
        StubIsoFieldRecognitionRunner runner = new();

        var result = runner.Run(sourcePath: null);

        Assert.Empty(result.Polylines);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void IsoFieldRecognitionRunnerFactory_UsesStubWhenCliWorkerIsNotConfigured()
    {
        string? previousWorker = Environment.GetEnvironmentVariable(IsoFieldRecognitionRunnerFactory.WorkerPathEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(IsoFieldRecognitionRunnerFactory.WorkerPathEnvironmentVariable, null);

            IIsoFieldRecognitionRunner runner = IsoFieldRecognitionRunnerFactory.Create(new TestLogger());

            Assert.IsType<StubIsoFieldRecognitionRunner>(runner);
        }
        finally
        {
            Environment.SetEnvironmentVariable(IsoFieldRecognitionRunnerFactory.WorkerPathEnvironmentVariable, previousWorker);
        }
    }

    [Fact]
    public void IsoFieldCliRecognitionRunner_ReadsWorkerOutputJson()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "source.png");
        string scriptPath = Path.Combine(tempDirectory, "worker.ps1");
        File.WriteAllText(sourcePath, "fake image");
        File.WriteAllText(
            scriptPath,
            """
            param([string]$Request, [string]$Output)
            if (-not (Test-Path -LiteralPath $Request)) { exit 3 }
            Set-Content -LiteralPath $Output -Encoding UTF8 -Value @'
            {
              "schemaVersion": "1.0",
              "polylines": [
                {
                  "id": "zone-cli",
                  "zoneName": "CLI Zone",
                  "confidence": 0.88,
                  "points": [
                    { "x": 1.0, "y": 2.0 },
                    { "x": 3.0, "y": 4.0 }
                  ]
                }
              ],
              "diagnostics": [ "fake worker" ]
            }
            '@
            """);

        try
        {
            IsoFieldCliRecognitionRunner runner = CreateCliRunner(scriptPath, tempDirectory);

            var result = runner.Run(sourcePath);

            var polyline = Assert.Single(result.Polylines);
            Assert.Equal("zone-cli", polyline.Id);
            Assert.Equal("CLI Zone", polyline.ZoneName);
            Assert.Equal(0.88, polyline.Confidence);
            Assert.Equal("fake worker", Assert.Single(result.Diagnostics));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void IsoFieldCliRecognitionRunner_ThrowsWhenWorkerFails()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "source.png");
        string scriptPath = Path.Combine(tempDirectory, "worker-fail.ps1");
        File.WriteAllText(sourcePath, "fake image");
        File.WriteAllText(
            scriptPath,
            """
            param([string]$Request, [string]$Output)
            Write-Error "fake failure"
            exit 7
            """);

        try
        {
            IsoFieldCliRecognitionRunner runner = CreateCliRunner(scriptPath, tempDirectory);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => runner.Run(sourcePath));

            Assert.Contains("ExitCode=7", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void IsoFieldCliRecognitionRunner_TimesOutWorker()
    {
        string tempDirectory = CreateTempDirectory();
        string sourcePath = Path.Combine(tempDirectory, "source.png");
        string scriptPath = Path.Combine(tempDirectory, "worker-timeout.ps1");
        File.WriteAllText(sourcePath, "fake image");
        File.WriteAllText(
            scriptPath,
            """
            param([string]$Request, [string]$Output)
            Start-Sleep -Seconds 5
            """);

        try
        {
            IsoFieldCliRecognitionRunner runner = CreateCliRunner(
                scriptPath,
                tempDirectory,
                TimeSpan.FromMilliseconds(200));

            Assert.Throws<TimeoutException>(() => runner.Run(sourcePath));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void IsoFieldFilePicker_ImplementsPickerContract()
    {
        Assert.IsAssignableFrom<IIsoFieldFilePicker>(new IsoFieldFilePicker());
    }

    [Fact]
    public void IsoFieldJsonReader_ReadsValidRecognitionResult()
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(
            filePath,
            """
            {
              "schemaVersion": "1.0",
              "polylines": [
                {
                  "id": "zone-a",
                  "zoneName": "Zone A",
                  "confidence": 0.9,
                  "points": [
                    { "x": 10.0, "y": 20.0 },
                    { "x": 30.0, "y": 40.0 }
                  ]
                }
              ],
              "diagnostics": [
                "sample"
              ]
            }
            """);

        try
        {
            var result = new IsoFieldJsonReader().Read(filePath);

            var polyline = Assert.Single(result.Polylines);
            Assert.Equal("zone-a", polyline.Id);
            Assert.Equal("Zone A", polyline.ZoneName);
            Assert.Equal(0.9, polyline.Confidence);
            Assert.Equal(2, polyline.Points.Count);
            Assert.Equal(10.0, polyline.Points[0].X);
            Assert.Equal(20.0, polyline.Points[0].Y);
            Assert.Equal("sample", Assert.Single(result.Diagnostics));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void IsoFieldJsonReader_RejectsPolylineWithoutEnoughPoints()
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(
            filePath,
            """
            {
              "schemaVersion": "1.0",
              "polylines": [
                {
                  "id": "zone-a",
                  "points": [
                    { "x": 10.0, "y": 20.0 }
                  ]
                }
              ]
            }
            """);

        try
        {
            Assert.Throws<InvalidDataException>(() => new IsoFieldJsonReader().Read(filePath));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static IsoFieldCliRecognitionRunner CreateCliRunner(
        string scriptPath,
        string tempDirectory,
        TimeSpan? timeout = null)
    {
        return new IsoFieldCliRecognitionRunner(
            new IsoFieldCliRecognitionRunnerOptions
            {
                ExecutablePath = ResolvePowerShellPath(),
                ArgumentsTemplate = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -Request \"{{request}}\" -Output \"{{output}}\"",
                Timeout = timeout ?? TimeSpan.FromSeconds(10),
                TempRootDirectory = tempDirectory
            },
            new IsoFieldJsonReader());
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TrueBIM-IsoField-{Guid.NewGuid():N}");
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

    private sealed class TestLogger : TrueBIM.App.Services.Logging.ITrueBimLogger
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
