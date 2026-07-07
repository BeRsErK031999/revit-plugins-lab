using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class StubIsoFieldRecognitionRunner : IIsoFieldRecognitionRunner, IIsoFieldRecognitionRunnerDiagnostics
{
    public string RunnerName => "Stub";

    public IsoFieldRecognitionResult Run(string? sourcePath)
    {
        return IsoFieldRecognitionResult.Empty;
    }
}
