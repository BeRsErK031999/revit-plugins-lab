namespace TrueBIM.App.Services.Logging;

public interface ITrueBimLogger
{
    void Info(string message);

    void Warning(string message);

    void Error(string message, Exception? exception = null);
}
