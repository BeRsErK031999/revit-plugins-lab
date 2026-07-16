using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
    private readonly Action<long, Action<ScheduleFieldCatalogResult>, Action<Exception>> loadFields;
    private readonly Action<ScheduleImportRequest, Action<ScheduleImportCreationResult>, Action<Exception>> executeSchedule;
    private readonly ParsedTableValidationService tableValidationService = new();
    private readonly ScheduleMappingConfigurationService mappingConfigurationService = new();
    private readonly ParameterMappingService parameterMappingService = new();
    private readonly ObservableCollection<TableItem> tables = new();
    private readonly ObservableCollection<ScheduleFieldOption> fieldOptions = new();
    private readonly ObservableCollection<ScheduleMappingRow> mappingRows = new();
    private readonly ObservableCollection<string> warnings = new();
    private readonly TextBox filePathInput = new();
    private readonly ComboBox tableInput = new();
    private readonly ComboBox categoryInput = new();
    private readonly DataGrid pdfPreviewGrid = new();
    private readonly DataGrid mappingGrid = new();
    private readonly DataGrid schedulePreviewGrid = new();
    private readonly ListBox warningList = new();
    private readonly TextBlock previewSummaryText = new();
    private readonly TextBlock statusText = new();
    private readonly TabControl tabs = new();
    private readonly Button recognizeButton = CreateButton("Распознать", TrueBimIcon.Preview, 130);
    private readonly Button validateButton = CreateButton("Проверить PDF", TrueBimIcon.Apply, 130);
    private readonly Button previewButton = CreateButton("Предпросмотр Revit", TrueBimIcon.Preview, 175);
    private readonly Button createButton = CreateButton("Создать спецификацию", TrueBimIcon.ScheduleImport, 190);
    private TabItem? schedulePreviewTab;
    private bool isApplying;
    private bool isLoadingFields;
    private bool isRecognizing;
    private string? previewFingerprint;

    public ScheduleImportWindow(
        ScheduleImportContext context,
        IPdfTableParser parser,
        ITrueBimLogger logger,
        Action<long, Action<ScheduleFieldCatalogResult>, Action<Exception>> loadFields,
        Action<ScheduleImportRequest, Action<ScheduleImportCreationResult>, Action<Exception>> executeSchedule)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.parser = parser ?? throw new ArgumentNullException(nameof(parser));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.loadFields = loadFields ?? throw new ArgumentNullException(nameof(loadFields));
        this.executeSchedule = executeSchedule ?? throw new ArgumentNullException(nameof(executeSchedule));

        Title = DialogTitle;
        Icon = IconFactory.CreateImage(TrueBimIcon.ScheduleImport, 32);
        Width = 1380;
        Height = 820;
        MinWidth = 1120;
        MinHeight = 680;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AllowDrop = true;
        DragEnter += OnDragEnter;
        Drop += OnDrop;

        Content = CreateContent();
        ConfigureActionHelp();
        InitializeInputs();
        AddWarnings(context.Warnings);
        UpdateStatus("Распознайте PDF, выберите категорию и сопоставьте колонки с реальными полями Revit.");
        logger.Info($"Schedule Import window opened for '{context.DocumentTitle}', active view '{context.ActiveViewName}'.");
    }

    private UIElement CreateContent()
    {
        WpfGrid body = new();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(CreateTopPanel());

        statusText.Foreground = TrueBimBrushes.TextSecondary;
        statusText.TextWrapping = TextWrapping.Wrap;
        tabs.Style = TrueBimStyles.CreateTabControlStyle();
        tabs.Items.Add(new TabItem
        {
            Header = "Таблица PDF",
            Content = CreatePdfPreviewPanel()
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Поля и условия",
            Content = CreateMappingPanel()
        });
        schedulePreviewTab = new TabItem
        {
            Header = "Предпросмотр Revit",
            Content = CreateSchedulePreviewPanel()
        };
        tabs.Items.Add(schedulePreviewTab);
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
        for (int index = 0; index < 4; index++)
        {
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

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
        previewButton.Click += (_, _) => RunSchedulePreview();
        topActions.Children.Add(previewButton);
        createButton.Click += (_, _) => CreateInRevit();
        topActions.Children.Add(createButton);
        WpfGrid.SetColumn(topActions, 2);
        root.Children.Add(topActions);

        AddLabel(root, "Файл", 0, 1);
        ConfigureTextBox(filePathInput);
        filePathInput.IsReadOnly = true;
        filePathInput.ToolTip = "PDF и DWG распознаются встроенными парсерами TrueBIM. JSON доступен как диагностический формат.";
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

        AddLabel(root, "Таблица", 0, 2);
        ConfigureComboBox(tableInput);
        tableInput.DisplayMemberPath = nameof(TableItem.DisplayName);
        tableInput.ToolTip = "Распознанная таблица или страница исходного файла.";
        tableInput.SelectionChanged += (_, _) => ShowSelectedTable();
        WpfGrid.SetRow(tableInput, 2);
        WpfGrid.SetColumn(tableInput, 1);
        root.Children.Add(tableInput);

        TextBlock tableHint = new()
        {
            Text = "PDF задаёт структуру, а не значения строк",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = TrueBimBrushes.TextSecondary,
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, TrueBimTheme.Spacing8)
        };
        WpfGrid.SetRow(tableHint, 2);
        WpfGrid.SetColumn(tableHint, 2);
        root.Children.Add(tableHint);

        AddLabel(root, "Категория Revit", 0, 3);
        ConfigureComboBox(categoryInput);
        categoryInput.DisplayMemberPath = nameof(ScheduleCategoryOption.DisplayName);
        categoryInput.IsTextSearchEnabled = true;
        categoryInput.ToolTip = "Категория определяет реальные элементы и набор доступных полей новой спецификации.";
        categoryInput.SelectionChanged += (_, _) => LoadSelectedCategoryFields();
        WpfGrid.SetRow(categoryInput, 3);
        WpfGrid.SetColumn(categoryInput, 1);
        root.Children.Add(categoryInput);

        TextBlock filterHint = new()
        {
            Text = "Все заданные условия применяются одновременно (AND)",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = TrueBimBrushes.TextSecondary,
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, TrueBimTheme.Spacing8)
        };
        WpfGrid.SetRow(filterHint, 3);
        WpfGrid.SetColumn(filterHint, 2);
        root.Children.Add(filterHint);
        return root;
    }

    private UIElement CreatePdfPreviewPanel()
    {
        ConfigurePreviewGrid(pdfPreviewGrid);
        return pdfPreviewGrid;
    }

    private UIElement CreateMappingPanel()
    {
        DockPanel panel = new();
        TextBlock hint = new()
        {
            Text = "Оставьте флажок для нужных колонок и выберите настоящее поле Revit. Колонки без флажка будут осознанно пропущены.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = TrueBimBrushes.TextSecondary,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };
        DockPanel.SetDock(hint, Dock.Top);
        panel.Children.Add(hint);

        mappingGrid.Style = TrueBimStyles.CreateDataGridStyle();
        mappingGrid.AutoGenerateColumns = false;
        mappingGrid.CanUserAddRows = false;
        mappingGrid.CanUserDeleteRows = false;
        mappingGrid.ItemsSource = mappingRows;
        mappingGrid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "В спеку",
            Binding = CreateBinding(nameof(ScheduleMappingRow.IsIncluded)),
            Width = 70
        });
        mappingGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Колонка PDF",
            Binding = new Binding(nameof(ScheduleMappingRow.SourceColumnName)),
            IsReadOnly = true,
            Width = new DataGridLength(1.1, DataGridLengthUnitType.Star)
        });
        mappingGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Пример из PDF",
            Binding = new Binding(nameof(ScheduleMappingRow.SampleValues)),
            IsReadOnly = true,
            Width = new DataGridLength(1.2, DataGridLengthUnitType.Star)
        });
        mappingGrid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "Поле Revit",
            ItemsSource = fieldOptions,
            DisplayMemberPath = nameof(ScheduleFieldOption.DisplayName),
            SelectedValuePath = nameof(ScheduleFieldOption.Key),
            SelectedValueBinding = CreateBinding(nameof(ScheduleMappingRow.TargetFieldKey)),
            Width = new DataGridLength(1.8, DataGridLengthUnitType.Star)
        });
        mappingGrid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "Условие",
            ItemsSource = ScheduleFilterRuleOption.All,
            DisplayMemberPath = nameof(ScheduleFilterRuleOption.DisplayName),
            SelectedValuePath = nameof(ScheduleFilterRuleOption.Rule),
            SelectedValueBinding = CreateBinding(nameof(ScheduleMappingRow.FilterRule)),
            Width = 150
        });
        mappingGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Значение условия",
            Binding = CreateBinding(nameof(ScheduleMappingRow.FilterValue)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        panel.Children.Add(mappingGrid);
        return panel;
    }

    private UIElement CreateSchedulePreviewPanel()
    {
        DockPanel panel = new();
        previewSummaryText.Text = "Выполните «Предпросмотр Revit». Модель при этом не изменяется.";
        previewSummaryText.TextWrapping = TextWrapping.Wrap;
        previewSummaryText.Foreground = TrueBimBrushes.TextSecondary;
        previewSummaryText.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8);
        DockPanel.SetDock(previewSummaryText, Dock.Top);
        panel.Children.Add(previewSummaryText);

        ConfigurePreviewGrid(schedulePreviewGrid);
        panel.Children.Add(schedulePreviewGrid);
        return panel;
    }

    private UIElement CreateWarningPanel()
    {
        warningList.ItemsSource = warnings;
        warningList.Style = TrueBimStyles.CreateListBoxStyle();
        return warningList;
    }

    private void InitializeInputs()
    {
        tableInput.ItemsSource = tables;
        categoryInput.ItemsSource = context.ScheduleCategories;
        tableInput.SelectedIndex = -1;
        categoryInput.SelectedIndex = -1;
        UpdateActionState();
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
        InvalidatePreview();
        UpdateStatus("Файл выбран. Нажмите «Распознать».");
        UpdateActionState();
    }

    private async Task RecognizeAsync()
    {
        string filePath = filePathInput.Text.Trim();
        isRecognizing = true;
        UpdateStatus("Распознаю таблицу из выбранного файла...");
        UpdateActionState();
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
            isRecognizing = false;
            UpdateActionState();
        }
    }

    private void ShowSelectedTable()
    {
        ParsedTable? table = SelectedTable;
        pdfPreviewGrid.ItemsSource = table is null ? null : BuildPreviewTable(table).DefaultView;
        RebuildMappingRows(table);
        if (table is not null)
        {
            AddWarnings(table.Warnings);
            ValidateCurrentTable(showDialog: false);
        }
        else
        {
            UpdateActionState();
        }
    }

    private void RebuildMappingRows(ParsedTable? table)
    {
        mappingRows.Clear();
        InvalidatePreview();
        if (table is null)
        {
            return;
        }

        foreach ((string column, int index) in table.Columns.Select((column, index) => (column, index)))
        {
            ScheduleMappingRow row = new(column, CreateSampleValues(table, index));
            row.PropertyChanged += (_, _) =>
            {
                InvalidatePreview();
                UpdateActionState();
            };
            mappingRows.Add(row);
        }

        ApplySuggestedMappings();
    }

    private void LoadSelectedCategoryFields()
    {
        InvalidatePreview();
        fieldOptions.Clear();
        foreach (ScheduleMappingRow row in mappingRows)
        {
            row.TargetFieldKey = null;
        }

        if (SelectedCategory is not ScheduleCategoryOption category)
        {
            UpdateActionState();
            return;
        }

        isLoadingFields = true;
        UpdateStatus($"Получаю реальные поля категории «{category.Name}» из Revit...");
        UpdateActionState();
        loadFields(category.CategoryId, OnFieldsLoaded, OnFieldsLoadFailed);
    }

    private void OnFieldsLoaded(ScheduleFieldCatalogResult result)
    {
        isLoadingFields = false;
        if (SelectedCategory?.CategoryId != result.CategoryId)
        {
            UpdateActionState();
            return;
        }

        AddWarnings(result.Warnings);
        AddWarnings(result.Errors);
        foreach (ScheduleFieldOption field in result.Fields)
        {
            fieldOptions.Add(field);
        }

        ApplySuggestedMappings();
        UpdateStatus(result.Succeeded
            ? $"Получено полей Revit: {result.Fields.Count}. Сопоставьте нужные колонки и выполните предпросмотр."
            : "Не удалось получить поля выбранной категории.");
        UpdateActionState();
    }

    private void OnFieldsLoadFailed(Exception exception)
    {
        isLoadingFields = false;
        logger.Error("Failed to load schedulable fields for Schedule Import.", exception);
        AddWarnings([exception.Message]);
        UpdateStatus("Не удалось получить поля выбранной категории Revit.");
        UpdateActionState();
    }

    private void ApplySuggestedMappings()
    {
        ParsedTable? table = SelectedTable;
        if (table is null || fieldOptions.Count == 0)
        {
            return;
        }

        IReadOnlyDictionary<string, string> suggestions = parameterMappingService.SuggestMappings(table, fieldOptions);
        foreach (ScheduleMappingRow row in mappingRows.Where(row => string.IsNullOrWhiteSpace(row.TargetFieldKey)))
        {
            if (suggestions.TryGetValue(row.SourceColumnName, out string? fieldKey))
            {
                row.TargetFieldKey = fieldKey;
            }
        }
    }

    private void ValidateCurrentTable(bool showDialog = true)
    {
        ParsedTable? table = SelectedTable;
        if (table is null)
        {
            UpdateActionState();
            return;
        }

        ParsedTableValidationResult validation = tableValidationService.Validate(table);
        AddWarnings(validation.Warnings);
        AddWarnings(validation.Errors);
        UpdateStatus(validation.Succeeded
            ? $"PDF проверен. Строк: {table.RowCount}. Колонок: {table.ColumnCount}. Теперь настройте поля Revit."
            : "Проверка PDF нашла ошибки.");
        if (showDialog)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                DialogTitle,
                validation.Succeeded
                    ? "Таблица PDF распознана. Для создания нужно сопоставить колонки с полями Revit и выполнить предпросмотр."
                    : string.Join(Environment.NewLine, validation.Errors));
        }

        UpdateActionState();
    }

    private void RunSchedulePreview()
    {
        ScheduleImportRequest? request = BuildRequest(previewOnly: true, showErrors: true);
        if (request is null)
        {
            return;
        }

        BeginScheduleRequest(request, "Revit строит временную спецификацию для предпросмотра...");
    }

    private void CreateInRevit()
    {
        ScheduleImportRequest? request = BuildRequest(previewOnly: false, showErrors: true);
        if (request is null)
        {
            return;
        }

        if (!string.Equals(previewFingerprint, request.ConfigurationFingerprint, StringComparison.Ordinal))
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                DialogTitle,
                "После последнего предпросмотра категория, поля или условия изменились. Выполните «Предпросмотр Revit» ещё раз.");
            return;
        }

        MessageBoxResult decision = MessageBox.Show(
            this,
            $"Создать параметрическую спецификацию категории «{request.CategoryName}»?\n\n" +
            $"Полей: {request.Mappings.Count}. Условий: {request.Mappings.Count(mapping => mapping.FilterRule != ScheduleFilterRule.None)}.\n" +
            "Строки будут сформированы из реальных элементов модели Revit.",
            DialogTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        BeginScheduleRequest(request, "Создаю параметрическую спецификацию Revit...");
    }

    private ScheduleImportRequest? BuildRequest(bool previewOnly, bool showErrors)
    {
        return BuildRequest(previewOnly, showErrors, out _);
    }

    private ScheduleImportRequest? BuildRequest(
        bool previewOnly,
        bool showErrors,
        out IReadOnlyList<string> blockers)
    {
        CommitMappingEdits();
        ParsedTable? table = SelectedTable;
        ScheduleCategoryOption? category = SelectedCategory;
        if (table is null || category is null)
        {
            List<string> prerequisites = [];
            if (table is null)
            {
                prerequisites.Add("Выберите файл и распознайте таблицу PDF или DWG.");
            }

            if (category is null)
            {
                prerequisites.Add("Выберите категорию Revit для новой спецификации.");
            }

            blockers = prerequisites;
            if (showErrors)
            {
                Autodesk.Revit.UI.TaskDialog.Show(DialogTitle, string.Join(Environment.NewLine, blockers));
            }

            return null;
        }

        List<string> localErrors = [];
        ParsedTableValidationResult tableValidation = tableValidationService.Validate(table);
        localErrors.AddRange(tableValidation.Errors);
        List<ScheduleFieldMapping> mappings = [];
        foreach (ScheduleMappingRow row in mappingRows.Where(row => row.IsIncluded))
        {
            ScheduleFieldOption? field = fieldOptions.FirstOrDefault(option =>
                string.Equals(option.Key, row.TargetFieldKey, StringComparison.Ordinal));
            if (field is null)
            {
                localErrors.Add($"Для колонки «{row.SourceColumnName}» выберите поле Revit или снимите флажок «В спеку».");
                continue;
            }

            mappings.Add(new ScheduleFieldMapping(
                row.SourceColumnName,
                field.Key,
                field.Name,
                field.ParameterId,
                field.FieldTypeValue,
                row.FilterRule,
                row.FilterValue));
        }

        ScheduleMappingValidationResult validation = mappingConfigurationService.Validate(
            table,
            category.CategoryId,
            mappings);
        localErrors.AddRange(validation.Errors);
        if (localErrors.Count > 0)
        {
            blockers = localErrors
                .Distinct(StringComparer.CurrentCulture)
                .ToList();
            if (showErrors)
            {
                Autodesk.Revit.UI.TaskDialog.Show(
                    DialogTitle,
                    string.Join(Environment.NewLine, blockers));
            }

            return null;
        }

        blockers = Array.Empty<string>();
        return new ScheduleImportRequest(
            table,
            category.CategoryId,
            category.Name,
            mappings,
            previewOnly,
            validation.ConfigurationFingerprint);
    }

    private void BeginScheduleRequest(ScheduleImportRequest request, string status)
    {
        isApplying = true;
        UpdateStatus(status);
        UpdateActionState();
        executeSchedule(request, OnScheduleExecuted, OnScheduleFailed);
    }

    private void OnScheduleExecuted(ScheduleImportCreationResult result)
    {
        isApplying = false;
        AddWarnings(result.Warnings);
        AddWarnings(result.Errors);
        if (!result.Succeeded)
        {
            InvalidatePreview();
            UpdateStatus(result.IsPreview
                ? "Предпросмотр спецификации завершился ошибкой."
                : "Создание спецификации завершилось ошибкой.");
            Autodesk.Revit.UI.TaskDialog.Show(DialogTitle, result.ToDialogText());
            UpdateActionState();
            return;
        }

        if (result.IsPreview)
        {
            previewFingerprint = result.ConfigurationFingerprint;
            schedulePreviewGrid.ItemsSource = BuildPreviewTable(result.Preview).DefaultView;
            previewSummaryText.Text =
                $"Категория: {result.ScheduleName}. Элементов модели: {result.RowCount}. " +
                $"Полей: {result.ColumnCount}. Условий: {result.FilterCount}. Модель не изменена.";
            if (schedulePreviewTab is not null)
            {
                tabs.SelectedItem = schedulePreviewTab;
            }

            UpdateStatus("Предпросмотр Revit готов. Если результат верный, создайте спецификацию.");
        }
        else
        {
            UpdateStatus($"Создана параметрическая спецификация Revit «{result.ScheduleName}».");
            Autodesk.Revit.UI.TaskDialog.Show(DialogTitle, result.ToDialogText());
        }

        UpdateActionState();
    }

    private void OnScheduleFailed(Exception exception)
    {
        isApplying = false;
        logger.Error("Failed to preview or create parametric Revit schedule.", exception);
        AddWarnings([exception.Message]);
        InvalidatePreview();
        UpdateStatus("Revit отклонил поля или условия спецификации.");
        Autodesk.Revit.UI.TaskDialog.Show(
            DialogTitle,
            $"Не удалось построить спецификацию Revit.\n\n{exception.Message}");
        UpdateActionState();
    }

    private void UpdateActionState()
    {
        bool hasSourceFile = !string.IsNullOrWhiteSpace(filePathInput.Text);
        recognizeButton.IsEnabled = !isApplying && !isRecognizing && hasSourceFile;
        recognizeButton.ToolTip = recognizeButton.IsEnabled
            ? "Распознать таблицу и полные названия колонок из выбранного PDF или DWG."
            : CreateBlockedActionTooltip(
                "Распознавание недоступно",
                [isApplying
                    ? "Дождитесь завершения операции Revit."
                    : isRecognizing
                        ? "Дождитесь завершения распознавания файла."
                        : "Сначала выберите файл PDF или DWG."]);

        validateButton.IsEnabled = !isApplying && !isRecognizing && SelectedTable is not null;
        validateButton.ToolTip = validateButton.IsEnabled
            ? "Проверить структуру, границы и распознанные колонки исходной таблицы."
            : CreateBlockedActionTooltip(
                "Проверка PDF недоступна",
                [isApplying
                    ? "Дождитесь завершения операции Revit."
                    : isRecognizing
                        ? "Дождитесь завершения распознавания файла."
                        : "Сначала распознайте таблицу PDF или DWG."]);

        categoryInput.IsEnabled = !isApplying && !isLoadingFields && !isRecognizing;
        tableInput.IsEnabled = !isApplying && !isRecognizing;
        mappingGrid.IsEnabled = !isApplying && !isLoadingFields && !isRecognizing && fieldOptions.Count > 0;

        ScheduleImportRequest? previewRequest = null;
        IReadOnlyList<string> previewBlockers;
        if (isApplying)
        {
            previewBlockers = ["Дождитесь завершения операции Revit."];
        }
        else if (isRecognizing)
        {
            previewBlockers = ["Дождитесь завершения распознавания файла."];
        }
        else if (isLoadingFields)
        {
            previewBlockers = ["Дождитесь загрузки реальных полей выбранной категории Revit."];
        }
        else
        {
            previewRequest = BuildRequest(previewOnly: true, showErrors: false, out previewBlockers);
        }

        previewButton.IsEnabled = previewRequest is not null;
        previewButton.ToolTip = previewButton.IsEnabled
            ? "Построить временную спецификацию из реальных элементов Revit без изменения модели."
            : CreateBlockedActionTooltip("Предпросмотр Revit недоступен", previewBlockers);

        List<string> createBlockers = previewBlockers.ToList();
        bool hasCurrentPreview = previewRequest is not null
            && previewFingerprint is not null
            && string.Equals(
                previewFingerprint,
                previewRequest.ConfigurationFingerprint,
                StringComparison.Ordinal);
        if (previewRequest is not null && previewFingerprint is null)
        {
            createBlockers.Add("Сначала выполните «Предпросмотр Revit».");
        }
        else if (previewRequest is not null && !hasCurrentPreview)
        {
            createBlockers.Add("Категория, поля или условия изменились — повторите «Предпросмотр Revit».");
        }

        createButton.IsEnabled = hasCurrentPreview;
        createButton.ToolTip = createButton.IsEnabled
            ? "Создать рабочую параметрическую спецификацию Revit с проверенными полями и условиями."
            : CreateBlockedActionTooltip("Создание спецификации недоступно", createBlockers);
    }

    private void InvalidatePreview()
    {
        previewFingerprint = null;
        schedulePreviewGrid.ItemsSource = null;
        previewSummaryText.Text = "Конфигурация изменена. Выполните «Предпросмотр Revit» ещё раз.";
        createButton.IsEnabled = false;
    }

    private void CommitMappingEdits()
    {
        mappingGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        mappingGrid.CommitEdit(DataGridEditingUnit.Row, true);
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
        if (args.Data.GetDataPresent(DataFormats.FileDrop)
            && args.Data.GetData(DataFormats.FileDrop) is string[] files
            && files.Length > 0)
        {
            filePathInput.Text = files[0];
            InvalidatePreview();
            UpdateStatus("Файл добавлен. Нажмите «Распознать».");
            UpdateActionState();
        }
    }

    private ParsedTable? SelectedTable => tableInput.SelectedItem is TableItem item ? item.Table : null;

    private ScheduleCategoryOption? SelectedCategory => categoryInput.SelectedItem as ScheduleCategoryOption;

    private void UpdateStatus(string? prefix = null)
    {
        ParsedTable? table = SelectedTable;
        string tableText = table is null
            ? "Таблица не выбрана."
            : $"PDF: {table.RowCount} x {table.ColumnCount}.";
        string categoryText = SelectedCategory is null
            ? "Категория не выбрана."
            : $"Категория: {SelectedCategory.Name}.";
        string previewText = previewFingerprint is null
            ? "Предпросмотр Revit не выполнен."
            : "Предпросмотр Revit актуален.";
        statusText.Text = string.IsNullOrWhiteSpace(prefix)
            ? $"{tableText} {categoryText} {previewText} Предупреждений: {warnings.Count}."
            : $"{prefix} {tableText} {categoryText} {previewText} Предупреждений: {warnings.Count}.";
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

    private static string CreateSampleValues(ParsedTable table, int columnIndex)
    {
        return string.Join(
            " | ",
            table.Rows
                .OrderBy(row => row.RowIndex)
                .Select(row => columnIndex < row.Values.Count ? row.Values[columnIndex]?.Trim() : null)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Where(value => !string.Equals(
                    value,
                    columnIndex < table.Columns.Count ? table.Columns[columnIndex]?.Trim() : null,
                    StringComparison.CurrentCultureIgnoreCase))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Take(3));
    }

    private static DataTable BuildPreviewTable(ParsedTable table)
    {
        IReadOnlyList<string> columns = table.Columns.Count > 0
            ? table.Columns
            : Enumerable.Range(1, table.ColumnCount).Select(index => $"Колонка {index}").ToList();
        return BuildDataTable(columns, table.Rows.Select(row => row.Values));
    }

    private static DataTable BuildPreviewTable(SchedulePreviewTable preview)
    {
        return BuildDataTable(preview.Columns, preview.Rows);
    }

    private static DataTable BuildDataTable(
        IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<string>> rows)
    {
        DataTable dataTable = new();
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

        foreach (IReadOnlyList<string> row in rows)
        {
            DataRow dataRow = dataTable.NewRow();
            for (int columnIndex = 0; columnIndex < dataTable.Columns.Count; columnIndex++)
            {
                dataRow[columnIndex] = columnIndex < row.Count ? row[columnIndex] : string.Empty;
            }

            dataTable.Rows.Add(dataRow);
        }

        return dataTable;
    }

    private void ConfigureActionHelp()
    {
        foreach (Button button in new[] { recognizeButton, validateButton, previewButton, createButton })
        {
            ToolTipService.SetShowOnDisabled(button, true);
            ToolTipService.SetInitialShowDelay(button, 250);
            ToolTipService.SetShowDuration(button, 30000);
        }
    }

    private static void ConfigurePreviewGrid(DataGrid dataGrid)
    {
        dataGrid.Style = TrueBimStyles.CreateDataGridStyle();
        dataGrid.AutoGenerateColumns = true;
        dataGrid.CanUserAddRows = false;
        dataGrid.CanUserDeleteRows = false;
        dataGrid.IsReadOnly = true;
        dataGrid.ColumnHeaderHeight = double.NaN;
        dataGrid.AutoGeneratingColumn += (_, args) => ConfigurePreviewColumn(args);
    }

    private static void ConfigurePreviewColumn(DataGridAutoGeneratingColumnEventArgs args)
    {
        string heading = ScheduleColumnHeadingNormalizer.Normalize(args.Column.Header?.ToString());
        if (heading.Length == 0)
        {
            heading = "Колонка";
        }

        TextBlock header = new()
        {
            Text = heading,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, TrueBimTheme.Spacing4),
            ToolTip = heading
        };
        ToolTipService.SetShowDuration(header, 30000);
        args.Column.Header = header;
        args.Column.MinWidth = 72;
        double widthWeight = Math.Min(2.8, Math.Max(0.75, heading.Length / 18.0));
        args.Column.Width = new DataGridLength(widthWeight, DataGridLengthUnitType.Star);
    }

    private static string CreateBlockedActionTooltip(string title, IEnumerable<string> reasons)
    {
        List<string> items = reasons
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Distinct(StringComparer.CurrentCulture)
            .ToList();
        if (items.Count == 0)
        {
            items.Add("Завершите предыдущий шаг.");
        }

        const int visibleReasonLimit = 5;
        string list = string.Join(
            Environment.NewLine,
            items.Take(visibleReasonLimit).Select(reason => $"• {reason}"));
        if (items.Count > visibleReasonLimit)
        {
            list += $"{Environment.NewLine}• Ещё требований: {items.Count - visibleReasonLimit}.";
        }

        return $"{title}:{Environment.NewLine}{list}";
    }

    private static void ConfigureTextBox(TextBox textBox)
    {
        textBox.MinHeight = TrueBimTheme.ControlHeight32;
        textBox.Style = TrueBimStyles.CreateTextBoxStyle();
        textBox.Margin = new Thickness(TrueBimTheme.Spacing8, 0, TrueBimTheme.Spacing12, TrueBimTheme.Spacing8);
    }

    private static void ConfigureComboBox(ComboBox comboBox)
    {
        comboBox.MinHeight = TrueBimTheme.ControlHeight32;
        comboBox.MinWidth = 280;
        comboBox.Style = TrueBimStyles.CreateComboBoxStyle();
        comboBox.Margin = new Thickness(TrueBimTheme.Spacing8, 0, TrueBimTheme.Spacing12, TrueBimTheme.Spacing8);
    }

    private static Binding CreateBinding(string path)
    {
        return new Binding(path)
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };
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

    private sealed class ScheduleMappingRow : INotifyPropertyChanged
    {
        private bool isIncluded = true;
        private string? targetFieldKey;
        private ScheduleFilterRule filterRule;
        private string? filterValue;

        public ScheduleMappingRow(string sourceColumnName, string sampleValues)
        {
            SourceColumnName = sourceColumnName;
            SampleValues = sampleValues;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string SourceColumnName { get; }

        public string SampleValues { get; }

        public bool IsIncluded
        {
            get => isIncluded;
            set => SetField(ref isIncluded, value);
        }

        public string? TargetFieldKey
        {
            get => targetFieldKey;
            set => SetField(ref targetFieldKey, value);
        }

        public ScheduleFilterRule FilterRule
        {
            get => filterRule;
            set => SetField(ref filterRule, value);
        }

        public string? FilterValue
        {
            get => filterValue;
            set => SetField(ref filterValue, value);
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
