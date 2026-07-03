using Autodesk.Revit.DB;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.Modules.ViewVisibility.Models;
using TrueBIM.App.Modules.ViewVisibility.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
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
        Width = 540;
        Height = 680;
        MinWidth = 480;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();

        searchBox.TextChanged += (_, _) => RefreshList();
        RefreshList();
        UpdateStatus();
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
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        WpfGrid.SetRow(body, 2);
        root.Children.Add(body);

        DockPanel tools = new()
        {
            LastChildFill = true,
            Margin = new Thickness(0, 0, 0, 8)
        };
        body.Children.Add(tools);

        Button showAllButton = CreateSmallButton("Показать все", (_, _) => SetAllVisible(true));
        DockPanel.SetDock(showAllButton, Dock.Left);
        tools.Children.Add(showAllButton);

        Button hideAllButton = CreateSmallButton("Скрыть все", (_, _) => SetAllVisible(false));
        hideAllButton.Margin = new Thickness(8, 0, 12, 0);
        DockPanel.SetDock(hideAllButton, Dock.Left);
        tools.Children.Add(hideAllButton);

        searchBox.MinWidth = 180;
        searchBox.Height = 28;
        searchBox.VerticalContentAlignment = VerticalAlignment.Center;
        searchBox.ToolTip = "Фильтр категорий";
        tools.Children.Add(searchBox);

        categoryList.BorderBrush = Brushes.LightGray;
        categoryList.BorderThickness = new Thickness(1);
        categoryList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        WpfGrid.SetRow(categoryList, 1);
        body.Children.Add(categoryList);

        statusText.Foreground = Brushes.DimGray;
        statusText.Margin = new Thickness(0, 10, 0, 10);
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
        string filter = searchBox.Text.Trim();
        categoryList.Items.Clear();

        foreach (CategoryToggleRow row in rows.Where(row => MatchesFilter(row, filter)))
        {
            categoryList.Items.Add(CreateCategoryRow(row));
        }
    }

    private UIElement CreateCategoryRow(CategoryToggleRow row)
    {
        CheckBox checkBox = new()
        {
            IsChecked = row.IsVisible,
            Margin = new Thickness(8, 6, 8, 6),
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
                        Text = LocalizeCategoryType(row.Item.CategoryType),
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
        string filter = searchBox.Text.Trim();
        foreach (CategoryToggleRow row in rows.Where(row => MatchesFilter(row, filter)))
        {
            row.IsVisible = isVisible;
        }

        RefreshList();
        UpdateStatus();
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

            statusText.Text =
                $"Применено: {result.UpdatedCount}. Показано: {result.ShownCount}. Скрыто: {result.HiddenCount}. Без изменений: {result.UnchangedCount}.";
            if (result.SkippedCount > 0)
            {
                statusText.Text += $" Пропущено: {result.SkippedCount}.";
            }
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
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        int visibleCount = rows.Count(row => row.IsVisible);
        int changedCount = rows.Count(row => row.IsVisible != row.InitialIsVisible);
        statusText.Text = $"Категорий: {rows.Count}. Видимых: {visibleCount}. Изменений: {changedCount}.";
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

    private static bool MatchesFilter(CategoryToggleRow row, string filter)
    {
        return string.IsNullOrWhiteSpace(filter)
            || row.Item.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
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
}
