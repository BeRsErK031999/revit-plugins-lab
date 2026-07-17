namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintPrinterOption(string Name, bool IsPdfDriver)
{
    public string DisplayName => IsPdfDriver ? $"{Name} (PDF-драйвер)" : Name;
}
