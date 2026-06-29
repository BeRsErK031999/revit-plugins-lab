namespace TrueBIM.App.Services.Logging;

public static class TrueBimLogFormatter
{
    public static string Format(DateTimeOffset timestamp, string level, string message, Exception? exception = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(level);
        ArgumentNullException.ThrowIfNull(message);

        string line = $"{timestamp:O} [{level}] {message}";
        return exception is null
            ? line
            : line + Environment.NewLine + exception;
    }
}
