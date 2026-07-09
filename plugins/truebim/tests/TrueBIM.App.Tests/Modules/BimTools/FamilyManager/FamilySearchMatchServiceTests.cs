using TrueBIM.App.Modules.BimTools.FamilyManager.Models;
using TrueBIM.App.Modules.BimTools.FamilyManager.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.FamilyManager;

public sealed class FamilySearchMatchServiceTests
{
    private readonly FamilySearchMatchService service = new();

    [Fact]
    public void FindMatchText_ReturnsNameMatch()
    {
        FamilyFileItem family = new()
        {
            Name = "Door Panel",
            Category = "Двери"
        };

        Assert.True(service.Matches(family, "panel"));
        Assert.Equal("Имя: Door Panel", service.FindMatchText(family, "panel"));
    }

    [Fact]
    public void FindMatchText_ReturnsTypeAndParameterMatches()
    {
        FamilyFileItem family = new()
        {
            Name = "Door",
            CachedTypes =
            [
                new FamilyTypeInfo(
                    0,
                    "900x2100",
                    [
                        new FamilyTypeParameterInfo("ADSK_Размер_Ширина", "900 мм", "Double", "Тип", "BaseWidth"),
                        new FamilyTypeParameterInfo("Fire Rating", "EI60", "String", "Тип", string.Empty)
                    ])
            ]
        };

        Assert.Equal("Тип: 900x2100", service.FindMatchText(family, "2100"));
        Assert.Equal("Параметр: 900x2100: ADSK_Размер_Ширина", service.FindMatchText(family, "Ширина"));
        Assert.Equal("Значение: 900x2100: Fire Rating = EI60", service.FindMatchText(family, "EI60"));
        Assert.Equal("Формула: 900x2100: ADSK_Размер_Ширина = BaseWidth", service.FindMatchText(family, "BaseWidth"));
    }

    [Fact]
    public void FindMatchText_ReturnsCatalogAndPathMatches()
    {
        FamilyFileItem family = new()
        {
            Name = "Chair",
            FilePath = @"C:\Lib\Furniture\Chair.rfa",
            TypeCatalogPath = @"C:\Lib\Furniture\Chair.txt",
            TypeCatalogTypeNames = ["Guest Chair", "Office Chair"]
        };

        Assert.Equal("Тип catalog: Office Chair", service.FindMatchText(family, "office"));
        Assert.Equal(@"Catalog: C:\Lib\Furniture\Chair.txt", service.FindMatchText(family, "Chair.txt"));
        Assert.Equal(@"Файл: C:\Lib\Furniture\Chair.rfa", service.FindMatchText(family, "Chair.rfa"));
    }

    [Fact]
    public void Matches_ReturnsTrueForEmptySearch()
    {
        Assert.True(service.Matches(new FamilyFileItem { Name = "Any" }, string.Empty));
    }
}
