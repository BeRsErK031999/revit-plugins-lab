using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.Modules.BimTools.ParaManager.Models;
using TrueBIM.App.Modules.BimTools.ParaManager.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace TrueBIM.App.Modules.BimTools.ParaManager.UI;

public sealed class ParaManagerWindow : TrueBimWindow
{
    private readonly UIApplication uiApplication;
    private readonly Document document;
    private readonly ParameterCsvImportService csvImportService;
    private readonly ProjectParameterExportService projectParameterExportService;
    private readonly ParaManagerValidationService validationService;
    private readonly ProjectParameterBindingService bindingService;
    private readonly CategoryResolveService categoryResolveService;
    private readonly ParaManagerCategoryPresetService categoryPresetService;
    private readonly ITrueBimLogger logger;
    private readonly ParaManagerImportExternalEventHandler importHandler;
    private readonly ExternalEvent importEvent;
    private readonly WpfTextBox sharedParameterPathInput = new();
    private readonly WpfTextBox csvPathInput = new();
    private readonly WpfTextBox categoryPresetInput = new();
    private readonly WpfTextBox projectParameterSearchInput = new();
    private readonly ListBox importRowList = new();
    private readonly DataGrid projectParameterGrid = new();
    private readonly WpfTextBox reportText = new();
    private readonly TextBlock statusText = new();
    private readonly ObservableCollection<ProjectParameterRow> projectParameterRows = [];
    private List<ProjectParameterRow> allProjectParameterRows = [];
    private List<ParameterImportRow> importRows = [];
    private List<string> selectedCategoryPreset = [];

    public ParaManagerWindow(
        UIApplication uiApplication,
        Document document,
        ParameterCsvImportService csvImportService,
        ParaManagerValidationService validationService,
        ProjectParameterBindingService bindingService,
        ITrueBimLogger logger)
    {
        this.uiApplication = uiApplication ?? throw new ArgumentNullException(nameof(uiApplication));
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        this.csvImportService = csvImportService ?? throw new ArgumentNullException(nameof(csvImportService));
        projectParameterExportService = new ProjectParameterExportService();
        this.validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
        this.bindingService = bindingService ?? throw new ArgumentNullException(nameof(bindingService));
        categoryResolveService = new CategoryResolveService();
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        importHandler = new ParaManagerImportExternalEventHandler(this);
        importEvent = ExternalEvent.Create(importHandler);
        categoryPresetService = new ParaManagerCategoryPresetService(
            ParaManagerCategoryPresetService.GetDefaultSettingsPath(),
            logger);
        selectedCategoryPreset = categoryPresetService.Load().ToList();

        Title = "ParaManager";
        Icon = IconFactory.CreateImage(TrueBimIcon.Parameters, 32);
        Width = 1040;
        Height = 700;
        MinWidth = 960;
        MinHeight = 620;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();
        sharedParameterPathInput.Text = uiApplication.Application.SharedParametersFilename ?? string.Empty;
        UpdateCategoryPresetText();
        RefreshProjectParameters();
        UpdateStatus();
    }

    private UIElement CreateContent()
    {
        TabControl tabs = new()
        {
            Style = TrueBimStyles.CreateTabControlStyle()
        };
        tabs.Items.Add(CreateProjectParametersTab());
        tabs.Items.Add(CreateImportTab());
        tabs.Items.Add(CreateReportTab());

        statusText.Foreground = TrueBimBrushes.TextSecondary;
        statusText.TextWrapping = TextWrapping.Wrap;

        Button helpButton = TrueBimUi.CreateSecondaryButton(
            "Справка",
            TrueBimIcon.Help,
            (_, _) => ShowHelp(),
            minWidth: 110);
        helpButton.ToolTip = "Показать короткую подсказку по ручному добавлению, CSV и shared parameter file.";

        Button closeButton = TrueBimUi.CreateSecondaryButton("Закрыть", TrueBimIcon.Close, minWidth: 110);
        closeButton.IsCancel = true;
        closeButton.Click += (_, _) => Close();

        return BuildShell(
            header: TrueBimUi.CreateHeader(
                Title,
                "Создание и привязка project/shared parameters к категориям текущей модели Revit.",
                TrueBimIcon.Parameters),
            commandBar: TrueBimUi.CreateCommandBar(helpButton),
            body: tabs,
            status: null,
            footer: TrueBimUi.CreateFooter(statusText, closeButton));
    }

    private TabItem CreateProjectParametersTab()
    {
        WpfGrid panel = new()
        {
            Margin = TrueBimTheme.SectionPadding
        };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        StackPanel toolbar = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };

        Button refreshButton = TrueBimUi.CreateSecondaryButton("Обновить список", TrueBimIcon.Preview, minWidth: 160);
        refreshButton.ToolTip = "Снова прочитать project parameters из текущего документа Revit.";
        refreshButton.Click += (_, _) => RefreshProjectParameters();
        toolbar.Children.Add(refreshButton);

        Button exportButton = TrueBimUi.CreateSecondaryButton("Экспорт списка", TrueBimIcon.Export, minWidth: 130);
        exportButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        exportButton.ToolTip = "Сохранить текущие project parameters в CSV для проверки или передачи BIM-координатору.";
        exportButton.Click += (_, _) => ExportProjectParameters();
        toolbar.Children.Add(exportButton);
        projectParameterSearchInput.MinHeight = TrueBimTheme.ControlHeight32;
        projectParameterSearchInput.Width = 260;
        projectParameterSearchInput.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        projectParameterSearchInput.Style = TrueBimStyles.CreateTextBoxStyle();
        projectParameterSearchInput.VerticalContentAlignment = VerticalAlignment.Center;
        projectParameterSearchInput.ToolTip = "Search project parameters by name, category, type or group.";
        projectParameterSearchInput.TextChanged += (_, _) => RefreshProjectParameterGrid();
        toolbar.Children.Add(projectParameterSearchInput);
        panel.Children.Add(toolbar);

        projectParameterGrid.AutoGenerateColumns = false;
        projectParameterGrid.CanUserAddRows = false;
        projectParameterGrid.CanUserDeleteRows = false;
        projectParameterGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        projectParameterGrid.IsReadOnly = true;
        projectParameterGrid.ItemsSource = projectParameterRows;
        projectParameterGrid.SelectionMode = DataGridSelectionMode.Extended;
        projectParameterGrid.Style = TrueBimStyles.CreateDataGridStyle();
        projectParameterGrid.ToolTip = "Project parameters already bound to categories in the current Revit document.";
        projectParameterGrid.Columns.Add(CreateTextColumn("Parameter", nameof(ProjectParameterRow.Name), 220));
        projectParameterGrid.Columns.Add(CreateTextColumn("Binding", nameof(ProjectParameterRow.BindingTypeDisplay), 90));
        projectParameterGrid.Columns.Add(CreateTextColumn("Categories", nameof(ProjectParameterRow.CategoriesDisplay), 260));
        projectParameterGrid.Columns.Add(CreateTextColumn("Group", nameof(ProjectParameterRow.GroupDisplay), 140));
        projectParameterGrid.Columns.Add(CreateTextColumn("Type", nameof(ProjectParameterRow.DataTypeDisplay), 120));
        projectParameterGrid.Columns.Add(CreateTextColumn("GUID", nameof(ProjectParameterRow.GuidDisplay), 240));
        WpfGrid.SetRow(projectParameterGrid, 1);
        panel.Children.Add(projectParameterGrid);

        return new TabItem
        {
            Header = "Параметры проекта",
            Content = panel,
            ToolTip = "Посмотреть параметры, которые уже есть в модели."
        };
    }

    private static DataGridTextColumn CreateTextColumn(string header, string bindingPath, double width)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new System.Windows.Data.Binding(bindingPath),
            Width = width
        };
    }

    private TabItem CreateImportTab()
    {
        WpfGrid panel = new()
        {
            Margin = TrueBimTheme.SectionPadding
        };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        UIElement sharedFileBar = CreatePathBar(
            sharedParameterPathInput,
            "Shared parameter file (*.txt)|*.txt|All files (*.*)|*.*",
            "Выбрать shared .txt",
            ChooseSharedParameterFile,
            "Файл общих параметров Revit. ParaManager создаёт в нём definitions и затем привязывает их к проекту.",
            "Выбрать существующий Revit shared parameter .txt.");
        panel.Children.Add(sharedFileBar);

        UIElement csvFileBar = CreatePathBar(
            csvPathInput,
            "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            "Выбрать CSV",
            ChooseImportFile,
            "CSV-шаблон ParaManager для массового создания параметров. Это не Revit-спецификация.",
            "Выбрать заполненный CSV-шаблон с колонками ParameterName, BindingType, Categories и другими.");
        WpfGrid.SetRow(csvFileBar, 1);
        panel.Children.Add(csvFileBar);

        UIElement categoryBar = CreateCategoryPresetBar();
        WpfGrid.SetRow(categoryBar, 2);
        panel.Children.Add(categoryBar);

        StackPanel toolbar = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };
        Button addButton = CreateSmallButton(
            "Добавить параметр",
            TrueBimIcon.Parameter,
            (_, _) => AddManualParameter(),
            "Создать одну строку параметра вручную без CSV.");
        toolbar.Children.Add(addButton);

        Button validateButton = CreateSmallButton(
            "Проверить",
            TrueBimIcon.Check,
            (_, _) => LoadAndValidate(),
            "Прочитать CSV и проверить имена, типы, категории и существующие параметры проекта.");
        validateButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        toolbar.Children.Add(validateButton);

        Button templateButton = CreateSmallButton(
            "CSV-шаблон",
            TrueBimIcon.Export,
            (_, _) => ExportTemplate(),
            "Сохранить CSV-шаблон с примерами строк. Заполните его и загрузите через «Выбрать CSV».");
        templateButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        toolbar.Children.Add(templateButton);

        Button removeButton = CreateSmallButton(
            "Убрать выбранные",
            TrueBimIcon.Close,
            (_, _) => RemoveSelectedRows(),
            "Удалить выделенные строки из текущего списка импорта. Модель Revit при этом не меняется.");
        removeButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        toolbar.Children.Add(removeButton);
        WpfGrid.SetRow(toolbar, 3);
        panel.Children.Add(toolbar);

        importRowList.Style = TrueBimStyles.CreateListBoxStyle();
        importRowList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        importRowList.SelectionMode = SelectionMode.Extended;
        importRowList.ToolTip = "Выделите строки, чтобы присвоить им выбранные категории или убрать их из списка.";
        WpfGrid.SetRow(importRowList, 4);
        panel.Children.Add(importRowList);

        StackPanel footer = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0)
        };
        Button applyButton = TrueBimUi.CreatePrimaryButton("Применить импорт", TrueBimIcon.Apply, minWidth: 170);
        applyButton.ToolTip = "Записать проверенные параметры в shared parameter file и привязать их к категориям текущего проекта.";
        applyButton.Click += (_, _) => ApplyImport();
        footer.Children.Add(applyButton);
        WpfGrid.SetRow(footer, 5);
        panel.Children.Add(footer);

        return new TabItem
        {
            Header = "Создание и импорт",
            Content = panel,
            ToolTip = "Добавить один параметр вручную или загрузить набор параметров из CSV."
        };
    }

    private TabItem CreateReportTab()
    {
        reportText.AcceptsReturn = true;
        reportText.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        reportText.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        reportText.IsReadOnly = true;
        reportText.TextWrapping = TextWrapping.Wrap;
        reportText.Style = TrueBimStyles.CreateTextBoxStyle();
        reportText.Text = "Импорт ещё не выполнялся.";

        return new TabItem
        {
            Header = "Отчёт",
            ToolTip = "Итоги последнего применения импорта и ошибки по строкам.",
            Content = new WpfGrid
            {
                Margin = TrueBimTheme.SectionPadding,
                Children = { reportText }
            }
        };
    }

    private static DockPanel CreatePathBar(
        WpfTextBox input,
        string filter,
        string buttonText,
        RoutedEventHandler clickHandler,
        string inputToolTip,
        string buttonToolTip)
    {
        DockPanel fileBar = new()
        {
            LastChildFill = true,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8),
            Tag = filter
        };

        Button chooseButton = TrueBimUi.CreateSecondaryButton(buttonText, TrueBimIcon.Open, minWidth: 190);
        chooseButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        chooseButton.ToolTip = buttonToolTip;
        chooseButton.Click += clickHandler;
        DockPanel.SetDock(chooseButton, Dock.Right);
        fileBar.Children.Add(chooseButton);

        input.MinHeight = TrueBimTheme.ControlHeight32;
        input.Style = TrueBimStyles.CreateTextBoxStyle();
        input.VerticalContentAlignment = VerticalAlignment.Center;
        input.ToolTip = inputToolTip;
        fileBar.Children.Add(input);

        return fileBar;
    }

    private DockPanel CreateCategoryPresetBar()
    {
        DockPanel categoryBar = new()
        {
            LastChildFill = true,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };

        Button applyButton = TrueBimUi.CreateSecondaryButton("Присвоить категории", TrueBimIcon.Apply, minWidth: 195);
        applyButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        applyButton.ToolTip = "Записать выбранный набор категорий в выделенные строки импорта. Это ещё не меняет модель.";
        applyButton.Click += (_, _) => ApplyCategoriesToSelectedRows();
        DockPanel.SetDock(applyButton, Dock.Right);
        categoryBar.Children.Add(applyButton);

        Button chooseButton = TrueBimUi.CreateSecondaryButton("Категории", TrueBimIcon.Open, minWidth: 125);
        chooseButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        chooseButton.ToolTip = "Выбрать категории Revit, которым нужно добавить параметры.";
        chooseButton.Click += (_, _) => ChooseCategories();
        DockPanel.SetDock(chooseButton, Dock.Right);
        categoryBar.Children.Add(chooseButton);

        categoryPresetInput.MinHeight = TrueBimTheme.ControlHeight32;
        categoryPresetInput.Style = TrueBimStyles.CreateTextBoxStyle();
        categoryPresetInput.VerticalContentAlignment = VerticalAlignment.Center;
        categoryPresetInput.IsReadOnly = true;
        categoryPresetInput.ToolTip = "Сохранённый набор категорий. Его можно применить к строкам CSV или использовать как стартовый набор при ручном добавлении.";
        categoryBar.Children.Add(categoryPresetInput);

        return categoryBar;
    }

    private static void ShowHelp()
    {
        TaskDialog dialog = new("ParaManager")
        {
            MainInstruction = "Что делает ParaManager",
            MainContent =
                "ParaManager создаёт project/shared parameters и привязывает их к категориям текущего проекта Revit.\n\n" +
                "1. «Выбрать shared .txt» — файл общих параметров Revit, где хранятся definitions.\n" +
                "2. «Добавить параметр» — ручной ввод одного параметра без CSV.\n" +
                "3. «Выбрать CSV» — массовый импорт строк из CSV-шаблона ParaManager, не выбор Revit-спецификации.\n" +
                "4. «Категории» и «Присвоить категории» — задают, какие категории получат параметр.\n" +
                "5. «Применить импорт» — единственный шаг, который записывает изменения в модель."
        };
        dialog.Show();
    }

    private void AddManualParameter()
    {
        IReadOnlyList<string> categoryNames = categoryResolveService.CollectBindableCategoryNames(document);
        ParameterManualAddWindow window = new(categoryNames, selectedCategoryPreset)
        {
            Owner = this
        };
        if (window.ShowDialog() != true || window.CreatedRow is null)
        {
            return;
        }

        importRows.Add(window.CreatedRow);
        ValidateRows();
        RefreshImportRows();
        statusText.Text = $"Параметр добавлен в список проверки: {window.CreatedRow.ParameterName}. Перед записью нажмите «Применить импорт».";
    }

    private void RemoveSelectedRows()
    {
        if (importRows.Count == 0)
        {
            statusText.Text = "Нет строк импорта для удаления.";
            return;
        }

        IReadOnlyList<ParameterImportRow> selectedRows = GetSelectedImportRows();
        if (selectedRows.Count == 0)
        {
            statusText.Text = "Выделите строки импорта, которые нужно убрать из списка.";
            return;
        }

        HashSet<ParameterImportRow> selectedRowSet = selectedRows.ToHashSet();
        importRows = importRows
            .Where(row => !selectedRowSet.Contains(row))
            .ToList();
        if (importRows.Count > 0)
        {
            ValidateRows();
        }

        RefreshImportRows();
        statusText.Text = $"Из списка импорта убрано строк: {selectedRows.Count}.";
    }

    private void ChooseSharedParameterFile(object sender, RoutedEventArgs args)
    {
        if (ChooseFile(sharedParameterPathInput, "Shared parameter file (*.txt)|*.txt|All files (*.*)|*.*"))
        {
            statusText.Text = $"Shared parameter .txt выбран: {sharedParameterPathInput.Text}. Теперь добавьте параметр вручную или загрузите CSV.";
        }
    }

    private void ChooseImportFile(object sender, RoutedEventArgs args)
    {
        if (ChooseFile(csvPathInput, "CSV files (*.csv)|*.csv|All files (*.*)|*.*"))
        {
            LoadAndValidate();
        }
    }

    private bool ChooseFile(WpfTextBox input, string filter)
    {
        OpenFileDialog dialog = new()
        {
            Filter = filter,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return false;
        }

        input.Text = dialog.FileName;
        return true;
    }

    private void ChooseCategories()
    {
        IReadOnlyList<string> categoryNames = categoryResolveService.CollectBindableCategoryNames(document);
        ParameterCategorySelectionWindow window = new(categoryNames, selectedCategoryPreset)
        {
            Owner = this
        };
        if (window.ShowDialog() != true)
        {
            return;
        }

        selectedCategoryPreset = window.SelectedCategoryNames.ToList();
        categoryPresetService.Save(selectedCategoryPreset);
        UpdateCategoryPresetText();
        statusText.Text = selectedCategoryPreset.Count == 0
            ? "Список категорий очищен."
            : $"Сохранено категорий: {selectedCategoryPreset.Count}.";
    }

    private void ApplyCategoriesToSelectedRows()
    {
        if (importRows.Count == 0)
        {
            statusText.Text = "Сначала загрузите строки импорта.";
            return;
        }

        if (selectedCategoryPreset.Count == 0)
        {
            statusText.Text = "Сначала выберите категории.";
            return;
        }

        IReadOnlyList<ParameterImportRow> selectedRows = GetSelectedImportRows();
        if (selectedRows.Count == 0)
        {
            statusText.Text = "Выделите одну или несколько строк импорта.";
            return;
        }

        HashSet<ParameterImportRow> selectedRowSet = selectedRows.ToHashSet();
        string categories = string.Join(",", selectedCategoryPreset);
        importRows = importRows
            .Select(row => selectedRowSet.Contains(row) ? row.WithCategories(categories) : row)
            .ToList();
        ValidateRows();
        RefreshImportRows();
        statusText.Text = $"Категории добавлены к строкам: {selectedRows.Count}.";
    }

    private IReadOnlyList<ParameterImportRow> GetSelectedImportRows()
    {
        return importRowList.SelectedItems
            .OfType<FrameworkElement>()
            .Select(item => item.Tag as ParameterImportRow)
            .Where(row => row is not null)
            .Cast<ParameterImportRow>()
            .ToList();
    }

    private void LoadAndValidate()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(csvPathInput.Text) || !File.Exists(csvPathInput.Text))
            {
                statusText.Text = "Выберите существующий файл таблицы импорта.";
                return;
            }

            importRows = csvImportService.Read(csvPathInput.Text).ToList();
            ValidateRows();
            RefreshImportRows();
        }
        catch (Exception exception)
        {
            logger.Error("Failed to read ParaManager import file.", exception);
            TaskDialog.Show("ParaManager", "Не удалось прочитать файл импорта. Используйте логи для диагностики.");
        }
    }

    private void ValidateRows()
    {
        ISet<string> existingNames = bindingService.CollectExistingProjectParameterNames(document);
        validationService.Validate(
            importRows,
            existingNames,
            categoryName => categoryResolveService.CategoryExists(document, categoryName));
    }

    private void RefreshImportRows()
    {
        importRowList.Items.Clear();
        foreach (ParameterImportRow row in importRows)
        {
            importRowList.Items.Add(CreateImportRow(row));
        }

        UpdateStatus();
    }

    private static UIElement CreateImportRow(ParameterImportRow row)
    {
        string source = GetRowSourceDisplay(row);
        DockPanel panel = new()
        {
            LastChildFill = true,
            Margin = new Thickness(TrueBimTheme.Spacing8, TrueBimTheme.Spacing4, TrueBimTheme.Spacing8, TrueBimTheme.Spacing4),
            Tag = row,
            ToolTip = $"{source}. {row.BindingType} parameter: {row.DataType}. Категории: {row.Categories}. {row.Message}"
        };

        TextBlock status = new()
        {
            Text = row.StatusDisplay,
            Width = 135,
            Foreground = GetStatusBrush(row.Status),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(status, Dock.Right);
        panel.Children.Add(status);

        StackPanel textPanel = new();
        textPanel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(row.ParameterName) ? "<пусто>" : row.ParameterName,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = $"{source}. {row.BindingType} | {row.DataType} | {row.GroupUnder} | {row.Categories}. {row.Message}",
            Foreground = TrueBimBrushes.TextSecondary,
            FontSize = TrueBimTheme.FontSize,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(textPanel);

        return panel;
    }

    private static string GetRowSourceDisplay(ParameterImportRow row)
    {
        return row.LineNumber > 0 ? $"Строка {row.LineNumber}" : "Ручной ввод";
    }

    private void RefreshProjectParameters()
    {
        try
        {
            allProjectParameterRows = bindingService.CollectProjectParameters(document).ToList();
            RefreshProjectParameterGrid();
            UpdateStatus();
        }
        catch (Exception exception)
        {
            logger.Error("Failed to collect project parameters.", exception);
            TaskDialog.Show("ParaManager", "Не удалось собрать параметры проекта. Используйте логи для диагностики.");
        }
    }

    private void RefreshProjectParameterGrid()
    {
        string query = projectParameterSearchInput.Text.Trim();
        IEnumerable<ProjectParameterRow> rows = allProjectParameterRows;
        if (!string.IsNullOrWhiteSpace(query))
        {
            rows = rows.Where(row =>
                Contains(row.Name, query)
                || Contains(row.BindingTypeDisplay, query)
                || Contains(row.CategoriesDisplay, query)
                || Contains(row.GroupDisplay, query)
                || Contains(row.DataTypeDisplay, query)
                || Contains(row.GuidDisplay, query));
        }

        projectParameterRows.Clear();
        foreach (ProjectParameterRow row in rows)
        {
            projectParameterRows.Add(row);
        }
    }

    private static bool Contains(string value, string query)
    {
        return value?.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    private void ExportProjectParameters()
    {
        try
        {
            IReadOnlyList<ProjectParameterRow> rows = bindingService.CollectProjectParameters(document);
            if (rows.Count == 0)
            {
                statusText.Text = "В проекте не найдено параметров для экспорта.";
                return;
            }

            SaveFileDialog dialog = new()
            {
                Title = "Экспорт параметров проекта",
                Filter = "CSV files (*.csv)|*.csv",
                FileName = "truebim-project-parameters.csv",
                AddExtension = true,
                DefaultExt = ".csv"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            File.WriteAllText(dialog.FileName, projectParameterExportService.BuildCsv(rows), System.Text.Encoding.UTF8);
            statusText.Text = $"Параметры проекта экспортированы: {rows.Count}. Файл: {dialog.FileName}";
            reportText.Text = $"Экспорт параметров проекта завершён.\n\nФайл: {dialog.FileName}\nСтрок: {rows.Count}";
        }
        catch (Exception exception)
        {
            logger.Error("Failed to export project parameters.", exception);
            TaskDialog.Show("ParaManager", "Не удалось экспортировать параметры проекта. Используйте логи для диагностики.");
        }
    }

    private void ExportTemplate()
    {
        SaveFileDialog dialog = new()
        {
            Title = "Сохранить CSV-шаблон ParaManager",
            Filter = "CSV files (*.csv)|*.csv",
            FileName = "truebim-paramanager-template.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, csvImportService.CreateTemplate(), System.Text.Encoding.UTF8);
        statusText.Text = $"CSV-шаблон сохранён: {dialog.FileName}. Заполните строки и загрузите файл через «Выбрать CSV».";
    }

    private void ApplyImport()
    {
        try
        {
            if (importRows.Count == 0)
            {
                LoadAndValidate();
            }

            ValidateRows();
            RefreshImportRows();
            int applyCount = importRows.Count(row => row.CanApply);
            if (applyCount == 0)
            {
                statusText.Text = "Нет строк для импорта.";
                return;
            }

            if (string.IsNullOrWhiteSpace(sharedParameterPathInput.Text) || !File.Exists(sharedParameterPathInput.Text))
            {
                statusText.Text = "Выберите существующий shared parameter .txt.";
                return;
            }

            if (!ConfirmApply(applyCount))
            {
                return;
            }

            QueueImport();
        }
        catch (Exception exception)
        {
            logger.Error("Failed to import ParaManager parameters.", exception);
            TaskDialog.Show("ParaManager", "Не удалось импортировать параметры. Используйте логи для диагностики.");
        }
    }

    private void QueueImport()
    {
        statusText.Text = "Импорт поставлен в очередь Revit. Операция будет выполнена через ExternalEvent.";
        logger.Info($"ParaManager import requested for {importRows.Count(row => row.CanApply)} row(s).");
        importEvent.Raise();
    }

    private void RunImport()
    {
        try
        {
            ParameterImportResult result = bindingService.Import(uiApplication.Application, document, sharedParameterPathInput.Text, importRows);
            importRows = result.Rows.ToList();
            RefreshImportRows();
            RefreshProjectParameters();
            reportText.Text = result.ToDialogText();
            statusText.Text = $"Создано: {result.CreatedCount}. Обновлено: {result.UpdatedCount}. Пропущено: {result.SkippedCount}. Ошибок: {result.FailedCount}.";
            TaskDialog.Show("ParaManager", result.ToDialogText());
        }
        catch (Exception exception)
        {
            logger.Error("Failed to import ParaManager parameters.", exception);
            TaskDialog.Show("ParaManager", "Не удалось импортировать параметры. Используйте логи для диагностики.");
        }
    }

    private static bool ConfirmApply(int count)
    {
        TaskDialog dialog = new("ParaManager")
        {
            MainInstruction = $"Будет создано или обновлено параметров: {count}.",
            MainContent = "Операция изменит привязки project parameters в текущем документе Revit. Продолжить?",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No
        };

        return dialog.Show() == TaskDialogResult.Yes;
    }

    private void UpdateCategoryPresetText()
    {
        categoryPresetInput.Text = selectedCategoryPreset.Count == 0
            ? "Категории не выбраны."
            : string.Join(", ", selectedCategoryPreset);
    }

    private void UpdateStatus()
    {
        if (importRows.Count == 0)
        {
            statusText.Text = $"Параметров проекта: {allProjectParameterRows.Count}. Добавьте параметр вручную или загрузите CSV-шаблон.";
            return;
        }

        int willCreate = importRows.Count(row => row.Status == ParameterImportStatus.WillCreate);
        int willUpdate = importRows.Count(row => row.Status == ParameterImportStatus.WillUpdate);
        int invalid = importRows.Count(row => row.Status is ParameterImportStatus.Invalid or ParameterImportStatus.Empty or ParameterImportStatus.DuplicateInFile);
        statusText.Text = $"Строк импорта: {importRows.Count}. Будет создано: {willCreate}. Будет обновлено: {willUpdate}. Ошибок проверки: {invalid}.";
    }

    private static Brush GetStatusBrush(ParameterImportStatus status)
    {
        return status switch
        {
            ParameterImportStatus.WillCreate => TrueBimBrushes.Success,
            ParameterImportStatus.WillUpdate => TrueBimBrushes.Info,
            ParameterImportStatus.Created => TrueBimBrushes.Success,
            ParameterImportStatus.Updated => TrueBimBrushes.Info,
            ParameterImportStatus.Invalid => TrueBimBrushes.Danger,
            ParameterImportStatus.Failed => TrueBimBrushes.Danger,
            ParameterImportStatus.DuplicateInFile => TrueBimBrushes.Warning,
            _ => TrueBimBrushes.TextSecondary
        };
    }

    private sealed class ParaManagerImportExternalEventHandler : IExternalEventHandler
    {
        private readonly ParaManagerWindow window;

        public ParaManagerImportExternalEventHandler(ParaManagerWindow window)
        {
            this.window = window ?? throw new ArgumentNullException(nameof(window));
        }

        public void Execute(UIApplication app)
        {
            window.RunImport();
        }

        public string GetName()
        {
            return "TrueBIM ParaManager Import";
        }
    }

    private static Button CreateSmallButton(string text, TrueBimIcon icon, RoutedEventHandler clickHandler, string? toolTip = null)
    {
        Button button = TrueBimUi.CreateSecondaryButton(text, icon, minWidth: 110);
        button.ToolTip = toolTip;
        button.Click += clickHandler;
        return button;
    }
}
