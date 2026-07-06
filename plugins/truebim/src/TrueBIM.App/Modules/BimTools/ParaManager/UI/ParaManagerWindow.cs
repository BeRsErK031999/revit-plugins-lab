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
    private readonly ParaManagerValidationService validationService;
    private readonly ProjectParameterBindingService bindingService;
    private readonly ITrueBimLogger logger;
    private readonly WpfTextBox sharedParameterPathInput = new();
    private readonly WpfTextBox csvPathInput = new();
    private readonly ListBox importRowList = new();
    private readonly ListBox projectParameterList = new();
    private readonly WpfTextBox reportText = new();
    private readonly TextBlock statusText = new();
    private List<ParameterImportRow> importRows = [];

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
        this.validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
        this.bindingService = bindingService ?? throw new ArgumentNullException(nameof(bindingService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Title = "ParaManager";
        Icon = IconFactory.CreateImage(TrueBimIcon.Parameters, 32);
        Width = 980;
        Height = 700;
        MinWidth = 900;
        MinHeight = 620;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();
        sharedParameterPathInput.Text = uiApplication.Application.SharedParametersFilename ?? string.Empty;
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

    private StackPanel CreateHeader()
    {
        StackPanel header = new();
        header.Children.Add(new TextBlock
        {
            Text = "ParaManager",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = "Импорт shared parameters в проект из CSV. Семейства, .xlsx и редактор shared parameter file будут отдельными этапами.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12)
        });
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

        Button refreshButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Preview, "Обновить список"),
            Height = 30,
            MinWidth = 160,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8)
        };
        refreshButton.Click += (_, _) => RefreshProjectParameters();
        panel.Children.Add(refreshButton);

        projectParameterList.BorderBrush = Brushes.LightGray;
        projectParameterList.BorderThickness = new Thickness(1);
        projectParameterList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        WpfGrid.SetRow(projectParameterList, 1);
        panel.Children.Add(projectParameterList);

        return new TabItem
        {
            Header = "Параметры проекта",
            Content = panel
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
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        UIElement sharedFileBar = CreatePathBar(sharedParameterPathInput, "Shared parameter file (*.txt)|*.txt|All files (*.*)|*.*", "Выбрать shared parameters", ChooseSharedParameterFile);
        panel.Children.Add(sharedFileBar);

        UIElement csvFileBar = CreatePathBar(csvPathInput, "CSV files (*.csv)|*.csv|All files (*.*)|*.*", "Выбрать CSV", ChooseCsvFile);
        WpfGrid.SetRow(csvFileBar, 1);
        panel.Children.Add(csvFileBar);

        StackPanel toolbar = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        toolbar.Children.Add(CreateSmallButton("Проверить", (_, _) => LoadAndValidate()));
        Button templateButton = CreateSmallButton("Экспортировать шаблон", (_, _) => ExportTemplate());
        templateButton.Margin = new Thickness(8, 0, 0, 0);
        toolbar.Children.Add(templateButton);
        WpfGrid.SetRow(toolbar, 2);
        panel.Children.Add(toolbar);

        importRowList.BorderBrush = Brushes.LightGray;
        importRowList.BorderThickness = new Thickness(1);
        importRowList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        WpfGrid.SetRow(importRowList, 3);
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
            Height = 32
        };
        applyButton.Click += (_, _) => ApplyImport();
        footer.Children.Add(applyButton);
        WpfGrid.SetRow(footer, 4);
        panel.Children.Add(footer);

        return new TabItem
        {
            Header = "Импорт CSV",
            Content = panel
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
            Content = new WpfGrid
            {
                Margin = new Thickness(10),
                Children = { reportText }
            }
        };
    }

    private static DockPanel CreatePathBar(WpfTextBox input, string filter, string buttonText, RoutedEventHandler clickHandler)
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
            Margin = new Thickness(8, 0, 0, 0)
        };
        chooseButton.Click += clickHandler;
        DockPanel.SetDock(chooseButton, Dock.Right);
        fileBar.Children.Add(chooseButton);

        input.Height = 30;
        input.VerticalContentAlignment = VerticalAlignment.Center;
        fileBar.Children.Add(input);

        return fileBar;
    }

    private void ChooseSharedParameterFile(object sender, RoutedEventArgs args)
    {
        ChooseFile(sharedParameterPathInput, "Shared parameter file (*.txt)|*.txt|All files (*.*)|*.*");
    }

    private void ChooseCsvFile(object sender, RoutedEventArgs args)
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

    private void LoadAndValidate()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(csvPathInput.Text) || !File.Exists(csvPathInput.Text))
            {
                statusText.Text = "Выберите существующий CSV-файл.";
                return;
            }

            importRows = csvImportService.Read(csvPathInput.Text).ToList();
            ValidateRows();
            RefreshImportRows();
        }
        catch (Exception exception)
        {
            logger.Error("Failed to read ParaManager CSV.", exception);
            TaskDialog.Show("ParaManager", "Не удалось прочитать CSV-файл. Используйте логи для диагностики.");
        }
    }

    private void ValidateRows()
    {
        CategoryResolveService categoryResolveService = new();
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
        DockPanel panel = new()
        {
            LastChildFill = true,
            Margin = new Thickness(8, 6, 8, 6)
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
            Text = $"Строка {row.LineNumber}. {row.BindingType} | {row.DataType} | {row.GroupUnder} | {row.Categories}. {row.Message}",
            Foreground = Brushes.DimGray,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(textPanel);

        return panel;
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

    private static UIElement CreateProjectParameterRow(ProjectParameterRow row)
    {
        DockPanel panel = new()
        {
            LastChildFill = true,
            Margin = new Thickness(8, 6, 8, 6)
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
            Title = "Сохранить шаблон ParaManager",
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
        statusText.Text = $"Шаблон сохранён: {dialog.FileName}";
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

    private void UpdateStatus()
    {
        if (importRows.Count == 0)
        {
            statusText.Text = $"Параметров проекта: {projectParameterList.Items.Count}. CSV не выбран.";
            return;
        }

        int willCreate = importRows.Count(row => row.Status == ParameterImportStatus.WillCreate);
        int willUpdate = importRows.Count(row => row.Status == ParameterImportStatus.WillUpdate);
        int invalid = importRows.Count(row => row.Status is ParameterImportStatus.Invalid or ParameterImportStatus.Empty or ParameterImportStatus.DuplicateInFile);
        statusText.Text = $"Строк CSV: {importRows.Count}. Будет создано: {willCreate}. Будет обновлено: {willUpdate}. Ошибок проверки: {invalid}.";
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

    private static Button CreateSmallButton(string text, RoutedEventHandler clickHandler)
    {
        Button button = new()
        {
            Content = text,
            Height = 28,
            MinWidth = 110
        };
        button.Click += clickHandler;
        return button;
    }
}
