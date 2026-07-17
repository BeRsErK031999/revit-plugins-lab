namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public interface IIsoFieldFilePicker
{
    IReadOnlyList<string> PickIsoFieldSourceFiles();

    string? PickSourceSetManifestSavePath(string? initialDirectory, string? suggestedFileName);

    string? PickRebarReportSavePath(string? initialDirectory, string? suggestedFileName);
}
