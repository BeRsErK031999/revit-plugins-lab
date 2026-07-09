using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.Modules.BimTools.Worksets.Models;
using TrueBIM.App.Modules.BimTools.Worksets.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using WpfColor = System.Windows.Media.Color;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace TrueBIM.App.Modules.BimTools.Worksets.UI;

public sealed class CreateWorksetsWindow : TrueBimWindow
{
    private readonly Document document;
    private readonly WorksetCsvReader csvReader;
    private readonly WorksetValidationService validationService;
    private readonly WorksharingService worksharingService;
    private readonly WorksetCreationService creationService;
    private readonly ITrueBimLogger logger;
    private readonly WpfTextBox pathInput = new();
    private readonly TextBlock statusText = new();
    private readonly ListBox rowList = new();
    private List<WorksetImportRow> rows = new();

    public CreateWorksetsWindow(
        Document document,
        WorksetCsvReader csvReader,
        WorksetValidationService validationService,
        WorksharingService worksharingService,
        WorksetCreationService creationService,
        ITrueBimLogger logger)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        this.csvReader = csvReader ?? throw new ArgumentNullException(nameof(csvReader));
        this.validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
        this.worksharingService = worksharingService ?? throw new ArgumentNullException(nameof(worksharingService));
        this.creationService = creationService ?? throw new ArgumentNullException(nameof(creationService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Title = "Рабочие наборы";
        Icon = IconFactory.CreateImage(TrueBimIcon.Worksets, 32);
        Width = 900;
        Height = 650;
        MinWidth = 820;
        MinHeight = 560;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();
        UpdateStatus();
    }

    private UIElement CreateContent()
    {
        WpfGrid root = new()
        {
            Margin = new Thickness(16)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(CreateHeader());

        DockPanel fileBar = CreateFileBar();
        WpfGrid.SetRow(fileBar, 1);
        root.Children.Add(fileBar);

        StackPanel toolbar = CreateToolbar();
        WpfGrid.SetRow(toolbar, 2);
        root.Children.Add(toolbar);

        rowList.BorderBrush = Brushes.LightGray;
        rowList.BorderThickness = new Thickness(1);
        rowList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        WpfGrid.SetRow(rowList, 3);
        root.Children.Add(rowList);

        statusText.Foreground = Brushes.DimGray;
        statusText.Margin = new Thickness(0, 10, 0, 10);
        statusText.TextWrapping = TextWrapping.Wrap;
        WpfGrid.SetRow(statusText, 4);
        root.Children.Add(statusText);

        StackPanel footer = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        WpfGrid.SetRow(footer, 5);
        root.Children.Add(footer);

        Button createButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Apply, "Создать рабочие наборы"),
            MinWidth = 210,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0)
        };
        createButton.Click += (_, _) => CreateWorksets();
        footer.Children.Add(createButton);

        Button closeButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Закрыть"),
            MinWidth = 110,
            Height = 32,
            IsCancel = true
        };
        closeButton.Click += (_, _) => Close();
        footer.Children.Add(closeButton);

        return root;
    }

    private static StackPanel CreateHeader()
    {
        StackPanel header = new();
        header.Children.Add(new TextBlock
        {
            Text = "Рабочие наборы",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = "Выберите CSV или XLSX с одним рабочим набором в строке. Инструмент создаёт только пользовательские worksets и не распределяет элементы.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12)
        });
        return header;
    }

    private DockPanel CreateFileBar()
    {
        DockPanel fileBar = new()
        {
            LastChildFill = true,
            Margin = new Thickness(0, 0, 0, 8)
        };

        Button chooseButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Open, "Выбрать файл"),
            Height = 30,
            MinWidth = 130,
            Margin = new Thickness(8, 0, 0, 0)
        };
        chooseButton.Click += (_, _) => ChooseImportFile();
        DockPanel.SetDock(chooseButton, Dock.Right);
        fileBar.Children.Add(chooseButton);

        pathInput.Height = 30;
        pathInput.VerticalContentAlignment = VerticalAlignment.Center;
        pathInput.ToolTip = "Путь к CSV или XLSX-файлу.";
        fileBar.Children.Add(pathInput);

        return fileBar;
    }

    private StackPanel CreateToolbar()
    {
        StackPanel toolbar = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };

        Button validateButton = CreateSmallButton("Проверить", (_, _) => LoadAndValidate());
        toolbar.Children.Add(validateButton);

        Button templateButton = CreateSmallButton("Сохранить шаблон импорта", (_, _) => ExportTemplate());
        templateButton.Margin = new Thickness(8, 0, 0, 0);
        templateButton.ToolTip = "Сохраняет пустой CSV/XLSX-файл с колонкой WorksetName. Заполните его названиями рабочих наборов и загрузите через «Выбрать файл».";
        toolbar.Children.Add(templateButton);

        return toolbar;
    }

    private void ChooseImportFile()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Выберите файл рабочих наборов",
            Filter = "Workset files (*.xlsx;*.csv)|*.xlsx;*.csv|Excel workbook (*.xlsx)|*.xlsx|CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        pathInput.Text = dialog.FileName;
        LoadAndValidate();
    }

    private void LoadAndValidate()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pathInput.Text) || !File.Exists(pathInput.Text))
            {
                statusText.Text = "Выберите существующий CSV или XLSX-файл.";
                return;
            }

            rows = csvReader.Read(pathInput.Text).ToList();
            ValidateRows();
            RefreshList();
        }
        catch (Exception exception)
        {
            logger.Error("Failed to read workset import file.", exception);
            Autodesk.Revit.UI.TaskDialog.Show("Рабочие наборы", "Не удалось прочитать файл рабочих наборов. Используйте логи для диагностики.");
        }
    }

    private void ValidateRows()
    {
        ISet<string> existingNames = creationService.CollectExistingWorksetNames(document);
        validationService.Validate(rows, existingNames);
    }

    private void RefreshList()
    {
        rowList.Items.Clear();
        foreach (WorksetImportRow row in rows)
        {
            rowList.Items.Add(CreateRow(row));
        }

        UpdateStatus();
    }

    private static UIElement CreateRow(WorksetImportRow row)
    {
        DockPanel panel = new()
        {
            LastChildFill = true,
            Margin = new Thickness(8, 6, 8, 6)
        };

        TextBlock status = new()
        {
            Text = row.StatusDisplay,
            Width = 150,
            Foreground = GetStatusBrush(row.Status),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(status, Dock.Right);
        panel.Children.Add(status);

        StackPanel textPanel = new();
        textPanel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(row.WorksetName) ? "<пусто>" : row.WorksetName,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = $"Строка {row.LineNumber}. {row.Message}",
            Foreground = Brushes.DimGray,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(textPanel);

        return panel;
    }

    private void CreateWorksets()
    {
        try
        {
            if (rows.Count == 0)
            {
                statusText.Text = "Сначала выберите и проверьте CSV или XLSX-файл.";
                return;
            }

            ValidateRows();
            int createCount = rows.Count(row => row.CanCreate);
            if (createCount == 0)
            {
                RefreshList();
                statusText.Text = "Нет рабочих наборов для создания.";
                return;
            }

            if (!document.IsWorkshared && !ConfirmEnableWorksharing())
            {
                return;
            }

            if (!worksharingService.EnsureWorkshared(document, out string worksharingMessage))
            {
                statusText.Text = worksharingMessage;
                return;
            }

            ValidateRows();
            WorksetCreateResult result = creationService.Create(document, rows);
            rows = result.Rows.ToList();
            RefreshList();
            statusText.Text = string.IsNullOrWhiteSpace(worksharingMessage)
                ? $"Создано: {result.CreatedCount}. Пропущено: {result.SkippedCount + result.ExistingCount}. Ошибок: {result.FailedCount}."
                : $"{worksharingMessage} Создано: {result.CreatedCount}. Пропущено: {result.SkippedCount + result.ExistingCount}. Ошибок: {result.FailedCount}.";
            Autodesk.Revit.UI.TaskDialog.Show("Рабочие наборы", result.ToDialogText());
        }
        catch (Exception exception)
        {
            logger.Error("Failed to create worksets.", exception);
            Autodesk.Revit.UI.TaskDialog.Show("Рабочие наборы", "Не удалось создать рабочие наборы. Используйте логи для диагностики.");
        }
    }

    private static bool ConfirmEnableWorksharing()
    {
        TaskDialog dialog = new("Рабочие наборы")
        {
            MainInstruction = "В модели не включена совместная работа.",
            MainContent = "Для создания рабочих наборов Revit должен включить Worksharing. Операция может очистить историю Undo. Перед продолжением рекомендуется сохранить модель.\n\nПродолжить?",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No
        };

        return dialog.Show() == TaskDialogResult.Yes;
    }

    private void ExportTemplate()
    {
        SaveFileDialog dialog = new()
        {
            Title = "Сохранить шаблон рабочих наборов",
            Filter = "Excel workbook (*.xlsx)|*.xlsx|CSV files (*.csv)|*.csv",
            FileName = "truebim-worksets-template.xlsx",
            AddExtension = true,
            DefaultExt = ".xlsx"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        csvReader.WriteTemplate(dialog.FileName);
        statusText.Text = $"Шаблон сохранён: {dialog.FileName}";
    }

    private void UpdateStatus()
    {
        if (rows.Count == 0)
        {
            statusText.Text = "Файл не выбран.";
            return;
        }

        int willCreate = rows.Count(row => row.Status == WorksetImportStatus.WillCreate);
        int existing = rows.Count(row => row.Status == WorksetImportStatus.Existing);
        int skipped = rows.Count(row => row.Status is WorksetImportStatus.Empty or WorksetImportStatus.Invalid or WorksetImportStatus.DuplicateInFile);
        int created = rows.Count(row => row.Status == WorksetImportStatus.Created);
        int failed = rows.Count(row => row.Status == WorksetImportStatus.Failed);
        statusText.Text = $"Строк: {rows.Count}. Будет создано: {willCreate}. Уже существует: {existing}. Пропущено: {skipped}. Создано: {created}. Ошибок: {failed}.";
    }

    private static Brush GetStatusBrush(WorksetImportStatus status)
    {
        return status switch
        {
            WorksetImportStatus.WillCreate => new SolidColorBrush(WpfColor.FromRgb(30, 110, 70)),
            WorksetImportStatus.Created => new SolidColorBrush(WpfColor.FromRgb(30, 110, 70)),
            WorksetImportStatus.Failed => Brushes.DarkRed,
            WorksetImportStatus.Invalid => Brushes.DarkRed,
            WorksetImportStatus.DuplicateInFile => new SolidColorBrush(WpfColor.FromRgb(150, 90, 20)),
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
