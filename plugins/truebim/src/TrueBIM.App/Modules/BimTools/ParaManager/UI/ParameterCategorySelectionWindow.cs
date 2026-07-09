using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.UI;
using WpfGrid = System.Windows.Controls.Grid;

namespace TrueBIM.App.Modules.BimTools.ParaManager.UI;

public sealed class ParameterCategorySelectionWindow : Window
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
            Margin = new Thickness(16)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        StackPanel header = new();
        header.Children.Add(new TextBlock
        {
            Text = "Категории параметров",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = "Выбранный список можно применить к выделенным строкам импорта ParaManager.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12)
        });
        root.Children.Add(header);

        filterInput.Height = 30;
        filterInput.VerticalContentAlignment = VerticalAlignment.Center;
        filterInput.Margin = new Thickness(0, 0, 0, 8);
        filterInput.ToolTip = "Фильтр по названию категории Revit.";
        filterInput.TextChanged += (_, _) => RefreshList();
        WpfGrid.SetRow(filterInput, 1);
        root.Children.Add(filterInput);

        StackPanel toolbar = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Button selectAllButton = CreateSmallButton(
            "Все видимые",
            (_, _) => SetSelection(true),
            "Выбрать все категории, которые видны с текущим фильтром.");
        toolbar.Children.Add(selectAllButton);
        Button clearButton = CreateSmallButton(
            "Снять видимые",
            (_, _) => SetSelection(false),
            "Снять выбор со всех категорий, которые видны с текущим фильтром.");
        clearButton.Margin = new Thickness(8, 0, 0, 0);
        toolbar.Children.Add(clearButton);
        WpfGrid.SetRow(toolbar, 2);
        root.Children.Add(toolbar);

        categoryList.BorderBrush = Brushes.LightGray;
        categoryList.BorderThickness = new Thickness(1);
        categoryList.ToolTip = "Отмеченные категории будут записаны в строку параметра как список Categories.";
        WpfGrid.SetRow(categoryList, 3);
        root.Children.Add(categoryList);

        StackPanel footer = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Button applyButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Apply, "Сохранить"),
            MinWidth = 130,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Сохранить выбранный набор категорий для ParaManager."
        };
        applyButton.Click += (_, _) => DialogResult = true;
        footer.Children.Add(applyButton);

        Button cancelButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Отмена"),
            MinWidth = 110,
            Height = 32,
            IsCancel = true,
            ToolTip = "Закрыть окно без изменения выбранных категорий."
        };
        cancelButton.Click += (_, _) => DialogResult = false;
        footer.Children.Add(cancelButton);
        WpfGrid.SetRow(footer, 4);
        root.Children.Add(footer);

        return root;
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
                Margin = new Thickness(8, 4, 8, 4),
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

    private static Button CreateSmallButton(string text, RoutedEventHandler clickHandler, string? toolTip = null)
    {
        Button button = new()
        {
            Content = text,
            Height = 28,
            MinWidth = 90,
            ToolTip = toolTip
        };
        button.Click += clickHandler;
        return button;
    }
}
