using TrueBIM.App.Modules.BimTools.ClashReport.Models;
using TrueBIM.App.Modules.BimTools.ClashReport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ClashReport;

public sealed class ClashReportFileImportServiceTests
{
    [Fact]
    public void ImportCsv_ReadsHeaderAndEscapedComment()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "clashes.csv");
        File.WriteAllText(
            path,
            """
            ClashId;ClashName;ElementId1;ElementId2;X;Y;Z;Status;Comment
            C-01;Pipe vs wall;101;202;1;2;3;ignored;"Checked; false positive"
            """);

        ClashImportResult result = new ClashReportFileImportService().Import(path);

        ClashItem item = Assert.Single(result.Items);
        Assert.Equal("C-01", item.ClashId);
        Assert.Equal("Pipe vs wall", item.Name);
        Assert.Equal(101, item.ElementId1);
        Assert.Equal(202, item.ElementId2);
        Assert.Equal(1, item.X);
        Assert.Equal(2, item.Y);
        Assert.Equal(3, item.Z);
        Assert.Equal(ClashStatus.Ignored, item.Status);
        Assert.Equal("Checked; false positive", item.Comment);
        Assert.Contains("CSV импортирован: 1 строк.", result.Messages);
    }

    [Fact]
    public void ImportXml_ReadsClashResultPointAndElementIds()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "clashes.xml");
        File.WriteAllText(
            path,
            """
            <exchange>
              <clashresult guid="C-02" status="active">
                <name>Duct vs beam</name>
                <clashpoint x="4" y="5" z="6" />
                <elementId1>303</elementId1>
                <elementId2>404</elementId2>
                <comment>Check in model</comment>
              </clashresult>
            </exchange>
            """);

        ClashImportResult result = new ClashReportFileImportService().Import(path);

        ClashItem item = Assert.Single(result.Items);
        Assert.Equal("C-02", item.ClashId);
        Assert.Equal("Duct vs beam", item.Name);
        Assert.Equal(303, item.ElementId1);
        Assert.Equal(404, item.ElementId2);
        Assert.Equal(4, item.X);
        Assert.Equal(5, item.Y);
        Assert.Equal(6, item.Z);
        Assert.Equal(ClashStatus.InProgress, item.Status);
        Assert.Equal("Check in model", item.Comment);
    }

    [Fact]
    public void ImportXml_FallsBackToNavisworksElementIdAttributes()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "navisworks.xml");
        File.WriteAllText(
            path,
            """
            <exchange>
              <clashresult guid="C-03" name="Wall vs pipe">
                <clashobjects>
                  <clashobject>
                    <objectattribute>
                      <name>Element ID</name>
                      <value>501</value>
                    </objectattribute>
                  </clashobject>
                  <clashobject>
                    <objectattribute>
                      <name>Element ID</name>
                      <value>502</value>
                    </objectattribute>
                  </clashobject>
                </clashobjects>
              </clashresult>
            </exchange>
            """);

        ClashImportResult result = new ClashReportFileImportService().Import(path);

        ClashItem item = Assert.Single(result.Items);
        Assert.Equal("C-03", item.ClashId);
        Assert.Equal("Wall vs pipe", item.Name);
        Assert.Equal(501, item.ElementId1);
        Assert.Equal(502, item.ElementId2);
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
