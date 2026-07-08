namespace TrueBIM.App.Modules.BimTools.FamilyManager.Models;

public sealed record FamilyLoadResult(
    FamilyLoadStatus Status,
    string FamilyName,
    string Message)
{
    public bool Succeeded => Status is FamilyLoadStatus.Loaded or FamilyLoadStatus.AlreadyLoaded;
}
