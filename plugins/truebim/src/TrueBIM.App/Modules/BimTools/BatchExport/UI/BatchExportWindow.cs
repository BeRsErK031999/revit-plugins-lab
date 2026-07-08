using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using TrueBIM.App.Modules.BimTools.BatchExport.Models;
using TrueBIM.App.Modules.BimTools.BatchExport.Services;
using TrueBIM.App.Modules.BimTools.Common.Services.Export;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Modules.Print.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using RevitDocument = Autodesk.Revit.DB.Document;
using WpfGrid = System.Windows.Controls.Grid;

namespace TrueBIM.App.Modules.BimTools.BatchExport.UI;

public sealed class BatchExportWindow : Window
{
    private readonly ObservableCollection<BatchExportSheetRow> visibleRows = new();
    private readonly ObservableCollection<BatchExportReportRow> reportRows = new();
    private readonly ObservableCollection<BatchExportSheetSet> sheetSets = new();
    private readonly List<BatchExportSheetRow> allRows;
    private readonly RevitDocument document;
    private readonly BatchExportProfileStorage profileStorage;
    private readonly BatchExportNamingService namingService = new();
    private readonly BatchExportReportCsvService reportCsvService = new();
    private readonly CsvExportService csvExportService = new();
    private readonly PrintPdfExportService pdfExportService = new();
    private readonly PrintCadExportService cadExportService = new();
    private readonly BatchExportFileNameContext fileNameContext;
    private readonly ITrueBimLogger logger;
    private readonly TextBox exportFolderInput = new();
    private readonly TextBox fileNameTemplateInput = new();
    private readonly TextBox filterInput = new();
    private readonly TextBox sheetSetNameInput = new();
    private readonly ComboBox sheetSetInput = new();
    private readonly CheckBox pdfInput = new()
    {
        Content = "PDF",
        IsChecked = true,
        ToolTip = "Экспортировать выбранные листы в PDF."
    };
    private readonly CheckBox dwgInput = new()
    {
        Content = "DWG",
        ToolTip = "Экспортировать выбранные листы в DWG."
    };
    private readonly DataGrid sheetGrid = new();
    private readonly DataGrid reportGrid = new();
    private readonly TextBlock statusText = new();
    private readonly Button exportButton = CreateActionButton("Экспортировать", TrueBimIcon.Export);
    private readonly Button exportReportButton = CreateActionButton("Отчёт CSV", TrueBimIcon.Export);
    private readonly Button loadSheetSetButton = CreateActionButton("Загрузить", TrueBimIcon.Apply);
    private readonly Button saveSheetSetButton = CreateActionButton("Сохранить набор", TrueBimIcon.Apply);
    private readonly Button deleteSheetSetButton = CreateActionButton("Удалить", TrueBimIcon.Close);
    private bool isUpdatingSheetSetSelection;

    public BatchExportWindow(
        RevitDocument document,
        IReadOnlyList<PrintSheetInfo> sheets,
        BatchExportProfileStorage profileStorage,
        ITrueBimLogger logger)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        this.profileStorage = profileStorage ?? throw new ArgumentNullException(nameof(profileStorage));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        fileNameContext = BatchExportNamingService.CreateContext(document);
        BatchExportProfile profile = this.profileStorage.Load();
        allRows = (sheets ?? throw new ArgumentNullException(nameof(sheets)))
            .OrderBy(sheet => sheet.SheetNumber, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(sheet => sheet.SheetName, StringComparer.CurrentCultureIgnoreCase)
            .Select(sheet => new BatchExportSheetRow(sheet))
            .ToList();

        foreach (BatchExportSheetRow row in allRows)
        {
            row.PropertyChanged += OnSheetRowPropertyChanged;
        }

        Title = "Экспорт PDF/DWG";
        Icon = IconFactory.CreateImage(TrueBimIcon.Export, 32);
        Width = 1120;
        Height = 720;
        MinWidth = 980;
        MinHeight = 620;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();

        ApplyProfile(profile);
        RefreshVisibleRows();
        UpdatePreviews();
        logger.Info($"Batch Export window opened for '{document.Title}' with {allRows.Count} sheets.");
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
            Margin = new Thickness(20)
        };

        UIElement controls = CreateControls();
        DockPanel.SetDock(controls, Dock.Top);
        root.Children.Add(controls);

        statusText.Margin = new Thickness(0, 0, 0, 10);
        statusText.TextWrapping = TextWrapping.Wrap;
        DockPanel.SetDock(statusText, Dock.Top);
        root.Children.Add(statusText);

        TabControl tabs = new();
        tabs.Items.Add(new TabItem
        {
            Header = "Листы",
            Content = CreateSheetGrid()
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Отчёт",
            Content = CreateReportGrid()
        });
        root.Children.Add(tabs);
        return root;
    }

    private UIElement CreateControls()
    {
        WpfGrid root = new()
        {
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddLabel(root, "Папка выгрузки", 0, 0);
        exportFolderInput.Height = 32;
        exportFolderInput.MinWidth = 420;
        exportFolderInput.Margin = new Thickness(8, 0, 8, 8);
        exportFolderInput.TextChanged += (_, _) => UpdateExportState();
        WpfGrid.SetColumn(exportFolderInput, 1);
        root.Children.Add(exportFolderInput);

        Button browseButton = CreateActionButton("Выбрать", TrueBimIcon.Open);
        browseButton.Margin = new Thickness(0, 0, 8, 8);
        browseButton.Click += (_, _) => BrowseExportFolder();
        WpfGrid.SetColumn(browseButton, 2);
        root.Children.Add(browseButton);

        StackPanel formats = new()
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        pdfInput.Margin = new Thickness(0, 0, 12, 0);
        dwgInput.Margin = new Thickness(0, 0, 12, 0);
        pdfInput.Checked += (_, _) => UpdateExportState();
        pdfInput.Unchecked += (_, _) => UpdateExportState();
        dwgInput.Checked += (_, _) => UpdateExportState();
        dwgInput.Unchecked += (_, _) => UpdateExportState();
        formats.Children.Add(pdfInput);
        formats.Children.Add(dwgInput);
        WpfGrid.SetColumn(formats, 3);
        root.Children.Add(formats);

        AddLabel(root, "Правило имени", 0, 1);
        fileNameTemplateInput.Height = 32;
        fileNameTemplateInput.Margin = new Thickness(8, 0, 8, 8);
        fileNameTemplateInput.ToolTip = "Поддерживаются {SheetNumber}, {SheetName}, {Revision}, {Date:yyyy-MM-dd}, {Counter:000}, {SheetParameter:...}, {ProjectParameter:...}.";
        fileNameTemplateInput.TextChanged += (_, _) => UpdatePreviews();
        WpfGrid.SetRow(fileNameTemplateInput, 1);
        WpfGrid.SetColumn(fileNameTemplateInput, 1);
        root.Children.Add(fileNameTemplateInput);

        Button previewButton = CreateActionButton("Предпросмотр", TrueBimIcon.Preview);
        previewButton.Margin = new Thickness(0, 0, 8, 8);
        previewButton.Click += (_, _) => UpdatePreviews();
        WpfGrid.SetRow(previewButton, 1);
        WpfGrid.SetColumn(previewButton, 2);
        root.Children.Add(previewButton);

        AddLabel(root, "Набор листов", 0, 2);
        StackPanel sheetSetControls = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 0, 8, 8)
        };
        sheetSetInput.Width = 240;
        sheetSetInput.Height = 32;
        sheetSetInput.DisplayMemberPath = nameof(BatchExportSheetSet.Name);
        sheetSetInput.ItemsSource = sheetSets;
        sheetSetInput.ToolTip = "Сохранённые наборы листов для текущего профиля экспорта.";
        sheetSetInput.SelectionChanged += (_, _) => OnSheetSetSelectionChanged();
        sheetSetControls.Children.Add(sheetSetInput);

        sheetSetNameInput.Width = 240;
        sheetSetNameInput.Height = 32;
        sheetSetNameInput.Margin = new Thickness(8, 0, 0, 0);
        sheetSetNameInput.ToolTip = "Имя нового или обновляемого набора листов.";
        sheetSetControls.Children.Add(sheetSetNameInput);

        WpfGrid.SetRow(sheetSetControls, 2);
        WpfGrid.SetColumn(sheetSetControls, 1);
        root.Children.Add(sheetSetControls);

        StackPanel sheetSetActions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        loadSheetSetButton.Click += (_, _) => LoadSelectedSheetSet();
        sheetSetActions.Children.Add(loadSheetSetButton);
        saveSheetSetButton.Click += (_, _) => SaveCurrentSheetSet();
        sheetSetActions.Children.Add(saveSheetSetButton);
        deleteSheetSetButton.Click += (_, _) => DeleteSelectedSheetSet();
        sheetSetActions.Children.Add(deleteSheetSetButton);
        WpfGrid.SetRow(sheetSetActions, 2);
        WpfGrid.SetColumn(sheetSetActions, 2);
        WpfGrid.SetColumnSpan(sheetSetActions, 2);
        root.Children.Add(sheetSetActions);

        AddLabel(root, "Фильтр", 0, 3);
        filterInput.Height = 32;
        filterInput.Margin = new Thickness(8, 0, 8, 0);
        filterInput.ToolTip = "Фильтр по номеру или имени листа.";
        filterInput.TextChanged += (_, _) => RefreshVisibleRows();
        WpfGrid.SetRow(filterInput, 3);
        WpfGrid.SetColumn(filterInput, 1);
        root.Children.Add(filterInput);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Button selectAllButton = CreateActionButton("Выбрать все", TrueBimIcon.Apply);
        selectAllButton.Click += (_, _) => SetVisibleRowsSelected(true);
        actions.Children.Add(selectAllButton);

        Button clearButton = CreateActionButton("Снять выбор", TrueBimIcon.Close);
        clearButton.Click += (_, _) => SetVisibleRowsSelected(false);
        actions.Children.Add(clearButton);

        exportButton.Click += (_, _) => StartExport();
        actions.Children.Add(exportButton);

        exportReportButton.IsEnabled = false;
        exportReportButton.Click += (_, _) => ExportReportCsv();
        actions.Children.Add(exportReportButton);

        Button closeButton = CreateActionButton("Закрыть", TrueBimIcon.Close);
        closeButton.IsCancel = true;
        closeButton.Click += (_, _) => Close();
        actions.Children.Add(closeButton);

        WpfGrid.SetRow(actions, 3);
        WpfGrid.SetColumn(actions, 2);
        WpfGrid.SetColumnSpan(actions, 2);
        root.Children.Add(actions);

        return root;
    }

    private DataGrid CreateSheetGrid()
    {
        sheetGrid.AutoGenerateColumns = false;
        sheetGrid.CanUserAddRows = false;
        sheetGrid.CanUserDeleteRows = false;
        sheetGrid.IsReadOnly = false;
        sheetGrid.SelectionMode = DataGridSelectionMode.Extended;
        sheetGrid.ItemsSource = visibleRows;
        sheetGrid.Columns.Add(CreateSelectionColumn());
        sheetGrid.Columns.Add(CreateTextColumn("Номер", nameof(BatchExportSheetRow.SheetNumber), 120));
        sheetGrid.Columns.Add(CreateTextColumn("Имя листа", nameof(BatchExportSheetRow.SheetName), new DataGridLength(1, DataGridLengthUnitType.Star)));
        sheetGrid.Columns.Add(CreateTextColumn("Формат", nameof(BatchExportSheetRow.SheetFormat), 120));
        sheetGrid.Columns.Add(CreateTextColumn("Печать", nameof(BatchExportSheetRow.PrintableStatus), 90));
        sheetGrid.Columns.Add(CreateTextColumn("Имя файла", nameof(BatchExportSheetRow.FileNamePreview), new DataGridLength(240)));
        sheetGrid.Columns.Add(CreateTextColumn("Статус", nameof(BatchExportSheetRow.Status), 160));
        return sheetGrid;
    }

    private DataGrid CreateReportGrid()
    {
        reportGrid.AutoGenerateColumns = false;
        reportGrid.CanUserAddRows = false;
        reportGrid.CanUserDeleteRows = false;
        reportGrid.IsReadOnly = true;
        reportGrid.ItemsSource = reportRows;
        reportGrid.Columns.Add(CreateTextColumn("Лист", nameof(BatchExportReportRow.SheetNumber), 100));
        reportGrid.Columns.Add(CreateTextColumn("Имя", nameof(BatchExportReportRow.SheetName), new DataGridLength(1, DataGridLengthUnitType.Star)));
        reportGrid.Columns.Add(CreateTextColumn("Формат", nameof(BatchExportReportRow.Format), 80));
        reportGrid.Columns.Add(CreateTextColumn("Статус", nameof(BatchExportReportRow.Status), 100));
        reportGrid.Columns.Add(CreateTextColumn("Сообщение", nameof(BatchExportReportRow.Message), new DataGridLength(220)));
        reportGrid.Columns.Add(CreateTextColumn("Файл", nameof(BatchExportReportRow.FilePath), new DataGridLength(260)));
        return reportGrid;
    }

    private void ApplyProfile(BatchExportProfile profile)
    {
        exportFolderInput.Text = BatchExportNamingService.GetInitialExportFolder(document, profile.ExportFolder);
        fileNameTemplateInput.Text = profile.FileNameTemplate;
        pdfInput.IsChecked = profile.ExportPdf;
        dwgInput.IsChecked = profile.ExportDwg;
        ApplySheetSets(profile.SheetSets, profile.ActiveSheetSetName);
    }

    private void SaveProfile()
    {
        profileStorage.Save(new BatchExportProfile
        {
            ExportFolder = exportFolderInput.Text,
            FileNameTemplate = fileNameTemplateInput.Text,
            ExportPdf = pdfInput.IsChecked == true,
            ExportDwg = dwgInput.IsChecked == true,
            ActiveSheetSetName = (sheetSetInput.SelectedItem as BatchExportSheetSet)?.Name,
            SheetSets = CloneSheetSets(sheetSets)
        });
    }

    private void RefreshVisibleRows()
    {
        string filter = filterInput.Text.Trim();
        IEnumerable<BatchExportSheetRow> rows = allRows;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            rows = rows.Where(row =>
                row.SheetNumber.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
                || row.SheetName.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        visibleRows.Clear();
        foreach (BatchExportSheetRow row in rows)
        {
            visibleRows.Add(row);
        }

        UpdateExportState();
    }

    private void ApplySheetSets(IReadOnlyList<BatchExportSheetSet> profileSheetSets, string? activeSheetSetName)
    {
        isUpdatingSheetSetSelection = true;
        sheetSets.Clear();
        foreach (BatchExportSheetSet sheetSet in profileSheetSets)
        {
            sheetSets.Add(CloneSheetSet(sheetSet));
        }

        BatchExportSheetSet? selected = sheetSets.FirstOrDefault(sheetSet =>
            string.Equals(sheetSet.Name, activeSheetSetName, StringComparison.CurrentCultureIgnoreCase))
            ?? sheetSets.FirstOrDefault();
        sheetSetInput.SelectedItem = selected;
        sheetSetNameInput.Text = selected?.Name ?? string.Empty;
        isUpdatingSheetSetSelection = false;
        UpdateSheetSetState();
    }

    private void OnSheetSetSelectionChanged()
    {
        if (isUpdatingSheetSetSelection)
        {
            return;
        }

        if (sheetSetInput.SelectedItem is BatchExportSheetSet sheetSet)
        {
            sheetSetNameInput.Text = sheetSet.Name;
        }

        UpdateSheetSetState();
        UpdateExportState();
    }

    private void LoadSelectedSheetSet()
    {
        if (sheetSetInput.SelectedItem is not BatchExportSheetSet sheetSet)
        {
            Autodesk.Revit.UI.TaskDialog.Show("Экспорт PDF/DWG", "Выберите сохранённый набор листов.");
            return;
        }

        HashSet<string> sheetNumbers = new(sheetSet.SheetNumbers, StringComparer.CurrentCultureIgnoreCase);
        int selectedCount = 0;
        int missingCount = sheetNumbers.Count;
        foreach (BatchExportSheetRow row in allRows)
        {
            bool shouldSelect = row.CanBePrinted && sheetNumbers.Contains(row.SheetNumber);
            row.IsSelected = shouldSelect;
            if (shouldSelect)
            {
                selectedCount++;
                missingCount--;
            }
        }

        SaveProfile();
        RefreshVisibleRows();
        Autodesk.Revit.UI.TaskDialog.Show(
            "Экспорт PDF/DWG",
            $"Загружен набор '{sheetSet.Name}'.\nВыбрано листов: {selectedCount}\nНе найдено или непечатаемых: {Math.Max(0, missingCount)}");
    }

    private void SaveCurrentSheetSet()
    {
        string name = sheetSetNameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Autodesk.Revit.UI.TaskDialog.Show("Экспорт PDF/DWG", "Введите имя набора листов.");
            return;
        }

        List<string> selectedSheetNumbers = allRows
            .Where(row => row.IsSelected && row.CanBePrinted)
            .Select(row => row.SheetNumber)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(sheetNumber => sheetNumber, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (selectedSheetNumbers.Count == 0)
        {
            Autodesk.Revit.UI.TaskDialog.Show("Экспорт PDF/DWG", "Выберите хотя бы один печатаемый лист для набора.");
            return;
        }

        BatchExportSheetSet saved = new()
        {
            Name = name,
            SheetNumbers = selectedSheetNumbers
        };
        ReplaceSheetSet(saved);
        SaveProfile();
        UpdateSheetSetState();
        UpdateExportState();
        Autodesk.Revit.UI.TaskDialog.Show("Экспорт PDF/DWG", $"Набор '{saved.Name}' сохранён. Листов: {saved.SheetNumbers.Count}.");
    }

    private void DeleteSelectedSheetSet()
    {
        if (sheetSetInput.SelectedItem is not BatchExportSheetSet sheetSet)
        {
            Autodesk.Revit.UI.TaskDialog.Show("Экспорт PDF/DWG", "Выберите набор листов для удаления.");
            return;
        }

        sheetSets.Remove(sheetSet);
        BatchExportSheetSet? next = sheetSets.FirstOrDefault();
        sheetSetInput.SelectedItem = next;
        sheetSetNameInput.Text = next?.Name ?? string.Empty;
        SaveProfile();
        UpdateSheetSetState();
        UpdateExportState();
        Autodesk.Revit.UI.TaskDialog.Show("Экспорт PDF/DWG", $"Набор '{sheetSet.Name}' удалён.");
    }

    private void ReplaceSheetSet(BatchExportSheetSet saved)
    {
        BatchExportSheetSet? existing = sheetSets.FirstOrDefault(sheetSet =>
            string.Equals(sheetSet.Name, saved.Name, StringComparison.CurrentCultureIgnoreCase));
        if (existing is not null)
        {
            sheetSets.Remove(existing);
        }

        int insertIndex = 0;
        while (insertIndex < sheetSets.Count
            && string.Compare(sheetSets[insertIndex].Name, saved.Name, StringComparison.CurrentCultureIgnoreCase) < 0)
        {
            insertIndex++;
        }

        sheetSets.Insert(insertIndex, saved);
        sheetSetInput.SelectedItem = saved;
        sheetSetNameInput.Text = saved.Name;
    }

    private void UpdateSheetSetState()
    {
        bool hasSet = sheetSetInput.SelectedItem is BatchExportSheetSet;
        loadSheetSetButton.IsEnabled = hasSet;
        deleteSheetSetButton.IsEnabled = hasSet;
        saveSheetSetButton.IsEnabled = allRows.Any(row => row.IsSelected && row.CanBePrinted);
    }

    private void UpdatePreviews()
    {
        int counter = 1;
        foreach (BatchExportSheetRow row in allRows)
        {
            row.ApplyPreview(namingService.Build(fileNameTemplateInput.Text, row.Sheet, fileNameContext, counter));
            row.Status = string.Empty;
            counter++;
        }

        HashSet<string> duplicateNames = allRows
            .Where(row => row.CanBePrinted)
            .GroupBy(row => row.FileNamePreview, StringComparer.CurrentCultureIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        foreach (BatchExportSheetRow row in allRows)
        {
            row.IsFileNameDuplicate = row.CanBePrinted && duplicateNames.Contains(row.FileNamePreview);
        }

        UpdateExportState();
    }

    private void SetVisibleRowsSelected(bool isSelected)
    {
        foreach (BatchExportSheetRow row in visibleRows.Where(row => row.CanBePrinted))
        {
            row.IsSelected = isSelected;
        }

        UpdateExportState();
    }

    private void BrowseExportFolder()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Выберите папку выгрузки",
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "Выберите папку",
            InitialDirectory = Directory.Exists(exportFolderInput.Text)
                ? exportFolderInput.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string? folder = Path.GetDirectoryName(dialog.FileName);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            exportFolderInput.Text = folder;
        }
    }

    private void StartExport()
    {
        SaveProfile();
        List<BatchExportSheetRow> selectedRows = allRows
            .Where(row => row.IsSelected && row.CanBePrinted)
            .ToList();
        if (selectedRows.Count == 0)
        {
            Autodesk.Revit.UI.TaskDialog.Show("Экспорт PDF/DWG", "Выберите хотя бы один печатаемый лист.");
            return;
        }

        string exportFolder = exportFolderInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(exportFolder))
        {
            Autodesk.Revit.UI.TaskDialog.Show("Экспорт PDF/DWG", "Укажите папку выгрузки.");
            return;
        }

        bool exportPdf = pdfInput.IsChecked == true;
        bool exportDwg = dwgInput.IsChecked == true;
        if (!exportPdf && !exportDwg)
        {
            Autodesk.Revit.UI.TaskDialog.Show("Экспорт PDF/DWG", "Выберите PDF или DWG.");
            return;
        }

        if (selectedRows.Any(row => row.IsFileNameDuplicate))
        {
            Autodesk.Revit.UI.TaskDialog.Show("Экспорт PDF/DWG", "В выбранных листах есть дубли имён файлов. Измените правило имени или выбор листов.");
            return;
        }

        List<string> existingPaths = selectedRows
            .SelectMany(row => GetOutputPaths(row, exportPdf, exportDwg, exportFolder))
            .Where(File.Exists)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (existingPaths.Count > 0)
        {
            MessageBoxResult result = MessageBox.Show(
                this,
                $"В папке выгрузки уже есть файлов с такими именами: {existingPaths.Count}.\n\nЗаменить существующие файлы?",
                "Экспорт PDF/DWG",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        reportRows.Clear();
        foreach (BatchExportSheetRow row in allRows)
        {
            row.Status = string.Empty;
        }

        Dictionary<BatchExportSheetRow, List<string>> rowStatuses = selectedRows.ToDictionary(row => row, _ => new List<string>());
        int exportedCount = 0;
        int failureCount = 0;

        if (exportPdf)
        {
            PrintPdfExportResult result = pdfExportService.Export(
                document,
                exportFolder,
                selectedRows.Select(row => new PrintPdfExportItem(row.ElementId, row.FileNamePreview)).ToList(),
                PrintPdfExportMode.SeparateFiles,
                combinedFileName: null,
                PrintPdfExportService.DefaultSettings,
                logger);
            exportedCount += result.ExportedFiles.Count;
            failureCount += result.Failures.Count;
            ApplyPdfReport(selectedRows, result, exportFolder, rowStatuses);
        }

        if (exportDwg)
        {
            PrintCadExportResult result = cadExportService.Export(
                document,
                exportFolder,
                selectedRows.Select(row => new PrintCadExportItem(row.ElementId, row.FileNamePreview)).ToList(),
                PrintCadExportFormat.Dwg,
                setupName: null,
                logger);
            exportedCount += result.ExportedFiles.Count;
            failureCount += result.Failures.Count;
            ApplyDwgReport(selectedRows, result, exportFolder, rowStatuses);
        }

        foreach (BatchExportSheetRow row in selectedRows)
        {
            row.Status = rowStatuses.TryGetValue(row, out List<string>? statuses)
                ? string.Join(", ", statuses)
                : string.Empty;
        }

        exportReportButton.IsEnabled = reportRows.Count > 0;
        Autodesk.Revit.UI.TaskDialog.Show(
            "Экспорт PDF/DWG",
            $"Экспортировано файлов: {exportedCount}\nОшибок: {failureCount}\nОтчётных строк: {reportRows.Count}");
        UpdateExportState();
    }

    private void ApplyPdfReport(
        IReadOnlyList<BatchExportSheetRow> selectedRows,
        PrintPdfExportResult result,
        string exportFolder,
        Dictionary<BatchExportSheetRow, List<string>> rowStatuses)
    {
        Dictionary<long, string> failuresById = result.Failures
            .GroupBy(failure => failure.Item.ElementId)
            .ToDictionary(group => group.Key, group => string.Join("; ", group.Select(failure => failure.Message)));

        foreach (BatchExportSheetRow row in selectedRows)
        {
            string path = Path.Combine(exportFolder, PrintPdfExportService.NormalizePdfFileName(row.FileNamePreview));
            if (failuresById.TryGetValue(row.ElementId, out string? message))
            {
                rowStatuses[row].Add("PDF ошибка");
                reportRows.Add(new BatchExportReportRow(row.SheetNumber, row.SheetName, "PDF", "Ошибка", message, path));
            }
            else
            {
                rowStatuses[row].Add("PDF готов");
                reportRows.Add(new BatchExportReportRow(row.SheetNumber, row.SheetName, "PDF", "Готово", "Экспорт выполнен.", path));
            }
        }
    }

    private void ApplyDwgReport(
        IReadOnlyList<BatchExportSheetRow> selectedRows,
        PrintCadExportResult result,
        string exportFolder,
        Dictionary<BatchExportSheetRow, List<string>> rowStatuses)
    {
        Dictionary<long, string> failuresById = result.Failures
            .GroupBy(failure => failure.Item.ElementId)
            .ToDictionary(group => group.Key, group => string.Join("; ", group.Select(failure => failure.Message)));

        foreach (BatchExportSheetRow row in selectedRows)
        {
            string path = Path.Combine(exportFolder, PrintCadExportService.NormalizeCadFileName(row.FileNamePreview, PrintCadExportFormat.Dwg));
            if (failuresById.TryGetValue(row.ElementId, out string? message))
            {
                rowStatuses[row].Add("DWG ошибка");
                reportRows.Add(new BatchExportReportRow(row.SheetNumber, row.SheetName, "DWG", "Ошибка", message, path));
            }
            else
            {
                rowStatuses[row].Add("DWG готов");
                reportRows.Add(new BatchExportReportRow(row.SheetNumber, row.SheetName, "DWG", "Готово", "Экспорт выполнен.", path));
            }
        }
    }

    private IEnumerable<string> GetOutputPaths(
        BatchExportSheetRow row,
        bool exportPdf,
        bool exportDwg,
        string exportFolder)
    {
        if (exportPdf)
        {
            yield return Path.Combine(exportFolder, PrintPdfExportService.NormalizePdfFileName(row.FileNamePreview));
        }

        if (exportDwg)
        {
            yield return Path.Combine(exportFolder, PrintCadExportService.NormalizeCadFileName(row.FileNamePreview, PrintCadExportFormat.Dwg));
        }
    }

    private void ExportReportCsv()
    {
        if (reportRows.Count == 0)
        {
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "Сохранить отчёт экспорта",
            Filter = "CSV UTF-8 (*.csv)|*.csv",
            FileName = "batch-export-report.csv",
            InitialDirectory = Directory.Exists(exportFolderInput.Text)
                ? exportFolderInput.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        csvExportService.WriteUtf8WithBom(dialog.FileName, reportCsvService.Format(reportRows.ToList()));
        logger.Info($"Batch Export report saved: {dialog.FileName}.");
    }

    private void UpdateExportState()
    {
        int selectedCount = allRows.Count(row => row.IsSelected && row.CanBePrinted);
        int printableCount = allRows.Count(row => row.CanBePrinted);
        int duplicateSelectedCount = allRows.Count(row => row.IsSelected && row.IsFileNameDuplicate);
        int truncatedCount = allRows.Count(row => row.IsSelected && row.IsFileNameTruncated);
        int missingTokenCount = allRows.Count(row => row.IsSelected && row.HasMissingTokens);
        bool hasFolder = !string.IsNullOrWhiteSpace(exportFolderInput.Text);
        bool hasFormat = pdfInput.IsChecked == true || dwgInput.IsChecked == true;
        exportButton.IsEnabled = selectedCount > 0 && hasFolder && hasFormat && duplicateSelectedCount == 0;
        UpdateSheetSetState();

        string formats = GetSelectedFormatsText();
        string duplicatesText = duplicateSelectedCount > 0
            ? $" Дубли имен: {duplicateSelectedCount}."
            : string.Empty;
        string truncatedText = truncatedCount > 0
            ? $" Обрезанных имен: {truncatedCount}."
            : string.Empty;
        string missingTokenText = missingTokenCount > 0
            ? $" Пустые токены: {missingTokenCount}."
            : string.Empty;

        statusText.Text = $"Листов: {allRows.Count}. Печатаемых: {printableCount}. Показано: {visibleRows.Count}. Выбрано: {selectedCount}. Наборов: {sheetSets.Count}. Форматы: {formats}.{duplicatesText}{truncatedText}{missingTokenText}";
    }

    private string GetSelectedFormatsText()
    {
        List<string> formats = new();
        if (pdfInput.IsChecked == true)
        {
            formats.Add("PDF");
        }

        if (dwgInput.IsChecked == true)
        {
            formats.Add("DWG");
        }

        return formats.Count == 0
            ? "не выбраны"
            : string.Join(", ", formats);
    }

    private void OnSheetRowPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(BatchExportSheetRow.IsSelected))
        {
            UpdateExportState();
        }
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

    private static Button CreateActionButton(string text, TrueBimIcon icon)
    {
        return new Button
        {
            Content = IconFactory.CreateButtonContent(icon, text),
            MinWidth = 110,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0)
        };
    }

    private static List<BatchExportSheetSet> CloneSheetSets(IEnumerable<BatchExportSheetSet> source)
    {
        return source.Select(CloneSheetSet).ToList();
    }

    private static BatchExportSheetSet CloneSheetSet(BatchExportSheetSet source)
    {
        return new BatchExportSheetSet
        {
            Name = source.Name,
            SheetNumbers = source.SheetNumbers.ToList()
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
            Binding = new Binding(bindingPath),
            Width = width,
            IsReadOnly = true
        };
    }

    private static DataGridTemplateColumn CreateSelectionColumn()
    {
        FrameworkElementFactory checkBox = new(typeof(CheckBox));
        checkBox.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        checkBox.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        checkBox.SetBinding(
            CheckBox.IsCheckedProperty,
            new Binding(nameof(BatchExportSheetRow.IsSelected))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });

        return new DataGridTemplateColumn
        {
            Header = "Выбран",
            CellTemplate = new DataTemplate
            {
                VisualTree = checkBox
            },
            Width = 72
        };
    }
}
