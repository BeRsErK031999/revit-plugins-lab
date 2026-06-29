using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Services.Logging;

public sealed class TrueBimLogPathsTests
{
    [Fact]
    public void Constructor_BuildsCurrentUserLogPath()
    {
        TrueBimLogPaths paths = new(@"C:\Users\Example\AppData\Roaming");

        Assert.Equal(@"C:\Users\Example\AppData\Roaming\TrueBIM\Logs", paths.LogDirectory);
        Assert.Equal(@"C:\Users\Example\AppData\Roaming\TrueBIM\Logs\truebim.log", paths.CurrentLogFile);
    }

    [Fact]
    public void Constructor_RejectsEmptyAppDataDirectory()
    {
        Assert.Throws<ArgumentException>(() => new TrueBimLogPaths(string.Empty));
    }
}
