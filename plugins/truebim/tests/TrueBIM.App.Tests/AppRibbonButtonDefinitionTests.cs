using TrueBIM.App.Commands;
using TrueBIM.App.UI;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class AppRibbonButtonDefinitionTests
{
    public static TheoryData<string, string, string, string, TrueBimIcon, string> BimToolButtons { get; } = new()
    {
        {
            "TrueBIM_ColorByParameter",
            "Проверка модели",
            "Цвета\nпо параметрам",
            $"TrueBIM.App.Commands.{nameof(ColorByParameterCommand)}",
            TrueBimIcon.ColorByParameter,
            "раскраски"
        },
        {
            "TrueBIM_CopyParameters",
            "Параметры",
            "Копирование\nпараметров",
            $"TrueBIM.App.Commands.{nameof(CopyParametersCommand)}",
            TrueBimIcon.CopyParameters,
            "копирования"
        },
        {
            "TrueBIM_ParaManager",
            "Параметры",
            "ParaManager",
            $"TrueBIM.App.Commands.{nameof(ParaManagerCommand)}",
            TrueBimIcon.Parameters,
            "shared parameters"
        },
        {
            "TrueBIM_CreateWorksets",
            "Администрирование",
            "Рабочие\nнаборы",
            $"TrueBIM.App.Commands.{nameof(CreateWorksetsCommand)}",
            TrueBimIcon.Worksets,
            "рабочих наборов"
        }
    };

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
    public void RibbonButtons_IncludesVoltageDropCalculationOnEomPanel()
    {
        TrueBimRibbonButtonDefinition button = Assert.Single(
            TrueBimRibbon.Buttons,
            button => string.Equals(button.Name, "TrueBIM_VoltageDropCalculation", StringComparison.Ordinal));

        Assert.Equal("ЭОМ", button.PanelName);
        Assert.Equal("Расчет\nпотери\nнапряжения", button.Text);
        Assert.Equal($"TrueBIM.App.Commands.{nameof(OpenVoltageDropCalculationCommand)}", button.CommandClassName);
        Assert.Equal(TrueBimIcon.VoltageDrop, button.Icon);
        Assert.Contains("потери напряжения", button.Tooltip, StringComparison.CurrentCultureIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(BimToolButtons))]
    public void RibbonButtons_IncludeBimToolScaffoldButtons(
        string name,
        string panelName,
        string text,
        string commandClassName,
        TrueBimIcon icon,
        string tooltipFragment)
    {
        TrueBimRibbonButtonDefinition button = Assert.Single(
            TrueBimRibbon.Buttons,
            button => string.Equals(button.Name, name, StringComparison.Ordinal));

        Assert.Equal(panelName, button.PanelName);
        Assert.Equal(text, button.Text);
        Assert.Equal(commandClassName, button.CommandClassName);
        Assert.Equal(icon, button.Icon);
        Assert.Contains(tooltipFragment, button.Tooltip, StringComparison.CurrentCultureIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(button.LongDescription));
    }

    [Fact]
    public void RibbonPanelNames_IncludeBimToolPanels()
    {
        Assert.Contains("Проверка модели", TrueBimRibbon.PanelNames);
        Assert.Contains("Параметры", TrueBimRibbon.PanelNames);
        Assert.Contains("Администрирование", TrueBimRibbon.PanelNames);
    }

    [Fact]
    public void RibbonButtons_UseKnownPanelNames()
    {
        HashSet<string> knownPanels = new(TrueBimRibbon.PanelNames, StringComparer.Ordinal);

        foreach (TrueBimRibbonButtonDefinition button in TrueBimRibbon.Buttons)
        {
            Assert.Contains(button.PanelName, knownPanels);
        }
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
    public void RibbonPanelNames_HaveUniqueNames()
    {
        string[] duplicateNames = TrueBimRibbon.PanelNames
            .GroupBy(panelName => panelName, StringComparer.Ordinal)
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
