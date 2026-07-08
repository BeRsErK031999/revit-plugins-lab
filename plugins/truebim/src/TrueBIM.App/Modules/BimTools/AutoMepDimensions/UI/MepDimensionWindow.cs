using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using TrueBIM.App.Modules.BimTools.AutoMepDimensions.Models;
using TrueBIM.App.Modules.BimTools.AutoMepDimensions.Services;
using TrueBIM.App.Modules.BimTools.Common.Services.Export;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using RevitDocument = Autodesk.Revit.DB.Document;
using RevitView = Autodesk.Revit.DB.View;
using WpfBinding = System.Windows.Data.Binding;
using WpfGrid = System.Windows.Controls.Grid;

namespace TrueBIM.App.Modules.BimTools.AutoMepDimensions.UI;

public sealed class MepDimensionWindow : Window
{
    private readonly RevitDocument document;
    private readonly RevitView activeView;
    private readonly MepDimensionCollectorService collectorService;
    private readonly MepDimensionCreationService creationService;
    private readonly MepDimensionProfileStorage profileStorage;
    private readonly ITrueBimLogger logger;
    private readonly CsvExportService csvExportService = new();
    private readonly MepDimensionReportCsvService reportCsvService = new();
    private readonly ObservableCollection<MepDimensionCandidateRow> candidateRows = new();
    private readonly ObservableCollection<MepDimensionReportRow> reportRows = new();
    private readonly List<MepDimensionCandidate> candidates = [];
    private readonly TextBox profileNameInput = new();
    private readonly TextBox filterInput = new();
    private readonly TextBox angleToleranceInput = new();
    private readonly ComboBox linePlacementInput = new()
    {
        ItemsSource = MepDimensionLinePlacements.Options,
        DisplayMemberPath = nameof(MepDimensionLinePlacementOption.DisplayName),
        SelectedValuePath = nameof(MepDimensionLinePlacementOption.Key)
    };
    private readonly TextBox dimensionOffsetInput = new();
    private readonly CheckBox includePipesInput = new()
    {
        Content = "Трубы",
        IsChecked = true,
        ToolTip = "Искать видимые PipeCurve на активном виде."
    };
    private readonly CheckBox includeDuctsInput = new()
    {
        Content = "Воздуховоды",
        ToolTip = "Искать видимые DuctCurve на активном виде."
    };
    private readonly CheckBox includeCableTraysInput = new()
    {
        Content = "Лотки",
        ToolTip = "Искать видимые Cable Tray на активном виде."
    };
    private readonly CheckBox includeConduitsInput = new()
    {
        Content = "Кабель-каналы",
        ToolTip = "Искать видимые Conduit на активном виде."
    };
    private readonly CheckBox allowFallbackInput = new()
    {
        Content = "Element fallback",
        IsChecked = true,
        ToolTip = "Если геометрический Reference не найден, пробовать Reference самого элемента."
    };
    private readonly DataGrid candidateGrid = new();
    private readonly DataGrid reportGrid = new();
    private readonly TextBlock statusText = new();
    private readonly Button applyButton = CreateButton("Создать размеры", TrueBimIcon.AutoDimensions, 150);
    private readonly Button exportReportButton = CreateButton("Отчёт CSV", TrueBimIcon.Export, 120);

    public MepDimensionWindow(
        RevitDocument document,
        RevitView activeView,
        MepDimensionCollectorService collectorService,
        MepDimensionCreationService creationService,
        MepDimensionProfileStorage profileStorage,
        ITrueBimLogger logger)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        this.activeView = activeView ?? throw new ArgumentNullException(nameof(activeView));
        this.collectorService = collectorService ?? throw new ArgumentNullException(nameof(collectorService));
        this.creationService = creationService ?? throw new ArgumentNullException(nameof(creationService));
        this.profileStorage = profileStorage ?? throw new ArgumentNullException(nameof(profileStorage));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        LoadProfile();

        Title = "Авторазмеры MEP";
        Icon = IconFactory.CreateImage(TrueBimIcon.AutoDimensions, 32);
        Width = 1180;
        Height = 740;
        MinWidth = 1040;
        MinHeight = 620;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();

        Preview();
        logger.Info($"Auto MEP Dimensions window opened for '{document.Title}' and view '{activeView.Name}'.");
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveProfile();
        base.OnClosed(e);
    }

    private void LoadProfile()
    {
        ApplyProfileToInputs(profileStorage.Load());
    }

    private void ApplyProfileToInputs(MepDimensionProfile profile)
    {
        profileNameInput.Text = profile.Name;
        includePipesInput.IsChecked = profile.IncludePipes;
        includeDuctsInput.IsChecked = profile.IncludeDucts;
        includeCableTraysInput.IsChecked = profile.IncludeCableTrays;
        includeConduitsInput.IsChecked = profile.IncludeConduits;
        allowFallbackInput.IsChecked = profile.AllowElementReferenceFallback;
        angleToleranceInput.Text = profile.AngleToleranceDegrees.ToString("0.##", CultureInfo.InvariantCulture);
        linePlacementInput.SelectedValue = profile.DimensionLinePlacement;
        dimensionOffsetInput.Text = MepDimensionLinePlacements.FormatMillimeters(profile.DimensionOffsetMm);
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
            Header = "Кандидаты",
            Content = CreateCandidatePanel()
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
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddLabel(root, $"Вид: {activeView.Name}", 0, 0);
        profileNameInput.Height = 32;
        profileNameInput.Margin = new Thickness(8, 0, 12, 8);
        profileNameInput.ToolTip = "Имя локального профиля авторазмеров MEP.";
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

        AddLabel(root, "Допуск угла", 0, 1);
        StackPanel settings = new()
        {
            Orientation = Orientation.Horizontal
        };
        angleToleranceInput.Width = 70;
        angleToleranceInput.Height = 32;
        angleToleranceInput.Margin = new Thickness(8, 0, 10, 0);
        angleToleranceInput.ToolTip = "Допуск в градусах для горизонтальных и вертикальных трасс, от 1 до 30.";
        settings.Children.Add(angleToleranceInput);

        TextBlock degreeLabel = new()
        {
            Text = "град.",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 18, 0)
        };
        settings.Children.Add(degreeLabel);

        includePipesInput.Margin = new Thickness(0, 0, 12, 0);
        includeDuctsInput.Margin = new Thickness(0, 0, 12, 0);
        includeCableTraysInput.Margin = new Thickness(0, 0, 12, 0);
        includeConduitsInput.Margin = new Thickness(0, 0, 16, 0);
        allowFallbackInput.Margin = new Thickness(0, 0, 12, 0);
        includePipesInput.Checked += (_, _) => UpdateStatus();
        includePipesInput.Unchecked += (_, _) => UpdateStatus();
        includeDuctsInput.Checked += (_, _) => UpdateStatus();
        includeDuctsInput.Unchecked += (_, _) => UpdateStatus();
        includeCableTraysInput.Checked += (_, _) => UpdateStatus();
        includeCableTraysInput.Unchecked += (_, _) => UpdateStatus();
        includeConduitsInput.Checked += (_, _) => UpdateStatus();
        includeConduitsInput.Unchecked += (_, _) => UpdateStatus();
        allowFallbackInput.Checked += (_, _) => UpdateStatus();
        allowFallbackInput.Unchecked += (_, _) => UpdateStatus();

        settings.Children.Add(includePipesInput);
        settings.Children.Add(includeDuctsInput);
        settings.Children.Add(includeCableTraysInput);
        settings.Children.Add(includeConduitsInput);
        settings.Children.Add(allowFallbackInput);
        WpfGrid.SetRow(settings, 1);
        WpfGrid.SetColumn(settings, 1);
        WpfGrid.SetColumnSpan(settings, 2);
        root.Children.Add(settings);

        AddLabel(root, "Линия размера", 0, 2);
        StackPanel lineSettings = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 6, 0, 0)
        };
        linePlacementInput.Width = 130;
        linePlacementInput.Height = 32;
        linePlacementInput.Margin = new Thickness(0, 0, 12, 0);
        linePlacementInput.ToolTip = "Где ставить размерную линию относительно общего участка MEP-трасс.";
        linePlacementInput.SelectionChanged += (_, _) => UpdateStatus();
        lineSettings.Children.Add(linePlacementInput);

        TextBlock offsetLabel = new()
        {
            Text = "Вынос",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        lineSettings.Children.Add(offsetLabel);

        dimensionOffsetInput.Width = 70;
        dimensionOffsetInput.Height = 32;
        dimensionOffsetInput.Margin = new Thickness(0, 0, 6, 0);
        dimensionOffsetInput.ToolTip = "Отступ размерной линии от общего участка трасс в миллиметрах. Используется для режимов До трасс и После трасс.";
        dimensionOffsetInput.TextChanged += (_, _) => UpdateStatus();
        lineSettings.Children.Add(dimensionOffsetInput);

        TextBlock offsetUnitLabel = new()
        {
            Text = "мм",
            VerticalAlignment = VerticalAlignment.Center
        };
        lineSettings.Children.Add(offsetUnitLabel);
        WpfGrid.SetRow(lineSettings, 2);
        WpfGrid.SetColumn(lineSettings, 1);
        WpfGrid.SetColumnSpan(lineSettings, 2);
        root.Children.Add(lineSettings);

        return root;
    }

    private UIElement CreateCandidatePanel()
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
        Button selectAllButton = CreateButton("Выбрать всё", TrueBimIcon.Apply, 130);
        selectAllButton.Click += (_, _) => SetVisibleRowsSelected(true);
        actions.Children.Add(selectAllButton);

        Button clearButton = CreateButton("Снять выбор", TrueBimIcon.Close, 130);
        clearButton.Click += (_, _) => SetVisibleRowsSelected(false);
        actions.Children.Add(clearButton);
        DockPanel.SetDock(actions, Dock.Right);
        filterBar.Children.Add(actions);

        filterInput.Height = 32;
        filterInput.Margin = new Thickness(0, 0, 8, 0);
        filterInput.ToolTip = "Фильтр по кандидату, категории, направлению, статусу или сообщению.";
        filterInput.TextChanged += (_, _) => RefreshVisibleRows();
        filterBar.Children.Add(filterInput);
        DockPanel.SetDock(filterBar, Dock.Top);
        panel.Children.Add(filterBar);

        candidateGrid.AutoGenerateColumns = false;
        candidateGrid.CanUserAddRows = false;
        candidateGrid.CanUserDeleteRows = false;
        candidateGrid.IsReadOnly = false;
        candidateGrid.ItemsSource = candidateRows;
        candidateGrid.Columns.Add(CreateSelectionColumn(nameof(MepDimensionCandidateRow.IsSelected)));
        candidateGrid.Columns.Add(CreateTextColumn("Кандидат", nameof(MepDimensionCandidateRow.CandidateId), 150));
        candidateGrid.Columns.Add(CreateTextColumn("Категория", nameof(MepDimensionCandidateRow.CategoryName), 140));
        candidateGrid.Columns.Add(CreateTextColumn("Направление", nameof(MepDimensionCandidateRow.DirectionName), 170));
        candidateGrid.Columns.Add(CreateTextColumn("Элементов", nameof(MepDimensionCandidateRow.ElementCount), 90));
        candidateGrid.Columns.Add(CreateTextColumn("Reference", nameof(MepDimensionCandidateRow.ReadyReferenceCount), 90));
        candidateGrid.Columns.Add(CreateTextColumn("Без Reference", nameof(MepDimensionCandidateRow.MissingReferenceCount), 110));
        candidateGrid.Columns.Add(CreateTextColumn("Размерная линия", nameof(MepDimensionCandidateRow.DimensionLine), 180));
        candidateGrid.Columns.Add(CreateTextColumn("Статус", nameof(MepDimensionCandidateRow.Status), 100));
        candidateGrid.Columns.Add(CreateTextColumn("Сообщение", nameof(MepDimensionCandidateRow.Message), new DataGridLength(1, DataGridLengthUnitType.Star)));
        panel.Children.Add(candidateGrid);
        return panel;
    }

    private DataGrid CreateReportGrid()
    {
        reportGrid.AutoGenerateColumns = false;
        reportGrid.CanUserAddRows = false;
        reportGrid.CanUserDeleteRows = false;
        reportGrid.IsReadOnly = true;
        reportGrid.ItemsSource = reportRows;
        reportGrid.Columns.Add(CreateTextColumn("Этап", nameof(MepDimensionReportRow.Phase), 110));
        reportGrid.Columns.Add(CreateTextColumn("Вид", nameof(MepDimensionReportRow.ViewName), 140));
        reportGrid.Columns.Add(CreateTextColumn("Кандидат", nameof(MepDimensionReportRow.CandidateId), 150));
        reportGrid.Columns.Add(CreateTextColumn("Категория", nameof(MepDimensionReportRow.CategoryName), 140));
        reportGrid.Columns.Add(CreateTextColumn("Направление", nameof(MepDimensionReportRow.DirectionName), 170));
        reportGrid.Columns.Add(CreateTextColumn("Элементов", nameof(MepDimensionReportRow.ElementCount), 90));
        reportGrid.Columns.Add(CreateTextColumn("Reference", nameof(MepDimensionReportRow.ReadyReferenceCount), 90));
        reportGrid.Columns.Add(CreateTextColumn("Без Reference", nameof(MepDimensionReportRow.MissingReferenceCount), 110));
        reportGrid.Columns.Add(CreateTextColumn("Статус", nameof(MepDimensionReportRow.Status), 100));
        reportGrid.Columns.Add(CreateTextColumn("Сообщение", nameof(MepDimensionReportRow.Message), new DataGridLength(1, DataGridLengthUnitType.Star)));
        return reportGrid;
    }

    private void Preview()
    {
        SaveProfile();
        MepDimensionProfile profile = CreateProfileFromInputs();
        ApplyProfileToInputs(profile);
        candidates.Clear();
        candidateRows.Clear();
        reportRows.Clear();

        foreach (MepDimensionCandidate candidate in collectorService.Collect(document, activeView, profile))
        {
            candidates.Add(candidate);
            MepDimensionCandidateRow row = candidate.ToRow();
            row.PropertyChanged += OnCandidateRowPropertyChanged;
            candidateRows.Add(row);
            reportRows.Add(new MepDimensionReportRow(
                "Предпросмотр",
                activeView.Name,
                candidate.CandidateId,
                candidate.CategoryName,
                candidate.DirectionName,
                candidate.ElementCount,
                candidate.ReadyReferenceCount,
                candidate.MissingReferenceCount,
                row.Status,
                row.Message));
        }

        exportReportButton.IsEnabled = reportRows.Count > 0;
        RefreshVisibleRows();
        UpdateStatus($"Предпросмотр: {candidateRows.Count} размерных цепочек.");
    }

    private void Apply()
    {
        SaveProfile();
        if (candidateRows.Count == 0)
        {
            Preview();
        }

        HashSet<string> selectedCandidateIds = candidateRows
            .Where(row => row.IsSelected && row.CanApply)
            .Select(row => row.CandidateId)
            .ToHashSet(StringComparer.Ordinal);
        if (selectedCandidateIds.Count == 0)
        {
            Autodesk.Revit.UI.TaskDialog.Show("Авторазмеры MEP", "Нет выбранных размерных цепочек, готовых к созданию.");
            return;
        }

        MessageBoxResult decision = MessageBox.Show(
            this,
            $"Создать размерные цепочки на активном виде: {selectedCandidateIds.Count}?",
            "Авторазмеры MEP",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        MepDimensionProfile profile = CreateProfileFromInputs();
        MepDimensionApplyResult result = creationService.Apply(
            document,
            activeView,
            candidates,
            selectedCandidateIds,
            profile,
            logger);

        Dictionary<string, MepDimensionReportRow> resultRowsByCandidateId = result.Rows
            .GroupBy(row => row.CandidateId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        foreach (MepDimensionCandidateRow row in candidateRows)
        {
            if (resultRowsByCandidateId.TryGetValue(row.CandidateId, out MepDimensionReportRow? resultRow))
            {
                row.ApplyResult(resultRow);
            }
        }

        reportRows.Clear();
        foreach (MepDimensionReportRow row in result.Rows)
        {
            reportRows.Add(row);
        }

        exportReportButton.IsEnabled = reportRows.Count > 0;
        Autodesk.Revit.UI.TaskDialog.Show("Авторазмеры MEP", result.ToDialogText());
        UpdateStatus($"Создано: {result.CreatedCount}. Пропущено: {result.SkippedCount}. Ошибок: {result.FailedCount}.");
    }

    private void ExportReport()
    {
        if (reportRows.Count == 0)
        {
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "Сохранить отчёт авторазмеров MEP",
            Filter = "CSV UTF-8 (*.csv)|*.csv",
            FileName = "auto-mep-dimensions-report.csv",
            InitialDirectory = Directory.Exists(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : null
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

    private MepDimensionProfile CreateProfileFromInputs()
    {
        return MepDimensionProfileStorage.Normalize(new MepDimensionProfile
        {
            Name = profileNameInput.Text,
            IncludePipes = includePipesInput.IsChecked == true,
            IncludeDucts = includeDuctsInput.IsChecked == true,
            IncludeCableTrays = includeCableTraysInput.IsChecked == true,
            IncludeConduits = includeConduitsInput.IsChecked == true,
            AllowElementReferenceFallback = allowFallbackInput.IsChecked == true,
            AngleToleranceDegrees = ParseAngleTolerance(),
            DimensionLinePlacement = linePlacementInput.SelectedValue as string ?? MepDimensionLinePlacements.Center,
            DimensionOffsetMm = ParseDimensionOffset()
        });
    }

    private double ParseAngleTolerance()
    {
        string text = angleToleranceInput.Text.Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double currentCultureValue))
        {
            return currentCultureValue;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariantValue)
            ? invariantValue
            : 10;
    }

    private double ParseDimensionOffset()
    {
        string text = dimensionOffsetInput.Text.Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double currentCultureValue))
        {
            return MepDimensionLinePlacements.NormalizeOffsetMillimeters(currentCultureValue);
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariantValue)
            ? MepDimensionLinePlacements.NormalizeOffsetMillimeters(invariantValue)
            : 0;
    }

    private void SetVisibleRowsSelected(bool isSelected)
    {
        foreach (MepDimensionCandidateRow row in GetFilteredRows().Where(row => row.CanApply))
        {
            row.IsSelected = isSelected;
        }

        UpdateStatus();
    }

    private void RefreshVisibleRows()
    {
        if (candidateRows.Count == 0)
        {
            UpdateStatus();
            return;
        }

        ICollectionView view = CollectionViewSource.GetDefaultView(candidateRows);
        view.Filter = row => row is MepDimensionCandidateRow candidateRow && IsRowVisible(candidateRow);
        view.Refresh();
        UpdateStatus();
    }

    private IEnumerable<MepDimensionCandidateRow> GetFilteredRows()
    {
        return candidateRows.Where(IsRowVisible);
    }

    private bool IsRowVisible(MepDimensionCandidateRow row)
    {
        string filter = filterInput.Text.Trim();
        return string.IsNullOrWhiteSpace(filter)
            || row.CandidateId.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
            || row.CategoryName.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
            || row.DirectionName.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
            || row.Status.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
            || row.Message.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    private void OnCandidateRowPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MepDimensionCandidateRow.IsSelected) or nameof(MepDimensionCandidateRow.Status))
        {
            UpdateStatus();
        }
    }

    private void UpdateStatus(string? prefix = null)
    {
        int readyRows = candidateRows.Count(row => row.CanApply);
        int selectedRows = candidateRows.Count(row => row.IsSelected && row.CanApply);
        string categories = CreateCategorySummary();
        string linePlacement = MepDimensionLinePlacements.FormatForDisplay(linePlacementInput.SelectedValue as string, ParseDimensionOffset());
        string text = $"Цепочек: {candidateRows.Count}. Готово: {readyRows}. Выбрано: {selectedRows}. Категории: {categories}. Допуск: {ParseAngleTolerance():0.##}°. Линия: {linePlacement}. Отчётных строк: {reportRows.Count}.";
        statusText.Text = string.IsNullOrWhiteSpace(prefix) ? text : $"{prefix} {text}";
        applyButton.IsEnabled = selectedRows > 0 || candidateRows.Count == 0;
    }

    private string CreateCategorySummary()
    {
        List<string> parts = [];
        if (includePipesInput.IsChecked == true)
        {
            parts.Add("трубы");
        }

        if (includeDuctsInput.IsChecked == true)
        {
            parts.Add("воздуховоды");
        }

        if (includeCableTraysInput.IsChecked == true)
        {
            parts.Add("лотки");
        }

        if (includeConduitsInput.IsChecked == true)
        {
            parts.Add("кабель-каналы");
        }

        return parts.Count == 0 ? "трубы" : string.Join(", ", parts);
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
