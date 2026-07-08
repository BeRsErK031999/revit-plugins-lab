namespace TrueBIM.App.Modules.BimTools.FamilyManager.Models;

public sealed class FamilyLoadHistoryItem
{
    public string FilePath { get; set; } = string.Empty;

    public string FamilyName { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public DateTimeOffset LoadedAtUtc { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Action)
        ? $"{FamilyName} - {LoadedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm}"
        : $"{FamilyName} - {Action} - {LoadedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm}";
}
