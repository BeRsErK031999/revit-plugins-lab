using System.Globalization;
using System.IO;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public static class ScheduleImportParserFactory
{
    public const string PythonEnvironmentVariable = "TRUEBIM_SCHEDULE_IMPORT_PYTHON";
    public const string WorkerPathEnvironmentVariable = "TRUEBIM_SCHEDULE_IMPORT_PDF_WORKER";
    public const string WorkerArgumentsEnvironmentVariable = "TRUEBIM_SCHEDULE_IMPORT_PDF_WORKER_ARGS";
    public const string WorkerTimeoutEnvironmentVariable = "TRUEBIM_SCHEDULE_IMPORT_TIMEOUT_SECONDS";

    public static IPdfTableParser Create(ITrueBimLogger logger)
    {
        if (logger is null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        ScheduleTableJsonReader jsonReader = new();
        LocalCliPdfTableParserOptions options = new()
        {
            PythonExecutablePath = ResolvePythonExecutablePath(),
            WorkerScriptPath = ResolveWorkerScriptPath(),
            ArgumentsTemplate = ResolveArgumentsTemplate(),
            Timeout = ResolveTimeout(logger)
        };

        logger.Info($"Schedule Import PDF worker configured: {Path.GetFileName(options.WorkerScriptPath)}.");
        return new JsonOrSamplePdfTableParser(
            jsonReader,
            new LocalCliPdfTableParser(options, jsonReader, logger),
            logger);
    }

    private static string ResolvePythonExecutablePath()
    {
        string? value = Environment.GetEnvironmentVariable(PythonEnvironmentVariable);
        return string.IsNullOrWhiteSpace(value)
            ? "python"
            : value!;
    }

    private static string ResolveWorkerScriptPath()
    {
        string? value = Environment.GetEnvironmentVariable(WorkerPathEnvironmentVariable);
        return string.IsNullOrWhiteSpace(value)
            ? Path.Combine(AppContext.BaseDirectory, "tools", "schedule-import", "pdf_table_parser.py")
            : value!;
    }

    private static string ResolveArgumentsTemplate()
    {
        string? value = Environment.GetEnvironmentVariable(WorkerArgumentsEnvironmentVariable);
        return string.IsNullOrWhiteSpace(value)
            ? LocalCliPdfTableParserOptions.DefaultArgumentsTemplate
            : value!;
    }

    private static TimeSpan ResolveTimeout(ITrueBimLogger logger)
    {
        string? value = Environment.GetEnvironmentVariable(WorkerTimeoutEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return TimeSpan.FromSeconds(45);
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
            && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        logger.Warning($"Schedule Import PDF worker timeout '{value}' is invalid. Using 45 seconds.");
        return TimeSpan.FromSeconds(45);
    }
}
