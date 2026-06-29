using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Services.Logging;

public sealed class TrueBimLogFormatterTests
{
    [Fact]
    public void Format_IncludesTimestampLevelAndMessage()
    {
        DateTimeOffset timestamp = new(2026, 6, 29, 12, 34, 56, TimeSpan.FromHours(7));

        string result = TrueBimLogFormatter.Format(timestamp, "INFO", "Launcher opened.");

        Assert.Equal("2026-06-29T12:34:56.0000000+07:00 [INFO] Launcher opened.", result);
    }

    [Fact]
    public void Format_IncludesExceptionDetails()
    {
        DateTimeOffset timestamp = new(2026, 6, 29, 12, 34, 57, TimeSpan.FromHours(7));
        InvalidOperationException exception = new("Something failed.");

        string result = TrueBimLogFormatter.Format(timestamp, "ERROR", "Preview failed.", exception);

        Assert.Contains("2026-06-29T12:34:57.0000000+07:00 [ERROR] Preview failed.", result);
        Assert.Contains("System.InvalidOperationException: Something failed.", result);
    }

    [Fact]
    public void Format_RejectsEmptyLevel()
    {
        DateTimeOffset timestamp = new(2026, 6, 29, 12, 34, 56, TimeSpan.FromHours(7));

        Assert.Throws<ArgumentException>(() => TrueBimLogFormatter.Format(timestamp, string.Empty, "Message"));
    }

    [Fact]
    public void Format_RejectsNullMessage()
    {
        DateTimeOffset timestamp = new(2026, 6, 29, 12, 34, 56, TimeSpan.FromHours(7));

        Assert.Throws<ArgumentNullException>(() => TrueBimLogFormatter.Format(timestamp, "INFO", null!));
    }
}
