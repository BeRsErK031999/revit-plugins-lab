using System.IO;
using System.IO.Compression;
using System.Text;
using TrueBIM.App.Modules.BimTools.ParameterAudit.Models;
using TrueBIM.App.Modules.BimTools.ParameterAudit.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ParameterAudit;

public sealed class ParameterAuditProfileReaderTests
{
    [Fact]
    public void ReadCsv_LoadsRequiredAndAllowedValueRule()
    {
        string path = Path.Combine(Path.GetTempPath(), $"truebim-parameter-audit-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(
                path,
                "RuleId;Category;ParameterName;Scope;Required;AllowedValues;Severity\n"
                + "DOOR_RATING;Двери;Огнестойкость;Type;true;EI30|EI60;Error",
                Encoding.UTF8);

            ParameterAuditProfile profile = new ParameterAuditProfileReader().Read(path);

            ParameterAuditRule rule = Assert.Single(profile.Rules);
            Assert.True(profile.IsValid);
            Assert.Equal("DOOR_RATING", rule.RuleId);
            Assert.Equal("Двери", rule.CategoryPattern);
            Assert.Equal(ParameterAuditScope.Type, rule.Scope);
            Assert.True(rule.Required);
            Assert.Equal(["EI30", "EI60"], rule.AllowedValues);
            Assert.Contains(profile.Issues, issue =>
                issue.Severity == ParameterAuditProfileIssueSeverity.Warning
                && issue.Message.Contains("по имени", StringComparison.CurrentCultureIgnoreCase));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadCsv_RejectsInvalidGuidAndRange()
    {
        string path = Path.Combine(Path.GetTempPath(), $"truebim-parameter-audit-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(
                path,
                "RuleId;Category;ParameterGuid;Min;Max\n"
                + "ROOM_AREA;Помещения;not-a-guid;100;10",
                Encoding.UTF8);

            ParameterAuditProfile profile = new ParameterAuditProfileReader().Read(path);

            Assert.False(profile.IsValid);
            Assert.Empty(profile.Rules);
            Assert.Contains(profile.Issues, issue => issue.Message.Contains("ParameterGuid", StringComparison.Ordinal));
            Assert.Contains(profile.Issues, issue => issue.Message.Contains("корректных правил", StringComparison.CurrentCultureIgnoreCase));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadXlsx_LoadsFirstWorksheetWithInlineStrings()
    {
        string path = Path.Combine(Path.GetTempPath(), $"truebim-parameter-audit-{Guid.NewGuid():N}.xlsx");
        try
        {
            CreateInlineStringWorkbook(path);

            ParameterAuditProfile profile = new ParameterAuditProfileReader().Read(path);

            ParameterAuditRule rule = Assert.Single(profile.Rules);
            Assert.True(profile.IsValid);
            Assert.Equal("WALL_MARK", rule.RuleId);
            Assert.Equal("Стены", rule.CategoryPattern);
            Assert.Equal("Марка", rule.ParameterName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CreateTemplate_CanBeReadBack()
    {
        string path = Path.Combine(Path.GetTempPath(), $"truebim-parameter-audit-{Guid.NewGuid():N}.csv");
        try
        {
            ParameterAuditProfileReader reader = new();
            File.WriteAllText(path, reader.CreateTemplate(), Encoding.UTF8);

            ParameterAuditProfile profile = reader.Read(path);

            Assert.True(profile.IsValid);
            Assert.Equal(3, profile.Rules.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void CreateInlineStringWorkbook(string path)
    {
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "xl/workbook.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" "
            + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
            + "<sheets><sheet name=\"Rules\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        WriteEntry(
            archive,
            "xl/_rels/workbook.xml.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>"
            + "</Relationships>");
        WriteEntry(
            archive,
            "xl/worksheets/sheet1.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>"
            + "<row r=\"1\">"
            + InlineCell("A1", "RuleId")
            + InlineCell("B1", "Category")
            + InlineCell("C1", "ParameterName")
            + InlineCell("D1", "Required")
            + "</row><row r=\"2\">"
            + InlineCell("A2", "WALL_MARK")
            + InlineCell("B2", "Стены")
            + InlineCell("C2", "Марка")
            + InlineCell("D2", "true")
            + "</row></sheetData></worksheet>");
    }

    private static string InlineCell(string reference, string value)
    {
        return $"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{value}</t></is></c>";
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
