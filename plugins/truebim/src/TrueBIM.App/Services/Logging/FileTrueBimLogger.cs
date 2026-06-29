using System.IO;

namespace TrueBIM.App.Services.Logging;

public sealed class FileTrueBimLogger : ITrueBimLogger
{
    private readonly TrueBimLogPaths paths;

    public FileTrueBimLogger(TrueBimLogPaths paths)
    {
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public string CurrentLogFile => paths.CurrentLogFile;

    public void Info(string message)
    {
        Write("INFO", message, null);
    }

    public void Warning(string message)
    {
        Write("WARNING", message, null);
    }

    public void Error(string message, Exception? exception = null)
    {
        Write("ERROR", message, exception);
    }

    public void EnsureLogFile()
    {
        Directory.CreateDirectory(paths.LogDirectory);

        if (!File.Exists(paths.CurrentLogFile))
        {
            using FileStream _ = File.Create(paths.CurrentLogFile);
        }
    }

    private void Write(string level, string message, Exception? exception)
    {
        Directory.CreateDirectory(paths.LogDirectory);
        string entry = TrueBimLogFormatter.Format(DateTimeOffset.Now, level, message, exception);
        File.AppendAllText(paths.CurrentLogFile, entry + Environment.NewLine);
    }
}
