using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed class LocalCliPdfTableParser : IPdfTableParser
{
    private readonly LocalCliPdfTableParserOptions options;
    private readonly ScheduleTableJsonReader jsonReader;
    private readonly ITrueBimLogger? logger;

    public LocalCliPdfTableParser(
        LocalCliPdfTableParserOptions options,
        ScheduleTableJsonReader jsonReader,
        ITrueBimLogger? logger = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.jsonReader = jsonReader ?? throw new ArgumentNullException(nameof(jsonReader));
        this.logger = logger;

        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Schedule Import PDF worker timeout must be positive.");
        }
    }

    public async Task<PdfParserResult> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return PdfParserResult.FromError("Распознавание PDF отменено.");
        }

        try
        {
            return await Task.Run(() => ParseCore(filePath, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return PdfParserResult.FromError("Распознавание PDF отменено.");
        }
    }

    private PdfParserResult ParseCore(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return PdfParserResult.FromError("Выберите PDF-файл со спецификацией.");
        }

        string sourcePath = Path.GetFullPath(filePath);
        if (!File.Exists(sourcePath))
        {
            return PdfParserResult.FromError($"PDF-файл не найден: {sourcePath}");
        }

        string workerScriptPath = string.IsNullOrWhiteSpace(options.WorkerScriptPath)
            ? string.Empty
            : Path.GetFullPath(options.WorkerScriptPath);
        if (string.IsNullOrWhiteSpace(workerScriptPath) || !File.Exists(workerScriptPath))
        {
            return PdfParserResult.FromError(
                $"Schedule Import PDF worker не найден: {workerScriptPath}. Проверьте установку TrueBIM или переменную TRUEBIM_SCHEDULE_IMPORT_PDF_WORKER.");
        }

        string pythonPath = string.IsNullOrWhiteSpace(options.PythonExecutablePath)
            ? "python"
            : options.PythonExecutablePath;
        string workDirectory = CreateWorkDirectory();
        try
        {
            string outputPath = Path.Combine(workDirectory, "schedule-import-result.json");
            logger?.Info($"Schedule Import PDF worker starting. Source='{Path.GetFileName(sourcePath)}'; TimeoutSeconds={options.Timeout.TotalSeconds:0.#}.");
            ProcessResult processResult = RunProcess(
                pythonPath,
                workerScriptPath,
                sourcePath,
                outputPath,
                workDirectory,
                cancellationToken);
            logger?.Info($"Schedule Import PDF worker exited. ExitCode={processResult.ExitCode}; StdOutLength={processResult.StandardOutput.Length}; StdErrLength={processResult.StandardError.Length}.");

            if (processResult.ExitCode != 0)
            {
                return PdfParserResult.FromError(
                    $"Schedule Import PDF worker failed. ExitCode={processResult.ExitCode}; StdErr={TrimProcessText(processResult.StandardError)}; StdOut={TrimProcessText(processResult.StandardOutput)}");
            }

            if (!File.Exists(outputPath))
            {
                logger?.Warning("Schedule Import PDF worker finished without result JSON.");
                return PdfParserResult.FromError("Schedule Import PDF worker не создал JSON-результат.");
            }

            PdfParserResult result = jsonReader.ReadParserResult(outputPath);
            logger?.Info($"Schedule Import PDF worker JSON loaded. Tables={result.Tables.Count}; Warnings={result.Warnings.Count}; Errors={result.Errors.Count}.");
            return result;
        }
        catch (TimeoutException exception)
        {
            logger?.Warning(exception.Message);
            return PdfParserResult.FromError(exception.Message);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or Win32Exception)
        {
            logger?.Error("Schedule Import PDF worker failed.", exception);
            return PdfParserResult.FromError(exception.Message);
        }
        finally
        {
            TryDeleteDirectory(workDirectory, logger);
        }
    }

    private string CreateWorkDirectory()
    {
        string rootDirectory = string.IsNullOrWhiteSpace(options.TempRootDirectory)
            ? Path.Combine(Path.GetTempPath(), "TrueBIM", "ScheduleImport")
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

    private ProcessResult RunProcess(
        string pythonPath,
        string workerScriptPath,
        string sourcePath,
        string outputPath,
        string workDirectory,
        CancellationToken cancellationToken)
    {
        StringBuilder standardOutput = new();
        StringBuilder standardError = new();
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = BuildArguments(workerScriptPath, sourcePath, outputPath),
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
            return new ProcessResult(-1, string.Empty, "Schedule Import PDF worker process did not start.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (!process.WaitForExit(100))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (stopwatch.Elapsed > options.Timeout)
            {
                TryKill(process);
                throw new TimeoutException($"Schedule Import PDF worker timed out after {options.Timeout.TotalSeconds:0.#} seconds.");
            }
        }

        process.WaitForExit();
        return new ProcessResult(
            process.ExitCode,
            standardOutput.ToString(),
            standardError.ToString());
    }

    private string BuildArguments(string workerScriptPath, string sourcePath, string outputPath)
    {
        string template = string.IsNullOrWhiteSpace(options.ArgumentsTemplate)
            ? LocalCliPdfTableParserOptions.DefaultArgumentsTemplate
            : options.ArgumentsTemplate;
        return template
            .Replace("{script}", EscapeArgumentValue(workerScriptPath))
            .Replace("{source}", EscapeArgumentValue(sourcePath))
            .Replace("{output}", EscapeArgumentValue(outputPath));
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
        catch (Win32Exception)
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
            logger?.Warning($"Schedule Import PDF worker temp cleanup failed for WorkId='{Path.GetFileName(directoryPath)}'. {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            logger?.Warning($"Schedule Import PDF worker temp cleanup was denied for WorkId='{Path.GetFileName(directoryPath)}'. {exception.Message}");
        }
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
