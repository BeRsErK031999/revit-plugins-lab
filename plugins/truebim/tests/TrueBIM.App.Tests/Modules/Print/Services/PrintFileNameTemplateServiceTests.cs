using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Modules.Print.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Print.Services;

public sealed class PrintFileNameTemplateServiceTests
{
    private readonly PrintFileNameTemplateService service = new();

    [Fact]
    public void Build_ReplacesSheetProjectAndDocumentTokens()
    {
        PrintFileNamePreview preview = service.Build(
            "{ProjectNumber}_{DocumentName}_{SheetNumber}_{SheetName}",
            Sheet("A-101", "План этажа"),
            Context("РД", "P-42", "Model"),
            counter: 1);

        Assert.Equal("P-42_Model_A-101_План этажа", preview.FileName);
        Assert.False(preview.HasUnknownTokens);
    }

    [Fact]
    public void Build_ReplacesRussianSheetProjectAndDocumentTokens()
    {
        PrintFileNamePreview preview = service.Build(
            "{Номер проекта}_{Имя документа}_{Номер листа}_{Имя листа}",
            Sheet("A-101", "План этажа"),
            Context("РД", "P-42", "Model"),
            counter: 1);

        Assert.Equal("P-42_Model_A-101_План этажа", preview.FileName);
        Assert.False(preview.HasUnknownTokens);
    }

    [Fact]
    public void BuildCombined_UsesMaskWithoutAddingServiceSuffix()
    {
        PrintFileNamePreview preview = service.BuildCombined(
            "{Номер проекта}_{Имя документа}",
            [Sheet("A-101", "План"), Sheet("A-102", "Разрез")],
            Context("РД", "P-42", "Model"));

        Assert.Equal("P-42_Model", preview.FileName);
        Assert.False(preview.HasUnknownTokens);
    }

    [Fact]
    public void BuildCombined_UsesDocumentNameAsDefault()
    {
        PrintFileNamePreview preview = service.BuildCombined(
            "  ",
            [Sheet("A-101", "План")],
            Context("РД", "P-42", "Model"));

        Assert.Equal("Model", preview.FileName);
    }

    [Fact]
    public void BuildCombined_ProducesSinglePdfExtensionFromResolvedMask()
    {
        PrintFileNamePreview preview = service.BuildCombined(
            "{Имя документа}.pdf",
            [Sheet("A-101", "План")],
            Context("РД", "P-42", "Model"));

        string finalFileName = PrintPdfExportService.BuildCombinedPdfFileName(preview.FileName);

        Assert.Equal("Model.pdf", finalFileName);
    }

    [Fact]
    public void Build_ReplacesDateAndCounterTokens()
    {
        PrintFileNamePreview preview = service.Build(
            "{Date:yyyy-MM-dd}_{Counter:000}_{SheetNumber}",
            Sheet("A-101", "План"),
            Context("РД", "P-42", "Model"),
            counter: 7);

        Assert.Equal("2026-07-02_007_A-101", preview.FileName);
    }

    [Fact]
    public void Build_ReplacesRussianDateAndCounterTokens()
    {
        PrintFileNamePreview preview = service.Build(
            "{Дата:yyyy-MM-dd}_{Счетчик:000}_{Номер листа}",
            Sheet("A-101", "План"),
            Context("РД", "P-42", "Model"),
            counter: 7);

        Assert.Equal("2026-07-02_007_A-101", preview.FileName);
    }

    [Fact]
    public void Build_ReplacesSheetAndProjectParameterTokens()
    {
        PrintFileNamePreview preview = service.Build(
            "{SheetParameter:Формат листа}_{ProjectParameter:Шифр}_{SheetNumber}",
            Sheet(
                "A-101",
                "План",
                new Dictionary<string, string> { ["Формат листа"] = "A1" }),
            Context(
                "РД",
                "P-42",
                "Model",
                new Dictionary<string, string> { ["Шифр"] = "KR-01" }),
            counter: 1);

        Assert.Equal("A1_KR-01_A-101", preview.FileName);
        Assert.False(preview.HasUnknownTokens);
    }

    [Fact]
    public void Build_ReplacesRussianSheetAndProjectParameterTokens()
    {
        PrintFileNamePreview preview = service.Build(
            "{Параметр листа:Формат листа}_{Параметр проекта:Шифр}_{Номер листа}",
            Sheet(
                "A-101",
                "План",
                new Dictionary<string, string> { ["Формат листа"] = "A1" }),
            Context(
                "РД",
                "P-42",
                "Model",
                new Dictionary<string, string> { ["Шифр"] = "KR-01" }),
            counter: 1);

        Assert.Equal("A1_KR-01_A-101", preview.FileName);
        Assert.False(preview.HasUnknownTokens);
    }

    [Fact]
    public void GetSheetParameterNames_ReturnsOnlyUniqueSheetParameterTokens()
    {
        IReadOnlyCollection<string> names = service.GetSheetParameterNames(
            "{Номер листа}_{SheetParameter:Формат листа}_{Параметр листа:Том}_{sheetParameter:ignored}_{Параметр листа: том }");

        Assert.Equal(2, names.Count);
        Assert.Contains("Формат листа", names);
        Assert.Contains("Том", names);
    }

    [Fact]
    public void GetProjectParameterNames_ReturnsEnglishAndRussianParameterTokens()
    {
        IReadOnlyCollection<string> names = service.GetProjectParameterNames(
            "{ProjectParameter:Шифр}_{Параметр проекта:Стадия}_{Имя проекта}");

        Assert.Equal(2, names.Count);
        Assert.Contains("Шифр", names);
        Assert.Contains("Стадия", names);
    }

    [Fact]
    public void GetParameterNames_CombinesPerSheetAndMergedTemplates()
    {
        IReadOnlyCollection<string> sheetNames = service.GetSheetParameterNames(
            "{Параметр листа:Том}_{Номер листа}",
            "{SheetParameter:Комплект}_{Имя документа}");
        IReadOnlyCollection<string> projectNames = service.GetProjectParameterNames(
            "{Параметр проекта:Шифр}_{Номер листа}",
            "{ProjectParameter:Стадия}_{Имя документа}");

        Assert.Equal(2, sheetNames.Count);
        Assert.Contains("Том", sheetNames);
        Assert.Contains("Комплект", sheetNames);
        Assert.Equal(2, projectNames.Count);
        Assert.Contains("Шифр", projectNames);
        Assert.Contains("Стадия", projectNames);
    }

    [Fact]
    public void Build_SanitizesWindowsFileNameCharacters()
    {
        PrintFileNamePreview preview = service.Build(
            "{SheetNumber}_{SheetName}",
            Sheet("A:101", "План/Разрез*"),
            Context("РД", "P-42", "Model"),
            counter: 1);

        Assert.Equal("A_101_План_Разрез", preview.FileName);
    }

    [Fact]
    public void Build_UsesDefaultTemplateWhenMaskIsEmpty()
    {
        PrintFileNamePreview preview = service.Build(
            "  ",
            Sheet("A-101", "План"),
            Context("РД", "P-42", "Model"),
            counter: 1);

        Assert.Equal("A-101_План", preview.FileName);
    }

    [Fact]
    public void Build_RemovesUnknownTokensAndReturnsWarningFlag()
    {
        PrintFileNamePreview preview = service.Build(
            "{SheetNumber}_{Unknown}_{SheetName}",
            Sheet("A-101", "План"),
            Context("РД", "P-42", "Model"),
            counter: 1);

        Assert.Equal("A-101_План", preview.FileName);
        Assert.True(preview.HasUnknownTokens);
    }

    [Fact]
    public void Build_TruncatesLongNames()
    {
        string longName = new('A', 220);

        PrintFileNamePreview preview = service.Build(
            "{SheetName}",
            Sheet("A-101", longName),
            Context("РД", "P-42", "Model"),
            counter: 1);

        Assert.Equal(180, preview.FileName.Length);
        Assert.True(preview.WasTruncated);
    }

    private static PrintSheetInfo Sheet(string number, string name)
    {
        return Sheet(number, name, new Dictionary<string, string>());
    }

    private static PrintSheetInfo Sheet(
        string number,
        string name,
        IReadOnlyDictionary<string, string> sheetParameters)
    {
        return new PrintSheetInfo(
            10,
            "model",
            "Model",
            SourceIsLinked: false,
            GroupName: "Без группы",
            number,
            name,
            "841 x 594 мм",
            IsPlaceholder: false,
            CanBePrinted: true,
            sheetParameters);
    }

    private static PrintFileNameContext Context(string projectName, string projectNumber, string documentName)
    {
        return Context(projectName, projectNumber, documentName, new Dictionary<string, string>());
    }

    private static PrintFileNameContext Context(
        string projectName,
        string projectNumber,
        string documentName,
        IReadOnlyDictionary<string, string> projectParameters)
    {
        return new PrintFileNameContext(
            documentName,
            projectName,
            projectNumber,
            new DateTime(2026, 7, 2),
            projectParameters);
    }
}
