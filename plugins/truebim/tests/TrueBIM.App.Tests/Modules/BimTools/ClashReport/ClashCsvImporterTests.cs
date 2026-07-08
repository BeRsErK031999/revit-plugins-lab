using System.Text;
using TrueBIM.App.Modules.BimTools.ClashReport.Models;
using TrueBIM.App.Modules.BimTools.ClashReport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ClashReport;

public sealed class ClashCsvImporterTests
{
    [Fact]
    public void Import_ReadsQuotedSemicolonCsvAndParsesStatuses()
    {
        using TempDirectory temp = new();
        string csvPath = Path.Combine(temp.Path, "clashes.csv");
        File.WriteAllText(
            csvPath,
            """
            ClashName;ElementId1;ElementId2;X;Y;Z;Status;Comment
            "Clash; A";123;456;1.5;2,5;3;In Progress;"Needs; check"
            Clash B;789;;4;5;6;Resolved;Done
            """,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        ClashImportResult result = new ClashCsvImporter().Import(csvPath);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Clash; A", result.Items[0].Name);
        Assert.Equal(123, result.Items[0].ElementId1);
        Assert.Equal(456, result.Items[0].ElementId2);
        Assert.Equal(2.5, result.Items[0].Y);
        Assert.Equal(ClashStatus.InProgress, result.Items[0].Status);
        Assert.Equal("Needs; check", result.Items[0].Comment);
        Assert.Equal(ClashStatus.Resolved, result.Items[1].Status);
    }

    [Fact]
    public void Import_ReportsRowsWithoutElementIdsOrPoint()
    {
        using TempDirectory temp = new();
        string csvPath = Path.Combine(temp.Path, "clashes.csv");
        File.WriteAllText(
            csvPath,
            """
            ClashId;Comment
            C-01;No ids
            """,
            Encoding.UTF8);

        ClashImportResult result = new ClashCsvImporter().Import(csvPath);

        Assert.Single(result.Items);
        Assert.Contains(result.Errors, error => error.Contains("ElementId1/ElementId2", StringComparison.CurrentCulture));
        Assert.Contains(result.Errors, error => error.Contains("нет ElementId", StringComparison.CurrentCulture));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "truebim-clash-import-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
