using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleImport;

public sealed class DwgGridTableParserTests
{
    [Fact]
    public async Task ParseAsync_ReadsLineAndTextGridFromDwg()
    {
        string directory = Path.Combine(Path.GetTempPath(), "truebim-dwg-parser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "schedule.dwg");
        try
        {
            CadDocument document = new(ACadVersion.AC1024);
            double[] xs = [0, 20, 80, 100];
            double[] ys = [100, 80, 60, 40];
            foreach (double x in xs)
            {
                document.Entities.Add(new Line(new XYZ(x, 40, 0), new XYZ(x, 100, 0)));
            }

            foreach (double y in ys)
            {
                document.Entities.Add(new Line(new XYZ(0, y, 0), new XYZ(100, y, 0)));
            }

            AddText(document, "Поз.", 3, 88);
            AddText(document, "Наименование", 28, 88);
            AddText(document, "Кол-во", 84, 88);
            AddText(document, "Раздел", 35, 68);
            AddText(document, "1", 5, 48);
            AddText(document, "Труба", 30, 48);
            AddText(document, "10", 88, 48);
            new DwgWriter(path, document).Write();

            DwgGridTableParser parser = new(new GridTableExtractionService());

            var result = await parser.ParseAsync(path, CancellationToken.None);

            Assert.Empty(result.Errors);
            var table = Assert.Single(result.Tables);
            Assert.Equal(3, table.ColumnCount);
            Assert.Equal(3, table.RowCount);
            Assert.Equal("Труба", table.Rows[2].Values[1]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AddText(CadDocument document, string value, double x, double y)
    {
        document.Entities.Add(new TextEntity
        {
            Value = value,
            InsertPoint = new XYZ(x, y, 0),
            Height = 3
        });
    }
}
