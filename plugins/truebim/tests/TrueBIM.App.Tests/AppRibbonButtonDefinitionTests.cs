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
            "TrueBIM_IsoFieldRebar",
            "БИМ",
            "Армирование\nпо изополям",
            $"TrueBIM.App.Commands.{nameof(IsoFieldRebarCommand)}",
            TrueBimIcon.IsoFieldRebar,
            "изополям"
        },
        {
            "TrueBIM_JoinCut",
            "Геометрия",
            "Соединить /\nВырезать",
            $"TrueBIM.App.Commands.{nameof(JoinCutCommand)}",
            TrueBimIcon.JoinCut,
            "геометрии"
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
        },
        {
            "TrueBIM_AutoMepDimensions",
            "Оформление",
            "Авторазмеры\nMEP",
            $"TrueBIM.App.Commands.{nameof(AutoMepDimensionsCommand)}",
            TrueBimIcon.AutoDimensions,
            "авторазмеров"
        },
        {
            "TrueBIM_AutoTags",
            "Оформление",
            "Автомарки",
            $"TrueBIM.App.Commands.{nameof(AutoTagCommand)}",
            TrueBimIcon.AutoTags,
            "марок"
        },
        {
            "TrueBIM_TitleBlockFill",
            "Оформление",
            "Оформить\nштамп",
            $"TrueBIM.App.Commands.{nameof(TitleBlockFillCommand)}",
            TrueBimIcon.TitleBlock,
            "штампа"
        },
        {
            "TrueBIM_DatumExtents",
            "Виды",
            "Оси\n2D/3D",
            $"TrueBIM.App.Commands.{nameof(DatumExtentCommand)}",
            TrueBimIcon.DatumExtents,
            "режимами"
        },
        {
            "TrueBIM_OpeningViews",
            "Виды",
            "Фасады\nдверей/окон",
            $"TrueBIM.App.Commands.{nameof(OpeningViewsCommand)}",
            TrueBimIcon.OpeningViews,
            "активном плане"
        },
        {
            "TrueBIM_ClashReport",
            "Координация",
            "Отчёт\nколлизий",
            $"TrueBIM.App.Commands.{nameof(ClashReportCommand)}",
            TrueBimIcon.ClashReport,
            "коллизий"
        },
        {
            "TrueBIM_FamilyManager",
            "Библиотека",
            "Диспетчер\nсемейств",
            $"TrueBIM.App.Commands.{nameof(FamilyManagerCommand)}",
            TrueBimIcon.FamilyManager,
            "семейств"
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
        Assert.True(button.IsPulldown);
        Assert.Contains("видимость", button.Tooltip, StringComparison.CurrentCultureIgnoreCase);

        TrueBimRibbonPulldownItemDefinition allItem = Assert.Single(
            button.Items,
            item => string.Equals(item.Name, "TrueBIM_ViewVisibility_All", StringComparison.Ordinal));
        Assert.Equal("Все", allItem.Text);
        Assert.Equal($"TrueBIM.App.Commands.{nameof(OpenViewVisibilityCommand)}", allItem.CommandClassName);
        Assert.Contains(button.Items, item => string.Equals(item.Text, "Окна", StringComparison.Ordinal));
        Assert.Contains(button.Items, item => string.Equals(item.Text, "Стены", StringComparison.Ordinal));
        Assert.Contains(button.Items, item => string.Equals(item.Text, "Воздуховоды", StringComparison.Ordinal));
        Assert.Contains(button.Items, item => string.Equals(item.Text, "Аннотации", StringComparison.Ordinal));
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

    [Fact]
    public void RibbonButtons_IncludesQuickLogAccessOnHelpPanel()
    {
        TrueBimRibbonButtonDefinition button = Assert.Single(
            TrueBimRibbon.Buttons,
            button => string.Equals(button.Name, "TrueBIM_OpenLogs", StringComparison.Ordinal));

        Assert.Equal("Помощь", button.PanelName);
        Assert.Equal("Логи", button.Text);
        Assert.Equal($"TrueBIM.App.Commands.{nameof(OpenTrueBimLogsCommand)}", button.CommandClassName);
        Assert.Equal(TrueBimIcon.Logs, button.Icon);
        Assert.Contains("логов", button.Tooltip, StringComparison.CurrentCultureIgnoreCase);
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
        Assert.Contains("Оформление", TrueBimRibbon.PanelNames);
        Assert.Contains("Виды", TrueBimRibbon.PanelNames);
        Assert.Contains("Координация", TrueBimRibbon.PanelNames);
        Assert.Contains("Библиотека", TrueBimRibbon.PanelNames);
        Assert.Contains("Проверка модели", TrueBimRibbon.PanelNames);
        Assert.Contains("Геометрия", TrueBimRibbon.PanelNames);
        Assert.Contains("Параметры", TrueBimRibbon.PanelNames);
        Assert.Contains("Администрирование", TrueBimRibbon.PanelNames);
        Assert.Contains("Помощь", TrueBimRibbon.PanelNames);
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
            .Select(button => button.Name)
            .Concat(TrueBimRibbon.Buttons.SelectMany(button => button.Items.Select(item => item.Name)))
            .GroupBy(name => name, StringComparer.Ordinal)
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

            foreach (TrueBimRibbonPulldownItemDefinition item in button.Items)
            {
                Assert.StartsWith("TrueBIM.App.Commands.", item.CommandClassName, StringComparison.Ordinal);
                Assert.EndsWith("Command", item.CommandClassName, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void DatumExtents_IsHiddenForRevit2022AndEarlier()
    {
        TrueBimRibbonButtonDefinition button = Assert.Single(
            TrueBimRibbon.Buttons,
            button => string.Equals(button.Name, "TrueBIM_DatumExtents", StringComparison.Ordinal));

        Assert.False(TrueBimRibbon.IsButtonAvailableForRevitVersion(button, "2022"));
        Assert.False(TrueBimRibbon.IsButtonAvailableForRevitVersion(button, "2021"));
        Assert.True(TrueBimRibbon.IsButtonAvailableForRevitVersion(button, "2023"));
        Assert.True(TrueBimRibbon.IsButtonAvailableForRevitVersion(button, "2025"));
    }

    [Fact]
    public void DatumExtents_UsesActiveViewAvailability()
    {
        TrueBimRibbonButtonDefinition button = Assert.Single(
            TrueBimRibbon.Buttons,
            button => string.Equals(button.Name, "TrueBIM_DatumExtents", StringComparison.Ordinal));

        Assert.Equal($"TrueBIM.App.Commands.{nameof(DatumExtentCommandAvailability)}", button.AvailabilityClassName);
        Assert.Contains("2D-виде", button.LongDescription, StringComparison.CurrentCultureIgnoreCase);
    }

    [Fact]
    public void OpeningViews_ExplainsFacadeWorkflowInRibbonHelp()
    {
        TrueBimRibbonButtonDefinition button = Assert.Single(
            TrueBimRibbon.Buttons,
            button => string.Equals(button.Name, "TrueBIM_OpeningViews", StringComparison.Ordinal));

        Assert.Contains("двери и окна", button.Tooltip, StringComparison.CurrentCultureIgnoreCase);
        Assert.Contains("фасадные", button.Tooltip, StringComparison.CurrentCultureIgnoreCase);
        Assert.Contains("crop box", button.LongDescription, StringComparison.CurrentCultureIgnoreCase);
        Assert.Contains("CSV-отчёт", button.LongDescription, StringComparison.CurrentCultureIgnoreCase);
    }
}
