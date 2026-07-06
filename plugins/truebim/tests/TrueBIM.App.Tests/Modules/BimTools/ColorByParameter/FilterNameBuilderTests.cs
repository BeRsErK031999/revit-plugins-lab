using TrueBIM.App.Modules.BimTools.ColorByParameter.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ColorByParameter;

public sealed class FilterNameBuilderTests
{
    [Fact]
    public void BuildCreatesOwnedNameWithStablePrefix()
    {
        FilterNameBuilder builder = new();

        string name = builder.Build("Комментарии", "АР / Стена: 01?");

        Assert.StartsWith(FilterNameBuilder.Prefix, name);
        Assert.DoesNotContain("/", name);
        Assert.DoesNotContain(":", name);
        Assert.DoesNotContain("?", name);
        Assert.True(builder.IsOwnedFilterName(name));
    }

    [Fact]
    public void BuildLimitsLongNames()
    {
        FilterNameBuilder builder = new();
        string longParameter = new('П', 120);
        string longValue = new('З', 120);

        string name = builder.Build(longParameter, longValue);

        Assert.True(name.Length <= FilterNameBuilder.MaxLength);
        Assert.StartsWith(FilterNameBuilder.Prefix, name);
    }

    [Theory]
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
