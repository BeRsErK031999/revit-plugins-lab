using TrueBIM.App.Modules.BimTools.ScheduleImport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleImport;

public sealed class GridTableExtractionServiceTests
{
    [Fact]
    public void Extract_ReconstructsPrimaryGridAndIgnoresFooterGrid()
    {
        double[] xs = [0, 20, 80, 100];
        double[] ys = [100, 80, 60, 40];
        List<TableSourceLine> lines = [];
        foreach (double x in xs)
        {
            lines.Add(new TableSourceLine(x, 40, x, 100));
        }

        foreach (double y in ys)
        {
            lines.Add(new TableSourceLine(0, y, 100, y));
        }

        lines.AddRange(
        [
            new TableSourceLine(0, 20, 30, 20),
            new TableSourceLine(0, 10, 30, 10),
            new TableSourceLine(0, 10, 0, 20),
            new TableSourceLine(10, 10, 10, 20),
            new TableSourceLine(30, 10, 30, 20)
        ]);
        IReadOnlyList<TableSourceWord> words =
        [
            Word("Поз.", 3, 88),
            Word("Наименование", 28, 94),
            Word("и характеристика", 28, 86),
            Word("Кол-во", 84, 88),
            Word("Раздел", 35, 68),
            Word("1", 5, 48),
            Word("Труба", 30, 48),
            Word("10", 88, 48),
            Word("Подпись", 12, 14)
        ];

        var table = new GridTableExtractionService().Extract(
            "sample.pdf",
            1,
            words,
            lines,
            ["test"]);

        Assert.NotNull(table);
        Assert.Equal(3, table.ColumnCount);
        Assert.Equal(3, table.RowCount);
        Assert.Equal($"Наименование{Environment.NewLine}и характеристика", table.Rows[0].Values[1]);
        Assert.Equal("Раздел", table.Rows[1].Values[1]);
        Assert.Equal("Труба", table.Rows[2].Values[1]);
        Assert.DoesNotContain(table.Cells, cell => cell.Text.Contains("Подпись", StringComparison.Ordinal));
    }

    private static TableSourceWord Word(string text, double x, double y)
    {
        return new TableSourceWord(text, x, y, x + Math.Max(2, text.Length), y + 3);
    }
}
