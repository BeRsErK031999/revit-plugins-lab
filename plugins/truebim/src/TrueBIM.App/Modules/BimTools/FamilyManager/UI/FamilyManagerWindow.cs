using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.FamilyManager.Models;
using TrueBIM.App.Modules.BimTools.FamilyManager.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using WpfBinding = System.Windows.Data.Binding;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfGrid = System.Windows.Controls.Grid;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.UI;

public sealed class FamilyManagerWindow : Window
{
    public static readonly DependencyProperty FamilySearchTextProperty = DependencyProperty.Register(
        nameof(FamilySearchText),
        typeof(string),
        typeof(FamilyManagerWindow),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ParameterHighlightTextProperty = DependencyProperty.Register(
        nameof(ParameterHighlightText),
        typeof(string),
        typeof(FamilyManagerWindow),
        new PropertyMetadata(string.Empty));

    private const string AllParameterPreset = "Все";
    private const string DimensionParameterPreset = "Размеры";
    private const string MaterialParameterPreset = "Материалы";
    private const string IdentityParameterPreset = "Идентификация";
    private const uint BrowseInfoReturnOnlyFileSystemDirectories = 0x0001;
    private const uint BrowseInfoNewDialogStyle = 0x0040;
    private const uint BrowseInfoNoNewFolderButton = 0x0200;
    private const int MaxPathLength = 260;

    private static readonly string[] DimensionParameterTokens =
    [
        "Width",
        "Height",
        "Length",
        "Depth",
        "Thickness",
        "Diameter",
        "Radius",
        "Ширина",
        "Высота",
        "Длина",
        "Глубина",
        "Толщина",
        "Диаметр",
        "Радиус"
    ];

    private static readonly string[] MaterialParameterTokens = ["Material", "Материал"];

    private static readonly string[] IdentityParameterTokens =
    [
        "Manufacturer",
        "Model",
        "Description",
        "Code",
        "Mark",
        "Производитель",
        "Модель",
        "Описание",
        "Код",
        "Марка"
    ];

    private readonly UIApplication uiApplication;
    private readonly UIDocument uiDocument;
    private readonly Document document;
    private readonly FamilyManagerProfileStorage profileStorage;
    private readonly FamilyLibraryScanner scanner;
    private readonly FamilyLibraryTreeBuilder treeBuilder = new();
    private readonly FamilyMetadataBatchSelector metadataBatchSelector = new();
    private readonly FamilyLibraryAuditService auditService = new();
    private readonly FamilySearchMatchService searchMatchService = new();
    private readonly FamilyLoadService loadService;
    private readonly FamilyMetadataService metadataService;
    private readonly FamilyThumbnailService thumbnailService;
    private readonly ITrueBimLogger logger;
    private readonly ObservableCollection<FamilyLibraryFolder> folders = new();
    private readonly ObservableCollection<FamilyLibraryFile> libraryFiles = new();
    private readonly ObservableCollection<FamilyFileItem> visibleFamilies = new();
    private readonly ObservableCollection<FamilyLoadHistoryItem> historyItems = new();
    private readonly ObservableCollection<FamilyTypeInfo> familyTypes = new();
    private readonly ObservableCollection<FamilyTypeParameterInfo> typeParameters = new();
    private readonly ObservableCollection<FamilyLibraryTreeNode> libraryTreeNodes = new();
    private readonly ObservableCollection<FamilyLibraryAuditIssue> auditIssues = new();
    private readonly List<FamilyFileItem> allFamilies = new();
    private readonly ListBox folderList = new();
    private readonly ListBox fileList = new();
    private readonly TreeView libraryTree = new();
    private readonly DataGrid familyGrid = new();
    private readonly DataGrid auditGrid = new();
    private readonly DataGrid parameterGrid = new();
    private readonly ListBox historyList = new();
    private readonly ListBox typeList = new();
    private readonly Image thumbnailImage = new();
    private readonly TextBlock thumbnailPlaceholderText = new();
    private readonly WpfTextBox searchInput = new();
    private readonly WpfTextBox parameterSearchInput = new();
    private readonly WpfComboBox categoryInput = new();
    private readonly WpfComboBox parameterPresetInput = new();
    private readonly CheckBox favoritesOnlyInput = new()
    {
        Content = "Избранное"
    };
    private readonly TextBlock statusText = new();
    private readonly TextBlock detailsText = new();
    private readonly Button favoriteButton = CreateButton("В избранное", TrueBimIcon.Apply, 130);
    private readonly Button loadButton = CreateButton("Загрузить", TrueBimIcon.Apply, 130);
    private readonly Button loadAndPlaceButton = CreateButton("Загрузить и разместить", TrueBimIcon.FamilyManager, 190);
    private readonly Button refreshMetadataButton = CreateButton("Обновить метаданные", TrueBimIcon.Preview, 220);
    private readonly Button refreshFolderMetadataButton = CreateButton("Метаданные папки", TrueBimIcon.Preview, 236);
    private readonly Button refreshThumbnailButton = CreateButton("Обновить preview", TrueBimIcon.Preview, 220);
    private readonly Button auditButton = CreateButton("Аудит библиотеки", TrueBimIcon.Preview, 236);
    private FamilyManagerProfile profile;
    private FamilyFileItem? selectedFamily;
    private FamilyTypeInfo? selectedType;
    private bool isRefreshing;

    public string FamilySearchText
    {
        get => (string)GetValue(FamilySearchTextProperty);
        set => SetValue(FamilySearchTextProperty, value);
    }

    public string ParameterHighlightText
    {
        get => (string)GetValue(ParameterHighlightTextProperty);
        set => SetValue(ParameterHighlightTextProperty, value);
    }

    public FamilyManagerWindow(
        UIApplication uiApplication,
        UIDocument uiDocument,
        FamilyManagerProfileStorage profileStorage,
        FamilyLibraryScanner scanner,
        FamilyLoadService loadService,
        FamilyMetadataService metadataService,
        FamilyThumbnailService thumbnailService,
        ITrueBimLogger logger)
    {
        this.uiApplication = uiApplication ?? throw new ArgumentNullException(nameof(uiApplication));
        this.uiDocument = uiDocument ?? throw new ArgumentNullException(nameof(uiDocument));
        document = uiDocument.Document;
        this.profileStorage = profileStorage ?? throw new ArgumentNullException(nameof(profileStorage));
        this.scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        this.loadService = loadService ?? throw new ArgumentNullException(nameof(loadService));
        this.metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
        this.thumbnailService = thumbnailService ?? throw new ArgumentNullException(nameof(thumbnailService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        profile = this.profileStorage.Load();

        Title = "Диспетчер семейств";
        Icon = IconFactory.CreateImage(TrueBimIcon.FamilyManager, 32);
        Width = 1180;
        Height = 760;
        MinWidth = 1040;
        MinHeight = 640;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();

        LoadProfileState();
        logger.Info($"Family Manager opened for '{document.Title}'. Cached families: {allFamilies.Count}.");
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveProfile();
        base.OnClosed(e);
    }

    private UIElement CreateContent()
    {
        DockPanel root = new()
        {
            Margin = new Thickness(18)
        };

        statusText.Margin = new Thickness(0, 10, 0, 0);
        statusText.TextWrapping = TextWrapping.Wrap;
        DockPanel.SetDock(statusText, Dock.Bottom);
        root.Children.Add(statusText);

        WpfGrid main = new();
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });

        UIElement left = CreateFoldersPanel();
        WpfGrid.SetColumn(left, 0);
        main.Children.Add(left);

        UIElement center = CreateFamiliesPanel();
        WpfGrid.SetColumn(center, 2);
        main.Children.Add(center);

        UIElement right = CreateDetailsPanel();
        WpfGrid.SetColumn(right, 4);
        main.Children.Add(right);

        root.Children.Add(main);
        return root;
    }

    private UIElement CreateFoldersPanel()
    {
        DockPanel panel = new()
        {
            LastChildFill = true
        };

        TextBlock title = CreatePanelTitle("Библиотеки");
        DockPanel.SetDock(title, Dock.Top);
        panel.Children.Add(title);

        WrapPanel actions = new()
        {
            Margin = new Thickness(0, 0, 0, 8)
        };
        Button addFolderButton = CreateButton("Папка", TrueBimIcon.Open, 68);
        addFolderButton.ToolTip = "Добавить папку библиотеки. Кнопка выбора подтверждает текущую папку, не проваливаясь внутрь.";
        addFolderButton.Click += (_, _) => AddFolder();
        actions.Children.Add(addFolderButton);

        Button addFileButton = CreateButton("Файл", TrueBimIcon.FamilyManager, 60);
        addFileButton.ToolTip = "Добавить отдельный .rfa файл в библиотеку.";
        addFileButton.Click += (_, _) => AddFile();
        actions.Children.Add(addFileButton);

        Button removeButton = CreateButton("Удалить", TrueBimIcon.Close, 82);
        removeButton.ToolTip = "Удалить выбранную папку или файл библиотеки.";
        removeButton.Click += (_, _) => RemoveSelectedLibrarySource();
        actions.Children.Add(removeButton);
        DockPanel.SetDock(actions, Dock.Top);
        panel.Children.Add(actions);

        Button scanButton = CreateButton("Сканировать", TrueBimIcon.Preview, 236);
        scanButton.Margin = new Thickness(0, 0, 0, 8);
        scanButton.Click += (_, _) => ScanLibraries();
        DockPanel.SetDock(scanButton, Dock.Top);
        panel.Children.Add(scanButton);

        refreshFolderMetadataButton.Margin = new Thickness(0, 0, 0, 8);
        refreshFolderMetadataButton.ToolTip = "Открывает все семейства из выбранной папки библиотеки и обновляет категории, типы и параметры в кэше.";
        refreshFolderMetadataButton.Click += (_, _) => RefreshSelectedFolderMetadata();
        DockPanel.SetDock(refreshFolderMetadataButton, Dock.Top);
        panel.Children.Add(refreshFolderMetadataButton);

        auditButton.Margin = new Thickness(0, 0, 0, 8);
        auditButton.ToolTip = "Read-only проверка дублей, отсутствующих файлов и устаревшего cache.";
        auditButton.Click += (_, _) => RunLibraryAudit();
        DockPanel.SetDock(auditButton, Dock.Top);
        panel.Children.Add(auditButton);

        TextBlock foldersTitle = CreateSubTitle("Папки");
        DockPanel.SetDock(foldersTitle, Dock.Top);
        panel.Children.Add(foldersTitle);

        folderList.ItemsSource = folders;
        folderList.DisplayMemberPath = nameof(FamilyLibraryFolder.Path);
        folderList.BorderThickness = new Thickness(1);
        folderList.Height = 105;
        folderList.Margin = new Thickness(0, 0, 0, 8);
        folderList.SelectionChanged += (_, _) =>
        {
            if (folderList.SelectedItem is not null)
            {
                fileList.SelectedItem = null;
            }
        };
        DockPanel.SetDock(folderList, Dock.Top);
        panel.Children.Add(folderList);

        TextBlock filesTitle = CreateSubTitle("Файлы");
        DockPanel.SetDock(filesTitle, Dock.Top);
        panel.Children.Add(filesTitle);

        fileList.ItemsSource = libraryFiles;
        fileList.DisplayMemberPath = nameof(FamilyLibraryFile.Path);
        fileList.BorderThickness = new Thickness(1);
        fileList.Height = 82;
        fileList.Margin = new Thickness(0, 0, 0, 12);
        fileList.SelectionChanged += (_, _) =>
        {
            if (fileList.SelectedItem is not null)
            {
                folderList.SelectedItem = null;
            }
        };
        DockPanel.SetDock(fileList, Dock.Top);
        panel.Children.Add(fileList);

        TextBlock treeTitle = CreateSubTitle("Структура");
        DockPanel.SetDock(treeTitle, Dock.Top);
        panel.Children.Add(treeTitle);

        libraryTree.ItemsSource = libraryTreeNodes;
        libraryTree.ItemTemplate = CreateTreeTemplate();
        libraryTree.ContextMenu = CreateTreeContextMenu();
        libraryTree.PreviewMouseRightButtonDown += (_, args) => SelectTreeItemUnderPointer(args.OriginalSource as DependencyObject);
        libraryTree.SelectedItemChanged += (_, args) => SelectTreeNode(args.NewValue as FamilyLibraryTreeNode);
        panel.Children.Add(libraryTree);
        return panel;
    }

    private UIElement CreateFamiliesPanel()
    {
        DockPanel panel = new()
        {
            LastChildFill = true
        };

        TextBlock title = CreatePanelTitle("Семейства");
        DockPanel.SetDock(title, Dock.Top);
        panel.Children.Add(title);

        WpfGrid filters = new()
        {
            Margin = new Thickness(0, 0, 0, 8)
        };
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        searchInput.Height = 32;
        searchInput.Margin = new Thickness(0, 0, 8, 0);
        searchInput.ToolTip = "Поиск по имени, категории, типу, параметрам или пути.";
        searchInput.TextChanged += (_, _) => RefreshVisibleFamilies();
        WpfGrid.SetColumn(searchInput, 0);
        filters.Children.Add(searchInput);

        categoryInput.Height = 32;
        categoryInput.Margin = new Thickness(0, 0, 8, 0);
        categoryInput.SelectionChanged += (_, _) => RefreshVisibleFamilies();
        WpfGrid.SetColumn(categoryInput, 1);
        filters.Children.Add(categoryInput);

        favoritesOnlyInput.VerticalAlignment = VerticalAlignment.Center;
        favoritesOnlyInput.Checked += (_, _) => RefreshVisibleFamilies();
        favoritesOnlyInput.Unchecked += (_, _) => RefreshVisibleFamilies();
        WpfGrid.SetColumn(favoritesOnlyInput, 2);
        filters.Children.Add(favoritesOnlyInput);

        DockPanel.SetDock(filters, Dock.Top);
        panel.Children.Add(filters);

        UIElement auditPanel = CreateAuditPanel();
        DockPanel.SetDock(auditPanel, Dock.Bottom);
        panel.Children.Add(auditPanel);

        familyGrid.AutoGenerateColumns = false;
        familyGrid.CanUserAddRows = false;
        familyGrid.CanUserDeleteRows = false;
        familyGrid.IsReadOnly = true;
        familyGrid.SelectionMode = DataGridSelectionMode.Single;
        familyGrid.ItemsSource = visibleFamilies;
        familyGrid.ContextMenu = CreateFamilyContextMenu();
        familyGrid.PreviewMouseRightButtonDown += (_, args) => SelectFamilyRowUnderPointer(args.OriginalSource as DependencyObject);
        familyGrid.SelectionChanged += (_, _) => SelectFamily(familyGrid.SelectedItem as FamilyFileItem);
        familyGrid.Columns.Add(CreateHighlightedTextColumn("Имя", nameof(FamilyFileItem.Name), new DataGridLength(1, DataGridLengthUnitType.Star), nameof(FamilySearchText)));
        familyGrid.Columns.Add(CreateTextColumn("Категория", nameof(FamilyFileItem.Category), 120));
        familyGrid.Columns.Add(CreateHighlightedTextColumn("Совпадение", nameof(FamilyFileItem.SearchMatchDisplay), 170, nameof(FamilySearchText)));
        familyGrid.Columns.Add(CreateTextColumn("Типов", nameof(FamilyFileItem.CachedTypeCountDisplay), 60));
        familyGrid.Columns.Add(CreateTextColumn("Каталог", nameof(FamilyFileItem.TypeCatalogDisplay), 70));
        familyGrid.Columns.Add(CreateTextColumn("Ширина", nameof(FamilyFileItem.WidthParameterDisplay), 76));
        familyGrid.Columns.Add(CreateTextColumn("Высота", nameof(FamilyFileItem.HeightParameterDisplay), 76));
        familyGrid.Columns.Add(CreateTextColumn("Материал", nameof(FamilyFileItem.MaterialParameterDisplay), 96));
        familyGrid.Columns.Add(CreateTextColumn("Preview", nameof(FamilyFileItem.ThumbnailDisplay), 88));
        familyGrid.Columns.Add(CreateTextColumn("Избр.", nameof(FamilyFileItem.FavoriteDisplay), 70));
        familyGrid.Columns.Add(CreateTextColumn("Размер", nameof(FamilyFileItem.SizeDisplay), 80));
        familyGrid.Columns.Add(CreateTextColumn("Статус", nameof(FamilyFileItem.Status), 150));
        panel.Children.Add(familyGrid);
        return panel;
    }

    private UIElement CreateAuditPanel()
    {
        DockPanel panel = new()
        {
            Height = 150,
            Margin = new Thickness(0, 10, 0, 0)
        };

        TextBlock title = CreateSubTitle("Аудит библиотеки");
        DockPanel.SetDock(title, Dock.Top);
        panel.Children.Add(title);

        auditGrid.AutoGenerateColumns = false;
        auditGrid.CanUserAddRows = false;
        auditGrid.CanUserDeleteRows = false;
        auditGrid.IsReadOnly = true;
        auditGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        auditGrid.SelectionMode = DataGridSelectionMode.Single;
        auditGrid.ItemsSource = auditIssues;
        auditGrid.SelectionChanged += (_, _) => SelectAuditIssue(auditGrid.SelectedItem as FamilyLibraryAuditIssue);
        auditGrid.Columns.Add(CreateTextColumn("Уровень", nameof(FamilyLibraryAuditIssue.SeverityDisplay), 70));
        auditGrid.Columns.Add(CreateTextColumn("Проверка", nameof(FamilyLibraryAuditIssue.KindDisplay), 120));
        auditGrid.Columns.Add(CreateTextColumn("Семейство", nameof(FamilyLibraryAuditIssue.FamilyName), 120));
        auditGrid.Columns.Add(CreateTextColumn("Описание", nameof(FamilyLibraryAuditIssue.Message), new DataGridLength(1, DataGridLengthUnitType.Star)));
        auditGrid.Columns.Add(CreateTextColumn("Кол.", nameof(FamilyLibraryAuditIssue.CountDisplay), 48));
        panel.Children.Add(auditGrid);
        return panel;
    }

    private UIElement CreateDetailsPanel()
    {
        DockPanel panel = new()
        {
            LastChildFill = true
        };

        TextBlock title = CreatePanelTitle("Детали");
        DockPanel.SetDock(title, Dock.Top);
        panel.Children.Add(title);

        detailsText.TextWrapping = TextWrapping.Wrap;
        detailsText.Margin = new Thickness(0, 0, 0, 12);
        DockPanel.SetDock(detailsText, Dock.Top);
        panel.Children.Add(detailsText);

        TextBlock previewTitle = CreateSubTitle("Preview");
        DockPanel.SetDock(previewTitle, Dock.Top);
        panel.Children.Add(previewTitle);

        Border previewFrame = new()
        {
            Height = 160,
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Background = Brushes.WhiteSmoke,
            Margin = new Thickness(0, 0, 0, 8)
        };
        WpfGrid previewHost = new();
        thumbnailImage.Stretch = Stretch.Uniform;
        thumbnailImage.Margin = new Thickness(8);
        previewHost.Children.Add(thumbnailImage);

        thumbnailPlaceholderText.Text = "Preview еще не создан.";
        thumbnailPlaceholderText.TextWrapping = TextWrapping.Wrap;
        thumbnailPlaceholderText.TextAlignment = TextAlignment.Center;
        thumbnailPlaceholderText.HorizontalAlignment = HorizontalAlignment.Center;
        thumbnailPlaceholderText.VerticalAlignment = VerticalAlignment.Center;
        thumbnailPlaceholderText.Margin = new Thickness(16);
        thumbnailPlaceholderText.Foreground = Brushes.DimGray;
        previewHost.Children.Add(thumbnailPlaceholderText);

        previewFrame.Child = previewHost;
        DockPanel.SetDock(previewFrame, Dock.Top);
        panel.Children.Add(previewFrame);

        refreshThumbnailButton.Margin = new Thickness(0, 0, 0, 12);
        refreshThumbnailButton.Click += (_, _) => RefreshSelectedThumbnail();
        DockPanel.SetDock(refreshThumbnailButton, Dock.Top);
        panel.Children.Add(refreshThumbnailButton);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };
        favoriteButton.Click += (_, _) => ToggleFavorite();
        actions.Children.Add(favoriteButton);

        loadButton.Click += (_, _) => LoadSelectedFamily(placeAfterLoad: false);
        actions.Children.Add(loadButton);
        DockPanel.SetDock(actions, Dock.Top);
        panel.Children.Add(actions);

        loadAndPlaceButton.Margin = new Thickness(0, 0, 0, 12);
        loadAndPlaceButton.Click += (_, _) => LoadSelectedFamily(placeAfterLoad: true);
        DockPanel.SetDock(loadAndPlaceButton, Dock.Top);
        panel.Children.Add(loadAndPlaceButton);

        refreshMetadataButton.Margin = new Thickness(0, 0, 0, 12);
        refreshMetadataButton.Click += (_, _) => RefreshSelectedMetadata();
        DockPanel.SetDock(refreshMetadataButton, Dock.Top);
        panel.Children.Add(refreshMetadataButton);

        TextBlock typesTitle = CreateSubTitle("Типы в файле/проекте");
        DockPanel.SetDock(typesTitle, Dock.Top);
        panel.Children.Add(typesTitle);

        typeList.ItemsSource = familyTypes;
        typeList.ItemTemplate = CreateTypeListTemplate();
        typeList.Height = 120;
        typeList.Margin = new Thickness(0, 0, 0, 12);
        typeList.SelectionChanged += (_, _) => SelectType(typeList.SelectedItem as FamilyTypeInfo);
        DockPanel.SetDock(typeList, Dock.Top);
        panel.Children.Add(typeList);

        TextBlock parametersTitle = CreateSubTitle("Параметры выбранного типа");
        DockPanel.SetDock(parametersTitle, Dock.Top);
        panel.Children.Add(parametersTitle);

        WpfGrid parameterFilters = new()
        {
            Margin = new Thickness(0, 0, 0, 8)
        };
        parameterFilters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        parameterFilters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126) });

        parameterSearchInput.Height = 30;
        parameterSearchInput.Margin = new Thickness(0, 0, 8, 0);
        parameterSearchInput.ToolTip = "Фильтр параметров по имени, значению, формуле или области.";
        parameterSearchInput.TextChanged += (_, _) => RefreshTypeParameters();
        WpfGrid.SetColumn(parameterSearchInput, 0);
        parameterFilters.Children.Add(parameterSearchInput);

        parameterPresetInput.Height = 30;
        parameterPresetInput.Items.Add(AllParameterPreset);
        parameterPresetInput.Items.Add(DimensionParameterPreset);
        parameterPresetInput.Items.Add(MaterialParameterPreset);
        parameterPresetInput.Items.Add(IdentityParameterPreset);
        parameterPresetInput.SelectedItem = AllParameterPreset;
        parameterPresetInput.SelectionChanged += (_, _) => RefreshTypeParameters();
        WpfGrid.SetColumn(parameterPresetInput, 1);
        parameterFilters.Children.Add(parameterPresetInput);

        DockPanel.SetDock(parameterFilters, Dock.Top);
        panel.Children.Add(parameterFilters);

        parameterGrid.AutoGenerateColumns = false;
        parameterGrid.CanUserAddRows = false;
        parameterGrid.CanUserDeleteRows = false;
        parameterGrid.IsReadOnly = true;
        parameterGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        parameterGrid.ItemsSource = typeParameters;
        parameterGrid.Height = 170;
        parameterGrid.Margin = new Thickness(0, 0, 0, 12);
        parameterGrid.Columns.Add(CreateHighlightedTextColumn("Параметр", nameof(FamilyTypeParameterInfo.Name), new DataGridLength(1, DataGridLengthUnitType.Star), nameof(ParameterHighlightText)));
        parameterGrid.Columns.Add(CreateHighlightedTextColumn("Значение", nameof(FamilyTypeParameterInfo.ValueDisplay), 95, nameof(ParameterHighlightText)));
        parameterGrid.Columns.Add(CreateHighlightedTextColumn("Формула", nameof(FamilyTypeParameterInfo.FormulaDisplay), 95, nameof(ParameterHighlightText)));
        parameterGrid.Columns.Add(CreateHighlightedTextColumn("Область", nameof(FamilyTypeParameterInfo.Scope), 80, nameof(ParameterHighlightText)));
        parameterGrid.Columns.Add(CreateTextColumn("Тип", nameof(FamilyTypeParameterInfo.StorageType), 70));
        DockPanel.SetDock(parameterGrid, Dock.Top);
        panel.Children.Add(parameterGrid);

        TextBlock historyTitle = CreateSubTitle("История");
        DockPanel.SetDock(historyTitle, Dock.Top);
        panel.Children.Add(historyTitle);

        historyList.ItemsSource = historyItems;
        historyList.DisplayMemberPath = nameof(FamilyLoadHistoryItem.DisplayName);
        panel.Children.Add(historyList);
        return panel;
    }

    private void LoadProfileState()
    {
        isRefreshing = true;
        folders.Clear();
        foreach (FamilyLibraryFolder folder in profile.LibraryFolders)
        {
            folders.Add(folder);
        }

        libraryFiles.Clear();
        foreach (FamilyLibraryFile file in profile.LibraryFiles)
        {
            libraryFiles.Add(file);
        }

        allFamilies.Clear();
        foreach (FamilyFileItem family in profile.CachedFiles)
        {
            SubscribeFamily(family);
            allFamilies.Add(family);
        }

        isRefreshing = false;
        RefreshCategories();
        RefreshHistory();
        RefreshVisibleFamilies();
        SelectFamily(visibleFamilies.FirstOrDefault());
    }

    private void AddFolder()
    {
        string? folderPath = SelectFolderPath();
        if (folderPath is null || string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        AddLibraryFolder(folderPath);
    }

    private void AddFile()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Выберите файл семейства",
            Filter = "Revit families (*.rfa)|*.rfa",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        AddLibraryFile(dialog.FileName);
    }

    private void AddLibraryFolder(string folderPath)
    {
        folderPath = FamilyPathNormalizer.Normalize(folderPath);
        if (!Directory.Exists(folderPath))
        {
            statusText.Text = "Папка библиотеки не найдена.";
            return;
        }

        if (folders.Any(folder => string.Equals(folder.Path, folderPath, StringComparison.CurrentCultureIgnoreCase)))
        {
            statusText.Text = "Эта папка уже есть в списке библиотек.";
            return;
        }

        folders.Add(new FamilyLibraryFolder
        {
            Path = folderPath,
            IsEnabled = true
        });
        SaveProfile();
        ScanLibraries();
    }

    private void AddLibraryFile(string filePath)
    {
        filePath = FamilyPathNormalizer.Normalize(filePath);
        if (!File.Exists(filePath) || !string.Equals(Path.GetExtension(filePath), ".rfa", StringComparison.CurrentCultureIgnoreCase))
        {
            statusText.Text = "Выберите существующий .rfa файл.";
            return;
        }

        if (libraryFiles.Any(file => string.Equals(file.Path, filePath, StringComparison.CurrentCultureIgnoreCase)))
        {
            statusText.Text = "Этот файл уже есть в списке библиотек.";
            return;
        }

        libraryFiles.Add(new FamilyLibraryFile
        {
            Path = filePath,
            IsEnabled = true
        });
        SaveProfile();
        ScanLibraries();
    }

    private void RemoveSelectedLibrarySource()
    {
        if (folderList.SelectedItem is FamilyLibraryFolder folder)
        {
            folders.Remove(folder);
            SaveProfile();
            ScanLibraries();
            return;
        }

        if (fileList.SelectedItem is FamilyLibraryFile file)
        {
            libraryFiles.Remove(file);
            SaveProfile();
            ScanLibraries();
            return;
        }

        statusText.Text = "Выберите папку или файл для удаления из списка.";
    }

    private string? SelectFolderPath()
    {
        BrowseInfo browseInfo = new()
        {
            HwndOwner = new WindowInteropHelper(this).Handle,
            Title = "Выберите папку библиотеки семейств",
            Flags = BrowseInfoReturnOnlyFileSystemDirectories
                | BrowseInfoNewDialogStyle
                | BrowseInfoNoNewFolderButton
        };

        IntPtr itemIdList = SHBrowseForFolder(ref browseInfo);
        if (itemIdList == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            StringBuilder path = new(MaxPathLength);
            return SHGetPathFromIDList(itemIdList, path)
                ? path.ToString()
                : null;
        }
        finally
        {
            CoTaskMemFree(itemIdList);
        }
    }

    private void ScanLibraries()
    {
        SaveProfile();
        HashSet<string> favoritePaths = profile.FavoritePaths.ToHashSet(FamilyPathNormalizer.Comparer);
        Dictionary<string, DateTimeOffset> lastLoadedByPath = profile.History
            .GroupBy(item => FamilyPathNormalizer.Normalize(item.FilePath), FamilyPathNormalizer.Comparer)
            .ToDictionary(group => group.Key, group => group.Max(item => item.LoadedAtUtc), FamilyPathNormalizer.Comparer);
        Dictionary<string, FamilyFileItem> previousFamilies = allFamilies
            .GroupBy(family => FamilyPathNormalizer.Normalize(family.FilePath), FamilyPathNormalizer.Comparer)
            .ToDictionary(group => group.Key, group => group.First(), FamilyPathNormalizer.Comparer);

        FamilyLibraryScanResult result = scanner.Scan(profile.LibraryFolders, profile.LibraryFiles, favoritePaths, lastLoadedByPath);
        allFamilies.Clear();
        foreach (FamilyFileItem family in result.Files)
        {
            PreserveCachedMetadata(family, previousFamilies);
            SubscribeFamily(family);
            allFamilies.Add(family);
        }

        profile.CachedFiles = allFamilies.ToList();
        profile.CacheUpdatedAtUtc = DateTimeOffset.UtcNow;
        profileStorage.Save(profile);
        RefreshCategories();
        RefreshVisibleFamilies();

        string warningText = result.Warnings.Count == 0
            ? string.Empty
            : $" Предупреждения: {string.Join(" ", result.Warnings.Take(2))}";
        statusText.Text = $"Просканировано папок: {result.ScannedFolderCount}. Файлов: {result.ScannedFileCount}. Не найдено папок: {result.MissingFolderCount}. Не найдено файлов: {result.MissingFileCount}. Найдено семейств: {allFamilies.Count}.{warningText}";
    }

    private static void PreserveCachedMetadata(
        FamilyFileItem family,
        IReadOnlyDictionary<string, FamilyFileItem> previousFamilies)
    {
        if (!previousFamilies.TryGetValue(FamilyPathNormalizer.Normalize(family.FilePath), out FamilyFileItem? previous)
            || previous.SizeBytes != family.SizeBytes
            || previous.LastWriteTimeUtc != family.LastWriteTimeUtc)
        {
            return;
        }

        family.ThumbnailPath = previous.ThumbnailPath;
        family.ThumbnailUpdatedAtUtc = previous.ThumbnailUpdatedAtUtc;
        if (previous.MetadataUpdatedAtUtc is null)
        {
            return;
        }

        family.Category = previous.Category;
        family.MetadataUpdatedAtUtc = previous.MetadataUpdatedAtUtc;
        family.CachedTypes = previous.CachedTypes
            .Select(type => new FamilyTypeInfo(
                type.ElementId,
                type.Name,
                type.Parameters.Select(parameter => new FamilyTypeParameterInfo(
                    parameter.Name,
                    parameter.Value,
                    parameter.StorageType,
                    parameter.Scope,
                    parameter.Formula))))
            .ToList();
    }

    private void RefreshCategories()
    {
        string? selected = categoryInput.SelectedItem as string;
        categoryInput.Items.Clear();
        categoryInput.Items.Add(FamilyManagerDefaults.AllCategories);
        foreach (string category in allFamilies
            .Select(family => family.Category)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase))
        {
            categoryInput.Items.Add(category);
        }

        categoryInput.SelectedItem = !string.IsNullOrWhiteSpace(selected)
            && categoryInput.Items.Cast<object>().Any(item => string.Equals(item.ToString(), selected, StringComparison.CurrentCultureIgnoreCase))
            ? selected
            : FamilyManagerDefaults.AllCategories;
    }

    private void RefreshVisibleFamilies()
    {
        if (isRefreshing)
        {
            return;
        }

        UpdateHighlightTextState();
        string search = FamilySearchText;
        string category = categoryInput.SelectedItem as string ?? FamilyManagerDefaults.AllCategories;
        bool favoritesOnly = favoritesOnlyInput.IsChecked == true;
        IEnumerable<FamilyFileItem> families = allFamilies;
        foreach (FamilyFileItem family in allFamilies)
        {
            family.SearchMatchText = searchMatchService.FindMatchText(family, search);
        }

        if (favoritesOnly)
        {
            families = families.Where(family => family.IsFavorite);
        }

        if (!string.Equals(category, FamilyManagerDefaults.AllCategories, StringComparison.CurrentCultureIgnoreCase))
        {
            families = families.Where(family => string.Equals(family.Category, category, StringComparison.CurrentCultureIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            families = families.Where(family => searchMatchService.Matches(family, search));
        }

        visibleFamilies.Clear();
        foreach (FamilyFileItem family in families)
        {
            visibleFamilies.Add(family);
        }

        RefreshLibraryTree();
        if (selectedFamily is not null && visibleFamilies.Contains(selectedFamily))
        {
            familyGrid.SelectedItem = selectedFamily;
        }
        else
        {
            SelectFamily(visibleFamilies.FirstOrDefault());
        }

        UpdateStatus();
    }

    private void RefreshLibraryTree()
    {
        IReadOnlyList<FamilyLibraryTreeNode> nodes = treeBuilder.Build(folders.ToList(), visibleFamilies.ToList());
        libraryTreeNodes.Clear();
        foreach (FamilyLibraryTreeNode node in nodes)
        {
            libraryTreeNodes.Add(node);
        }
    }

    private void RefreshHistory()
    {
        historyItems.Clear();
        foreach (FamilyLoadHistoryItem item in profile.History.OrderByDescending(item => item.LoadedAtUtc).Take(40))
        {
            historyItems.Add(item);
        }
    }

    private void RunLibraryAudit()
    {
        auditIssues.Clear();
        IReadOnlyList<FamilyLibraryAuditIssue> issues = auditService.Audit(allFamilies, folders.ToList());
        foreach (FamilyLibraryAuditIssue issue in issues)
        {
            auditIssues.Add(issue);
        }

        int errorCount = issues.Count(issue => issue.Severity == FamilyLibraryAuditSeverity.Error);
        int warningCount = issues.Count(issue => issue.Severity == FamilyLibraryAuditSeverity.Warning);
        int infoCount = issues.Count(issue => issue.Severity == FamilyLibraryAuditSeverity.Info);
        statusText.Text = $"Аудит библиотеки: ошибок {errorCount}, рисков {warningCount}, инфо {infoCount}. Проверено семейств: {allFamilies.Count}.";
    }

    private void SelectAuditIssue(FamilyLibraryAuditIssue? issue)
    {
        if (issue is null || string.IsNullOrWhiteSpace(issue.FilePath))
        {
            return;
        }

        FamilyFileItem? family = allFamilies.FirstOrDefault(item =>
            string.Equals(
                FamilyPathNormalizer.Normalize(item.FilePath),
                FamilyPathNormalizer.Normalize(issue.FilePath),
                StringComparison.CurrentCultureIgnoreCase));
        if (family is null)
        {
            statusText.Text = issue.GroupKey;
            return;
        }

        if (!visibleFamilies.Contains(family))
        {
            statusText.Text = "Семейство из аудита скрыто текущими фильтрами.";
            SelectFamily(family);
            return;
        }

        familyGrid.SelectedItem = family;
        familyGrid.ScrollIntoView(family);
        SelectFamily(family);
        statusText.Text = string.IsNullOrWhiteSpace(issue.GroupKey)
            ? issue.Message
            : issue.GroupKey;
    }

    private void SelectFamily(FamilyFileItem? family)
    {
        selectedFamily = family;
        familyGrid.SelectedItem = family;
        familyTypes.Clear();
        typeParameters.Clear();
        selectedType = null;

        if (family is null)
        {
            detailsText.Text = "Семейство не выбрано.";
            favoriteButton.IsEnabled = false;
            loadButton.IsEnabled = false;
            loadAndPlaceButton.IsEnabled = false;
            refreshMetadataButton.IsEnabled = false;
            refreshThumbnailButton.IsEnabled = false;
            UpdateThumbnailPreview(null);
            UpdateStatus();
            return;
        }

        favoriteButton.IsEnabled = true;
        loadButton.IsEnabled = true;
        loadAndPlaceButton.IsEnabled = true;
        refreshMetadataButton.IsEnabled = true;
        refreshThumbnailButton.IsEnabled = true;
        favoriteButton.Content = IconFactory.CreateButtonContent(
            family.IsFavorite ? TrueBimIcon.Close : TrueBimIcon.Apply,
            family.IsFavorite ? "Убрать" : "В избранное");

        detailsText.Text =
            $"Имя: {family.Name}\n" +
            $"Категория: {family.Category}\n" +
            $"Метаданные: {family.MetadataDisplay}\n" +
            $"Preview: {family.ThumbnailDisplay}\n" +
            $"Типов в файле: {family.CachedTypes.Count}\n" +
            $"Каталог типов: {family.TypeCatalogDetailsDisplay}\n" +
            $"Файл: {family.FilePath}\n" +
            $"Размер: {family.SizeDisplay}\n" +
            $"Изменён: {family.LastWriteDisplay}";
        UpdateThumbnailPreview(family);

        HashSet<string> typeNames = new(StringComparer.CurrentCultureIgnoreCase);
        foreach (FamilyTypeInfo type in family.CachedTypes)
        {
            AddTypeOption(type, typeNames);
        }

        foreach (string typeName in family.TypeCatalogTypeNames)
        {
            AddTypeOption(new FamilyTypeInfo(0, typeName), typeNames);
        }

        try
        {
            foreach (FamilyTypeInfo type in loadService.CollectLoadedTypes(document, family.Name))
            {
                AddTypeOption(type, typeNames);
            }
        }
        catch (Exception exception)
        {
            logger.Warning($"Failed to collect loaded family types for '{family.Name}': {exception.Message}");
        }

        FamilyTypeInfo? preferredType = familyTypes.FirstOrDefault(type => type.Parameters.Count > 0)
            ?? familyTypes.FirstOrDefault();
        typeList.SelectedItem = preferredType;
        SelectType(preferredType);
        UpdateStatus();
    }

    private void AddTypeOption(FamilyTypeInfo type, ISet<string> typeNames)
    {
        if (string.IsNullOrWhiteSpace(type.Name) || !typeNames.Add(type.Name))
        {
            return;
        }

        familyTypes.Add(type);
    }

    private void SelectType(FamilyTypeInfo? type)
    {
        selectedType = type;
        RefreshTypeParameters();
    }

    private void RefreshTypeParameters()
    {
        UpdateHighlightTextState();
        typeParameters.Clear();
        if (selectedType is null)
        {
            return;
        }

        string search = parameterSearchInput.Text.Trim();
        string preset = parameterPresetInput.SelectedItem as string ?? AllParameterPreset;
        IEnumerable<FamilyTypeParameterInfo> parameters = selectedType.Parameters;

        if (!string.Equals(preset, AllParameterPreset, StringComparison.CurrentCultureIgnoreCase))
        {
            string[] tokens = preset switch
            {
                DimensionParameterPreset => DimensionParameterTokens,
                MaterialParameterPreset => MaterialParameterTokens,
                IdentityParameterPreset => IdentityParameterTokens,
                _ => []
            };

            parameters = parameters.Where(parameter => ParameterNameMatches(parameter.Name, tokens));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters = parameters.Where(parameter =>
                parameter.Name.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0
                || parameter.Value.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0
                || parameter.Formula.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0
                || parameter.Scope.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        foreach (FamilyTypeParameterInfo parameter in parameters)
        {
            typeParameters.Add(parameter);
        }
    }

    private void UpdateHighlightTextState()
    {
        FamilySearchText = searchInput.Text.Trim();
        string parameterSearch = parameterSearchInput.Text.Trim();
        ParameterHighlightText = string.IsNullOrWhiteSpace(parameterSearch)
            ? FamilySearchText
            : parameterSearch;
    }

    private void UpdateThumbnailPreview(FamilyFileItem? family)
    {
        thumbnailImage.Source = null;
        if (family is null)
        {
            ShowThumbnailPlaceholder("Семейство не выбрано.");
            return;
        }

        string? thumbnailPath = thumbnailService.TryGetCachedThumbnail(family);
        if (string.IsNullOrWhiteSpace(thumbnailPath))
        {
            ShowThumbnailPlaceholder("Preview еще не создан.");
            return;
        }

        try
        {
            BitmapImage bitmap = new();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(thumbnailPath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            thumbnailImage.Source = bitmap;
            thumbnailPlaceholderText.Visibility = System.Windows.Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            logger.Warning($"Failed to load family thumbnail '{thumbnailPath}': {exception.Message}");
            ShowThumbnailPlaceholder("Не удалось прочитать preview.");
        }
    }

    private void ShowThumbnailPlaceholder(string text)
    {
        thumbnailPlaceholderText.Text = text;
        thumbnailPlaceholderText.Visibility = System.Windows.Visibility.Visible;
    }

    private void RefreshSelectedThumbnail()
    {
        if (selectedFamily is null)
        {
            statusText.Text = "Выберите семейство для обновления preview.";
            return;
        }

        if (!File.Exists(selectedFamily.FilePath))
        {
            selectedFamily.Status = "Файл не найден";
            statusText.Text = "Файл семейства не найден. Пересканируйте библиотеку.";
            UpdateThumbnailPreview(selectedFamily);
            return;
        }

        refreshThumbnailButton.IsEnabled = false;
        statusText.Text = $"Чтение preview: {selectedFamily.Name}...";
        FamilyThumbnailResult result = thumbnailService.Refresh(uiApplication.Application, selectedFamily, logger);
        selectedFamily.Status = result.Message;
        if (result.Succeeded)
        {
            selectedFamily.ThumbnailPath = result.ThumbnailPath;
            selectedFamily.ThumbnailUpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        SaveProfile();
        SelectFamily(selectedFamily);
        refreshThumbnailButton.IsEnabled = selectedFamily is not null;
        statusText.Text = result.Message;
    }

    private void SelectTreeNode(FamilyLibraryTreeNode? node)
    {
        if (node is null)
        {
            return;
        }

        if (node.Kind is FamilyLibraryTreeNodeKind.Category)
        {
            categoryInput.SelectedItem = categoryInput.Items
                .Cast<object>()
                .FirstOrDefault(item => string.Equals(item.ToString(), node.Title, StringComparison.CurrentCultureIgnoreCase))
                ?? categoryInput.SelectedItem;
            return;
        }

        if (node.Kind is not (FamilyLibraryTreeNodeKind.Family or FamilyLibraryTreeNodeKind.Type))
        {
            return;
        }

        SelectFamilyByPath(node.FamilyPath, node.TypeName);
    }

    private void SelectTreeItemUnderPointer(DependencyObject? source)
    {
        TreeViewItem? item = FindVisualParent<TreeViewItem>(source);
        if (item is null)
        {
            return;
        }

        item.IsSelected = true;
        item.Focus();
    }

    private void SelectFamilyRowUnderPointer(DependencyObject? source)
    {
        DataGridRow? row = FindVisualParent<DataGridRow>(source);
        if (row?.Item is not FamilyFileItem family)
        {
            return;
        }

        familyGrid.SelectedItem = family;
        SelectFamily(family);
    }

    private void SelectFamilyByPath(string familyPath, string typeName)
    {
        string normalizedPath = FamilyPathNormalizer.Normalize(familyPath);
        FamilyFileItem? family = visibleFamilies.FirstOrDefault(item =>
            string.Equals(FamilyPathNormalizer.Normalize(item.FilePath), normalizedPath, StringComparison.CurrentCultureIgnoreCase));
        if (family is null)
        {
            statusText.Text = "Семейство из структуры не найдено в текущем списке.";
            return;
        }

        familyGrid.SelectedItem = family;
        familyGrid.ScrollIntoView(family);
        SelectFamily(family);
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return;
        }

        FamilyTypeInfo? type = familyTypes.FirstOrDefault(item =>
            string.Equals(item.Name, typeName, StringComparison.CurrentCultureIgnoreCase));
        if (type is not null)
        {
            typeList.SelectedItem = type;
            SelectType(type);
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? source)
        where T : DependencyObject
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is T typed)
            {
                return typed;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static bool ParameterNameMatches(string parameterName, IEnumerable<string> tokens)
    {
        return tokens.Any(token =>
            parameterName.Equals(token, StringComparison.CurrentCultureIgnoreCase)
            || parameterName.IndexOf(token, StringComparison.CurrentCultureIgnoreCase) >= 0);
    }

    private void ToggleFavorite()
    {
        if (selectedFamily is null)
        {
            return;
        }

        selectedFamily.IsFavorite = !selectedFamily.IsFavorite;
        SaveProfile();
        SelectFamily(selectedFamily);
        RefreshVisibleFamilies();
    }

    private void RefreshSelectedMetadata()
    {
        if (selectedFamily is null)
        {
            statusText.Text = "Выберите семейство для обновления метаданных.";
            return;
        }

        if (!File.Exists(selectedFamily.FilePath))
        {
            selectedFamily.Status = "Файл не найден";
            statusText.Text = "Файл семейства не найден. Пересканируйте библиотеку.";
            return;
        }

        refreshMetadataButton.IsEnabled = false;
        statusText.Text = $"Чтение метаданных: {selectedFamily.Name}...";
        FamilyMetadataResult result = metadataService.Read(uiApplication.Application, document, selectedFamily.FilePath, logger);
        selectedFamily.Status = result.Message;

        if (result.Succeeded)
        {
            ApplyMetadataResult(selectedFamily, result);
            SaveProfile();
            RefreshCategories();
            RefreshVisibleFamilies();
            if (visibleFamilies.Contains(selectedFamily))
            {
                SelectFamily(selectedFamily);
            }
            else
            {
                SelectFamily(visibleFamilies.FirstOrDefault());
            }
        }
        else
        {
            SaveProfile();
            SelectFamily(selectedFamily);
        }

        refreshMetadataButton.IsEnabled = selectedFamily is not null;
        statusText.Text = result.Message;
    }

    private void RefreshSelectedFolderMetadata()
    {
        if (folderList.SelectedItem is not FamilyLibraryFolder folder)
        {
            statusText.Text = "Выберите папку библиотеки для пакетного обновления метаданных.";
            return;
        }

        IReadOnlyList<FamilyFileItem> families = metadataBatchSelector.SelectFolderScope(allFamilies, folder.Path);
        if (families.Count == 0)
        {
            statusText.Text = "В выбранной папке нет семейств в кэше. Сначала нажмите Сканировать.";
            return;
        }

        MessageBoxResult decision = MessageBox.Show(
            this,
            $"Обновить метаданные для семейств в выбранной папке?\n\nПапка: {folder.Path}\nСемейств: {families.Count}\n\nОперация последовательно открывает `.rfa` файлы, читает категорию, типы и параметры, затем закрывает файлы без сохранения.",
            "Диспетчер семейств",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        refreshFolderMetadataButton.IsEnabled = false;
        refreshMetadataButton.IsEnabled = false;
        int updated = 0;
        int failed = 0;
        try
        {
            for (int index = 0; index < families.Count; index++)
            {
                FamilyFileItem family = families[index];
                if (!File.Exists(family.FilePath))
                {
                    family.Status = "Файл не найден";
                    failed++;
                    continue;
                }

                statusText.Text = $"Чтение метаданных {index + 1}/{families.Count}: {family.Name}...";
                FamilyMetadataResult result = metadataService.Read(uiApplication.Application, document, family.FilePath, logger);
                family.Status = result.Message;
                if (result.Succeeded)
                {
                    ApplyMetadataResult(family, result);
                    updated++;
                }
                else
                {
                    failed++;
                }
            }

            SaveProfile();
            RefreshCategories();
            RefreshVisibleFamilies();
            if (selectedFamily is not null && visibleFamilies.Contains(selectedFamily))
            {
                SelectFamily(selectedFamily);
            }
            else
            {
                SelectFamily(visibleFamilies.FirstOrDefault());
            }

            statusText.Text = $"Пакетное обновление завершено. Обновлено: {updated}. Ошибок/пропусков: {failed}.";
        }
        finally
        {
            refreshFolderMetadataButton.IsEnabled = true;
            refreshMetadataButton.IsEnabled = selectedFamily is not null;
        }
    }

    private static void ApplyMetadataResult(FamilyFileItem family, FamilyMetadataResult result)
    {
        family.Category = result.Category;
        family.CachedTypes = result.Types.ToList();
        family.TypeCatalogPath = result.TypeCatalogPath;
        family.TypeCatalogTypeNames = result.TypeCatalogTypeNames.ToList();
        family.MetadataUpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private void LoadSelectedFamily(bool placeAfterLoad)
    {
        if (selectedFamily is null)
        {
            statusText.Text = "Выберите семейство для загрузки.";
            return;
        }

        if (!File.Exists(selectedFamily.FilePath))
        {
            selectedFamily.Status = "Файл не найден";
            statusText.Text = "Файл семейства не найден. Пересканируйте библиотеку.";
            return;
        }

        string? requestedTypeName = typeList.SelectedItem is FamilyTypeInfo selectedType
            ? selectedType.Name
            : null;
        FamilyLoadResult? result;
        if (string.IsNullOrWhiteSpace(requestedTypeName))
        {
            result = LoadWholeFamilyWithOverwritePrompt(selectedFamily);
        }
        else
        {
            result = LoadRequestedFamilyType(selectedFamily, requestedTypeName!);
        }
        if (result is null)
        {
            return;
        }

        selectedFamily.Status = result.Message;
        statusText.Text = result.Message;

        if (!result.Succeeded)
        {
            Autodesk.Revit.UI.TaskDialog.Show("Диспетчер семейств", result.Message);
            return;
        }

        string action = placeAfterLoad ? "Загрузка и размещение" : "Загрузка";
        if (!string.IsNullOrWhiteSpace(requestedTypeName))
        {
            action += $" типа {requestedTypeName}";
        }

        AddHistory(selectedFamily, action);
        SelectFamily(selectedFamily);
        SaveProfile();

        FamilySymbol? confirmedSymbol = null;
        if (!string.IsNullOrWhiteSpace(requestedTypeName))
        {
            confirmedSymbol = loadService.ResolveSymbolExact(document, selectedFamily.Name, requestedTypeName!);
            if (confirmedSymbol is null)
            {
                string message = $"Семейство загружено, но выбранный тип не найден в проекте: {requestedTypeName}.";
                selectedFamily.Status = message;
                statusText.Text = message;
                Autodesk.Revit.UI.TaskDialog.Show("Диспетчер семейств", message);
                return;
            }
        }

        if (placeAfterLoad)
        {
            RequestPlacement(selectedFamily, requestedTypeName);
        }

        string confirmation = confirmedSymbol is null
            ? result.Message
            : $"{result.Message}\nТип подтверждён в проекте: {confirmedSymbol.Name}.";
        Autodesk.Revit.UI.TaskDialog.Show("Диспетчер семейств", confirmation);
    }

    private FamilyLoadResult? LoadRequestedFamilyType(FamilyFileItem family, string requestedTypeName)
    {
        bool overwrite = false;
        FamilySymbol? existingSymbol = loadService.ResolveSymbolExact(document, family.Name, requestedTypeName);
        if (existingSymbol is not null)
        {
            MessageBoxResult decision = MessageBox.Show(
                this,
                $"Тип уже загружен в проект: {requestedTypeName}\n\nДа - перезагрузить тип из файла библиотеки.\nНет - использовать уже загруженный тип.\nОтмена - остановить операцию.",
                "Диспетчер семейств",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (decision == MessageBoxResult.Cancel)
            {
                return null;
            }

            if (decision == MessageBoxResult.No)
            {
                return new FamilyLoadResult(
                    FamilyLoadStatus.AlreadyLoaded,
                    family.Name,
                    $"Тип уже загружен в проект: {requestedTypeName}.");
            }

            overwrite = true;
        }

        FamilyLoadResult result = loadService.LoadSymbol(document, family.FilePath, requestedTypeName, overwrite, logger);
        if (result.Succeeded)
        {
            return result;
        }

        MessageBoxResult fallbackDecision = MessageBox.Show(
            this,
            $"{result.Message}\n\nВыполнить полную загрузку семейства как fallback?",
            "Диспетчер семейств",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        return fallbackDecision == MessageBoxResult.Yes
            ? LoadWholeFamilyWithOverwritePrompt(family)
            : result;
    }

    private FamilyLoadResult? LoadWholeFamilyWithOverwritePrompt(FamilyFileItem family)
    {
        bool overwrite = false;
        if (loadService.FamilyExists(document, family.Name))
        {
            MessageBoxResult decision = MessageBox.Show(
                this,
                "Семейство уже загружено в проект.\n\nДа - перезаписать из файла библиотеки.\nНет - использовать уже загруженное семейство.\nОтмена - остановить операцию.",
                "Диспетчер семейств",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (decision == MessageBoxResult.Cancel)
            {
                return null;
            }

            overwrite = decision == MessageBoxResult.Yes;
        }

        return loadService.Load(document, family.FilePath, overwrite, logger);
    }

    private void RequestPlacement(FamilyFileItem family, string? requestedTypeName)
    {
        try
        {
            FamilySymbol? symbol = string.IsNullOrWhiteSpace(requestedTypeName)
                ? loadService.ResolveSymbol(document, family.Name, null)
                : loadService.ResolveSymbolExact(document, family.Name, requestedTypeName!);
            if (symbol is null)
            {
                statusText.Text = string.IsNullOrWhiteSpace(requestedTypeName)
                    ? "Семейство загружено, но подходящий тип для размещения не найден."
                    : $"Семейство загружено, но выбранный тип для размещения не найден: {requestedTypeName}.";
                return;
            }

            loadService.ActivateAndRequestPlacement(uiDocument, symbol);
            statusText.Text = $"Запущено штатное размещение типа: {symbol.Name}.";
        }
        catch (Exception exception)
        {
            logger.Error($"Failed to start family placement for '{family.FilePath}'.", exception);
            Autodesk.Revit.UI.TaskDialog.Show("Диспетчер семейств", "Семейство загружено, но не удалось запустить размещение. Используйте логи для диагностики.");
        }
    }

    private void AddHistory(FamilyFileItem family, string action)
    {
        DateTimeOffset loadedAtUtc = DateTimeOffset.UtcNow;
        family.LastLoadedAtUtc = loadedAtUtc;
        profile.History.Insert(0, new FamilyLoadHistoryItem
        {
            FilePath = family.FilePath,
            FamilyName = family.Name,
            Action = action,
            LoadedAtUtc = loadedAtUtc
        });
        profile = FamilyManagerProfileStorage.Normalize(profile);
        RefreshHistory();
    }

    private void SaveProfile()
    {
        profile.LibraryFolders = folders.ToList();
        profile.LibraryFiles = libraryFiles.ToList();
        profile.CachedFiles = allFamilies.ToList();
        profile.FavoritePaths = allFamilies
            .Where(family => family.IsFavorite)
            .Select(family => family.FilePath)
            .ToList();
        profile = FamilyManagerProfileStorage.Normalize(profile);
        profileStorage.Save(profile);
    }

    private void SubscribeFamily(FamilyFileItem family)
    {
        family.PropertyChanged += OnFamilyPropertyChanged;
    }

    private void OnFamilyPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(FamilyFileItem.IsFavorite))
        {
            SaveProfile();
            RefreshVisibleFamilies();
        }
    }

    private void UpdateStatus()
    {
        string cacheText = profile.CacheUpdatedAtUtc is null
            ? "Кэш ещё не создан."
            : $"Кэш: {profile.CacheUpdatedAtUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm}.";
        int favoritesCount = allFamilies.Count(family => family.IsFavorite);
        int metadataCount = allFamilies.Count(family => family.MetadataUpdatedAtUtc is not null);
        statusText.Text = $"Папок: {folders.Count}. Файлов: {libraryFiles.Count}. Семейств в кэше: {allFamilies.Count}. Показано: {visibleFamilies.Count}. Избранных: {favoritesCount}. Метаданные: {metadataCount}. {cacheText}";
    }

    private WpfContextMenu CreateTreeContextMenu()
    {
        WpfContextMenu menu = new();
        WpfMenuItem openItem = new()
        {
            Header = "Открыть в Explorer"
        };
        openItem.Click += (_, _) => OpenTreeNodeInExplorer();
        menu.Items.Add(openItem);

        WpfMenuItem copyItem = new()
        {
            Header = "Скопировать путь"
        };
        copyItem.Click += (_, _) => CopyTreeNodePath();
        menu.Items.Add(copyItem);
        return menu;
    }

    private WpfContextMenu CreateFamilyContextMenu()
    {
        WpfContextMenu menu = new();
        WpfMenuItem openItem = new()
        {
            Header = "Открыть в Explorer"
        };
        openItem.Click += (_, _) => OpenSelectedFamilyInExplorer();
        menu.Items.Add(openItem);

        WpfMenuItem copyItem = new()
        {
            Header = "Скопировать путь"
        };
        copyItem.Click += (_, _) => CopySelectedFamilyPath();
        menu.Items.Add(copyItem);
        return menu;
    }

    private void OpenTreeNodeInExplorer()
    {
        if (libraryTree.SelectedItem is not FamilyLibraryTreeNode node)
        {
            statusText.Text = "Выберите узел структуры.";
            return;
        }

        OpenPathInExplorer(node.ExplorerPath);
    }

    private void CopyTreeNodePath()
    {
        if (libraryTree.SelectedItem is not FamilyLibraryTreeNode node)
        {
            statusText.Text = "Выберите узел структуры.";
            return;
        }

        CopyPath(node.ExplorerPath);
    }

    private void OpenSelectedFamilyInExplorer()
    {
        if (selectedFamily is null)
        {
            statusText.Text = "Выберите семейство.";
            return;
        }

        OpenPathInExplorer(selectedFamily.FilePath);
    }

    private void CopySelectedFamilyPath()
    {
        if (selectedFamily is null)
        {
            statusText.Text = "Выберите семейство.";
            return;
        }

        CopyPath(selectedFamily.FilePath);
    }

    private void OpenPathInExplorer(string path)
    {
        string normalizedPath = FamilyPathNormalizer.Normalize(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            statusText.Text = "Путь не задан.";
            return;
        }

        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "explorer.exe",
                UseShellExecute = true
            };
            if (File.Exists(normalizedPath))
            {
                startInfo.Arguments = $"/select,\"{normalizedPath}\"";
            }
            else if (Directory.Exists(normalizedPath))
            {
                startInfo.Arguments = $"\"{normalizedPath}\"";
            }
            else
            {
                statusText.Text = $"Путь не найден: {normalizedPath}";
                return;
            }

            Process.Start(startInfo);
            statusText.Text = $"Открыто в Explorer: {normalizedPath}";
        }
        catch (Exception exception)
        {
            logger.Warning($"Failed to open path in Explorer '{normalizedPath}': {exception.Message}");
            statusText.Text = "Не удалось открыть путь в Explorer. Используйте логи для диагностики.";
        }
    }

    private void CopyPath(string path)
    {
        string normalizedPath = FamilyPathNormalizer.Normalize(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            statusText.Text = "Путь не задан.";
            return;
        }

        try
        {
            Clipboard.SetText(normalizedPath);
            statusText.Text = $"Путь скопирован: {normalizedPath}";
        }
        catch (Exception exception)
        {
            logger.Warning($"Failed to copy path '{normalizedPath}': {exception.Message}");
            statusText.Text = "Не удалось скопировать путь в буфер обмена.";
        }
    }

    private static TextBlock CreatePanelTitle(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        };
    }

    private static TextBlock CreateSubTitle(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
    }

    private static HierarchicalDataTemplate CreateTreeTemplate()
    {
        HierarchicalDataTemplate template = new(typeof(FamilyLibraryTreeNode))
        {
            ItemsSource = new WpfBinding(nameof(FamilyLibraryTreeNode.Children))
        };
        FrameworkElementFactory text = new(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(FamilyLibraryTreeNode.Title)));
        text.SetBinding(FrameworkElement.ToolTipProperty, new WpfBinding(nameof(FamilyLibraryTreeNode.ExplorerPath)));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        template.VisualTree = text;
        return template;
    }

    private static DataTemplate CreateTypeListTemplate()
    {
        DataTemplate template = new(typeof(FamilyTypeInfo));
        FrameworkElementFactory text = CreateHighlightedTextFactory(
            nameof(FamilyTypeInfo.Name),
            nameof(FamilySearchText));
        text.SetBinding(FrameworkElement.ToolTipProperty, new WpfBinding(nameof(FamilyTypeInfo.Name)));
        template.VisualTree = text;
        return template;
    }

    private static Button CreateButton(string text, TrueBimIcon icon, double minWidth)
    {
        return new Button
        {
            Content = IconFactory.CreateButtonContent(icon, text),
            MinWidth = minWidth,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0)
        };
    }

    private static DataGridTextColumn CreateTextColumn(string header, string bindingPath, double width)
    {
        return CreateTextColumn(header, bindingPath, new DataGridLength(width));
    }

    private static DataGridTextColumn CreateTextColumn(string header, string bindingPath, DataGridLength width)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new WpfBinding(bindingPath),
            Width = width,
            IsReadOnly = true
        };
    }

    private static DataGridTemplateColumn CreateHighlightedTextColumn(
        string header,
        string bindingPath,
        double width,
        string searchBindingPath)
    {
        return CreateHighlightedTextColumn(header, bindingPath, new DataGridLength(width), searchBindingPath);
    }

    private static DataGridTemplateColumn CreateHighlightedTextColumn(
        string header,
        string bindingPath,
        DataGridLength width,
        string searchBindingPath)
    {
        DataTemplate template = new();
        template.VisualTree = CreateHighlightedTextFactory(bindingPath, searchBindingPath);
        return new DataGridTemplateColumn
        {
            Header = header,
            CellTemplate = template,
            Width = width,
            IsReadOnly = true
        };
    }

    private static FrameworkElementFactory CreateHighlightedTextFactory(string bindingPath, string searchBindingPath)
    {
        FrameworkElementFactory text = new(typeof(SearchHighlightTextBlock));
        text.SetBinding(SearchHighlightTextBlock.HighlightTextProperty, new WpfBinding(bindingPath));
        text.SetBinding(SearchHighlightTextBlock.SearchTextProperty, new WpfBinding(searchBindingPath)
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(FamilyManagerWindow), 1)
        });
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 0, 4, 0));
        return text;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolder(ref BrowseInfo browseInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHGetPathFromIDList(IntPtr itemIdList, StringBuilder path);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr itemIdList);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BrowseInfo
    {
        public IntPtr HwndOwner;

        public IntPtr RootItemIdList;

        public IntPtr DisplayName;

        public string Title;

        public uint Flags;

        public IntPtr Callback;

        public IntPtr Parameter;

        public int Image;
    }
}
