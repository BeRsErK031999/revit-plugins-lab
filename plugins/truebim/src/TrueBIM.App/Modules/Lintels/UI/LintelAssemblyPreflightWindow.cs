using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TrueBIM.App.Modules.Lintels.Models;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;
using WpfBinding = System.Windows.Data.Binding;

namespace TrueBIM.App.Modules.Lintels.UI;

public sealed class LintelAssemblyPreflightWindow : TrueBimWindow
{
    public LintelAssemblyPreflightWindow(LintelAssemblyPreflightResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        Title = "Проверка сборок перемычек";
        Icon = IconFactory.CreateImage(TrueBimIcon.Search, TrueBimTheme.IconSizeRibbon);
        Width = 980;
        Height = 580;
        MinWidth = 780;
        MinHeight = 460;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Border summary = TrueBimUi.CreateInfoBanner(result.BuildSummary(), GetSummarySeverity(result));
        DataGrid grid = CreateResultGrid(result);

        Button closeButton = TrueBimUi.CreateSecondaryButton(
            "Закрыть",
            TrueBimIcon.Close,
            (_, _) => Close(),
            minWidth: 110);
        closeButton.IsCancel = true;

        ApplyTrueBimShell(
            TrueBimUi.CreateHeader(
                "Проверка будущих сборок",
                "Revit проверил состав, категорию именования и повторный запуск. Никакие элементы не создавались.",
                TrueBimIcon.Search),
            summary,
            grid,
            footer: TrueBimUi.CreateFooter(null, closeButton));
    }

    private static DataGrid CreateResultGrid(LintelAssemblyPreflightResult result)
    {
        DataGrid grid = new()
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserReorderColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            Style = TrueBimStyles.CreateDataGridStyle(),
            ItemsSource = result.Items,
            Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0)
        };
        grid.Columns.Add(CreateColumn("Сборка", nameof(LintelAssemblyPreflightItem.AssemblyName), 2d));
        grid.Columns.Add(CreateColumn("Кол-во", nameof(LintelAssemblyPreflightItem.MemberCount), 72));
        grid.Columns.Add(CreateColumn("ID компонентов", nameof(LintelAssemblyPreflightItem.MemberIdsDisplay), 1.6d));
        grid.Columns.Add(CreateColumn("Категория ID", nameof(LintelAssemblyPreflightItem.NamingCategoryDisplay), 105));
        grid.Columns.Add(CreateColumn("Статус", nameof(LintelAssemblyPreflightItem.StatusDisplay), 125));
        grid.Columns.Add(CreateColumn("Результат проверки", nameof(LintelAssemblyPreflightItem.Message), 3d));
        AutomationProperties.SetName(grid, "Результаты проверки сборок перемычек");
        AutomationProperties.SetHelpText(grid, "В таблице показано, какие сборки готовы, уже существуют или заблокированы Revit API.");
        return grid;
    }

    private static DataGridTextColumn CreateColumn(string header, string bindingPath, double starWidth)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new WpfBinding(bindingPath),
            Width = new DataGridLength(starWidth, DataGridLengthUnitType.Star),
            ElementStyle = CreateCellTextStyle(bindingPath)
        };
    }

    private static DataGridTextColumn CreateColumn(string header, string bindingPath, int width)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new WpfBinding(bindingPath),
            Width = new DataGridLength(width),
            ElementStyle = CreateCellTextStyle(bindingPath)
        };
    }

    private static Style CreateCellTextStyle(string bindingPath)
    {
        Style style = new(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new WpfBinding(bindingPath)));
        return style;
    }

    private static TrueBimUiSeverity GetSummarySeverity(LintelAssemblyPreflightResult result)
    {
        if (result.BlockedCount > 0)
        {
            return TrueBimUiSeverity.Warning;
        }

        return result.ReadyCount > 0
            ? TrueBimUiSeverity.Success
            : TrueBimUiSeverity.Info;
    }
}
