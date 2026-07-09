using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using TrueBIM.App.Modules.BimTools.FamilyManager.Models;
using TrueBIM.App.Modules.BimTools.FamilyManager.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.UI;

public sealed class FamilyManagerCompactPaneControl : UserControl
{
    public static readonly DependencyProperty CompactSearchTextProperty = DependencyProperty.Register(
        nameof(CompactSearchText),
        typeof(string),
        typeof(FamilyManagerCompactPaneControl),
        new PropertyMetadata(string.Empty));

    private readonly string folderPath;
    private readonly FamilyManagerProfileStorage profileStorage;
    private readonly ITrueBimLogger logger;
    private readonly Action openManager;
    private readonly Action hidePane;
    private readonly FamilyLibraryTreeBuilder treeBuilder = new();
    private readonly FamilySearchMatchService searchMatchService = new();
    private readonly ObservableCollection<FamilyLibraryTreeNode> libraryTreeNodes = new();
    private readonly List<FamilyFileItem> folderFamilies = [];
    private readonly ComboBox catalogInput = new();
    private readonly TextBox searchInput = new();
    private readonly TextBlock searchPlaceholderText = new();
    private readonly TreeView libraryTree = new();
    private readonly TextBlock statusText = new();
    private readonly TextBlock emptyStateText = new();

    public FamilyManagerCompactPaneControl(
        string folderPath,
        FamilyManagerProfileStorage profileStorage,
        ITrueBimLogger logger,
        Action openManager,
        Action hidePane)
    {
        this.folderPath = FamilyPathNormalizer.Normalize(folderPath);
        this.profileStorage = profileStorage ?? throw new ArgumentNullException(nameof(profileStorage));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.openManager = openManager ?? throw new ArgumentNullException(nameof(openManager));
        this.hidePane = hidePane ?? throw new ArgumentNullException(nameof(hidePane));

        Background = Brushes.White;
        Content = CreateContent();
        RefreshSummary();
    }

    public string CompactSearchText
    {
        get => (string)GetValue(CompactSearchTextProperty);
        set => SetValue(CompactSearchTextProperty, value);
    }

    private UIElement CreateContent()
    {
        DockPanel root = new()
        {
            LastChildFill = true,
            Background = Brushes.White
        };

        UIElement toolbar = CreateToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        DockPanel browser = new()
        {
            LastChildFill = true,
            Margin = new Thickness(2, 4, 2, 2)
        };

        ConfigureCatalogInput();
        DockPanel.SetDock(catalogInput, Dock.Top);
        browser.Children.Add(catalogInput);

        UIElement searchBar = CreateSearchBar();
        DockPanel.SetDock(searchBar, Dock.Top);
        browser.Children.Add(searchBar);

        statusText.FontSize = 11;
        statusText.Foreground = Brushes.DimGray;
        statusText.Margin = new Thickness(2, 4, 2, 0);
        statusText.TextTrimming = TextTrimming.CharacterEllipsis;
        DockPanel.SetDock(statusText, Dock.Bottom);
        browser.Children.Add(statusText);

        emptyStateText.Foreground = Brushes.DimGray;
        emptyStateText.TextWrapping = TextWrapping.Wrap;
        emptyStateText.Margin = new Thickness(4, 8, 4, 4);
        emptyStateText.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(emptyStateText, Dock.Top);
        browser.Children.Add(emptyStateText);

        libraryTree.ItemsSource = libraryTreeNodes;
        libraryTree.ItemTemplate = CreateTreeTemplate();
        libraryTree.BorderThickness = new Thickness(0);
        libraryTree.Background = Brushes.White;
        libraryTree.Padding = new Thickness(0, 2, 0, 0);
        ScrollViewer.SetHorizontalScrollBarVisibility(libraryTree, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(libraryTree, ScrollBarVisibility.Auto);
        browser.Children.Add(libraryTree);

        root.Children.Add(browser);
        return root;
    }

    private UIElement CreateToolbar()
    {
        Border border = new()
        {
            Background = new SolidColorBrush(Color.FromRgb(232, 232, 232)),
            BorderBrush = Brushes.Gainsboro,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        Button openButton = CreateIconButton(TrueBimIcon.FamilyManager, "Открыть окно диспетчера");
        openButton.Click += (_, _) => openManager();
        actions.Children.Add(openButton);

        Button hideButton = CreateIconButton(TrueBimIcon.Close, "Скрыть панель");
        hideButton.Click += (_, _) => hidePane();
        actions.Children.Add(hideButton);

        border.Child = actions;
        return border;
    }

    private UIElement CreateSearchBar()
    {
        DockPanel searchBar = new()
        {
            LastChildFill = true,
            Margin = new Thickness(0, 2, 0, 2)
        };

        Button clearButton = CreateIconButton(TrueBimIcon.Close, "Очистить поиск");
        clearButton.Width = 26;
        clearButton.Height = 26;
        clearButton.Margin = new Thickness(2, 0, 0, 0);
        clearButton.Click += (_, _) => searchInput.Clear();
        DockPanel.SetDock(clearButton, Dock.Right);
        searchBar.Children.Add(clearButton);

        Grid searchHost = new();
        searchInput.Height = 26;
        searchInput.VerticalContentAlignment = VerticalAlignment.Center;
        searchInput.ToolTip = "Поиск по семействам, типам, параметрам и пути.";
        searchInput.TextChanged += (_, _) =>
        {
            string search = searchInput.Text.Trim();
            CompactSearchText = search.Length >= 2
                ? search
                : string.Empty;
            searchPlaceholderText.Visibility = string.IsNullOrWhiteSpace(searchInput.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            RefreshTree();
        };
        searchHost.Children.Add(searchInput);

        searchPlaceholderText.Text = "Поиск от двух символов...";
        searchPlaceholderText.Foreground = Brushes.Gray;
        searchPlaceholderText.Margin = new Thickness(6, 0, 0, 0);
        searchPlaceholderText.VerticalAlignment = VerticalAlignment.Center;
        searchPlaceholderText.IsHitTestVisible = false;
        searchHost.Children.Add(searchPlaceholderText);
        searchBar.Children.Add(searchHost);

        return searchBar;
    }

    private void ConfigureCatalogInput()
    {
        catalogInput.Height = 26;
        catalogInput.Margin = new Thickness(0, 0, 0, 2);
        catalogInput.VerticalContentAlignment = VerticalAlignment.Center;
        catalogInput.ToolTip = folderPath;
    }

    private void RefreshSummary()
    {
        try
        {
            FamilyManagerProfile profile = profileStorage.Load();
            folderFamilies.Clear();
            foreach (FamilyFileItem family in profile.CachedFiles.Where(family => IsUnderFolder(family.FilePath, folderPath)))
            {
                folderFamilies.Add(family);
            }

            catalogInput.Items.Clear();
            catalogInput.Items.Add("Каталог семейств");
            catalogInput.SelectedIndex = 0;
            catalogInput.ToolTip = folderPath;
            RefreshTree();
        }
        catch (Exception exception)
        {
            logger.Warning($"Failed to refresh Family Manager compact pane: {exception.Message}");
            folderFamilies.Clear();
            libraryTreeNodes.Clear();
            catalogInput.Items.Clear();
            catalogInput.Items.Add("Кэш недоступен");
            catalogInput.SelectedIndex = 0;
            emptyStateText.Text = "Откройте окно диспетчера и пересканируйте библиотеку.";
            emptyStateText.Visibility = Visibility.Visible;
            statusText.Text = "Не удалось прочитать кэш.";
        }
    }

    private void RefreshTree()
    {
        string search = CompactSearchText.Trim();
        bool hasSearch = !string.IsNullOrWhiteSpace(search);
        List<FamilyFileItem> visibleFamilies = folderFamilies
            .Where(family => !hasSearch || searchMatchService.Matches(family, search))
            .OrderBy(family => family.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        IReadOnlyList<FamilyLibraryTreeNode> nodes = treeBuilder.Build(
            [new FamilyLibraryFolder { Path = folderPath, IsEnabled = true }],
            visibleFamilies);

        libraryTreeNodes.Clear();
        foreach (FamilyLibraryTreeNode node in nodes)
        {
            libraryTreeNodes.Add(node);
        }

        int typeCount = visibleFamilies.Sum(family => family.AvailableTypeNames.Count);
        statusText.Text = hasSearch
            ? $"Найдено: {visibleFamilies.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} из {folderFamilies.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} · Типов: {typeCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : $"Семейств: {folderFamilies.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} · Типов: {typeCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        emptyStateText.Text = folderFamilies.Count == 0
            ? "В кэше нет семейств для выбранного каталога."
            : "Поиск не нашёл семейств в выбранном каталоге.";
        emptyStateText.Visibility = libraryTreeNodes.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static HierarchicalDataTemplate CreateTreeTemplate()
    {
        HierarchicalDataTemplate template = new(typeof(FamilyLibraryTreeNode))
        {
            ItemsSource = new Binding(nameof(FamilyLibraryTreeNode.Children))
        };

        FrameworkElementFactory text = new(typeof(SearchHighlightTextBlock));
        text.SetBinding(SearchHighlightTextBlock.HighlightTextProperty, new Binding(nameof(FamilyLibraryTreeNode.DisplayTitle)));
        text.SetBinding(SearchHighlightTextBlock.SearchTextProperty, new Binding(nameof(CompactSearchText))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(FamilyManagerCompactPaneControl), 1)
        });
        text.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(FamilyLibraryTreeNode.ExplorerPath)));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 1, 2, 1));
        template.VisualTree = text;
        return template;
    }

    private static Button CreateIconButton(TrueBimIcon icon, string toolTip)
    {
        return new Button
        {
            Content = new Image
            {
                Source = IconFactory.CreateImage(icon, 16),
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform
            },
            ToolTip = toolTip,
            Width = 28,
            Height = 26,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 2, 0, 2),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
    }

    private static bool IsUnderFolder(string filePath, string folderPath)
    {
        string normalizedPath = FamilyPathNormalizer.Normalize(filePath);
        string normalizedFolder = FamilyPathNormalizer.Normalize(folderPath);
        if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(normalizedFolder))
        {
            return false;
        }

        return normalizedPath.Equals(normalizedFolder, StringComparison.CurrentCultureIgnoreCase)
            || normalizedPath.StartsWith(normalizedFolder + Path.DirectorySeparatorChar, StringComparison.CurrentCultureIgnoreCase)
            || normalizedPath.StartsWith(normalizedFolder + Path.AltDirectorySeparatorChar, StringComparison.CurrentCultureIgnoreCase);
    }
}
