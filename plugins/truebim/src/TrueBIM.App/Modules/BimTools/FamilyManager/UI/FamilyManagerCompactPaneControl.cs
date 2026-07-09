using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using RevitDocument = Autodesk.Revit.DB.Document;
using RevitFamily = Autodesk.Revit.DB.Family;
using RevitFamilySymbol = Autodesk.Revit.DB.FamilySymbol;
using RevitFilteredElementCollector = Autodesk.Revit.DB.FilteredElementCollector;
using RevitTaskDialog = Autodesk.Revit.UI.TaskDialog;
using RevitUidocument = Autodesk.Revit.UI.UIDocument;
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

    private readonly RevitUidocument uiDocument;
    private readonly RevitDocument document;
    private readonly string folderPath;
    private readonly FamilyManagerProfileStorage profileStorage;
    private readonly FamilyLoadService loadService;
    private readonly ITrueBimLogger logger;
    private readonly FamilyManagerRevitActionDispatcher revitActionDispatcher;
    private readonly Action openManager;
    private readonly Action hidePane;
    private readonly FamilyLibraryTreeBuilder treeBuilder = new();
    private readonly FamilySearchMatchService searchMatchService = new();
    private readonly ObservableCollection<FamilyLibraryTreeNode> libraryTreeNodes = new();
    private readonly List<FamilyLibraryTreeNode> subscribedNodes = [];
    private readonly List<FamilyFileItem> folderFamilies = [];
    private readonly HashSet<string> loadedFamilyNames = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly HashSet<string> loadedTypeKeys = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly ComboBox catalogInput = new();
    private readonly TextBox searchInput = new();
    private readonly TextBlock searchPlaceholderText = new();
    private readonly TreeView libraryTree = new();
    private readonly TextBlock statusText = new();
    private readonly TextBlock emptyStateText = new();
    private readonly TextBlock selectionText = new();
    private readonly Button actionButton = CreateFooterButton("Загрузить", TrueBimIcon.Apply);
    private readonly Button clearSelectionButton = CreateFooterButton("Снять выбор", TrueBimIcon.Close);
    private FamilyManagerProfile profile = new();
    private bool isUpdatingSelection;
    private bool isFamilyActionQueued;

    public FamilyManagerCompactPaneControl(
        RevitUidocument uiDocument,
        string folderPath,
        FamilyManagerProfileStorage profileStorage,
        FamilyLoadService loadService,
        ITrueBimLogger logger,
        FamilyManagerRevitActionDispatcher revitActionDispatcher,
        Action openManager,
        Action hidePane)
    {
        this.uiDocument = uiDocument ?? throw new ArgumentNullException(nameof(uiDocument));
        document = uiDocument.Document;
        this.folderPath = FamilyPathNormalizer.Normalize(folderPath);
        this.profileStorage = profileStorage ?? throw new ArgumentNullException(nameof(profileStorage));
        this.loadService = loadService ?? throw new ArgumentNullException(nameof(loadService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.revitActionDispatcher = revitActionDispatcher ?? throw new ArgumentNullException(nameof(revitActionDispatcher));
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

        UIElement footer = CreateSelectionFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

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
        libraryTree.SelectedItemChanged += (_, _) => UpdateSelectionActions();
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

        Button refreshButton = CreateIconButton(TrueBimIcon.Preview, "Обновить панель");
        refreshButton.Click += (_, _) => RefreshSummary();
        actions.Children.Add(refreshButton);

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

    private UIElement CreateSelectionFooter()
    {
        Border border = new()
        {
            Background = new SolidColorBrush(Color.FromRgb(232, 232, 232)),
            BorderBrush = Brushes.Gainsboro,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(2)
        };

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        actionButton.Margin = new Thickness(0, 0, 1, 0);
        actionButton.Click += (_, _) => ExecutePrimaryAction();
        Grid.SetColumn(actionButton, 0);
        grid.Children.Add(actionButton);

        clearSelectionButton.Margin = new Thickness(1, 0, 0, 0);
        clearSelectionButton.Click += (_, _) => ClearSelection();
        Grid.SetColumn(clearSelectionButton, 1);
        grid.Children.Add(clearSelectionButton);

        selectionText.FontSize = 11;
        selectionText.Foreground = Brushes.DimGray;
        selectionText.Margin = new Thickness(2, 3, 2, 0);
        selectionText.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumnSpan(selectionText, 2);
        Grid.SetRow(selectionText, 1);
        grid.Children.Add(selectionText);

        border.Child = grid;
        return border;
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
            profile = profileStorage.Load();
            RefreshProjectLoadIndex();

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
            profile = new FamilyManagerProfile();
            folderFamilies.Clear();
            ClearTreeSubscriptions();
            libraryTreeNodes.Clear();
            catalogInput.Items.Clear();
            catalogInput.Items.Add("Кэш недоступен");
            catalogInput.SelectedIndex = 0;
            emptyStateText.Text = "Откройте окно диспетчера и пересканируйте библиотеку.";
            emptyStateText.Visibility = Visibility.Visible;
            statusText.Text = "Не удалось прочитать кэш.";
            UpdateSelectionActions();
        }
    }

    private void RefreshTree()
    {
        HashSet<string> selectedKeys = CollectSelectedKeys();
        string search = CompactSearchText.Trim();
        bool hasSearch = !string.IsNullOrWhiteSpace(search);
        List<FamilyFileItem> visibleFamilies = folderFamilies
            .Where(family => !hasSearch || searchMatchService.Matches(family, search))
            .OrderBy(family => family.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        IReadOnlyList<FamilyLibraryTreeNode> nodes = treeBuilder.Build(
            [new FamilyLibraryFolder { Path = folderPath, IsEnabled = true }],
            visibleFamilies);

        isUpdatingSelection = true;
        ClearTreeSubscriptions();
        libraryTreeNodes.Clear();
        foreach (FamilyLibraryTreeNode node in nodes)
        {
            ApplyNodeState(node, selectedKeys);
            SubscribeNode(node);
            libraryTreeNodes.Add(node);
        }

        isUpdatingSelection = false;

        int typeCount = visibleFamilies.Sum(family => family.AvailableTypeNames.Count);
        statusText.Text = hasSearch
            ? $"Найдено: {visibleFamilies.Count.ToString(CultureInfo.InvariantCulture)} из {folderFamilies.Count.ToString(CultureInfo.InvariantCulture)}. Типов: {typeCount.ToString(CultureInfo.InvariantCulture)}"
            : $"Семейств: {folderFamilies.Count.ToString(CultureInfo.InvariantCulture)}. Типов: {typeCount.ToString(CultureInfo.InvariantCulture)}";

        emptyStateText.Text = folderFamilies.Count == 0
            ? "В кэше нет семейств для выбранного каталога."
            : "Поиск не нашёл семейств в выбранном каталоге.";
        emptyStateText.Visibility = libraryTreeNodes.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateSelectionActions();
    }

    private void RefreshProjectLoadIndex()
    {
        loadedFamilyNames.Clear();
        loadedTypeKeys.Clear();

        try
        {
            foreach (RevitFamily family in new RevitFilteredElementCollector(document).OfClass(typeof(RevitFamily)).Cast<RevitFamily>())
            {
                if (string.IsNullOrWhiteSpace(family.Name))
                {
                    continue;
                }

                loadedFamilyNames.Add(family.Name);
                foreach (Autodesk.Revit.DB.ElementId symbolId in family.GetFamilySymbolIds())
                {
                    if (document.GetElement(symbolId) is not RevitFamilySymbol symbol || string.IsNullOrWhiteSpace(symbol.Name))
                    {
                        continue;
                    }

                    loadedTypeKeys.Add(CreateTypeKey(family.Name, symbol.Name));
                }
            }
        }
        catch (Exception exception)
        {
            logger.Warning($"Failed to collect loaded Revit families for compact pane: {exception.Message}");
        }
    }

    private void RefreshTreeProjectState()
    {
        foreach (FamilyLibraryTreeNode node in libraryTreeNodes)
        {
            ApplyNodeState(node, null);
        }
    }

    private void ApplyNodeState(FamilyLibraryTreeNode node, ISet<string>? selectedKeys)
    {
        if (node.CanSelectForAction && node.Family is not null)
        {
            bool isLoaded = IsTargetLoaded(node.Family, node.Kind is FamilyLibraryTreeNodeKind.Type ? node.TypeName : null);
            node.IsLoadedInProject = isLoaded;
            node.ProjectStatus = isLoaded ? "Загружен" : string.Empty;
            if (selectedKeys is not null)
            {
                node.IsSelectedForAction = selectedKeys.Contains(CreateNodeKey(node));
            }
        }

        foreach (FamilyLibraryTreeNode child in node.Children)
        {
            ApplyNodeState(child, selectedKeys);
        }
    }

    private void SubscribeNode(FamilyLibraryTreeNode node)
    {
        node.PropertyChanged += OnTreeNodePropertyChanged;
        subscribedNodes.Add(node);
        foreach (FamilyLibraryTreeNode child in node.Children)
        {
            SubscribeNode(child);
        }
    }

    private void ClearTreeSubscriptions()
    {
        foreach (FamilyLibraryTreeNode node in subscribedNodes)
        {
            node.PropertyChanged -= OnTreeNodePropertyChanged;
        }

        subscribedNodes.Clear();
    }

    private void OnTreeNodePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (isUpdatingSelection || args.PropertyName is not nameof(FamilyLibraryTreeNode.IsSelectedForAction))
        {
            return;
        }

        UpdateSelectionActions();
    }

    private static HierarchicalDataTemplate CreateTreeTemplate()
    {
        BooleanToVisibilityConverter visibilityConverter = new();
        HierarchicalDataTemplate template = new(typeof(FamilyLibraryTreeNode))
        {
            ItemsSource = new Binding(nameof(FamilyLibraryTreeNode.Children))
        };

        FrameworkElementFactory row = new(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        row.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 2, 3));
        row.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(FamilyLibraryTreeNode.ExplorerPath)));

        FrameworkElementFactory checkBox = new(typeof(CheckBox));
        checkBox.SetValue(FrameworkElement.WidthProperty, 22.0);
        checkBox.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
        checkBox.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 2, 0));
        checkBox.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(FamilyLibraryTreeNode.IsSelectedForAction))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        checkBox.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(FamilyLibraryTreeNode.CanSelectForAction))
        {
            Converter = visibilityConverter
        });
        row.AppendChild(checkBox);

        FrameworkElementFactory thumbnail = new(typeof(Image));
        thumbnail.SetValue(FrameworkElement.WidthProperty, 42.0);
        thumbnail.SetValue(FrameworkElement.HeightProperty, 42.0);
        thumbnail.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 6, 2));
        thumbnail.SetValue(Image.StretchProperty, Stretch.UniformToFill);
        thumbnail.SetBinding(Image.SourceProperty, new Binding(nameof(FamilyLibraryTreeNode.ThumbnailPath)));
        thumbnail.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(FamilyLibraryTreeNode.ShowsThumbnail))
        {
            Converter = visibilityConverter
        });
        row.AppendChild(thumbnail);

        FrameworkElementFactory textStack = new(typeof(StackPanel));
        textStack.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
        textStack.SetValue(FrameworkElement.MinWidthProperty, 170.0);

        FrameworkElementFactory title = new(typeof(SearchHighlightTextBlock));
        title.SetBinding(SearchHighlightTextBlock.HighlightTextProperty, new Binding(nameof(FamilyLibraryTreeNode.DisplayTitle)));
        title.SetBinding(SearchHighlightTextBlock.SearchTextProperty, new Binding(nameof(CompactSearchText))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(FamilyManagerCompactPaneControl), 1)
        });
        title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        title.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 2, 1));
        textStack.AppendChild(title);

        FrameworkElementFactory statusRow = new(typeof(StackPanel));
        statusRow.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        statusRow.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(FamilyLibraryTreeNode.HasProjectStatus))
        {
            Converter = visibilityConverter
        });

        FrameworkElementFactory statusBadge = new(typeof(Border));
        statusBadge.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(120, 220, 160)));
        statusBadge.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        statusBadge.SetValue(Border.PaddingProperty, new Thickness(4, 1, 4, 1));
        statusBadge.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 2));

        FrameworkElementFactory statusText = new(typeof(TextBlock));
        statusText.SetValue(TextBlock.FontSizeProperty, 11.0);
        statusText.SetValue(TextBlock.ForegroundProperty, Brushes.DarkGreen);
        statusText.SetBinding(TextBlock.TextProperty, new Binding(nameof(FamilyLibraryTreeNode.ProjectStatus)));
        statusBadge.AppendChild(statusText);
        statusRow.AppendChild(statusBadge);
        textStack.AppendChild(statusRow);

        FrameworkElementFactory subtitle = new(typeof(TextBlock));
        subtitle.SetBinding(TextBlock.TextProperty, new Binding(nameof(FamilyLibraryTreeNode.Subtitle)));
        subtitle.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(FamilyLibraryTreeNode.CanSelectForAction))
        {
            Converter = visibilityConverter
        });
        subtitle.SetValue(TextBlock.FontSizeProperty, 11.0);
        subtitle.SetValue(TextBlock.FontStyleProperty, FontStyles.Italic);
        subtitle.SetValue(TextBlock.ForegroundProperty, Brushes.DimGray);
        subtitle.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        textStack.AppendChild(subtitle);

        row.AppendChild(textStack);
        template.VisualTree = row;
        return template;
    }

    private void ExecutePrimaryAction()
    {
        IReadOnlyList<FamilyActionTarget> targets = GetSelectedTargets();
        if (targets.Count == 0)
        {
            statusText.Text = "Выберите семейство или тип.";
            return;
        }

        QueueFamilyAction(() => RunPrimaryAction(targets));
    }

    private void RunPrimaryAction(IReadOnlyList<FamilyActionTarget> targets)
    {
        RefreshProjectLoadIndex();

        if (targets.Count == 1 && IsTargetLoaded(targets[0]))
        {
            RequestPlacement(targets[0]);
            RefreshTreeProjectState();
            UpdateSelectionActions();
            return;
        }

        int loaded = 0;
        int skipped = 0;
        int failed = 0;
        foreach (FamilyActionTarget target in targets)
        {
            if (IsTargetLoaded(target))
            {
                skipped++;
                continue;
            }

            if (!File.Exists(target.Family.FilePath))
            {
                target.Family.Status = "Файл не найден";
                failed++;
                continue;
            }

            FamilyLoadResult result = string.IsNullOrWhiteSpace(target.TypeName)
                ? loadService.Load(document, target.Family.FilePath, overwriteExisting: false, logger)
                : loadService.LoadSymbol(document, target.Family.FilePath, target.TypeName!, overwriteExisting: false, logger);
            target.Family.Status = result.Message;
            if (result.Status is FamilyLoadStatus.Loaded)
            {
                loaded++;
                AddHistory(target);
            }
            else if (result.Status is FamilyLoadStatus.AlreadyLoaded)
            {
                skipped++;
            }
            else
            {
                failed++;
            }
        }

        if (loaded > 0)
        {
            profileStorage.Save(profile);
        }

        RefreshProjectLoadIndex();
        RefreshTreeProjectState();
        UpdateSelectionActions();
        statusText.Text =
            $"Загружено: {loaded.ToString(CultureInfo.InvariantCulture)}. Уже было: {skipped.ToString(CultureInfo.InvariantCulture)}. Ошибок: {failed.ToString(CultureInfo.InvariantCulture)}.";
    }

    private void QueueFamilyAction(Action action)
    {
        if (isFamilyActionQueued)
        {
            statusText.Text = "Операция уже ожидает Revit.";
            return;
        }

        isFamilyActionQueued = true;
        actionButton.IsEnabled = false;
        clearSelectionButton.IsEnabled = false;
        statusText.Text = "Запрос передан в Revit.";

        try
        {
            revitActionDispatcher.Raise(() =>
            {
                try
                {
                    action();
                }
                finally
                {
                    isFamilyActionQueued = false;
                    UpdateSelectionActions();
                }
            });
        }
        catch
        {
            isFamilyActionQueued = false;
            UpdateSelectionActions();
            throw;
        }
    }

    private void RequestPlacement(FamilyActionTarget target)
    {
        try
        {
            RevitFamilySymbol? symbol = string.IsNullOrWhiteSpace(target.TypeName)
                ? loadService.ResolveSymbol(document, target.Family.Name, null)
                : loadService.ResolveSymbolExact(document, target.Family.Name, target.TypeName!);
            if (symbol is null)
            {
                statusText.Text = string.IsNullOrWhiteSpace(target.TypeName)
                    ? "Семейство загружено, но подходящий тип для размещения не найден."
                    : $"Выбранный тип для размещения не найден: {target.TypeName}.";
                return;
            }

            loadService.ActivateAndRequestPlacement(uiDocument, symbol);
            statusText.Text = $"Запущено размещение типа: {symbol.Name}.";
        }
        catch (Exception exception)
        {
            logger.Error($"Failed to start compact family placement for '{target.Family.FilePath}'.", exception);
            RevitTaskDialog.Show("Диспетчер семейств", "Не удалось запустить размещение. Используйте логи для диагностики.");
        }
    }

    private void AddHistory(FamilyActionTarget target)
    {
        DateTimeOffset loadedAtUtc = DateTimeOffset.UtcNow;
        target.Family.LastLoadedAtUtc = loadedAtUtc;
        profile.History.Insert(0, new FamilyLoadHistoryItem
        {
            FilePath = target.Family.FilePath,
            FamilyName = target.Family.Name,
            Action = string.IsNullOrWhiteSpace(target.TypeName)
                ? "Загрузка из компактной панели"
                : $"Загрузка типа {target.TypeName} из компактной панели",
            LoadedAtUtc = loadedAtUtc
        });
    }

    private IReadOnlyList<FamilyActionTarget> GetSelectedTargets()
    {
        List<FamilyActionTarget> targets = [];
        HashSet<string> seen = new(StringComparer.CurrentCultureIgnoreCase);
        HashSet<string> familiesWithTypeSelection = new(StringComparer.CurrentCultureIgnoreCase);

        foreach (FamilyLibraryTreeNode node in EnumerateNodes(libraryTreeNodes))
        {
            if (!node.IsSelectedForAction || !node.CanSelectForAction || node.Family is null)
            {
                continue;
            }

            string? typeName = node.Kind is FamilyLibraryTreeNodeKind.Type
                ? node.TypeName
                : null;
            FamilyActionTarget target = new(node.Family, typeName);
            if (!seen.Add(target.Key))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(typeName))
            {
                familiesWithTypeSelection.Add(FamilyPathNormalizer.Normalize(node.Family.FilePath));
            }

            targets.Add(target);
        }

        return targets
            .Where(target =>
                !string.IsNullOrWhiteSpace(target.TypeName)
                || !familiesWithTypeSelection.Contains(FamilyPathNormalizer.Normalize(target.Family.FilePath)))
            .ToList();
    }

    private void ClearSelection()
    {
        isUpdatingSelection = true;
        foreach (FamilyLibraryTreeNode node in EnumerateNodes(libraryTreeNodes))
        {
            node.IsSelectedForAction = false;
        }

        isUpdatingSelection = false;
        UpdateSelectionActions();
    }

    private void UpdateSelectionActions()
    {
        IReadOnlyList<FamilyActionTarget> targets = GetSelectedTargets();
        clearSelectionButton.IsEnabled = targets.Count > 0;
        if (isFamilyActionQueued)
        {
            clearSelectionButton.IsEnabled = false;
            SetButtonContent(actionButton, TrueBimIcon.Apply, "Ожидание Revit");
            actionButton.IsEnabled = false;
            selectionText.Text = targets.Count == 0
                ? "Запрос выполняется через Revit."
                : targets.Count == 1
                    ? targets[0].DisplayName
                    : $"Выбрано: {targets.Count.ToString(CultureInfo.InvariantCulture)}";
            return;
        }

        if (targets.Count == 0)
        {
            SetButtonContent(actionButton, TrueBimIcon.Apply, "Загрузить");
            actionButton.IsEnabled = false;
            selectionText.Text = "Выберите семейство или тип.";
            return;
        }

        bool canPlace = targets.Count == 1 && IsTargetLoaded(targets[0]);
        SetButtonContent(
            actionButton,
            canPlace ? TrueBimIcon.FamilyManager : TrueBimIcon.Apply,
            canPlace ? "Разместить" : $"Загрузить ({targets.Count.ToString(CultureInfo.InvariantCulture)})");
        actionButton.IsEnabled = true;
        selectionText.Text = targets.Count == 1
            ? targets[0].DisplayName
            : $"Выбрано: {targets.Count.ToString(CultureInfo.InvariantCulture)}";
    }

    private HashSet<string> CollectSelectedKeys()
    {
        HashSet<string> keys = new(StringComparer.CurrentCultureIgnoreCase);
        foreach (FamilyLibraryTreeNode node in EnumerateNodes(libraryTreeNodes))
        {
            if (node.IsSelectedForAction && node.CanSelectForAction)
            {
                keys.Add(CreateNodeKey(node));
            }
        }

        return keys;
    }

    private bool IsTargetLoaded(FamilyActionTarget target)
    {
        return IsTargetLoaded(target.Family, target.TypeName);
    }

    private bool IsTargetLoaded(FamilyFileItem family, string? typeName)
    {
        return string.IsNullOrWhiteSpace(typeName)
            ? loadedFamilyNames.Contains(family.Name)
            : loadedTypeKeys.Contains(CreateTypeKey(family.Name, typeName!));
    }

    private static IEnumerable<FamilyLibraryTreeNode> EnumerateNodes(IEnumerable<FamilyLibraryTreeNode> nodes)
    {
        foreach (FamilyLibraryTreeNode node in nodes)
        {
            yield return node;
            foreach (FamilyLibraryTreeNode child in EnumerateNodes(node.Children))
            {
                yield return child;
            }
        }
    }

    private static string CreateNodeKey(FamilyLibraryTreeNode node)
    {
        string filePath = FamilyPathNormalizer.Normalize(node.Family?.FilePath ?? node.FamilyPath);
        string typeName = node.Kind is FamilyLibraryTreeNodeKind.Type ? node.TypeName : string.Empty;
        return $"{filePath}|{typeName}";
    }

    private static string CreateTypeKey(string familyName, string typeName)
    {
        return $"{familyName.Trim()}|{typeName.Trim()}";
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

    private static Button CreateFooterButton(string text, TrueBimIcon icon)
    {
        return new Button
        {
            Content = IconFactory.CreateButtonContent(icon, text),
            Height = 28,
            Padding = new Thickness(6, 0, 6, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.WhiteSmoke,
            BorderBrush = Brushes.Silver,
            BorderThickness = new Thickness(1)
        };
    }

    private static void SetButtonContent(Button button, TrueBimIcon icon, string text)
    {
        button.Content = IconFactory.CreateButtonContent(icon, text);
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

    private sealed record FamilyActionTarget(FamilyFileItem Family, string? TypeName)
    {
        public string Key => $"{FamilyPathNormalizer.Normalize(Family.FilePath)}|{TypeName ?? string.Empty}";

        public string DisplayName => string.IsNullOrWhiteSpace(TypeName)
            ? Family.Name
            : $"{Family.Name}: {TypeName}";
    }
}
