using TrueBIM.App.Modules.BimTools.Worksets.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.Worksets;

public sealed class WorksetCsvReaderTests
{
    [Fact]
    public void Read_SkipsHeaderAndNormalizesNames()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path, [
                "WorksetName",
                "  АР_Стены  ",
                "\"КР  Несущие конструкции\",ignored",
                "",
            ]);

            IReadOnlyList<TrueBIM.App.Modules.BimTools.Worksets.Models.WorksetImportRow> rows = new WorksetCsvReader().Read(path);

            Assert.Collection(
                rows,
                row => Assert.Equal("АР_Стены", row.WorksetName),
                row => Assert.Equal("КР Несущие конструкции", row.WorksetName),
                row => Assert.Equal(string.Empty, row.WorksetName));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
