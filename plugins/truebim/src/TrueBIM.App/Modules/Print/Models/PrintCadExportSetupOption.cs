namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintCadExportSetupOption(string? SetupName, string DisplayName)
{
    public bool IsDefault => string.IsNullOrWhiteSpace(SetupName);

    public override string ToString()
    {
        return DisplayName;
    }
}
