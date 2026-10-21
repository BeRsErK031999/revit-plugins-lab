using System.Reflection;
using TrueBIM.App.UI;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IconFactoryTests
{
    [Theory]
    [InlineData(16, TrueBimIcon.IsoFieldRebar)]
    [InlineData(32, TrueBimIcon.IsoFieldRebar)]
    [InlineData(16, TrueBimIcon.RebarSplit)]
    [InlineData(32, TrueBimIcon.RebarSplit)]
    [InlineData(16, TrueBimIcon.ElectricalSchematic)]
    [InlineData(32, TrueBimIcon.ElectricalSchematic)]
    public void CreateImage_RendersRequestedPixelSize(int requestedSize, TrueBimIcon icon)
    {
        Type iconFactory = typeof(TrueBimIcon).Assembly.GetType("TrueBIM.App.UI.IconFactory", throwOnError: true)!;
        MethodInfo createImage = iconFactory.GetMethod(
            "CreateImage",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(TrueBimIcon), typeof(double)],
            modifiers: null)!;

        object image = createImage.Invoke(null, [icon, (double)requestedSize])!;
        PropertyInfo pixelWidth = image.GetType().GetProperty("PixelWidth")!;
        PropertyInfo pixelHeight = image.GetType().GetProperty("PixelHeight")!;

        Assert.Equal(requestedSize, pixelWidth.GetValue(image));
        Assert.Equal(requestedSize, pixelHeight.GetValue(image));
    }
}
