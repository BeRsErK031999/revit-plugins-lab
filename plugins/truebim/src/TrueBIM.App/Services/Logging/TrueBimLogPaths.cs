using System.IO;

namespace TrueBIM.App.Services.Logging;

public sealed class TrueBimLogPaths
{
    public string LogDirectory { get; }

    public string CurrentLogFile { get; }

    public TrueBimLogPaths()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))
    {
    }

    public TrueBimLogPaths(string appDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);

        LogDirectory = Path.Combine(appDataDirectory, "TrueBIM", "Logs");
        CurrentLogFile = Path.Combine(LogDirectory, "truebim.log");
    }
}
