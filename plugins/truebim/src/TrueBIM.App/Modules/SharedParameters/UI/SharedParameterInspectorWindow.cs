using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Microsoft.Win32;
using TrueBIM.App.Modules.SharedParameters.Models;
using TrueBIM.App.Modules.SharedParameters.Revit;
using TrueBIM.App.Modules.SharedParameters.Services;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;
using Forms = System.Windows.Forms;
using Binding = System.Windows.Data.Binding;
using Grid = System.Windows.Controls.Grid;
using UIApplication = Autodesk.Revit.UI.UIApplication;
using UIDocument = Autodesk.Revit.UI.UIDocument;
using Visibility = System.Windows.Visibility;

namespace TrueBIM.App.Modules.SharedParameters.UI;

public sealed class SharedParameterInspectorWindow : TrueBimWindow
{
    private const int VisibleElementLimit = 500;

    private readonly UIApplication application;
    private readonly SharedParameterProjectCatalogService catalogService;
    private readonly SharedParameterProjectAnalysisService projectAnalysisService;
    private readonly SharedParameterFamilyAnalysisService familyAnalysisService;
    private readonly SharedParameterDeletionWorkflow deletionWorkflow;
    private readonly SharedParameterDeletionPlanBuilder deletionPlanBuilder;
    private readonly SharedParameterSearchService searchService;
    private readonly SharedParameterFamilyFileScanner familyFileScanner;
    private readonly SharedParameterReportExportService reportExportService;
    private readonly ITrueBimLogger logger;
    private readonly RevitActionDispatcher revitActions;

    private readonly ObservableCollection<SharedParameterListRow> parameterRows = [];
    private readonly ObservableCollection<FamilySourceRow> familySourceRows = [];
    private readonly ObservableCollection<FamilyParameterChoice> familyParameterChoices = [];
    private readonly ObservableCollection<FamilyReportRow> familyReportRows = [];
    private readonly Dictionary<string, SharedParameterProjectAnalysis> analysisCache = new(StringComparer.Ordinal);
    private readonly List<FamilyParameterUsageReport> familyReports = [];

    private readonly TabControl mainTabs = new();
    private readonly TabItem projectTab = new() { Header = "Проект" };
    private readonly TabItem familiesTab = new() { Header = "Семейства" };
    private readonly TabItem reportTab = new() { Header = "Отчёт" };
    private readonly TextBox searchInput = TrueBimUi.CreateSearchBox("Поиск по имени, полному GUID или части GUID.");
    private readonly ComboBox parameterFilterInput = new();
    private readonly DataGrid parameterGrid = CreateDataGrid();
    private readonly TextBlock parameterCardText = CreateWrappedText();
    private readonly TextBlock analysisSummaryText = CreateWrappedText();
    private readonly TextBlock analysisLimitText = CreateWrappedText();
    private readonly DataGrid elementAggregateGrid = CreateDataGrid();
    private readonly DataGrid elementGrid = CreateDataGrid();
    private readonly DataGrid scheduleGrid = CreateDataGrid();
    private readonly DataGrid viewFilterGrid = CreateDataGrid();
    private readonly DataGrid globalParameterGrid = CreateDataGrid();
    private readonly DataGrid familyPresenceGrid = CreateDataGrid();
    private readonly Button analyzeButton;
    private readonly Button familyPresenceButton;
    private readonly Button deleteButton;

    private readonly ComboBox familySourceInput = new();
    private readonly ComboBox familyParameterInput = new();
    private readonly TextBox familyGuidInput = new();
    private readonly TextBox folderPathInput = new();
    private readonly CheckBox includeSubfoldersInput = new();
    private readonly DataGrid familySourceGrid = CreateDataGrid();
    private readonly DataGrid familyReportGrid = CreateDataGrid();
    private readonly TextBlock familyDetailsText = CreateWrappedText();
    private readonly DataGrid familyTypeValueGrid = CreateDataGrid();
    private readonly DataGrid familyFormulaGrid = CreateDataGrid();
    private readonly DataGrid familyDimensionGrid = CreateDataGrid();
    private readonly DataGrid familyAssociationGrid = CreateDataGrid();
    private readonly DataGrid familyNestedGrid = CreateDataGrid();
    private readonly TextBlock familyLimitationsText = CreateWrappedText();
    private readonly Button scanFolderButton;
    private readonly Button selectFilesButton;
    private readonly Button scanFamilyParametersButton;
    private readonly Button analyzeFamiliesButton;

    private readonly TextBox reportText = new();
    private readonly Button exportReportButton;
    private readonly TextBlock statusText = CreateWrappedText();
    private readonly TextBlock progressCountText = new();
    private readonly ProgressBar progressBar = new();
    private readonly Button cancelButton;

    private IReadOnlyList<SharedParameterDescriptor> allParameters = [];
    private SharedParameterProjectAnalysis? currentAnalysis;
    private SharedParameterProjectAnalysis? reportAnalysis;
    private SharedParameterDeletionResult? lastDeletion;
    private CancellationTokenSource? cancellationSource;
    private bool analysisIsStale;
    private bool activeDocumentIsFamily;
    private DocumentIdentity? activeDocumentIdentity;
    private string currentFamilyCategoryName = "Без категории";

    public SharedParameterInspectorWindow(
        UIApplication application,
        SharedParameterProjectCatalogService catalogService,
        SharedParameterProjectAnalysisService projectAnalysisService,
        SharedParameterFamilyAnalysisService familyAnalysisService,
        SharedParameterDeletionWorkflow deletionWorkflow,
        SharedParameterDeletionPlanBuilder deletionPlanBuilder,
        SharedParameterSearchService searchService,
        SharedParameterFamilyFileScanner familyFileScanner,
        SharedParameterReportExportService reportExportService,
        ITrueBimLogger logger)
    {
        this.application = application ?? throw new ArgumentNullException(nameof(application));
        this.catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
        this.projectAnalysisService = projectAnalysisService ?? throw new ArgumentNullException(nameof(projectAnalysisService));
        this.familyAnalysisService = familyAnalysisService ?? throw new ArgumentNullException(nameof(familyAnalysisService));
        this.deletionWorkflow = deletionWorkflow ?? throw new ArgumentNullException(nameof(deletionWorkflow));
        this.deletionPlanBuilder = deletionPlanBuilder ?? throw new ArgumentNullException(nameof(deletionPlanBuilder));
        this.searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        this.familyFileScanner = familyFileScanner ?? throw new ArgumentNullException(nameof(familyFileScanner));
        this.reportExportService = reportExportService ?? throw new ArgumentNullException(nameof(reportExportService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        revitActions = new RevitActionDispatcher("Общие параметры", logger);

        Title = "Общие параметры";
        Width = 1420;
        Height = 900;
        MinWidth = 1100;
        MinHeight = 700;
        Icon = IconFactory.CreateImage(TrueBimIcon.SharedParameters, 32);

        analyzeButton = TrueBimUi.CreatePrimaryButton(
            "Проверить использование",
            TrueBimIcon.Preview,
            (_, _) => AnalyzeSelectedParameter(),
            isEnabled: false,
            minWidth: 190);
        familyPresenceButton = TrueBimUi.CreateSecondaryButton(
            "Найти в семействах проекта",
            TrueBimIcon.FamilyManager,
            (_, _) => AnalyzeProjectFamilyPresence(),
            isEnabled: false,
            minWidth: 210);
        deleteButton = TrueBimUi.CreateDangerButton(
            "Удалить из проекта",
            TrueBimIcon.Delete,
            (_, _) => PrepareDeletion(),
            isEnabled: false,
            minWidth: 160);
        scanFolderButton = TrueBimUi.CreateSecondaryButton(
            "Выбрать папку",
            TrueBimIcon.Open,
            (_, _) => BrowseFamilyFolder(),
            minWidth: 135);
        selectFilesButton = TrueBimUi.CreateSecondaryButton(
            "Выбрать RFA",
            TrueBimIcon.Open,
            (_, _) => BrowseFamilyFiles(),
            minWidth: 130);
        scanFamilyParametersButton = TrueBimUi.CreateSecondaryButton(
            "Собрать каталог GUID",
            TrueBimIcon.Preview,
            (_, _) => ScanFamilyParameterCatalogs(),
            isEnabled: false,
            minWidth: 180);
        analyzeFamiliesButton = TrueBimUi.CreatePrimaryButton(
            "Анализировать семейства",
            TrueBimIcon.Preview,
            (_, _) => AnalyzeSelectedFamilies(),
            isEnabled: false,
            minWidth: 195);
        exportReportButton = TrueBimUi.CreatePrimaryButton(
            "Экспорт HTML / JSON / CSV / TXT",
            TrueBimIcon.Export,
            (_, _) => ExportReport(),
            isEnabled: false,
            minWidth: 250);
        cancelButton = TrueBimUi.CreateSecondaryButton(
            "Отменить операцию",
            TrueBimIcon.Close,
            (_, _) => cancellationSource?.Cancel(),
            isEnabled: false,
            minWidth: 145);

        ConfigureControls();
        ConfigureActionToolTips();
        projectTab.Content = CreateProjectTab();
        familiesTab.Content = CreateFamiliesTab();
        reportTab.Content = CreateReportTab();
        mainTabs.Items.Add(projectTab);
        mainTabs.Items.Add(familiesTab);
        mainTabs.Items.Add(reportTab);
        mainTabs.Style = TrueBimStyles.CreateTabControlStyle();

        Button closeButton = TrueBimUi.CreateSecondaryButton(
            "Закрыть",
            TrueBimIcon.Close,
            (_, _) => Close());
        closeButton.IsCancel = true;

        ApplyTrueBimShell(
            CreateWindowHeader(),
            null,
            mainTabs,
            CreateProgressStatus(),
            TrueBimUi.CreateFooter(null, closeButton));

        Loaded += (_, _) => RefreshCatalog();
        Closed += (_, _) =>
        {
            cancellationSource?.Cancel();
            if (!revitActions.Raise(() =>
                {
                    application.Application.DocumentChanged -= OnDocumentChanged;
                }))
            {
                logger.Warning(
                    "Revit rejected Shared Parameter Inspector DocumentChanged event cleanup.");
            }
        };
        application.Application.DocumentChanged += OnDocumentChanged;
    }

    private UIElement CreateWindowHeader()
    {
        Grid header = new();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        UIElement title = TrueBimUi.CreateHeader(
            "Общие параметры",
            "Анализ использования и безопасное удаление общих параметров в проекте и выбранных семействах.",
            TrueBimIcon.SharedParameters);
        header.Children.Add(title);

        Button guideButton = TrueBimUi.CreateSecondaryButton(
            "Методичка",
            TrueBimIcon.Help,
            (_, _) => ShowGuide(SharedParameterGuideTopic.Overview),
            minWidth: 130);
        guideButton.Margin = new Thickness(
            TrueBimTheme.Spacing16,
            TrueBimTheme.Spacing4,
            0,
            TrueBimTheme.Spacing16);
        guideButton.VerticalAlignment = VerticalAlignment.Top;
        guideButton.ToolTip = "Открыть общую методичку по вкладкам, рекомендуемому маршруту и безопасному удалению.";
        AutomationProperties.SetName(guideButton, "Открыть общую методичку по общим параметрам");
        AutomationProperties.SetHelpText(guideButton, "Пошаговая справка по анализу проекта, семействам, отчёту и удалению.");
        Grid.SetColumn(guideButton, 1);
        header.Children.Add(guideButton);
        return header;
    }

    private Grid CreateGuidedActionRow(
        Button actionButton,
        SharedParameterGuideTopic topic,
        string helpText)
    {
        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        actionButton.MinWidth = 0;
        actionButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        row.Children.Add(actionButton);

        Button helpButton = CreateContextHelpButton(topic, helpText);
        helpButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        Grid.SetColumn(helpButton, 1);
        row.Children.Add(helpButton);
        return row;
    }

    private Button CreateContextHelpButton(
        SharedParameterGuideTopic topic,
        string helpText)
    {
        Button button = new()
        {
            Content = new Image
            {
                Source = IconFactory.CreateImage(TrueBimIcon.Help, TrueBimTheme.IconSizeSmall),
                Width = TrueBimTheme.IconSizeSmall,
                Height = TrueBimTheme.IconSizeSmall
            },
            Width = TrueBimTheme.ControlHeight32,
            Height = TrueBimTheme.ControlHeight32,
            Padding = new Thickness(TrueBimTheme.Spacing4),
            Style = TrueBimStyles.CreateButtonStyle(TrueBimButtonStyleKind.Ghost),
            ToolTip = helpText
        };
        AutomationProperties.SetName(button, helpText);
        AutomationProperties.SetHelpText(button, "Открыть контекстную справку без запуска действия.");
        button.Click += (_, _) => ShowGuide(topic);
        return button;
    }

    private void ShowGuide(SharedParameterGuideTopic topic)
    {
        SharedParameterGuidePage page = SharedParameterGuideCatalog.Get(topic);
        logger.Info($"Shared Parameter Inspector guide requested. Topic={topic}; Title='{page.Title}'.");
        SharedParameterInspectorGuideWindow guideWindow = new(topic)
        {
            Owner = this
        };
        guideWindow.ShowDialog();
    }

    private static TextBlock CreateActionGroupTitle(
        string text,
        Thickness margin)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = TrueBimBrushes.TextSecondary,
            FontSize = TrueBimTheme.CaptionFontSize,
            FontWeight = FontWeights.SemiBold,
            Margin = margin
        };
    }

    private static Grid CreateEqualButtonRow(params Button[] buttons)
    {
        Grid row = new();
        for (int index = 0; index < buttons.Length; index++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            Button button = buttons[index];
            button.MinWidth = 0;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Margin = new Thickness(
                0,
                0,
                index == buttons.Length - 1 ? 0 : TrueBimTheme.Spacing8,
                0);
            Grid.SetColumn(button, index);
            row.Children.Add(button);
        }

        return row;
    }

    private void ConfigureActionToolTips()
    {
        searchInput.ToolTip = "Фильтр по имени, полному GUID или любой части GUID.";
        parameterFilterInput.ToolTip = "Ограничить список параметров по привязке или найденному использованию.";
        familySourceInput.ToolTip = "Выберите, откуда получить семейства для каталога GUID и глубокого анализа.";
        folderPathInput.ToolTip = "Папка с RFA. Путь заполняется кнопкой «Выбрать папку».";
        includeSubfoldersInput.ToolTip = "Искать поддерживаемые RFA также во вложенных папках.";
        familyParameterInput.ToolTip = "Параметр из собранного каталога GUID. Выбор заполняет поле GUID справа.";
        familyGuidInput.ToolTip = "Полный GUID общего параметра для глубокого анализа семейств.";
        reportText.ToolTip = "Текст последнего анализа, ограничений и результата удаления.";
        scanFolderButton.ToolTip = "Выбрать папку и перечислить поддерживаемые RFA без открытия файлов.";
        selectFilesButton.ToolTip = "Выбрать один или несколько RFA для каталога GUID и глубокого анализа.";
        exportReportButton.ToolTip = "Экспортировать последний результат в HTML, JSON, CSV и TXT.";
        cancelButton.ToolTip = "Запросить отмену текущей пакетной операции на ближайшей безопасной точке.";

        foreach (Button button in new[]
                 {
                     analyzeButton,
                     familyPresenceButton,
                     deleteButton,
                     scanFolderButton,
                     selectFilesButton,
                     scanFamilyParametersButton,
                     analyzeFamiliesButton,
                     exportReportButton,
                     cancelButton
                 })
        {
            ToolTipService.SetShowOnDisabled(button, true);
        }

        UpdateActionToolTips();
    }

    private void UpdateActionToolTips()
    {
        bool analysisMatchesSelection = SelectedParameter is not null
            && currentAnalysis?.Parameter.IdentityKey == SelectedParameter.IdentityKey;

        analyzeButton.ToolTip = IsBusy
            ? "Дождитесь завершения или отмените текущую операцию."
            : SelectedParameter is null
                ? "Выберите параметр в таблице слева."
                : "Проверить элементы, спецификации, фильтры видов и глобальные ассоциации выбранного GUID без изменения модели.";

        familyPresenceButton.ToolTip = IsBusy
            ? "Дождитесь завершения или отмените текущую операцию."
            : SelectedParameter is null
                ? "Выберите параметр в таблице слева."
                : !analysisMatchesSelection
                    ? "Сначала выполните «Проверить использование» для выбранного параметра."
                    : "Проверить наличие выбранного GUID в загружаемых семействах активного проекта.";

        deleteButton.ToolTip = IsBusy
            ? "Дождитесь завершения или отмените текущую операцию."
            : activeDocumentIsFamily
                ? "Удаление доступно только в проектном документе."
                : SelectedParameter is null
                    ? "Выберите параметр в таблице слева."
                    : "Открыть безопасный сценарий: свежий анализ, dry run с откатом, план, подтверждение и контрольная проверка.";

        FamilySourceKind source = ReadChoice(
            familySourceInput,
            FamilySourceKind.ActiveProject);
        scanFolderButton.ToolTip = source == FamilySourceKind.Folder
            ? "Выбрать папку и перечислить поддерживаемые RFA без открытия файлов."
            : "Выберите источник «Из папки».";
        selectFilesButton.ToolTip = source == FamilySourceKind.SelectedFiles
            ? "Выбрать один или несколько RFA для каталога GUID и глубокого анализа."
            : "Выберите источник «Выбрать файлы».";
        scanFamilyParametersButton.ToolTip = IsBusy
            ? "Дождитесь завершения или отмените текущую операцию."
            : source is not (FamilySourceKind.Folder or FamilySourceKind.SelectedFiles)
                ? "Каталог GUID нужен только для внешних RFA из папки или выбранных файлов."
                : familySourceRows.Count == 0
                    ? "Сначала добавьте RFA в таблицу источников."
                    : "Открыть выбранные RFA по одному, собрать общие параметры и объединить их по GUID без сохранения файлов.";
        analyzeFamiliesButton.ToolTip = IsBusy
            ? "Дождитесь завершения или отмените текущую операцию."
            : !Guid.TryParse(familyGuidInput.Text, out _)
                ? "Выберите параметр из каталога или введите корректный полный GUID."
                : familySourceRows.Count == 0
                    ? "Сначала добавьте или выберите семейства для анализа."
                    : "Проверить значения типов, формулы, размеры, ассоциации и вложенные семейства без сохранения внешних RFA.";
        exportReportButton.ToolTip = IsBusy
            ? "Дождитесь завершения или отмените текущую операцию."
            : currentAnalysis is null && reportAnalysis is null
                ? "Сначала выполните проектную проверку использования."
                : "Экспортировать последний результат в HTML, JSON, CSV и TXT.";
        cancelButton.ToolTip = IsBusy
            ? "Запросить отмену на ближайшей безопасной точке пакетной операции."
            : "Сейчас нет операции, которую можно отменить.";
    }

    private void ConfigureControls()
    {
        searchInput.TextChanged += (_, _) => ApplyParameterFilter();
        parameterFilterInput.Style = TrueBimStyles.CreateComboBoxStyle();
        parameterFilterInput.DisplayMemberPath = nameof(Choice<SharedParameterListFilter>.DisplayName);
        parameterFilterInput.ItemsSource = new Choice<SharedParameterListFilter>[]
        {
            new(SharedParameterListFilter.All, "Все"),
            new(SharedParameterListFilter.Instance, "Параметры экземпляра"),
            new(SharedParameterListFilter.Type, "Параметры типа"),
            new(SharedParameterListFilter.Bound, "С привязкой"),
            new(SharedParameterListFilter.Unbound, "Без привязки"),
            new(SharedParameterListFilter.UsedInSchedules, "Используются в спецификациях"),
            new(SharedParameterListFilter.UsedInViewFilters, "Используются в фильтрах"),
            new(SharedParameterListFilter.PresentInFamilies, "Присутствуют в семействах"),
            new(SharedParameterListFilter.Unused, "Не используются")
        };
        parameterFilterInput.SelectedIndex = 0;
        parameterFilterInput.SelectionChanged += (_, _) => ApplyParameterFilter();

        parameterGrid.ItemsSource = parameterRows;
        AddTextColumn(parameterGrid, "Имя", nameof(SharedParameterListRow.Name), 1.6);
        AddTextColumn(parameterGrid, "GUID", nameof(SharedParameterListRow.ShortGuid), 0.75);
        AddTextColumn(parameterGrid, "Тип данных", nameof(SharedParameterListRow.DataType), 0.9);
        AddTextColumn(parameterGrid, "Привязка", nameof(SharedParameterListRow.Binding), 0.85);
        AddTextColumn(parameterGrid, "Категории", nameof(SharedParameterListRow.CategoryCount), 0.65);
        parameterGrid.SelectionChanged += (_, _) => SelectedParameterChanged();

        elementGrid.SelectionMode = DataGridSelectionMode.Extended;
        AddTextColumn(elementAggregateGrid, "Категория", nameof(ElementUsageAggregate.CategoryName), 1.5);
        AddTextColumn(elementAggregateGrid, "Элементы", nameof(ElementUsageAggregate.ElementCount), 0.65);
        AddTextColumn(elementAggregateGrid, "Есть параметр", nameof(ElementUsageAggregate.HasParameterCount), 0.75);
        AddTextColumn(elementAggregateGrid, "Заполнено", nameof(ElementUsageAggregate.FilledCount), 0.65);
        AddTextColumn(elementAggregateGrid, "Пусто", nameof(ElementUsageAggregate.EmptyCount), 0.55);
        AddTextColumn(elementAggregateGrid, "Read-only", nameof(ElementUsageAggregate.ReadOnlyCount), 0.65);
        AddTextColumn(elementGrid, "Категория", nameof(ElementParameterUsage.CategoryName), 1.1);
        AddTextColumn(elementGrid, "Имя", nameof(ElementParameterUsage.Name), 1.2);
        AddTextColumn(elementGrid, "Семейство", nameof(ElementParameterUsage.FamilyName), 1.1);
        AddTextColumn(elementGrid, "Тип", nameof(ElementParameterUsage.TypeName), 1.1);
        AddTextColumn(elementGrid, "ElementId", nameof(ElementParameterUsage.ElementId), 0.75);
        AddCheckColumn(elementGrid, "Заполнено", nameof(ElementParameterUsage.HasValue));
        AddCheckColumn(elementGrid, "Read-only", nameof(ElementParameterUsage.IsReadOnly));

        AddTextColumn(scheduleGrid, "Спецификация", nameof(ScheduleFieldUsage.ScheduleName), 1.5);
        AddTextColumn(scheduleGrid, "Поле", nameof(ScheduleFieldUsage.FieldName), 1.1);
        AddTextColumn(scheduleGrid, "Заголовок", nameof(ScheduleFieldUsage.ColumnHeading), 1.1);
        AddCheckColumn(scheduleGrid, "Скрыто", nameof(ScheduleFieldUsage.IsHidden));
        AddCheckColumn(scheduleGrid, "Фильтр", nameof(ScheduleFieldUsage.UsedInFilter));
        AddCheckColumn(scheduleGrid, "Сортировка", nameof(ScheduleFieldUsage.UsedInSortOrGroup));

        AddTextColumn(viewFilterGrid, "Фильтр", nameof(ViewFilterUsage.FilterName), 1.6);
        AddTextColumn(viewFilterGrid, "ElementId", nameof(ViewFilterUsage.FilterId), 0.7);
        AddTextColumn(viewFilterGrid, "Confidence", nameof(ViewFilterUsage.Confidence), 0.8);
        AddTextColumn(viewFilterGrid, "Перестроение", nameof(ViewFilterUsage.CanRebuildWithoutTarget), 0.8);

        AddTextColumn(globalParameterGrid, "Элемент", nameof(GlobalParameterAssociationUsage.ElementName), 1.2);
        AddTextColumn(globalParameterGrid, "Категория", nameof(GlobalParameterAssociationUsage.ElementCategory), 1.0);
        AddTextColumn(globalParameterGrid, "ElementId", nameof(GlobalParameterAssociationUsage.ElementId), 0.7);
        AddTextColumn(globalParameterGrid, "Глобальный параметр", nameof(GlobalParameterAssociationUsage.GlobalParameterName), 1.3);
        AddTextColumn(globalParameterGrid, "Global ElementId", nameof(GlobalParameterAssociationUsage.GlobalParameterId), 0.8);
        AddTextColumn(globalParameterGrid, "Формула", nameof(GlobalParameterAssociationUsage.Formula), 1.2);

        AddTextColumn(familyPresenceGrid, "Семейство", nameof(ProjectFamilyPresence.FamilyName), 1.6);
        AddTextColumn(familyPresenceGrid, "Категория", nameof(ProjectFamilyPresence.CategoryName), 1.2);
        AddTextColumn(familyPresenceGrid, "Статус", nameof(ProjectFamilyPresence.Status), 0.9);
        AddCheckColumn(familyPresenceGrid, "Параметр найден", nameof(ProjectFamilyPresence.ContainsParameter));

        familySourceInput.Style = TrueBimStyles.CreateComboBoxStyle();
        familySourceInput.DisplayMemberPath = nameof(Choice<FamilySourceKind>.DisplayName);
        familySourceInput.ItemsSource = new Choice<FamilySourceKind>[]
        {
            new(FamilySourceKind.ActiveProject, "Из активного проекта"),
            new(FamilySourceKind.Folder, "Из папки"),
            new(FamilySourceKind.SelectedFiles, "Выбрать файлы"),
            new(FamilySourceKind.CurrentFamily, "Текущее семейство")
        };
        familySourceInput.SelectedIndex = 0;
        familySourceInput.SelectionChanged += (_, _) => FamilySourceChanged();

        familyParameterInput.Style = TrueBimStyles.CreateComboBoxStyle();
        familyParameterInput.DisplayMemberPath = nameof(FamilyParameterChoice.DisplayName);
        familyParameterInput.ItemsSource = familyParameterChoices;
        familyParameterInput.ToolTip =
            "Параметры активного документа или объединённый GUID-каталог выбранных RFA.";
        familyParameterInput.SelectionChanged += (_, _) =>
        {
            if (familyParameterInput.SelectedItem is FamilyParameterChoice choice)
            {
                familyGuidInput.Text = choice.Guid.ToString("D");
            }
        };
        familyGuidInput.Style = TrueBimStyles.CreateTextBoxStyle();
        familyGuidInput.ToolTip =
            "GUID общего параметра. Можно выбрать найденный параметр слева или ввести GUID вручную.";
        familyGuidInput.TextChanged += (_, _) => UpdateFamilyActionsState();
        folderPathInput.Style = TrueBimStyles.CreateTextBoxStyle();
        folderPathInput.IsReadOnly = true;
        includeSubfoldersInput.Content = "Включать вложенные папки";
        includeSubfoldersInput.IsChecked = true;
        includeSubfoldersInput.Style = TrueBimStyles.CreateCheckBoxStyle();

        familySourceGrid.ItemsSource = familySourceRows;
        familySourceGrid.SelectionMode = DataGridSelectionMode.Extended;
        AddTextColumn(familySourceGrid, "Источник", nameof(FamilySourceRow.SourceDisplay), 0.9);
        AddTextColumn(familySourceGrid, "Семейство / файл", nameof(FamilySourceRow.Name), 1.5);
        AddTextColumn(familySourceGrid, "Категория", nameof(FamilySourceRow.CategoryName), 1.0);
        AddTextColumn(familySourceGrid, "Путь", nameof(FamilySourceRow.Path), 2.1);
        AddTextColumn(familySourceGrid, "Статус", nameof(FamilySourceRow.Status), 1.0);
        familySourceGrid.SelectionChanged += (_, _) => UpdateFamilyActionsState();

        familyReportGrid.ItemsSource = familyReportRows;
        AddTextColumn(familyReportGrid, "Семейство", nameof(FamilyReportRow.FamilyName), 1.5);
        AddTextColumn(familyReportGrid, "Параметр", nameof(FamilyReportRow.ParameterStatus), 0.9);
        AddTextColumn(familyReportGrid, "Типы", nameof(FamilyReportRow.TypeCount), 0.5);
        AddTextColumn(familyReportGrid, "Формулы", nameof(FamilyReportRow.FormulaCount), 0.55);
        AddTextColumn(familyReportGrid, "Размеры", nameof(FamilyReportRow.DimensionCount), 0.55);
        AddTextColumn(familyReportGrid, "Ассоциации", nameof(FamilyReportRow.AssociationCount), 0.65);
        AddTextColumn(familyReportGrid, "Blockers", nameof(FamilyReportRow.BlockerCount), 0.55);
        familyReportGrid.SelectionChanged += (_, _) => RenderSelectedFamilyReport();

        AddTextColumn(familyTypeValueGrid, "Тип", nameof(FamilyTypeValueUsage.TypeName), 1.2);
        AddCheckColumn(familyTypeValueGrid, "Есть значение", nameof(FamilyTypeValueUsage.HasValue));
        AddTextColumn(familyTypeValueGrid, "Отображаемое", nameof(FamilyTypeValueUsage.DisplayValue), 1.2);
        AddTextColumn(familyTypeValueGrid, "Внутреннее", nameof(FamilyTypeValueUsage.InternalValue), 1.0);
        AddCheckColumn(familyTypeValueGrid, "Формула", nameof(FamilyTypeValueUsage.IsFormulaDriven));

        AddTextColumn(familyFormulaGrid, "Параметр", nameof(FormulaUsage.ParameterName), 1.0);
        AddTextColumn(familyFormulaGrid, "Формула", nameof(FormulaUsage.Formula), 2.0);
        AddCheckColumn(familyFormulaGrid, "Формула цели", nameof(FormulaUsage.IsTargetFormula));
        AddTextColumn(familyFormulaGrid, "Confidence", nameof(FormulaUsage.Confidence), 0.8);

        AddTextColumn(familyDimensionGrid, "ElementId", nameof(DimensionUsage.DimensionId), 0.7);
        AddTextColumn(familyDimensionGrid, "Вид", nameof(DimensionUsage.ViewName), 1.2);
        AddTextColumn(familyDimensionGrid, "Сегменты", nameof(DimensionUsage.SegmentCount), 0.6);
        AddCheckColumn(familyDimensionGrid, "Reporting", nameof(DimensionUsage.IsReporting));
        AddTextColumn(familyDimensionGrid, "Значение", nameof(DimensionUsage.Value), 0.9);

        AddTextColumn(familyAssociationGrid, "Элемент", nameof(AssociatedParameterUsage.ElementName), 1.0);
        AddTextColumn(familyAssociationGrid, "Категория", nameof(AssociatedParameterUsage.CategoryName), 0.9);
        AddTextColumn(familyAssociationGrid, "Параметр", nameof(AssociatedParameterUsage.ParameterName), 1.0);
        AddTextColumn(familyAssociationGrid, "ElementId", nameof(AssociatedParameterUsage.ElementId), 0.7);
        AddTextColumn(familyAssociationGrid, "Направление", nameof(AssociatedParameterUsage.Direction), 1.1);

        AddTextColumn(familyNestedGrid, "Семейство", nameof(NestedFamilyUsage.FamilyName), 1.1);
        AddTextColumn(familyNestedGrid, "Тип", nameof(NestedFamilyUsage.TypeName), 1.0);
        AddTextColumn(familyNestedGrid, "Параметр", nameof(NestedFamilyUsage.ParameterName), 1.0);
        AddTextColumn(familyNestedGrid, "Ассоциация", nameof(NestedFamilyUsage.AssociationKind), 0.8);
        AddTextColumn(familyNestedGrid, "ElementId", nameof(NestedFamilyUsage.ElementId), 0.7);

        reportText.IsReadOnly = true;
        reportText.AcceptsReturn = true;
        reportText.AcceptsTab = true;
        reportText.TextWrapping = TextWrapping.NoWrap;
        reportText.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        reportText.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        reportText.FontFamily = new System.Windows.Media.FontFamily("Consolas");
        reportText.Style = TrueBimStyles.CreateTextBoxStyle();

        progressBar.Minimum = 0;
        progressBar.Maximum = 1;
        progressBar.Height = TrueBimTheme.Spacing8;
        progressBar.Foreground = TrueBimBrushes.Info;
        progressBar.Background = TrueBimBrushes.SurfaceAlt;
    }

    private UIElement CreateProjectTab()
    {
        Grid layout = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0)
        };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(500) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TrueBimTheme.Spacing12) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid left = new();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid searchRow = new()
        {
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        searchInput.Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, 0);
        searchRow.Children.Add(searchInput);
        Grid.SetColumn(parameterFilterInput, 1);
        searchRow.Children.Add(parameterFilterInput);
        left.Children.Add(searchRow);

        Grid.SetRow(parameterGrid, 1);
        left.Children.Add(parameterGrid);

        StackPanel parameterCard = new();
        parameterCard.Children.Add(parameterCardText);

        Button copyNameButton = TrueBimUi.CreateSecondaryButton(
            "Имя",
            TrueBimIcon.Copy,
            (_, _) => CopySelectedParameterValue(parameter => parameter.Name),
            minWidth: 0);
        copyNameButton.ToolTip = "Скопировать точное имя выбранного параметра.";
        Button copyGuidButton = TrueBimUi.CreateSecondaryButton(
            "GUID",
            TrueBimIcon.Copy,
            (_, _) => CopySelectedParameterValue(parameter => parameter.Guid.ToString("D")),
            minWidth: 0);
        copyGuidButton.ToolTip = "Скопировать полный GUID — основной идентификатор общего параметра.";
        Button copyIdButton = TrueBimUi.CreateSecondaryButton(
            "ElementId",
            TrueBimIcon.Copy,
            (_, _) => CopySelectedParameterValue(parameter =>
                parameter.ParameterElementId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            minWidth: 0);
        copyIdButton.ToolTip = "Скопировать ElementId объекта SharedParameterElement.";

        parameterCard.Children.Add(CreateActionGroupTitle(
            "Скопировать",
            new Thickness(0, TrueBimTheme.Spacing12, 0, TrueBimTheme.Spacing8)));
        parameterCard.Children.Add(CreateEqualButtonRow(
            copyNameButton,
            copyGuidButton,
            copyIdButton));

        parameterCard.Children.Add(CreateActionGroupTitle(
            "Проверка использования",
            new Thickness(0, TrueBimTheme.Spacing12, 0, TrueBimTheme.Spacing8)));
        parameterCard.Children.Add(CreateGuidedActionRow(
            analyzeButton,
            SharedParameterGuideTopic.ProjectUsageAnalysis,
            "Что проверяет использование параметра"));

        Grid familyPresenceAction = CreateGuidedActionRow(
            familyPresenceButton,
            SharedParameterGuideTopic.ProjectFamilyPresence,
            "Как выполняется поиск параметра в семействах проекта");
        familyPresenceAction.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        parameterCard.Children.Add(familyPresenceAction);

        parameterCard.Children.Add(CreateActionGroupTitle(
            "Опасная операция",
            new Thickness(0, TrueBimTheme.Spacing12, 0, TrueBimTheme.Spacing8)));
        Border deletionAction = new()
        {
            Background = TrueBimBrushes.DangerBackground,
            BorderBrush = TrueBimBrushes.Danger,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Padding = new Thickness(TrueBimTheme.Spacing8),
            Child = CreateGuidedActionRow(
                deleteButton,
                SharedParameterGuideTopic.SafeDeletion,
                "Как устроено безопасное удаление")
        };
        parameterCard.Children.Add(deletionAction);

        Border parameterCardBorder = TrueBimUi.CreateSectionCard("Выбранный параметр", parameterCard);
        parameterCardBorder.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        Grid.SetRow(parameterCardBorder, 2);
        left.Children.Add(parameterCardBorder);

        Grid.SetColumn(left, 0);
        layout.Children.Add(left);

        TabControl results = new()
        {
            Style = TrueBimStyles.CreateTabControlStyle()
        };
        results.Items.Add(CreateTab("Сводка", new ScrollViewer
        {
            Content = new StackPanel
            {
                Children =
                {
                    TrueBimUi.CreateSectionCard("Результат анализа", analysisSummaryText),
                    CreateMarginCard("Ограничения", analysisLimitText)
                }
            },
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        }));
        results.Items.Add(CreateTab("Элементы", CreateElementResults()));
        results.Items.Add(CreateTab("Спецификации", CreateScheduleResults()));
        results.Items.Add(CreateTab("Фильтры видов", CreateViewFilterResults()));
        results.Items.Add(CreateTab("Глобальные параметры", globalParameterGrid));
        results.Items.Add(CreateTab("Семейства", CreateFamilyPresenceResults()));

        Grid.SetColumn(results, 2);
        layout.Children.Add(results);
        return layout;
    }

    private UIElement CreateElementResults()
    {
        DockPanel panel = new();
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };
        Button selectButton = TrueBimUi.CreateSecondaryButton(
            "Выбрать элементы",
            TrueBimIcon.Preview,
            (_, _) => SelectElements());
        selectButton.ToolTip = "Выбрать в Revit отмеченные строки. Если строки не отмечены, выбираются все показанные элементы.";
        Button copyButton = TrueBimUi.CreateSecondaryButton(
            "Скопировать ElementId",
            TrueBimIcon.Copy,
            (_, _) => CopySelectedElementIds());
        copyButton.ToolTip = "Скопировать ElementId отмеченных или всех показанных элементов.";
        copyButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        Button copyUniqueIdButton = TrueBimUi.CreateSecondaryButton(
            "Скопировать UniqueId",
            TrueBimIcon.Copy,
            (_, _) => CopySelectedUniqueIds());
        copyUniqueIdButton.ToolTip = "Скопировать стабильные UniqueId отмеченных или всех показанных элементов.";
        copyUniqueIdButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        actions.Children.Add(selectButton);
        actions.Children.Add(copyButton);
        actions.Children.Add(copyUniqueIdButton);
        DockPanel.SetDock(actions, Dock.Top);
        panel.Children.Add(actions);
        TabControl elementTabs = new()
        {
            Style = TrueBimStyles.CreateTabControlStyle()
        };
        elementTabs.Items.Add(CreateTab("Сводка по категориям", elementAggregateGrid));
        elementTabs.Items.Add(CreateTab($"Элементы (до {VisibleElementLimit})", elementGrid));
        panel.Children.Add(elementTabs);
        return panel;
    }

    private UIElement CreateScheduleResults()
    {
        DockPanel panel = new();
        Button openButton = TrueBimUi.CreateSecondaryButton(
            "Открыть спецификацию",
            TrueBimIcon.Open,
            (_, _) => OpenSelectedSchedule(),
            minWidth: 160);
        openButton.ToolTip = "Открыть в Revit спецификацию из выбранной строки.";
        openButton.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8);
        DockPanel.SetDock(openButton, Dock.Top);
        panel.Children.Add(openButton);
        panel.Children.Add(scheduleGrid);
        return panel;
    }

    private UIElement CreateViewFilterResults()
    {
        DockPanel panel = new();
        Button openButton = TrueBimUi.CreateSecondaryButton(
            "Показать связанный вид",
            TrueBimIcon.Open,
            (_, _) => OpenFirstViewForSelectedFilter(),
            minWidth: 175);
        openButton.ToolTip = "Открыть первый обычный вид, на котором назначен выбранный фильтр. Шаблоны видов пропускаются.";
        openButton.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8);
        DockPanel.SetDock(openButton, Dock.Top);
        panel.Children.Add(openButton);
        panel.Children.Add(viewFilterGrid);
        return panel;
    }

    private UIElement CreateFamilyPresenceResults()
    {
        DockPanel panel = new();
        Button deepButton = TrueBimUi.CreateSecondaryButton(
            "Перейти к глубокому анализу",
            TrueBimIcon.FamilyManager,
            (_, _) => MoveSelectedFamilyToDeepAnalysis(),
            minWidth: 220);
        deepButton.ToolTip = "Перенести выбранное семейство и GUID на вкладку «Семейства» для проверки формул, размеров и ассоциаций.";
        Grid deepAction = CreateGuidedActionRow(
            deepButton,
            SharedParameterGuideTopic.DeepFamilyAnalysis,
            "Что проверяет глубокий анализ семейства");
        deepAction.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8);
        DockPanel.SetDock(deepAction, Dock.Top);
        panel.Children.Add(deepAction);
        panel.Children.Add(familyPresenceGrid);
        return panel;
    }

    private UIElement CreateFamiliesTab()
    {
        Grid root = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(320) });

        StackPanel sourceSettings = new();
        sourceSettings.Children.Add(CreateActionGroupTitle(
            "1. Источник семейств",
            new Thickness(0, 0, 0, TrueBimTheme.Spacing8)));

        Grid controls = new()
        {
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        familySourceInput.Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, 0);
        controls.Children.Add(familySourceInput);
        folderPathInput.Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, 0);
        Grid.SetColumn(folderPathInput, 1);
        controls.Children.Add(folderPathInput);
        Grid.SetColumn(scanFolderButton, 2);
        controls.Children.Add(scanFolderButton);
        selectFilesButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        Grid.SetColumn(selectFilesButton, 3);
        controls.Children.Add(selectFilesButton);
        sourceSettings.Children.Add(controls);
        includeSubfoldersInput.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        sourceSettings.Children.Add(includeSubfoldersInput);

        sourceSettings.Children.Add(CreateActionGroupTitle(
            "2. Параметр",
            new Thickness(0, 0, 0, TrueBimTheme.Spacing8)));
        Grid parameterControls = new()
        {
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };
        parameterControls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        parameterControls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        familyParameterInput.Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, 0);
        parameterControls.Children.Add(familyParameterInput);
        Grid.SetColumn(familyGuidInput, 1);
        parameterControls.Children.Add(familyGuidInput);
        sourceSettings.Children.Add(parameterControls);

        sourceSettings.Children.Add(CreateActionGroupTitle(
            "3. Анализ",
            new Thickness(0, 0, 0, TrueBimTheme.Spacing8)));
        StackPanel analysisActions = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12)
        };
        Grid scanAction = CreateGuidedActionRow(
            scanFamilyParametersButton,
            SharedParameterGuideTopic.FamilyGuidCatalog,
            "Зачем собирать каталог GUID из RFA");
        scanAction.Width = 235;
        analysisActions.Children.Add(scanAction);

        Grid deepAnalysisAction = CreateGuidedActionRow(
            analyzeFamiliesButton,
            SharedParameterGuideTopic.DeepFamilyAnalysis,
            "Что проверяет глубокий анализ семейств");
        deepAnalysisAction.Width = 250;
        deepAnalysisAction.Margin = new Thickness(TrueBimTheme.Spacing12, 0, 0, 0);
        analysisActions.Children.Add(deepAnalysisAction);
        sourceSettings.Children.Add(analysisActions);

        root.Children.Add(sourceSettings);

        Grid.SetRow(familySourceGrid, 1);
        root.Children.Add(familySourceGrid);

        Grid reportArea = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0)
        };
        reportArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        reportArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TrueBimTheme.Spacing12) });
        reportArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        DockPanel familyResults = new();
        StackPanel familyNavigation = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };
        Button openFamilyButton = TrueBimUi.CreateSecondaryButton(
            "Открыть RFA",
            TrueBimIcon.Open,
            (_, _) => OpenSelectedExternalFamily(),
            minWidth: 120);
        openFamilyButton.ToolTip = "Открыть выбранный внешний RFA стандартным способом. Команда доступна только для файловой строки.";
        Button openFolderButton = TrueBimUi.CreateSecondaryButton(
            "Открыть папку",
            TrueBimIcon.Open,
            (_, _) => OpenSelectedFamilyFolder(),
            minWidth: 125);
        openFolderButton.ToolTip = "Открыть папку, содержащую выбранный RFA.";
        Button copyFamilyPathButton = TrueBimUi.CreateSecondaryButton(
            "Копировать путь",
            TrueBimIcon.Copy,
            (_, _) => CopySelectedFamilyPath(),
            minWidth: 135);
        copyFamilyPathButton.ToolTip = "Скопировать полный путь выбранного RFA.";
        openFolderButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        copyFamilyPathButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        familyNavigation.Children.Add(openFamilyButton);
        familyNavigation.Children.Add(openFolderButton);
        familyNavigation.Children.Add(copyFamilyPathButton);
        DockPanel.SetDock(familyNavigation, Dock.Top);
        familyResults.Children.Add(familyNavigation);
        familyResults.Children.Add(familyReportGrid);
        reportArea.Children.Add(familyResults);

        TabControl familyDetailsTabs = new()
        {
            Style = TrueBimStyles.CreateTabControlStyle()
        };
        familyDetailsTabs.Items.Add(CreateTab(
            "Общие сведения",
            new ScrollViewer
            {
                Content = familyDetailsText,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            }));
        familyDetailsTabs.Items.Add(CreateTab("Значения по типам", familyTypeValueGrid));
        familyDetailsTabs.Items.Add(CreateTab("Формулы", familyFormulaGrid));
        familyDetailsTabs.Items.Add(CreateTab("Размеры", familyDimensionGrid));
        familyDetailsTabs.Items.Add(CreateTab("Ассоциации", familyAssociationGrid));
        familyDetailsTabs.Items.Add(CreateTab("Вложенные", familyNestedGrid));
        familyDetailsTabs.Items.Add(CreateTab(
            "Ограничения",
            new ScrollViewer
            {
                Content = familyLimitationsText,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            }));
        Grid.SetColumn(familyDetailsTabs, 2);
        reportArea.Children.Add(familyDetailsTabs);
        Grid.SetRow(reportArea, 2);
        root.Children.Add(reportArea);
        return root;
    }

    private UIElement CreateReportTab()
    {
        DockPanel panel = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0)
        };
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };
        actions.Children.Add(exportReportButton);
        Button copyButton = TrueBimUi.CreateSecondaryButton(
            "Скопировать отчёт",
            TrueBimIcon.Copy,
            (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(reportText.Text))
                {
                    Clipboard.SetText(reportText.Text);
                }
            },
            minWidth: 150);
        copyButton.ToolTip = "Скопировать текст последнего сформированного отчёта в буфер обмена.";
        copyButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        actions.Children.Add(copyButton);
        DockPanel.SetDock(actions, Dock.Top);
        panel.Children.Add(actions);
        panel.Children.Add(reportText);
        return panel;
    }

    private UIElement CreateProgressStatus()
    {
        Grid status = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0)
        };
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        status.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        statusText.Text = "Готово.";
        status.Children.Add(statusText);
        progressCountText.Foreground = TrueBimBrushes.TextSecondary;
        progressCountText.Margin = new Thickness(TrueBimTheme.Spacing8, 0, TrueBimTheme.Spacing8, 0);
        Grid.SetColumn(progressCountText, 1);
        status.Children.Add(progressCountText);

        progressBar.Margin = new Thickness(0, TrueBimTheme.Spacing8, TrueBimTheme.Spacing8, 0);
        Grid.SetRow(progressBar, 1);
        status.Children.Add(progressBar);
        cancelButton.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        Grid.SetRow(cancelButton, 1);
        Grid.SetColumn(cancelButton, 1);
        status.Children.Add(cancelButton);
        return status;
    }

    private void RefreshCatalog()
    {
        SetBusy("Загрузка списка общих параметров...");
        if (!revitActions.Raise(() =>
            {
                try
                {
                    Document? document = GetActiveDocument();
                    if (document is null)
                    {
                        SetStatus("Активный документ Revit отсутствует.");
                        return;
                    }

                    allParameters = document.IsFamilyDocument
                        ? familyAnalysisService.CollectSharedParameters(document)
                        : catalogService.Collect(document);
                    activeDocumentIsFamily = document.IsFamilyDocument;
                    activeDocumentIdentity = catalogService.GetDocumentIdentity(document);
                    currentFamilyCategoryName = document.IsFamilyDocument
                        ? document.OwnerFamily?.FamilyCategory?.Name ?? "Без категории"
                        : "Не применимо";
                    analysisCache.Clear();
                    currentAnalysis = null;
                    reportAnalysis = null;
                    lastDeletion = null;
                    analysisIsStale = false;
                    ApplyParameterFilter();
                    PopulateFamilyParameterChoices(
                        allParameters.Select(parameter => (
                            Parameter: parameter,
                            Source: activeDocumentIdentity.Title)));

                    if (document.IsFamilyDocument)
                    {
                        mainTabs.SelectedItem = familiesTab;
                        familySourceInput.SelectedIndex = 3;
                        PopulateCurrentFamilySource();
                        SetStatus($"Текущее семейство: найдено общих параметров — {allParameters.Count}.");
                    }
                    else
                    {
                        mainTabs.SelectedItem = projectTab;
                        SetStatus($"Проект: найдено общих параметров — {allParameters.Count}.");
                    }

                    RenderCurrentAnalysis();
                }
                catch (Exception exception)
                {
                    logger.Error("Failed to load shared parameter catalog.", exception);
                    SetStatus($"Не удалось загрузить список параметров: {exception.Message}");
                }
                finally
                {
                    SetBusyComplete();
                }
            }))
        {
            SetBusyComplete();
        }
    }

    private void ApplyParameterFilter()
    {
        SharedParameterListFilter filter = ReadChoice(
            parameterFilterInput,
            SharedParameterListFilter.All);
        IReadOnlyList<SharedParameterDescriptor> filtered = searchService.Filter(
            allParameters,
            searchInput.Text,
            filter,
            analysisCache);
        SharedParameterListRow? previouslySelected = parameterGrid.SelectedItem as SharedParameterListRow;
        parameterRows.Clear();
        foreach (SharedParameterDescriptor parameter in filtered)
        {
            parameterRows.Add(new SharedParameterListRow(parameter));
        }

        parameterGrid.SelectedItem = previouslySelected is null
            ? parameterRows.FirstOrDefault()
            : parameterRows.FirstOrDefault(row => row.IdentityKey == previouslySelected.IdentityKey)
                ?? parameterRows.FirstOrDefault();
        if (searchService.RequiresAnalysis(filter)
            && allParameters.Count > 0
            && analysisCache.Count == 0)
        {
            statusText.Text = "Этот фильтр станет информативным после анализа параметров.";
        }
    }

    private void SelectedParameterChanged()
    {
        SharedParameterDescriptor? parameter = SelectedParameter;
        analyzeButton.IsEnabled = parameter is not null && !IsBusy;
        familyPresenceButton.IsEnabled = parameter is not null
            && currentAnalysis?.Parameter.IdentityKey == parameter?.IdentityKey
            && !IsBusy;
        deleteButton.IsEnabled = parameter is not null
            && !activeDocumentIsFamily
            && !IsBusy;

        if (parameter is null)
        {
            parameterCardText.Text = "Параметр не выбран.";
            UpdateActionToolTips();
            return;
        }

        parameterCardText.Text =
            $"{parameter.Name}\n"
            + $"GUID: {parameter.Guid:D}\n"
            + $"ElementId: {parameter.ParameterElementId}\n"
            + $"Тип данных: {parameter.DataTypeName}\n"
            + $"Группа: {parameter.ParameterGroupName}\n"
            + $"Привязка: {parameter.BindingDisplay}; категорий: {parameter.Categories.Count}\n"
            + $"Категории: {(parameter.Categories.Count == 0 ? "Нет" : string.Join(", ", parameter.Categories.Select(category => category.Name)))}\n"
            + $"VariesAcrossGroups: {parameter.VariesAcrossGroups}";
        familyGuidInput.Text = parameter.Guid.ToString("D");
        familyParameterInput.SelectedItem = familyParameterChoices.FirstOrDefault(choice =>
            choice.Guid == parameter.Guid);

        if (analysisCache.TryGetValue(parameter.IdentityKey, out SharedParameterProjectAnalysis? analysis))
        {
            currentAnalysis = analysis;
            reportAnalysis = analysis;
            analysisIsStale = false;
            RenderCurrentAnalysis();
            UpdateActionToolTips();
            return;
        }

        if (currentAnalysis?.Parameter.IdentityKey != parameter.IdentityKey)
        {
            currentAnalysis = null;
            analysisIsStale = false;
            RenderCurrentAnalysis();
        }

        UpdateActionToolTips();
    }

    private void AnalyzeSelectedParameter()
    {
        SharedParameterDescriptor? selected = SelectedParameter;
        if (selected is null)
        {
            return;
        }

        SetBusy($"Быстрый анализ «{selected.Name}»...");
        CancellationToken token = cancellationSource!.Token;
        if (!revitActions.Raise(() =>
            {
                try
                {
                    Document document = RequireProjectDocument();
                    SharedParameterDescriptor parameter = catalogService.Find(document, selected.Guid)
                        ?? throw new InvalidOperationException("Параметр больше не существует в активном проекте.");
                    currentAnalysis = projectAnalysisService.AnalyzeQuick(document, parameter, token);
                    reportAnalysis = currentAnalysis;
                    analysisCache[parameter.IdentityKey] = currentAnalysis;
                    analysisIsStale = false;
                    RenderCurrentAnalysis();
                    SetStatus($"Анализ завершён: «{parameter.Name}».");
                }
                catch (OperationCanceledException)
                {
                    SetStatus("Анализ отменён.");
                }
                catch (Exception exception)
                {
                    logger.Error("Shared parameter quick analysis failed.", exception);
                    SetStatus($"Ошибка анализа: {exception.Message}");
                }
                finally
                {
                    SetBusyComplete();
                }
            }))
        {
            SetBusyComplete();
        }
    }

    private void AnalyzeProjectFamilyPresence()
    {
        if (currentAnalysis is null)
        {
            AnalyzeSelectedParameter();
            return;
        }

        SharedParameterProjectAnalysis baseAnalysis = currentAnalysis;
        SetBusy($"Проверка семейств для «{baseAnalysis.Parameter.Name}»...");
        CancellationToken token = cancellationSource!.Token;
        List<long> familyIds = [];
        List<ProjectFamilyPresence> results = [];
        int nextIndex = 0;
        bool completed = false;

        void Complete(string message)
        {
            if (completed)
            {
                return;
            }

            completed = true;
            SetStatus(message);
            SetBusyComplete();
        }

        Action processNext = null!;
        processNext = () =>
        {
            if (token.IsCancellationRequested)
            {
                Complete($"Проверка семейств отменена после {results.Count} из {familyIds.Count}.");
                return;
            }

            if (nextIndex >= familyIds.Count)
            {
                if (!revitActions.Raise(() =>
                    {
                        try
                        {
                            Document document = RequireProjectDocument();
                            currentAnalysis = projectAnalysisService.WithFamilyPresence(
                                document,
                                baseAnalysis,
                                results);
                            reportAnalysis = currentAnalysis;
                            analysisCache[currentAnalysis.Parameter.IdentityKey] = currentAnalysis;
                            RenderCurrentAnalysis();
                            PopulateProjectFamilySources();
                            logger.Info(
                                $"Shared Parameter Inspector family presence completed. "
                                + $"Guid={baseAnalysis.Parameter.Guid:D}; Families={results.Count}; "
                                + $"Found={results.Count(family => family.ContainsParameter)}.");
                            Complete(
                                $"Проверено семейств: {results.Count}; параметр найден: "
                                + $"{results.Count(family => family.ContainsParameter)}.");
                        }
                        catch (Exception exception)
                        {
                            logger.Error("Shared parameter family presence finalization failed.", exception);
                            Complete($"Ошибка завершения проверки семейств: {exception.Message}");
                        }
                    }))
                {
                    Complete("Revit не принял завершение проверки семейств.");
                }

                return;
            }

            int familyIndex = nextIndex;
            long familyId = familyIds[familyIndex];
            if (!revitActions.Raise(() =>
                {
                    try
                    {
                        Document document = RequireProjectDocument();
                        if (document.GetElement(RevitElementIds.Create(familyId)) is not Family family)
                        {
                            results.Add(new ProjectFamilyPresence(
                                familyId,
                                familyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                "Неизвестно",
                                FamilyPresenceStatus.Failed,
                                false,
                                "Семейство больше не существует в активном проекте."));
                        }
                        else
                        {
                            results.Add(familyAnalysisService.CheckProjectFamilyPresence(
                                document,
                                family,
                                baseAnalysis.Parameter.Guid));
                        }

                        nextIndex++;
                        UpdateProgress(nextIndex, familyIds.Count, results[results.Count - 1].FamilyName);
                        Dispatcher.BeginInvoke(processNext);
                    }
                    catch (Exception exception)
                    {
                        logger.Error("Shared parameter family presence analysis failed.", exception);
                        Complete($"Ошибка проверки семейств: {exception.Message}");
                    }
                }))
            {
                Complete("Revit не принял следующий пакет проверки семейств.");
            }
        };

        if (!revitActions.Raise(() =>
            {
                try
                {
                    Document document = RequireProjectDocument();
                    familyIds = new FilteredElementCollector(document)
                        .OfClass(typeof(Family))
                        .Cast<Family>()
                        .OrderBy(family => family.Name, StringComparer.CurrentCultureIgnoreCase)
                        .Select(family => RevitElementIds.GetValue(family.Id))
                        .ToList();
                    UpdateProgress(0, familyIds.Count, "Подготовка списка семейств");
                    Dispatcher.BeginInvoke(processNext);
                }
                catch (Exception exception)
                {
                    logger.Error("Shared parameter family presence analysis failed.", exception);
                    Complete($"Ошибка проверки семейств: {exception.Message}");
                }
            }))
        {
            Complete("Revit не принял запрос проверки семейств.");
        }
    }

    private void PrepareDeletion()
    {
        SharedParameterDescriptor? selected = SelectedParameter;
        if (selected is null)
        {
            return;
        }

        SetBusy($"Подготовка удаления «{selected.Name}»: свежий анализ, семейства и dry run...");
        CancellationToken token = cancellationSource!.Token;
        if (!revitActions.Raise(() =>
            {
                try
                {
                    Document document = RequireProjectDocument();
                    SharedParameterDescriptor freshParameter = catalogService.Find(document, selected.Guid)
                        ?? throw new InvalidOperationException("Параметр больше не существует.");
                    SharedParameterProjectAnalysis freshAnalysis = projectAnalysisService.AnalyzeQuick(
                        document,
                        freshParameter,
                        token);
                    IReadOnlyList<ProjectFamilyPresence> presence = familyAnalysisService.CollectProjectFamilyPresence(
                        document,
                        freshParameter.Guid,
                        token);
                    freshAnalysis = projectAnalysisService.WithFamilyPresence(
                        document,
                        freshAnalysis,
                        presence);

                    List<FamilyParameterUsageReport> freshFamilyReports = [];
                    foreach (ProjectFamilyPresence family in presence.Where(family => family.ContainsParameter))
                    {
                        token.ThrowIfCancellationRequested();
                        freshFamilyReports.Add(familyAnalysisService.AnalyzeProjectFamily(
                            document,
                            family.FamilyId,
                            freshParameter.Guid));
                    }

                    SharedParameterDryRunResult dryRun = deletionWorkflow.DryRun(document, freshAnalysis);
                    SharedParameterDeletionPlan plan = deletionPlanBuilder.Build(
                        freshAnalysis,
                        dryRun,
                        freshFamilyReports);
                    currentAnalysis = freshAnalysis;
                    reportAnalysis = freshAnalysis;
                    analysisCache[freshParameter.IdentityKey] = freshAnalysis;
                    familyReports.Clear();
                    familyReports.AddRange(freshFamilyReports);
                    analysisIsStale = false;
                    RenderCurrentAnalysis();
                    RenderFamilyReports();
                    SetBusyComplete();
                    SetStatus(
                        plan.Blockers.Count == 0
                            ? "План удаления готов. Требуется явное подтверждение."
                            : $"Удаление заблокировано. Blockers: {plan.Blockers.Count}.");
                    Dispatcher.BeginInvoke(new Action(() => ShowDeletionConfirmation(plan)));
                }
                catch (OperationCanceledException)
                {
                    SetBusyComplete();
                    SetStatus("Подготовка удаления отменена.");
                }
                catch (Exception exception)
                {
                    SetBusyComplete();
                    logger.Error("Shared parameter deletion preparation failed.", exception);
                    SetStatus($"Ошибка подготовки удаления: {exception.Message}");
                }
            }))
        {
            SetBusyComplete();
        }
    }

    private void ShowDeletionConfirmation(SharedParameterDeletionPlan plan)
    {
        SharedParameterProjectAnalysis analysis = currentAnalysis
            ?? throw new InvalidOperationException("Актуальный анализ удаления отсутствует.");
        SharedParameterDeletionConfirmWindow confirmation = new(plan, analysis)
        {
            Owner = this
        };
        if (confirmation.ShowDialog() != true || currentAnalysis is null)
        {
            return;
        }

        ExecuteDeletion(plan, confirmation.SelectedMode);
    }

    private void ExecuteDeletion(SharedParameterDeletionPlan plan, DeletionMode mode)
    {
        SetBusy($"Удаление «{plan.Parameter.Name}»...");
        CancellationToken token = cancellationSource!.Token;
        if (!revitActions.Raise(() =>
            {
                try
                {
                    Document document = RequireProjectDocument();
                    lastDeletion = deletionWorkflow.Execute(
                        document,
                        currentAnalysis!,
                        plan,
                        familyReports,
                        mode,
                        token);
                    RenderReport();
                    SetStatus(lastDeletion.Summary);
                    if (lastDeletion.Status == DeletionStatus.Success)
                    {
                        allParameters = catalogService.Collect(document);
                        analysisCache.Clear();
                        currentAnalysis = null;
                        parameterRows.Clear();
                        ApplyParameterFilter();
                        RenderCurrentAnalysis();
                    }
                }
                catch (Exception exception)
                {
                    logger.Error("Shared parameter deletion execution failed.", exception);
                    SetStatus($"Ошибка удаления: {exception.Message}");
                }
                finally
                {
                    SetBusyComplete();
                }
            }))
        {
            SetBusyComplete();
        }
    }

    private void FamilySourceChanged()
    {
        FamilySourceKind source = ReadChoice(familySourceInput, FamilySourceKind.ActiveProject);
        scanFolderButton.IsEnabled = source == FamilySourceKind.Folder && !IsBusy;
        selectFilesButton.IsEnabled = source == FamilySourceKind.SelectedFiles && !IsBusy;
        includeSubfoldersInput.IsEnabled = source == FamilySourceKind.Folder;
        folderPathInput.Visibility = source == FamilySourceKind.Folder
            ? Visibility.Visible
            : Visibility.Collapsed;
        familySourceRows.Clear();
        familyParameterChoices.Clear();

        if (source == FamilySourceKind.ActiveProject)
        {
            PopulateProjectFamilySources();
            PopulateFamilyParameterChoices(
                allParameters.Select(parameter => (
                    Parameter: parameter,
                    Source: activeDocumentIdentity?.Title ?? "Активный проект")));
        }
        else if (source == FamilySourceKind.CurrentFamily)
        {
            PopulateCurrentFamilySource();
            if (activeDocumentIsFamily)
            {
                PopulateFamilyParameterChoices(
                    allParameters.Select(parameter => (
                        Parameter: parameter,
                        Source: activeDocumentIdentity?.Title ?? "Текущее семейство")));
            }
        }

        UpdateFamilyActionsState();
    }

    private void BrowseFamilyFolder()
    {
        using Forms.FolderBrowserDialog dialog = new()
        {
            Description = "Выберите папку с RFA. Файлы сначала будут перечислены без открытия.",
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        folderPathInput.Text = dialog.SelectedPath;
        try
        {
            IReadOnlyList<string> paths = familyFileScanner.Scan(
                dialog.SelectedPath,
                includeSubfoldersInput.IsChecked == true);
            familySourceRows.Clear();
            familyParameterChoices.Clear();
            foreach (string path in paths)
            {
                familySourceRows.Add(FamilySourceRow.FromPath(path, FamilySourceKind.Folder));
            }

            SetStatus($"Найдено RFA-файлов: {paths.Count}. Выберите конкретные файлы для анализа.");
        }
        catch (Exception exception)
        {
            SetStatus($"Не удалось просканировать папку: {exception.Message}");
        }

        UpdateFamilyActionsState();
    }

    private void BrowseFamilyFiles()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Выберите семейства Revit",
            Filter = "Revit families (*.rfa)|*.rfa",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        familySourceRows.Clear();
        familyParameterChoices.Clear();
        foreach (string path in dialog.FileNames
                     .Where(familyFileScanner.IsSupportedFamilyPath)
                     .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase))
        {
            familySourceRows.Add(FamilySourceRow.FromPath(path, FamilySourceKind.SelectedFiles));
        }

        UpdateFamilyActionsState();
    }

    private void ScanFamilyParameterCatalogs()
    {
        FamilySourceKind sourceKind = ReadChoice(
            familySourceInput,
            FamilySourceKind.ActiveProject);
        if (sourceKind is not (FamilySourceKind.Folder or FamilySourceKind.SelectedFiles))
        {
            PopulateFamilyParameterChoices(
                allParameters.Select(parameter => (
                    Parameter: parameter,
                    Source: activeDocumentIdentity?.Title ?? "Активный документ")));
            SetStatus($"Каталог активного документа содержит параметров: {familyParameterChoices.Count}.");
            return;
        }

        List<FamilySourceRow> selectedRows = familySourceGrid.SelectedItems
            .Cast<FamilySourceRow>()
            .Where(row => !string.IsNullOrWhiteSpace(row.Path))
            .ToList();
        if (selectedRows.Count == 0)
        {
            selectedRows = familySourceRows
                .Where(row => !string.IsNullOrWhiteSpace(row.Path))
                .ToList();
        }

        if (selectedRows.Count == 0)
        {
            SetStatus("Сначала выберите RFA-файлы или папку.");
            return;
        }

        SetBusy($"Предварительный просмотр GUID: {selectedRows.Count} RFA...");
        CancellationToken token = cancellationSource!.Token;
        List<(SharedParameterDescriptor Parameter, string Source)> scannedParameters = [];
        int errorCount = 0;
        int nextIndex = 0;
        bool completed = false;

        void Complete(string message)
        {
            if (completed)
            {
                return;
            }

            completed = true;
            PopulateFamilyParameterChoices(scannedParameters);
            SetStatus(message);
            SetBusyComplete();
        }

        Action processNext = null!;
        processNext = () =>
        {
            if (token.IsCancellationRequested)
            {
                Complete(
                    $"Сканирование GUID отменено после {nextIndex} из {selectedRows.Count}; "
                    + $"объединённых параметров: "
                    + $"{scannedParameters.Select(item => item.Parameter.Guid).Distinct().Count()}.");
                return;
            }

            if (nextIndex >= selectedRows.Count)
            {
                Complete(
                    $"GUID-каталог готов: файлов {selectedRows.Count}; "
                    + $"уникальных параметров {scannedParameters.Select(item => item.Parameter.Guid).Distinct().Count()}; "
                    + $"ошибок {errorCount}.");
                return;
            }

            FamilySourceRow row = selectedRows[nextIndex];
            UpdateProgress(nextIndex, selectedRows.Count, row.Name);
            if (!revitActions.Raise(() =>
                {
                    try
                    {
                        FamilySharedParameterCatalogScan result =
                            familyAnalysisService.ScanExternalFamilyCatalog(
                                application.Application,
                                row.Path,
                                row.SourceKind);
                        scannedParameters.AddRange(result.Parameters.Select(parameter => (
                            Parameter: parameter,
                            Source: row.Path)));
                        errorCount += result.Errors.Count;
                        int collectionIndex = familySourceRows.IndexOf(row);
                        if (collectionIndex >= 0)
                        {
                            familySourceRows[collectionIndex] = row with
                            {
                                Name = result.Family.Name,
                                CategoryName = result.Family.CategoryName,
                                Status = result.Errors.Count == 0
                                    ? $"GUID: {result.Parameters.Count}"
                                    : $"Ошибка: {result.Errors[0].Message}"
                            };
                        }

                        nextIndex++;
                        UpdateProgress(nextIndex, selectedRows.Count, row.Name);
                        Dispatcher.BeginInvoke(processNext);
                    }
                    catch (Exception exception)
                    {
                        logger.Error("Shared parameter family catalog scan failed.", exception);
                        errorCount++;
                        nextIndex++;
                        Dispatcher.BeginInvoke(processNext);
                    }
                }))
            {
                Complete("Revit не принял следующий пакет предварительного просмотра GUID.");
            }
        };

        Dispatcher.BeginInvoke(processNext);
    }

    private void AnalyzeSelectedFamilies()
    {
        if (!Guid.TryParse(familyGuidInput.Text, out Guid targetGuid))
        {
            SetStatus("Введите корректный GUID общего параметра.");
            return;
        }

        List<FamilySourceRow> selectedRows = familySourceGrid.SelectedItems
            .Cast<FamilySourceRow>()
            .ToList();
        if (selectedRows.Count == 0)
        {
            selectedRows = familySourceRows.ToList();
        }

        if (selectedRows.Count == 0)
        {
            SetStatus("Выберите хотя бы одно семейство.");
            return;
        }

        SetBusy($"Глубокий анализ семейств: {selectedRows.Count}...");
        CancellationToken token = cancellationSource!.Token;
        familyReports.Clear();
        int nextIndex = 0;
        bool completed = false;

        void Complete(string message)
        {
            if (completed)
            {
                return;
            }

            completed = true;
            SetStatus(message);
            SetBusyComplete();
        }

        Action processNext = null!;
        processNext = () =>
        {
            if (token.IsCancellationRequested)
            {
                RenderFamilyReports();
                RenderReport();
                Complete($"Глубокий анализ отменён после {familyReports.Count} из {selectedRows.Count}.");
                return;
            }

            if (nextIndex >= selectedRows.Count)
            {
                UpdateProgress(selectedRows.Count, selectedRows.Count, "Готово");
                RenderFamilyReports();
                RenderReport();
                Complete($"Глубокий анализ завершён: {familyReports.Count} семейств.");
                return;
            }

            FamilySourceRow row = selectedRows[nextIndex];
            UpdateProgress(nextIndex, selectedRows.Count, row.Name);
            if (!revitActions.Raise(() =>
                {
                    try
                    {
                        Document? document = GetActiveDocument();
                        if (document is null)
                        {
                            throw new InvalidOperationException("Активный документ отсутствует.");
                        }

                        ValidateActiveDocumentIdentity(document);
                        FamilyParameterUsageReport report = row.SourceKind switch
                        {
                            FamilySourceKind.ActiveProject when row.ProjectFamilyId.HasValue && !document.IsFamilyDocument =>
                                familyAnalysisService.AnalyzeProjectFamily(
                                    document,
                                    row.ProjectFamilyId.Value,
                                    targetGuid),
                            FamilySourceKind.CurrentFamily when document.IsFamilyDocument =>
                                familyAnalysisService.AnalyzeCurrentFamily(document, targetGuid),
                            _ when !string.IsNullOrWhiteSpace(row.Path) =>
                                familyAnalysisService.AnalyzeExternalFamily(
                                    application.Application,
                                    row.Path,
                                    targetGuid),
                            _ => throw new InvalidOperationException(
                                $"Источник семейства «{row.Name}» не соответствует активному документу.")
                        };
                        report = report with
                        {
                            Family = report.Family with { SourceKind = row.SourceKind }
                        };
                        familyReports.Add(report);
                        nextIndex++;
                        UpdateProgress(nextIndex, selectedRows.Count, row.Name);
                        Dispatcher.BeginInvoke(processNext);
                    }
                    catch (Exception exception)
                    {
                        logger.Error("Shared parameter deep family analysis failed.", exception);
                        Complete($"Ошибка глубокого анализа: {exception.Message}");
                    }
                }))
            {
                Complete("Revit не принял следующий пакет глубокого анализа.");
            }
        };

        Dispatcher.BeginInvoke(processNext);
    }

    private void PopulateProjectFamilySources()
    {
        familySourceRows.Clear();
        if (currentAnalysis is null)
        {
            return;
        }

        foreach (ProjectFamilyPresence family in currentAnalysis.Families)
        {
            familySourceRows.Add(new FamilySourceRow(
                FamilySourceKind.ActiveProject,
                family.FamilyName,
                family.CategoryName,
                string.Empty,
                family.FamilyId,
                family.ContainsParameter ? "Параметр найден" : family.Status.ToString()));
        }

        UpdateFamilyActionsState();
    }

    private void PopulateCurrentFamilySource()
    {
        familySourceRows.Clear();
        if (activeDocumentIdentity is null)
        {
            return;
        }

        if (!activeDocumentIsFamily)
        {
            familySourceRows.Add(new FamilySourceRow(
                FamilySourceKind.CurrentFamily,
                activeDocumentIdentity.Title,
                "Не применимо",
                activeDocumentIdentity.Path,
                null,
                "Текущий документ не является семейством."));
            return;
        }

        familySourceRows.Add(new FamilySourceRow(
            FamilySourceKind.CurrentFamily,
            activeDocumentIdentity.Title,
            currentFamilyCategoryName,
            activeDocumentIdentity.Path,
            null,
            "Доступно"));
        UpdateFamilyActionsState();
    }

    private void MoveSelectedFamilyToDeepAnalysis()
    {
        if (familyPresenceGrid.SelectedItem is not ProjectFamilyPresence selected)
        {
            return;
        }

        mainTabs.SelectedItem = familiesTab;
        familySourceInput.SelectedIndex = 0;
        PopulateProjectFamilySources();
        familySourceGrid.SelectedItem = familySourceRows.FirstOrDefault(row =>
            row.ProjectFamilyId == selected.FamilyId);
        familySourceGrid.ScrollIntoView(familySourceGrid.SelectedItem);
    }

    private void RenderCurrentAnalysis()
    {
        if (currentAnalysis is null)
        {
            analysisSummaryText.Text = "Выберите общий параметр и запустите быстрый анализ.";
            analysisLimitText.Text = "Ограничения и blockers появятся после анализа.";
            elementAggregateGrid.ItemsSource = null;
            elementGrid.ItemsSource = null;
            scheduleGrid.ItemsSource = null;
            viewFilterGrid.ItemsSource = null;
            globalParameterGrid.ItemsSource = null;
            familyPresenceGrid.ItemsSource = null;
            RenderReport();
            familyPresenceButton.IsEnabled = false;
            return;
        }

        SharedParameterProjectAnalysis analysis = currentAnalysis;
        string stale = analysisIsStale ? "\nРезультат может быть устаревшим." : string.Empty;
        analysisSummaryText.Text =
            $"Параметр: {analysis.Parameter.Name}\n"
            + $"Элементы с параметром: {analysis.Elements.Count(element => element.HasParameter)}\n"
            + $"Заполнено: {analysis.FilledValueCount}; пусто: {analysis.EmptyValueCount}\n"
            + $"Поля спецификаций: {analysis.ScheduleFields.Count}; фильтры видов: {analysis.ViewFilters.Count}\n"
            + $"Глобальные ассоциации: {analysis.GlobalParameterAssociations.Count}\n"
            + $"Семейства с параметром: {analysis.FamilyCountWithParameter}\n"
            + $"Blockers: {analysis.Blockers.Count}; предупреждения: {analysis.Warnings.Count}; ошибки: {analysis.Errors.Count}"
            + stale;
        analysisLimitText.Text = BuildLimitText(analysis);
        elementAggregateGrid.ItemsSource = analysis.ElementAggregates;
        elementGrid.ItemsSource = analysis.Elements.Take(VisibleElementLimit).ToList();
        scheduleGrid.ItemsSource = analysis.ScheduleFields;
        viewFilterGrid.ItemsSource = analysis.ViewFilters;
        globalParameterGrid.ItemsSource = analysis.GlobalParameterAssociations;
        familyPresenceGrid.ItemsSource = analysis.Families;
        familyPresenceButton.IsEnabled = !IsBusy
            && SelectedParameter?.IdentityKey == analysis.Parameter.IdentityKey;
        RenderReport();
        deleteButton.IsEnabled = !IsBusy && !activeDocumentIsFamily;
    }

    private void RenderFamilyReports()
    {
        familyReportRows.Clear();
        foreach (FamilyParameterUsageReport report in familyReports)
        {
            familyReportRows.Add(new FamilyReportRow(report));
        }

        familyReportGrid.SelectedItem = familyReportRows.FirstOrDefault();
        RenderSelectedFamilyReport();
    }

    private void RenderSelectedFamilyReport()
    {
        if (familyReportGrid.SelectedItem is not FamilyReportRow row)
        {
            familyDetailsText.Text = "Выберите результат глубокого анализа.";
            familyTypeValueGrid.ItemsSource = null;
            familyFormulaGrid.ItemsSource = null;
            familyDimensionGrid.ItemsSource = null;
            familyAssociationGrid.ItemsSource = null;
            familyNestedGrid.ItemsSource = null;
            familyLimitationsText.Text = string.Empty;
            return;
        }

        FamilyParameterUsageReport report = row.Report;
        familyDetailsText.Text =
            $"Семейство: {report.Family.Name}\n"
            + $"Путь: {report.Family.Path}\n"
            + $"GUID: {report.TargetGuid:D}\n"
            + $"Параметр найден: {report.ParameterFound}\n"
            + $"Instance/type: {(report.Parameter?.IsInstance == true ? "Экземпляр" : "Тип")}\n"
            + $"Формула: {report.Parameter?.Formula}\n"
            + $"Типы: {report.TypeValues.Count}; заполнено: {report.TypeValues.Count(value => value.HasValue)}\n"
            + $"Зависимые формулы: {report.Formulas.Count}\n"
            + $"Размеры: {report.Dimensions.Count}\n"
            + $"Ассоциации: {report.Associations.Count}; вложенные: {report.NestedFamilies.Count}\n"
            + $"Аннотационные ограничения: {report.Annotations.Count}\n"
            + $"Blockers: {report.DeletionBlockers.Count}; ошибки: {report.Errors.Count}";
        familyTypeValueGrid.ItemsSource = report.TypeValues;
        familyFormulaGrid.ItemsSource = report.Formulas;
        familyDimensionGrid.ItemsSource = report.Dimensions;
        familyAssociationGrid.ItemsSource = report.Associations;
        familyNestedGrid.ItemsSource = report.NestedFamilies;
        familyLimitationsText.Text = string.Join(
            Environment.NewLine,
            report.Annotations
                .Select(annotation => $"ANNOTATION [{annotation.Confidence}] {annotation.Message}")
                .Concat(report.DeletionBlockers.Select(blocker =>
                    $"BLOCKER [{blocker.Code}] {blocker.Message}"))
                .Concat(report.Errors.Select(error =>
                    $"ERROR [{error.Phase}] {error.Message}")));
    }

    private void OpenSelectedExternalFamily()
    {
        if (familyReportGrid.SelectedItem is not FamilyReportRow row
            || string.IsNullOrWhiteSpace(row.Report.Family.Path)
            || !File.Exists(row.Report.Family.Path))
        {
            SetStatus("Для выбранного результата нет доступного внешнего RFA.");
            return;
        }

        string path = row.Report.Family.Path;
        revitActions.Raise(() =>
        {
            application.OpenAndActivateDocument(path);
            Dispatcher.BeginInvoke(new Action(RefreshCatalog));
        });
    }

    private void OpenSelectedFamilyFolder()
    {
        if (familyReportGrid.SelectedItem is not FamilyReportRow row
            || string.IsNullOrWhiteSpace(row.Report.Family.Path))
        {
            SetStatus("Для выбранного результата путь не задан.");
            return;
        }

        string? folder = Path.GetDirectoryName(row.Report.Family.Path);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            SetStatus("Папка семейства больше не существует.");
            return;
        }

        Process.Start(new ProcessStartInfo(folder)
        {
            UseShellExecute = true
        });
    }

    private void CopySelectedFamilyPath()
    {
        if (familyReportGrid.SelectedItem is FamilyReportRow row
            && !string.IsNullOrWhiteSpace(row.Report.Family.Path))
        {
            Clipboard.SetText(row.Report.Family.Path);
        }
    }

    private void RenderReport()
    {
        SharedParameterProjectAnalysis? analysis = currentAnalysis ?? reportAnalysis;
        if (analysis is null)
        {
            reportText.Text = lastDeletion?.Summary ?? "Отчёт появится после анализа.";
            exportReportButton.IsEnabled = false;
            return;
        }

        reportText.Text = reportExportService.BuildText(analysis, lastDeletion, familyReports);
        exportReportButton.IsEnabled = !IsBusy;
    }

    private void ExportReport()
    {
        SharedParameterProjectAnalysis? analysis = currentAnalysis ?? reportAnalysis;
        if (analysis is null)
        {
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "Сохранить отчёт анализа общих параметров",
            Filter = "JSON report (*.json)|*.json",
            FileName = $"shared-parameter-{SanitizeFileName(analysis.Parameter.Name)}-{DateTime.Now:yyyyMMdd-HHmm}.json",
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            SharedParameterReportPackage package = reportExportService.Save(
                analysis,
                lastDeletion,
                dialog.FileName,
                familyReports);
            SetStatus($"Отчёт сохранён: {package.HtmlPath}");
            MessageBoxResult open = MessageBox.Show(
                this,
                "Отчёт сохранён в JSON, CSV, HTML и TXT.\n\nОткрыть HTML-отчёт?",
                "Общие параметры",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information,
                MessageBoxResult.Yes);
            if (open == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(package.HtmlPath)
                {
                    UseShellExecute = true
                });
            }
        }
        catch (Exception exception)
        {
            logger.Error("Failed to export Shared Parameter Inspector report.", exception);
            SetStatus($"Не удалось сохранить отчёт: {exception.Message}");
        }
    }

    private void SelectElements()
    {
        List<ElementParameterUsage> selected = elementGrid.SelectedItems
            .Cast<ElementParameterUsage>()
            .ToList();
        if (selected.Count == 0)
        {
            return;
        }

        revitActions.Raise(() =>
        {
            UIDocument uiDocument = application.ActiveUIDocument
                ?? throw new InvalidOperationException("Активный документ отсутствует.");
            ValidateActiveDocumentIdentity(uiDocument.Document);
            ICollection<ElementId> ids = selected
                .Select(item => RevitElementIds.Create(item.ElementId))
                .Where(id => uiDocument.Document.GetElement(id) is not null)
                .ToList();
            uiDocument.Selection.SetElementIds(ids);
            if (ids.Count > 0)
            {
                uiDocument.ShowElements(ids);
            }
        });
    }

    private void CopySelectedElementIds()
    {
        IReadOnlyList<long> ids = elementGrid.SelectedItems
            .Cast<ElementParameterUsage>()
            .Select(item => item.ElementId)
            .Distinct()
            .ToList();
        if (ids.Count > 0)
        {
            Clipboard.SetText(string.Join(Environment.NewLine, ids));
        }
    }

    private void CopySelectedUniqueIds()
    {
        IReadOnlyList<string> ids = elementGrid.SelectedItems
            .Cast<ElementParameterUsage>()
            .Select(item => item.UniqueId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count > 0)
        {
            Clipboard.SetText(string.Join(Environment.NewLine, ids));
        }
    }

    private void CopySelectedParameterValue(
        Func<SharedParameterDescriptor, string> valueFactory)
    {
        SharedParameterDescriptor? parameter = SelectedParameter;
        if (parameter is not null)
        {
            Clipboard.SetText(valueFactory(parameter));
        }
    }

    private void OpenSelectedSchedule()
    {
        if (scheduleGrid.SelectedItem is not ScheduleFieldUsage schedule)
        {
            return;
        }

        revitActions.Raise(() =>
        {
            UIDocument uiDocument = application.ActiveUIDocument
                ?? throw new InvalidOperationException("Активный документ отсутствует.");
            ValidateActiveDocumentIdentity(uiDocument.Document);
            if (uiDocument.Document.GetElement(RevitElementIds.Create(schedule.ScheduleId)) is not ViewSchedule view)
            {
                throw new InvalidOperationException("Спецификация больше не существует.");
            }

            uiDocument.ActiveView = view;
        });
    }

    private void OpenFirstViewForSelectedFilter()
    {
        if (viewFilterGrid.SelectedItem is not ViewFilterUsage filter
            || filter.AppliedViews.FirstOrDefault(view => !view.IsTemplate) is not AppliedViewFilterUsage applicationRow)
        {
            SetStatus("Для выбранного фильтра не найден обычный вид, который можно открыть.");
            return;
        }

        revitActions.Raise(() =>
        {
            UIDocument uiDocument = application.ActiveUIDocument
                ?? throw new InvalidOperationException("Активный документ отсутствует.");
            ValidateActiveDocumentIdentity(uiDocument.Document);
            if (uiDocument.Document.GetElement(RevitElementIds.Create(applicationRow.ViewId)) is not View view)
            {
                throw new InvalidOperationException("Вид больше не существует.");
            }

            uiDocument.ActiveView = view;
        });
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs args)
    {
        if (currentAnalysis is null)
        {
            return;
        }

        Document changedDocument = args.GetDocument();
        if (!string.Equals(changedDocument.Title, currentAnalysis.Document.Title, StringComparison.Ordinal))
        {
            return;
        }

        analysisIsStale = true;
        Dispatcher.BeginInvoke(new Action(RenderCurrentAnalysis));
    }

    private void SetBusy(string message)
    {
        cancellationSource?.Dispose();
        cancellationSource = new CancellationTokenSource();
        progressBar.IsIndeterminate = true;
        progressBar.Value = 0;
        progressCountText.Text = string.Empty;
        statusText.Text = message;
        cancelButton.IsEnabled = true;
        UpdateActionStatesForBusy(isBusy: true);
    }

    private void SetBusyComplete()
    {
        progressBar.IsIndeterminate = false;
        progressBar.Value = progressBar.Maximum;
        cancelButton.IsEnabled = false;
        cancellationSource?.Dispose();
        cancellationSource = null;
        UpdateActionStatesForBusy(isBusy: false);
    }

    private void UpdateProgress(int completed, int total, string current)
    {
        progressBar.IsIndeterminate = false;
        progressBar.Maximum = Math.Max(1, total);
        progressBar.Value = Math.Min(completed, total);
        progressCountText.Text = $"{completed} / {total}";
        statusText.Text = current;
    }

    private void UpdateActionStatesForBusy(bool isBusy)
    {
        analyzeButton.IsEnabled = !isBusy && SelectedParameter is not null;
        familyPresenceButton.IsEnabled = !isBusy
            && SelectedParameter is not null
            && currentAnalysis?.Parameter.IdentityKey == SelectedParameter.IdentityKey;
        deleteButton.IsEnabled = !isBusy
            && SelectedParameter is not null
            && !activeDocumentIsFamily;
        scanFolderButton.IsEnabled = !isBusy
            && ReadChoice(familySourceInput, FamilySourceKind.ActiveProject) == FamilySourceKind.Folder;
        selectFilesButton.IsEnabled = !isBusy
            && ReadChoice(familySourceInput, FamilySourceKind.ActiveProject) == FamilySourceKind.SelectedFiles;
        scanFamilyParametersButton.IsEnabled = !isBusy
            && (ReadChoice(familySourceInput, FamilySourceKind.ActiveProject)
                is FamilySourceKind.Folder or FamilySourceKind.SelectedFiles)
            && familySourceRows.Count > 0;
        exportReportButton.IsEnabled = !isBusy && (currentAnalysis is not null || reportAnalysis is not null);
        UpdateFamilyActionsState();
    }

    private void UpdateFamilyActionsState()
    {
        scanFamilyParametersButton.IsEnabled = !IsBusy
            && (ReadChoice(familySourceInput, FamilySourceKind.ActiveProject)
                is FamilySourceKind.Folder or FamilySourceKind.SelectedFiles)
            && familySourceRows.Count > 0;
        analyzeFamiliesButton.IsEnabled = !IsBusy
            && Guid.TryParse(familyGuidInput.Text, out _)
            && familySourceRows.Count > 0;
        UpdateActionToolTips();
    }

    private void PopulateFamilyParameterChoices(
        IEnumerable<(SharedParameterDescriptor Parameter, string Source)> entries)
    {
        Guid? preferredGuid = Guid.TryParse(familyGuidInput.Text, out Guid parsedGuid)
            ? parsedGuid
            : null;
        IReadOnlyList<FamilyParameterChoice> choices = entries
            .GroupBy(entry => entry.Parameter.Guid)
            .Select(group =>
            {
                IReadOnlyList<string> names = group
                    .Select(entry => entry.Parameter.Name)
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                IReadOnlyList<string> dataTypes = group
                    .Select(entry => entry.Parameter.DataTypeName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                int sourceCount = group
                    .Select(entry => entry.Source)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                return new FamilyParameterChoice(
                    group.Key,
                    string.Join(" / ", names),
                    string.Join(" / ", dataTypes),
                    sourceCount,
                    names.Count > 1);
            })
            .OrderBy(choice => choice.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(choice => choice.Guid)
            .ToList();

        familyParameterChoices.Clear();
        foreach (FamilyParameterChoice choice in choices)
        {
            familyParameterChoices.Add(choice);
        }

        familyParameterInput.SelectedItem = preferredGuid.HasValue
            ? familyParameterChoices.FirstOrDefault(choice => choice.Guid == preferredGuid.Value)
                ?? familyParameterChoices.FirstOrDefault()
            : familyParameterChoices.FirstOrDefault();
    }

    private bool IsBusy => cancellationSource is not null;

    private void SetStatus(string message)
    {
        statusText.Text = message;
    }

    private Document? GetActiveDocument()
    {
        return application.ActiveUIDocument?.Document;
    }

    private Document RequireProjectDocument()
    {
        Document document = GetActiveDocument()
            ?? throw new InvalidOperationException("Активный документ Revit отсутствует.");
        ValidateActiveDocumentIdentity(document);
        if (document.IsFamilyDocument)
        {
            throw new InvalidOperationException("Для проектного анализа откройте RVT-проект.");
        }

        return document;
    }

    private void ValidateActiveDocumentIdentity(Document document)
    {
        if (activeDocumentIdentity is null)
        {
            return;
        }

        string currentPath = document.PathName ?? string.Empty;
        if (!string.Equals(document.Title, activeDocumentIdentity.Title, StringComparison.Ordinal)
            || !string.Equals(currentPath, activeDocumentIdentity.Path, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Активный документ изменился. Обновите каталог и повторите операцию.");
        }
    }

    private SharedParameterDescriptor? SelectedParameter =>
        (parameterGrid.SelectedItem as SharedParameterListRow)?.Descriptor;

    private static string BuildLimitText(SharedParameterProjectAnalysis analysis)
    {
        List<string> lines =
        [
            "Расчётные и объединённые поля спецификаций блокируют безопасное удаление, если публичный API не позволяет доказать отсутствие зависимости.",
            "Точное использование общего параметра внутри label семейства марки/аннотации публичным API Revit 2019–2026 не подтверждается."
        ];
        lines.AddRange(analysis.Blockers.Select(blocker => $"BLOCKER [{blocker.Code}] {blocker.Message}"));
        lines.AddRange(analysis.Warnings.Select(warning => $"WARNING [{warning.Code}] {warning.Message}"));
        lines.AddRange(analysis.Errors.Select(error => $"ERROR [{error.Phase}] {error.Message}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = new(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "parameter" : sanitized;
    }

    private static SharedParameterListFilter ReadChoice(
        ComboBox input,
        SharedParameterListFilter fallback)
    {
        return input.SelectedItem is Choice<SharedParameterListFilter> choice
            ? choice.Value
            : fallback;
    }

    private static FamilySourceKind ReadChoice(
        ComboBox input,
        FamilySourceKind fallback)
    {
        return input.SelectedItem is Choice<FamilySourceKind> choice
            ? choice.Value
            : fallback;
    }

    private static DataGrid CreateDataGrid()
    {
        return new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            Style = TrueBimStyles.CreateDataGridStyle(),
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
        };
    }

    private static void AddTextColumn(
        DataGrid grid,
        string header,
        string property,
        double starWidth)
    {
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(property),
            Width = new DataGridLength(starWidth, DataGridLengthUnitType.Star)
        });
    }

    private static void AddCheckColumn(DataGrid grid, string header, string property)
    {
        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = header,
            Binding = new Binding(property),
            Width = DataGridLength.Auto
        });
    }

    private static TextBlock CreateWrappedText()
    {
        return new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = TrueBimBrushes.TextPrimary
        };
    }

    private static TabItem CreateTab(string header, UIElement content)
    {
        return new TabItem
        {
            Header = header,
            Content = content
        };
    }

    private static Border CreateMarginCard(string title, UIElement content)
    {
        Border card = TrueBimUi.CreateSectionCard(title, content);
        card.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        return card;
    }

    private sealed record Choice<T>(T Value, string DisplayName);

    private sealed record FamilyParameterChoice(
        Guid Guid,
        string Name,
        string DataTypeName,
        int SourceCount,
        bool HasNameConflict)
    {
        public string DisplayName =>
            $"{Name} — {Guid:D} — {DataTypeName} — источников: {SourceCount}"
            + (HasNameConflict ? " — КОНФЛИКТ ИМЁН" : string.Empty);
    }

    private sealed class SharedParameterListRow
    {
        public SharedParameterListRow(SharedParameterDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public SharedParameterDescriptor Descriptor { get; }

        public string IdentityKey => Descriptor.IdentityKey;

        public string Name => Descriptor.Name;

        public string ShortGuid => Descriptor.ShortGuid;

        public string DataType => Descriptor.DataTypeName;

        public string Binding => Descriptor.BindingDisplay;

        public int CategoryCount => Descriptor.Categories.Count;
    }

    private sealed record FamilySourceRow(
        FamilySourceKind SourceKind,
        string Name,
        string CategoryName,
        string Path,
        long? ProjectFamilyId,
        string Status)
    {
        public string SourceDisplay => SourceKind switch
        {
            FamilySourceKind.ActiveProject => "Проект",
            FamilySourceKind.Folder => "Папка",
            FamilySourceKind.SelectedFiles => "Файл",
            FamilySourceKind.CurrentFamily => "Текущее",
            _ => SourceKind.ToString()
        };

        public static FamilySourceRow FromPath(string path, FamilySourceKind sourceKind)
        {
            return new FamilySourceRow(
                sourceKind,
                System.IO.Path.GetFileNameWithoutExtension(path),
                "Будет определена после открытия",
                path,
                null,
                "Не открыт");
        }
    }

    private sealed class FamilyReportRow
    {
        public FamilyReportRow(FamilyParameterUsageReport report)
        {
            Report = report;
        }

        public FamilyParameterUsageReport Report { get; }

        public string FamilyName => Report.Family.Name;

        public string ParameterStatus => Report.ParameterFound ? "Найден" : "Не найден";

        public int TypeCount => Report.TypeValues.Count;

        public int FormulaCount => Report.Formulas.Count;

        public int DimensionCount => Report.Dimensions.Count;

        public int AssociationCount => Report.Associations.Count;

        public int BlockerCount => Report.DeletionBlockers.Count;
    }
}
