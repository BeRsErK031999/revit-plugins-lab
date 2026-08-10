using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Microsoft.Win32;
using TrueBIM.App.Modules.BimTools.ParameterAudit.Models;
using TrueBIM.App.Modules.BimTools.ParameterAudit.Revit;
using TrueBIM.App.Modules.BimTools.ParameterAudit.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace TrueBIM.App.Modules.BimTools.ParameterAudit.UI;

public sealed class ParameterAuditWindow : TrueBimWindow
{
    private readonly Document document;
    private readonly ParameterAuditProfileReader profileReader;
    private readonly ParameterAuditService auditService;
    private readonly ParameterAuditReportExportService exportService;
    private readonly ITrueBimLogger logger;
    private readonly RevitActionDispatcher revitActions;
    private readonly WpfTextBox pathInput = new();
    private readonly CheckBox includeLinksInput = new();
    private readonly TextBlock statusText = new();
    private readonly DataGrid ruleGrid = CreateDataGrid();
    private readonly DataGrid profileIssueGrid = CreateDataGrid();
    private readonly DataGrid resultGrid = CreateDataGrid();
    private readonly TabControl tabs = new();
    private readonly Button runButton = new();
    private readonly Button exportButton = new();
    private ParameterAuditProfile? profile;
    private ParameterAuditReport? report;

    public ParameterAuditWindow(
        Document document,
        ParameterAuditProfileReader profileReader,
        ParameterAuditService auditService,
        ParameterAuditReportExportService exportService,
        ITrueBimLogger logger)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        this.profileReader = profileReader ?? throw new ArgumentNullException(nameof(profileReader));
        this.auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        this.exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        revitActions = new RevitActionDispatcher("проверка параметров", this.logger);

        Title = "Проверка параметров";
        Icon = IconFactory.CreateImage(TrueBimIcon.Check, 32);
        Width = 1280;
        Height = 780;
        MinWidth = 960;
        MinHeight = 620;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ConfigureGrids();
        Content = CreateContent();
        UpdateStatus("Выберите исходную матрицу XLSX/CSV или расширенную таблицу правил.");
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

        UIElement fileBar = CreateFileBar();
        WpfGrid.SetRow(fileBar, 1);
        root.Children.Add(fileBar);

        UIElement toolbar = CreateToolbar();
        WpfGrid.SetRow(toolbar, 2);
        root.Children.Add(toolbar);

        tabs.Margin = new Thickness(0, 4, 0, 0);
        tabs.Items.Add(new TabItem { Header = "Правила", Content = ruleGrid });
        tabs.Items.Add(new TabItem { Header = "Ошибки таблицы", Content = profileIssueGrid });
        tabs.Items.Add(new TabItem { Header = "Результат", Content = resultGrid });
        WpfGrid.SetRow(tabs, 3);
        root.Children.Add(tabs);

        statusText.Foreground = Brushes.DimGray;
        statusText.Margin = new Thickness(0, 10, 0, 10);
        statusText.TextWrapping = TextWrapping.Wrap;
        WpfGrid.SetRow(statusText, 4);
        root.Children.Add(statusText);

        UIElement footer = CreateFooter();
        WpfGrid.SetRow(footer, 5);
        root.Children.Add(footer);
        return root;
    }

    private static StackPanel CreateHeader()
    {
        StackPanel header = new();
        header.Children.Add(new TextBlock
        {
            Text = "Проверка параметров",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = "Read-only проверка заполненности по матрице: A1 — параметр отбора, колонка A — его значения, знак + — обязательный параметр.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12)
        });
        return header;
    }

    private UIElement CreateFileBar()
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
            MinWidth = 140,
            Margin = new Thickness(8, 0, 0, 0)
        };
        chooseButton.Click += (_, _) => ChooseProfileFile();
        DockPanel.SetDock(chooseButton, Dock.Right);
        fileBar.Children.Add(chooseButton);

        pathInput.Height = 30;
        pathInput.VerticalContentAlignment = VerticalAlignment.Center;
        pathInput.ToolTip = "CSV или XLSX. XLSX можно выбирать прямо из Excel после сохранения; читается первый лист.";
        fileBar.Children.Add(pathInput);
        return fileBar;
    }

    private UIElement CreateToolbar()
    {
        DockPanel container = new()
        {
            LastChildFill = true,
            Margin = new Thickness(0, 0, 0, 8)
        };
        includeLinksInput.Content = "Проверять загруженные RVT-связи";
        includeLinksInput.VerticalAlignment = VerticalAlignment.Center;
        includeLinksInput.ToolTip = "Связи проверяются только для чтения. ElementId в отчёте относится к связанному документу.";
        DockPanel.SetDock(includeLinksInput, Dock.Right);
        container.Children.Add(includeLinksInput);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal
        };
        Button validateButton = CreateSmallButton("Проверить таблицу", (_, _) => LoadProfile());
        buttons.Children.Add(validateButton);

        Button templateButton = CreateSmallButton("Сохранить шаблон CSV", (_, _) => ExportTemplate());
        templateButton.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(templateButton);

        Button guideButton = CreateSmallButton("Методичка", (_, _) => ShowGuide());
        guideButton.Content = IconFactory.CreateButtonContent(TrueBimIcon.Help, "Методичка");
        guideButton.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(guideButton);

        runButton.Content = IconFactory.CreateButtonContent(TrueBimIcon.Check, "Запустить проверку");
        runButton.Height = 30;
        runButton.MinWidth = 170;
        runButton.Margin = new Thickness(8, 0, 0, 0);
        runButton.IsEnabled = false;
        runButton.Click += (_, _) => RunAudit();
        buttons.Children.Add(runButton);
        container.Children.Add(buttons);
        return container;
    }

    private void ShowGuide()
    {
        ParameterAuditGuideWindow guide = new()
        {
            Owner = this
        };
        guide.ShowDialog();
    }

    private UIElement CreateFooter()
    {
        StackPanel footer = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        exportButton.Content = IconFactory.CreateButtonContent(TrueBimIcon.Export, "Экспорт ошибок CSV");
        exportButton.MinWidth = 180;
        exportButton.Height = 32;
        exportButton.Margin = new Thickness(0, 0, 8, 0);
        exportButton.IsEnabled = false;
        exportButton.Click += (_, _) => ExportReport();
        footer.Children.Add(exportButton);

        Button closeButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Закрыть"),
            MinWidth = 110,
            Height = 32,
            IsCancel = true
        };
        closeButton.Click += (_, _) => Close();
        footer.Children.Add(closeButton);
        return footer;
    }

    private void ConfigureGrids()
    {
        AddTextColumn(ruleGrid, "Строка", nameof(ParameterAuditRule.LineNumber), 65);
        AddTextColumn(ruleGrid, "RuleId", nameof(ParameterAuditRule.RuleId), 140);
        AddTextColumn(ruleGrid, "Отбор элементов", nameof(ParameterAuditRule.SelectionDisplay), 260);
        AddTextColumn(ruleGrid, "Категория", nameof(ParameterAuditRule.CategoryPattern), 130);
        AddTextColumn(ruleGrid, "Семейство", nameof(ParameterAuditRule.FamilyPattern), 130);
        AddTextColumn(ruleGrid, "Тип", nameof(ParameterAuditRule.TypePattern), 130);
        AddTextColumn(ruleGrid, "Параметр", nameof(ParameterAuditRule.ParameterDisplay), 160);
        AddTextColumn(ruleGrid, "Область", nameof(ParameterAuditRule.ScopeDisplay), 90);
        AddTextColumn(ruleGrid, "Требование", nameof(ParameterAuditRule.ExpectedDescription), 260);
        AddTextColumn(ruleGrid, "Критичность", nameof(ParameterAuditRule.SeverityDisplay), 110);

        AddTextColumn(profileIssueGrid, "Строка", nameof(ParameterAuditProfileIssue.LineNumber), 65);
        AddTextColumn(profileIssueGrid, "RuleId", nameof(ParameterAuditProfileIssue.RuleId), 140);
        AddTextColumn(profileIssueGrid, "Уровень", nameof(ParameterAuditProfileIssue.SeverityDisplay), 110);
        AddTextColumn(profileIssueGrid, "Сообщение", nameof(ParameterAuditProfileIssue.Message), 700);

        AddTextColumn(resultGrid, "Уровень", nameof(ParameterAuditResultRow.SeverityDisplay), 105);
        AddTextColumn(resultGrid, "RuleId", nameof(ParameterAuditResultRow.RuleId), 130);
        AddTextColumn(resultGrid, "Модель", nameof(ParameterAuditResultRow.SourceDisplay), 180);
        AddTextColumn(resultGrid, "ElementId", nameof(ParameterAuditResultRow.ElementIdDisplay), 90);
        AddTextColumn(resultGrid, "Категория", nameof(ParameterAuditResultRow.CategoryName), 125);
        AddTextColumn(resultGrid, "Семейство", nameof(ParameterAuditResultRow.FamilyName), 135);
        AddTextColumn(resultGrid, "Тип", nameof(ParameterAuditResultRow.TypeName), 135);
        AddTextColumn(resultGrid, "Параметр", nameof(ParameterAuditResultRow.ParameterName), 150);
        AddTextColumn(resultGrid, "Фактически", nameof(ParameterAuditResultRow.ActualValue), 140);
        AddTextColumn(resultGrid, "Требование", nameof(ParameterAuditResultRow.ExpectedValue), 190);
        AddTextColumn(resultGrid, "Ошибка", nameof(ParameterAuditResultRow.Message), 360);
    }

    private void ChooseProfileFile()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Выберите таблицу правил проверки параметров",
            Filter = "Таблицы правил (*.xlsx;*.csv)|*.xlsx;*.csv|Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        pathInput.Text = dialog.FileName;
        LoadProfile();
    }

    private void LoadProfile()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pathInput.Text) || !File.Exists(pathInput.Text))
            {
                UpdateStatus("Выберите существующий CSV или XLSX-файл.");
                return;
            }

            profile = profileReader.Read(pathInput.Text);
            report = null;
            ruleGrid.ItemsSource = profile.Rules;
            profileIssueGrid.ItemsSource = profile.Issues;
            resultGrid.ItemsSource = null;
            runButton.IsEnabled = profile.IsValid;
            exportButton.IsEnabled = false;
            int enabledRules = profile.Rules.Count(rule => rule.Enabled);
            int errors = profile.Issues.Count(issue => issue.Severity == ParameterAuditProfileIssueSeverity.Error);
            int warnings = profile.Issues.Count(issue => issue.Severity == ParameterAuditProfileIssueSeverity.Warning);
            UpdateStatus($"Правил: {profile.Rules.Count}; включено: {enabledRules}; ошибок таблицы: {errors}; предупреждений: {warnings}.");
            tabs.SelectedIndex = errors > 0 ? 1 : 0;
        }
        catch (Exception exception)
        {
            profile = null;
            report = null;
            ruleGrid.ItemsSource = null;
            profileIssueGrid.ItemsSource = null;
            resultGrid.ItemsSource = null;
            runButton.IsEnabled = false;
            exportButton.IsEnabled = false;
            UpdateStatus($"Не удалось прочитать таблицу: {exception.Message}");
            logger.Error("Failed to read Parameter Audit profile.", exception);
        }
    }

    private void RunAudit()
    {
        LoadProfile();
        if (profile?.IsValid != true)
        {
            return;
        }

        runButton.IsEnabled = false;
        exportButton.IsEnabled = false;
        UpdateStatus("Проверка поставлена в очередь Revit…");
        if (!revitActions.Raise(RunAuditInRevitContext))
        {
            runButton.IsEnabled = true;
        }
    }

    private void RunAuditInRevitContext()
    {
        try
        {
            if (!document.IsValidObject)
            {
                throw new InvalidOperationException("Документ, для которого открыто окно, уже закрыт.");
            }

            ParameterAuditProfile currentProfile = profile
                ?? throw new InvalidOperationException("Таблица правил не загружена.");
            report = auditService.Analyze(document, currentProfile.Rules, includeLinksInput.IsChecked == true);
            resultGrid.ItemsSource = report.Rows;
            exportButton.IsEnabled = report.Rows.Count > 0;
            UpdateStatus(
                $"Проверено сочетаний элемент–правило: {report.CheckedCount}; прошло: {report.PassedCount}; "
                + $"ошибок: {report.ErrorCount}; предупреждений: {report.WarningCount}.");
            tabs.SelectedIndex = 2;
        }
        catch (Exception exception)
        {
            logger.Error("Parameter Audit failed.", exception);
            UpdateStatus($"Проверка не выполнена: {exception.Message}");
            Autodesk.Revit.UI.TaskDialog.Show(
                "Проверка параметров",
                "Не удалось выполнить проверку. Используйте логи TrueBIM для диагностики.");
        }
        finally
        {
            runButton.IsEnabled = profile?.IsValid == true;
        }
    }

    private void ExportTemplate()
    {
        SaveFileDialog dialog = new()
        {
            Title = "Сохранить шаблон матрицы проверки параметров",
            Filter = "CSV для Excel (*.csv)|*.csv",
            FileName = "truebim-parameter-audit-matrix.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, profileReader.CreateTemplate(), new System.Text.UTF8Encoding(true));
            UpdateStatus($"Шаблон сохранён: {dialog.FileName}");
        }
        catch (Exception exception)
        {
            logger.Error("Failed to export Parameter Audit template.", exception);
            UpdateStatus($"Не удалось сохранить шаблон: {exception.Message}");
        }
    }

    private void ExportReport()
    {
        if (report is null)
        {
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "Экспортировать ошибки проверки параметров",
            Filter = "CSV для Excel (*.csv)|*.csv",
            FileName = $"truebim-parameter-audit-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            exportService.ExportCsv(dialog.FileName, report);
            UpdateStatus($"Отчёт сохранён: {dialog.FileName}");
        }
        catch (Exception exception)
        {
            logger.Error("Failed to export Parameter Audit report.", exception);
            UpdateStatus($"Не удалось сохранить отчёт: {exception.Message}");
        }
    }

    private void UpdateStatus(string text)
    {
        statusText.Text = $"Модель: {document.Title}. {text}";
    }

    private static DataGrid CreateDataGrid()
    {
        return new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true
        };
    }

    private static void AddTextColumn(
        DataGrid grid,
        string header,
        string propertyName,
        double width)
    {
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new System.Windows.Data.Binding(propertyName),
            Width = new DataGridLength(width)
        });
    }

    private static Button CreateSmallButton(string text, RoutedEventHandler handler)
    {
        Button button = new()
        {
            Content = text,
            Height = 30,
            MinWidth = 130
        };
        button.Click += handler;
        return button;
    }
}
