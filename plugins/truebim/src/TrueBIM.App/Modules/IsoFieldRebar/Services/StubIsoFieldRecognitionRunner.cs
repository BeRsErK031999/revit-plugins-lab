using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class StubIsoFieldRecognitionRunner : IIsoFieldRecognitionRunner, IIsoFieldRecognitionRunnerDiagnostics
{
    public string RunnerName => "Stub";

    public string RunnerVersion => GetType().Assembly.GetName().Version?.ToString() ?? "unknown";

    public IsoFieldRecognitionResult Run(string? sourcePath)
    {
        return IsoFieldRecognitionResult.Empty;
    }
}
