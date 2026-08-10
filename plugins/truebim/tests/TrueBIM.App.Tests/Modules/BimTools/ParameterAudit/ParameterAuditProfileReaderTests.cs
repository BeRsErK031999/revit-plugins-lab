using System.IO;
using System.IO.Compression;
using System.Text;
using TrueBIM.App.Modules.BimTools.ParameterAudit.Models;
using TrueBIM.App.Modules.BimTools.ParameterAudit.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ParameterAudit;

public sealed class ParameterAuditProfileReaderTests
{
    static ParameterAuditProfileReaderTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

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
    public void ReadCsv_LoadsWindows1251MatrixWhileFileIsOpenInExcel()
    {
        string path = Path.Combine(Path.GetTempPath(), $"truebim-parameter-audit-{Guid.NewGuid():N}.csv");
        try
        {
            string csv = "Описание;ADSK_Номер корпуса;ADSK_Этаж\r\n"
                + "Стены, перегородки;+;+\r\n"
                + "Двери;+;";
            File.WriteAllBytes(path, Encoding.GetEncoding(1251).GetBytes(csv));
            using FileStream excelHandle = new(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete);

            ParameterAuditProfile profile = new ParameterAuditProfileReader().Read(path);

            Assert.True(profile.IsValid);
            Assert.Equal(3, profile.Rules.Count);
            ParameterAuditRule floorRule = Assert.Single(profile.Rules.Where(rule =>
                rule.SelectionExpectedValue == "Стены, перегородки"
                && rule.ParameterName == "ADSK_Этаж"));
            Assert.Equal("Описание", floorRule.SelectionParameterName);
            Assert.Equal("C2", floorRule.RuleId);
            Assert.Equal("*", floorRule.CategoryPattern);
            Assert.True(floorRule.Required);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadXlsx_LoadsMatrixWhileWorkbookIsOpenInExcel()
    {
        string path = Path.Combine(Path.GetTempPath(), $"truebim-parameter-audit-{Guid.NewGuid():N}.xlsx");
        try
        {
            CreateMatrixWorkbook(path);
            using FileStream excelHandle = new(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete);

            ParameterAuditProfile profile = new ParameterAuditProfileReader().Read(path);

            Assert.True(profile.IsValid);
            Assert.Equal(2, profile.Rules.Count);
            Assert.All(profile.Rules, rule => Assert.Equal("Описание", rule.SelectionParameterName));
            Assert.Contains(profile.Rules, rule =>
                rule.SelectionExpectedValue == "Двери"
                && rule.ParameterName == "ADSK_Номер корпуса");
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
            Assert.Equal(7, profile.Rules.Count);
            Assert.All(profile.Rules, rule => Assert.True(rule.HasSelectionFilter));
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

    private static void CreateMatrixWorkbook(string path)
    {
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "xl/workbook.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" "
            + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
            + "<sheets><sheet name=\"Матрица\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
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
            + InlineCell("A1", "Описание")
            + InlineCell("B1", "ADSK_Номер корпуса")
            + InlineCell("C1", "ADSK_Этаж")
            + "</row><row r=\"2\">"
            + InlineCell("A2", "Стены, перегородки")
            + InlineCell("B2", "+")
            + "</row><row r=\"3\">"
            + InlineCell("A3", "Двери")
            + InlineCell("B3", "+")
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
