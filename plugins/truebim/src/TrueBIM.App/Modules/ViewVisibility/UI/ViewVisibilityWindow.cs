using Autodesk.Revit.DB;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.Modules.ViewVisibility.Models;
using TrueBIM.App.Modules.ViewVisibility.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using WpfColor = System.Windows.Media.Color;
using WpfGrid = System.Windows.Controls.Grid;

namespace TrueBIM.App.Modules.ViewVisibility.UI;

public sealed class ViewVisibilityWindow : Window
{
    private readonly Document document;
    private readonly View view;
    private readonly ViewCategoryVisibilityService service;
    private readonly ITrueBimLogger logger;
    private readonly List<CategoryToggleRow> rows;
    private readonly ListBox categoryList = new();
    private readonly TextBlock statusText = new();
    private readonly TextBox searchBox = new();
    private readonly ComboBox categoryTypeFilter = new();
    private string? applySummary;

    public ViewVisibilityWindow(
        Document document,
        View view,
        IReadOnlyList<ViewCategoryVisibilityItem> categories,
        ViewCategoryVisibilityService service,
        ITrueBimLogger logger)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        this.view = view ?? throw new ArgumentNullException(nameof(view));
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        rows = categories
            .Select(category => new CategoryToggleRow(category))
            .ToList();

        Title = "Видимость";
        Width = 620;
        Height = 720;
        MinWidth = 540;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        InitializeCategoryTypeFilter();
        Content = CreateContent();

        searchBox.TextChanged += (_, _) => RefreshList();
        categoryTypeFilter.SelectionChanged += (_, _) => RefreshList();
        RefreshList();
    }

    private UIElement CreateContent()
    {
        WpfGrid root = new()
        {
            Margin = new Thickness(16)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock title = new()
        {
            Text = "Видимость категорий",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.Children.Add(title);

        TextBlock viewName = new()
        {
            Text = $"Активный вид: {view.Name}",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 12)
        };
        WpfGrid.SetRow(viewName, 1);
        root.Children.Add(viewName);

        WpfGrid body = new();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        WpfGrid.SetRow(body, 2);
        root.Children.Add(body);

        StackPanel actionBar = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        body.Children.Add(actionBar);

        Button showAllButton = CreateSmallButton("Показать все", (_, _) => SetAllVisible(true));
        actionBar.Children.Add(showAllButton);

        Button hideAllButton = CreateSmallButton("Скрыть все", (_, _) => SetAllVisible(false));
        hideAllButton.Margin = new Thickness(8, 0, 16, 0);
        actionBar.Children.Add(hideAllButton);

        actionBar.Children.Add(new TextBlock
        {
            Text = "Группа:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });

        categoryTypeFilter.Width = 190;
        categoryTypeFilter.Height = 28;
        categoryTypeFilter.VerticalContentAlignment = VerticalAlignment.Center;
        actionBar.Children.Add(categoryTypeFilter);

        DockPanel searchBar = new()
        {
            LastChildFill = true,
            Margin = new Thickness(0, 0, 0, 8)
        };
        WpfGrid.SetRow(searchBar, 1);
        body.Children.Add(searchBar);

        Button clearSearchButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Очистить"),
            Height = 28,
            MinWidth = 92,
            Margin = new Thickness(8, 0, 0, 0)
        };
        clearSearchButton.Click += (_, _) => searchBox.Clear();
        DockPanel.SetDock(clearSearchButton, Dock.Right);
        searchBar.Children.Add(clearSearchButton);

        TextBlock searchLabel = new()
        {
            Text = "Поиск:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        DockPanel.SetDock(searchLabel, Dock.Left);
        searchBar.Children.Add(searchLabel);

        searchBox.MinWidth = 180;
        searchBox.Height = 28;
        searchBox.VerticalContentAlignment = VerticalAlignment.Center;
        searchBox.ToolTip = "Поиск категории по названию или группе.";
        searchBar.Children.Add(searchBox);

        categoryList.BorderBrush = Brushes.LightGray;
        categoryList.BorderThickness = new Thickness(1);
        categoryList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        WpfGrid.SetRow(categoryList, 2);
        body.Children.Add(categoryList);

        statusText.Foreground = Brushes.DimGray;
        statusText.Margin = new Thickness(0, 10, 0, 10);
        statusText.TextWrapping = TextWrapping.Wrap;
        WpfGrid.SetRow(statusText, 3);
        root.Children.Add(statusText);

        StackPanel footer = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        WpfGrid.SetRow(footer, 4);
        root.Children.Add(footer);

        Button applyButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Apply, "Применить"),
            MinWidth = 120,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0)
        };
        applyButton.Click += (_, _) => ApplyChanges();
        footer.Children.Add(applyButton);

        Button closeButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Закрыть"),
            MinWidth = 110,
            Height = 32,
            IsCancel = true
        };
        closeButton.Click += (_, _) => Close();
        footer.Children.Add(closeButton);

        return root;
    }

    private void RefreshList()
    {
        IReadOnlyList<CategoryToggleRow> filteredRows = GetFilteredRows();
        categoryList.Items.Clear();

        foreach (IGrouping<CategoryType, CategoryToggleRow> group in filteredRows
            .GroupBy(row => row.Item.CategoryType)
            .OrderBy(group => GetCategoryTypeOrder(group.Key)))
        {
            IReadOnlyList<CategoryToggleRow> groupRows = group.ToList();
            categoryList.Items.Add(CreateGroupHeader(group.Key, groupRows));
            foreach (CategoryToggleRow row in groupRows)
            {
                categoryList.Items.Add(CreateCategoryRow(row));
            }
        }

        UpdateStatus(filteredRows);
    }

    private UIElement CreateGroupHeader(CategoryType categoryType, IReadOnlyList<CategoryToggleRow> groupRows)
    {
        DockPanel content = new()
        {
            LastChildFill = true,
            Background = new SolidColorBrush(WpfColor.FromRgb(242, 244, 247)),
            Margin = new Thickness(0, 6, 0, 2),
            MinHeight = 28
        };

        TextBlock summary = new()
        {
            Text = $"{groupRows.Count} / видно {groupRows.Count(row => row.IsVisible)}",
            Foreground = Brushes.DimGray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 10, 0)
        };
        DockPanel.SetDock(summary, Dock.Right);
        content.Children.Add(summary);

        content.Children.Add(new TextBlock
        {
            Text = LocalizeCategoryType(categoryType),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0)
        });

        return new ListBoxItem
        {
            Content = content,
            Padding = new Thickness(0),
            Focusable = false,
            IsHitTestVisible = false
        };
    }

    private UIElement CreateCategoryRow(CategoryToggleRow row)
    {
        CheckBox checkBox = new()
        {
            IsChecked = row.IsVisible,
            Margin = new Thickness(22, 5, 8, 5),
            VerticalAlignment = VerticalAlignment.Center,
            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children =
                {
                    new TextBlock
                    {
                        Text = row.Item.Name,
                        FontWeight = FontWeights.SemiBold
                    },
                    new TextBlock
                    {
                        Text = row.IsVisible == row.InitialIsVisible ? "Без изменений" : "Изменено",
                        Foreground = Brushes.DimGray,
                        FontSize = 11
                    }
                }
            }
        };
        checkBox.Checked += (_, _) => UpdateRowVisibility(row, isVisible: true);
        checkBox.Unchecked += (_, _) => UpdateRowVisibility(row, isVisible: false);

        return checkBox;
    }

    private void SetAllVisible(bool isVisible)
    {
        IReadOnlyList<CategoryToggleRow> filteredRows = GetFilteredRows();
        foreach (CategoryToggleRow row in filteredRows)
        {
            row.IsVisible = isVisible;
        }

        applySummary = null;
        RefreshList();
    }

    private void ApplyChanges()
    {
        try
        {
            IReadOnlyList<ViewCategoryVisibilityUpdate> updates = rows
                .Where(row => row.IsVisible != row.InitialIsVisible)
                .Select(row => new ViewCategoryVisibilityUpdate(row.Item.CategoryId, row.IsVisible))
                .ToList();

            if (updates.Count == 0)
            {
                statusText.Text = "Нет изменений для применения.";
                return;
            }

            ViewCategoryVisibilityApplyResult result = service.Apply(document, view, updates);
            foreach (CategoryToggleRow row in rows)
            {
                row.InitialIsVisible = row.IsVisible;
            }

            applySummary =
                $"Применено: {result.UpdatedCount}. Показано: {result.ShownCount}. Скрыто: {result.HiddenCount}. Без изменений: {result.UnchangedCount}.";
            if (result.SkippedCount > 0)
            {
                applySummary += $" Пропущено: {result.SkippedCount}.";
            }

            RefreshList();
        }
        catch (Exception exception)
        {
            logger.Error("Failed to apply category visibility changes.", exception);
            Autodesk.Revit.UI.TaskDialog.Show("Видимость", "Не удалось применить изменения видимости. Используйте логи для диагностики.");
        }
    }

    private void UpdateRowVisibility(CategoryToggleRow row, bool isVisible)
    {
        row.IsVisible = isVisible;
        applySummary = null;
        RefreshList();
    }

    private void UpdateStatus(IReadOnlyList<CategoryToggleRow> filteredRows)
    {
        int visibleCount = rows.Count(row => row.IsVisible);
        int changedCount = rows.Count(row => row.IsVisible != row.InitialIsVisible);
        int filteredChangedCount = filteredRows.Count(row => row.IsVisible != row.InitialIsVisible);
        string shownText = filteredRows.Count == rows.Count
            ? $"Показано категорий: {rows.Count}"
            : $"Показано категорий: {filteredRows.Count} из {rows.Count}";
        statusText.Text =
            $"{shownText}. Всего видимых: {visibleCount}. Всего скрытых: {rows.Count - visibleCount}. Изменений: {changedCount}.";

        if (filteredRows.Count != rows.Count)
        {
            statusText.Text += $" Изменений в фильтре: {filteredChangedCount}.";
        }

        if (!string.IsNullOrWhiteSpace(applySummary))
        {
            statusText.Text += $" {applySummary}";
        }
    }

    private static Button CreateSmallButton(string text, RoutedEventHandler clickHandler)
    {
        Button button = new()
        {
            Content = text,
            Height = 28,
            MinWidth = 92
        };
        button.Click += clickHandler;
        return button;
    }

    private void InitializeCategoryTypeFilter()
    {
        categoryTypeFilter.Items.Add(new CategoryTypeFilterOption(null, "Все группы"));
        foreach (CategoryType categoryType in rows
            .Select(row => row.Item.CategoryType)
            .Distinct()
            .OrderBy(GetCategoryTypeOrder))
        {
            categoryTypeFilter.Items.Add(new CategoryTypeFilterOption(categoryType, LocalizeCategoryType(categoryType)));
        }

        categoryTypeFilter.SelectedIndex = 0;
    }

    private IReadOnlyList<CategoryToggleRow> GetFilteredRows()
    {
        string filter = searchBox.Text.Trim();
        CategoryType? selectedCategoryType = GetSelectedCategoryType();

        return rows
            .Where(row => selectedCategoryType is null || row.Item.CategoryType == selectedCategoryType)
            .Where(row => MatchesSearch(row, filter))
            .ToList();
    }

    private CategoryType? GetSelectedCategoryType()
    {
        return categoryTypeFilter.SelectedItem is CategoryTypeFilterOption option
            ? option.CategoryType
            : null;
    }

    private static bool MatchesSearch(CategoryToggleRow row, string filter)
    {
        return string.IsNullOrWhiteSpace(filter)
            || row.Item.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
            || LocalizeCategoryType(row.Item.CategoryType).IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    private static int GetCategoryTypeOrder(CategoryType categoryType)
    {
        return categoryType switch
        {
            CategoryType.Model => 0,
            CategoryType.Annotation => 1,
            CategoryType.AnalyticalModel => 2,
            _ => 3
        };
    }

    private static string LocalizeCategoryType(CategoryType categoryType)
    {
        return categoryType switch
        {
            CategoryType.Model => "Модель",
            CategoryType.Annotation => "Аннотации",
            CategoryType.AnalyticalModel => "Аналитическая модель",
            _ => "Категория"
        };
    }

    private sealed class CategoryToggleRow
    {
        public CategoryToggleRow(ViewCategoryVisibilityItem item)
        {
            Item = item;
            IsVisible = item.IsVisible;
            InitialIsVisible = item.IsVisible;
        }

        public ViewCategoryVisibilityItem Item { get; }

        public bool IsVisible { get; set; }

        public bool InitialIsVisible { get; set; }
    }

    private sealed class CategoryTypeFilterOption
    {
        public CategoryTypeFilterOption(CategoryType? categoryType, string displayName)
        {
            CategoryType = categoryType;
            DisplayName = displayName;
        }

        public CategoryType? CategoryType { get; }

        private string DisplayName { get; }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
