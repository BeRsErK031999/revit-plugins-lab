namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed record LocalCliPdfTableParserOptions
{
    public const string DefaultArgumentsTemplate = "\"{script}\" --input \"{source}\" --output \"{output}\"";

    public string PythonExecutablePath { get; init; } = "python";

    public string WorkerScriptPath { get; init; } = string.Empty;

    public string ArgumentsTemplate { get; init; } = DefaultArgumentsTemplate;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(45);

    public string? TempRootDirectory { get; init; }
}
