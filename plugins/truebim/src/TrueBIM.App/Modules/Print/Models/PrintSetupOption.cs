namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintSetupOption(string? SetupName, string DisplayName)
{
    public static PrintSetupOption Automatic { get; } = new(null, "Авто по листу · 100%");
}
