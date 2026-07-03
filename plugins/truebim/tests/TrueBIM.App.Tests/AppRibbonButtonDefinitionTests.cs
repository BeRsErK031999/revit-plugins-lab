using TrueBIM.App.Commands;
using TrueBIM.App.UI;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class AppRibbonButtonDefinitionTests
{
    [Fact]
    public void RibbonButtons_IncludesViewVisibilityOnBimPanel()
    {
        TrueBimRibbonButtonDefinition button = Assert.Single(
            TrueBimRibbon.Buttons,
            button => string.Equals(button.Name, "TrueBIM_ViewVisibility", StringComparison.Ordinal));

        Assert.Equal("БИМ", button.PanelName);
        Assert.Equal("Видимость", button.Text);
        Assert.Equal($"TrueBIM.App.Commands.{nameof(OpenViewVisibilityCommand)}", button.CommandClassName);
        Assert.Equal(TrueBimIcon.Visibility, button.Icon);
        Assert.Contains("видимость", button.Tooltip, StringComparison.CurrentCultureIgnoreCase);
    }

    [Fact]
    public void RibbonButtons_HaveUniqueNames()
    {
        string[] duplicateNames = TrueBimRibbon.Buttons
            .GroupBy(button => button.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicateNames);
    }

    [Fact]
    public void RibbonButtons_HaveCommandClassNames()
    {
        foreach (TrueBimRibbonButtonDefinition button in TrueBimRibbon.Buttons)
        {
            Assert.StartsWith("TrueBIM.App.Commands.", button.CommandClassName, StringComparison.Ordinal);
            Assert.EndsWith("Command", button.CommandClassName, StringComparison.Ordinal);
        }
    }
}
