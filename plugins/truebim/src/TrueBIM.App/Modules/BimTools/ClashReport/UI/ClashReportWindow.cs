using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Autodesk.Revit.DB;
using Microsoft.Win32;
using TrueBIM.App.Modules.BimTools.ClashReport.Models;
using TrueBIM.App.Modules.BimTools.ClashReport.Services;
using TrueBIM.App.Modules.BimTools.Common.Services.Export;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using RevitDocument = Autodesk.Revit.DB.Document;
using RevitUIDocument = Autodesk.Revit.UI.UIDocument;
using WpfBinding = System.Windows.Data.Binding;
using WpfGrid = System.Windows.Controls.Grid;

namespace TrueBIM.App.Modules.BimTools.ClashReport.UI;

public sealed class ClashReportWindow : Window
{
    private static readonly IReadOnlyList<ClashStatus> StatusOptions =
        Enum.GetValues(typeof(ClashStatus)).Cast<ClashStatus>().ToList();

    private readonly RevitUIDocument uiDocument;
    private readonly RevitDocument document;
    private readonly ClashLinkScanner linkScanner;
    private readonly ClashElementResolver elementResolver;
    private readonly ClashViewNavigator viewNavigator;
    private readonly ClashReportStorage storage;
    private readonly ITrueBimLogger logger;
    private readonly CsvExportService csvExportService = new();
    private readonly ClashReportCsvService reportCsvService = new();
    private readonly ObservableCollection<ClashItem> clashRows = new();
    private readonly ObservableCollection<ClashReportRow> reportRows = new();
    private readonly TextBox profileNameInput = new();
    private readonly TextBox filterInput = new();
    private readonly TextBox paddingInput = new();
    private readonly TextBox minimumOverlapInput = new();
    private readonly CheckBox scanCurrentModelInput = new()
    {
        Content = "Текущая модель",
        IsChecked = true,
        ToolTip = "Искать пересечения между видимыми элементами активного вида текущего файла."
    };
    private readonly CheckBox scanRvtLinksInput = new()
    {
        Content = "Модель ↔ RVT-связи",
        IsChecked = true,
        ToolTip = "Искать пересечения между текущей моделью и загруженными RVT-связями."
    };
    private readonly CheckBox scanLinksAgainstEachOtherInput = new()
    {
        Content = "RVT-связи между собой",
        ToolTip = "Дополнительно сравнить загруженные RVT-связи друг с другом."
    };
    private readonly CheckBox highlightInput = new()
    {
        Content = "Подсветка в 3D",
        IsChecked = true,
        ToolTip = "При переходе в 3D подсвечивать найденные элементы коллизии служебными overrides."
    };
    private readonly DataGrid clashGrid = new();
    private readonly DataGrid reportGrid = new();
    private readonly TextBlock statusText = new();
    private readonly Button selectButton = CreateButton("Выбрать", TrueBimIcon.Apply, 105);
    private readonly Button navigateButton = CreateButton("Показать в 3D", TrueBimIcon.ClashReport, 140);
    private readonly Button saveStateButton = CreateButton("Сохранить", TrueBimIcon.Apply, 110);
    private readonly Button exportReportButton = CreateButton("Отчёт CSV", TrueBimIcon.Export, 120);
    private readonly string modelKey;

    public ClashReportWindow(
        RevitUIDocument uiDocument,
        ClashLinkScanner linkScanner,
        ClashElementResolver elementResolver,
        ClashViewNavigator viewNavigator,
        ClashReportStorage storage,
        ITrueBimLogger logger)
    {
        this.uiDocument = uiDocument ?? throw new ArgumentNullException(nameof(uiDocument));
        document = uiDocument.Document;
        this.linkScanner = linkScanner ?? throw new ArgumentNullException(nameof(linkScanner));
        this.elementResolver = elementResolver ?? throw new ArgumentNullException(nameof(elementResolver));
        this.viewNavigator = viewNavigator ?? throw new ArgumentNullException(nameof(viewNavigator));
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        modelKey = ClashReportStorage.BuildModelKey(document);

        ApplyProfile(storage.LoadProfile());

        Title = "Отчёт коллизий";
        Icon = IconFactory.CreateImage(TrueBimIcon.ClashReport, 32);
        Width = 1220;
        Height = 760;
        MinWidth = 1040;
        MinHeight = 640;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();

        UpdateStatus("Нажмите «Сканировать», чтобы проверить текущую модель и загруженные RVT-связи.");
        logger.Info($"Clash Report window opened for '{document.Title}'.");
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveState(showDialog: false);
        base.OnClosed(e);
    }

    private void ApplyProfile(ClashReportProfile profile)
    {
        profileNameInput.Text = profile.Name;
        paddingInput.Text = profile.SectionBoxPaddingMm.ToString("0.##", CultureInfo.InvariantCulture);
        minimumOverlapInput.Text = profile.MinimumOverlapMm.ToString("0.##", CultureInfo.InvariantCulture);
        scanCurrentModelInput.IsChecked = profile.ScanCurrentModel;
        scanRvtLinksInput.IsChecked = profile.ScanRvtLinks;
        scanLinksAgainstEachOtherInput.IsChecked = profile.ScanLinksAgainstEachOther;
        highlightInput.IsChecked = profile.HighlightOnNavigate;
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
            Header = "Коллизии",
            Content = CreateClashPanel()
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
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddLabel(root, $"Модель: {document.Title}", 0, 0);
        profileNameInput.Height = 32;
        profileNameInput.Margin = new Thickness(8, 0, 12, 8);
        profileNameInput.ToolTip = "Имя локального профиля отчёта коллизий.";
        WpfGrid.SetColumn(profileNameInput, 1);
        root.Children.Add(profileNameInput);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Button scanButton = CreateButton("Сканировать", TrueBimIcon.Preview, 135);
        scanButton.Click += (_, _) => ScanLinks();
        actions.Children.Add(scanButton);

        Button refreshButton = CreateButton("Проверить", TrueBimIcon.Preview, 115);
        refreshButton.Click += (_, _) => ResolveRows();
        actions.Children.Add(refreshButton);

        selectButton.Click += (_, _) => SelectSelectedClashElements(showDialogWhenMissing: true);
        actions.Children.Add(selectButton);

        navigateButton.Click += (_, _) => NavigateToSelectedClash();
        actions.Children.Add(navigateButton);

        saveStateButton.Click += (_, _) => SaveState(showDialog: true);
        actions.Children.Add(saveStateButton);

        exportReportButton.Click += (_, _) => ExportReport();
        actions.Children.Add(exportReportButton);

        Button closeButton = CreateButton("Закрыть", TrueBimIcon.Close, 110);
        closeButton.IsCancel = true;
        closeButton.Click += (_, _) => Close();
        actions.Children.Add(closeButton);

        WpfGrid.SetColumn(actions, 2);
        root.Children.Add(actions);

        AddLabel(root, "Источник", 0, 1);
        TextBlock sourceText = new()
        {
            Text = "Текущая модель активного вида и загруженные RVT-связи. CSV используется только для экспорта отчёта.",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 12, 8),
            Foreground = System.Windows.Media.Brushes.DimGray
        };
        WpfGrid.SetRow(sourceText, 1);
        WpfGrid.SetColumn(sourceText, 1);
        root.Children.Add(sourceText);

        AddLabel(root, "Проверять", 0, 2);
        StackPanel modes = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        modes.Children.Add(scanCurrentModelInput);
        modes.Children.Add(scanRvtLinksInput);
        modes.Children.Add(scanLinksAgainstEachOtherInput);
        foreach (UIElement child in modes.Children)
        {
            if (child is CheckBox checkBox)
            {
                checkBox.Margin = new Thickness(8, 0, 18, 8);
                checkBox.VerticalAlignment = VerticalAlignment.Center;
            }
        }

        WpfGrid.SetRow(modes, 2);
        WpfGrid.SetColumn(modes, 1);
        root.Children.Add(modes);

        AddLabel(root, "Параметры", 0, 3);
        StackPanel options = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        options.Children.Add(CreateOptionLabel("Запас section box, мм"));
        paddingInput.Width = 90;
        paddingInput.Height = 32;
        paddingInput.Margin = new Thickness(8, 0, 18, 8);
        paddingInput.ToolTip = "Запас вокруг bounding box найденного пересечения.";
        options.Children.Add(paddingInput);
        options.Children.Add(CreateOptionLabel("Мин. пересечение, мм"));
        minimumOverlapInput.Width = 90;
        minimumOverlapInput.Height = 32;
        minimumOverlapInput.Margin = new Thickness(8, 0, 18, 8);
        minimumOverlapInput.ToolTip = "Минимальный размер пересечения по X/Y/Z. 0 показывает все пересекающиеся bounding box.";
        options.Children.Add(minimumOverlapInput);
        options.Children.Add(highlightInput);

        WpfGrid.SetRow(options, 3);
        WpfGrid.SetColumn(options, 1);
        root.Children.Add(options);

        return root;
    }

    private UIElement CreateClashPanel()
    {
        DockPanel panel = new();

        StackPanel filterPanel = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        filterPanel.Children.Add(CreateOptionLabel("Фильтр"));
        filterInput.Width = 360;
        filterInput.Height = 30;
        filterInput.Margin = new Thickness(8, 0, 12, 0);
        filterInput.ToolTip = "Фильтр по имени, ElementId, статусу, сообщению или комментарию.";
        filterInput.TextChanged += (_, _) => RefreshFilter();
        filterPanel.Children.Add(filterInput);

        DockPanel.SetDock(filterPanel, Dock.Top);
        panel.Children.Add(filterPanel);

        panel.Children.Add(CreateClashGrid());
        return panel;
    }

    private DataGrid CreateClashGrid()
    {
        clashGrid.AutoGenerateColumns = false;
        clashGrid.CanUserAddRows = false;
        clashGrid.CanUserDeleteRows = false;
        clashGrid.IsReadOnly = false;
        clashGrid.SelectionMode = DataGridSelectionMode.Single;
        clashGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
        clashGrid.ItemsSource = clashRows;
        clashGrid.Columns.Add(CreateTextColumn("Источник", nameof(ClashItem.Source), 145));
        clashGrid.Columns.Add(CreateTextColumn("ID", nameof(ClashItem.ClashId), 110));
        clashGrid.Columns.Add(CreateTextColumn("Имя", nameof(ClashItem.Name), new DataGridLength(1, DataGridLengthUnitType.Star)));
        clashGrid.Columns.Add(CreateTextColumn("ElementId 1", nameof(ClashItem.ElementId1Text), 105));
        clashGrid.Columns.Add(CreateTextColumn("Element 1", nameof(ClashItem.Element1Name), 170));
        clashGrid.Columns.Add(CreateTextColumn("ElementId 2", nameof(ClashItem.ElementId2Text), 105));
        clashGrid.Columns.Add(CreateTextColumn("Element 2", nameof(ClashItem.Element2Name), 170));
        clashGrid.Columns.Add(CreateTextColumn("Точка", nameof(ClashItem.PointText), 150));
        clashGrid.Columns.Add(CreateStatusColumn());
        clashGrid.Columns.Add(CreateEditableTextColumn("Комментарий", nameof(ClashItem.Comment), 220));
        clashGrid.Columns.Add(CreateTextColumn("Resolve", nameof(ClashItem.ResolvedDisplay), 85));
        clashGrid.Columns.Add(CreateTextColumn("Сообщение", nameof(ClashItem.Message), 220));
        clashGrid.MouseDoubleClick += (_, _) => SelectSelectedClashElements(showDialogWhenMissing: true);

        ICollectionView view = CollectionViewSource.GetDefaultView(clashGrid.ItemsSource);
        view.Filter = MatchesFilter;

        return clashGrid;
    }

    private DataGrid CreateReportGrid()
    {
        reportGrid.AutoGenerateColumns = false;
        reportGrid.CanUserAddRows = false;
        reportGrid.CanUserDeleteRows = false;
        reportGrid.IsReadOnly = true;
        reportGrid.ItemsSource = reportRows;
        reportGrid.Columns.Add(CreateTextColumn("Операция", nameof(ClashReportRow.Operation), 120));
        reportGrid.Columns.Add(CreateTextColumn("ID", nameof(ClashReportRow.ClashId), 110));
        reportGrid.Columns.Add(CreateTextColumn("Имя", nameof(ClashReportRow.ClashName), new DataGridLength(1, DataGridLengthUnitType.Star)));
        reportGrid.Columns.Add(CreateTextColumn("Статус", nameof(ClashReportRow.Status), 120));
        reportGrid.Columns.Add(CreateTextColumn("Сообщение", nameof(ClashReportRow.Message), new DataGridLength(2, DataGridLengthUnitType.Star)));
        return reportGrid;
    }

    private void ScanLinks()
    {
        ClashReportProfile profile = ReadProfile();
        ClashLinkScanResult result = linkScanner.Scan(document, uiDocument.ActiveView, CreateScanOptions(profile));
        clashRows.Clear();
        reportRows.Clear();

        foreach (ClashItem item in result.Items)
        {
            storage.ApplyState(modelKey, item);
            elementResolver.Resolve(document, item);
            clashRows.Add(item);
        }

        foreach (string message in result.Messages)
        {
            reportRows.Add(new ClashReportRow("Сканирование", string.Empty, string.Empty, "Info", message));
        }

        storage.SaveProfile(profile);
        RefreshFilter();
        UpdateStatus($"Сканирование: найдено {result.Items.Count} коллизий.");
        logger.Info($"Clash Report scan found {result.Items.Count} rows with {result.Messages.Count} messages.");
    }

    private void ResolveRows()
    {
        CommitGridEdits();
        foreach (ClashItem item in clashRows)
        {
            elementResolver.Resolve(document, item);
        }

        AddReport("Проверка", string.Empty, string.Empty, "OK", $"Обновлено строк: {clashRows.Count}.");
        UpdateStatus();
    }

    private bool SelectSelectedClashElements(bool showDialogWhenMissing)
    {
        CommitGridEdits();
        if (clashGrid.SelectedItem is not ClashItem item)
        {
            if (showDialogWhenMissing)
            {
                TaskDialog("Отчёт коллизий", "Выберите строку коллизии для выбора элементов.");
            }

            return false;
        }

        elementResolver.Resolve(document, item);
        List<ElementId> elementIds = item
            .GetResolvedElementIds()
            .Distinct()
            .Select(RevitElementIds.Create)
            .ToList();
        if (elementIds.Count == 0)
        {
            string message = "Нет найденных ElementId для выбора. Нажмите «Проверить» или пересканируйте модель.";
            AddReport("Выбор", item.ClashId, item.Name, "Error", message);
            UpdateStatus();
            if (showDialogWhenMissing)
            {
                TaskDialog("Отчёт коллизий", message);
            }

            return false;
        }

        try
        {
            uiDocument.Selection.SetElementIds(elementIds);
            string linkedNote = item.IsLinkDriven
                ? " Для RVT-связей выбирается экземпляр связи; вложенный элемент доступен в отчёте по ElementId."
                : string.Empty;
            string message = $"Выбрано элементов: {elementIds.Count}.{linkedNote}";
            AddReport("Выбор", item.ClashId, item.Name, "OK", message);
            UpdateStatus(message);
            logger.Info($"Clash '{item.ClashId}' selected {elementIds.Count} Revit elements.");
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Autodesk.Revit.Exceptions.InvalidOperationException or ArgumentException)
        {
            string message = $"Не удалось выбрать элементы: {exception.Message}";
            AddReport("Выбор", item.ClashId, item.Name, "Error", message);
            UpdateStatus();
            logger.Warning($"Failed to select clash '{item.ClashId}': {exception.Message}");
            if (showDialogWhenMissing)
            {
                TaskDialog("Отчёт коллизий", message);
            }

            return false;
        }
    }

    private void NavigateToSelectedClash()
    {
        CommitGridEdits();
        if (clashGrid.SelectedItem is not ClashItem item)
        {
            TaskDialog("Отчёт коллизий", "Выберите строку коллизии для перехода в 3D.");
            return;
        }

        elementResolver.Resolve(document, item);
        ClashReportProfile profile = ReadProfile();
        storage.SaveStates(modelKey, clashRows, profile);
        ClashNavigationResult result = viewNavigator.Focus(uiDocument, document, item, profile);
        AddReport(
            "3D",
            item.ClashId,
            item.Name,
            result.Succeeded ? "OK" : "Error",
            string.IsNullOrWhiteSpace(result.ViewName)
                ? result.Message
                : $"{result.Message} Вид: {result.ViewName}, элементов: {result.SelectedElementCount}.");

        UpdateStatus();
        if (!result.Succeeded)
        {
            TaskDialog("Отчёт коллизий", result.Message);
        }
    }

    private void SaveState(bool showDialog)
    {
        CommitGridEdits();
        storage.SaveStates(modelKey, clashRows, ReadProfile());
        if (showDialog)
        {
            TaskDialog("Отчёт коллизий", "Состояние коллизий сохранено в локальный JSON профиля.");
        }
    }

    private void ExportReport()
    {
        CommitGridEdits();
        SaveState(showDialog: false);
        if (clashRows.Count == 0)
        {
            TaskDialog("Отчёт коллизий", "Нет найденных коллизий для экспорта.");
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "Экспорт отчёта коллизий",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = "truebim-clash-report.csv",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string csv = reportCsvService.Format(clashRows);
        csvExportService.WriteUtf8WithBom(dialog.FileName, csv);
        AddReport("Экспорт", string.Empty, string.Empty, "OK", $"CSV сохранён: {dialog.FileName}");
        UpdateStatus();
    }

    private ClashReportProfile ReadProfile()
    {
        return ClashReportStorage.NormalizeProfile(new ClashReportProfile
        {
            Name = profileNameInput.Text,
            LastCsvPath = string.Empty,
            SectionBoxPaddingMm = ParseDouble(paddingInput.Text, 1500),
            MinimumOverlapMm = ParseDouble(minimumOverlapInput.Text, 0),
            HighlightOnNavigate = highlightInput.IsChecked == true,
            ScanCurrentModel = scanCurrentModelInput.IsChecked == true,
            ScanRvtLinks = scanRvtLinksInput.IsChecked == true,
            ScanLinksAgainstEachOther = scanLinksAgainstEachOtherInput.IsChecked == true
        });
    }

    private static ClashScanOptions CreateScanOptions(ClashReportProfile profile)
    {
        return new ClashScanOptions
        {
            ScanCurrentModel = profile.ScanCurrentModel,
            ScanRvtLinks = profile.ScanRvtLinks,
            ScanLinksAgainstEachOther = profile.ScanLinksAgainstEachOther,
            MinimumOverlapMm = profile.MinimumOverlapMm
        };
    }

    private void RefreshFilter()
    {
        CollectionViewSource.GetDefaultView(clashGrid.ItemsSource)?.Refresh();
        UpdateStatus();
    }

    private bool MatchesFilter(object value)
    {
        if (value is not ClashItem item)
        {
            return false;
        }

        string filter = filterInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return Contains(item.ClashId, filter)
            || Contains(item.Source, filter)
            || Contains(item.Name, filter)
            || Contains(item.ElementId1Text, filter)
            || Contains(item.ElementId2Text, filter)
            || Contains(item.Element1Name, filter)
            || Contains(item.Element2Name, filter)
            || Contains(item.StatusDisplay, filter)
            || Contains(item.Comment, filter)
            || Contains(item.Message, filter);
    }

    private void UpdateStatus(string? prefix = null)
    {
        int resolved = clashRows.Count(item => item.GetResolvedElementIds().Count > 0);
        int open = clashRows.Count(item => item.Status == ClashStatus.Open);
        int filtered = CollectionViewSource.GetDefaultView(clashGrid.ItemsSource)?.Cast<object>().Count() ?? clashRows.Count;
        string text = $"Коллизий: {clashRows.Count}; показано: {filtered}; с найденными элементами: {resolved}; Open: {open}; отчётных событий: {reportRows.Count}.";
        statusText.Text = string.IsNullOrWhiteSpace(prefix) ? text : $"{prefix} {text}";
        bool hasRows = clashRows.Count > 0;
        selectButton.IsEnabled = hasRows;
        navigateButton.IsEnabled = hasRows;
        saveStateButton.IsEnabled = hasRows;
        exportReportButton.IsEnabled = hasRows;
    }

    private void AddReport(string operation, string clashId, string clashName, string status, string message)
    {
        reportRows.Add(new ClashReportRow(operation, clashId, clashName, status, message));
    }

    private void CommitGridEdits()
    {
        clashGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        clashGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
    }

    private static void AddLabel(WpfGrid grid, string text, int column, int row)
    {
        TextBlock label = new()
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold
        };
        WpfGrid.SetColumn(label, column);
        WpfGrid.SetRow(label, row);
        grid.Children.Add(label);
    }

    private static TextBlock CreateOptionLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
    }

    private static Button CreateButton(string text, TrueBimIcon icon, double minWidth)
    {
        Button button = new()
        {
            MinWidth = minWidth,
            Height = 32,
            Margin = new Thickness(6, 0, 0, 8),
            Padding = new Thickness(10, 0, 10, 0)
        };

        StackPanel content = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        content.Children.Add(IconFactory.Create(icon, 16));
        content.Children.Add(new TextBlock
        {
            Text = text,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        button.Content = content;
        return button;
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

    private static DataGridTextColumn CreateEditableTextColumn(string header, string bindingPath, double width)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new WpfBinding(bindingPath)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
            },
            Width = new DataGridLength(width)
        };
    }

    private static DataGridComboBoxColumn CreateStatusColumn()
    {
        return new DataGridComboBoxColumn
        {
            Header = "Статус",
            ItemsSource = StatusOptions,
            SelectedItemBinding = new WpfBinding(nameof(ClashItem.Status))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = new DataGridLength(120)
        };
    }

    private static bool Contains(string value, string filter)
    {
        return value?.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    private static double ParseDouble(string value, double fallback)
    {
        string normalized = (value ?? string.Empty).Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
            ? result
            : fallback;
    }

    private static void TaskDialog(string title, string message)
    {
        Autodesk.Revit.UI.TaskDialog.Show(title, message);
    }
}
