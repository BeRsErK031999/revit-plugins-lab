using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleImport;

public sealed class BimScheduleDryRunServiceTests
{
    [Fact]
    public void CreateReport_SummarizesMatchedAndUnmappedColumns()
    {
        ParsedTable table = CreateTable();
        var mappings = new ParameterMappingService().SuggestMappings(table, ["Марка", "Длина"]);
        ScheduleImportContext context = CreateContext(canUseBimScheduleMode: true, ["Марка", "Длина"]);

        BimScheduleDryRunReport report = new BimScheduleDryRunService().CreateReport(table, mappings, context);

        Assert.True(report.Succeeded);
        Assert.Equal(2, report.AvailableFieldCount);
        Assert.Equal(3, report.SourceColumnCount);
        Assert.Equal(2, report.MatchedColumnCount);
        Assert.Equal(2, report.DataRowCount);
        Assert.Equal(["Комментарий"], report.UnmappedColumns);
        Assert.Empty(report.RequiredUnmappedColumns);
        Assert.Contains(report.Warnings, warning => warning.Contains("Dry-run", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateReport_RequiresActiveViewSchedule()
    {
        ParsedTable table = CreateTable();
        var mappings = new ParameterMappingService().SuggestMappings(table, ["Марка"]);
        ScheduleImportContext context = CreateContext(canUseBimScheduleMode: false, Array.Empty<string>());

        BimScheduleDryRunReport report = new BimScheduleDryRunService().CreateReport(table, mappings, context);

        string error = Assert.Single(report.Errors);
        Assert.Contains("ViewSchedule", error, StringComparison.Ordinal);
        Assert.False(report.Succeeded);
    }

    private static ParsedTable CreateTable()
    {
        string[] columns = ["Марка", "Длина, м", "Комментарий"];
        string[][] rows =
        [
            columns,
            ["A-1", "12.5", "Проверить"],
            ["A-2", "7.0", ""]
        ];
        List<ParsedRow> parsedRows = [];
        List<ParsedCell> cells = [];
        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            parsedRows.Add(new ParsedRow(rowIndex, rows[rowIndex]));
            for (int columnIndex = 0; columnIndex < rows[rowIndex].Length; columnIndex++)
            {
                cells.Add(new ParsedCell(
                    rowIndex,
                    columnIndex,
                    1,
                    1,
                    rows[rowIndex][columnIndex],
                    null,
                    1,
                    rowIndex == 0));
            }
        }

        return new ParsedTable(
            "source.pdf",
            1,
            parsedRows,
            columns,
            cells,
            1,
            Array.Empty<string>());
    }

    private static ScheduleImportContext CreateContext(
        bool canUseBimScheduleMode,
        IReadOnlyList<string> availableFields)
    {
        return new ScheduleImportContext(
            "doc.rvt",
            "Schedule",
            canUseBimScheduleMode ? "Schedule" : "FloorPlan",
            42,
            true,
            canUseBimScheduleMode,
            availableFields,
            Array.Empty<string>());
    }
}
