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
