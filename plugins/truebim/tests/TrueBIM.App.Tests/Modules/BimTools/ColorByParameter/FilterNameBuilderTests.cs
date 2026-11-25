using TrueBIM.App.Modules.BimTools.ColorByParameter.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ColorByParameter;

public sealed class FilterNameBuilderTests
{
    [Fact]
    public void BuildCreatesOwnedNameWithStablePrefix()
    {
        FilterNameBuilder builder = new();

        string name = builder.Build("ADSK_Марка", "АР / Стена: 01");
        Assert.Equal("［TB］ ADSK_Марка АР / Стена: 01", name);
        Assert.True(builder.IsOwnedFilterName(name));
    }

    [Fact]
    public void BuildPreservesBothLongComponents()
    {
        FilterNameBuilder builder = new();
        string longParameter = new('П', 120);
        string longValue = new('З', 120);

        string name = builder.Build(longParameter, longValue);

        Assert.Equal($"［TB］ {longParameter} {longValue}", name);
    }

    [Theory]
    [InlineData("A;B", "［TB］ Марка A_B")]
    [InlineData("[?]", "［TB］ Марка ___")]
    [InlineData("", "［TB］ Марка Пусто")]
    public void BuildHandlesUnsupportedCharactersAndEmptyValues(string value, string expected)
    {
        Assert.Equal(expected, new FilterNameBuilder().Build("Марка", value));
    }

    [Theory]
    [InlineData("［TB］ Марка В1", true)]
    [InlineData("［TB］Прочее", false)]
    [InlineData("BIM_F_Комментарии_AABBCCDD", true)]
    [InlineData("BIM_Комментарии_AABBCCDD", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsOwnedFilterNameChecksPrefix(string? name, bool expected)
    {
        FilterNameBuilder builder = new();

        Assert.Equal(expected, builder.IsOwnedFilterName(name));
    }
}
