using System.IO;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public static class IsoFieldRecognitionRunnerFactory
{
    public const string WorkerPathEnvironmentVariable = "TRUEBIM_ISOFIELD_WORKER";
    public const string WorkerArgumentsEnvironmentVariable = "TRUEBIM_ISOFIELD_WORKER_ARGS";
    public const string WorkerTimeoutEnvironmentVariable = "TRUEBIM_ISOFIELD_WORKER_TIMEOUT_SECONDS";

    public static IIsoFieldRecognitionRunner Create(ITrueBimLogger logger)
    {
        if (logger is null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        string? workerPath = Environment.GetEnvironmentVariable(WorkerPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(workerPath))
        {
            logger.Info("IsoField CLI worker is not configured. Using stub recognition runner.");
            return new StubIsoFieldRecognitionRunner();
        }

        IsoFieldCliRecognitionRunnerOptions options = new()
        {
            ExecutablePath = workerPath!,
            ArgumentsTemplate = ResolveArgumentsTemplate(),
            Timeout = ResolveTimeout(logger)
        };
        logger.Info($"IsoField CLI worker configured: {Path.GetFileName(workerPath)}.");
        return new IsoFieldCliRecognitionRunner(options, new IsoFieldJsonReader());
    }

    private static string ResolveArgumentsTemplate()
    {
        string? argumentsTemplate = Environment.GetEnvironmentVariable(WorkerArgumentsEnvironmentVariable);
        return string.IsNullOrWhiteSpace(argumentsTemplate)
            ? IsoFieldCliRecognitionRunnerOptions.DefaultArgumentsTemplate
            : argumentsTemplate!;
    }

    private static TimeSpan ResolveTimeout(ITrueBimLogger logger)
    {
        string? timeoutValue = Environment.GetEnvironmentVariable(WorkerTimeoutEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(timeoutValue))
        {
            return TimeSpan.FromSeconds(30);
        }

        if (double.TryParse(
            timeoutValue,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double seconds)
            && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        logger.Warning($"IsoField CLI worker timeout '{timeoutValue}' is invalid. Using 30 seconds.");
        return TimeSpan.FromSeconds(30);
    }
}
