namespace TrueBIM.App.Modules.Print.Models;

public sealed class PrintPreset
{
    public const string DefaultPresetName = "По умолчанию";

    public string Name { get; set; } = DefaultPresetName;

    public PrintSettings? Settings { get; set; }

    public DwgExportProfile? DwgProfile { get; set; }

    public PrintPreset Clone()
    {
        return new PrintPreset
        {
            Name = Name,
            Settings = Settings is null ? null : Settings with { },
            DwgProfile = DwgProfile?.Clone()
        };
    }
}
