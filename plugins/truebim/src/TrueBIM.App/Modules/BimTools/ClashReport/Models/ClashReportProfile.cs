namespace TrueBIM.App.Modules.BimTools.ClashReport.Models;

public sealed class ClashReportProfile
{
    public string Name { get; set; } = "Импорт коллизий";

    public string LastImportPath { get; set; } = string.Empty;

    public double SectionBoxPaddingMm { get; set; } = 1500;

    public bool HighlightOnNavigate { get; set; } = true;
}
