using System.Text;
using System.Text.Json;
using TrueBIM.App.Modules.SharedParameters.Models;
using TrueBIM.App.Modules.SharedParameters.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.SharedParameters;

public sealed class SharedParameterReportExportServiceTests
{
    private readonly SharedParameterReportExportService service = new();

    [Fact]
    public void Builders_PreserveUnicodeEscapeCsvAndEncodeHtml()
    {
        SharedParameterDescriptor parameter = SharedParameterTestData.Parameter(name: "Марка; <опасно>");
        SharedParameterProjectAnalysis analysis = SharedParameterTestData.Analysis(parameter);

        string csv = service.BuildCsv(analysis);
        string html = service.BuildHtml(analysis);
        string text = service.BuildText(analysis);

        Assert.Contains("\"Марка; <опасно>\"", csv, StringComparison.Ordinal);
        Assert.Contains("Марка; &lt;опасно&gt;", html, StringComparison.Ordinal);
        Assert.Contains("Параметр: Марка; <опасно>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_CreatesAllRequiredReportFormats()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"truebim-shared-parameter-{Guid.NewGuid():N}");
        try
        {
            FamilyParameterUsageReport familyReport = new(
                new FamilyDocumentDescriptor(
                    "QA-Дверь",
                    @"C:\Families\QA-Дверь.rfa",
                    "Двери",
                    FamilySourceKind.SelectedFiles,
                    null),
                SharedParameterTestData.Parameter().Guid,
                false,
                null,
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                []);
            SharedParameterReportPackage package = service.Save(
                SharedParameterTestData.Analysis(),
                null,
                Path.Combine(directory, "report.json"),
                [familyReport]);

            Assert.All(
                new[] { package.JsonPath, package.CsvPath, package.HtmlPath, package.TextPath },
                path => Assert.True(File.Exists(path), path));
            byte[] preamble = Encoding.UTF8.GetPreamble();
            Assert.False(File.ReadAllBytes(package.JsonPath).Take(preamble.Length).SequenceEqual(preamble));
            Assert.True(File.ReadAllBytes(package.CsvPath).Take(preamble.Length).SequenceEqual(preamble));
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(package.JsonPath));
            JsonElement familyReports = json.RootElement.GetProperty("FamilyReports");
            Assert.Equal(
                "QA-Дверь",
                familyReports[0].GetProperty("Family").GetProperty("Name").GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
