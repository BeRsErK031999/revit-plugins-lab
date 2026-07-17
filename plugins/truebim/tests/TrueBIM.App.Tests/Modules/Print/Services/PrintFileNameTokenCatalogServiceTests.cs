using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Modules.Print.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Print.Services;

public sealed class PrintFileNameTokenCatalogServiceTests
{
    private readonly PrintFileNameTokenCatalogService service = new();

    [Fact]
    public void BuildOptions_GroupsUniqueParametersByContractSource()
    {
        PrintParameterCatalog first = new(
            ["Том", "Комплект"],
            ["Формат", "Разработал"],
            ["Шифр"]);
        PrintParameterCatalog second = new(
            ["том", "Стадия листа"],
            ["формат"],
            ["Стадия"]);

        IReadOnlyList<PrintFileNameTokenOption> options = service.BuildOptions([first, second]);

        Assert.Contains(options, option => option.Token == "{Номер листа}");
        Assert.Single(options, option => option.Token == "{Параметр листа:Том}");
        Assert.Contains(options, option => option.Token == "{Параметр листа:Стадия листа}");
        Assert.Single(options, option => option.Token == "{Параметр основной надписи:Формат}");
        Assert.Contains(options, option => option.Token == "{Параметр основной надписи:Разработал}");
        Assert.Contains(options, option => option.Token == "{Параметр проекта:Шифр}");
        Assert.Contains(options, option => option.Token == "{Параметр проекта:Стадия}");
    }

    [Theory]
    [InlineData("ABCD", 2, "{Номер листа}", "AB{Номер листа}CD")]
    [InlineData("AB", -5, "{Имя листа}", "{Имя листа}AB")]
    [InlineData("AB", 20, "{Имя документа}", "AB{Имя документа}")]
    public void InsertAtCaret_InsertsTokenAtSafeCaretPosition(
        string source,
        int caretIndex,
        string token,
        string expected)
    {
        PrintFileNameTokenInsertion result = service.InsertAtCaret(source, caretIndex, token);

        Assert.Equal(expected, result.Text);
        Assert.Equal(expected.IndexOf(token, StringComparison.Ordinal) + token.Length, result.CaretIndex);
    }
}
