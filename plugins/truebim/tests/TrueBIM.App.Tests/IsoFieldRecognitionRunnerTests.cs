using System.Windows.Media;
using System.Windows.Media.Imaging;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using TrueBIM.App.Modules.IsoFieldRebar.Models;
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
        Assert.Equal("Stub", Assert.IsAssignableFrom<IIsoFieldRecognitionRunnerDiagnostics>(runner).RunnerName);
    }

    [Fact]
    public void IsoFieldRecognitionRunnerFactory_UsesBuiltInWhenCliWorkerIsNotConfigured()
    {
        string? previousWorker = Environment.GetEnvironmentVariable(IsoFieldRecognitionRunnerFactory.WorkerPathEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(IsoFieldRecognitionRunnerFactory.WorkerPathEnvironmentVariable, null);

            IIsoFieldRecognitionRunner runner = IsoFieldRecognitionRunnerFactory.Create(new TestLogger());

            Assert.IsType<BuiltInIsoFieldRecognitionRunner>(runner);
            Assert.Equal("Встроенный", Assert.IsAssignableFrom<IIsoFieldRecognitionRunnerDiagnostics>(runner).RunnerName);
        }
        finally
        {
            Environment.SetEnvironmentVariable(IsoFieldRecognitionRunnerFactory.WorkerPathEnvironmentVariable, previousWorker);
        }
    }

    [Fact]
    public void BuiltInRecognitionRunner_ExtractsLegendAndDenseColorZones()
    {
        string directory = CreateTempDirectory();
        string imagePath = Path.Combine(directory, "pk-lira-map.png");
        try
        {
            CreateSyntheticPkLiraMap(imagePath);

            IsoFieldRecognitionResult result = new BuiltInIsoFieldRecognitionRunner().Run(imagePath);

            IsoFieldLegend legend = Assert.Single(result.EffectiveLegends);
            Assert.Equal(3, legend.Bands.Count);
            Assert.Equal("#FFFF64", legend.Bands[0].HexColor);
            Assert.Equal("#FF0000", legend.Bands[2].HexColor);
            Assert.True(legend.HasNumericRanges);
            Assert.Equal(0.5, legend.Bands[0].MinimumValue);
            Assert.Equal(1.5, legend.Bands[0].MaximumValue);
            Assert.Equal(2.5, legend.Bands[2].MinimumValue);
            Assert.Equal(3.5, legend.Bands[2].MaximumValue);
            Assert.True(legend.HasReinforcementLabels);
            Assert.Equal(4, legend.EffectiveBoundaries.Count);
            Assert.Equal("d10s200", legend.EffectiveBoundaries[0].ReinforcementLabel);
            Assert.Equal("d10s200+d14s200", legend.EffectiveBoundaries[3].ReinforcementLabel);
            Assert.All(legend.EffectiveBoundaries, boundary => Assert.True(boundary.LabelConfidence >= 0.82));
            Assert.Equal(2, result.Polylines.Count);
            Assert.Contains(result.Polylines, zone => zone.ZoneName!.Contains("0,5–1,5 см²/м", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Polylines, zone => zone.ZoneName!.Contains("2,5–3,5 см²/м", StringComparison.OrdinalIgnoreCase));
            Assert.All(result.Polylines, zone => Assert.True(zone.Points.Count >= 4));
            Assert.Contains(result.Diagnostics, message => message.Contains("максимальный уровень", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Diagnostics, message => message.Contains("числовые границы", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Diagnostics, message => message.Contains("диаметр/шаг", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BuiltInRecognitionRunner_ExplainsMissingLegend()
    {
        string directory = CreateTempDirectory();
        string imagePath = Path.Combine(directory, "blank.png");
        try
        {
            CreateBlankPng(imagePath, 180, 120);

            IsoFieldRecognitionResult result = new BuiltInIsoFieldRecognitionRunner().Run(imagePath);

            Assert.Empty(result.Polylines);
            Assert.Empty(result.EffectiveLegends);
            Assert.Contains(result.Diagnostics, message => message.Contains("шкала не найдена", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BuiltInRecognitionRunner_KeepsColorOnlyFallbackWhenNumbersAreMissing()
    {
        string directory = CreateTempDirectory();
        string imagePath = Path.Combine(directory, "pk-lira-map-without-labels.png");
        try
        {
            CreateSyntheticPkLiraMap(imagePath, includeNumericLabels: false);

            IsoFieldRecognitionResult result = new BuiltInIsoFieldRecognitionRunner().Run(imagePath);

            IsoFieldLegend legend = Assert.Single(result.EffectiveLegends);
            Assert.False(legend.HasNumericRanges);
            Assert.All(legend.Bands, band =>
            {
                Assert.Null(band.MinimumValue);
                Assert.Null(band.MaximumValue);
            });
            Assert.Contains(result.Polylines, zone => zone.ZoneName!.Contains("уровень", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Diagnostics, message => message.Contains("не полностью", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BuiltInRecognitionRunner_RejectsNonIncreasingNumericScale()
    {
        string directory = CreateTempDirectory();
        string imagePath = Path.Combine(directory, "pk-lira-map-with-invalid-scale.png");
        try
        {
            CreateSyntheticPkLiraMap(imagePath, numericLabels: ["0.5", "2.5", "1.5", "3.5"]);

            IsoFieldRecognitionResult result = new BuiltInIsoFieldRecognitionRunner().Run(imagePath);

            IsoFieldLegend legend = Assert.Single(result.EffectiveLegends);
            Assert.False(legend.HasNumericRanges);
            Assert.Contains(result.Diagnostics, message => message.Contains("не полностью", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BuiltInRecognitionRunner_RejectsIncompleteReinforcementLabels()
    {
        string directory = CreateTempDirectory();
        string imagePath = Path.Combine(directory, "pk-lira-map-without-reinforcement-labels.png");
        try
        {
            CreateSyntheticPkLiraMap(imagePath, includeReinforcementLabels: false);

            IsoFieldRecognitionResult result = new BuiltInIsoFieldRecognitionRunner().Run(imagePath);

            IsoFieldLegend legend = Assert.Single(result.EffectiveLegends);
            Assert.True(legend.HasNumericRanges);
            Assert.False(legend.HasReinforcementLabels);
            Assert.All(legend.EffectiveBoundaries, boundary =>
            {
                Assert.Null(boundary.ReinforcementLabel);
                Assert.Null(boundary.LabelConfidence);
            });
            Assert.Contains(result.Diagnostics, message => message.Contains("не были приняты", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void IsoFieldRecognitionRunnerFactory_UsesCliWhenWorkerIsConfigured()
    {
        string? previousWorker = Environment.GetEnvironmentVariable(IsoFieldRecognitionRunnerFactory.WorkerPathEnvironmentVariable);
        string? previousArguments = Environment.GetEnvironmentVariable(IsoFieldRecognitionRunnerFactory.WorkerArgumentsEnvironmentVariable);
        string? previousTimeout = Environment.GetEnvironmentVariable(IsoFieldRecognitionRunnerFactory.WorkerTimeoutEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(IsoFieldRecognitionRunnerFactory.WorkerPathEnvironmentVariable, ResolvePowerShellPath());
            Environment.SetEnvironmentVariable(IsoFieldRecognitionRunnerFactory.WorkerArgumentsEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(IsoFieldRecognitionRunnerFactory.WorkerTimeoutEnvironmentVariable, "2");

            IIsoFieldRecognitionRunner runner = IsoFieldRecognitionRunnerFactory.Create(new TestLogger());

            Assert.IsType<IsoFieldCliRecognitionRunner>(runner);
            Assert.Equal("CLI", Assert.IsAssignableFrom<IIsoFieldRecognitionRunnerDiagnostics>(runner).RunnerName);
        }
        finally
        {
            Environment.SetEnvironmentVariable(IsoFieldRecognitionRunnerFactory.WorkerPathEnvironmentVariable, previousWorker);
            Environment.SetEnvironmentVariable(IsoFieldRecognitionRunnerFactory.WorkerArgumentsEnvironmentVariable, previousArguments);
            Environment.SetEnvironmentVariable(IsoFieldRecognitionRunnerFactory.WorkerTimeoutEnvironmentVariable, previousTimeout);
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
                  "layerRole": "As1X",
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
            Assert.Equal(IsoFieldLayerRole.As1X, polyline.LayerRole);
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

    [Fact]
    public void IsoFieldJsonReader_ReadsDocumentedExampleFiles()
    {
        string examplesDirectory = Path.Combine(
            FindRepositoryRoot(),
            "docs",
            "IsoFieldRebar",
            "examples");
        string[] examplePaths = Directory.GetFiles(examplesDirectory, "*.json");

        Assert.NotEmpty(examplePaths);
        foreach (string examplePath in examplePaths)
        {
            var result = new IsoFieldJsonReader().Read(examplePath);

            Assert.NotEmpty(result.Polylines);
            Assert.All(result.Polylines, polyline => Assert.True(polyline.Points.Count >= 2));
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

    private static void CreateSyntheticPkLiraMap(
        string path,
        bool includeNumericLabels = true,
        IReadOnlyList<string>? numericLabels = null,
        bool includeReinforcementLabels = true)
    {
        const int width = 420;
        const int height = 180;
        int stride = width * 4;
        byte[] pixels = CreateWhitePixels(width, height);
        int[] centers = [60, 160, 260, 360];
        FillRectangle(pixels, stride, 60, 20, 100, 12, 255, 255, 100);
        FillRectangle(pixels, stride, 160, 20, 100, 12, 0, 255, 0);
        FillRectangle(pixels, stride, 260, 20, 100, 12, 255, 0, 0);
        if (includeReinforcementLabels)
        {
            string[] reinforcementLabels =
            [
                "d10s200",
                "d10s200+d10s200",
                "d10s200+d12s200",
                "d10s200+d14s200"
            ];
            for (int index = 0; index < centers.Length; index++)
            {
                DrawReinforcementLabel(pixels, stride, centers[index], 6, reinforcementLabels[index]);
            }
        }

        if (includeNumericLabels)
        {
            IReadOnlyList<string> labels = numericLabels ?? ["0.5", "1.5", "2.5", "3.5"];
            for (int index = 0; index < centers.Length; index++)
            {
                int textWidth = GetRasterTextWidth(labels[index]);
                DrawRasterText(pixels, stride, centers[index] - (textWidth / 2), 39, labels[index]);
            }
        }

        FillRectangle(pixels, stride, 85, 82, 28, 24, 255, 255, 100);
        FillRectangle(pixels, stride, 290, 118, 32, 26, 255, 0, 0);
        SavePng(path, width, height, pixels, stride);
    }

    private static void CreateBlankPng(string path, int width, int height)
    {
        int stride = width * 4;
        SavePng(path, width, height, CreateWhitePixels(width, height), stride);
    }

    private static byte[] CreateWhitePixels(int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 255;
            pixels[index + 1] = 255;
            pixels[index + 2] = 255;
            pixels[index + 3] = 255;
        }

        return pixels;
    }

    private static int GetRasterTextWidth(string text)
    {
        return text.Sum(character => GetRasterGlyph(character)[0].Length + 2) - 2;
    }

    private static void DrawRasterText(byte[] pixels, int stride, int x, int y, string text)
    {
        int cursorX = x;
        foreach (char character in text)
        {
            string[] rows = GetRasterGlyph(character);
            for (int row = 0; row < rows.Length; row++)
            {
                for (int column = 0; column < rows[row].Length; column++)
                {
                    if (rows[row][column] == '#')
                    {
                        FillRectangle(pixels, stride, cursorX + column, y + row, 1, 1, 0, 0, 0);
                    }
                }
            }

            cursorX += rows[0].Length + 2;
        }
    }

    private static void DrawReinforcementLabel(
        byte[] pixels,
        int stride,
        int centerX,
        int y,
        string text)
    {
        int[] offsets = text.Length == 7
            ? [0, 7, 14, 20, 26, 34, 41]
            : [0, 7, 14, 20, 26, 34, 41, 48, 55, 62, 69, 75, 81, 89, 96];
        int width = text.Length == 7 ? 46 : 101;
        int left = centerX - (width / 2);
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            string[] rows = GetRasterGlyph(character);
            int glyphTop = character == 's' ? 3 : character == '+' ? 2 : 0;
            for (int row = 0; row < rows.Length; row++)
            {
                for (int column = 0; column < rows[row].Length; column++)
                {
                    if (rows[row][column] == '#')
                    {
                        FillRectangle(
                            pixels,
                            stride,
                            left + offsets[index] + column,
                            y + glyphTop + row,
                            1,
                            1,
                            0,
                            0,
                            0);
                    }
                }
            }
        }
    }

    private static string[] GetRasterGlyph(char character)
    {
        return character switch
        {
            '0' => [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
            '1' => ["..#", "###", "..#", "..#", "..#", "..#", "..#", "..#", "..#"],
            '2' => ["..###.", ".#...#", ".....#", ".....#", "....#.", "...#..", "..#...", ".#....", "######"],
            '3' => [".###.", "#..##", "....#", "...#.", "..##.", "....#", "....#", "#...#", ".###."],
            '4' => ["....#.", "...##.", "...##.", "..#.#.", ".#..#.", "....#.", "######", "....#.", "....#."],
            '5' => ["#####", "#....", "#....", "####.", "#...#", "....#", "....#", "#...#", "####."],
            '6' => [".###.", "#...#", "#....", "####.", "#...#", "#...#", "#...#", "#...#", ".###."],
            'd' => ["....#", "....#", "....#", ".####", "#...#", "....#", "....#", "#..##", "#####"],
            's' => [".####", ".#..#", ".##..", "...##", "##..#", ".####"],
            '+' => ["..#..", "..#..", "#####", "..#..", "..#.."],
            '.' => [".", ".", ".", ".", ".", ".", ".", ".", "#"],
            _ => throw new ArgumentOutOfRangeException(nameof(character), character, "Unsupported raster glyph.")
        };
    }

    private static void FillRectangle(
        byte[] pixels,
        int stride,
        int x,
        int y,
        int width,
        int height,
        byte red,
        byte green,
        byte blue)
    {
        for (int offsetY = 0; offsetY < height; offsetY++)
        {
            for (int offsetX = 0; offsetX < width; offsetX++)
            {
                int index = ((y + offsetY) * stride) + ((x + offsetX) * 4);
                pixels[index] = blue;
                pixels[index + 1] = green;
                pixels[index + 2] = red;
                pixels[index + 3] = 255;
            }
        }
    }

    private static void SavePng(string path, int width, int height, byte[] pixels, int stride)
    {
        BitmapSource bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write);
        encoder.Save(stream);
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

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string examplesPath = Path.Combine(directory.FullName, "docs", "IsoFieldRebar", "examples");
            if (Directory.Exists(examplesPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate docs/IsoFieldRebar/examples from test output directory.");
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
