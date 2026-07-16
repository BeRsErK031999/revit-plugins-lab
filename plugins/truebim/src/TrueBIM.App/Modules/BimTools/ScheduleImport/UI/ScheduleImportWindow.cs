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
using TrueBIM.App.UI.DesignSystem;
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
    private readonly ObservableCollection<TableItem> tables = new();
    private readonly ObservableCollection<string> warnings = new();
    private readonly TextBox filePathInput = new();
    private readonly ComboBox tableInput = new();
    private readonly TextBox scaleInput = new() { Text = "1.0" };
    private readonly CheckBox createViewInput = new()
    {
        Content = "Создать чертёжный вид при необходимости",
        IsChecked = true
    };
    private readonly DataGrid previewGrid = new();
    private readonly ListBox warningList = new();
    private readonly TextBlock statusText = new();
    private readonly Button recognizeButton = CreateButton("Распознать", TrueBimIcon.Preview, 130);
    private readonly Button validateButton = CreateButton("Проверить", TrueBimIcon.Apply, 120);
    private readonly Button createButton = CreateButton("Создать таблицу", TrueBimIcon.ScheduleImport, 170);
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
        InitializeTableInput();
        AddWarnings(context.Warnings);
        UpdateStatus("Выберите PDF или DWG и распознайте визуальную таблицу для размещения в Revit.");
        logger.Info($"Schedule Import window opened for '{context.DocumentTitle}', active view '{context.ActiveViewName}'.");
    }

    private UIElement CreateContent()
    {
        WpfGrid body = new();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        UIElement top = CreateTopPanel();
        body.Children.Add(top);

        statusText.Foreground = TrueBimBrushes.TextSecondary;
        statusText.TextWrapping = TextWrapping.Wrap;

        TabControl tabs = new()
        {
            Style = TrueBimStyles.CreateTabControlStyle()
        };
        tabs.Items.Add(new TabItem
        {
            Header = "Предпросмотр",
            Content = CreatePreviewPanel()
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Предупреждения",
            Content = CreateWarningPanel()
        });
        WpfGrid.SetRow(tabs, 1);
        body.Children.Add(tabs);

        Button closeButton = TrueBimUi.CreateSecondaryButton("Закрыть", TrueBimIcon.Close, minWidth: 110);
        closeButton.IsCancel = true;
        closeButton.Click += (_, _) => Close();

        return BuildShell(
            header: TrueBimUi.CreateHeader(
                Title,
                $"Документ: {context.DocumentTitle}. Активный вид: {context.ActiveViewName} ({context.ActiveViewKind}).",
                TrueBimIcon.ScheduleImport),
            commandBar: null,
            body: body,
            status: null,
            footer: TrueBimUi.CreateFooter(statusText, closeButton));
    }

    private UIElement CreateTopPanel()
    {
        WpfGrid root = new()
        {
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12)
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
            Foreground = TrueBimBrushes.TextSecondary,
            Margin = new Thickness(TrueBimTheme.Spacing8, 0, TrueBimTheme.Spacing12, TrueBimTheme.Spacing8)
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
        WpfGrid.SetColumn(topActions, 2);
        root.Children.Add(topActions);

        AddLabel(root, "Файл", 0, 1);
        filePathInput.MinHeight = TrueBimTheme.ControlHeight32;
        filePathInput.Style = TrueBimStyles.CreateTextBoxStyle();
        filePathInput.Margin = new Thickness(TrueBimTheme.Spacing8, 0, TrueBimTheme.Spacing12, TrueBimTheme.Spacing8);
        filePathInput.IsReadOnly = true;
        filePathInput.ToolTip = "PDF и DWG распознаются встроенными парсерами TrueBIM без внешнего Python worker. JSON остаётся доступен как промежуточный формат.";
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

        AddLabel(root, "Размещение", 0, 2);
        StackPanel options = new()
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(TrueBimTheme.Spacing8, 0, TrueBimTheme.Spacing12, 0)
        };
        options.Children.Add(new TextBlock
        {
            Text = "Чертёжная таблица",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = TrueBimBrushes.TextPrimary,
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing16, 0)
        });
        createViewInput.Style = TrueBimStyles.CreateCheckBoxStyle();
        createViewInput.Margin = new Thickness(0, 0, TrueBimTheme.Spacing16, 0);
        createViewInput.ToolTip = "Если активный вид не поддерживает DetailCurve и TextNote, TrueBIM создаст новый чертёжный вид.";
        createViewInput.Checked += (_, _) => ValidateCurrentTable(showDialog: false);
        createViewInput.Unchecked += (_, _) => ValidateCurrentTable(showDialog: false);
        options.Children.Add(createViewInput);
        options.Children.Add(new TextBlock
        {
            Text = "Размер",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = TrueBimBrushes.TextSecondary,
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, 0)
        });
        scaleInput.Width = 70;
        scaleInput.MinHeight = TrueBimTheme.ControlHeight32;
        scaleInput.Style = TrueBimStyles.CreateTextBoxStyle();
        scaleInput.ToolTip = "Множитель ширины колонок, высоты строк и размера текста. Обычно оставьте 1.0.";
        scaleInput.TextChanged += (_, _) => UpdateStatus();
        options.Children.Add(scaleInput);
        WpfGrid.SetRow(options, 2);
        WpfGrid.SetColumn(options, 1);
        root.Children.Add(options);

        tableInput.MinHeight = TrueBimTheme.ControlHeight32;
        tableInput.MinWidth = 230;
        tableInput.Style = TrueBimStyles.CreateComboBoxStyle();
        tableInput.DisplayMemberPath = nameof(TableItem.DisplayName);
        tableInput.ToolTip = "Распознанная таблица или страница исходного файла.";
        tableInput.SelectionChanged += (_, _) => ShowSelectedTable();
        WpfGrid.SetRow(tableInput, 2);
        WpfGrid.SetColumn(tableInput, 2);
        root.Children.Add(tableInput);

        return root;
    }

    private UIElement CreatePreviewPanel()
    {
        DockPanel panel = new();
        previewGrid.Style = TrueBimStyles.CreateDataGridStyle();
        previewGrid.AutoGenerateColumns = true;
        previewGrid.CanUserAddRows = false;
        previewGrid.CanUserDeleteRows = false;
        previewGrid.IsReadOnly = true;
        panel.Children.Add(previewGrid);
        return panel;
    }

    private UIElement CreateWarningPanel()
    {
        warningList.ItemsSource = warnings;
        warningList.Style = TrueBimStyles.CreateListBoxStyle();
        return warningList;
    }

    private void InitializeTableInput()
    {
        tableInput.ItemsSource = tables;
    }

    private void SelectFile()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Выбрать PDF или DWG со спецификацией",
            Filter = "PDF или DWG (*.pdf;*.dwg)|*.pdf;*.dwg|PDF (*.pdf)|*.pdf|DWG (*.dwg)|*.dwg|JSON (*.json)|*.json",
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
            UpdateStatus(CreateRecognitionStatus(result));
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
            createButton.IsEnabled = false;
            validateButton.IsEnabled = false;
            return;
        }

        previewGrid.ItemsSource = BuildPreviewTable(table).DefaultView;
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
        bool canPlace = context.CanUseDraftingTableMode || createViewInput.IsChecked == true;
        bool canCreate = validation.Succeeded && canPlace;
        createButton.IsEnabled = canCreate;
        validateButton.IsEnabled = true;
        UpdateStatus(validation.Succeeded && canPlace
            ? $"Проверка пройдена. Строк: {table.RowCount}. Колонок: {table.ColumnCount}."
            : validation.Succeeded
                ? "Активный вид не подходит; включите создание чертёжного вида."
                : "Проверка нашла ошибки.");

        if (showDialog)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                DialogTitle,
                CreateValidationDialogText(validation));
        }
    }

    private string CreateValidationDialogText(ParsedTableValidationResult validation)
    {
        if (!validation.Succeeded)
        {
            return string.Join(Environment.NewLine, validation.Errors);
        }

        if (!context.CanUseDraftingTableMode && createViewInput.IsChecked != true)
        {
            return "Активный вид не поддерживает визуальную таблицу. Включите создание нового чертёжного вида.";
        }

        return context.CanUseDraftingTableMode
            ? $"Таблица готова к размещению на активном виде «{context.ActiveViewName}»."
            : "Таблица готова к размещению на новом чертёжном виде.";
    }

    private void CreateInRevit()
    {
        ParsedTable? table = SelectedTable;
        if (table is null)
        {
            Autodesk.Revit.UI.TaskDialog.Show(DialogTitle, "Сначала распознайте или загрузите таблицу.");
            return;
        }

        if (!context.CanUseDraftingTableMode && createViewInput.IsChecked != true)
        {
            Autodesk.Revit.UI.TaskDialog.Show(DialogTitle, "Активный вид не подходит. Включите создание нового чертёжного вида.");
            return;
        }

        MessageBoxResult decision = MessageBox.Show(
            this,
            $"Создать визуальную таблицу с линиями и текстом: {table.RowCount} x {table.ColumnCount}?\n\n" +
            (context.CanUseDraftingTableMode
                ? $"Таблица будет размещена на активном виде «{context.ActiveViewName}»."
                : "Для таблицы будет создан новый чертёжный вид."),
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
                ScheduleImportCreationOptionsFactory.Create(
                    context.ActiveViewId,
                    createViewInput.IsChecked == true,
                    ParseScale(scaleInput.Text))),
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

    private void UpdateStatus(string? prefix = null)
    {
        ParsedTable? table = SelectedTable;
        string placement = context.CanUseDraftingTableMode
            ? $"активный вид «{context.ActiveViewName}»"
            : createViewInput.IsChecked == true
                ? "новый чертёжный вид"
                : "размещение недоступно";
        string tableText = table is null
            ? "Таблица не выбрана."
            : $"Таблица: {table.RowCount} x {table.ColumnCount}.";
        statusText.Text = string.IsNullOrWhiteSpace(prefix)
            ? $"{tableText} Размещение: {placement}. Предупреждений: {warnings.Count}."
            : $"{prefix} {tableText} Размещение: {placement}. Предупреждений: {warnings.Count}.";
        if (table is not null)
        {
            ParsedTableValidationResult validation = validationService.Validate(table);
            bool canPlace = context.CanUseDraftingTableMode || createViewInput.IsChecked == true;
            createButton.IsEnabled = !isApplying && validation.Succeeded && canPlace;
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

    private static string CreateRecognitionStatus(PdfParserResult result)
    {
        if (result.Tables.Count > 0)
        {
            return $"Найдено таблиц: {result.Tables.Count}.";
        }

        return result.Errors.FirstOrDefault()
            ?? result.Warnings.FirstOrDefault()
            ?? "Таблицы не найдены.";
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
            Foreground = TrueBimBrushes.TextSecondary,
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, TrueBimTheme.Spacing8)
        };
        WpfGrid.SetColumn(label, column);
        WpfGrid.SetRow(label, row);
        grid.Children.Add(label);
    }

    private static Button CreateButton(string text, TrueBimIcon icon, double minWidth)
    {
        Button button = icon == TrueBimIcon.ScheduleImport
            ? TrueBimUi.CreatePrimaryButton(text, icon, minWidth: minWidth)
            : TrueBimUi.CreateSecondaryButton(text, icon, minWidth: minWidth);
        button.Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, 0);
        return button;
    }

    private sealed record TableItem(int Index, ParsedTable Table)
    {
        public string DisplayName => $"Страница {Table.PageNumber}: {Table.RowCount} x {Table.ColumnCount}";
    }
}
