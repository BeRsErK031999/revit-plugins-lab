using TrueBIM.App.Modules.BimTools.ColorByParameter.Models;
using TrueBIM.App.Modules.BimTools.ColorByParameter.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ColorByParameter;

public sealed class ColorPaletteServiceTests
{
    [Fact]
    public void GenerateReturnsRequestedColorCount()
    {
        ColorPaletteService service = new();

        IReadOnlyList<ColorSwatch> colors = service.Generate(12);

        Assert.Equal(12, colors.Count);
        Assert.All(colors, color => Assert.Matches("^#[0-9A-F]{6}$", color.Hex));
    }

    [Fact]
    public void GenerateReturnsEmptyListForZeroCount()
    {
        ColorPaletteService service = new();

        Assert.Empty(service.Generate(0));
    }
}
