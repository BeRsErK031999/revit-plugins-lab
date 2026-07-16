using System.IO;

namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldSourceFile(
    string FilePath,
    IsoFieldLayerRole? Role,
    int? PixelWidth,
    int? PixelHeight,
    string? ValidationError = null,
    IsoFieldRoleDetection? RoleDetection = null)
{
    public string FileName => Path.GetFileName(FilePath);

    public bool HasValidImageSize => PixelWidth > 0 && PixelHeight > 0;

    public string ImageSizeText => HasValidImageSize
        ? $"{PixelWidth}×{PixelHeight} px"
        : "размер не определён";
}
