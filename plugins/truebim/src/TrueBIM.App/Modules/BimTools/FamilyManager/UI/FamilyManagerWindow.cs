using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.FamilyManager.Models;
using TrueBIM.App.Modules.BimTools.FamilyManager.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using WpfBinding = System.Windows.Data.Binding;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.UI;

public sealed class FamilyManagerWindow : Window
{
    private readonly UIDocument uiDocument;
    private readonly Document document;
    private readonly FamilyManagerProfileStorage profileStorage;
    private readonly FamilyLibraryScanner scanner;
    private readonly FamilyLoadService loadService;
    private readonly ITrueBimLogger logger;
    private readonly ObservableCollection<FamilyLibraryFolder> folders = new();
    private readonly ObservableCollection<FamilyFileItem> visibleFamilies = new();
    private readonly ObservableCollection<FamilyLoadHistoryItem> historyItems = new();
    private readonly ObservableCollection<FamilyTypeInfo> familyTypes = new();
    private readonly List<FamilyFileItem> allFamilies = new();
    private readonly ListBox folderList = new();
    private readonly DataGrid familyGrid = new();
    private readonly ListBox historyList = new();
    private readonly ListBox typeList = new();
    private readonly WpfTextBox searchInput = new();
    private readonly WpfComboBox categoryInput = new();
    private readonly CheckBox favoritesOnlyInput = new()
    {
        Content = "Избранное"
    };
    private readonly TextBlock statusText = new();
    private readonly TextBlock detailsText = new();
    private readonly Button favoriteButton = CreateButton("В избранное", TrueBimIcon.Apply, 130);
    private readonly Button loadButton = CreateButton("Загрузить", TrueBimIcon.Apply, 130);
    private readonly Button loadAndPlaceButton = CreateButton("Загрузить и разместить", TrueBimIcon.FamilyManager, 190);
    private FamilyManagerProfile profile;
    private FamilyFileItem? selectedFamily;
    private bool isRefreshing;

    public FamilyManagerWindow(
        UIApplication uiApplication,
        UIDocument uiDocument,
        FamilyManagerProfileStorage profileStorage,
        FamilyLibraryScanner scanner,
        FamilyLoadService loadService,
        ITrueBimLogger logger)
    {
        _ = uiApplication ?? throw new ArgumentNullException(nameof(uiApplication));
        this.uiDocument = uiDocument ?? throw new ArgumentNullException(nameof(uiDocument));
        document = uiDocument.Document;
        this.profileStorage = profileStorage ?? throw new ArgumentNullException(nameof(profileStorage));
        this.scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        this.loadService = loadService ?? throw new ArgumentNullException(nameof(loadService));
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

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Button addButton = CreateButton("Добавить", TrueBimIcon.Open, 112);
        addButton.Click += (_, _) => AddFolder();
        actions.Children.Add(addButton);

        Button removeButton = CreateButton("Удалить", TrueBimIcon.Close, 112);
        removeButton.Click += (_, _) => RemoveSelectedFolder();
        actions.Children.Add(removeButton);
        DockPanel.SetDock(actions, Dock.Top);
        panel.Children.Add(actions);

        Button scanButton = CreateButton("Сканировать", TrueBimIcon.Preview, 236);
        scanButton.Margin = new Thickness(0, 0, 0, 8);
        scanButton.Click += (_, _) => ScanLibraries();
        DockPanel.SetDock(scanButton, Dock.Top);
        panel.Children.Add(scanButton);

        folderList.ItemsSource = folders;
        folderList.DisplayMemberPath = nameof(FamilyLibraryFolder.Path);
        folderList.BorderThickness = new Thickness(1);
        panel.Children.Add(folderList);
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
        searchInput.ToolTip = "Поиск по имени, категории или пути.";
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

        familyGrid.AutoGenerateColumns = false;
        familyGrid.CanUserAddRows = false;
        familyGrid.CanUserDeleteRows = false;
        familyGrid.IsReadOnly = true;
        familyGrid.SelectionMode = DataGridSelectionMode.Single;
        familyGrid.ItemsSource = visibleFamilies;
        familyGrid.SelectionChanged += (_, _) => SelectFamily(familyGrid.SelectedItem as FamilyFileItem);
        familyGrid.Columns.Add(CreateTextColumn("Имя", nameof(FamilyFileItem.Name), new DataGridLength(1, DataGridLengthUnitType.Star)));
        familyGrid.Columns.Add(CreateTextColumn("Категория", nameof(FamilyFileItem.Category), 120));
        familyGrid.Columns.Add(CreateTextColumn("Избр.", nameof(FamilyFileItem.FavoriteDisplay), 70));
        familyGrid.Columns.Add(CreateTextColumn("Размер", nameof(FamilyFileItem.SizeDisplay), 80));
        familyGrid.Columns.Add(CreateTextColumn("Статус", nameof(FamilyFileItem.Status), 150));
        panel.Children.Add(familyGrid);
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

        TextBlock typesTitle = CreateSubTitle("Типы в проекте");
        DockPanel.SetDock(typesTitle, Dock.Top);
        panel.Children.Add(typesTitle);

        typeList.ItemsSource = familyTypes;
        typeList.DisplayMemberPath = nameof(FamilyTypeInfo.Name);
        typeList.Height = 120;
        typeList.Margin = new Thickness(0, 0, 0, 12);
        DockPanel.SetDock(typeList, Dock.Top);
        panel.Children.Add(typeList);

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
        OpenFileDialog dialog = new()
        {
            Title = "Выберите папку библиотеки семейств",
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "Выберите папку"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string? folderPath = Path.GetDirectoryName(dialog.FileName);
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        folderPath = FamilyPathNormalizer.Normalize(folderPath);
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

    private void RemoveSelectedFolder()
    {
        if (folderList.SelectedItem is not FamilyLibraryFolder folder)
        {
            statusText.Text = "Выберите папку для удаления из списка.";
            return;
        }

        folders.Remove(folder);
        SaveProfile();
        ScanLibraries();
    }

    private void ScanLibraries()
    {
        SaveProfile();
        HashSet<string> favoritePaths = profile.FavoritePaths.ToHashSet(FamilyPathNormalizer.Comparer);
        Dictionary<string, DateTimeOffset> lastLoadedByPath = profile.History
            .GroupBy(item => FamilyPathNormalizer.Normalize(item.FilePath), FamilyPathNormalizer.Comparer)
            .ToDictionary(group => group.Key, group => group.Max(item => item.LoadedAtUtc), FamilyPathNormalizer.Comparer);

        FamilyLibraryScanResult result = scanner.Scan(profile.LibraryFolders, favoritePaths, lastLoadedByPath);
        allFamilies.Clear();
        foreach (FamilyFileItem family in result.Files)
        {
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
        statusText.Text = $"Просканировано папок: {result.ScannedFolderCount}. Не найдено папок: {result.MissingFolderCount}. Найдено семейств: {allFamilies.Count}.{warningText}";
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

        string search = searchInput.Text.Trim();
        string category = categoryInput.SelectedItem as string ?? FamilyManagerDefaults.AllCategories;
        bool favoritesOnly = favoritesOnlyInput.IsChecked == true;
        IEnumerable<FamilyFileItem> families = allFamilies;

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
            families = families.Where(family =>
                family.Name.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0
                || family.Category.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0
                || family.FilePath.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        visibleFamilies.Clear();
        foreach (FamilyFileItem family in families)
        {
            visibleFamilies.Add(family);
        }

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

    private void RefreshHistory()
    {
        historyItems.Clear();
        foreach (FamilyLoadHistoryItem item in profile.History.OrderByDescending(item => item.LoadedAtUtc).Take(40))
        {
            historyItems.Add(item);
        }
    }

    private void SelectFamily(FamilyFileItem? family)
    {
        selectedFamily = family;
        familyGrid.SelectedItem = family;
        familyTypes.Clear();

        if (family is null)
        {
            detailsText.Text = "Семейство не выбрано.";
            favoriteButton.IsEnabled = false;
            loadButton.IsEnabled = false;
            loadAndPlaceButton.IsEnabled = false;
            UpdateStatus();
            return;
        }

        favoriteButton.IsEnabled = true;
        loadButton.IsEnabled = true;
        loadAndPlaceButton.IsEnabled = true;
        favoriteButton.Content = IconFactory.CreateButtonContent(
            family.IsFavorite ? TrueBimIcon.Close : TrueBimIcon.Apply,
            family.IsFavorite ? "Убрать" : "В избранное");

        detailsText.Text =
            $"Имя: {family.Name}\n" +
            $"Категория: {family.Category}\n" +
            $"Файл: {family.FilePath}\n" +
            $"Размер: {family.SizeDisplay}\n" +
            $"Изменён: {family.LastWriteDisplay}";

        try
        {
            foreach (FamilyTypeInfo type in loadService.CollectLoadedTypes(document, family.Name))
            {
                familyTypes.Add(type);
            }
        }
        catch (Exception exception)
        {
            logger.Warning($"Failed to collect loaded family types for '{family.Name}': {exception.Message}");
        }

        UpdateStatus();
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

        bool overwrite = false;
        if (loadService.FamilyExists(document, selectedFamily.Name))
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
                return;
            }

            overwrite = decision == MessageBoxResult.Yes;
        }

        FamilyLoadResult result = loadService.Load(document, selectedFamily.FilePath, overwrite, logger);
        selectedFamily.Status = result.Message;
        statusText.Text = result.Message;

        if (!result.Succeeded)
        {
            Autodesk.Revit.UI.TaskDialog.Show("Диспетчер семейств", result.Message);
            return;
        }

        AddHistory(selectedFamily, placeAfterLoad ? "Загрузка и размещение" : "Загрузка");
        SelectFamily(selectedFamily);
        SaveProfile();

        if (placeAfterLoad)
        {
            RequestPlacement(selectedFamily);
        }

        Autodesk.Revit.UI.TaskDialog.Show("Диспетчер семейств", result.Message);
    }

    private void RequestPlacement(FamilyFileItem family)
    {
        try
        {
            string? selectedTypeName = typeList.SelectedItem is FamilyTypeInfo type
                ? type.Name
                : null;
            FamilySymbol? symbol = loadService.ResolveSymbol(document, family.Name, selectedTypeName);
            if (symbol is null)
            {
                statusText.Text = "Семейство загружено, но подходящий тип для размещения не найден.";
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
        statusText.Text = $"Папок: {folders.Count}. Семейств в кэше: {allFamilies.Count}. Показано: {visibleFamilies.Count}. Избранных: {favoritesCount}. {cacheText}";
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
}
