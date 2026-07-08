using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using TrueBIM.App.Modules.BimTools.Common.Services.Export;
using TrueBIM.App.Modules.BimTools.DatumExtents.Models;
using TrueBIM.App.Modules.BimTools.DatumExtents.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using RevitDocument = Autodesk.Revit.DB.Document;
using RevitView = Autodesk.Revit.DB.View;
using WpfBinding = System.Windows.Data.Binding;
using WpfGrid = System.Windows.Controls.Grid;

namespace TrueBIM.App.Modules.BimTools.DatumExtents.UI;

public sealed class DatumExtentWindow : Window
{
    private readonly RevitDocument document;
    private readonly RevitView activeView;
    private readonly DatumExtentCollectorService collectorService;
    private readonly DatumExtentService datumExtentService;
    private readonly DatumExtentProfileStorage profileStorage;
    private readonly ITrueBimLogger logger;
    private readonly CsvExportService csvExportService = new();
    private readonly DatumExtentReportCsvService reportCsvService = new();
    private readonly ObservableCollection<DatumExtentRow> rows = new();
    private readonly ObservableCollection<DatumExtentReportRow> reportRows = new();
    private readonly TextBox profileNameInput = new();
    private readonly TextBox filterInput = new();
    private readonly ComboBox targetTypeInput = new();
    private readonly CheckBox includeEnd0Input = new()
    {
        Content = "End0",
        IsChecked = true,
        ToolTip = "Переключать первый конец datum-элемента."
    };
    private readonly CheckBox includeEnd1Input = new()
    {
        Content = "End1",
        IsChecked = true,
        ToolTip = "Переключать второй конец datum-элемента."
    };
    private readonly CheckBox includeGridsInput = new()
    {
        Content = "Оси",
        IsChecked = true,
        ToolTip = "Показывать Grid на активном виде."
    };
    private readonly CheckBox includeLevelsInput = new()
    {
        Content = "Уровни",
        IsChecked = true,
        ToolTip = "Показывать Level на активном виде."
    };
    private readonly DataGrid datumGrid = new();
    private readonly DataGrid reportGrid = new();
    private readonly TextBlock statusText = new();
    private readonly Button applyButton = CreateButton("Применить", TrueBimIcon.DatumExtents, 130);
    private readonly Button exportReportButton = CreateButton("Отчёт CSV", TrueBimIcon.Export, 120);

    public DatumExtentWindow(
        RevitDocument document,
        RevitView activeView,
        DatumExtentCollectorService collectorService,
        DatumExtentService datumExtentService,
        DatumExtentProfileStorage profileStorage,
        ITrueBimLogger logger)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        this.activeView = activeView ?? throw new ArgumentNullException(nameof(activeView));
        this.collectorService = collectorService ?? throw new ArgumentNullException(nameof(collectorService));
        this.datumExtentService = datumExtentService ?? throw new ArgumentNullException(nameof(datumExtentService));
        this.profileStorage = profileStorage ?? throw new ArgumentNullException(nameof(profileStorage));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        LoadProfile();

        Title = "Оси 2D/3D";
        Icon = IconFactory.CreateImage(TrueBimIcon.DatumExtents, 32);
        Width = 1180;
        Height = 740;
        MinWidth = 1040;
        MinHeight = 620;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();

        Preview();
        logger.Info($"Datum Extents window opened for '{document.Title}' and view '{activeView.Name}'.");
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveProfile();
        base.OnClosed(e);
    }

    private void LoadProfile()
    {
        DatumExtentProfile profile = profileStorage.Load();
        profileNameInput.Text = profile.Name;
        includeEnd0Input.IsChecked = profile.IncludeEnd0;
        includeEnd1Input.IsChecked = profile.IncludeEnd1;
        includeGridsInput.IsChecked = profile.IncludeGrids;
        includeLevelsInput.IsChecked = profile.IncludeLevels;

        targetTypeInput.ItemsSource = DatumExtentTargets.Options;
        targetTypeInput.DisplayMemberPath = nameof(DatumExtentTargetOption.DisplayName);
        targetTypeInput.SelectedItem = DatumExtentTargets.Options.FirstOrDefault(option => option.Value == profile.TargetExtentType)
            ?? DatumExtentTargets.Options[0];
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
            Header = "Datum",
            Content = CreateDatumPanel()
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Отчёт",
            Content = CreateReportGrid()
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

        AddLabel(root, $"Вид: {activeView.Name}", 0, 0);
        profileNameInput.Height = 32;
        profileNameInput.Margin = new Thickness(8, 0, 12, 8);
        profileNameInput.ToolTip = "Имя локального профиля управления осями.";
        WpfGrid.SetColumn(profileNameInput, 1);
        root.Children.Add(profileNameInput);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Button previewButton = CreateButton("Предпросмотр", TrueBimIcon.Preview, 140);
        previewButton.Click += (_, _) => Preview();
        actions.Children.Add(previewButton);

        applyButton.Click += (_, _) => Apply();
        actions.Children.Add(applyButton);

        exportReportButton.IsEnabled = false;
        exportReportButton.Click += (_, _) => ExportReport();
        actions.Children.Add(exportReportButton);

        Button closeButton = CreateButton("Закрыть", TrueBimIcon.Close, 110);
        closeButton.IsCancel = true;
        closeButton.Click += (_, _) => Close();
        actions.Children.Add(closeButton);

        WpfGrid.SetColumn(actions, 2);
        root.Children.Add(actions);

        AddLabel(root, "Режим", 0, 1);
        targetTypeInput.Height = 32;
        targetTypeInput.MinWidth = 240;
        targetTypeInput.Margin = new Thickness(8, 0, 12, 0);
        targetTypeInput.SelectionChanged += (_, _) => UpdateStatus();
        WpfGrid.SetRow(targetTypeInput, 1);
        WpfGrid.SetColumn(targetTypeInput, 1);
        root.Children.Add(targetTypeInput);

        StackPanel options = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        includeEnd0Input.Margin = new Thickness(0, 0, 12, 0);
        includeEnd0Input.Checked += (_, _) => UpdateStatus();
        includeEnd0Input.Unchecked += (_, _) => UpdateStatus();
        options.Children.Add(includeEnd0Input);

        includeEnd1Input.Margin = new Thickness(0, 0, 18, 0);
        includeEnd1Input.Checked += (_, _) => UpdateStatus();
        includeEnd1Input.Unchecked += (_, _) => UpdateStatus();
        options.Children.Add(includeEnd1Input);

        includeGridsInput.Margin = new Thickness(0, 0, 12, 0);
        includeGridsInput.Checked += (_, _) => UpdateStatus();
        includeGridsInput.Unchecked += (_, _) => UpdateStatus();
        options.Children.Add(includeGridsInput);

        includeLevelsInput.Margin = new Thickness(0, 0, 12, 0);
        includeLevelsInput.Checked += (_, _) => UpdateStatus();
        includeLevelsInput.Unchecked += (_, _) => UpdateStatus();
        options.Children.Add(includeLevelsInput);

        WpfGrid.SetRow(options, 1);
        WpfGrid.SetColumn(options, 2);
        root.Children.Add(options);

        return root;
    }

    private UIElement CreateDatumPanel()
    {
        DockPanel panel = new();

        DockPanel filterBar = new()
        {
            LastChildFill = true,
            Margin = new Thickness(0, 0, 0, 8)
        };
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal
        };
        Button selectAllButton = CreateButton("Выбрать все", TrueBimIcon.Apply, 130);
        selectAllButton.Click += (_, _) => SetVisibleRowsSelected(true);
        actions.Children.Add(selectAllButton);

        Button clearButton = CreateButton("Снять выбор", TrueBimIcon.Close, 130);
        clearButton.Click += (_, _) => SetVisibleRowsSelected(false);
        actions.Children.Add(clearButton);
        DockPanel.SetDock(actions, Dock.Right);
        filterBar.Children.Add(actions);

        filterInput.Height = 32;
        filterInput.Margin = new Thickness(0, 0, 8, 0);
        filterInput.ToolTip = "Фильтр по ElementId, типу или имени datum-элемента.";
        filterInput.TextChanged += (_, _) => RefreshVisibleRows();
        filterBar.Children.Add(filterInput);
        DockPanel.SetDock(filterBar, Dock.Top);
        panel.Children.Add(filterBar);

        datumGrid.AutoGenerateColumns = false;
        datumGrid.CanUserAddRows = false;
        datumGrid.CanUserDeleteRows = false;
        datumGrid.IsReadOnly = false;
        datumGrid.ItemsSource = rows;
        datumGrid.Columns.Add(CreateSelectionColumn(nameof(DatumExtentRow.IsSelected)));
        datumGrid.Columns.Add(CreateTextColumn("ElementId", nameof(DatumExtentRow.ElementId), 90));
        datumGrid.Columns.Add(CreateTextColumn("Тип", nameof(DatumExtentRow.Kind), 90));
        datumGrid.Columns.Add(CreateTextColumn("Имя", nameof(DatumExtentRow.Name), new DataGridLength(1, DataGridLengthUnitType.Star)));
        datumGrid.Columns.Add(CreateTextColumn("End0", nameof(DatumExtentRow.End0Type), 150));
        datumGrid.Columns.Add(CreateTextColumn("End1", nameof(DatumExtentRow.End1Type), 150));
        datumGrid.Columns.Add(CreateTextColumn("Model кривые", nameof(DatumExtentRow.ModelCurveCount), 110));
        datumGrid.Columns.Add(CreateTextColumn("2D кривые", nameof(DatumExtentRow.ViewSpecificCurveCount), 100));
        datumGrid.Columns.Add(CreateTextColumn("Статус", nameof(DatumExtentRow.Status), 120));
        datumGrid.Columns.Add(CreateTextColumn("Сообщение", nameof(DatumExtentRow.Message), 260));
        panel.Children.Add(datumGrid);
        return panel;
    }

    private DataGrid CreateReportGrid()
    {
        reportGrid.AutoGenerateColumns = false;
        reportGrid.CanUserAddRows = false;
        reportGrid.CanUserDeleteRows = false;
        reportGrid.IsReadOnly = true;
        reportGrid.ItemsSource = reportRows;
        reportGrid.Columns.Add(CreateTextColumn("Этап", nameof(DatumExtentReportRow.Phase), 110));
        reportGrid.Columns.Add(CreateTextColumn("ElementId", nameof(DatumExtentReportRow.ElementId), 90));
        reportGrid.Columns.Add(CreateTextColumn("Тип", nameof(DatumExtentReportRow.Kind), 90));
        reportGrid.Columns.Add(CreateTextColumn("Имя", nameof(DatumExtentReportRow.Name), new DataGridLength(1, DataGridLengthUnitType.Star)));
        reportGrid.Columns.Add(CreateTextColumn("Цель", nameof(DatumExtentReportRow.TargetExtentType), 170));
        reportGrid.Columns.Add(CreateTextColumn("End0", nameof(DatumExtentReportRow.End0Type), 150));
        reportGrid.Columns.Add(CreateTextColumn("End1", nameof(DatumExtentReportRow.End1Type), 150));
        reportGrid.Columns.Add(CreateTextColumn("Статус", nameof(DatumExtentReportRow.Status), 100));
        reportGrid.Columns.Add(CreateTextColumn("Сообщение", nameof(DatumExtentReportRow.Message), 260));
        return reportGrid;
    }

    private void Preview()
    {
        SaveProfile();
        DatumExtentProfile profile = CreateProfileFromInputs();
        rows.Clear();
        reportRows.Clear();

        IReadOnlyList<DatumExtentRow> previewRows = collectorService.Collect(document, activeView, profile);
        foreach (DatumExtentRow row in previewRows)
        {
            row.PropertyChanged += OnRowPropertyChanged;
            rows.Add(row);
            reportRows.Add(new DatumExtentReportRow(
                "Предпросмотр",
                activeView.Name,
                row.ElementId,
                row.Kind,
                row.Name,
                DatumExtentTargets.GetDisplayName(profile.TargetExtentType),
                row.End0Type,
                row.End1Type,
                row.Status,
                row.Message));
        }

        exportReportButton.IsEnabled = reportRows.Count > 0;
        RefreshVisibleRows();
        UpdateStatus($"Предпросмотр: {rows.Count} datum-элементов.");
    }

    private void Apply()
    {
        SaveProfile();
        if (rows.Count == 0)
        {
            Preview();
        }

        List<DatumExtentRow> selectedRows = rows
            .Where(row => row.IsSelected && row.CanApply)
            .ToList();
        if (selectedRows.Count == 0)
        {
            Autodesk.Revit.UI.TaskDialog.Show("Оси 2D/3D", "Нет выбранных datum-элементов, готовых к изменению.");
            return;
        }

        DatumExtentProfile profile = CreateProfileFromInputs();
        MessageBoxResult decision = MessageBox.Show(
            this,
            $"Переключить выбранные datum-элементы: {selectedRows.Count}\nЦелевой режим: {DatumExtentTargets.GetDisplayName(profile.TargetExtentType)}",
            "Оси 2D/3D",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        DatumExtentApplyResult result = datumExtentService.Apply(document, activeView, selectedRows, profile, logger);
        Dictionary<long, DatumExtentReportRow> resultRowsByElementId = result.Rows
            .GroupBy(row => row.ElementId)
            .ToDictionary(group => group.Key, group => group.Last());
        foreach (DatumExtentRow row in rows)
        {
            if (resultRowsByElementId.TryGetValue(row.ElementId, out DatumExtentReportRow? reportRow))
            {
                row.ApplyResult(reportRow);
            }
        }

        reportRows.Clear();
        foreach (DatumExtentReportRow row in result.Rows)
        {
            reportRows.Add(row);
        }

        exportReportButton.IsEnabled = reportRows.Count > 0;
        Autodesk.Revit.UI.TaskDialog.Show("Оси 2D/3D", result.ToDialogText());
        UpdateStatus($"Изменено: {result.ChangedCount}. Без изменений: {result.UnchangedCount}. Ошибок: {result.FailedCount}.");
    }

    private void ExportReport()
    {
        if (reportRows.Count == 0)
        {
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "Сохранить отчёт осей 2D/3D",
            Filter = "CSV UTF-8 (*.csv)|*.csv",
            FileName = "datum-extents-report.csv",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        csvExportService.WriteUtf8WithBom(dialog.FileName, reportCsvService.Format(reportRows.ToList()));
        UpdateStatus($"Отчёт сохранён: {dialog.FileName}");
    }

    private void SaveProfile()
    {
        profileStorage.Save(CreateProfileFromInputs());
    }

    private DatumExtentProfile CreateProfileFromInputs()
    {
        string target = (targetTypeInput.SelectedItem as DatumExtentTargetOption)?.Value
            ?? DatumExtentTargets.ViewSpecific;

        return DatumExtentProfileStorage.Normalize(new DatumExtentProfile
        {
            Name = profileNameInput.Text,
            TargetExtentType = target,
            IncludeEnd0 = includeEnd0Input.IsChecked == true,
            IncludeEnd1 = includeEnd1Input.IsChecked == true,
            IncludeGrids = includeGridsInput.IsChecked == true,
            IncludeLevels = includeLevelsInput.IsChecked == true
        });
    }

    private void SetVisibleRowsSelected(bool isSelected)
    {
        foreach (DatumExtentRow row in GetFilteredRows().Where(row => row.CanApply))
        {
            row.IsSelected = isSelected;
        }

        UpdateStatus();
    }

    private void RefreshVisibleRows()
    {
        if (rows.Count == 0)
        {
            UpdateStatus();
            return;
        }

        ICollectionView view = CollectionViewSource.GetDefaultView(rows);
        view.Filter = row => row is DatumExtentRow datumRow && IsRowVisible(datumRow);
        view.Refresh();
        UpdateStatus();
    }

    private IEnumerable<DatumExtentRow> GetFilteredRows()
    {
        return rows.Where(IsRowVisible);
    }

    private bool IsRowVisible(DatumExtentRow row)
    {
        string filter = filterInput.Text.Trim();
        return string.IsNullOrWhiteSpace(filter)
            || row.ElementId.ToString(System.Globalization.CultureInfo.InvariantCulture).IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
            || row.Kind.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
            || row.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(DatumExtentRow.IsSelected) or nameof(DatumExtentRow.Status))
        {
            UpdateStatus();
        }
    }

    private void UpdateStatus(string? prefix = null)
    {
        int readyRows = rows.Count(row => row.CanApply);
        int selectedRows = rows.Count(row => row.IsSelected && row.CanApply);
        string target = (targetTypeInput.SelectedItem as DatumExtentTargetOption)?.DisplayName
            ?? DatumExtentTargets.Options[0].DisplayName;
        string text = $"Datum: {rows.Count}. Готово к изменению: {readyRows}. Выбрано: {selectedRows}. Режим: {target}. Отчётных строк: {reportRows.Count}.";
        statusText.Text = string.IsNullOrWhiteSpace(prefix) ? text : $"{prefix} {text}";
        applyButton.IsEnabled = selectedRows > 0 || rows.Count == 0;
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

    private static DataGridTemplateColumn CreateSelectionColumn(string bindingPath, string header = "Выбран")
    {
        FrameworkElementFactory checkBox = new(typeof(CheckBox));
        checkBox.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        checkBox.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        checkBox.SetBinding(
            CheckBox.IsCheckedProperty,
            new WpfBinding(bindingPath)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });

        return new DataGridTemplateColumn
        {
            Header = header,
            CellTemplate = new DataTemplate
            {
                VisualTree = checkBox
            },
            Width = 78
        };
    }
}
