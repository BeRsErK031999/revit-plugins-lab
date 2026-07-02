using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using TrueBIM.App.Modules.Print.Services;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using RevitDocument = Autodesk.Revit.DB.Document;

namespace TrueBIM.App.Modules.Print.UI;

public sealed class PrintWindow : Window
{
    private readonly ObservableCollection<PrintSheetRow> sheetRows = new();
    private readonly IReadOnlyList<PrintSheetInfo> sheets;
    private readonly RevitDocument document;
    private readonly ITrueBimLogger logger;
    private readonly PrintFileNameTemplateService fileNameTemplateService = new();
    private readonly PrintPdfExportService pdfExportService = new();
    private readonly PrintCadExportService cadExportService = new();
    private readonly PrintCadExportSetupService cadExportSetupService = new();
    private readonly ObservableCollection<PrintCadExportSetupOption> cadExportSetupOptions = new();
    private readonly PrintFileNameContext fileNameContext;
    private readonly DataGrid sheetGrid = new();
    private readonly TextBlock statusText = new();
    private readonly TextBox exportFolderInput = new();
    private readonly TextBox fileNameMaskInput = new()
    {
        Text = PrintFileNameTemplateService.DefaultTemplate,
        ToolTip = "Маска имени файла. Доступны токены: {SheetNumber}, {SheetName}, {ProjectNumber}, {ProjectName}, {DocumentName}, {Date:yyyy-MM-dd}, {Counter}, {Counter:000}."
    };
    private readonly CheckBox includePlaceholdersInput = new()
    {
        Content = "Листы-заглушки",
        ToolTip = "Показывает листы-заглушки в таблице. Они не выбираются для печати."
    };
    private readonly CheckBox pdfInput = new()
    {
        Content = "PDF",
        IsChecked = true,
        ToolTip = "Добавить PDF в очередь экспорта."
    };
    private readonly CheckBox dwgInput = new()
    {
        Content = "DWG",
        ToolTip = "Добавить DWG в очередь экспорта."
    };
    private readonly CheckBox dxfInput = new()
    {
        Content = "DXF",
        ToolTip = "Добавить DXF в очередь экспорта."
    };
    private readonly ComboBox dwgSetupInput = CreateCadSetupInput("Настройка экспорта DWG из сохраненных настроек Revit.");
    private readonly ComboBox dxfSetupInput = CreateCadSetupInput("Настройка экспорта DXF из сохраненных настроек Revit.");
    private readonly Button exportButton = CreateActionButton("Экспорт", TrueBimIcon.Export, isEnabled: false);

    public PrintWindow(RevitDocument document, IReadOnlyList<PrintSheetInfo> sheets, ITrueBimLogger logger)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        this.sheets = sheets ?? throw new ArgumentNullException(nameof(sheets));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        fileNameContext = CreateFileNameContext(document);
        LoadCadExportSetupOptions();

        Title = "Печать";
        Icon = IconFactory.CreateImage(TrueBimIcon.Print, 32);
        Width = 1120;
        Height = 720;
        MinWidth = 980;
        MinHeight = 620;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();

        LoadSheets();
        logger.Info($"Print window opened for '{document.Title}' with {sheets.Count} sheets.");
    }

    private UIElement CreateContent()
    {
        DockPanel root = new()
        {
            Margin = new Thickness(20)
        };

        UIElement readSettings = CreateReadSettings();
        DockPanel.SetDock(readSettings, Dock.Top);
        root.Children.Add(readSettings);

        UIElement status = CreateStatus();
        DockPanel.SetDock(status, Dock.Top);
        root.Children.Add(status);

        UIElement exportSettings = CreateExportSettings();
        DockPanel.SetDock(exportSettings, Dock.Bottom);
        root.Children.Add(exportSettings);

        root.Children.Add(CreateSheetGrid());
        return root;
    }

    private UIElement CreateReadSettings()
    {
        DockPanel controls = new()
        {
            Margin = new Thickness(0, 0, 0, 12)
        };

        StackPanel selectionActions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        Button selectAllButton = CreateActionButton("Выбрать все", TrueBimIcon.Apply, isEnabled: true);
        selectAllButton.Margin = new Thickness(0, 0, 8, 0);
        selectAllButton.ToolTip = "Отметить все печатаемые листы.";
        selectAllButton.Click += (_, _) => SetAllSelected(isSelected: true);
        selectionActions.Children.Add(selectAllButton);

        Button clearSelectionButton = CreateActionButton("Снять выбор", TrueBimIcon.Close, isEnabled: true);
        clearSelectionButton.Margin = new Thickness(0, 0, 8, 0);
        clearSelectionButton.ToolTip = "Снять отметки со всех листов.";
        clearSelectionButton.Click += (_, _) => SetAllSelected(isSelected: false);
        selectionActions.Children.Add(clearSelectionButton);

        Button refreshButton = CreateActionButton("Обновить", TrueBimIcon.Preview, isEnabled: true);
        refreshButton.Margin = new Thickness(0, 0, 16, 0);
        refreshButton.ToolTip = "Перечитать список листов, уже собранный при открытии окна.";
        refreshButton.Click += (_, _) => LoadSheets();
        selectionActions.Children.Add(refreshButton);

        includePlaceholdersInput.VerticalAlignment = VerticalAlignment.Center;
        includePlaceholdersInput.Checked += (_, _) => LoadSheets();
        includePlaceholdersInput.Unchecked += (_, _) => LoadSheets();
        selectionActions.Children.Add(includePlaceholdersInput);

        DockPanel.SetDock(selectionActions, Dock.Left);
        controls.Children.Add(selectionActions);

        TextBlock documentText = new()
        {
            Text = string.IsNullOrWhiteSpace(document.Title) ? "Активный документ" : document.Title,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        controls.Children.Add(documentText);

        return controls;
    }

    private UIElement CreateStatus()
    {
        statusText.Margin = new Thickness(0, 0, 0, 12);
        statusText.TextWrapping = TextWrapping.Wrap;
        return statusText;
    }

    private UIElement CreateSheetGrid()
    {
        sheetGrid.AutoGenerateColumns = false;
        sheetGrid.CanUserAddRows = false;
        sheetGrid.IsReadOnly = false;
        sheetGrid.ItemsSource = sheetRows;
        sheetGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        sheetGrid.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        sheetGrid.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        sheetGrid.SelectionMode = DataGridSelectionMode.Single;
        sheetGrid.ToolTip = "Список листов активного документа для будущей печати и экспорта.";

        sheetGrid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Выбран",
            Binding = new Binding(nameof(PrintSheetRow.IsSelected))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = 72
        });
        sheetGrid.Columns.Add(CreateTextColumn("Источник", nameof(PrintSheetRow.SourceName), 150));
        sheetGrid.Columns.Add(CreateTextColumn("Номер", nameof(PrintSheetRow.SheetNumber), 110));
        sheetGrid.Columns.Add(CreateTextColumn("Имя листа", nameof(PrintSheetRow.SheetName), new DataGridLength(1, DataGridLengthUnitType.Star)));
        sheetGrid.Columns.Add(CreateTextColumn("Формат", nameof(PrintSheetRow.SheetFormat), 120));
        sheetGrid.Columns.Add(CreateTextColumn("Статус", nameof(PrintSheetRow.Status), 150));
        sheetGrid.Columns.Add(CreateTextColumn("Имя файла", nameof(PrintSheetRow.FileNamePreview), 240));

        return sheetGrid;
    }

    private UIElement CreateExportSettings()
    {
        Grid root = new()
        {
            Margin = new Thickness(0, 16, 0, 0)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid folderRow = new();
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        folderRow.Children.Add(new TextBlock
        {
            Text = "Папка",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        exportFolderInput.Text = GetInitialExportFolder();
        exportFolderInput.Height = 32;
        exportFolderInput.ToolTip = "Папка назначения для будущего экспорта.";
        exportFolderInput.TextChanged += (_, _) =>
        {
            ResetExportStatuses();
            UpdateExportState();
        };
        Grid.SetColumn(exportFolderInput, 1);
        folderRow.Children.Add(exportFolderInput);

        Button browseButton = CreateActionButton("Обзор", TrueBimIcon.Open, isEnabled: true);
        browseButton.Margin = new Thickness(8, 0, 0, 0);
        browseButton.ToolTip = "Выбрать папку назначения.";
        browseButton.Click += (_, _) => BrowseExportFolder();
        Grid.SetColumn(browseButton, 2);
        folderRow.Children.Add(browseButton);

        Grid.SetRow(folderRow, 0);
        root.Children.Add(folderRow);

        Grid maskRow = new()
        {
            Margin = new Thickness(0, 8, 0, 0)
        };
        maskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        maskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        maskRow.Children.Add(new TextBlock
        {
            Text = "Маска имени",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        fileNameMaskInput.Height = 32;
        fileNameMaskInput.TextChanged += (_, _) => UpdateFileNamePreviews();
        Grid.SetColumn(fileNameMaskInput, 1);
        maskRow.Children.Add(fileNameMaskInput);

        Grid.SetRow(maskRow, 1);
        root.Children.Add(maskRow);

        Grid cadSetupRow = new()
        {
            Margin = new Thickness(0, 8, 0, 0)
        };
        cadSetupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cadSetupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cadSetupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cadSetupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        cadSetupRow.Children.Add(new TextBlock
        {
            Text = "DWG настройка",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        BindCadSetupInput(dwgSetupInput);
        Grid.SetColumn(dwgSetupInput, 1);
        cadSetupRow.Children.Add(dwgSetupInput);

        TextBlock dxfSetupLabel = new()
        {
            Text = "DXF настройка",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 8, 0)
        };
        Grid.SetColumn(dxfSetupLabel, 2);
        cadSetupRow.Children.Add(dxfSetupLabel);

        BindCadSetupInput(dxfSetupInput);
        Grid.SetColumn(dxfSetupInput, 3);
        cadSetupRow.Children.Add(dxfSetupInput);

        Grid.SetRow(cadSetupRow, 2);
        root.Children.Add(cadSetupRow);

        DockPanel actionRow = new()
        {
            Margin = new Thickness(0, 12, 0, 0)
        };

        StackPanel formatActions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        pdfInput.Margin = new Thickness(0, 0, 16, 0);
        dwgInput.Margin = new Thickness(0, 0, 16, 0);
        dxfInput.Margin = new Thickness(0, 0, 16, 0);
        pdfInput.Checked += (_, _) => UpdateExportState();
        pdfInput.Unchecked += (_, _) => UpdateExportState();
        dwgInput.Checked += (_, _) => UpdateExportState();
        dwgInput.Unchecked += (_, _) => UpdateExportState();
        dxfInput.Checked += (_, _) => UpdateExportState();
        dxfInput.Unchecked += (_, _) => UpdateExportState();
        formatActions.Children.Add(pdfInput);
        formatActions.Children.Add(dwgInput);
        formatActions.Children.Add(dxfInput);

        DockPanel.SetDock(formatActions, Dock.Left);
        actionRow.Children.Add(formatActions);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        exportButton.ToolTip = "Экспорт будет реализован следующими задачами.";
        exportButton.Click += (_, _) => StartExport();
        actions.Children.Add(exportButton);

        Button closeButton = CreateActionButton("Закрыть", TrueBimIcon.Close, isEnabled: true);
        closeButton.Margin = new Thickness(8, 0, 0, 0);
        closeButton.IsCancel = true;
        closeButton.ToolTip = "Закрыть окно печати.";
        closeButton.Click += (_, _) => Close();
        actions.Children.Add(closeButton);

        actionRow.Children.Add(actions);
        Grid.SetRow(actionRow, 3);
        root.Children.Add(actionRow);

        return root;
    }

    private void BindCadSetupInput(ComboBox setupInput)
    {
        setupInput.ItemsSource = cadExportSetupOptions;
        setupInput.SelectionChanged += (_, _) =>
        {
            ResetExportStatuses();
            UpdateExportState();
        };
        setupInput.SelectedIndex = cadExportSetupOptions.Count > 0 ? 0 : -1;
        setupInput.IsEnabled = cadExportSetupOptions.Count > 1;
    }

    private void LoadCadExportSetupOptions()
    {
        cadExportSetupOptions.Clear();
        foreach (PrintCadExportSetupOption option in cadExportSetupService.GetAvailableOptions(document, logger))
        {
            cadExportSetupOptions.Add(option);
        }
    }

    private void LoadSheets()
    {
        sheetRows.Clear();
        bool includePlaceholders = includePlaceholdersInput.IsChecked == true;
        IEnumerable<PrintSheetInfo> visibleSheets = includePlaceholders
            ? sheets
            : sheets.Where(sheet => !sheet.IsPlaceholder);

        foreach (PrintSheetInfo sheet in visibleSheets)
        {
            PrintSheetRow row = new(sheet);
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(PrintSheetRow.IsSelected))
                {
                    UpdateExportState();
                }
            };
            sheetRows.Add(row);
        }

        UpdateFileNamePreviews();
    }

    private void UpdateFileNamePreviews()
    {
        int counter = 1;
        foreach (PrintSheetRow row in sheetRows)
        {
            PrintFileNamePreview preview = fileNameTemplateService.Build(
                fileNameMaskInput.Text,
                row.Sheet,
                fileNameContext,
                counter);
            row.UpdateFileNamePreview(preview);
            row.ExportStatus = string.Empty;
            counter++;
        }

        HashSet<string> duplicatedNames = sheetRows
            .Where(row => row.CanBePrinted)
            .GroupBy(row => row.FileNamePreview, StringComparer.CurrentCultureIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        foreach (PrintSheetRow row in sheetRows)
        {
            row.IsFileNameDuplicate = row.CanBePrinted && duplicatedNames.Contains(row.FileNamePreview);
        }

        UpdateExportState();
    }

    private void SetAllSelected(bool isSelected)
    {
        foreach (PrintSheetRow row in sheetRows.Where(row => row.CanBePrinted))
        {
            row.IsSelected = isSelected;
        }

        logger.Info($"Print sheet selection changed: selected={isSelected}, rows={sheetRows.Count}.");
        UpdateExportState();
    }

    private void BrowseExportFolder()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Выберите папку экспорта",
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
        IReadOnlyList<PrintSheetRow> selectedRows = sheetRows
            .Where(row => row.IsSelected && row.CanBePrinted)
            .ToList();
        string formats = GetSelectedFormatsText();
        bool exportPdf = pdfInput.IsChecked == true;
        bool exportDwg = dwgInput.IsChecked == true;
        bool exportDxf = dxfInput.IsChecked == true;
        string? dwgSetupName = GetSelectedSetupName(dwgSetupInput);
        string? dxfSetupName = GetSelectedSetupName(dxfSetupInput);

        logger.Info($"Print export requested for {selectedRows.Count} sheets. Formats: {formats}. CAD setups: {GetSelectedCadSetupsText()}. Folder: {exportFolderInput.Text}. Mask: {fileNameMaskInput.Text}.");
        Dictionary<long, List<string>> rowStatuses = selectedRows.ToDictionary(
            row => row.Sheet.ElementId,
            _ => new List<string>());
        int exportedCount = 0;
        int failureCount = 0;
        List<string> failureMessages = new();
        foreach (PrintSheetRow row in selectedRows)
        {
            row.ExportStatus = "Экспорт: в очереди";
        }

        if (exportPdf)
        {
            PrintPdfExportResult result = pdfExportService.Export(
                document,
                exportFolderInput.Text,
                selectedRows
                    .Select(row => new PrintPdfExportItem(row.Sheet.ElementId, row.FileNamePreview))
                    .ToList(),
                logger);
            exportedCount += result.ExportedFiles.Count;
            failureCount += result.Failures.Count;
            ApplyPdfStatus(rowStatuses, result);
            failureMessages.AddRange(result.Failures.Select(failure => $"PDF {failure.Item.FileName}: {failure.Message}"));
        }

        if (exportDwg)
        {
            PrintCadExportResult result = cadExportService.Export(
                document,
                exportFolderInput.Text,
                selectedRows
                    .Select(row => new PrintCadExportItem(row.Sheet.ElementId, row.FileNamePreview))
                    .ToList(),
                PrintCadExportFormat.Dwg,
                dwgSetupName,
                logger);
            exportedCount += result.ExportedFiles.Count;
            failureCount += result.Failures.Count;
            ApplyCadStatus(rowStatuses, result);
            failureMessages.AddRange(result.Failures.Select(failure => $"{PrintCadExportService.GetDisplayName(failure.Format)} {failure.Item.FileName}: {failure.Message}"));
        }

        if (exportDxf)
        {
            PrintCadExportResult result = cadExportService.Export(
                document,
                exportFolderInput.Text,
                selectedRows
                    .Select(row => new PrintCadExportItem(row.Sheet.ElementId, row.FileNamePreview))
                    .ToList(),
                PrintCadExportFormat.Dxf,
                dxfSetupName,
                logger);
            exportedCount += result.ExportedFiles.Count;
            failureCount += result.Failures.Count;
            ApplyCadStatus(rowStatuses, result);
            failureMessages.AddRange(result.Failures.Select(failure => $"{PrintCadExportService.GetDisplayName(failure.Format)} {failure.Item.FileName}: {failure.Message}"));
        }

        foreach (PrintSheetRow row in selectedRows)
        {
            row.ExportStatus = rowStatuses.TryGetValue(row.Sheet.ElementId, out List<string>? statuses)
                ? string.Join(", ", statuses)
                : string.Empty;
        }

        string failureMessage = failureMessages.Count > 0
            ? "\n\nОшибки:\n" + string.Join("\n", failureMessages.Take(3))
            : string.Empty;
        Autodesk.Revit.UI.TaskDialog.Show(
            "Печать",
            $"Экспортировано файлов: {exportedCount}\nОшибок: {failureCount}{failureMessage}");
        UpdateExportState();
    }

    private static void ApplyPdfStatus(Dictionary<long, List<string>> rowStatuses, PrintPdfExportResult result)
    {
        HashSet<long> failedIds = result.Failures
            .Select(failure => failure.Item.ElementId)
            .ToHashSet();
        foreach (long elementId in rowStatuses.Keys.ToList())
        {
            rowStatuses[elementId].Add(failedIds.Contains(elementId) ? "PDF ошибка" : "PDF готов");
        }
    }

    private static void ApplyCadStatus(Dictionary<long, List<string>> rowStatuses, PrintCadExportResult result)
    {
        string formatName = PrintCadExportService.GetDisplayName(result.Format);
        HashSet<long> failedIds = result.Failures
            .Select(failure => failure.Item.ElementId)
            .ToHashSet();
        foreach (long elementId in rowStatuses.Keys.ToList())
        {
            rowStatuses[elementId].Add(failedIds.Contains(elementId) ? $"{formatName} ошибка" : $"{formatName} готов");
        }
    }

    private void ResetExportStatuses()
    {
        foreach (PrintSheetRow row in sheetRows)
        {
            row.ExportStatus = string.Empty;
        }
    }

    private void UpdateExportState()
    {
        int selectedCount = sheetRows.Count(row => row.IsSelected && row.CanBePrinted);
        int printableCount = sheetRows.Count(row => row.CanBePrinted);
        int hiddenPlaceholderCount = includePlaceholdersInput.IsChecked == true
            ? 0
            : sheets.Count(sheet => sheet.IsPlaceholder);
        int duplicateSelectedCount = sheetRows.Count(row => row.IsSelected && row.IsFileNameDuplicate);
        int truncatedSelectedCount = sheetRows.Count(row => row.IsSelected && row.IsFileNameTruncated);
        int unknownTokenCount = sheetRows.Count(row => row.HasUnknownFileNameTokens);
        bool hasFormat = pdfInput.IsChecked == true || dwgInput.IsChecked == true || dxfInput.IsChecked == true;
        bool hasFolder = !string.IsNullOrWhiteSpace(exportFolderInput.Text);

        exportButton.IsEnabled = selectedCount > 0 && hasFormat && hasFolder && duplicateSelectedCount == 0;
        exportButton.ToolTip = exportButton.IsEnabled
            ? "Подготовить выбранные листы к экспорту."
            : "Выберите листы, формат, папку назначения и устраните дубли имен.";

        string hiddenText = hiddenPlaceholderCount > 0
            ? $" Скрыто листов-заглушек: {hiddenPlaceholderCount}."
            : string.Empty;
        string duplicateText = duplicateSelectedCount > 0
            ? $" Дубли имен: {duplicateSelectedCount}."
            : string.Empty;
        string truncatedText = truncatedSelectedCount > 0
            ? $" Обрезанных имен: {truncatedSelectedCount}."
            : string.Empty;
        string unknownTokenText = unknownTokenCount > 0
            ? $" Неизвестные токены в маске: {unknownTokenCount}."
            : string.Empty;
        string cadSetupText = GetSelectedCadSetupsText();
        statusText.Text = $"Листов в таблице: {sheetRows.Count}. Печатаемых: {printableCount}. Выбрано: {selectedCount}. Форматы: {GetSelectedFormatsText()}.{cadSetupText}{hiddenText}{duplicateText}{truncatedText}{unknownTokenText}";
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

        if (dxfInput.IsChecked == true)
        {
            formats.Add("DXF");
        }

        return formats.Count == 0
            ? "не выбраны"
            : string.Join(", ", formats);
    }

    private string GetSelectedCadSetupsText()
    {
        List<string> setupDisplays = new();
        if (dwgInput.IsChecked == true)
        {
            setupDisplays.Add(PrintCadExportSetupService.GetSelectionDisplayName(
                PrintCadExportFormat.Dwg,
                dwgSetupInput.SelectedItem as PrintCadExportSetupOption));
        }

        if (dxfInput.IsChecked == true)
        {
            setupDisplays.Add(PrintCadExportSetupService.GetSelectionDisplayName(
                PrintCadExportFormat.Dxf,
                dxfSetupInput.SelectedItem as PrintCadExportSetupOption));
        }

        return setupDisplays.Count == 0
            ? string.Empty
            : $" CAD настройки: {string.Join("; ", setupDisplays)}.";
    }

    private static string? GetSelectedSetupName(ComboBox setupInput)
    {
        return setupInput.SelectedItem is PrintCadExportSetupOption option
            ? option.SetupName
            : null;
    }

    private string GetInitialExportFolder()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(document.PathName))
            {
                string? documentFolder = Path.GetDirectoryName(document.PathName);
                if (!string.IsNullOrWhiteSpace(documentFolder))
                {
                    return documentFolder;
                }
            }
        }
        catch (ArgumentException)
        {
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
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

    private static ComboBox CreateCadSetupInput(string tooltip)
    {
        return new ComboBox
        {
            DisplayMemberPath = nameof(PrintCadExportSetupOption.DisplayName),
            Height = 32,
            MinWidth = 220,
            ToolTip = tooltip
        };
    }

    private static Button CreateActionButton(string text, TrueBimIcon icon, bool isEnabled)
    {
        return new Button
        {
            Content = IconFactory.CreateButtonContent(icon, text),
            MinWidth = 110,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = isEnabled
        };
    }

    private static PrintFileNameContext CreateFileNameContext(RevitDocument document)
    {
        string documentName = string.IsNullOrWhiteSpace(document.Title)
            ? "Активный документ"
            : document.Title;

        try
        {
            return new PrintFileNameContext(
                documentName,
                document.ProjectInformation?.Name ?? string.Empty,
                document.ProjectInformation?.Number ?? string.Empty,
                DateTime.Now);
        }
        catch (Exception)
        {
            return new PrintFileNameContext(documentName, string.Empty, string.Empty, DateTime.Now);
        }
    }

    private sealed class PrintSheetRow : INotifyPropertyChanged
    {
        private bool isSelected;
        private string fileNamePreview = string.Empty;
        private string exportStatus = string.Empty;
        private bool isFileNameDuplicate;
        private bool isFileNameTruncated;
        private bool hasUnknownFileNameTokens;

        public PrintSheetRow(PrintSheetInfo sheet)
        {
            Sheet = sheet;
            isSelected = sheet.CanBePrinted;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public PrintSheetInfo Sheet { get; }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (!CanBePrinted)
                {
                    value = false;
                }

                if (isSelected == value)
                {
                    return;
                }

                isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public string SourceName => Sheet.SourceName;

        public string SheetNumber => Sheet.SheetNumber;

        public string SheetName => Sheet.SheetName;

        public string SheetFormat => Sheet.SheetFormat;

        public bool CanBePrinted => Sheet.CanBePrinted;

        public string FileNamePreview
        {
            get => fileNamePreview;
            private set
            {
                if (fileNamePreview == value)
                {
                    return;
                }

                fileNamePreview = value;
                NotifyChanged(nameof(FileNamePreview));
            }
        }

        public bool IsFileNameDuplicate
        {
            get => isFileNameDuplicate;
            set
            {
                if (isFileNameDuplicate == value)
                {
                    return;
                }

                isFileNameDuplicate = value;
                NotifyChanged(nameof(IsFileNameDuplicate));
                NotifyChanged(nameof(Status));
            }
        }

        public bool IsFileNameTruncated
        {
            get => isFileNameTruncated;
            private set
            {
                if (isFileNameTruncated == value)
                {
                    return;
                }

                isFileNameTruncated = value;
                NotifyChanged(nameof(IsFileNameTruncated));
                NotifyChanged(nameof(Status));
            }
        }

        public bool HasUnknownFileNameTokens
        {
            get => hasUnknownFileNameTokens;
            private set
            {
                if (hasUnknownFileNameTokens == value)
                {
                    return;
                }

                hasUnknownFileNameTokens = value;
                NotifyChanged(nameof(HasUnknownFileNameTokens));
                NotifyChanged(nameof(Status));
            }
        }

        public string ExportStatus
        {
            get => exportStatus;
            set
            {
                if (exportStatus == value)
                {
                    return;
                }

                exportStatus = value;
                NotifyChanged(nameof(ExportStatus));
                NotifyChanged(nameof(Status));
            }
        }

        public string Status
        {
            get
            {
                if (Sheet.IsPlaceholder)
                {
                    return "Лист-заглушка";
                }

                if (IsFileNameDuplicate)
                {
                    return "Дубликат имени";
                }

                if (HasUnknownFileNameTokens)
                {
                    return "Неизвестный токен";
                }

                if (IsFileNameTruncated)
                {
                    return "Имя обрезано";
                }

                if (!string.IsNullOrWhiteSpace(ExportStatus))
                {
                    return ExportStatus;
                }

                return Sheet.CanBePrinted
                    ? "Готов"
                    : "Не печатается";
            }
        }

        public void UpdateFileNamePreview(PrintFileNamePreview preview)
        {
            FileNamePreview = preview.FileName;
            IsFileNameTruncated = preview.WasTruncated;
            HasUnknownFileNameTokens = preview.HasUnknownTokens;
        }

        private void NotifyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
