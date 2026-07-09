using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.Modules.BimTools.ParaManager.Models;
using TrueBIM.App.Modules.BimTools.ParaManager.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace TrueBIM.App.Modules.BimTools.ParaManager.UI;

public sealed class ParaManagerWindow : Window
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
    private readonly WpfTextBox sharedParameterPathInput = new();
    private readonly WpfTextBox csvPathInput = new();
    private readonly WpfTextBox categoryPresetInput = new();
    private readonly ListBox importRowList = new();
    private readonly ListBox projectParameterList = new();
    private readonly WpfTextBox reportText = new();
    private readonly TextBlock statusText = new();
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
        WpfGrid root = new()
        {
            Margin = new Thickness(16)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(CreateHeader());

        TabControl tabs = new();
        tabs.Items.Add(CreateProjectParametersTab());
        tabs.Items.Add(CreateImportTab());
        tabs.Items.Add(CreateReportTab());
        WpfGrid.SetRow(tabs, 1);
        root.Children.Add(tabs);

        statusText.Foreground = Brushes.DimGray;
        statusText.Margin = new Thickness(0, 10, 0, 10);
        statusText.TextWrapping = TextWrapping.Wrap;
        WpfGrid.SetRow(statusText, 2);
        root.Children.Add(statusText);

        StackPanel footer = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Button closeButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Закрыть"),
            MinWidth = 110,
            Height = 32,
            IsCancel = true
        };
        closeButton.Click += (_, _) => Close();
        footer.Children.Add(closeButton);
        WpfGrid.SetRow(footer, 3);
        root.Children.Add(footer);

        return root;
    }

    private UIElement CreateHeader()
    {
        DockPanel header = new()
        {
            Margin = new Thickness(0, 0, 0, 12)
        };

        Button helpButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Help, "Справка"),
            Height = 30,
            MinWidth = 110,
            ToolTip = "Показать короткую подсказку по ручному добавлению, CSV и shared parameter file."
        };
        helpButton.Click += (_, _) => ShowHelp();
        DockPanel.SetDock(helpButton, Dock.Right);
        header.Children.Add(helpButton);

        StackPanel textPanel = new()
        {
            Margin = new Thickness(0, 0, 12, 0)
        };
        textPanel.Children.Add(new TextBlock
        {
            Text = "ParaManager",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = "Создание и привязка project/shared parameters к категориям текущей модели Revit.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        header.Children.Add(textPanel);
        return header;
    }

    private TabItem CreateProjectParametersTab()
    {
        WpfGrid panel = new()
        {
            Margin = new Thickness(10)
        };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        StackPanel toolbar = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };

        Button refreshButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Preview, "Обновить список"),
            Height = 30,
            MinWidth = 160,
            ToolTip = "Снова прочитать project parameters из текущего документа Revit."
        };
        refreshButton.Click += (_, _) => RefreshProjectParameters();
        toolbar.Children.Add(refreshButton);

        Button exportButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Export, "Экспорт списка"),
            Height = 30,
            MinWidth = 130,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Сохранить текущие project parameters в CSV для проверки или передачи BIM-координатору."
        };
        exportButton.Click += (_, _) => ExportProjectParameters();
        toolbar.Children.Add(exportButton);
        panel.Children.Add(toolbar);

        projectParameterList.BorderBrush = Brushes.LightGray;
        projectParameterList.BorderThickness = new Thickness(1);
        projectParameterList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        projectParameterList.ToolTip = "Список параметров, уже привязанных к категориям проекта.";
        WpfGrid.SetRow(projectParameterList, 1);
        panel.Children.Add(projectParameterList);

        return new TabItem
        {
            Header = "Параметры проекта",
            Content = panel,
            ToolTip = "Посмотреть параметры, которые уже есть в модели."
        };
    }

    private TabItem CreateImportTab()
    {
        WpfGrid panel = new()
        {
            Margin = new Thickness(10)
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
            Margin = new Thickness(0, 0, 0, 8)
        };
        Button addButton = CreateSmallButton(
            "Добавить параметр",
            (_, _) => AddManualParameter(),
            "Создать одну строку параметра вручную без CSV.");
        toolbar.Children.Add(addButton);

        Button validateButton = CreateSmallButton(
            "Проверить",
            (_, _) => LoadAndValidate(),
            "Прочитать CSV и проверить имена, типы, категории и существующие параметры проекта.");
        validateButton.Margin = new Thickness(8, 0, 0, 0);
        toolbar.Children.Add(validateButton);

        Button templateButton = CreateSmallButton(
            "CSV-шаблон",
            (_, _) => ExportTemplate(),
            "Сохранить CSV-шаблон с примерами строк. Заполните его и загрузите через «Выбрать CSV».");
        templateButton.Margin = new Thickness(8, 0, 0, 0);
        toolbar.Children.Add(templateButton);

        Button removeButton = CreateSmallButton(
            "Убрать выбранные",
            (_, _) => RemoveSelectedRows(),
            "Удалить выделенные строки из текущего списка импорта. Модель Revit при этом не меняется.");
        removeButton.Margin = new Thickness(8, 0, 0, 0);
        toolbar.Children.Add(removeButton);
        WpfGrid.SetRow(toolbar, 3);
        panel.Children.Add(toolbar);

        importRowList.BorderBrush = Brushes.LightGray;
        importRowList.BorderThickness = new Thickness(1);
        importRowList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        importRowList.SelectionMode = SelectionMode.Extended;
        importRowList.ToolTip = "Выделите строки, чтобы присвоить им выбранные категории или убрать их из списка.";
        WpfGrid.SetRow(importRowList, 4);
        panel.Children.Add(importRowList);

        StackPanel footer = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        Button applyButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Apply, "Применить импорт"),
            MinWidth = 170,
            Height = 32,
            ToolTip = "Записать проверенные параметры в shared parameter file и привязать их к категориям текущего проекта."
        };
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
        reportText.Text = "Импорт ещё не выполнялся.";

        return new TabItem
        {
            Header = "Отчёт",
            ToolTip = "Итоги последнего применения импорта и ошибки по строкам.",
            Content = new WpfGrid
            {
                Margin = new Thickness(10),
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
            Margin = new Thickness(0, 0, 0, 8),
            Tag = filter
        };

        Button chooseButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Open, buttonText),
            Height = 30,
            MinWidth = 190,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = buttonToolTip
        };
        chooseButton.Click += clickHandler;
        DockPanel.SetDock(chooseButton, Dock.Right);
        fileBar.Children.Add(chooseButton);

        input.Height = 30;
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
            Margin = new Thickness(0, 0, 0, 8)
        };

        Button applyButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Apply, "Присвоить категории"),
            Height = 30,
            MinWidth = 195,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Записать выбранный набор категорий в выделенные строки импорта. Это ещё не меняет модель."
        };
        applyButton.Click += (_, _) => ApplyCategoriesToSelectedRows();
        DockPanel.SetDock(applyButton, Dock.Right);
        categoryBar.Children.Add(applyButton);

        Button chooseButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Open, "Категории"),
            Height = 30,
            MinWidth = 125,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Выбрать категории Revit, которым нужно добавить параметры."
        };
        chooseButton.Click += (_, _) => ChooseCategories();
        DockPanel.SetDock(chooseButton, Dock.Right);
        categoryBar.Children.Add(chooseButton);

        categoryPresetInput.Height = 30;
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
            Margin = new Thickness(8, 6, 8, 6),
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
            Foreground = Brushes.DimGray,
            FontSize = 12,
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
            projectParameterList.Items.Clear();
            foreach (ProjectParameterRow row in bindingService.CollectProjectParameters(document))
            {
                projectParameterList.Items.Add(CreateProjectParameterRow(row));
            }

            UpdateStatus();
        }
        catch (Exception exception)
        {
            logger.Error("Failed to collect project parameters.", exception);
            TaskDialog.Show("ParaManager", "Не удалось собрать параметры проекта. Используйте логи для диагностики.");
        }
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

    private static UIElement CreateProjectParameterRow(ProjectParameterRow row)
    {
        DockPanel panel = new()
        {
            LastChildFill = true,
            Margin = new Thickness(8, 6, 8, 6),
            ToolTip = $"{row.BindingTypeDisplay} parameter. Раздел: {row.GroupDisplay}. Категории: {row.CategoriesDisplay}."
        };

        TextBlock typeText = new()
        {
            Text = row.BindingTypeDisplay,
            Width = 80,
            Foreground = Brushes.DimGray,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(typeText, Dock.Right);
        panel.Children.Add(typeText);

        StackPanel textPanel = new();
        textPanel.Children.Add(new TextBlock
        {
            Text = row.Name,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        string sharedText = row.IsShared ? $"Shared GUID: {row.GuidDisplay}" : "Project/internal";
        textPanel.Children.Add(new TextBlock
        {
            Text = $"{row.DataTypeDisplay} | {row.GroupDisplay} | {sharedText} | {row.CategoriesDisplay}",
            Foreground = Brushes.DimGray,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(textPanel);

        return panel;
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
            statusText.Text = $"Параметров проекта: {projectParameterList.Items.Count}. Добавьте параметр вручную или загрузите CSV-шаблон.";
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
            ParameterImportStatus.WillCreate => new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 110, 70)),
            ParameterImportStatus.WillUpdate => new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 90, 150)),
            ParameterImportStatus.Created => new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 110, 70)),
            ParameterImportStatus.Updated => new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 90, 150)),
            ParameterImportStatus.Invalid => Brushes.DarkRed,
            ParameterImportStatus.Failed => Brushes.DarkRed,
            ParameterImportStatus.DuplicateInFile => new SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 90, 20)),
            _ => Brushes.DimGray
        };
    }

    private static Button CreateSmallButton(string text, RoutedEventHandler clickHandler, string? toolTip = null)
    {
        Button button = new()
        {
            Content = text,
            Height = 28,
            MinWidth = 110,
            ToolTip = toolTip
        };
        button.Click += clickHandler;
        return button;
    }
}
