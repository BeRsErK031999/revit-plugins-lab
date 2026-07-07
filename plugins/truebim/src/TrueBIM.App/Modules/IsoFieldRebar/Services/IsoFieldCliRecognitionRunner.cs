using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldCliRecognitionRunner : IIsoFieldRecognitionRunner, IIsoFieldRecognitionRunnerDiagnostics
{
    private const string RequestSchemaVersion = "1.0";
    private readonly IsoFieldCliRecognitionRunnerOptions options;
    private readonly IIsoFieldJsonReader jsonReader;
    private readonly ITrueBimLogger? logger;

    public IsoFieldCliRecognitionRunner(
        IsoFieldCliRecognitionRunnerOptions options,
        IIsoFieldJsonReader jsonReader,
        ITrueBimLogger? logger = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.jsonReader = jsonReader ?? throw new ArgumentNullException(nameof(jsonReader));
        this.logger = logger;

        if (string.IsNullOrWhiteSpace(options.ExecutablePath))
        {
            throw new ArgumentException("IsoField CLI worker executable path is required.", nameof(options));
        }

        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "IsoField CLI worker timeout must be positive.");
        }
    }

    public string RunnerName => "CLI";

    public IsoFieldRecognitionResult Run(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new InvalidOperationException("Для CLI-распознавания нужен исходный файл изополей.");
        }

        string normalizedSourcePath = Path.GetFullPath(sourcePath!);
        if (!File.Exists(normalizedSourcePath))
        {
            throw new FileNotFoundException("IsoField source file was not found.", normalizedSourcePath);
        }

        string workerPath = Path.GetFullPath(options.ExecutablePath);
        if (!File.Exists(workerPath))
        {
            throw new FileNotFoundException("IsoField CLI worker executable was not found.", workerPath);
        }

        logger?.Info($"IsoField CLI recognition starting. Worker='{Path.GetFileName(workerPath)}'; Source='{Path.GetFileName(normalizedSourcePath)}'; TimeoutSeconds={options.Timeout.TotalSeconds:0.#}.");
        string workDirectory = CreateWorkDirectory();
        try
        {
            string requestPath = Path.Combine(workDirectory, "request.json");
            string outputPath = Path.Combine(workDirectory, "recognition-result.json");
            WriteRequest(requestPath, normalizedSourcePath, outputPath);
            logger?.Info($"IsoField CLI worker request prepared. WorkId='{Path.GetFileName(workDirectory)}'.");

            ProcessResult processResult = RunProcess(workerPath, workDirectory, requestPath, normalizedSourcePath, outputPath);
            logger?.Info($"IsoField CLI worker exited. ExitCode={processResult.ExitCode}; StdOutLength={processResult.StandardOutput.Length}; StdErrLength={processResult.StandardError.Length}.");
            if (processResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"IsoField CLI worker failed. ExitCode={processResult.ExitCode}; StdErr={TrimProcessText(processResult.StandardError)}; StdOut={TrimProcessText(processResult.StandardOutput)}");
            }

            if (!File.Exists(outputPath))
            {
                logger?.Warning("IsoField CLI worker finished without recognition-result JSON.");
                throw new InvalidDataException("IsoField CLI worker did not create recognition-result JSON.");
            }

            IsoFieldRecognitionResult result = jsonReader.Read(outputPath);
            logger?.Info($"IsoField CLI recognition JSON validated. Polylines={result.Polylines.Count}; Diagnostics={result.Diagnostics.Count}.");
            return result;
        }
        finally
        {
            TryDeleteDirectory(workDirectory, logger);
        }
    }

    private string CreateWorkDirectory()
    {
        string rootDirectory = string.IsNullOrWhiteSpace(options.TempRootDirectory)
            ? Path.Combine(Path.GetTempPath(), "TrueBIM", "IsoFieldRebar")
            : options.TempRootDirectory!;
        Directory.CreateDirectory(rootDirectory);

        string directoryName = string.Concat(
            DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture),
            "-",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        string workDirectory = Path.Combine(rootDirectory, directoryName);
        Directory.CreateDirectory(workDirectory);
        return workDirectory;
    }

    private static void WriteRequest(string requestPath, string sourcePath, string outputPath)
    {
        WorkerRequestContract request = new()
        {
            SchemaVersion = RequestSchemaVersion,
            SourcePath = sourcePath,
            OutputPath = outputPath
        };
        string json = JsonSerializer.Serialize(
            request,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(requestPath, json, Encoding.UTF8);
    }

    private ProcessResult RunProcess(
        string workerPath,
        string workDirectory,
        string requestPath,
        string sourcePath,
        string outputPath)
    {
        StringBuilder standardOutput = new();
        StringBuilder standardError = new();
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = workerPath,
                Arguments = BuildArguments(requestPath, sourcePath, outputPath),
                WorkingDirectory = workDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                standardOutput.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                standardError.AppendLine(args.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("IsoField CLI worker process did not start.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(ToTimeoutMilliseconds(options.Timeout)))
        {
            logger?.Warning($"IsoField CLI worker timeout. TimeoutSeconds={options.Timeout.TotalSeconds:0.#}; Worker='{Path.GetFileName(workerPath)}'.");
            TryKill(process);
            throw new TimeoutException($"IsoField CLI worker timed out after {options.Timeout.TotalSeconds:0.#} seconds.");
        }

        process.WaitForExit();
        return new ProcessResult(
            process.ExitCode,
            standardOutput.ToString(),
            standardError.ToString());
    }

    private string BuildArguments(string requestPath, string sourcePath, string outputPath)
    {
        string template = string.IsNullOrWhiteSpace(options.ArgumentsTemplate)
            ? IsoFieldCliRecognitionRunnerOptions.DefaultArgumentsTemplate
            : options.ArgumentsTemplate;
        return template
            .Replace("{request}", EscapeArgumentValue(requestPath))
            .Replace("{source}", EscapeArgumentValue(sourcePath))
            .Replace("{output}", EscapeArgumentValue(outputPath));
    }

    private static int ToTimeoutMilliseconds(TimeSpan timeout)
    {
        double milliseconds = Math.Ceiling(timeout.TotalMilliseconds);
        if (milliseconds > int.MaxValue)
        {
            return int.MaxValue;
        }

        return Math.Max(1, (int)milliseconds);
    }

    private static string EscapeArgumentValue(string value)
    {
        return value.Replace("\"", "\\\"");
    }

    private static string TrimProcessText(string value)
    {
        string text = (value ?? string.Empty).Trim();
        return text.Length <= 500
            ? text
            : text.Substring(0, 500);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(1000);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static void TryDeleteDirectory(string directoryPath, ITrueBimLogger? logger)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
        catch (IOException exception)
        {
            logger?.Warning($"IsoField CLI temp cleanup failed for WorkId='{Path.GetFileName(directoryPath)}'. {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            logger?.Warning($"IsoField CLI temp cleanup was denied for WorkId='{Path.GetFileName(directoryPath)}'. {exception.Message}");
        }
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed class WorkerRequestContract
    {
        public string SchemaVersion { get; set; } = string.Empty;

        public string SourcePath { get; set; } = string.Empty;

        public string OutputPath { get; set; } = string.Empty;
    }
}
