namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintFileNameTokenOption(
    string Category,
    string DisplayName,
    string Token);

public sealed record PrintFileNameTokenInsertion(
    string Text,
    int CaretIndex);
