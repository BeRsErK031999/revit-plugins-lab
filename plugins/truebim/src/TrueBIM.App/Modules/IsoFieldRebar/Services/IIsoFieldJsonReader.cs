using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public interface IIsoFieldJsonReader
{
    IsoFieldRecognitionResult Read(string filePath);
}
