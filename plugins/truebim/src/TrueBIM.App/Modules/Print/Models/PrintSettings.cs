namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintSettings(
    string? ExportFolder,
    string FileNameMask,
    bool IncludePlaceholders,
    bool ExportPdf,
    bool CombinePdf,
    string CombinedPdfFileName,
    PrintPdfColorMode PdfColorMode,
    PrintPdfRasterQuality PdfRasterQuality,
    bool AlwaysUseRasterPdf,
    bool ExportDwg,
    bool ExportDxf,
    bool ExportDwf,
    bool CombineDwg,
    bool ExportSeparatePdfWithCombined,
    string? DwgSetupName,
    string? DxfSetupName);
