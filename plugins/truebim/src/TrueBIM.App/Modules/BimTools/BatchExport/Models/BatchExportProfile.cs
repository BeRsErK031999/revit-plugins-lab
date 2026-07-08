using TrueBIM.App.Modules.BimTools.BatchExport.Services;

namespace TrueBIM.App.Modules.BimTools.BatchExport.Models;

public sealed class BatchExportProfile
{
    public string Name { get; set; } = "Рабочая документация";

    public string? ExportFolder { get; set; }

    public string FileNameTemplate { get; set; } = BatchExportNamingService.DefaultTemplate;

    public bool ExportPdf { get; set; } = true;

    public bool ExportDwg { get; set; }
}
