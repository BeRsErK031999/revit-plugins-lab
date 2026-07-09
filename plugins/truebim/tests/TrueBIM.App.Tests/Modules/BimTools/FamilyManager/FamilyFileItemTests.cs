using TrueBIM.App.Modules.BimTools.FamilyManager.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.FamilyManager;

public sealed class FamilyFileItemTests
{
    [Fact]
    public void PresetParameterDisplays_ReadCommonParameterValues()
    {
        FamilyFileItem family = new()
        {
            CachedTypes =
            [
                new FamilyTypeInfo(
                    0,
                    "900x2100",
                    [
                        new FamilyTypeParameterInfo("ADSK_Размер_Ширина", "900", "Double", "Тип", string.Empty),
                        new FamilyTypeParameterInfo("Высота", "2100", "Double", "Тип", string.Empty),
                        new FamilyTypeParameterInfo("Материал полотна", "Дерево", "String", "Тип", string.Empty)
                    ]),
                new FamilyTypeInfo(
                    0,
                    "1000x2100",
                    [
                        new FamilyTypeParameterInfo("Width", "1000", "Double", "Тип", string.Empty),
                        new FamilyTypeParameterInfo("Material", "Металл", "String", "Тип", string.Empty)
                    ])
            ]
        };

        Assert.Equal("900, 1000", family.WidthParameterDisplay);
        Assert.Equal("2100", family.HeightParameterDisplay);
        Assert.Equal("Дерево, Металл", family.MaterialParameterDisplay);
    }

    [Fact]
    public void AvailableTypeNames_MergesCachedAndCatalogTypes()
    {
        FamilyFileItem family = new()
        {
            CachedTypes =
            [
                new FamilyTypeInfo(0, "DN15"),
                new FamilyTypeInfo(0, "DN20")
            ],
            TypeCatalogTypeNames = ["DN20", "DN25"]
        };

        Assert.Equal(["DN15", "DN20", "DN25"], family.AvailableTypeNames);

        family.SelectedTypeName = "DN20";

        Assert.Equal("DN20", family.SelectedTypeDisplay);
    }
}
