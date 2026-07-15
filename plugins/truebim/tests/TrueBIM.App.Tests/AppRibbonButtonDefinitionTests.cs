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
            TrueBimRibbon.ModelReviewPanelName,
            "Цвета\nпо параметрам",
            $"TrueBIM.App.Commands.{nameof(ColorByParameterCommand)}",
            TrueBimIcon.ColorByParameter,
            "раскраски"
        },
        {
            "TrueBIM_IsoFieldRebar",
            TrueBimRibbon.KrPanelName,
            "Армирование\nпо изополям",
            $"TrueBIM.App.Commands.{nameof(IsoFieldRebarCommand)}",
            TrueBimIcon.IsoFieldRebar,
            "изополям"
        },
        {
            "TrueBIM_Lintels",
            TrueBimRibbon.KrPanelName,
            "Перемычки",
            $"TrueBIM.App.Commands.{nameof(LintelsCommand)}",
            TrueBimIcon.Lintels,
            "оформления перемычек"
        },
        {
            "TrueBIM_ScheduleImport",
            TrueBimRibbon.BimDocumentationPanelName,
            "Импорт\nтаблиц",
            $"TrueBIM.App.Commands.{nameof(ScheduleImportCommand)}",
            TrueBimIcon.ScheduleImport,
            "таблиц"
        },
        {
            "TrueBIM_JoinCut",
            TrueBimRibbon.GeometryPanelName,
            "Соединить /\nВырезать",
            $"TrueBIM.App.Commands.{nameof(JoinCutCommand)}",
            TrueBimIcon.JoinCut,
            "геометрии"
        },
        {
            "TrueBIM_CopyParameters",
            TrueBimRibbon.ParametersPanelName,
            "Копирование\nпараметров",
            $"TrueBIM.App.Commands.{nameof(CopyParametersCommand)}",
            TrueBimIcon.CopyParameters,
            "копирования"
        },
        {
            "TrueBIM_ParaManager",
            TrueBimRibbon.ParametersPanelName,
            "ParaManager",
            $"TrueBIM.App.Commands.{nameof(ParaManagerCommand)}",
            TrueBimIcon.Parameters,
            "shared parameters"
        },
        {
            "TrueBIM_CreateWorksets",
            TrueBimRibbon.AdministrationPanelName,
            "Рабочие\nнаборы",
            $"TrueBIM.App.Commands.{nameof(CreateWorksetsCommand)}",
            TrueBimIcon.Worksets,
            "рабочих наборов"
        },
        {
            "TrueBIM_DatumExtents",
            TrueBimRibbon.BimViewsPanelName,
            "Оси\n2D/3D",
            $"TrueBIM.App.Commands.{nameof(DatumExtentCommand)}",
            TrueBimIcon.DatumExtents,
            "режимами"
        },
        {
            "TrueBIM_OpeningViews",
            TrueBimRibbon.BimViewsPanelName,
            "Фасады\nпроёмов",
            $"TrueBIM.App.Commands.{nameof(OpeningViewsCommand)}",
            TrueBimIcon.OpeningViews,
            "активном плане"
        },
        {
            "TrueBIM_ClashReport",
            TrueBimRibbon.BimCoordinationPanelName,
            "Отчёт\nколлизий",
            $"TrueBIM.App.Commands.{nameof(ClashReportCommand)}",
            TrueBimIcon.ClashReport,
            "коллизий"
        },
        {
            "TrueBIM_FamilyManager",
            TrueBimRibbon.BimLibraryPanelName,
            "Диспетчер\nсемейств",
            $"TrueBIM.App.Commands.{nameof(FamilyManagerCommand)}",
            TrueBimIcon.FamilyManager,
            "семейств"
        }
    };

    [Fact]
    public void RibbonButtons_IncludesViewVisibilityOnViewsPanel()
    {
        TrueBimRibbonButtonDefinition button = Assert.Single(
            TrueBimRibbon.Buttons,
            button => string.Equals(button.Name, "TrueBIM_ViewVisibility", StringComparison.Ordinal));

        Assert.Equal(TrueBimRibbon.BimViewsPanelName, button.PanelName);
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
    public void RibbonButtons_GroupsOpeningViewCreationAndAnnotation()
    {
        TrueBimRibbonButtonDefinition button = Assert.Single(
            TrueBimRibbon.Buttons,
            button => string.Equals(button.Name, "TrueBIM_OpeningViews", StringComparison.Ordinal));

        Assert.True(button.IsPulldown);
        TrueBimRibbonPulldownItemDefinition createItem = Assert.Single(
            button.Items,
            item => string.Equals(item.Name, "TrueBIM_OpeningViews_Create", StringComparison.Ordinal));
        Assert.Equal("Шаги 1–2: создать фасады", createItem.Text);
        Assert.Equal($"TrueBIM.App.Commands.{nameof(OpeningViewsCommand)}", createItem.CommandClassName);

        TrueBimRibbonPulldownItemDefinition annotateItem = Assert.Single(
            button.Items,
            item => string.Equals(item.Name, "TrueBIM_OpeningViews_Annotate", StringComparison.Ordinal));
        Assert.Equal("Шаг 3: оформить активный фасад", annotateItem.Text);
        Assert.Equal($"TrueBIM.App.Commands.{nameof(OpeningViewAnnotationCommand)}", annotateItem.CommandClassName);
        Assert.Equal(
            $"TrueBIM.App.Commands.{nameof(OpeningViewAnnotationCommandAvailability)}",
            annotateItem.AvailabilityClassName);

        TrueBimRibbonPulldownItemDefinition guideItem = Assert.Single(
            button.Items,
            item => string.Equals(item.Name, "TrueBIM_OpeningViews_Guide", StringComparison.Ordinal));
        Assert.Equal("Методичка: как работать", guideItem.Text);
        Assert.Equal($"TrueBIM.App.Commands.{nameof(OpeningViewsGuideCommand)}", guideItem.CommandClassName);
        Assert.Equal(TrueBimIcon.Help, guideItem.Icon);
        Assert.True(guideItem.BeginsGroup);
        Assert.Empty(guideItem.AvailabilityClassName);
    }

    [Fact]
    public void RibbonButtons_IncludesSheetNumberingOnDocumentationPanel()
    {
        TrueBimRibbonButtonDefinition button = Assert.Single(
            TrueBimRibbon.Buttons,
            button => string.Equals(button.Name, "TrueBIM_SheetNumbering", StringComparison.Ordinal));

        Assert.Equal(TrueBimRibbon.BimDocumentationPanelName, button.PanelName);
        Assert.Equal("Нумератор\nлистов", button.Text);
        Assert.Equal($"TrueBIM.App.Commands.{nameof(OpenSheetNumberingCommand)}", button.CommandClassName);
        Assert.Equal(TrueBimIcon.SheetNumbering, button.Icon);
        Assert.Contains("нумератор листов", button.Tooltip, StringComparison.CurrentCultureIgnoreCase);
    }

    [Fact]
    public void RibbonButtons_IncludesVoltageDropCalculationOnEomPanel()
    {
        TrueBimRibbonButtonDefinition button = Assert.Single(
            TrueBimRibbon.Buttons,
            button => string.Equals(button.Name, "TrueBIM_VoltageDropCalculation", StringComparison.Ordinal));

        Assert.Equal(TrueBimRibbon.EomPanelName, button.PanelName);
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

        Assert.Equal(TrueBimRibbon.HelpPanelName, button.PanelName);
        Assert.Equal("Логи", button.Text);
        Assert.Equal($"TrueBIM.App.Commands.{nameof(OpenTrueBimLogsCommand)}", button.CommandClassName);
        Assert.Equal(TrueBimIcon.Logs, button.Icon);
        Assert.Contains("логов", button.Tooltip, StringComparison.CurrentCultureIgnoreCase);
    }

    [Fact]
    public void RibbonButtons_IncludeSingleUnifiedPrintButton()
    {
        TrueBimRibbonButtonDefinition printButton = Assert.Single(
            TrueBimRibbon.Buttons,
            button => string.Equals(button.Name, "TrueBIM_Print", StringComparison.Ordinal));

        Assert.Equal(TrueBimRibbon.BimDocumentationPanelName, printButton.PanelName);
        Assert.Equal("Печать", printButton.Text);
        Assert.Equal($"TrueBIM.App.Commands.{nameof(OpenPrintCommand)}", printButton.CommandClassName);
        Assert.Equal(TrueBimIcon.Print, printButton.Icon);
        Assert.Contains("PDF", printButton.Tooltip, StringComparison.Ordinal);
        Assert.Contains("DWG", printButton.Tooltip, StringComparison.Ordinal);
        Assert.DoesNotContain(TrueBimRibbon.Buttons, button =>
            string.Equals(button.Name, "TrueBIM_PrintPdf", StringComparison.Ordinal)
            || string.Equals(button.Name, "TrueBIM_PrintDwg", StringComparison.Ordinal));
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
    public void RibbonPanelNames_IncludeVisiblePanelsOnly()
    {
        Assert.Contains(TrueBimRibbon.BimDocumentationPanelName, TrueBimRibbon.PanelNames);
        Assert.Contains(TrueBimRibbon.BimViewsPanelName, TrueBimRibbon.PanelNames);
        Assert.Contains(TrueBimRibbon.BimCoordinationPanelName, TrueBimRibbon.PanelNames);
        Assert.Contains(TrueBimRibbon.BimLibraryPanelName, TrueBimRibbon.PanelNames);
        Assert.Contains(TrueBimRibbon.ModelReviewPanelName, TrueBimRibbon.PanelNames);
        Assert.Contains(TrueBimRibbon.GeometryPanelName, TrueBimRibbon.PanelNames);
        Assert.Contains(TrueBimRibbon.ParametersPanelName, TrueBimRibbon.PanelNames);
        Assert.Contains(TrueBimRibbon.AdministrationPanelName, TrueBimRibbon.PanelNames);
        Assert.Contains(TrueBimRibbon.HelpPanelName, TrueBimRibbon.PanelNames);
        Assert.Contains(TrueBimRibbon.KrPanelName, TrueBimRibbon.PanelNames);
        Assert.Contains(TrueBimRibbon.EomPanelName, TrueBimRibbon.PanelNames);

        Assert.DoesNotContain(TrueBimRibbon.BimPanelName, TrueBimRibbon.PanelNames);
        Assert.DoesNotContain(TrueBimRibbon.SsPanelName, TrueBimRibbon.PanelNames);
    }

    [Fact]
    public void RibbonPanelNames_UseReviewedOrder()
    {
        string[] expectedPanelNames =
        [
            TrueBimRibbon.BimDocumentationPanelName,
            TrueBimRibbon.BimViewsPanelName,
            TrueBimRibbon.BimCoordinationPanelName,
            TrueBimRibbon.BimLibraryPanelName,
            TrueBimRibbon.ModelReviewPanelName,
            TrueBimRibbon.GeometryPanelName,
            TrueBimRibbon.ParametersPanelName,
            TrueBimRibbon.AdministrationPanelName,
            TrueBimRibbon.KrPanelName,
            TrueBimRibbon.EomPanelName,
            TrueBimRibbon.HelpPanelName
        ];

        Assert.Equal(expectedPanelNames, TrueBimRibbon.PanelNames);
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
    public void RibbonPanelNames_DoNotIncludePanelsWithoutButtons()
    {
        HashSet<string> usedPanels = new(
            TrueBimRibbon.Buttons.Select(button => button.PanelName),
            StringComparer.Ordinal);

        foreach (string panelName in TrueBimRibbon.PanelNames)
        {
            Assert.Contains(panelName, usedPanels);
        }
    }

    [Fact]
    public void RibbonPanels_KeepReviewedRootButtonDensity()
    {
        Dictionary<string, int> maxButtonCountByPanel = new(StringComparer.Ordinal)
        {
            [TrueBimRibbon.BimDocumentationPanelName] = 4,
            [TrueBimRibbon.BimViewsPanelName] = 3,
            [TrueBimRibbon.BimCoordinationPanelName] = 2,
            [TrueBimRibbon.BimLibraryPanelName] = 2,
            [TrueBimRibbon.ModelReviewPanelName] = 2,
            [TrueBimRibbon.GeometryPanelName] = 2,
            [TrueBimRibbon.ParametersPanelName] = 3,
            [TrueBimRibbon.AdministrationPanelName] = 2,
            [TrueBimRibbon.KrPanelName] = 3,
            [TrueBimRibbon.EomPanelName] = 2,
            [TrueBimRibbon.HelpPanelName] = 2
        };

        foreach (string panelName in TrueBimRibbon.PanelNames)
        {
            int buttonCount = TrueBimRibbon.Buttons.Count(button => string.Equals(button.PanelName, panelName, StringComparison.Ordinal));

            Assert.InRange(buttonCount, 1, maxButtonCountByPanel[panelName]);
        }
    }

    [Theory]
    [InlineData("TrueBIM_AutoTags")]
    [InlineData("TrueBIM_TitleBlockFill")]
    public void RibbonButtons_HideDeferredDocumentationTools(string name)
    {
        Assert.DoesNotContain(
            TrueBimRibbon.Buttons,
            button => string.Equals(button.Name, name, StringComparison.Ordinal));
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

        Assert.Contains("двери", button.Tooltip, StringComparison.CurrentCultureIgnoreCase);
        Assert.Contains("витражи", button.Tooltip, StringComparison.CurrentCultureIgnoreCase);
        Assert.Contains("фасадные", button.Tooltip, StringComparison.CurrentCultureIgnoreCase);
        Assert.Contains("crop box", button.LongDescription, StringComparison.CurrentCultureIgnoreCase);
        Assert.Contains("CSV-отчёт", button.LongDescription, StringComparison.CurrentCultureIgnoreCase);
    }
}
