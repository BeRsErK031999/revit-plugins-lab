namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintParameterCatalog(
    IReadOnlyList<string> SheetParameterNames,
    IReadOnlyList<string> TitleBlockParameterNames,
    IReadOnlyList<string> ProjectParameterNames)
{
    public static PrintParameterCatalog Empty { get; } = new(
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>());
}
