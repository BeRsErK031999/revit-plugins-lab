using System.IO;
using System.IO.Compression;
using System.Text;
using TrueBIM.App.Modules.BimTools.ParameterAudit.Models;
using TrueBIM.App.Modules.BimTools.ParameterAudit.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ParameterAudit;

public sealed class ParameterAuditProfileRepairServiceTests
{
    [Fact]
    public void CreateRepairedCopy_MergesDuplicateColumnsAndPreservesOriginal()
    {
        string sourcePath = Path.Combine(
            Path.GetTempPath(),
            $"truebim-parameter-audit-{Guid.NewGuid():N}.xlsx");
        string repairedPath = string.Empty;
        try
        {
            CreateDuplicateMatrixWorkbook(sourcePath);
            ParameterAuditProfileReader reader = new();
            ParameterAuditProfile sourceProfile = reader.Read(sourcePath);
            ParameterAuditProfileFix fix = Assert.Single(sourceProfile.AvailableFixes);

            ParameterAuditRepairResult result = new ParameterAuditProfileRepairService()
                .CreateRepairedCopy(sourcePath, [fix]);
            repairedPath = result.RepairedPath;

            Assert.True(File.Exists(repairedPath));
            Assert.Equal(1, result.MergedColumnCount);
            Assert.Equal(1, result.MovedMarkerCount);
            Assert.Single(reader.Read(sourcePath).AvailableFixes);

            ParameterAuditProfile repairedProfile = reader.Read(repairedPath);
            Assert.True(repairedProfile.IsValid);
            Assert.Empty(repairedProfile.AvailableFixes);
            Assert.Equal(2, repairedProfile.Rules.Count);
            Assert.Contains(repairedProfile.Rules, rule => rule.ParameterName == "ADSK_Размер_Ширина");
            Assert.Contains(repairedProfile.Rules, rule => rule.ParameterName == "ADSK_Размер_Высота");
        }
        finally
        {
            if (File.Exists(repairedPath))
            {
                File.Delete(repairedPath);
            }

            File.Delete(sourcePath);
        }
    }

    private static void CreateDuplicateMatrixWorkbook(string path)
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
            + InlineCell("B1", "ADSK_Размер_Ширина")
            + InlineCell("C1", "ADSK_Размер_Высота")
            + InlineCell("D1", "ADSK_Размер_Ширина")
            + "</row><row r=\"2\">"
            + InlineCell("A2", "Блок дверной")
            + InlineCell("C2", "+")
            + InlineCell("D2", "+")
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
