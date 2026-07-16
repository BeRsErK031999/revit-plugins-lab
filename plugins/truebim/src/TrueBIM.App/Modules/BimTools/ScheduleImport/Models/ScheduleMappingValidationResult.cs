namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ScheduleMappingValidationResult(
    string ConfigurationFingerprint,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0;
}
