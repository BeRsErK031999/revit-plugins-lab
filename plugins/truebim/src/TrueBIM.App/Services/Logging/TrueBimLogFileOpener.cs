using System.Diagnostics;

namespace TrueBIM.App.Services.Logging;

public sealed class TrueBimLogFileOpener
{
    private readonly FileTrueBimLogger logger;

    public TrueBimLogFileOpener(FileTrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void OpenLogFile()
    {
        logger.EnsureLogFile();
        logger.Info("Opening local log file.");

        ProcessStartInfo startInfo = new()
        {
            FileName = logger.CurrentLogFile,
            UseShellExecute = true
        };

        Process.Start(startInfo);
    }
}
