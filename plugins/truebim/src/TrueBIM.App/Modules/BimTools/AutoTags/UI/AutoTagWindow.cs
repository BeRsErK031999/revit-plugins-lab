using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using TrueBIM.App.Modules.BimTools.AutoTags.Models;
using TrueBIM.App.Modules.BimTools.AutoTags.Services;
using TrueBIM.App.Modules.BimTools.Common.Services.Export;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using RevitDocument = Autodesk.Revit.DB.Document;
using RevitView = Autodesk.Revit.DB.View;
using WpfBinding = System.Windows.Data.Binding;
using WpfGrid = System.Windows.Controls.Grid;

namespace TrueBIM.App.Modules.BimTools.AutoTags.UI;

public sealed class AutoTagWindow : Window
{
    private readonly RevitDocument document;
    private readonly RevitView activeView;
    private readonly AutoTagCollectorService collectorService;
    private readonly AutoTagProfileStorage profileStorage;
    private readonly AutoTagPlacementService placementService;
    private readonly ITrueBimLogger logger;
    private readonly CsvExportService csvExportService = new();
    private readonly AutoTagReportCsvService reportCsvService = new();
    private readonly ObservableCollection<AutoTagCategoryOption> categories = new();
    private readonly ObservableCollection<AutoTagElementRow> elementRows = new();
    private readonly ObservableCollection<AutoTagReportRow> reportRows = new();
    private readonly ObservableCollection<AutoTagTypeOption> tagTypes = new();
    private readonly TextBox profileNameInput = new();
    private readonly TextBox elementFilterInput = new();
    private readonly TextBox maxPreviewInput = new();
    private readonly TextBox offsetRightInput = new();
    private readonly TextBox offsetUpInput = new();
    private readonly CheckBox onlyUntaggedInput = new()
    {
        Content = "Только без марки",
        IsChecked = true,
        ToolTip = "Пропускать элементы, у которых на активном виде уже есть марка."
    };
    private readonly CheckBox leaderInput = new()
    {
        Content = "С выноской",
        ToolTip = "Создавать марки с leader. По умолчанию выключено для аккуратного MVP."
    };
    private readonly ComboBox tagTypeInput = new();
    private readonly DataGrid categoryGrid = new();
    private readonly DataGrid elementGrid = new();
    private readonly DataGrid reportGrid = new();
    private readonly TextBlock statusText = new();
    private readonly Button applyButton = CreateButton("Поставить марки", TrueBimIcon.AutoTags, 150);
    private readonly Button exportReportButton = CreateButton("Отчёт CSV", TrueBimIcon.Export, 120);

    public AutoTagWindow(
        RevitDocument document,
        RevitView activeView,
        AutoTagCollectorService collectorService,
        AutoTagProfileStorage profileStorage,
        AutoTagPlacementService placementService,
        ITrueBimLogger logger)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        this.activeView = activeView ?? throw new ArgumentNullException(nameof(activeView));
        this.collectorService = collectorService ?? throw new ArgumentNullException(nameof(collectorService));
        this.profileStorage = profileStorage ?? throw new ArgumentNullException(nameof(profileStorage));
        this.placementService = placementService ?? throw new ArgumentNullException(nameof(placementService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        LoadInitialData();

        Title = "Автомарки";
        Icon = IconFactory.CreateImage(TrueBimIcon.AutoTags, 32);
        Width = 1180;
        Height = 760;
        MinWidth = 1040;
        MinHeight = 640;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();

        RefreshVisibleRows();
        UpdateStatus();
        logger.Info($"Auto Tags window opened for '{document.Title}' and view '{activeView.Name}'.");
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveProfile();
        base.OnClosed(e);
    }

    private void LoadInitialData()
    {
        AutoTagProfile profile = profileStorage.Load();
        profileNameInput.Text = profile.Name;
        onlyUntaggedInput.IsChecked = profile.OnlyUntagged;
        leaderInput.IsChecked = profile.UseLeader;
        offsetRightInput.Text = FormatMillimeters(profile.OffsetRightMm);
        offsetUpInput.Text = FormatMillimeters(profile.OffsetUpMm);
        maxPreviewInput.Text = profile.MaxPreviewCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

        foreach (AutoTagCategoryOption category in collectorService.CollectCategories(document, activeView))
        {
            category.PropertyChanged += OnCategoryPropertyChanged;
            categories.Add(category);
        }

        if (profile.SelectedCategoryIds.Count > 0)
        {
            HashSet<long> selectedCategoryIds = profile.SelectedCategoryIds.ToHashSet();
            foreach (AutoTagCategoryOption category in categories)
            {
                category.IsSelected = selectedCategoryIds.Contains(category.CategoryIdValue);
            }
        }

        foreach (AutoTagTypeOption tagType in collectorService.CollectTagTypes(document))
        {
            tagTypes.Add(tagType);
        }

        tagTypeInput.ItemsSource = tagTypes;
        tagTypeInput.DisplayMemberPath = nameof(AutoTagTypeOption.DisplayName);
        tagTypeInput.SelectedItem = tagTypes.FirstOrDefault(tagType => tagType.ElementId == profile.SelectedTagTypeId)
            ?? tagTypes.FirstOrDefault()
            ?? AutoTagTypeOption.Automatic;
    }

    private UIElement CreateContent()
    {
        DockPanel root = new()
        {
            Margin = new Thickness(18)
        };

        UIElement top = CreateTopPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        statusText.Margin = new Thickness(0, 10, 0, 0);
        statusText.TextWrapping = TextWrapping.Wrap;
        DockPanel.SetDock(statusText, Dock.Bottom);
        root.Children.Add(statusText);

        TabControl tabs = new();
        tabs.Items.Add(new TabItem
        {
            Header = "Категории",
            Content = CreateCategoryPanel()
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Элементы",
            Content = CreateElementPanel()
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Отчёт",
            Content = CreateReportGrid()
        });
        root.Children.Add(tabs);
        return root;
    }

    private UIElement CreateTopPanel()
    {
        WpfGrid root = new()
        {
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddLabel(root, $"Вид: {activeView.Name}", 0, 0);

        profileNameInput.Height = 32;
        profileNameInput.Margin = new Thickness(8, 0, 12, 8);
        profileNameInput.ToolTip = "Имя локального профиля автомарок.";
        WpfGrid.SetColumn(profileNameInput, 1);
        root.Children.Add(profileNameInput);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Button previewButton = CreateButton("Предпросмотр", TrueBimIcon.Preview, 140);
        previewButton.Click += (_, _) => Preview();
        actions.Children.Add(previewButton);

        applyButton.Click += (_, _) => Apply();
        actions.Children.Add(applyButton);

        exportReportButton.IsEnabled = false;
        exportReportButton.Click += (_, _) => ExportReport();
        actions.Children.Add(exportReportButton);

        Button closeButton = CreateButton("Закрыть", TrueBimIcon.Close, 110);
        closeButton.IsCancel = true;
        closeButton.Click += (_, _) => Close();
        actions.Children.Add(closeButton);

        WpfGrid.SetColumn(actions, 2);
        root.Children.Add(actions);

        AddLabel(root, "Тип марки", 0, 1);
        tagTypeInput.Height = 32;
        tagTypeInput.MinWidth = 300;
        tagTypeInput.Margin = new Thickness(8, 0, 12, 0);
        WpfGrid.SetRow(tagTypeInput, 1);
        WpfGrid.SetColumn(tagTypeInput, 1);
        root.Children.Add(tagTypeInput);

        StackPanel options = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        onlyUntaggedInput.Margin = new Thickness(0, 0, 14, 0);
        onlyUntaggedInput.Checked += (_, _) => UpdateStatus();
        onlyUntaggedInput.Unchecked += (_, _) => UpdateStatus();
        options.Children.Add(onlyUntaggedInput);
        leaderInput.Margin = new Thickness(0, 0, 14, 0);
        options.Children.Add(leaderInput);

        TextBlock offsetRightLabel = new()
        {
            Text = "X, мм",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        options.Children.Add(offsetRightLabel);
        offsetRightInput.Width = 70;
        offsetRightInput.Height = 32;
        offsetRightInput.Margin = new Thickness(0, 0, 10, 0);
        offsetRightInput.ToolTip = "Смещение марки вправо по активному виду, мм. Отрицательное значение смещает влево.";
        offsetRightInput.TextChanged += (_, _) => UpdateStatus();
        options.Children.Add(offsetRightInput);

        TextBlock offsetUpLabel = new()
        {
            Text = "Y, мм",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        options.Children.Add(offsetUpLabel);
        offsetUpInput.Width = 70;
        offsetUpInput.Height = 32;
        offsetUpInput.Margin = new Thickness(0, 0, 14, 0);
        offsetUpInput.ToolTip = "Смещение марки вверх по активному виду, мм. Отрицательное значение смещает вниз.";
        offsetUpInput.TextChanged += (_, _) => UpdateStatus();
        options.Children.Add(offsetUpInput);

        TextBlock limitLabel = new()
        {
            Text = "Лимит",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        options.Children.Add(limitLabel);
        maxPreviewInput.Width = 70;
        maxPreviewInput.Height = 32;
        maxPreviewInput.ToolTip = "Максимальное число строк preview, от 50 до 5000.";
        options.Children.Add(maxPreviewInput);

        WpfGrid.SetRow(options, 1);
        WpfGrid.SetColumn(options, 2);
        root.Children.Add(options);

        return root;
    }

    private UIElement CreateCategoryPanel()
    {
        DockPanel panel = new();

        StackPanel toolbar = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Button selectAllButton = CreateButton("Выбрать все", TrueBimIcon.Apply, 130);
        selectAllButton.Click += (_, _) => SetCategoriesSelected(true);
        toolbar.Children.Add(selectAllButton);

        Button clearButton = CreateButton("Снять выбор", TrueBimIcon.Close, 130);
        clearButton.Click += (_, _) => SetCategoriesSelected(false);
        toolbar.Children.Add(clearButton);

        Button refreshButton = CreateButton("Обновить", TrueBimIcon.Preview, 120);
        refreshButton.Click += (_, _) => RefreshCategories();
        toolbar.Children.Add(refreshButton);
        DockPanel.SetDock(toolbar, Dock.Top);
        panel.Children.Add(toolbar);

        categoryGrid.AutoGenerateColumns = false;
        categoryGrid.CanUserAddRows = false;
        categoryGrid.CanUserDeleteRows = false;
        categoryGrid.IsReadOnly = false;
        categoryGrid.ItemsSource = categories;
        categoryGrid.Columns.Add(CreateSelectionColumn(nameof(AutoTagCategoryOption.IsSelected), "Вкл."));
        categoryGrid.Columns.Add(CreateTextColumn("Категория", nameof(AutoTagCategoryOption.Name), new DataGridLength(1, DataGridLengthUnitType.Star)));
        categoryGrid.Columns.Add(CreateTextColumn("Элементов", nameof(AutoTagCategoryOption.ElementCount), 120));
        panel.Children.Add(categoryGrid);
        return panel;
    }

    private UIElement CreateElementPanel()
    {
        DockPanel panel = new();

        DockPanel filterBar = new()
        {
            LastChildFill = true,
            Margin = new Thickness(0, 0, 0, 8)
        };
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal
        };
        Button selectAllButton = CreateButton("Выбрать все", TrueBimIcon.Apply, 130);
        selectAllButton.Click += (_, _) => SetVisibleElementsSelected(true);
        actions.Children.Add(selectAllButton);

        Button clearButton = CreateButton("Снять выбор", TrueBimIcon.Close, 130);
        clearButton.Click += (_, _) => SetVisibleElementsSelected(false);
        actions.Children.Add(clearButton);
        DockPanel.SetDock(actions, Dock.Right);
        filterBar.Children.Add(actions);

        elementFilterInput.Height = 32;
        elementFilterInput.Margin = new Thickness(0, 0, 8, 0);
        elementFilterInput.ToolTip = "Фильтр по ElementId, имени или категории.";
        elementFilterInput.TextChanged += (_, _) => RefreshVisibleRows();
        filterBar.Children.Add(elementFilterInput);
        DockPanel.SetDock(filterBar, Dock.Top);
        panel.Children.Add(filterBar);

        elementGrid.AutoGenerateColumns = false;
        elementGrid.CanUserAddRows = false;
        elementGrid.CanUserDeleteRows = false;
        elementGrid.IsReadOnly = false;
        elementGrid.ItemsSource = elementRows;
        elementGrid.Columns.Add(CreateSelectionColumn(nameof(AutoTagElementRow.IsSelected)));
        elementGrid.Columns.Add(CreateTextColumn("ElementId", nameof(AutoTagElementRow.ElementId), 90));
        elementGrid.Columns.Add(CreateTextColumn("Категория", nameof(AutoTagElementRow.CategoryName), 160));
        elementGrid.Columns.Add(CreateTextColumn("Имя", nameof(AutoTagElementRow.ElementName), new DataGridLength(1, DataGridLengthUnitType.Star)));
        elementGrid.Columns.Add(CreateTextColumn("Марок", nameof(AutoTagElementRow.ExistingTagCount), 80));
        elementGrid.Columns.Add(CreateTextColumn("Статус", nameof(AutoTagElementRow.Status), 110));
        elementGrid.Columns.Add(CreateTextColumn("Сообщение", nameof(AutoTagElementRow.Message), new DataGridLength(260)));
        panel.Children.Add(elementGrid);
        return panel;
    }

    private DataGrid CreateReportGrid()
    {
        reportGrid.AutoGenerateColumns = false;
        reportGrid.CanUserAddRows = false;
        reportGrid.CanUserDeleteRows = false;
        reportGrid.IsReadOnly = true;
        reportGrid.ItemsSource = reportRows;
        reportGrid.Columns.Add(CreateTextColumn("Этап", nameof(AutoTagReportRow.Phase), 110));
        reportGrid.Columns.Add(CreateTextColumn("ElementId", nameof(AutoTagReportRow.ElementId), 90));
        reportGrid.Columns.Add(CreateTextColumn("Категория", nameof(AutoTagReportRow.CategoryName), 150));
        reportGrid.Columns.Add(CreateTextColumn("Имя", nameof(AutoTagReportRow.ElementName), new DataGridLength(1, DataGridLengthUnitType.Star)));
        reportGrid.Columns.Add(CreateTextColumn("Тип марки", nameof(AutoTagReportRow.TagTypeName), 210));
        reportGrid.Columns.Add(CreateTextColumn("Статус", nameof(AutoTagReportRow.Status), 100));
        reportGrid.Columns.Add(CreateTextColumn("Сообщение", nameof(AutoTagReportRow.Message), 260));
        return reportGrid;
    }

    private void RefreshCategories()
    {
        HashSet<long> selectedCategoryIds = categories
            .Where(category => category.IsSelected)
            .Select(category => category.CategoryIdValue)
            .ToHashSet();
        categories.Clear();

        foreach (AutoTagCategoryOption category in collectorService.CollectCategories(document, activeView))
        {
            category.IsSelected = selectedCategoryIds.Count == 0 || selectedCategoryIds.Contains(category.CategoryIdValue);
            category.PropertyChanged += OnCategoryPropertyChanged;
            categories.Add(category);
        }

        elementRows.Clear();
        reportRows.Clear();
        exportReportButton.IsEnabled = false;
        UpdateStatus("Категории обновлены.");
    }

    private void Preview()
    {
        SaveProfile();
        elementRows.Clear();
        reportRows.Clear();

        if (categories.Count(category => category.IsSelected) == 0)
        {
            Autodesk.Revit.UI.TaskDialog.Show("Автомарки", "Выберите хотя бы одну категорию.");
            UpdateStatus();
            return;
        }

        AutoTagExistingTagIndex existingTagIndex = AutoTagExistingTagIndex.Create(document, activeView, logger);
        IReadOnlyList<AutoTagElementRow> rows = collectorService.CollectElements(
            document,
            activeView,
            categories.ToList(),
            existingTagIndex,
            onlyUntaggedInput.IsChecked == true,
            GetMaxPreviewCount());

        foreach (AutoTagElementRow row in rows)
        {
            row.PropertyChanged += OnElementRowPropertyChanged;
            elementRows.Add(row);
            reportRows.Add(new AutoTagReportRow(
                "Предпросмотр",
                activeView.Name,
                row.ElementId,
                row.CategoryName,
                row.ElementName,
                GetSelectedTagType().DisplayName,
                row.Status,
                AppendOffsetText(row.Message)));
        }

        exportReportButton.IsEnabled = reportRows.Count > 0;
        UpdateStatus($"Предпросмотр: {rows.Count} элементов.");
    }

    private void Apply()
    {
        SaveProfile();
        if (elementRows.Count == 0)
        {
            Preview();
        }

        List<AutoTagElementRow> selectedRows = elementRows
            .Where(row => row.IsSelected && row.CanApply)
            .ToList();
        if (selectedRows.Count == 0)
        {
            Autodesk.Revit.UI.TaskDialog.Show("Автомарки", "Нет выбранных элементов, готовых к постановке марки.");
            return;
        }

        MessageBoxResult decision = MessageBox.Show(
            this,
            $"Создать марки на активном виде для выбранных элементов: {selectedRows.Count}?",
            "Автомарки",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        AutoTagTypeOption tagType = GetSelectedTagType();
        AutoTagApplyResult result = placementService.Apply(
            document,
            activeView,
            selectedRows,
            tagType,
            onlyUntaggedInput.IsChecked == true,
            leaderInput.IsChecked == true,
            GetOffsetRightMm(),
            GetOffsetUpMm(),
            logger);

        Dictionary<long, AutoTagReportRow> resultRowsByElementId = result.Rows
            .GroupBy(row => row.ElementId)
            .ToDictionary(group => group.Key, group => group.Last());
        foreach (AutoTagElementRow row in elementRows)
        {
            if (resultRowsByElementId.TryGetValue(row.ElementId, out AutoTagReportRow? resultRow))
            {
                row.ApplyResult(resultRow);
            }
        }

        reportRows.Clear();
        foreach (AutoTagReportRow row in result.Rows)
        {
            reportRows.Add(row);
        }

        exportReportButton.IsEnabled = reportRows.Count > 0;
        Autodesk.Revit.UI.TaskDialog.Show("Автомарки", result.ToDialogText());
        UpdateStatus($"Создано: {result.CreatedCount}. Пропущено: {result.SkippedCount}. Ошибок: {result.FailedCount}.");
    }

    private void ExportReport()
    {
        if (reportRows.Count == 0)
        {
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "Сохранить отчёт автомарок",
            Filter = "CSV UTF-8 (*.csv)|*.csv",
            FileName = "auto-tags-report.csv",
            InitialDirectory = Directory.Exists(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : null
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        csvExportService.WriteUtf8WithBom(dialog.FileName, reportCsvService.Format(reportRows.ToList()));
        UpdateStatus($"Отчёт сохранён: {dialog.FileName}");
    }

    private void SaveProfile()
    {
        AutoTagTypeOption selectedTagType = GetSelectedTagType();
        profileStorage.Save(new AutoTagProfile
        {
            Name = profileNameInput.Text,
            OnlyUntagged = onlyUntaggedInput.IsChecked == true,
            UseLeader = leaderInput.IsChecked == true,
            OffsetRightMm = GetOffsetRightMm(),
            OffsetUpMm = GetOffsetUpMm(),
            MaxPreviewCount = GetMaxPreviewCount(),
            SelectedTagTypeId = selectedTagType.IsAutomatic ? null : selectedTagType.ElementId,
            SelectedCategoryIds = categories
                .Where(category => category.IsSelected)
                .Select(category => category.CategoryIdValue)
                .ToList()
        });
    }

    private AutoTagTypeOption GetSelectedTagType()
    {
        return tagTypeInput.SelectedItem as AutoTagTypeOption
            ?? tagTypes.FirstOrDefault()
            ?? AutoTagTypeOption.Automatic;
    }

    private int GetMaxPreviewCount()
    {
        return int.TryParse(maxPreviewInput.Text.Trim(), out int value)
            ? Clamp(value, 50, 5000)
            : 500;
    }

    private double GetOffsetRightMm()
    {
        return GetOffsetMillimeters(offsetRightInput.Text);
    }

    private double GetOffsetUpMm()
    {
        return GetOffsetMillimeters(offsetUpInput.Text);
    }

    private static double GetOffsetMillimeters(string text)
    {
        return double.TryParse(
            text.Trim(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.CurrentCulture,
            out double value)
            || double.TryParse(
                text.Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value)
            ? AutoTagPlacementOffset.NormalizeMillimeters(value)
            : 0.0;
    }

    private void SetCategoriesSelected(bool isSelected)
    {
        foreach (AutoTagCategoryOption category in categories)
        {
            category.IsSelected = isSelected;
        }

        UpdateStatus();
    }

    private void SetVisibleElementsSelected(bool isSelected)
    {
        foreach (AutoTagElementRow row in GetFilteredRows().Where(row => row.CanApply))
        {
            row.IsSelected = isSelected;
        }

        UpdateStatus();
    }

    private void RefreshVisibleRows()
    {
        if (elementRows.Count == 0)
        {
            UpdateStatus();
            return;
        }

        ICollectionView view = CollectionViewSource.GetDefaultView(elementRows);
        view.Filter = row => row is AutoTagElementRow elementRow && IsRowVisible(elementRow);
        view.Refresh();
        UpdateStatus();
    }

    private IEnumerable<AutoTagElementRow> GetFilteredRows()
    {
        return elementRows.Where(IsRowVisible);
    }

    private bool IsRowVisible(AutoTagElementRow row)
    {
        string filter = elementFilterInput.Text.Trim();
        return string.IsNullOrWhiteSpace(filter)
            || row.ElementId.ToString(System.Globalization.CultureInfo.InvariantCulture).IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
            || row.CategoryName.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
            || row.ElementName.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    private void OnCategoryPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AutoTagCategoryOption.IsSelected))
        {
            UpdateStatus();
        }
    }

    private void OnElementRowPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(AutoTagElementRow.IsSelected) or nameof(AutoTagElementRow.Status))
        {
            UpdateStatus();
        }
    }

    private void UpdateStatus(string? prefix = null)
    {
        int selectedCategories = categories.Count(category => category.IsSelected);
        int readyRows = elementRows.Count(row => row.CanApply);
        int selectedRows = elementRows.Count(row => row.IsSelected && row.CanApply);
        string tagTypeText = GetSelectedTagType().DisplayName;
        string text = $"Категорий: {categories.Count}. Выбрано категорий: {selectedCategories}. Элементов в preview: {elementRows.Count}. Готово: {readyRows}. Выбрано: {selectedRows}. Тип: {tagTypeText}.";
        string offsetText = AutoTagPlacementOffset.FormatForReport(GetOffsetRightMm(), GetOffsetUpMm());
        if (!string.IsNullOrWhiteSpace(offsetText))
        {
            text += $" {offsetText}";
        }

        statusText.Text = string.IsNullOrWhiteSpace(prefix) ? text : $"{prefix} {text}";
        applyButton.IsEnabled = selectedRows > 0 || elementRows.Count == 0;
    }

    private string AppendOffsetText(string message)
    {
        string offsetText = AutoTagPlacementOffset.FormatForReport(GetOffsetRightMm(), GetOffsetUpMm());
        return string.IsNullOrWhiteSpace(offsetText) ? message : $"{message} {offsetText}";
    }

    private static string FormatMillimeters(double value)
    {
        return AutoTagPlacementOffset.NormalizeMillimeters(value)
            .ToString("0.#", System.Globalization.CultureInfo.CurrentCulture);
    }

    private static void AddLabel(WpfGrid grid, string text, int column, int row)
    {
        TextBlock label = new()
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 8)
        };
        WpfGrid.SetColumn(label, column);
        WpfGrid.SetRow(label, row);
        grid.Children.Add(label);
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

    private static DataGridTemplateColumn CreateSelectionColumn(string bindingPath, string header = "Выбран")
    {
        FrameworkElementFactory checkBox = new(typeof(CheckBox));
        checkBox.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        checkBox.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        checkBox.SetBinding(
            CheckBox.IsCheckedProperty,
            new WpfBinding(bindingPath)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });

        return new DataGridTemplateColumn
        {
            Header = header,
            CellTemplate = new DataTemplate
            {
                VisualTree = checkBox
            },
            Width = 78
        };
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }
}
