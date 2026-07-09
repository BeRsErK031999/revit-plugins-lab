using Autodesk.Revit.DB;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.Modules.ViewVisibility.Models;
using TrueBIM.App.Modules.ViewVisibility.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;
using MediaColor = System.Windows.Media.Color;
using WpfGrid = System.Windows.Controls.Grid;

namespace TrueBIM.App.Modules.ViewVisibility.UI;

public sealed class ViewVisibilityWindow : TrueBimWindow
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
    private static readonly MediaColor VisibleEyeColor = TrueBimTheme.InfoColor;
    private static readonly MediaColor HiddenEyeColor = TrueBimTheme.TextMutedColor;
    private static readonly Brush VisibleBadgeBackground = TrueBimBrushes.InfoBackground;
    private static readonly Brush VisibleBadgeBorder = TrueBimBrushes.Info;
    private static readonly Brush VisibleBadgeForeground = TrueBimBrushes.Info;
    private static readonly Brush HiddenBadgeBackground = TrueBimBrushes.SurfaceAlt;
    private static readonly Brush HiddenBadgeBorder = TrueBimBrushes.Border;
    private static readonly Brush HiddenBadgeForeground = TrueBimBrushes.TextSecondary;

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
        Icon = IconFactory.CreateImage(TrueBimIcon.Visibility, 32);
        Width = 620;
        Height = 720;
        MinWidth = 540;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ApplySharedControlStyles();
        InitializeCategoryTypeFilter();
        Content = CreateContent();

        searchBox.TextChanged += (_, _) => RefreshList();
        categoryTypeFilter.SelectionChanged += (_, _) => RefreshList();
        RefreshList();
    }

    private UIElement CreateContent()
    {
        return BuildShell(
            header: TrueBimUi.CreateHeader(
                "Видимость категорий",
                $"Активный вид: {view.Name}",
                TrueBimIcon.Visibility),
            commandBar: CreateCommandPanel(),
            body: CreateCategorySection(),
            status: CreateStatus(),
            footer: CreateFooter());
    }

    private UIElement CreateCommandPanel()
    {
        StackPanel panel = new()
        {
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12)
        };
        panel.Children.Add(CreateActionBar());
        panel.Children.Add(CreateSearchBar());
        return panel;
    }

    private UIElement CreateActionBar()
    {
        StackPanel actionBar = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };

        Button showAllButton = CreateSmallButton("Показать все", TrueBimIcon.Visibility, (_, _) => SetAllVisible(true));
        actionBar.Children.Add(showAllButton);

        Button hideAllButton = CreateSmallButton("Скрыть все", TrueBimIcon.Close, (_, _) => SetAllVisible(false));
        hideAllButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, TrueBimTheme.Spacing16, 0);
        actionBar.Children.Add(hideAllButton);

        actionBar.Children.Add(new TextBlock
        {
            Text = "Группа:",
            Foreground = TrueBimBrushes.TextSecondary,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, 0)
        });

        categoryTypeFilter.Width = 190;
        categoryTypeFilter.MinHeight = TrueBimTheme.ControlHeight32;
        categoryTypeFilter.VerticalContentAlignment = VerticalAlignment.Center;
        actionBar.Children.Add(categoryTypeFilter);
        return actionBar;
    }

    private UIElement CreateSearchBar()
    {
        DockPanel searchBar = new()
        {
            LastChildFill = true,
            Margin = new Thickness(0)
        };

        Button clearSearchButton = TrueBimUi.CreateSecondaryButton("Очистить", TrueBimIcon.Close, minWidth: 100);
        clearSearchButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        clearSearchButton.Click += (_, _) => searchBox.Clear();
        DockPanel.SetDock(clearSearchButton, Dock.Right);
        searchBar.Children.Add(clearSearchButton);

        TextBlock searchLabel = new()
        {
            Text = "Поиск:",
            Foreground = TrueBimBrushes.TextSecondary,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, 0)
        };
        DockPanel.SetDock(searchLabel, Dock.Left);
        searchBar.Children.Add(searchLabel);

        searchBox.MinWidth = 180;
        searchBox.MinHeight = TrueBimTheme.ControlHeight32;
        searchBox.VerticalContentAlignment = VerticalAlignment.Center;
        searchBox.ToolTip = "Поиск категории по названию или группе.";
        searchBar.Children.Add(searchBox);
        return searchBar;
    }

    private UIElement CreateCategoryList()
    {
        categoryList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        return categoryList;
    }

    private UIElement CreateCategorySection()
    {
        WpfGrid content = new()
        {
            Margin = TrueBimTheme.SectionPadding
        };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        content.Children.Add(new TextBlock
        {
            Text = "Категории вида",
            FontSize = TrueBimTheme.SectionTitleFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12)
        });

        UIElement list = CreateCategoryList();
        WpfGrid.SetRow(list, 1);
        content.Children.Add(list);

        return new Border
        {
            Background = TrueBimBrushes.Surface,
            BorderBrush = TrueBimBrushes.Border,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Child = content
        };
    }

    private UIElement CreateStatus()
    {
        statusText.Foreground = TrueBimBrushes.TextPrimary;
        statusText.TextWrapping = TextWrapping.Wrap;
        return TrueBimUi.CreateInfoBanner(statusText, TrueBimUiSeverity.Info);
    }

    private UIElement CreateFooter()
    {
        Button applyButton = TrueBimUi.CreatePrimaryButton("Применить", TrueBimIcon.Apply, minWidth: 120);
        applyButton.Click += (_, _) => ApplyChanges();

        Button closeButton = TrueBimUi.CreateSecondaryButton("Закрыть", TrueBimIcon.Close);
        closeButton.IsCancel = true;
        closeButton.Click += (_, _) => Close();

        return TrueBimUi.CreateFooter(null, applyButton, closeButton);
    }

    private void ApplySharedControlStyles()
    {
        categoryList.Style = TrueBimStyles.CreateListBoxStyle();
        searchBox.Style = TrueBimStyles.CreateTextBoxStyle();
        categoryTypeFilter.Style = TrueBimStyles.CreateComboBoxStyle();
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
            Background = TrueBimBrushes.SurfaceAlt,
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, TrueBimTheme.Spacing4),
            MinHeight = 28
        };

        TextBlock summary = new()
        {
            Text = $"{groupRows.Count} / видно {groupRows.Count(row => row.IsVisible)}",
            Foreground = TrueBimBrushes.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(TrueBimTheme.Spacing12, 0, TrueBimTheme.Spacing12, 0)
        };
        DockPanel.SetDock(summary, Dock.Right);
        content.Children.Add(summary);

        content.Children.Add(new TextBlock
        {
            Text = LocalizeCategoryType(categoryType),
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(TrueBimTheme.Spacing12, 0, TrueBimTheme.Spacing12, 0)
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
        DockPanel content = new()
        {
            LastChildFill = true,
            MinHeight = 42,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        Border statusBadge = CreateVisibilityBadge(row.IsVisible);
        DockPanel.SetDock(statusBadge, Dock.Right);
        content.Children.Add(statusBadge);

        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 12, 0),
            Children =
            {
                new TextBlock
                {
                    Text = row.Item.Name,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = row.IsVisible ? TrueBimBrushes.TextPrimary : TrueBimBrushes.TextMuted,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                new TextBlock
                {
                    Text = row.IsVisible == row.InitialIsVisible ? "Без изменений" : "Изменено",
                    Foreground = TrueBimBrushes.TextSecondary,
                    FontSize = TrueBimTheme.CaptionFontSize
                }
            }
        });

        CheckBox checkBox = new()
        {
            IsChecked = row.IsVisible,
            Margin = new Thickness(22, 5, 8, 5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Style = TrueBimStyles.CreateCheckBoxStyle(),
            ToolTip = row.IsVisible ? "Категория включена на активном виде." : "Категория выключена на активном виде.",
            Content = content
        };
        checkBox.Checked += (_, _) => UpdateRowVisibility(row, isVisible: true);
        checkBox.Unchecked += (_, _) => UpdateRowVisibility(row, isVisible: false);

        return checkBox;
    }

    private static Border CreateVisibilityBadge(bool isVisible)
    {
        StackPanel content = new()
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(IconFactory.Create(
            TrueBimIcon.Visibility,
            isVisible ? VisibleEyeColor : HiddenEyeColor,
            16));
        content.Children.Add(new TextBlock
        {
            Text = isVisible ? "Будет видно" : "Будет скрыто",
            Foreground = isVisible ? VisibleBadgeForeground : HiddenBadgeForeground,
            FontSize = TrueBimTheme.CaptionFontSize,
            FontWeight = FontWeights.SemiBold,
            MinWidth = 82,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        return new Border
        {
            Background = isVisible ? VisibleBadgeBackground : HiddenBadgeBackground,
            BorderBrush = isVisible ? VisibleBadgeBorder : HiddenBadgeBorder,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(10),
            Padding = TrueBimTheme.BadgePadding,
            Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = isVisible ? "Голубой глаз: категория должна быть видна." : "Серый глаз: категория должна быть скрыта.",
            Child = content
        };
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

    private static Button CreateSmallButton(string text, TrueBimIcon icon, RoutedEventHandler clickHandler)
    {
        Button button = TrueBimUi.CreateSecondaryButton(text, icon, minWidth: 104);
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
