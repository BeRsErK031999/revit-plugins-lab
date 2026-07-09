namespace TrueBIM.App.Modules.BimTools.FamilyManager.Models;

public sealed class FamilyThumbnailResult
{
    public bool Succeeded { get; set; }

    public string ThumbnailPath { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
