namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintPdfExportSettings(
    PrintPdfColorMode ColorMode,
    PrintPdfRasterQuality RasterQuality,
    bool AlwaysUseRaster);
