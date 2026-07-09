using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using WpfBinding = System.Windows.Data.Binding;
using WpfGrid = System.Windows.Controls.Grid;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.UI;

public sealed class ScheduleImportWindow : TrueBimWindow
{
    private const string DialogTitle = "Импорт таблиц";

    private readonly ScheduleImportContext context;
    private readonly IPdfTableParser parser;
    private readonly ITrueBimLogger logger;
    private readonly Action<ScheduleImportRequest, Action<DraftingTableCreationResult>, Action<Exception>> createTable;
    private readonly ParsedTableValidationService validationService = new();
    private readonly ParameterMappingService mappingService = new();
    private readonly ObservableCollection<TableItem> tables = new();
    private readonly ObservableCollection<ColumnMapping> mappings = new();
    private readonly ObservableCollection<string> warnings = new();
    private readonly TextBox filePathInput = new();
    private readonly ComboBox tableInput = new();
    private readonly ComboBox modeInput = new();
    private readonly TextBox scaleInput = new() { Text = "1.0" };
    private readonly CheckBox createViewInput = new()
    {
        Content = "Создать чертёжный вид при необходимости",
        IsChecked = true,
        ToolTip = "Если активный вид не принимает DetailCurve/TextNote, таблица будет создана на новом Drafting View."
    };
    private readonly DataGrid previewGrid = new();
    private readonly DataGrid mappingGrid = new();
    private readonly ListBox warningList = new();
    private readonly TextBlock statusText = new();
    private readonly Button recognizeButton = CreateButton("Распознать", TrueBimIcon.Preview, 130);
    private readonly Button validateButton = CreateButton("Проверить", TrueBimIcon.Apply, 120);
    private readonly Button createButton = CreateButton("Создать в Revit", TrueBimIcon.ScheduleImport, 150);
    private bool isApplying;

    public ScheduleImportWindow(
        ScheduleImportContext context,
        IPdfTableParser parser,
        ITrueBimLogger logger,
        Action<ScheduleImportRequest, Action<DraftingTableCreationResult>, Action<Exception>> createTable)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.parser = parser ?? throw new ArgumentNullException(nameof(parser));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.createTable = createTable ?? throw new ArgumentNullException(nameof(createTable));

        Title = DialogTitle;
        Icon = IconFactory.CreateImage(TrueBimIcon.ScheduleImport, 32);
        Width = 1180;
        Height = 760;
        MinWidth = 1040;
        MinHeight = 640;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AllowDrop = true;
        DragEnter += OnDragEnter;
        Drop += OnDrop;

        Content = CreateContent();
        InitializeModeInput();
        InitializeTableInput();
        AddWarnings(context.Warnings);
        UpdateStatus("Выберите PDF или JSON-модель таблицы.");
        logger.Info($"Schedule Import window opened for '{context.DocumentTitle}', active view '{context.ActiveViewName}'.");
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
            Header = "Предпросмотр",
            Content = CreatePreviewPanel()
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Колонки",
            Content = CreateMappingGrid()
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Предупреждения",
            Content = CreateWarningPanel()
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

        AddLabel(root, $"Документ: {context.DocumentTitle}", 0, 0);
        TextBlock activeViewText = new()
        {
            Text = $"Активный вид: {context.ActiveViewName} ({context.ActiveViewKind})",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 12, 8)
        };
        WpfGrid.SetColumn(activeViewText, 1);
        root.Children.Add(activeViewText);

        StackPanel topActions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        validateButton.Click += (_, _) => ValidateCurrentTable();
        topActions.Children.Add(validateButton);
        createButton.Click += (_, _) => CreateInRevit();
        topActions.Children.Add(createButton);
        Button closeButton = CreateButton("Закрыть", TrueBimIcon.Close, 110);
        closeButton.IsCancel = true;
        closeButton.Click += (_, _) => Close();
        topActions.Children.Add(closeButton);
        WpfGrid.SetColumn(topActions, 2);
        root.Children.Add(topActions);

        AddLabel(root, "Файл", 0, 1);
        filePathInput.Height = 32;
        filePathInput.Margin = new Thickness(8, 0, 12, 8);
        filePathInput.IsReadOnly = true;
        filePathInput.ToolTip = "PDF распознаётся локальным worker, JSON загружает промежуточную модель таблицы.";
        WpfGrid.SetRow(filePathInput, 1);
        WpfGrid.SetColumn(filePathInput, 1);
        root.Children.Add(filePathInput);

        StackPanel fileActions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Button selectFileButton = CreateButton("Выбрать файл", TrueBimIcon.Open, 130);
        selectFileButton.Click += (_, _) => SelectFile();
        fileActions.Children.Add(selectFileButton);
        recognizeButton.Click += async (_, _) => await RecognizeAsync();
        fileActions.Children.Add(recognizeButton);
        WpfGrid.SetRow(fileActions, 1);
        WpfGrid.SetColumn(fileActions, 2);
        root.Children.Add(fileActions);

        AddLabel(root, "Режим", 0, 2);
        StackPanel options = new()
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 12, 0)
        };
        modeInput.Width = 230;
        modeInput.Height = 32;
        modeInput.Margin = new Thickness(0, 0, 12, 0);
        modeInput.SelectionChanged += (_, _) =>
        {
            if (SelectedTable is null)
            {
                UpdateStatus();
                return;
            }

            ValidateCurrentTable(showDialog: false);
        };
        options.Children.Add(modeInput);
        createViewInput.Margin = new Thickness(0, 0, 16, 0);
        options.Children.Add(createViewInput);
        options.Children.Add(new TextBlock
        {
            Text = "Масштаб",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });
        scaleInput.Width = 70;
        scaleInput.Height = 32;
        scaleInput.ToolTip = "Множитель размера визуальной таблицы. 1.0 подходит для первого теста.";
        scaleInput.TextChanged += (_, _) => UpdateStatus();
        options.Children.Add(scaleInput);
        WpfGrid.SetRow(options, 2);
        WpfGrid.SetColumn(options, 1);
        root.Children.Add(options);

        tableInput.Height = 32;
        tableInput.MinWidth = 230;
        tableInput.DisplayMemberPath = nameof(TableItem.DisplayName);
        tableInput.SelectionChanged += (_, _) => ShowSelectedTable();
        WpfGrid.SetRow(tableInput, 2);
        WpfGrid.SetColumn(tableInput, 2);
        root.Children.Add(tableInput);

        return root;
    }

    private UIElement CreatePreviewPanel()
    {
        DockPanel panel = new();
        previewGrid.AutoGenerateColumns = true;
        previewGrid.CanUserAddRows = false;
        previewGrid.CanUserDeleteRows = false;
        previewGrid.IsReadOnly = true;
        panel.Children.Add(previewGrid);
        return panel;
    }

    private DataGrid CreateMappingGrid()
    {
        mappingGrid.AutoGenerateColumns = false;
        mappingGrid.CanUserAddRows = false;
        mappingGrid.CanUserDeleteRows = false;
        mappingGrid.IsReadOnly = true;
        mappingGrid.ItemsSource = mappings;
        mappingGrid.Columns.Add(CreateTextColumn("Колонка PDF/JSON", nameof(ColumnMapping.SourceColumnName), new DataGridLength(1, DataGridLengthUnitType.Star)));
        mappingGrid.Columns.Add(CreateTextColumn("Параметр Revit", nameof(ColumnMapping.TargetRevitParameterName), new DataGridLength(220)));
        mappingGrid.Columns.Add(CreateTextColumn("Тип данных", nameof(ColumnMapping.DataType), 120));
        mappingGrid.Columns.Add(CreateTextColumn("Ед. исходная", nameof(ColumnMapping.UnitSource), 120));
        mappingGrid.Columns.Add(CreateTextColumn("Обязательная", nameof(ColumnMapping.IsRequired), 110));
        return mappingGrid;
    }

    private UIElement CreateWarningPanel()
    {
        warningList.ItemsSource = warnings;
        return warningList;
    }

    private void InitializeModeInput()
    {
        modeInput.ItemsSource = new[]
        {
            new ModeItem(ScheduleImportMode.DraftingTable, "Drafting Table"),
            new ModeItem(ScheduleImportMode.PreviewOnly, "Preview Only"),
            new ModeItem(ScheduleImportMode.BimSchedule, "BIM Schedule"),
            new ModeItem(ScheduleImportMode.KeySchedule, "Key Schedule")
        };
        modeInput.DisplayMemberPath = nameof(ModeItem.DisplayName);
        modeInput.SelectedIndex = 0;
    }

    private void InitializeTableInput()
    {
        tableInput.ItemsSource = tables;
    }

    private void SelectFile()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Выбрать таблицу PDF/JSON",
            Filter = "PDF, DWG или JSON (*.pdf;*.dwg;*.json)|*.pdf;*.dwg;*.json|PDF (*.pdf)|*.pdf|JSON (*.json)|*.json|DWG (*.dwg)|*.dwg",
            InitialDirectory = Directory.Exists(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : null
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        filePathInput.Text = dialog.FileName;
        UpdateStatus("Файл выбран. Нажмите «Распознать».");
    }

    private async Task RecognizeAsync()
    {
        string filePath = filePathInput.Text.Trim();
        recognizeButton.IsEnabled = false;
        try
        {
            PdfParserResult result = await parser.ParseAsync(filePath, CancellationToken.None);
            tables.Clear();
            mappings.Clear();
            warnings.Clear();
            AddWarnings(context.Warnings);
            AddWarnings(result.Warnings);
            AddWarnings(result.Errors);

            for (int index = 0; index < result.Tables.Count; index++)
            {
                tables.Add(new TableItem(index, result.Tables[index]));
            }

            tableInput.SelectedIndex = tables.Count > 0 ? 0 : -1;
            ShowSelectedTable();
            UpdateStatus(tables.Count > 0
                ? $"Найдено таблиц: {tables.Count}."
                : "Таблицы не найдены.");
        }
        catch (Exception exception)
        {
            logger.Error("Failed to parse schedule import source.", exception);
            AddWarnings([exception.Message]);
            UpdateStatus("Не удалось распознать таблицу.");
        }
        finally
        {
            recognizeButton.IsEnabled = true;
        }
    }

    private void ShowSelectedTable()
    {
        ParsedTable? table = SelectedTable;
        if (table is null)
        {
            previewGrid.ItemsSource = null;
            mappings.Clear();
            createButton.IsEnabled = false;
            validateButton.IsEnabled = false;
            return;
        }

        previewGrid.ItemsSource = BuildPreviewTable(table).DefaultView;
        mappings.Clear();
        foreach (ColumnMapping mapping in mappingService.SuggestMappings(table, context.AvailableBimScheduleParameterNames))
        {
            mappings.Add(mapping);
        }

        AddWarnings(table.Warnings);
        ValidateCurrentTable(showDialog: false);
    }

    private void ValidateCurrentTable(bool showDialog = true)
    {
        ParsedTable? table = SelectedTable;
        if (table is null)
        {
            createButton.IsEnabled = false;
            validateButton.IsEnabled = false;
            return;
        }

        ParsedTableValidationResult validation = validationService.Validate(table);
        AddWarnings(validation.Warnings);
        AddWarnings(validation.Errors);
        ScheduleImportMode mode = SelectedMode;
        if (mode is ScheduleImportMode.BimSchedule)
        {
            AddWarnings(context.CanUseBimScheduleMode
                ? [$"BIM Schedule Mode: доступен read-only предпросмотр сопоставления с активной ViewSchedule. Полей найдено: {context.AvailableBimScheduleParameterNames.Count}. Запись в параметры будет отдельным этапом."]
                : ["BIM Schedule Mode требует активную ViewSchedule. Для текущего вида используйте Drafting Table Mode."]);
        }
        else if (mode is ScheduleImportMode.KeySchedule)
        {
            AddWarnings(["Key Schedule Mode пока экспериментальный и не создаёт строки в этом MVP."]);
        }

        bool canCreate = validation.Succeeded && mode == ScheduleImportMode.DraftingTable;
        createButton.IsEnabled = canCreate;
        validateButton.IsEnabled = true;
        UpdateStatus(validation.Succeeded
            ? $"Проверка пройдена. Строк: {table.RowCount}. Колонок: {table.ColumnCount}."
            : "Проверка нашла ошибки.");

        if (showDialog)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                DialogTitle,
                validation.Succeeded
                    ? "Таблица готова к Drafting Table Mode."
                    : string.Join(Environment.NewLine, validation.Errors));
        }
    }

    private void CreateInRevit()
    {
        ParsedTable? table = SelectedTable;
        if (table is null)
        {
            Autodesk.Revit.UI.TaskDialog.Show(DialogTitle, "Сначала распознайте или загрузите таблицу.");
            return;
        }

        if (SelectedMode != ScheduleImportMode.DraftingTable)
        {
            Autodesk.Revit.UI.TaskDialog.Show(DialogTitle, "В первом рабочем срезе создание доступно только для Drafting Table Mode.");
            return;
        }

        MessageBoxResult decision = MessageBox.Show(
            this,
            $"Создать визуальную таблицу с линиями и текстом: {table.RowCount} x {table.ColumnCount}?",
            DialogTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        isApplying = true;
        createButton.IsEnabled = false;
        UpdateStatus("Запрос передан в Revit через ExternalEvent.");
        createTable(
            new ScheduleImportRequest(
                table,
                new ImportOptions(
                    ScheduleImportMode.DraftingTable,
                    TargetViewId: context.ActiveViewId,
                    CreateNewViewIfNeeded: createViewInput.IsChecked == true,
                    TableScale: ParseScale(scaleInput.Text),
                    DryRun: false)),
            OnCreated,
            OnFailed);
    }

    private void OnCreated(DraftingTableCreationResult result)
    {
        isApplying = false;
        AddWarnings(result.Warnings);
        AddWarnings(result.Errors);
        createButton.IsEnabled = result.Errors.Count > 0;
        UpdateStatus(result.Succeeded
            ? $"Создана визуальная таблица на виде «{result.TargetViewName}»."
            : "Создание таблицы завершилось ошибкой.");
        Autodesk.Revit.UI.TaskDialog.Show(DialogTitle, result.ToDialogText());
    }

    private void OnFailed(Exception exception)
    {
        isApplying = false;
        logger.Error("Failed to create schedule import drafting table.", exception);
        createButton.IsEnabled = true;
        AddWarnings([exception.Message]);
        UpdateStatus("Не удалось создать таблицу в Revit.");
        Autodesk.Revit.UI.TaskDialog.Show(DialogTitle, "Не удалось создать таблицу. Используйте логи для диагностики.");
    }

    private void OnDragEnter(object sender, DragEventArgs args)
    {
        args.Effects = args.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        args.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs args)
    {
        if (!args.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (args.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            filePathInput.Text = files[0];
            UpdateStatus("Файл добавлен. Нажмите «Распознать».");
        }
    }

    private ParsedTable? SelectedTable => tableInput.SelectedItem is TableItem item ? item.Table : null;

    private ScheduleImportMode SelectedMode => modeInput.SelectedItem is ModeItem item
        ? item.Mode
        : ScheduleImportMode.DraftingTable;

    private void UpdateStatus(string? prefix = null)
    {
        ParsedTable? table = SelectedTable;
        string mode = modeInput.SelectedItem is ModeItem item ? item.DisplayName : "Drafting Table";
        string tableText = table is null
            ? "Таблица не выбрана."
            : $"Таблица: {table.RowCount} x {table.ColumnCount}.";
        statusText.Text = string.IsNullOrWhiteSpace(prefix)
            ? $"{tableText} Режим: {mode}. Предупреждений: {warnings.Count}."
            : $"{prefix} {tableText} Режим: {mode}. Предупреждений: {warnings.Count}.";
        if (table is not null)
        {
            ParsedTableValidationResult validation = validationService.Validate(table);
            createButton.IsEnabled = !isApplying && validation.Succeeded && SelectedMode == ScheduleImportMode.DraftingTable;
            validateButton.IsEnabled = true;
        }
        else
        {
            createButton.IsEnabled = false;
            validateButton.IsEnabled = false;
        }
    }

    private void AddWarnings(IEnumerable<string> items)
    {
        foreach (string item in items.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            if (!warnings.Contains(item))
            {
                warnings.Add(item);
            }
        }
    }

    private static DataTable BuildPreviewTable(ParsedTable table)
    {
        DataTable dataTable = new();
        IReadOnlyList<string> columns = table.Columns.Count > 0
            ? table.Columns
            : Enumerable.Range(1, table.ColumnCount).Select(index => $"Колонка {index}").ToList();
        HashSet<string> columnNames = new(StringComparer.Ordinal);
        for (int index = 0; index < columns.Count; index++)
        {
            string name = string.IsNullOrWhiteSpace(columns[index]) ? $"Колонка {index + 1}" : columns[index];
            string uniqueName = name;
            int suffix = 2;
            while (!columnNames.Add(uniqueName))
            {
                uniqueName = $"{name} {suffix}";
                suffix++;
            }

            dataTable.Columns.Add(uniqueName);
        }

        foreach (ParsedRow row in table.Rows)
        {
            DataRow dataRow = dataTable.NewRow();
            for (int columnIndex = 0; columnIndex < dataTable.Columns.Count; columnIndex++)
            {
                dataRow[columnIndex] = columnIndex < row.Values.Count ? row.Values[columnIndex] : string.Empty;
            }

            dataTable.Rows.Add(dataRow);
        }

        return dataTable;
    }

    private static double ParseScale(string text)
    {
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out double value)
            && !double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return 1;
        }

        if (value < 0.2)
        {
            return 0.2;
        }

        return value > 4 ? 4 : value;
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

    private sealed record TableItem(int Index, ParsedTable Table)
    {
        public string DisplayName => $"Страница {Table.PageNumber}: {Table.RowCount} x {Table.ColumnCount}";
    }

    private sealed record ModeItem(ScheduleImportMode Mode, string DisplayName);
}
