namespace TrueBIM.App.Modules.BimTools.BatchExport.Models;

public sealed class BatchExportSheetSet
{
    public string Name { get; set; } = string.Empty;

    public List<string> SheetNumbers { get; set; } = [];
}
