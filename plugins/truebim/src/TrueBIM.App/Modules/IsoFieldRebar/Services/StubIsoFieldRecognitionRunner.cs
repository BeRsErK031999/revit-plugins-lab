using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class StubIsoFieldRecognitionRunner : IIsoFieldRecognitionRunner
{
    public IsoFieldRecognitionResult Run(string? sourcePath)
    {
        return IsoFieldRecognitionResult.Empty;
    }
}
