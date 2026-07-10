using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using TrueBIM.App.Modules.BimTools.Common.Services.Export;
using TrueBIM.App.Modules.BimTools.OpeningViews.Models;
using TrueBIM.App.Modules.BimTools.OpeningViews.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using RevitDocument = Autodesk.Revit.DB.Document;
using RevitViewPlan = Autodesk.Revit.DB.ViewPlan;
using WpfBinding = System.Windows.Data.Binding;
using WpfGrid = System.Windows.Controls.Grid;

namespace TrueBIM.App.Modules.BimTools.OpeningViews.UI;

public sealed class OpeningViewsWindow : TrueBimWindow
{
    private const string DialogTitle = "Фасады проёмов";

    private readonly RevitDocument document;
    private readonly RevitViewPlan activePlan;
    private readonly OpeningViewCollectorService collectorService;
    private readonly OpeningViewCreationService creationService;
    private readonly OpeningViewProfileStorage profileStorage;
    private readonly ITrueBimLogger logger;
    private readonly RevitActionDispatcher revitActions;
    private readonly CsvExportService csvExportService = new();
    private readonly OpeningViewReportCsvService reportCsvService = new();
    private readonly ObservableCollection<OpeningViewRow> openingRows = new();
    private readonly ObservableCollection<OpeningViewReportRow> reportRows = new();
    private readonly ObservableCollection<OpeningViewTypeOption> viewTypeOptions = new();
    private readonly ObservableCollection<OpeningViewTemplateOption> viewTemplateOptions = new();
    private readonly List<OpeningViewCandidate> candidates = [];
    private readonly TextBox profileNameInput = new();
    private readonly TextBox filterInput = new();
    private readonly TextBox scaleInput = new();
    private readonly TextBox cropMarginInput = new();
    private readonly TextBox depthMarginInput = new();
    private readonly TextBox viewNameTemplateInput = new();
    private readonly ComboBox elevationTypeInput = new();
    private readonly ComboBox viewTemplateInput = new();
    private readonly ComboBox orientationSourceInput = new()
    {
        ItemsSource = OpeningViewOrientationSources.Options,
        DisplayMemberPath = nameof(OpeningViewOrientationSourceOption.DisplayName),
        SelectedValuePath = nameof(OpeningViewOrientationSourceOption.Key)
    };
    private readonly CheckBox includeDoorsInput = new()
    {
        Content = "Двери",
        IsChecked = true,
        ToolTip = "Включить двери, которые видны на активном плане. Для выбранных строк будет создан отдельный фасадный elevation-вид."
    };
    private readonly CheckBox includeWindowsInput = new()
    {
        Content = "Окна",
        ToolTip = "Включить окна, которые видны на активном плане. Можно создавать фасады только окон или вместе с дверями."
    };
    private readonly CheckBox includeCurtainWallsInput = new()
    {
        Content = "Витражи",
        ToolTip = "Включить прямолинейные стены-витражи активного плана. Фасад строится со стороны наружной ориентации стены и охватывает всю конструкцию."
    };
    private readonly DataGrid openingGrid = new();
    private readonly DataGrid reportGrid = new();
    private readonly TextBlock statusText = new();
    private readonly Button previewButton = CreateButton("1. Предпросмотр", TrueBimIcon.Preview, 150);
    private readonly Button applyButton = CreateButton("2. Создать виды", TrueBimIcon.OpeningViews, 150);
    private readonly Button exportReportButton = CreateButton("Отчёт CSV", TrueBimIcon.Export, 120);

    public OpeningViewsWindow(
        RevitDocument document,
        RevitViewPlan activePlan,
        OpeningViewCollectorService collectorService,
        OpeningViewCreationService creationService,
        OpeningViewProfileStorage profileStorage,
        ITrueBimLogger logger)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        this.activePlan = activePlan ?? throw new ArgumentNullException(nameof(activePlan));
        this.collectorService = collectorService ?? throw new ArgumentNullException(nameof(collectorService));
        this.creationService = creationService ?? throw new ArgumentNullException(nameof(creationService));
        this.profileStorage = profileStorage ?? throw new ArgumentNullException(nameof(profileStorage));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        revitActions = new RevitActionDispatcher("фасады проёмов", this.logger);

        LoadInitialData();

        Title = DialogTitle;
        Icon = IconFactory.CreateImage(TrueBimIcon.OpeningViews, 32);
        Width = 1180;
        Height = 760;
        MinWidth = 1040;
        MinHeight = 640;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();

        previewButton.ToolTip = "Собрать выбранные категории с активного плана и проверить будущие виды без изменения модели.";
        applyButton.ToolTip = "Сначала выполните предпросмотр и отметьте строки, готовые к созданию.";
        exportReportButton.ToolTip = "Сначала выполните предпросмотр, чтобы сформировать строки отчёта.";
        ToolTipService.SetShowOnDisabled(previewButton, true);
        ToolTipService.SetShowOnDisabled(applyButton, true);
        ToolTipService.SetShowOnDisabled(exportReportButton, true);

        UpdateStatus("Нажмите «Предпросмотр», чтобы собрать двери, окна и витражи на активном плане.");
        logger.Info($"Opening Views window opened for '{document.Title}' and plan '{activePlan.Name}'.");
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveProfile();
        base.OnClosed(e);
    }

    private void LoadInitialData()
    {
        OpeningViewProfile profile = profileStorage.Load();

        foreach (OpeningViewTypeOption option in collectorService.CollectElevationViewTypes(document))
        {
            viewTypeOptions.Add(option);
        }

        foreach (OpeningViewTemplateOption option in collectorService.CollectViewTemplates(document))
        {
            viewTemplateOptions.Add(option);
        }

        elevationTypeInput.ItemsSource = viewTypeOptions;
        elevationTypeInput.DisplayMemberPath = nameof(OpeningViewTypeOption.DisplayName);
        elevationTypeInput.SelectedItem = viewTypeOptions.FirstOrDefault(option => option.ElementId == profile.ElevationViewTypeId)
            ?? viewTypeOptions.FirstOrDefault();

        viewTemplateInput.ItemsSource = viewTemplateOptions;
        viewTemplateInput.DisplayMemberPath = nameof(OpeningViewTemplateOption.DisplayName);
        viewTemplateInput.SelectedItem = viewTemplateOptions.FirstOrDefault(option => option.ElementId == profile.ViewTemplateId)
            ?? viewTemplateOptions.FirstOrDefault()
            ?? OpeningViewTemplateOption.None;

        ApplyProfileToInputs(profile);
    }

    private void ApplyProfileToInputs(OpeningViewProfile profile)
    {
        profileNameInput.Text = profile.Name;
        includeDoorsInput.IsChecked = profile.IncludeDoors;
        includeWindowsInput.IsChecked = profile.IncludeWindows;
        includeCurtainWallsInput.IsChecked = profile.IncludeCurtainWalls;
        scaleInput.Text = profile.Scale.ToString(CultureInfo.InvariantCulture);
        cropMarginInput.Text = profile.CropMarginMm.ToString("0.##", CultureInfo.InvariantCulture);
        depthMarginInput.Text = profile.DepthMarginMm.ToString("0.##", CultureInfo.InvariantCulture);
        orientationSourceInput.SelectedValue = profile.OrientationSource;
        viewNameTemplateInput.Text = profile.ViewNameTemplate;
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
            Header = "Проёмы",
            Content = CreateOpeningPanel()
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
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddLabel(root, $"План: {activePlan.Name}", 0, 0);
        profileNameInput.Height = 32;
        profileNameInput.Margin = new Thickness(8, 0, 12, 8);
        profileNameInput.ToolTip = "Имя локального профиля фасадов дверей, окон и витражей.";
        WpfGrid.SetColumn(profileNameInput, 1);
        root.Children.Add(profileNameInput);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        previewButton.Click += (_, _) => Preview();
        actions.Children.Add(previewButton);

        applyButton.Click += (_, _) => Apply();
        actions.Children.Add(applyButton);

        exportReportButton.IsEnabled = false;
        exportReportButton.Click += (_, _) => ExportReport();
        actions.Children.Add(exportReportButton);

        Button guideButton = CreateGuideButton();
        actions.Children.Add(guideButton);

        Button closeButton = CreateButton("Закрыть", TrueBimIcon.Close, 110);
        closeButton.IsCancel = true;
        closeButton.ToolTip = "Закрыть окно и сохранить текущий профиль настроек.";
        closeButton.Click += (_, _) => Close();
        actions.Children.Add(closeButton);

        WpfGrid.SetColumn(actions, 2);
        root.Children.Add(actions);

        AddLabel(root, "Тип фасада", 0, 1);
        elevationTypeInput.Height = 32;
        elevationTypeInput.MinWidth = 280;
        elevationTypeInput.Margin = new Thickness(8, 0, 12, 8);
        elevationTypeInput.ToolTip = "ViewFamilyType семейства Elevation, через который Revit создаёт фасадный вид для каждого выбранного проёма.";
        elevationTypeInput.SelectionChanged += (_, _) => UpdateStatus();
        WpfGrid.SetRow(elevationTypeInput, 1);
        WpfGrid.SetColumn(elevationTypeInput, 1);
        root.Children.Add(elevationTypeInput);

        StackPanel categories = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        AddInlineLabel(categories, "Ориентация");
        orientationSourceInput.Width = 130;
        orientationSourceInput.Height = 32;
        orientationSourceInput.Margin = new Thickness(0, 0, 16, 0);
        orientationSourceInput.ToolTip = "Для двери/окна: по элементу использует FacingOrientation, по стене — стену-основу. Витраж всегда ориентируется по наружной стороне своей стены.";
        orientationSourceInput.SelectionChanged += (_, _) => UpdateStatus();
        categories.Children.Add(orientationSourceInput);

        includeDoorsInput.Margin = new Thickness(0, 0, 12, 0);
        includeWindowsInput.Margin = new Thickness(0, 0, 12, 0);
        includeCurtainWallsInput.Margin = new Thickness(0, 0, 18, 0);
        includeDoorsInput.Checked += (_, _) => UpdateStatus();
        includeDoorsInput.Unchecked += (_, _) => UpdateStatus();
        includeWindowsInput.Checked += (_, _) => UpdateStatus();
        includeWindowsInput.Unchecked += (_, _) => UpdateStatus();
        includeCurtainWallsInput.Checked += (_, _) => UpdateStatus();
        includeCurtainWallsInput.Unchecked += (_, _) => UpdateStatus();
        categories.Children.Add(includeDoorsInput);
        categories.Children.Add(includeWindowsInput);
        categories.Children.Add(includeCurtainWallsInput);
        WpfGrid.SetRow(categories, 1);
        WpfGrid.SetColumn(categories, 2);
        root.Children.Add(categories);

        AddLabel(root, "Шаблон вида", 0, 2);
        viewTemplateInput.Height = 32;
        viewTemplateInput.MinWidth = 280;
        viewTemplateInput.Margin = new Thickness(8, 0, 12, 8);
        viewTemplateInput.ToolTip = "Необязательный шаблон elevation-вида. Если выбран, применяется к каждому созданному фасаду.";
        WpfGrid.SetRow(viewTemplateInput, 2);
        WpfGrid.SetColumn(viewTemplateInput, 1);
        root.Children.Add(viewTemplateInput);

        StackPanel numericSettings = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        AddInlineLabel(numericSettings, "Масштаб");
        scaleInput.Width = 70;
        scaleInput.Height = 32;
        scaleInput.Margin = new Thickness(0, 0, 12, 0);
        scaleInput.ToolTip = "Масштаб каждого создаваемого фасадного elevation-вида, от 1 до 500.";
        numericSettings.Children.Add(scaleInput);

        AddInlineLabel(numericSettings, "Crop, мм");
        cropMarginInput.Width = 80;
        cropMarginInput.Height = 32;
        cropMarginInput.Margin = new Thickness(0, 0, 12, 0);
        cropMarginInput.ToolTip = "Запас crop box вокруг двери, окна или полного габарита витража. Увеличьте, если в фасад должны попасть откосы или соседняя отделка.";
        numericSettings.Children.Add(cropMarginInput);

        AddInlineLabel(numericSettings, "Глубина, мм");
        depthMarginInput.Width = 80;
        depthMarginInput.Height = 32;
        depthMarginInput.ToolTip = "Запас crop box по направлению взгляда. Увеличьте, если часть проёма или стены обрезается по глубине.";
        numericSettings.Children.Add(depthMarginInput);

        WpfGrid.SetRow(numericSettings, 2);
        WpfGrid.SetColumn(numericSettings, 2);
        root.Children.Add(numericSettings);

        AddLabel(root, "Имя вида", 0, 3);
        viewNameTemplateInput.Height = 32;
        viewNameTemplateInput.Margin = new Thickness(8, 0, 12, 0);
        viewNameTemplateInput.ToolTip = "Шаблон имени будущего вида. Поддерживаются {ElementId}, {CategoryKey}, {Category}, {Family}, {Type}, {Level}. Дубли имён будут пропущены.";
        WpfGrid.SetRow(viewNameTemplateInput, 3);
        WpfGrid.SetColumn(viewNameTemplateInput, 1);
        WpfGrid.SetColumnSpan(viewNameTemplateInput, 2);
        root.Children.Add(viewNameTemplateInput);

        return root;
    }

    private UIElement CreateOpeningPanel()
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
        selectAllButton.ToolTip = "Отметить все видимые строки, которые готовы к созданию фасадного вида.";
        selectAllButton.Click += (_, _) => SetVisibleRowsSelected(true);
        actions.Children.Add(selectAllButton);

        Button clearButton = CreateButton("Снять выбор", TrueBimIcon.Close, 130);
        clearButton.ToolTip = "Снять отметки с видимых строк предпросмотра.";
        clearButton.Click += (_, _) => SetVisibleRowsSelected(false);
        actions.Children.Add(clearButton);
        DockPanel.SetDock(actions, Dock.Right);
        filterBar.Children.Add(actions);

        filterInput.Height = 32;
        filterInput.Margin = new Thickness(0, 0, 8, 0);
        filterInput.ToolTip = "Фильтр по ElementId, категории, семейству, типу, уровню, будущему имени вида, статусу или сообщению.";
        filterInput.TextChanged += (_, _) => RefreshVisibleRows();
        filterBar.Children.Add(filterInput);
        DockPanel.SetDock(filterBar, Dock.Top);
        panel.Children.Add(filterBar);

        openingGrid.AutoGenerateColumns = false;
        openingGrid.CanUserAddRows = false;
        openingGrid.CanUserDeleteRows = false;
        openingGrid.IsReadOnly = false;
        openingGrid.ItemsSource = openingRows;
        openingGrid.Columns.Add(CreateSelectionColumn(nameof(OpeningViewRow.IsSelected)));
        openingGrid.Columns.Add(CreateTextColumn("ElementId", nameof(OpeningViewRow.ElementId), 90));
        openingGrid.Columns.Add(CreateTextColumn("Категория", nameof(OpeningViewRow.CategoryName), 100));
        openingGrid.Columns.Add(CreateTextColumn("Семейство", nameof(OpeningViewRow.FamilyName), 170));
        openingGrid.Columns.Add(CreateTextColumn("Тип", nameof(OpeningViewRow.TypeName), 170));
        openingGrid.Columns.Add(CreateTextColumn("Уровень", nameof(OpeningViewRow.LevelName), 120));
        openingGrid.Columns.Add(CreateTextColumn("Имя вида", nameof(OpeningViewRow.ViewName), new DataGridLength(1, DataGridLengthUnitType.Star)));
        openingGrid.Columns.Add(CreateTextColumn("Ориентация", nameof(OpeningViewRow.OrientationSource), 130));
        openingGrid.Columns.Add(CreateTextColumn("Статус", nameof(OpeningViewRow.Status), 100));
        openingGrid.Columns.Add(CreateTextColumn("Сообщение", nameof(OpeningViewRow.Message), 250));
        panel.Children.Add(openingGrid);
        return panel;
    }

    private DataGrid CreateReportGrid()
    {
        reportGrid.AutoGenerateColumns = false;
        reportGrid.CanUserAddRows = false;
        reportGrid.CanUserDeleteRows = false;
        reportGrid.IsReadOnly = true;
        reportGrid.ItemsSource = reportRows;
        reportGrid.Columns.Add(CreateTextColumn("Этап", nameof(OpeningViewReportRow.Phase), 110));
        reportGrid.Columns.Add(CreateTextColumn("План", nameof(OpeningViewReportRow.SourceViewName), 140));
        reportGrid.Columns.Add(CreateTextColumn("ElementId", nameof(OpeningViewReportRow.ElementId), 90));
        reportGrid.Columns.Add(CreateTextColumn("Категория", nameof(OpeningViewReportRow.CategoryName), 100));
        reportGrid.Columns.Add(CreateTextColumn("Семейство", nameof(OpeningViewReportRow.FamilyName), 160));
        reportGrid.Columns.Add(CreateTextColumn("Тип", nameof(OpeningViewReportRow.TypeName), 160));
        reportGrid.Columns.Add(CreateTextColumn("Уровень", nameof(OpeningViewReportRow.LevelName), 120));
        reportGrid.Columns.Add(CreateTextColumn("Имя вида", nameof(OpeningViewReportRow.ViewName), new DataGridLength(1, DataGridLengthUnitType.Star)));
        reportGrid.Columns.Add(CreateTextColumn("Статус", nameof(OpeningViewReportRow.Status), 100));
        reportGrid.Columns.Add(CreateTextColumn("Сообщение", nameof(OpeningViewReportRow.Message), 260));
        return reportGrid;
    }

    private void Preview()
    {
        if (!HasSelectedCategory())
        {
            UpdateStatus("Выберите хотя бы одну категорию: двери, окна или витражи.");
            return;
        }

        UpdateStatus("Предпросмотр поставлен в очередь Revit.");
        revitActions.Raise(PreviewInRevitContext);
    }

    private void PreviewInRevitContext()
    {
        SaveProfile();
        OpeningViewProfile profile = CreateProfileFromInputs();
        openingRows.Clear();
        reportRows.Clear();
        candidates.Clear();

        foreach (OpeningViewCandidate candidate in collectorService.CollectOpenings(document, activePlan, profile))
        {
            candidates.Add(candidate);
            OpeningViewRow row = candidate.ToRow();
            row.PropertyChanged += OnOpeningRowPropertyChanged;
            openingRows.Add(row);
            reportRows.Add(new OpeningViewReportRow(
                "Предпросмотр",
                activePlan.Name,
                candidate.ElementId,
                candidate.CategoryName,
                candidate.FamilyName,
                candidate.TypeName,
                candidate.LevelName,
                candidate.ViewName,
                row.Status,
                row.Message));
        }

        exportReportButton.IsEnabled = reportRows.Count > 0;
        RefreshVisibleRows();
        UpdateStatus(openingRows.Count == 0
            ? "Ничего не найдено. Проверьте видимость элементов, категории и фильтры активного плана."
            : $"Предпросмотр: найдено элементов — {openingRows.Count}.");
    }

    private void Apply()
    {
        UpdateStatus("Создание видов поставлено в очередь Revit.");
        revitActions.Raise(ApplyInRevitContext);
    }

    private void ApplyInRevitContext()
    {
        SaveProfile();
        if (openingRows.Count == 0)
        {
            PreviewInRevitContext();
        }

        HashSet<long> selectedElementIds = openingRows
            .Where(row => row.IsSelected && row.CanApply)
            .Select(row => row.ElementId)
            .ToHashSet();
        if (selectedElementIds.Count == 0)
        {
            Autodesk.Revit.UI.TaskDialog.Show(DialogTitle, "Нет выбранных проёмов, готовых к созданию фасадных видов.");
            return;
        }

        MessageBoxResult decision = MessageBox.Show(
            this,
            $"Создать фасадные elevation-виды для выбранных проёмов: {selectedElementIds.Count}?",
            DialogTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        OpeningViewApplyResult result = creationService.Apply(
            document,
            activePlan,
            candidates,
            selectedElementIds,
            CreateProfileFromInputs(),
            logger);

        Dictionary<long, OpeningViewReportRow> resultRowsByElementId = result.Rows
            .GroupBy(row => row.ElementId)
            .ToDictionary(group => group.Key, group => group.Last());
        foreach (OpeningViewRow row in openingRows)
        {
            if (resultRowsByElementId.TryGetValue(row.ElementId, out OpeningViewReportRow? resultRow))
            {
                row.ApplyResult(resultRow);
            }
        }

        reportRows.Clear();
        foreach (OpeningViewReportRow row in result.Rows)
        {
            reportRows.Add(row);
        }

        exportReportButton.IsEnabled = reportRows.Count > 0;
        Autodesk.Revit.UI.TaskDialog.Show(DialogTitle, result.ToDialogText());
        UpdateStatus(
            $"Создано: {result.CreatedCount}. Пропущено: {result.SkippedCount}. Ошибок: {result.FailedCount}. "
            + "Следующий шаг: откройте созданный фасад и выберите «Шаг 3: оформить активный фасад» в меню плагина.");
    }

    private void ExportReport()
    {
        if (reportRows.Count == 0)
        {
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "Сохранить отчёт фасадов проёмов",
            Filter = "CSV UTF-8 (*.csv)|*.csv",
            FileName = "opening-views-report.csv",
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

    private OpeningViewProfile CreateProfileFromInputs()
    {
        return OpeningViewProfileStorage.Normalize(new OpeningViewProfile
        {
            Name = profileNameInput.Text,
            IncludeDoors = includeDoorsInput.IsChecked == true,
            IncludeWindows = includeWindowsInput.IsChecked == true,
            IncludeCurtainWalls = includeCurtainWallsInput.IsChecked == true,
            ElevationViewTypeId = (elevationTypeInput.SelectedItem as OpeningViewTypeOption)?.ElementId,
            ViewTemplateId = (viewTemplateInput.SelectedItem as OpeningViewTemplateOption)?.ElementId,
            Scale = ParseInt(scaleInput.Text, 50),
            CropMarginMm = ParseDouble(cropMarginInput.Text, 600),
            DepthMarginMm = ParseDouble(depthMarginInput.Text, 600),
            OrientationSource = orientationSourceInput.SelectedValue as string ?? OpeningViewOrientationSources.ElementFacing,
            ViewNameTemplate = viewNameTemplateInput.Text
        });
    }

    private void SetVisibleRowsSelected(bool isSelected)
    {
        foreach (OpeningViewRow row in GetFilteredRows().Where(row => row.CanApply))
        {
            row.IsSelected = isSelected;
        }

        UpdateStatus();
    }

    private void RefreshVisibleRows()
    {
        if (openingRows.Count == 0)
        {
            UpdateStatus();
            return;
        }

        ICollectionView view = CollectionViewSource.GetDefaultView(openingRows);
        view.Filter = row => row is OpeningViewRow openingRow && IsRowVisible(openingRow);
        view.Refresh();
        UpdateStatus();
    }

    private IEnumerable<OpeningViewRow> GetFilteredRows()
    {
        return openingRows.Where(IsRowVisible);
    }

    private bool IsRowVisible(OpeningViewRow row)
    {
        string filter = filterInput.Text.Trim();
        return string.IsNullOrWhiteSpace(filter)
            || row.ElementId.ToString(CultureInfo.InvariantCulture).IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
            || row.CategoryName.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
            || row.FamilyName.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
            || row.TypeName.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
            || row.LevelName.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
            || row.ViewName.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
            || row.Status.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    private void OnOpeningRowPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(OpeningViewRow.IsSelected) or nameof(OpeningViewRow.Status))
        {
            UpdateStatus();
        }
    }

    private void UpdateStatus(string? prefix = null)
    {
        int readyRows = openingRows.Count(row => row.CanApply);
        int selectedRows = openingRows.Count(row => row.IsSelected && row.CanApply);
        string elevationType = (elevationTypeInput.SelectedItem as OpeningViewTypeOption)?.DisplayName ?? "не выбран";
        List<string> selectedCategories = [];
        if (includeDoorsInput.IsChecked == true)
        {
            selectedCategories.Add("двери");
        }

        if (includeWindowsInput.IsChecked == true)
        {
            selectedCategories.Add("окна");
        }

        if (includeCurtainWallsInput.IsChecked == true)
        {
            selectedCategories.Add("витражи");
        }

        string categories = selectedCategories.Count == 0 ? "не выбраны" : string.Join(", ", selectedCategories);
        string orientation = OpeningViewOrientationSources.GetDisplayName(orientationSourceInput.SelectedValue as string).ToLowerInvariant();
        string text = $"Элементов: {openingRows.Count}. Готово: {readyRows}. Выбрано: {selectedRows}. Категории: {categories}. Ориентация дверей/окон: {orientation}. Тип фасада: {elevationType}. Отчётных строк: {reportRows.Count}.";
        statusText.Text = string.IsNullOrWhiteSpace(prefix) ? text : $"{prefix} {text}";
        previewButton.IsEnabled = selectedCategories.Count > 0;
        previewButton.ToolTip = previewButton.IsEnabled
            ? "Собрать выбранные категории с активного плана и проверить будущие виды без изменения модели."
            : "Выберите хотя бы одну категорию: двери, окна или витражи.";
        applyButton.IsEnabled = selectedRows > 0;
        applyButton.ToolTip = applyButton.IsEnabled
            ? $"Создать выбранные фасадные elevation-виды: {selectedRows}. После создания откройте фасад и запустите его оформление."
            : openingRows.Count == 0
                ? "Сначала выполните «1. Предпросмотр»."
                : "Отметьте хотя бы одну строку со статусом «Готово».";
        exportReportButton.ToolTip = exportReportButton.IsEnabled
            ? "Сохранить CSV-отчёт по предпросмотру или результатам создания видов."
            : "Сначала выполните предпросмотр, чтобы сформировать строки отчёта.";
    }

    private bool HasSelectedCategory()
    {
        return includeDoorsInput.IsChecked == true
            || includeWindowsInput.IsChecked == true
            || includeCurtainWallsInput.IsChecked == true;
    }

    private Button CreateGuideButton()
    {
        Button guideButton = new()
        {
            Content = new Image
            {
                Source = IconFactory.CreateImage(TrueBimIcon.Help, 18),
                Width = 18,
                Height = 18,
                Stretch = Stretch.Uniform
            },
            Width = 34,
            Height = 32,
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = CreateGuideToolTip()
        };
        AutomationProperties.SetName(guideButton, "Открыть методичку по фасадам проёмов");
        AutomationProperties.SetHelpText(guideButton, "Пошаговый сценарий создания и оформления фасадов.");
        guideButton.Click += (_, _) => ShowGuide();
        return guideButton;
    }

    private static ToolTip CreateGuideToolTip()
    {
        StackPanel content = new()
        {
            Width = 330,
            Margin = new Thickness(2)
        };
        content.Children.Add(new TextBlock
        {
            Text = "Методичка по фасадам проёмов",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        content.Children.Add(CreateMutedText("Нажмите, чтобы открыть пошаговое описание: что собирается с активного плана, что делает предпросмотр, как создаются elevation-виды и какие ограничения есть у инструмента."));

        return new ToolTip
        {
            Content = content
        };
    }

    private void ShowGuide()
    {
        logger.Info("Opening Views guide requested from the window header.");
        OpeningViewsGuideWindow guideWindow = new()
        {
            Owner = this
        };
        guideWindow.ShowDialog();
    }

    private static int ParseInt(string text, int fallback)
    {
        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int currentCultureValue)
            ? currentCultureValue
            : int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int invariantValue)
                ? invariantValue
                : fallback;
    }

    private static double ParseDouble(string text, double fallback)
    {
        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out double currentCultureValue)
            ? currentCultureValue
            : double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double invariantValue)
                ? invariantValue
                : fallback;
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

    private static void AddInlineLabel(Panel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });
    }

    private static TextBlock CreateMutedText(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };
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
