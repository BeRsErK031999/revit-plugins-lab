using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public interface IIsoFieldRecognitionRunner
{
    IsoFieldRecognitionResult Run(string? sourcePath);
}
