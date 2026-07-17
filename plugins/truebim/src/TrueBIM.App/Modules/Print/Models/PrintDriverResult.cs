namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintDriverResult(
    int PrintedSheetCount,
    IReadOnlyList<PrintDriverFailure> Failures);
