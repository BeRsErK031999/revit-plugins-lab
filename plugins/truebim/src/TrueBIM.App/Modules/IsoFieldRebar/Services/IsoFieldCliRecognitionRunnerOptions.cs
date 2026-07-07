namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed record IsoFieldCliRecognitionRunnerOptions
{
    public const string DefaultArgumentsTemplate = "--request \"{request}\" --output \"{output}\"";

    public string ExecutablePath { get; init; } = string.Empty;

    public string ArgumentsTemplate { get; init; } = DefaultArgumentsTemplate;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public string? TempRootDirectory { get; init; }
}
