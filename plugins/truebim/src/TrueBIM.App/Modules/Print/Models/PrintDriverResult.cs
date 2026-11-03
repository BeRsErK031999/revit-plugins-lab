namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintDriverResult(
    int PrintedSheetCount,
    IReadOnlyList<PrintDriverFailure> Failures)
{
    public IReadOnlyDictionary<long, PrintPaperSize> ExpectedPaper { get; init; } = new Dictionary<long, PrintPaperSize>();
    public IReadOnlyDictionary<long, string> LargerPaper { get; init; } = new Dictionary<long, string>();
}
