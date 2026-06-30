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
        Guard.NotNullOrWhiteSpace(appDataDirectory, nameof(appDataDirectory));

        LogDirectory = Path.Combine(appDataDirectory, "TrueBIM", "Logs");
        CurrentLogFile = Path.Combine(LogDirectory, "truebim.log");
    }
}
