using TrueBIM.App.Modules.SheetNumbering.Models;
using TrueBIM.App.Modules.SheetNumbering.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.SheetNumbering.Services;

public sealed class SheetNumberPreviewExportFormatterTests
{
    [Fact]
    public void FormatCsv_WritesHeaderAndRows()
    {
        SheetNumberPreviewExportFormatter formatter = new();
        SheetNumberPreviewExportRow[] rows =
        [
            new(10, "A-01", "B-01", "Floor Plan", false, "Preview")
        ];

        string csv = formatter.FormatCsv(rows);

        Assert.Contains("ElementId,CurrentNumber,NewNumber,SheetName,IsPlaceholder,Status", csv);
        Assert.Contains("10,A-01,B-01,Floor Plan,false,Preview", csv);
    }

    [Fact]
    public void FormatCsv_EscapesCsvValues()
    {
        SheetNumberPreviewExportFormatter formatter = new();
        SheetNumberPreviewExportRow[] rows =
        [
            new(10, "A-01", "B-01", "Plan, \"Main\"", true, "Duplicate preview number B-01")
        ];

        string csv = formatter.FormatCsv(rows);

        Assert.Contains("10,A-01,B-01,\"Plan, \"\"Main\"\"\",true,Duplicate preview number B-01", csv);
    }

    [Fact]
    public void FormatCsv_RejectsNullRows()
    {
        SheetNumberPreviewExportFormatter formatter = new();

        Assert.Throws<ArgumentNullException>(() => formatter.FormatCsv(null!));
    }
}
