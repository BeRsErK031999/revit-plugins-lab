using System.Windows;
using System.Windows.Controls;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;
using WpfGrid = System.Windows.Controls.Grid;

namespace TrueBIM.App.Modules.BimTools.ParaManager.UI;

public sealed class ParameterCategorySelectionWindow : TrueBimWindow
{
    private readonly IReadOnlyList<string> categoryNames;
    private readonly HashSet<string> selectedCategoryNames;
    private readonly TextBox filterInput = new();
    private readonly ListBox categoryList = new();

    public ParameterCategorySelectionWindow(IReadOnlyList<string> categoryNames, IReadOnlyList<string> selectedCategoryNames)
    {
        this.categoryNames = categoryNames ?? throw new ArgumentNullException(nameof(categoryNames));
        this.selectedCategoryNames = new HashSet<string>(selectedCategoryNames ?? [], StringComparer.CurrentCultureIgnoreCase);

        Title = "Категории параметров";
        Icon = IconFactory.CreateImage(TrueBimIcon.Parameters, 32);
        Width = 520;
        Height = 640;
        MinWidth = 460;
        MinHeight = 520;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();
        RefreshList();
    }

    public IReadOnlyList<string> SelectedCategoryNames => selectedCategoryNames
        .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    private UIElement CreateContent()
    {
        WpfGrid root = new()
        {
            Margin = new Thickness(0)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        filterInput.MinHeight = TrueBimTheme.ControlHeight32;
        filterInput.Style = TrueBimStyles.CreateTextBoxStyle();
        filterInput.VerticalContentAlignment = VerticalAlignment.Center;
        filterInput.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8);
        filterInput.ToolTip = "Фильтр по названию категории Revit.";
        filterInput.TextChanged += (_, _) => RefreshList();

        StackPanel toolbar = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };
        Button selectAllButton = CreateSmallButton(
            "Все видимые",
            TrueBimIcon.Check,
            (_, _) => SetSelection(true),
            "Выбрать все категории, которые видны с текущим фильтром.");
        toolbar.Children.Add(selectAllButton);
        Button clearButton = CreateSmallButton(
            "Снять видимые",
            TrueBimIcon.Close,
            (_, _) => SetSelection(false),
            "Снять выбор со всех категорий, которые видны с текущим фильтром.");
        clearButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        toolbar.Children.Add(clearButton);

        categoryList.Style = TrueBimStyles.CreateListBoxStyle();
        categoryList.ToolTip = "Отмеченные категории будут записаны в строку параметра как список Categories.";
        WpfGrid.SetRow(categoryList, 1);
        root.Children.Add(categoryList);

        DockPanel topPanel = new()
        {
            LastChildFill = true
        };
        DockPanel.SetDock(toolbar, Dock.Bottom);
        topPanel.Children.Add(toolbar);
        topPanel.Children.Add(filterInput);
        WpfGrid.SetRow(topPanel, 0);
        root.Children.Add(topPanel);

        Button applyButton = TrueBimUi.CreatePrimaryButton("Сохранить", TrueBimIcon.Apply, minWidth: 130);
        applyButton.ToolTip = "Сохранить выбранный набор категорий для ParaManager.";
        applyButton.Click += (_, _) => DialogResult = true;

        Button cancelButton = TrueBimUi.CreateSecondaryButton("Отмена", TrueBimIcon.Close, minWidth: 110);
        cancelButton.IsCancel = true;
        cancelButton.ToolTip = "Закрыть окно без изменения выбранных категорий.";
        cancelButton.Click += (_, _) => DialogResult = false;

        return BuildShell(
            header: TrueBimUi.CreateHeader(
                Title,
                "Выбранный список можно применить к выделенным строкам импорта ParaManager.",
                TrueBimIcon.Parameters),
            commandBar: null,
            body: root,
            status: null,
            footer: TrueBimUi.CreateFooter(null, applyButton, cancelButton));
    }

    private void RefreshList()
    {
        categoryList.Items.Clear();
        foreach (string categoryName in GetVisibleCategoryNames())
        {
            CheckBox checkBox = new()
            {
                Content = categoryName,
                IsChecked = selectedCategoryNames.Contains(categoryName),
                Margin = new Thickness(TrueBimTheme.Spacing8, TrueBimTheme.Spacing4, TrueBimTheme.Spacing8, TrueBimTheme.Spacing4),
                Style = TrueBimStyles.CreateCheckBoxStyle(),
                Tag = categoryName,
                ToolTip = "Включить категорию в привязку project parameter."
            };
            checkBox.Checked += (_, _) => selectedCategoryNames.Add(categoryName);
            checkBox.Unchecked += (_, _) => selectedCategoryNames.Remove(categoryName);
            categoryList.Items.Add(checkBox);
        }
    }

    private void SetSelection(bool isSelected)
    {
        IReadOnlyList<string> visibleCategoryNames = GetVisibleCategoryNames();
        if (isSelected)
        {
            foreach (string categoryName in visibleCategoryNames)
            {
                selectedCategoryNames.Add(categoryName);
            }
        }
        else
        {
            foreach (string categoryName in visibleCategoryNames)
            {
                selectedCategoryNames.Remove(categoryName);
            }
        }

        RefreshList();
    }

    private IReadOnlyList<string> GetVisibleCategoryNames()
    {
        string filter = filterInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(filter))
        {
            return categoryNames;
        }

        return categoryNames
            .Where(categoryName => categoryName.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0)
            .ToList();
    }

    private static Button CreateSmallButton(string text, TrueBimIcon icon, RoutedEventHandler clickHandler, string? toolTip = null)
    {
        Button button = TrueBimUi.CreateSecondaryButton(text, icon, minWidth: 90);
        button.ToolTip = toolTip;
        button.Click += clickHandler;
        return button;
    }
}
