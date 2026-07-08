using TrueBIM.App.Modules.BimTools.BatchExport.Models;
using TrueBIM.App.Modules.BimTools.BatchExport.Services;
using TrueBIM.App.Modules.Print.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.BatchExport;

public sealed class BatchExportNamingServiceTests
{
    private readonly BatchExportNamingService service = new();

    [Fact]
    public void Build_UsesSheetFieldsSimpleParametersDateAndCounter()
    {
        PrintSheetInfo sheet = CreateSheet(
            "A-101",
            "План/этажа",
            new Dictionary<string, string>
            {
                ["Revision"] = "R1"
            });
        BatchExportFileNameContext context = CreateContext();

        BatchExportFileNamePreview preview = service.Build(
            "{SheetNumber}_{SheetName}_{Revision}_{Date:yyyyMMdd}_{Counter:000}",
            sheet,
            context,
            3);

        Assert.Equal("A-101_План_этажа_R1_20260708_003", preview.FileName);
        Assert.False(preview.HasMissingTokens);
    }

    [Fact]
    public void Build_UsesExplicitSheetAndProjectParameterTokens()
    {
        PrintSheetInfo sheet = CreateSheet(
            "A-102",
            "Разрез",
            new Dictionary<string, string>
            {
                ["Раздел"] = "АР"
            });
        BatchExportFileNameContext context = CreateContext(
            new Dictionary<string, string>
            {
                ["Код проекта"] = "P-44"
            });

        BatchExportFileNamePreview preview = service.Build(
            "{ProjectParameter:Код проекта}_{SheetParameter:Раздел}_{SheetNumber}",
            sheet,
            context,
            1);

        Assert.Equal("P-44_АР_A-102", preview.FileName);
    }

    [Fact]
    public void Build_MarksMissingTokensAndKeepsUsableFileName()
    {
        PrintSheetInfo sheet = CreateSheet("A-103", "Фасад", new Dictionary<string, string>());
        BatchExportFileNameContext context = CreateContext();

        BatchExportFileNamePreview preview = service.Build(
            "{SheetNumber}_{Unknown}_{MissingParameter}",
            sheet,
            context,
            1);

        Assert.Equal("A-103", preview.FileName);
        Assert.True(preview.HasMissingTokens);
        Assert.Equal(["Unknown", "MissingParameter"], preview.MissingTokens);
    }

    [Fact]
    public void Build_FallsBackWhenTemplateResolvesToBlank()
    {
        PrintSheetInfo sheet = CreateSheet("A-104", "План", new Dictionary<string, string>());
        BatchExportFileNameContext context = CreateContext();

        BatchExportFileNamePreview preview = service.Build("{Unknown}", sheet, context, 7);

        Assert.Equal("Лист_7", preview.FileName);
    }

    private static PrintSheetInfo CreateSheet(
        string sheetNumber,
        string sheetName,
        IReadOnlyDictionary<string, string> parameters)
    {
        return new PrintSheetInfo(
            ElementId: 42,
            SourceId: "active",
            SourceName: "Project.rvt",
            SourceIsLinked: false,
            GroupName: "Без группы",
            SheetNumber: sheetNumber,
            SheetName: sheetName,
            SheetFormat: "A1",
            IsPlaceholder: false,
            CanBePrinted: true,
            SheetParameters: parameters);
    }

    private static BatchExportFileNameContext CreateContext(
        IReadOnlyDictionary<string, string>? projectParameters = null)
    {
        return new BatchExportFileNameContext(
            "Project.rvt",
            "Project",
            "001",
            new DateTime(2026, 7, 8),
            projectParameters ?? new Dictionary<string, string>());
    }
}
