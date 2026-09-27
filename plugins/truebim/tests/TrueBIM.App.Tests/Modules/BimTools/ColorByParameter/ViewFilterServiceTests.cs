using System.Reflection;
using TrueBIM.App.Modules.BimTools.ColorByParameter.Models;
using TrueBIM.App.Modules.BimTools.ColorByParameter.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ColorByParameter;

public sealed class ViewFilterServiceTests
{
    [Fact]
    public void CreateOverrides_ColorsOnlySurfaceAndCutPatterns()
    {
        EnsureRevitApiLoaded();
        ColorRuleRow row = new(
            ParameterValueToken.FromString("value", "value"),
            new ColorSwatch(17, 34, 51));
        MethodInfo createOverrides = typeof(ViewFilterService).GetMethod(
            "CreateOverrides",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        object settings = createOverrides.Invoke(null, [row, null])!;

        Assert.False(GetBoolean(GetProperty(settings, "ProjectionLineColor"), "IsValid"));
        Assert.False(GetBoolean(GetProperty(settings, "CutLineColor"), "IsValid"));
        AssertColor(GetProperty(settings, "SurfaceForegroundPatternColor"), 17, 34, 51);
        AssertColor(GetProperty(settings, "CutForegroundPatternColor"), 17, 34, 51);
        Assert.True(GetBoolean(settings, "IsSurfaceForegroundPatternVisible"));
        Assert.True(GetBoolean(settings, "IsCutForegroundPatternVisible"));
    }

    private static void AssertColor(object color, byte red, byte green, byte blue)
    {
        Assert.True(GetBoolean(color, "IsValid"));
        Assert.Equal(red, GetByte(color, "Red"));
        Assert.Equal(green, GetByte(color, "Green"));
        Assert.Equal(blue, GetByte(color, "Blue"));
    }

    private static object GetProperty(object source, string propertyName)
    {
        return source.GetType().GetProperty(propertyName)!.GetValue(source)!;
    }

    private static bool GetBoolean(object source, string propertyName)
    {
        return (bool)GetProperty(source, propertyName);
    }

    private static byte GetByte(object source, string propertyName)
    {
        return (byte)GetProperty(source, propertyName);
    }

    private static void EnsureRevitApiLoaded()
    {
        if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "RevitAPI"))
        {
            return;
        }

        Assembly.LoadFrom(@"C:\Program Files\Autodesk\Revit 2025\RevitAPI.dll");
    }
}
