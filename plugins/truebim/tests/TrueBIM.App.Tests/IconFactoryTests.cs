using System.Reflection;
using TrueBIM.App.UI;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IconFactoryTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    public void CreateImage_RendersRequestedPixelSize(int requestedSize)
    {
        Type iconFactory = typeof(TrueBimIcon).Assembly.GetType("TrueBIM.App.UI.IconFactory", throwOnError: true)!;
        MethodInfo createImage = iconFactory.GetMethod(
            "CreateImage",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(TrueBimIcon), typeof(double)],
            modifiers: null)!;

        object image = createImage.Invoke(null, [TrueBimIcon.IsoFieldRebar, (double)requestedSize])!;
        PropertyInfo pixelWidth = image.GetType().GetProperty("PixelWidth")!;
        PropertyInfo pixelHeight = image.GetType().GetProperty("PixelHeight")!;

        Assert.Equal(requestedSize, pixelWidth.GetValue(image));
        Assert.Equal(requestedSize, pixelHeight.GetValue(image));
    }
}
