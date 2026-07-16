namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ScheduleFieldCatalogResult(
    long CategoryId,
    IReadOnlyList<ScheduleFieldOption> Fields,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0;
}
