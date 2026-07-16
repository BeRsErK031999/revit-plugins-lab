using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Revit;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;
using WpfBinding = System.Windows.Data.Binding;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfEllipse = System.Windows.Shapes.Ellipse;
using WpfPath = System.Windows.Shapes.Path;
using WpfPolyline = System.Windows.Shapes.Polyline;
using ElementId = Autodesk.Revit.DB.ElementId;

namespace TrueBIM.App.Modules.IsoFieldRebar.UI;

public sealed class IsoFieldRebarWindow : TrueBimWindow
{
    private readonly string documentTitle;
    private readonly string documentKey;
    private readonly UIDocument? uiDocument;
    private readonly IIsoFieldFilePicker filePicker;
    private readonly IIsoFieldJsonReader jsonReader;
    private readonly IIsoFieldRecognitionRunner recognitionRunner;
    private readonly IsoFieldRevitPreviewService revitPreviewService;
    private readonly IsoFieldHostSelectionService hostSelectionService;
    private readonly IsoFieldRebarCreationService rebarCreationService;
    private readonly IsoFieldCoordinateMapper coordinateMapper = new();
    private readonly IsoFieldPreviewLayoutService previewLayoutService = new();
    private readonly IsoFieldSlabBindingService slabBindingService = new();
    private readonly IsoFieldSlabOverlayLayoutService slabOverlayLayoutService = new();
    private readonly IsoFieldSlabRebarLayoutService slabRebarLayoutService = new();
    private readonly IsoFieldSlabBindingProfileStorage slabBindingProfileStorage;
    private readonly IsoFieldSourceSetService sourceSetService = new();
    private readonly IsoFieldSourceSetManifestService sourceSetManifestService;
    private readonly IsoFieldSourceSetRecognitionService sourceSetRecognitionService = new();
    private readonly RebarRuleValidationService rebarRuleValidationService = new();
    private readonly IsoFieldRebarReviewService rebarReviewService = new();
    private readonly IsoFieldRebarChangePlanService rebarChangePlanService = new();
    private readonly IsoFieldRebarRuleOverrideService rebarRuleOverrideService = new();
    private readonly ObservableCollection<IsoFieldRebarReviewRow> rebarReviewRows = new();
    private readonly Dictionary<string, IsoFieldRebarRuleOverride> ruleOverrides = new(StringComparer.Ordinal);
    private readonly ITrueBimLogger logger;
    private readonly RevitActionDispatcher revitActions;
    private readonly TextBlock selectedFileText;
    private readonly TextBlock recognitionStatusText;
    private readonly TextBlock hostStatusText;
    private readonly TextBlock calibrationStatusText;
    private readonly TextBlock ruleStatusText;
    private readonly TextBlock rebarCreationStatusText;
    private readonly TextBlock previewStatusText;
    private readonly TextBlock footerStatusText;
    private readonly Canvas previewCanvas;
    private readonly Button recognizeButton;
    private readonly Button correctZonesButton;
    private readonly Button showRevitPreviewButton;
    private readonly Button clearRevitPreviewButton;
    private readonly Button selectHostButton;
    private readonly Button clearHostButton;
    private readonly Button previewRulesButton;
    private readonly Button compareChangesButton;
    private readonly Button createTestRebarButton;
    private readonly Button editZoneRuleButton;
    private readonly Button resetZoneRulesButton;
    private readonly Button saveSourceSetManifestButton;
    private readonly TextBlock workflowSummaryText;
    private readonly TextBlock sourceStepText;
    private readonly TextBlock mappingStepText;
    private readonly TextBlock zonesStepText;
    private readonly TextBlock hostStepText;
    private readonly TextBlock rulesStepText;
    private readonly TextBlock layerMappingStatusText;
    private readonly TextBlock manifestStatusText;
    private readonly StackPanel sourceSetRows = new();
    private readonly WrapPanel legendSummaryPanel = new();
    private readonly WpfTextBox calibrationAnchorXInput;
    private readonly WpfTextBox calibrationAnchorYInput;
    private readonly WpfTextBox calibrationMillimetersPerPixelInput;
    private readonly CheckBox calibrationInvertYInput;
    private readonly WpfTextBox slabImagePoint1XInput;
    private readonly WpfTextBox slabImagePoint1YInput;
    private readonly WpfTextBox slabImagePoint2XInput;
    private readonly WpfTextBox slabImagePoint2YInput;
    private readonly WpfTextBox slabImagePoint3XInput;
    private readonly WpfTextBox slabImagePoint3YInput;
    private readonly CheckBox slabMirrorImageYInput;
    private readonly Button pickSlabPoint1Button;
    private readonly Button pickSlabPoint2Button;
    private readonly Button pickSlabPoint3Button;
    private readonly Button applySlabBindingButton;
    private readonly Button loadSlabBindingProfileButton;
    private readonly Button saveSlabBindingProfileButton;
    private readonly TextBlock slabHostPoint1Text;
    private readonly TextBlock slabHostPoint2Text;
    private readonly TextBlock slabHostPoint3Text;
    private readonly TextBlock slabBindingStatusText;
    private readonly Expander slabBindingExpander;
    private readonly WpfComboBox reinforcementModeInput;
    private readonly WpfTextBox concreteCoverInput;
    private readonly WpfTextBox boundaryOffsetInput;
    private readonly WpfTextBox minimumBarLengthInput;
    private readonly WpfTextBox reviewSearchInput;
    private readonly WpfComboBox reviewLayerFilter;
    private readonly WpfComboBox reviewStatusFilter;
    private readonly WpfComboBox reviewDiameterFilter;
    private readonly WpfComboBox reviewSpacingFilter;
    private readonly WpfComboBox reviewConfidenceFilter;
    private readonly DataGrid rebarReviewGrid;
    private readonly TextBlock reviewSummaryText;
    private string? selectedJsonPath;
    private IsoFieldSourceSet? selectedSourceSet;
    private string? selectedSourceSetManifestPath;
    private bool isSourceSetManifestDirty;
    private IsoFieldRecognitionResult? currentRecognitionResult;
    private IsoFieldHostElement? selectedHostElement;
    private IsoFieldCalibration currentCalibration = IsoFieldCalibration.Default;
    private IsoFieldPoint? slabHostPoint1Feet;
    private IsoFieldPoint? slabHostPoint2Feet;
    private IsoFieldPoint? slabHostPoint3Feet;
    private IsoFieldSlabBindingAnalysis? currentSlabBinding;
    private IsoFieldSlabBindingProfile? availableSlabBindingProfile;
    private long selectedHostViewId;
    private RebarRulePreviewResult? currentRulePreview;
    private RebarRulePreviewResult? calculatedRulePreview;
    private IsoFieldRebarChangePlan? currentChangePlan;
    private string? currentChangePlanFingerprint;
    private IReadOnlyList<ElementId> activeRevitPreviewIds = Array.Empty<ElementId>();
    private const double PreviewCanvasWidth = 430;
    private const double PreviewCanvasHeight = 180;
    private static readonly IReadOnlyList<IsoFieldFaceOption> LayerFaceOptions =
    [
        new(IsoFieldRebarFace.Unconfirmed, "Не задано"),
        new(IsoFieldRebarFace.Bottom, "Низ"),
        new(IsoFieldRebarFace.Top, "Верх")
    ];
    private static readonly IReadOnlyList<IsoFieldReinforcementModeOption> ReinforcementModeOptions =
    [
        new(
            IsoFieldReinforcementMode.AdditionalOverBase,
            "Только усиление поверх базовой сетки"),
        new(
            IsoFieldReinforcementMode.FullCombination,
            "Полное сочетание внутри зон")
    ];
    private static readonly IReadOnlyList<IsoFieldReviewLayerOption> ReviewLayerOptions =
    [
        new(null, "Все слои"),
        new(IsoFieldLayerRole.As1X, "As1X"),
        new(IsoFieldLayerRole.As2X, "As2X"),
        new(IsoFieldLayerRole.As3Y, "As3Y"),
        new(IsoFieldLayerRole.As4Y, "As4Y")
    ];
    private static readonly IReadOnlyList<IsoFieldReviewStatusOption> ReviewStatusOptions =
    [
        new(null, "Все статусы"),
        new(IsoFieldRebarReviewStatus.NotCompared, "Не сравнено"),
        new(IsoFieldRebarReviewStatus.Add, "Добавить"),
        new(IsoFieldRebarReviewStatus.Update, "Обновить"),
        new(IsoFieldRebarReviewStatus.Delete, "Удалить"),
        new(IsoFieldRebarReviewStatus.Unchanged, "Без изменений"),
        new(IsoFieldRebarReviewStatus.Mixed, "Смешано"),
        new(IsoFieldRebarReviewStatus.Invalid, "Ошибка"),
        new(IsoFieldRebarReviewStatus.Excluded, "Исключена")
    ];
    private static readonly IReadOnlyList<IsoFieldReviewNumberOption> ReviewConfidenceOptions =
    [
        new(null, "Любой confidence"),
        new(0.9, "Не ниже 90%"),
        new(0.75, "Не ниже 75%"),
        new(0.5, "Не ниже 50%")
    ];

    public IsoFieldRebarWindow(
        string? documentTitle,
        UIDocument? uiDocument,
        IIsoFieldFilePicker filePicker,
        IIsoFieldJsonReader jsonReader,
        IIsoFieldRecognitionRunner recognitionRunner,
        IsoFieldRevitPreviewService revitPreviewService,
        IsoFieldHostSelectionService hostSelectionService,
        IsoFieldRebarCreationService rebarCreationService,
        ITrueBimLogger logger)
    {
        this.documentTitle = string.IsNullOrWhiteSpace(documentTitle)
            ? "документ не открыт"
            : documentTitle!;
        this.uiDocument = uiDocument;
        documentKey = IsoFieldSlabBindingProfileStorage.CreateDocumentKey(
            uiDocument?.Document?.PathName,
            this.documentTitle);
        this.filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
        this.jsonReader = jsonReader ?? throw new ArgumentNullException(nameof(jsonReader));
        this.recognitionRunner = recognitionRunner ?? throw new ArgumentNullException(nameof(recognitionRunner));
        this.revitPreviewService = revitPreviewService ?? throw new ArgumentNullException(nameof(revitPreviewService));
        this.hostSelectionService = hostSelectionService ?? throw new ArgumentNullException(nameof(hostSelectionService));
        this.rebarCreationService = rebarCreationService ?? throw new ArgumentNullException(nameof(rebarCreationService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        slabBindingProfileStorage = new IsoFieldSlabBindingProfileStorage(this.logger);
        sourceSetManifestService = new IsoFieldSourceSetManifestService(sourceSetService);
        revitActions = new RevitActionDispatcher("армирование по изополям", this.logger);

        selectedFileText = CreateMutedText("Источник не выбран.");
        recognitionStatusText = CreateMutedText($"JSON загружается сразу. Локальный обработчик изображений: {ResolveRecognitionRunnerName()}.");
        hostStatusText = CreateMutedText("Host-элемент не выбран.");
        calibrationAnchorXInput = CreateCalibrationInput(currentCalibration.ImageAnchor.X);
        calibrationAnchorYInput = CreateCalibrationInput(currentCalibration.ImageAnchor.Y);
        calibrationMillimetersPerPixelInput = CreateCalibrationInput(currentCalibration.MillimetersPerPixel);
        calibrationInvertYInput = new CheckBox
        {
            Content = "Y вниз",
            IsChecked = currentCalibration.InvertImageY,
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0),
            Style = TrueBimStyles.CreateCheckBoxStyle(),
            ToolTip = "Инвертировать ось Y изображения относительно направления вверх на виде."
        };
        calibrationStatusText = CreateMutedText(FormatCalibration(currentCalibration));
        slabImagePoint1XInput = CreateBindingInput(0);
        slabImagePoint1YInput = CreateBindingInput(0);
        slabImagePoint2XInput = CreateBindingInput(100);
        slabImagePoint2YInput = CreateBindingInput(0);
        slabImagePoint3XInput = CreateBindingInput(0);
        slabImagePoint3YInput = CreateBindingInput(100);
        slabMirrorImageYInput = new CheckBox
        {
            Content = "Отразить Y изображения",
            IsChecked = true,
            Style = TrueBimStyles.CreateCheckBoxStyle(),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Используйте для карт, где ось Y направлена вниз. Изменение требует повторной проверки привязки."
        };
        pickSlabPoint1Button = CreateActionButton(
            "Точка 1 на плите",
            TrueBimIcon.Apply,
            158,
            "Сначала выберите горизонтальную плиту и загрузите зоны.",
            (_, _) => PickSlabControlPoint(1));
        pickSlabPoint2Button = CreateActionButton(
            "Точка 2 на плите",
            TrueBimIcon.Apply,
            158,
            "Сначала выберите горизонтальную плиту и загрузите зоны.",
            (_, _) => PickSlabControlPoint(2));
        pickSlabPoint3Button = CreateActionButton(
            "Точка 3 на плите",
            TrueBimIcon.Apply,
            158,
            "Третья точка независимо проверяет масштаб, поворот и зеркальность.",
            (_, _) => PickSlabControlPoint(3));
        applySlabBindingButton = CreateActionButton(
            "Проверить привязку",
            TrueBimIcon.Preview,
            176,
            "Укажите три точки на карте и соответствующие точки на плите.",
            (_, _) => ApplySlabBinding(showDialogOnError: false));
        loadSlabBindingProfileButton = CreateActionButton(
            "Загрузить профиль",
            TrueBimIcon.Import,
            164,
            "Загрузить последнюю проверенную привязку для текущего документа, вида и плиты.",
            (_, _) => LoadSlabBindingProfile());
        saveSlabBindingProfileButton = CreateActionButton(
            "Сохранить профиль",
            TrueBimIcon.Export,
            164,
            "Сначала выполните успешную проверку привязки.",
            (_, _) => SaveSlabBindingProfile());
        slabHostPoint1Text = CreateMutedText("Точка 1 на плите не указана.");
        slabHostPoint2Text = CreateMutedText("Точка 2 на плите не указана.");
        slabHostPoint3Text = CreateMutedText("Точка 3 на плите не указана.");
        slabBindingStatusText = CreateMutedText("Выберите горизонтальную плиту, затем задайте три пары контрольных точек.");
        slabBindingExpander = CreateSlabBindingPanel();
        reinforcementModeInput = new WpfComboBox
        {
            ItemsSource = ReinforcementModeOptions,
            DisplayMemberPath = nameof(IsoFieldReinforcementModeOption.Label),
            SelectedIndex = 0,
            MinWidth = 292,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateComboBoxStyle(),
            ToolTip = "В режиме усиления первый компонент сочетания считается существующей базовой сеткой и повторно не создаётся."
        };
        concreteCoverInput = CreateBindingInput(IsoFieldEngineeringSettings.Default.ConcreteCoverMillimeters);
        boundaryOffsetInput = CreateBindingInput(IsoFieldEngineeringSettings.Default.BoundaryOffsetMillimeters);
        minimumBarLengthInput = CreateBindingInput(IsoFieldEngineeringSettings.Default.MinimumBarLengthMillimeters);
        reviewSearchInput = new WpfTextBox
        {
            Width = 180,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateTextBoxStyle(),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Найти строку по имени или id зоны."
        };
        reviewLayerFilter = CreateReviewFilterComboBox(ReviewLayerOptions, nameof(IsoFieldReviewLayerOption.Label), 112);
        reviewStatusFilter = CreateReviewFilterComboBox(ReviewStatusOptions, nameof(IsoFieldReviewStatusOption.Label), 150);
        reviewDiameterFilter = CreateReviewFilterComboBox(
            new[] { new IsoFieldReviewNumberOption(null, "Любой Ø") },
            nameof(IsoFieldReviewNumberOption.Label),
            112);
        reviewSpacingFilter = CreateReviewFilterComboBox(
            new[] { new IsoFieldReviewNumberOption(null, "Любой шаг") },
            nameof(IsoFieldReviewNumberOption.Label),
            118);
        reviewConfidenceFilter = CreateReviewFilterComboBox(
            ReviewConfidenceOptions,
            nameof(IsoFieldReviewNumberOption.Label),
            150);
        reviewSummaryText = CreateMutedText("Таблица появится после расчёта раскладки.");
        rebarReviewGrid = CreateRebarReviewGrid();
        editZoneRuleButton = CreateActionButton(
            "Настроить выбранную",
            TrueBimIcon.Settings,
            184,
            "Выберите расчётную строку зоны в таблице.",
            (_, _) => EditSelectedZoneRule());
        resetZoneRulesButton = CreateActionButton(
            "Сбросить настройки",
            TrueBimIcon.Refresh,
            174,
            "Ручных настроек зон пока нет.",
            (_, _) => ResetZoneRuleOverrides());
        ruleStatusText = CreateMutedText("Правила пока не рассчитаны.");
        rebarCreationStatusText = CreateMutedText("Раскладка армирования пока не создана.");
        previewStatusText = CreateMutedText("Контуры пока не загружены.");
        previewCanvas = CreatePreviewCanvas();
        recognizeButton = CreateActionButton(
            "Загрузить зоны",
            TrueBimIcon.Preview,
            176,
            "Сначала выберите JSON или полный комплект As1X, As2X, As3Y, As4Y.",
            (_, _) => RunRecognition());
        correctZonesButton = CreateActionButton(
            "Исправить зоны",
            TrueBimIcon.Settings,
            152,
            "Сначала загрузите или распознайте зоны.",
            (_, _) => CorrectZones());
        showRevitPreviewButton = CreateRevitPreviewButton();
        clearRevitPreviewButton = CreateClearRevitPreviewButton();
        selectHostButton = CreateActionButton(
            "Выбрать стену/плиту",
            TrueBimIcon.Apply,
            190,
            "Выбрать стену или плиту как host для армирования.",
            (_, _) => SelectHostElement());
        clearHostButton = CreateActionButton(
            "Сбросить",
            TrueBimIcon.Close,
            116,
            "Сбросить выбранный host-элемент.",
            (_, _) => ClearHostElement());
        previewRulesButton = CreateActionButton(
            "Рассчитать раскладку",
            TrueBimIcon.Preview,
            188,
            "Сначала загрузите зоны и выберите host-элемент.",
            (_, _) => PreviewRebarRules());
        compareChangesButton = CreateActionButton(
            "Сравнить с моделью",
            TrueBimIcon.Refresh,
            196,
            "Сначала рассчитайте валидную инженерную раскладку.",
            (_, _) => CompareEngineeringChanges());
        createTestRebarButton = CreateActionButton(
            "Применить изменения",
            TrueBimIcon.Apply,
            244,
            "Сначала рассчитайте раскладку и отдельно сравните её с моделью.",
            (_, _) => CreateTestRebar(),
            TrueBimButtonStyleKind.Primary);
        saveSourceSetManifestButton = CreateActionButton(
            "Сохранить manifest",
            TrueBimIcon.Export,
            168,
            "Сначала выберите полный комплект из четырёх изображений.",
            (_, _) => SaveSourceSetManifest());
        workflowSummaryText = CreateMutedText("Готово 0 из 5 обязательных шагов.");
        sourceStepText = CreateWorkflowStepText("Источник выбран");
        mappingStepText = CreateWorkflowStepText("Верх/низ подтверждены");
        zonesStepText = CreateWorkflowStepText("Зоны загружены");
        hostStepText = CreateWorkflowStepText("Host выбран");
        rulesStepText = CreateWorkflowStepText("Правила проверены");
        layerMappingStatusText = CreateMutedText("Назначение верх/низ появится после выбора комплекта изображений.");
        manifestStatusText = CreateMutedText("Manifest не сохранён.");
        footerStatusText = CreateMutedText("Линии предпросмотра создаются только по явной кнопке.");

        slabImagePoint1XInput.TextChanged += (_, _) => InvalidateSlabBinding();
        slabImagePoint1YInput.TextChanged += (_, _) => InvalidateSlabBinding();
        slabImagePoint2XInput.TextChanged += (_, _) => InvalidateSlabBinding();
        slabImagePoint2YInput.TextChanged += (_, _) => InvalidateSlabBinding();
        slabImagePoint3XInput.TextChanged += (_, _) => InvalidateSlabBinding();
        slabImagePoint3YInput.TextChanged += (_, _) => InvalidateSlabBinding();
        slabMirrorImageYInput.Checked += (_, _) => InvalidateSlabBinding();
        slabMirrorImageYInput.Unchecked += (_, _) => InvalidateSlabBinding();
        reinforcementModeInput.SelectionChanged += (_, _) => InvalidateEngineeringRules();
        concreteCoverInput.TextChanged += (_, _) => InvalidateEngineeringRules();
        boundaryOffsetInput.TextChanged += (_, _) => InvalidateEngineeringRules();
        minimumBarLengthInput.TextChanged += (_, _) => InvalidateEngineeringRules();
        reviewSearchInput.TextChanged += (_, _) => RefreshRebarReviewFilter();
        reviewLayerFilter.SelectionChanged += (_, _) => RefreshRebarReviewFilter();
        reviewStatusFilter.SelectionChanged += (_, _) => RefreshRebarReviewFilter();
        reviewDiameterFilter.SelectionChanged += (_, _) => RefreshRebarReviewFilter();
        reviewSpacingFilter.SelectionChanged += (_, _) => RefreshRebarReviewFilter();
        reviewConfidenceFilter.SelectionChanged += (_, _) => RefreshRebarReviewFilter();
        rebarReviewGrid.SelectionChanged += (_, _) => RefreshZoneRuleActions();
        rebarReviewGrid.MouseDoubleClick += (_, _) => EditSelectedZoneRule();

        Title = "Армирование по изополям";
        Icon = IconFactory.CreateImage(TrueBimIcon.IsoFieldRebar, 32);
        Width = 980;
        Height = 780;
        MinWidth = 820;
        MinHeight = 640;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();
        ClearPreview("Контуры пока не загружены.");
        RefreshWorkflowState();

        this.logger.Info("IsoField Rebar window opened.");
    }

    private UIElement CreateContent()
    {
        WpfGrid body = new();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Border filePanel = CreateFilePanel();
        WpfGrid.SetColumn(filePanel, 0);
        body.Children.Add(filePanel);

        Border workflowPanel = CreateWorkflowPanel();
        WpfGrid.SetColumn(workflowPanel, 1);
        WpfGrid.SetRowSpan(workflowPanel, 5);
        workflowPanel.Margin = new Thickness(TrueBimTheme.Spacing12, 0, 0, 0);
        body.Children.Add(workflowPanel);

        Border previewPanel = CreatePreviewPanel();
        WpfGrid.SetRow(previewPanel, 1);
        previewPanel.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        body.Children.Add(previewPanel);

        Border hostPanel = CreateHostPanel();
        WpfGrid.SetRow(hostPanel, 2);
        hostPanel.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        body.Children.Add(hostPanel);

        Border rulePanel = CreateRulePanel();
        WpfGrid.SetRow(rulePanel, 3);
        rulePanel.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        body.Children.Add(rulePanel);

        Border calibrationPanel = CreateCalibrationPanel();
        WpfGrid.SetRow(calibrationPanel, 4);
        calibrationPanel.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        body.Children.Add(calibrationPanel);

        return BuildShell(
            header: TrueBimUi.CreateHeader(
                Title,
                $"Активный документ: {documentTitle}. Последовательный сценарий: источник, зоны, host, инженерная раскладка, сравнение и применение после подтверждения.",
                TrueBimIcon.IsoFieldRebar),
            commandBar: TrueBimUi.CreateCommandBar(CreateGuideButton()),
            body: CreateScrollableBody(body),
            status: null,
            footer: CreateFooter());
    }

    private static ScrollViewer CreateScrollableBody(UIElement body)
    {
        return new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
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
            Height = TrueBimTheme.ControlHeight32,
            Padding = new Thickness(4),
            Style = TrueBimStyles.CreateButtonStyle(TrueBimButtonStyleKind.Ghost),
            ToolTip = CreateGuideToolTip(),
            HorizontalAlignment = HorizontalAlignment.Right
        };
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
            Text = "Методичка каркаса изополей",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        content.Children.Add(CreateMutedText("Нажмите, чтобы открыть справку с картинками: комплект карт, привязка, инженерные правила, сравнение и применение раскладки."));
        content.Children.Add(CreateMutedText("Сначала выполните «Сравнить с моделью» и проверьте таблицу зон. До отдельного подтверждения «Применить изменения» арматура в модель не записывается."));

        return new ToolTip
        {
            Content = content
        };
    }

    private void ShowGuide()
    {
        logger.Info("IsoField Rebar guide requested from the window header.");
        IsoFieldRebarGuideWindow guideWindow = new()
        {
            Owner = this
        };
        guideWindow.ShowDialog();
    }

    private Border CreateFilePanel()
    {
        StackPanel content = CreatePanelContent("1. Источник и зоны изополей");

        WrapPanel buttonRow = new();

        Button chooseButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Open, "Выбрать источник/manifest"),
            MinWidth = 214,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateButtonStyle(),
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Выбрать готовый JSON зон, manifest комплекта или четыре карты As1X, As2X, As3Y, As4Y."
        };
        chooseButton.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing4);
        chooseButton.Click += (_, _) => ChooseSourceFile();
        buttonRow.Children.Add(chooseButton);
        recognizeButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, TrueBimTheme.Spacing4);
        buttonRow.Children.Add(recognizeButton);
        saveSourceSetManifestButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, TrueBimTheme.Spacing4);
        buttonRow.Children.Add(saveSourceSetManifestButton);
        content.Children.Add(buttonRow);

        selectedFileText.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        content.Children.Add(selectedFileText);
        sourceSetRows.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        content.Children.Add(sourceSetRows);
        layerMappingStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, 0);
        layerMappingStatusText.Visibility = Visibility.Collapsed;
        content.Children.Add(layerMappingStatusText);
        manifestStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, 0);
        manifestStatusText.Visibility = Visibility.Collapsed;
        content.Children.Add(manifestStatusText);
        recognitionStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        content.Children.Add(recognitionStatusText);
        legendSummaryPanel.Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, 0);
        legendSummaryPanel.Visibility = Visibility.Collapsed;
        content.Children.Add(legendSummaryPanel);

        return CreatePanel(content);
    }

    private Border CreatePreviewPanel()
    {
        StackPanel content = CreatePanelContent("2. Проверка зон");

        Border canvasBorder = new()
        {
            BorderBrush = TrueBimBrushes.Border,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            Background = TrueBimBrushes.SurfaceAlt,
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Child = previewCanvas
        };
        content.Children.Add(canvasBorder);

        StackPanel buttonRow = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0)
        };
        buttonRow.Children.Add(correctZonesButton);
        showRevitPreviewButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        buttonRow.Children.Add(showRevitPreviewButton);
        buttonRow.Children.Add(clearRevitPreviewButton);
        content.Children.Add(buttonRow);

        previewStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        content.Children.Add(previewStatusText);

        return CreatePanel(content);
    }

    private Border CreateHostPanel()
    {
        StackPanel content = CreatePanelContent("3. Основа армирования");

        StackPanel buttonRow = new()
        {
            Orientation = Orientation.Horizontal
        };

        buttonRow.Children.Add(selectHostButton);

        clearHostButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        buttonRow.Children.Add(clearHostButton);

        content.Children.Add(buttonRow);

        hostStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        content.Children.Add(hostStatusText);
        content.Children.Add(slabBindingExpander);

        return CreatePanel(content);
    }

    private Border CreateCalibrationPanel()
    {
        StackPanel content = CreatePanelContent("Дополнительные настройки");

        StackPanel calibrationContent = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0)
        };

        StackPanel rows = new();
        rows.Children.Add(CreateInputRow("Якорь X", calibrationAnchorXInput));
        rows.Children.Add(CreateInputRow("Якорь Y", calibrationAnchorYInput));
        rows.Children.Add(CreateInputRow("Мм/пикс", calibrationMillimetersPerPixelInput));
        rows.Children.Add(calibrationInvertYInput);
        calibrationContent.Children.Add(rows);

        Button applyCalibrationButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Apply, "Применить"),
            MinWidth = 130,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateButtonStyle(),
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Проверить параметры калибровки."
        };
        applyCalibrationButton.Click += (_, _) => ApplyCalibration(showDialogOnError: true);
        calibrationContent.Children.Add(applyCalibrationButton);

        calibrationStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        calibrationContent.Children.Add(calibrationStatusText);

        content.Children.Add(new Expander
        {
            Header = "Ручная калибровка временных линий на виде Revit",
            Content = calibrationContent,
            IsExpanded = false,
            ToolTip = "Отдельная настройка старого preview на виде. Привязка к плите проверяется выше по трём контрольным точкам."
        });

        return CreatePanel(content);
    }

    private Expander CreateSlabBindingPanel()
    {
        StackPanel content = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0)
        };
        content.Children.Add(TrueBimUi.CreateInfoBanner(
            "Первые две точки задают масштаб и поворот. Третья точка должна находиться в стороне от их линии и независимо подтверждает привязку.",
            TrueBimUiSeverity.Neutral));

        StackPanel rows = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0)
        };
        rows.Children.Add(CreateSlabBindingPointRow(
            "Точка 1 на карте",
            slabImagePoint1XInput,
            slabImagePoint1YInput,
            pickSlabPoint1Button));
        slabHostPoint1Text.Margin = new Thickness(124, 0, 0, TrueBimTheme.Spacing8);
        rows.Children.Add(slabHostPoint1Text);
        rows.Children.Add(CreateSlabBindingPointRow(
            "Точка 2 на карте",
            slabImagePoint2XInput,
            slabImagePoint2YInput,
            pickSlabPoint2Button));
        slabHostPoint2Text.Margin = new Thickness(124, 0, 0, TrueBimTheme.Spacing8);
        rows.Children.Add(slabHostPoint2Text);
        rows.Children.Add(CreateSlabBindingPointRow(
            "Точка 3 на карте",
            slabImagePoint3XInput,
            slabImagePoint3YInput,
            pickSlabPoint3Button));
        slabHostPoint3Text.Margin = new Thickness(124, 0, 0, TrueBimTheme.Spacing8);
        rows.Children.Add(slabHostPoint3Text);
        content.Children.Add(rows);

        WrapPanel actions = new();
        actions.Children.Add(slabMirrorImageYInput);
        applySlabBindingButton.Margin = new Thickness(TrueBimTheme.Spacing16, 0, 0, 0);
        actions.Children.Add(applySlabBindingButton);
        content.Children.Add(actions);

        WrapPanel profileActions = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0)
        };
        profileActions.Children.Add(loadSlabBindingProfileButton);
        saveSlabBindingProfileButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        profileActions.Children.Add(saveSlabBindingProfileButton);
        content.Children.Add(profileActions);

        slabBindingStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        content.Children.Add(slabBindingStatusText);
        return new Expander
        {
            Header = "Привязка плиты по трём точкам",
            Content = content,
            IsExpanded = true,
            ToolTip = "Проверка масштаба, поворота и отражения с отсечением зон по контуру и отверстиям плиты."
        };
    }

    private static UIElement CreateSlabBindingPointRow(
        string label,
        WpfTextBox xInput,
        WpfTextBox yInput,
        Button pickButton)
    {
        WrapPanel row = new()
        {
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing4)
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Width = 124,
            Foreground = TrueBimBrushes.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(new TextBlock
        {
            Text = "X",
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = TrueBimBrushes.TextMuted
        });
        row.Children.Add(xInput);
        row.Children.Add(new TextBlock
        {
            Text = "Y",
            Margin = new Thickness(TrueBimTheme.Spacing8, 0, TrueBimTheme.Spacing4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = TrueBimBrushes.TextMuted
        });
        row.Children.Add(yInput);
        pickButton.Margin = new Thickness(TrueBimTheme.Spacing12, 0, 0, 0);
        row.Children.Add(pickButton);
        return row;
    }

    private Border CreateRulePanel()
    {
        StackPanel content = CreatePanelContent("4. Правила и применение");
        content.Children.Add(TrueBimUi.CreateInfoBanner(
            "Требуемая площадь принимается по верхней границе диапазона зоны, см²/м. Сочетание диаметр/шаг допускается только когда расчётная площадь не меньше требуемой.",
            TrueBimUiSeverity.Neutral));

        StackPanel settings = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0)
        };
        settings.Children.Add(CreateEngineeringModeRow());
        WrapPanel numericSettings = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0)
        };
        numericSettings.Children.Add(CreateEngineeringNumberInput(
            "Защитный слой, мм",
            concreteCoverInput,
            "Расстояние от грани бетона до поверхности крайнего стержня."));
        numericSettings.Children.Add(CreateEngineeringNumberInput(
            "Отступ от границ, мм",
            boundaryOffsetInput,
            "Стержни и их оси отступают от контура зоны и отверстий."));
        numericSettings.Children.Add(CreateEngineeringNumberInput(
            "Мин. длина, мм",
            minimumBarLengthInput,
            "Более короткие отрезки после отсечения не создаются."));
        settings.Children.Add(numericSettings);
        TextBlock layerOrderNote = CreateMutedText(
            "По толщине плиты слой X располагается ближе к соответствующей грани, слой Y — глубже с зазором 5 мм. Перед выпуском проверьте это правило на вашем стандарте.");
        layerOrderNote.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        settings.Children.Add(layerOrderNote);
        content.Children.Add(new Expander
        {
            Header = "Инженерные параметры раскладки",
            Content = settings,
            IsExpanded = true,
            ToolTip = "Параметры влияют на расчёт количества и фактическое положение стержней."
        });

        WrapPanel buttonRow = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0)
        };

        buttonRow.Children.Add(previewRulesButton);

        compareChangesButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, TrueBimTheme.Spacing4);
        buttonRow.Children.Add(compareChangesButton);

        createTestRebarButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, TrueBimTheme.Spacing4);
        buttonRow.Children.Add(createTestRebarButton);

        content.Children.Add(buttonRow);

        ruleStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        content.Children.Add(ruleStatusText);
        rebarCreationStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        content.Children.Add(rebarCreationStatusText);
        content.Children.Add(CreateRebarReviewPanel());

        return CreatePanel(content);
    }

    private UIElement CreateRebarReviewPanel()
    {
        StackPanel content = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0)
        };
        content.Children.Add(TrueBimUi.CreateInfoBanner(
            "Проверьте строки и счётчики до применения. Фильтры меняют только отображение и не исключают зоны из раскладки.",
            TrueBimUiSeverity.Info));

        WrapPanel filters = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, TrueBimTheme.Spacing8)
        };
        filters.Children.Add(CreateReviewFilterField("Поиск", reviewSearchInput));
        filters.Children.Add(CreateReviewFilterField("Слой", reviewLayerFilter));
        filters.Children.Add(CreateReviewFilterField("Статус", reviewStatusFilter));
        filters.Children.Add(CreateReviewFilterField("Диаметр", reviewDiameterFilter));
        filters.Children.Add(CreateReviewFilterField("Шаг", reviewSpacingFilter));
        filters.Children.Add(CreateReviewFilterField("Confidence", reviewConfidenceFilter));
        content.Children.Add(filters);

        WrapPanel zoneActions = new()
        {
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };
        zoneActions.Children.Add(editZoneRuleButton);
        resetZoneRulesButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        zoneActions.Children.Add(resetZoneRulesButton);
        content.Children.Add(zoneActions);
        content.Children.Add(rebarReviewGrid);
        content.Children.Add(reviewSummaryText);

        return new Expander
        {
            Header = "Проверка зон и изменений",
            Content = content,
            IsExpanded = true,
            ToolTip = "Таблица позволяет настроить или исключить расчётную зону, а после сравнения показывает добавление, обновление, удаление и неизменённые элементы."
        };
    }

    private DataGrid CreateRebarReviewGrid()
    {
        DataGrid grid = new()
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserReorderColumns = true,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            IsReadOnly = true,
            MinHeight = 220,
            MaxHeight = 340,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            Style = TrueBimStyles.CreateDataGridStyle(),
            ItemsSource = rebarReviewRows
        };
        grid.Columns.Add(CreateReviewColumn("Слой", nameof(IsoFieldRebarReviewRow.LayerText), 74));
        grid.Columns.Add(CreateReviewColumn("Зона", nameof(IsoFieldRebarReviewRow.ZoneName), new DataGridLength(1, DataGridLengthUnitType.Star), 180));
        grid.Columns.Add(CreateReviewColumn("Статус", nameof(IsoFieldRebarReviewRow.StatusText), 118));
        grid.Columns.Add(CreateReviewColumn("Напр./грань", nameof(IsoFieldRebarReviewRow.FaceDirectionText), 116));
        grid.Columns.Add(CreateReviewColumn("Армирование", nameof(IsoFieldRebarReviewRow.ReinforcementText), 142));
        grid.Columns.Add(CreateReviewColumn("Площадь", nameof(IsoFieldRebarReviewRow.AreaText), 132));
        grid.Columns.Add(CreateReviewColumn("Стержни", nameof(IsoFieldRebarReviewRow.EstimatedBarCountText), 78));
        grid.Columns.Add(CreateReviewColumn("Confidence", nameof(IsoFieldRebarReviewRow.ConfidenceText), 92));
        grid.Columns.Add(CreateReviewColumn("Настройка", nameof(IsoFieldRebarReviewRow.SettingText), 136));
        grid.Columns.Add(CreateReviewColumn("Изменения", nameof(IsoFieldRebarReviewRow.ChangeSummary), 190));
        return grid;
    }

    private static DataGridTextColumn CreateReviewColumn(
        string header,
        string bindingPath,
        double width)
    {
        return CreateReviewColumn(header, bindingPath, new DataGridLength(width), 0);
    }

    private static DataGridTextColumn CreateReviewColumn(
        string header,
        string bindingPath,
        DataGridLength width,
        double minWidth)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new WpfBinding(bindingPath),
            Width = width,
            MinWidth = minWidth
        };
    }

    private static StackPanel CreateReviewFilterField(string label, UIElement control)
    {
        StackPanel field = new()
        {
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, TrueBimTheme.Spacing8)
        };
        field.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = TrueBimBrushes.TextMuted,
            FontSize = TrueBimTheme.CaptionFontSize,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing4)
        });
        field.Children.Add(control);
        return field;
    }

    private static WpfComboBox CreateReviewFilterComboBox(
        System.Collections.IEnumerable itemsSource,
        string displayMemberPath,
        double width)
    {
        return new WpfComboBox
        {
            ItemsSource = itemsSource,
            DisplayMemberPath = displayMemberPath,
            SelectedIndex = 0,
            Width = width,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateComboBoxStyle()
        };
    }

    private void RefreshRebarReviewRows()
    {
        rebarReviewRows.Clear();
        if (currentRulePreview is null || currentRecognitionResult is null)
        {
            SetReviewNumberOptions(reviewDiameterFilter, Array.Empty<double>(), "Любой Ø", "Ø");
            SetReviewNumberOptions(reviewSpacingFilter, Array.Empty<double>(), "Любой шаг", "Шаг");
            reviewSummaryText.Text = "Таблица появится после расчёта раскладки.";
            return;
        }

        IReadOnlyList<IsoFieldRebarReviewRow> rows = rebarReviewService.BuildRows(
            currentRulePreview,
            currentRecognitionResult,
            currentChangePlan);
        foreach (IsoFieldRebarReviewRow row in rows)
        {
            rebarReviewRows.Add(row);
        }

        SetReviewNumberOptions(
            reviewDiameterFilter,
            rows.SelectMany(row => row.DiametersMillimeters),
            "Любой Ø",
            "Ø");
        SetReviewNumberOptions(
            reviewSpacingFilter,
            rows.SelectMany(row => row.SpacingsMillimeters),
            "Любой шаг",
            "Шаг");
        RefreshRebarReviewFilter();
    }

    private void RefreshRebarReviewFilter()
    {
        if (rebarReviewGrid is null)
        {
            return;
        }

        IsoFieldRebarReviewFilter filter = new(
            reviewSearchInput.Text ?? string.Empty,
            (reviewLayerFilter.SelectedItem as IsoFieldReviewLayerOption)?.LayerRole,
            (reviewStatusFilter.SelectedItem as IsoFieldReviewStatusOption)?.Status,
            (reviewDiameterFilter.SelectedItem as IsoFieldReviewNumberOption)?.Value,
            (reviewSpacingFilter.SelectedItem as IsoFieldReviewNumberOption)?.Value,
            (reviewConfidenceFilter.SelectedItem as IsoFieldReviewNumberOption)?.Value);
        ICollectionView view = CollectionViewSource.GetDefaultView(rebarReviewRows);
        view.Filter = item => item is IsoFieldRebarReviewRow row
            && rebarReviewService.MatchesFilter(row, filter);
        view.Refresh();

        int visibleCount = view.Cast<object>().Count();
        string planSummary = currentChangePlan is null
            ? "Сравнение с моделью ещё не выполнено."
            : currentChangePlan.Summary;
        string overrideSummary = ruleOverrides.Count > 0
            ? $"Ручных настроек: {ruleOverrides.Count}. "
            : string.Empty;
        reviewSummaryText.Text = $"Зон: {rebarReviewRows.Count}; показано: {visibleCount}. {overrideSummary}{planSummary}";
    }

    private static void SetReviewNumberOptions(
        WpfComboBox comboBox,
        IEnumerable<double> values,
        string allLabel,
        string valuePrefix)
    {
        double? selectedValue = (comboBox.SelectedItem as IsoFieldReviewNumberOption)?.Value;
        List<IsoFieldReviewNumberOption> options =
        [
            new(null, allLabel)
        ];
        options.AddRange(values
            .Distinct()
            .OrderBy(value => value)
            .Select(value => new IsoFieldReviewNumberOption(
                value,
                $"{valuePrefix} {FormatNumber(value)} мм")));
        comboBox.ItemsSource = options;
        comboBox.SelectedItem = options.FirstOrDefault(option => option.Value == selectedValue)
            ?? options[0];
    }

    private void SetCurrentChangePlan(IsoFieldRebarChangePlan? changePlan)
    {
        currentChangePlan = changePlan;
        currentChangePlanFingerprint = changePlan is null
            ? null
            : rebarChangePlanService.BuildFingerprint(changePlan);
        RefreshRebarReviewRows();
        RefreshWorkflowState();
    }

    private void EditSelectedZoneRule()
    {
        if (rebarReviewGrid.SelectedItem is not IsoFieldRebarReviewRow selectedRow
            || calculatedRulePreview?.EngineeringSettings is null)
        {
            return;
        }

        RebarRulePreviewItem? calculatedItem = calculatedRulePreview.Items.FirstOrDefault(item =>
            string.Equals(item.ZoneId, selectedRow.ZoneId, StringComparison.Ordinal)
            && item.Rule.LayerRole == selectedRow.LayerRole);
        if (calculatedItem is null)
        {
            return;
        }

        ruleOverrides.TryGetValue(calculatedItem.ZoneId, out IsoFieldRebarRuleOverride? currentOverride);
        IsoFieldRebarRuleOverrideWindow dialog = new(
            calculatedItem,
            calculatedRulePreview.EngineeringSettings,
            ResolveReinforcementOptions(calculatedItem),
            currentOverride)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (dialog.ResetToCalculated || IsCalculatedRule(dialog.Result, calculatedItem))
        {
            ruleOverrides.Remove(calculatedItem.ZoneId);
        }
        else if (dialog.Result is not null)
        {
            ruleOverrides[calculatedItem.ZoneId] = dialog.Result;
        }

        ApplyZoneRuleOverrides();
    }

    private void ResetZoneRuleOverrides()
    {
        if (ruleOverrides.Count == 0)
        {
            return;
        }

        TaskDialog dialog = new("Армирование по изополям")
        {
            MainInstruction = "Сбросить ручные настройки зон?",
            MainContent = $"Будут восстановлены расчётные правила для {ruleOverrides.Count} зон. Сравнение с моделью потребуется выполнить заново.",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No
        };
        if (dialog.Show() != TaskDialogResult.Yes)
        {
            return;
        }

        ruleOverrides.Clear();
        ApplyZoneRuleOverrides();
    }

    private void ApplyZoneRuleOverrides()
    {
        if (calculatedRulePreview is null)
        {
            return;
        }

        currentRulePreview = rebarRuleOverrideService.Apply(calculatedRulePreview, ruleOverrides);
        currentChangePlan = null;
        currentChangePlanFingerprint = null;
        ruleStatusText.Text = FormatRulePreview(currentRulePreview);
        rebarCreationStatusText.Text = currentRulePreview.CanCreateRebar
            ? $"Ручные настройки применены: {ruleOverrides.Count}. Нажмите «Сравнить с моделью» и проверьте обновлённый diff."
            : "Ручные настройки применены, но раскладка содержит ошибки. Исправьте зоны, выделенные в таблице.";
        footerStatusText.Text = "Раскладка пересчитана после ручных настроек. Модель Revit не изменялась.";
        if (currentRecognitionResult is not null && currentSlabBinding is not null)
        {
            RenderPreview(currentRecognitionResult);
        }

        RefreshRebarReviewRows();
        RefreshWorkflowState();
        logger.Info($"IsoField zone rule overrides applied. Overrides={ruleOverrides.Count}; ActiveZones={currentRulePreview.ActiveItems.Count}; EstimatedBars={currentRulePreview.EstimatedBarCount}; CanCreate={currentRulePreview.CanCreateRebar}.");
    }

    private void RefreshZoneRuleActions()
    {
        IsoFieldRebarReviewRow? selectedRow = rebarReviewGrid?.SelectedItem as IsoFieldRebarReviewRow;
        bool canEdit = selectedRow is not null
            && calculatedRulePreview?.EngineeringSettings is not null
            && calculatedRulePreview.Items.Any(item =>
                string.Equals(item.ZoneId, selectedRow.ZoneId, StringComparison.Ordinal)
                && item.Rule.LayerRole == selectedRow.LayerRole);
        editZoneRuleButton.IsEnabled = canEdit;
        editZoneRuleButton.ToolTip = canEdit
            ? "Изменить сочетание диаметр/шаг или исключить выбранную зону до сравнения с моделью."
            : "Выберите расчётную строку зоны. Ранее созданные зоны только на удаление не редактируются.";
        resetZoneRulesButton.IsEnabled = ruleOverrides.Count > 0;
        resetZoneRulesButton.ToolTip = resetZoneRulesButton.IsEnabled
            ? $"Сбросить ручные настройки всех зон: {ruleOverrides.Count}."
            : "Ручных настроек зон пока нет.";
    }

    private IReadOnlyList<string> ResolveReinforcementOptions(RebarRulePreviewItem item)
    {
        IEnumerable<string?> recognized = currentRecognitionResult?.EffectiveLegends
            .Where(legend => legend.LayerRole == item.Rule.LayerRole)
            .SelectMany(legend => legend.EffectiveBoundaries)
            .Select(boundary => boundary.ReinforcementLabel)
            ?? Array.Empty<string?>();
        return new[] { item.Rule.ReinforcementLabel }
            .Concat(recognized)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static bool IsCalculatedRule(
        IsoFieldRebarRuleOverride? zoneOverride,
        RebarRulePreviewItem calculatedItem)
    {
        return zoneOverride is not null
            && zoneOverride.IsIncluded
            && string.Equals(
                zoneOverride.ReinforcementLabel.Trim(),
                calculatedItem.Rule.ReinforcementLabel?.Trim(),
                StringComparison.CurrentCultureIgnoreCase);
    }

    private UIElement CreateEngineeringModeRow()
    {
        WrapPanel row = new();
        row.Children.Add(new TextBlock
        {
            Text = "Режим",
            Width = 124,
            Foreground = TrueBimBrushes.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(reinforcementModeInput);
        return row;
    }

    private static UIElement CreateEngineeringNumberInput(
        string label,
        WpfTextBox input,
        string toolTip)
    {
        StackPanel field = new()
        {
            Width = 174,
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing12, 0),
            ToolTip = toolTip
        };
        field.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = TrueBimBrushes.TextSecondary,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing4)
        });
        input.Width = 112;
        field.Children.Add(input);
        return field;
    }

    private Border CreateWorkflowPanel()
    {
        StackPanel content = CreatePanelContent("Готовность");

        workflowSummaryText.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        content.Children.Add(TrueBimUi.CreateInfoBanner(workflowSummaryText));
        content.Children.Add(sourceStepText);
        content.Children.Add(mappingStepText);
        content.Children.Add(zonesStepText);
        content.Children.Add(hostStepText);
        content.Children.Add(rulesStepText);

        TextBlock note = CreateMutedText("Применение доступно только после проверки обязательных шагов. Перед подтверждением модуль сравнит расчёт с принадлежащей ему арматурой на выбранной плите; ручные элементы не изменяются.");
        note.Margin = new Thickness(0, TrueBimTheme.Spacing16, 0, 0);
        content.Children.Add(note);

        return CreatePanel(content);
    }

    private UIElement CreateFooter()
    {
        Button closeButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Закрыть"),
            MinWidth = 120,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateButtonStyle(),
            IsCancel = true,
            ToolTip = "Закрыть окно."
        };
        closeButton.Click += (_, _) => Close();

        return TrueBimUi.CreateFooter(footerStatusText, closeButton);
    }

    private void ChooseSourceFile()
    {
        try
        {
            IReadOnlyList<string> paths = filePicker.PickIsoFieldSourceFiles();
            if (paths.Count == 0)
            {
                footerStatusText.Text = "Выбор источников отменен.";
                logger.Info("IsoField source selection canceled.");
                return;
            }

            bool containsJson = paths.Any(IsJsonFile);
            if (containsJson)
            {
                if (paths.Count != 1 || !IsJsonFile(paths[0]))
                {
                    selectedJsonPath = null;
                    selectedSourceSet = null;
                    ResetSourceSetManifestState();
                    sourceSetRows.Children.Clear();
                    selectedFileText.Text = "Выбор отклонён: JSON и изображения нельзя смешивать.";
                    selectedFileText.Foreground = TrueBimBrushes.Danger;
                    selectedFileText.ToolTip = null;
                    ClearPreview("Контуры не загружены: выберите один JSON или четыре изображения.");
                    recognitionStatusText.Text = "JSON нужно выбирать отдельно от комплекта изображений.";
                    footerStatusText.Text = "Выбор отклонён. Модель Revit не изменялась.";
                    logger.Warning("IsoField source selection mixed JSON with other files and was rejected.");
                    return;
                }

                string selectedPath = paths[0];
                if (IsoFieldSourceSetManifestService.IsManifestPath(selectedPath))
                {
                    LoadSourceSetManifest(selectedPath);
                    return;
                }

                selectedJsonPath = selectedPath;
                selectedSourceSet = null;
                ResetSourceSetManifestState();
                sourceSetRows.Children.Clear();
                selectedFileText.Text = $"JSON: {Path.GetFileName(selectedPath)}";
                selectedFileText.Foreground = TrueBimBrushes.Success;
                selectedFileText.ToolTip = selectedPath;
                logger.Info($"IsoField JSON source selected: {Path.GetFileName(selectedPath)}.");
                ReadJsonSource(selectedPath);
            }
            else
            {
                selectedJsonPath = null;
                selectedSourceSet = sourceSetService.Build(paths);
                selectedSourceSetManifestPath = null;
                isSourceSetManifestDirty = true;
                UpdateSourceSetPresentation();
                ClearPreview("Контуры появятся после обработки полного комплекта изображений.");
                footerStatusText.Text = selectedSourceSet.IsComplete
                    ? "Комплект изополей готов к обработке. Модель Revit не изменялась."
                    : "Комплект требует исправления назначения файлов.";
                logger.Info(
                    $"IsoField image source set selected. Files={selectedSourceSet.Files.Count}; "
                    + $"Complete={selectedSourceSet.IsComplete}; Issues={selectedSourceSet.ValidationMessages.Count}; "
                    + $"HeaderRoles={selectedSourceSet.Files.Count(file => file.RoleDetection?.HeaderRole.HasValue == true)}; "
                    + $"RoleConflicts={selectedSourceSet.Files.Count(file => file.RoleDetection?.Kind == IsoFieldRoleDetectionKind.Conflict)}.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            logger.Error("Failed to select IsoField source file.", exception);
            selectedJsonPath = null;
            selectedSourceSet = null;
            ResetSourceSetManifestState();
            sourceSetRows.Children.Clear();
            selectedFileText.Text = "Источник не выбран.";
            selectedFileText.Foreground = TrueBimBrushes.Danger;
            selectedFileText.ToolTip = null;
            ClearPreview("Контуры не загружены из выбранного файла.");
            recognitionStatusText.Text = "Источник не удалось прочитать. Проверьте выбранные файлы.";
            ClearRulePreview("Правила не рассчитаны: контуры не загружены.");
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось выбрать файл изополей. Используйте логи для диагностики.");
            footerStatusText.Text = "Не удалось выбрать файл.";
        }
    }

    private void LoadSourceSetManifest(string manifestPath)
    {
        selectedJsonPath = null;
        selectedSourceSet = sourceSetManifestService.Load(manifestPath);
        selectedSourceSetManifestPath = manifestPath;
        isSourceSetManifestDirty = false;
        UpdateSourceSetPresentation();
        ClearPreview("Комплект восстановлен из manifest. Зоны нужно загрузить заново.");
        footerStatusText.Text = selectedSourceSet.IsComplete
            ? "Manifest комплекта загружен и проверен. Модель Revit не изменялась."
            : "Manifest загружен, но исходные файлы не прошли проверку.";
        logger.Info(
            $"IsoField source-set manifest loaded. File={Path.GetFileName(manifestPath)}; "
            + $"Complete={selectedSourceSet.IsComplete}; MappingsConfirmed={selectedSourceSet.HasConfirmedLayerMappings}.");
    }

    private void SaveSourceSetManifest()
    {
        try
        {
            if (selectedSourceSet?.IsComplete != true)
            {
                footerStatusText.Text = "Manifest не сохранён: сначала исправьте комплект изображений.";
                return;
            }

            string? initialDirectory = selectedSourceSetManifestPath is null
                ? Path.GetDirectoryName(selectedSourceSet.Files[0].FilePath)
                : Path.GetDirectoryName(selectedSourceSetManifestPath);
            string? suggestedFileName = selectedSourceSetManifestPath is null
                ? IsoFieldSourceSetManifestService.DefaultManifestFileName
                : Path.GetFileName(selectedSourceSetManifestPath);
            string? manifestPath = filePicker.PickSourceSetManifestSavePath(initialDirectory, suggestedFileName);
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                footerStatusText.Text = "Сохранение manifest отменено.";
                logger.Info("IsoField source-set manifest save canceled.");
                return;
            }

            sourceSetManifestService.Save(selectedSourceSet, manifestPath!);
            selectedSourceSetManifestPath = manifestPath;
            isSourceSetManifestDirty = false;
            UpdateManifestStatus();
            footerStatusText.Text = "Manifest комплекта сохранён. Модель Revit не изменялась.";
            logger.Info($"IsoField source-set manifest saved. File={Path.GetFileName(manifestPath)}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.Error("Failed to save IsoField source-set manifest.", exception);
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось сохранить manifest комплекта. Используйте логи для диагностики.");
            footerStatusText.Text = "Не удалось сохранить manifest комплекта.";
        }
    }

    private void UpdateSourceSetPresentation()
    {
        sourceSetRows.Children.Clear();
        if (selectedSourceSet is null)
        {
            return;
        }

        int assignedCount = selectedSourceSet.Files.Count(file => file.Role.HasValue);
        int headerRoleCount = selectedSourceSet.Files.Count(file => file.RoleDetection?.HeaderRole.HasValue == true);
        selectedFileText.Text = selectedSourceSet.IsComplete
            ? $"Комплект готов: 4 из 4 слоёв назначены, заголовком подтверждено {headerRoleCount} из 4."
            : $"Комплект не готов: назначено {assignedCount} из 4 слоёв.";
        selectedFileText.Foreground = selectedSourceSet.IsComplete
            ? TrueBimBrushes.Success
            : TrueBimBrushes.Danger;
        selectedFileText.ToolTip = string.Join(Environment.NewLine, selectedSourceSet.Files.Select(file => file.FilePath));

        sourceSetRows.Children.Add(CreateSourceSetHeader());
        foreach (IsoFieldSourceFile sourceFile in selectedSourceSet.Files)
        {
            sourceSetRows.Children.Add(CreateSourceSetRow(sourceFile));
        }

        recognitionStatusText.Text = selectedSourceSet.IsComplete
            ? $"Комплект проверен. Доступен обработчик «{ResolveRecognitionRunnerName()}»; нажмите «Распознать 4 изображения»."
            : FormatSourceSetIssues(selectedSourceSet);
        UpdateLayerMappingStatus();
        UpdateManifestStatus();
    }

    private void UpdateLayerMappingStatus()
    {
        if (selectedSourceSet is null)
        {
            layerMappingStatusText.Visibility = Visibility.Collapsed;
            return;
        }

        layerMappingStatusText.Visibility = Visibility.Visible;
        layerMappingStatusText.Text = selectedSourceSet.HasConfirmedLayerMappings
            ? "Назначение подтверждено: для X и Y выбрано по одному верхнему и нижнему слою."
            : string.Join(" ", selectedSourceSet.LayerMappingValidationMessages);
        bool hasUnconfirmedFaces = selectedSourceSet.EffectiveLayerMappings
            .Any(mapping => mapping.Face == IsoFieldRebarFace.Unconfirmed);
        layerMappingStatusText.Foreground = selectedSourceSet.HasConfirmedLayerMappings
            ? TrueBimBrushes.Success
            : hasUnconfirmedFaces ? TrueBimBrushes.Warning : TrueBimBrushes.Danger;
    }

    private void UpdateManifestStatus()
    {
        if (selectedSourceSet is null)
        {
            manifestStatusText.Visibility = Visibility.Collapsed;
            return;
        }

        manifestStatusText.Visibility = Visibility.Visible;
        if (selectedSourceSetManifestPath is null)
        {
            manifestStatusText.Text = "Manifest ещё не сохранён.";
            manifestStatusText.Foreground = TrueBimBrushes.TextMuted;
            manifestStatusText.ToolTip = null;
            return;
        }

        manifestStatusText.Text = isSourceSetManifestDirty
            ? $"Комплект изменён после загрузки {Path.GetFileName(selectedSourceSetManifestPath)} — сохраните manifest заново."
            : $"Manifest: {Path.GetFileName(selectedSourceSetManifestPath)}";
        manifestStatusText.Foreground = isSourceSetManifestDirty
            ? TrueBimBrushes.Warning
            : TrueBimBrushes.Success;
        manifestStatusText.ToolTip = selectedSourceSetManifestPath;
    }

    private void ResetSourceSetManifestState()
    {
        selectedSourceSetManifestPath = null;
        isSourceSetManifestDirty = false;
        layerMappingStatusText.Visibility = Visibility.Collapsed;
        manifestStatusText.Visibility = Visibility.Collapsed;
        manifestStatusText.ToolTip = null;
    }

    private Border CreateSourceSetRow(IsoFieldSourceFile sourceFile)
    {
        WpfGrid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });

        Border thumbnailBorder = new()
        {
            Width = 96,
            Height = 54,
            BorderBrush = TrueBimBrushes.Border,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Background = TrueBimBrushes.SurfaceAlt,
            Child = new Image
            {
                Source = LoadSourceThumbnail(sourceFile.FilePath),
                Stretch = Stretch.Uniform
            }
        };
        row.Children.Add(thumbnailBorder);

        StackPanel fileInfo = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(TrueBimTheme.Spacing8, 0, TrueBimTheme.Spacing8, 0)
        };
        fileInfo.Children.Add(new TextBlock
        {
            Text = sourceFile.FileName,
            Foreground = TrueBimBrushes.TextPrimary,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = sourceFile.FilePath
        });
        fileInfo.Children.Add(CreateMutedText(sourceFile.ImageSizeText));
        RoleDetectionPresentation roleDetection = CreateRoleDetectionPresentation(sourceFile);
        TextBlock roleEvidenceText = CreateMutedText(roleDetection.Label);
        roleEvidenceText.Foreground = roleDetection.Foreground;
        roleEvidenceText.ToolTip = roleDetection.ToolTip;
        fileInfo.Children.Add(roleEvidenceText);
        WpfGrid.SetColumn(fileInfo, 1);
        row.Children.Add(fileInfo);

        WpfComboBox roleSelector = new()
        {
            ItemsSource = IsoFieldSourceSet.RequiredRoles,
            MinHeight = TrueBimTheme.ControlHeight32,
            VerticalAlignment = VerticalAlignment.Center,
            Style = TrueBimStyles.CreateComboBoxStyle(),
            ToolTip = roleDetection.ToolTip
        };
        if (sourceFile.Role.HasValue)
        {
            roleSelector.SelectedItem = sourceFile.Role.Value;
        }

        roleSelector.SelectionChanged += (_, _) =>
        {
            if (roleSelector.SelectedItem is IsoFieldLayerRole role && sourceFile.Role != role)
            {
                AssignSourceRole(sourceFile.FilePath, role);
            }
        };
        WpfGrid.SetColumn(roleSelector, 2);
        row.Children.Add(roleSelector);

        TextBlock directionText = CreateMutedText(sourceFile.Role.HasValue
            ? IsoFieldLayerMapping.ResolveDirection(sourceFile.Role.Value).ToString()
            : "—");
        directionText.HorizontalAlignment = HorizontalAlignment.Center;
        directionText.VerticalAlignment = VerticalAlignment.Center;
        directionText.FontWeight = FontWeights.SemiBold;
        WpfGrid.SetColumn(directionText, 3);
        row.Children.Add(directionText);

        IsoFieldLayerMapping? mapping = sourceFile.Role.HasValue
            ? selectedSourceSet?.GetLayerMapping(sourceFile.Role.Value)
            : null;
        WpfComboBox faceSelector = new()
        {
            ItemsSource = LayerFaceOptions,
            DisplayMemberPath = nameof(IsoFieldFaceOption.Label),
            MinHeight = TrueBimTheme.ControlHeight32,
            VerticalAlignment = VerticalAlignment.Center,
            Style = TrueBimStyles.CreateComboBoxStyle(),
            IsEnabled = selectedSourceSet?.IsComplete == true && sourceFile.Role.HasValue,
            ToolTip = selectedSourceSet?.IsComplete == true
                ? "Явно назначьте верхнюю или нижнюю грань для расчётного слоя."
                : "Сначала исправьте состав и роли комплекта."
        };
        faceSelector.SelectedItem = LayerFaceOptions.First(option => option.Face == (mapping?.Face ?? IsoFieldRebarFace.Unconfirmed));
        faceSelector.SelectionChanged += (_, _) =>
        {
            if (sourceFile.Role.HasValue
                && faceSelector.SelectedItem is IsoFieldFaceOption option
                && selectedSourceSet?.GetLayerMapping(sourceFile.Role.Value).Face != option.Face)
            {
                AssignSourceFace(sourceFile.Role.Value, option.Face);
            }
        };
        WpfGrid.SetColumn(faceSelector, 4);
        row.Children.Add(faceSelector);

        return new Border
        {
            Child = row,
            Padding = new Thickness(TrueBimTheme.Spacing8),
            BorderBrush = TrueBimBrushes.Border,
            BorderThickness = new Thickness(0, 0, 0, TrueBimTheme.BorderWidth),
            Background = sourceFile.RoleDetection?.Kind == IsoFieldRoleDetectionKind.Conflict
                ? TrueBimBrushes.DangerBackground
                : TrueBimBrushes.Surface
        };
    }

    private static RoleDetectionPresentation CreateRoleDetectionPresentation(IsoFieldSourceFile sourceFile)
    {
        IsoFieldRoleDetection detection = sourceFile.RoleDetection
            ?? new IsoFieldRoleDetection(IsoFieldRoleDetectionKind.NotDetected);
        string confidence = detection.HeaderConfidence.HasValue
            ? $" Уверенность: {detection.HeaderConfidence.Value:P0}."
            : string.Empty;
        return detection.Kind switch
        {
            IsoFieldRoleDetectionKind.FileNameAndHeader => new RoleDetectionPresentation(
                "Роль: имя + заголовок",
                $"Имя файла и растровый заголовок совпадают: {detection.HeaderRole}.{confidence}",
                TrueBimBrushes.Success),
            IsoFieldRoleDetectionKind.Header => new RoleDetectionPresentation(
                "Роль: по заголовку",
                $"Имя файла не содержит роли; распознано в заголовке: {detection.HeaderRole}.{confidence}",
                TrueBimBrushes.Success),
            IsoFieldRoleDetectionKind.FileName => new RoleDetectionPresentation(
                "Роль: только по имени",
                $"Заголовок не распознан; используется роль из имени файла: {detection.FileNameRole}. Проверьте назначение.",
                TrueBimBrushes.Warning),
            IsoFieldRoleDetectionKind.Conflict => new RoleDetectionPresentation(
                "Конфликт имени и заголовка",
                $"Имя файла: {detection.FileNameRole}; заголовок: {detection.HeaderRole}.{confidence} Выберите слой вручную.",
                TrueBimBrushes.Danger),
            IsoFieldRoleDetectionKind.Manual => new RoleDetectionPresentation(
                "Роль: назначена вручную",
                "Пользователь вручную подтвердил расчётный слой. При смене исходника проверьте назначение заново.",
                TrueBimBrushes.Warning),
            IsoFieldRoleDetectionKind.Manifest => new RoleDetectionPresentation(
                "Роль: из manifest",
                "Назначение восстановлено из проверенного manifest комплекта.",
                TrueBimBrushes.Success),
            _ => new RoleDetectionPresentation(
                "Роль не определена",
                "Роль не найдена ни в имени файла, ни в растровом заголовке. Выберите слой вручную.",
                TrueBimBrushes.Danger)
        };
    }

    private static WpfGrid CreateSourceSetHeader()
    {
        WpfGrid header = new()
        {
            Margin = new Thickness(TrueBimTheme.Spacing8, 0, TrueBimTheme.Spacing8, TrueBimTheme.Spacing4)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });

        TextBlock fileHeader = CreateMutedText("Карта и размер");
        fileHeader.FontWeight = FontWeights.SemiBold;
        fileHeader.Margin = new Thickness(0);
        WpfGrid.SetColumnSpan(fileHeader, 2);
        header.Children.Add(fileHeader);

        TextBlock roleHeader = CreateMutedText("Слой");
        roleHeader.FontWeight = FontWeights.SemiBold;
        roleHeader.Margin = new Thickness(0);
        WpfGrid.SetColumn(roleHeader, 2);
        header.Children.Add(roleHeader);

        TextBlock directionHeader = CreateMutedText("Ось");
        directionHeader.FontWeight = FontWeights.SemiBold;
        directionHeader.Margin = new Thickness(0);
        directionHeader.HorizontalAlignment = HorizontalAlignment.Center;
        WpfGrid.SetColumn(directionHeader, 3);
        header.Children.Add(directionHeader);

        TextBlock faceHeader = CreateMutedText("Грань");
        faceHeader.FontWeight = FontWeights.SemiBold;
        faceHeader.Margin = new Thickness(0);
        WpfGrid.SetColumn(faceHeader, 4);
        header.Children.Add(faceHeader);
        return header;
    }

    private void AssignSourceRole(string filePath, IsoFieldLayerRole role)
    {
        if (selectedSourceSet is null)
        {
            return;
        }

        selectedSourceSet = sourceSetService.AssignRole(selectedSourceSet, filePath, role);
        isSourceSetManifestDirty = true;
        UpdateSourceSetPresentation();
        ClearPreview("Назначение слоя изменено. Запустите обработку комплекта заново.");
        footerStatusText.Text = selectedSourceSet.IsComplete
            ? "Назначение слоёв исправлено; комплект готов."
            : "Назначение изменено, но комплект пока не готов.";
        logger.Info(
            $"IsoField source role assigned. File={Path.GetFileName(filePath)}; Role={role}; "
            + $"Complete={selectedSourceSet.IsComplete}.");
    }

    private void AssignSourceFace(IsoFieldLayerRole role, IsoFieldRebarFace face)
    {
        if (selectedSourceSet is null)
        {
            return;
        }

        selectedSourceSet = sourceSetService.AssignFace(selectedSourceSet, role, face);
        isSourceSetManifestDirty = true;
        UpdateLayerMappingStatus();
        UpdateManifestStatus();
        if (currentRulePreview is not null)
        {
            ClearRulePreview("Назначение верх/низ изменено. Рассчитайте раскладку заново.");
        }

        RefreshWorkflowState();
        footerStatusText.Text = selectedSourceSet.HasConfirmedLayerMappings
            ? "Назначение верх/низ подтверждено для всех слоёв."
            : "Назначение грани изменено; заполните оставшиеся слои.";
        logger.Info(
            $"IsoField layer face assigned. Role={role}; Face={face}; "
            + $"MappingsConfirmed={selectedSourceSet.HasConfirmedLayerMappings}.");
    }

    private static ImageSource? LoadSourceThumbnail(string filePath)
    {
        try
        {
            using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            BitmapImage image = new();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 192;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            return null;
        }
    }

    private static string FormatSourceSetIssues(IsoFieldSourceSet sourceSet)
    {
        return sourceSet.ValidationMessages.Count == 0
            ? "Назначьте каждому изображению уникальный слой."
            : string.Join(" ", sourceSet.ValidationMessages);
    }

    private void ReadJsonSource(string path)
    {
        logger.Info($"IsoField JSON source read started: {Path.GetFileName(path)}.");
        IsoFieldRecognitionResult result = jsonReader.Read(path);
        currentRecognitionResult = result;
        ResetSlabBindingForSource(result);
        recognitionStatusText.Text = $"JSON прочитан. Контуров: {result.Polylines.Count}. Диагностик: {result.Diagnostics.Count}.";
        recognitionStatusText.ToolTip = CreateRecognitionDiagnosticsToolTip(result);
        UpdateLegendPresentation(result);
        RenderPreview(result);
        ClearRulePreview("Нажмите «Рассчитать раскладку» после выбора host-элемента.");
        footerStatusText.Text = "JSON-контракт изополей прочитан. Модель Revit не изменялась.";
        logger.Info($"IsoField recognition JSON read. Polylines: {result.Polylines.Count}, diagnostics: {result.Diagnostics.Count}.");
    }

    private void RunRecognition()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(selectedJsonPath))
            {
                ReadJsonSource(selectedJsonPath!);
                return;
            }

            if (selectedSourceSet?.IsComplete != true)
            {
                logger.Warning("IsoField recognition was requested without a complete source set.");
                TaskDialog.Show(
                    "Армирование по изополям",
                    "Выберите четыре изображения и назначьте уникальные слои As1X, As2X, As3Y, As4Y.");
                footerStatusText.Text = "Обработка не запущена: комплект не готов.";
                return;
            }

            logger.Info(
                $"IsoField source set recognition started. Runner={ResolveRecognitionRunnerName()}; "
                + $"Files={selectedSourceSet.Files.Count}.");
            IsoFieldRecognitionResult result = sourceSetRecognitionService.Run(selectedSourceSet, recognitionRunner);
            currentRecognitionResult = result;
            ResetSlabBindingForSource(result);
            recognitionStatusText.Text = $"Обработано 4 слоя. Контуров: {result.Polylines.Count}. Легенд: {result.EffectiveLegends.Count} из 4. Диагностик: {result.Diagnostics.Count}.";
            recognitionStatusText.ToolTip = CreateRecognitionDiagnosticsToolTip(result);
            UpdateLegendPresentation(result);
            RenderPreview(result);
            ClearRulePreview(result.Polylines.Count == 0
                ? "Правила не рассчитаны: результат распознавания не содержит зон."
                : "Нажмите «Рассчитать раскладку» после выбора host-элемента.");
            footerStatusText.Text = "Распознавание завершено. Модель Revit не изменялась.";
            logger.Info(
                $"IsoField source set recognition completed. Polylines={result.Polylines.Count}; "
                + $"Legends={result.EffectiveLegends.Count}; Diagnostics={result.Diagnostics.Count}.");
        }
        catch (Exception exception)
        {
            logger.Error("Failed to run IsoField recognition.", exception);
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось выполнить распознавание. Используйте логи для диагностики.");
            footerStatusText.Text = "Не удалось выполнить распознавание.";
        }
    }

    private void ShowRevitPreview()
    {
        footerStatusText.Text = "Предпросмотр поставлен в очередь Revit.";
        revitActions.Raise(ShowRevitPreviewInRevitContext);
    }

    private void CorrectZones()
    {
        if (currentRecognitionResult is null || currentRecognitionResult.Polylines.Count == 0)
        {
            footerStatusText.Text = "Коррекция недоступна: сначала загрузите зоны.";
            logger.Warning("IsoField zone correction was requested without recognition polylines.");
            return;
        }

        int sourceCount = currentRecognitionResult.Polylines.Count;
        bool hadSlabBinding = currentSlabBinding is not null;
        IsoFieldZoneCorrectionWindow correctionWindow = new(currentRecognitionResult)
        {
            Owner = this
        };
        if (correctionWindow.ShowDialog() != true || correctionWindow.Result is null)
        {
            footerStatusText.Text = "Коррекция зон отменена. Текущий результат не изменён.";
            logger.Info("IsoField zone correction canceled.");
            return;
        }

        currentRecognitionResult = correctionWindow.Result;
        recognitionStatusText.Text = $"Зоны проверены вручную. Было: {sourceCount}; стало: {currentRecognitionResult.Polylines.Count}. Диагностик: {currentRecognitionResult.Diagnostics.Count}.";
        recognitionStatusText.ToolTip = CreateRecognitionDiagnosticsToolTip(currentRecognitionResult);
        UpdateLegendPresentation(currentRecognitionResult);
        if (hadSlabBinding)
        {
            ApplySlabBinding(showDialogOnError: false, renderPreview: false);
        }
        else
        {
            currentSlabBinding = null;
        }

        RenderPreview(currentRecognitionResult);
        ClearRulePreview("Правила сброшены после коррекции зон. Рассчитайте их заново для выбранного host-элемента.");
        footerStatusText.Text = activeRevitPreviewIds.Count > 0
            ? "Зоны обновлены. Повторно нажмите «Показать в Revit», чтобы заменить старые линии preview."
            : "Зоны обновлены в текущем результате. Модель Revit не изменялась.";
        logger.Info(
            $"IsoField zone correction applied. SourcePolylines={sourceCount}; "
            + $"ResultPolylines={currentRecognitionResult.Polylines.Count}; ActiveRevitPreviewIds={activeRevitPreviewIds.Count}.");
    }

    private void ShowRevitPreviewInRevitContext()
    {
        if (uiDocument is null)
        {
            logger.Warning("IsoField Revit preview was requested without an open Revit document.");
            TaskDialog.Show("Армирование по изополям", "Откройте документ Revit перед созданием линий предпросмотра.");
            return;
        }

        if (currentRecognitionResult is null || currentRecognitionResult.Polylines.Count == 0)
        {
            logger.Warning("IsoField Revit preview was requested without recognition polylines.");
            TaskDialog.Show("Армирование по изополям", "Сначала выберите JSON-файл с контурами изополей.");
            return;
        }

        try
        {
            if (!ApplyCalibration(showDialogOnError: true))
            {
                return;
            }

            logger.Info($"IsoField Revit preview requested. Polylines={currentRecognitionResult.Polylines.Count}; ExistingPreviewIds={activeRevitPreviewIds.Count}; CalibrationScale={currentCalibration.MillimetersPerPixel}.");
            IsoFieldRevitPreviewResult result = revitPreviewService.Show(
                uiDocument,
                currentRecognitionResult,
                activeRevitPreviewIds,
                currentCalibration);
            activeRevitPreviewIds = result.CreatedElementIds;
            footerStatusText.Text = result.Message;
            RefreshWorkflowState();
            logger.Info($"IsoField Revit preview command completed. Created={result.CreatedCount}; Deleted={result.DeletedCount}.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or Autodesk.Revit.Exceptions.ApplicationException or Autodesk.Revit.Exceptions.ArgumentException)
        {
            logger.Error("Failed to create IsoField Revit preview lines.", exception);
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось создать линии предпросмотра в Revit. Используйте 2D-вид и логи для диагностики.");
            footerStatusText.Text = "Не удалось создать линии предпросмотра в Revit.";
        }
    }

    private void ClearRevitPreview()
    {
        footerStatusText.Text = "Очистка предпросмотра поставлена в очередь Revit.";
        revitActions.Raise(ClearRevitPreviewInRevitContext);
    }

    private void ClearRevitPreviewInRevitContext()
    {
        if (uiDocument is null)
        {
            logger.Warning("IsoField Revit preview clear was requested without an open Revit document.");
            TaskDialog.Show("Армирование по изополям", "Откройте документ Revit перед очисткой линий предпросмотра.");
            return;
        }

        try
        {
            logger.Info($"IsoField Revit preview clear requested. ExistingPreviewIds={activeRevitPreviewIds.Count}.");
            IsoFieldRevitPreviewResult result = revitPreviewService.Clear(uiDocument, activeRevitPreviewIds);
            activeRevitPreviewIds = Array.Empty<ElementId>();
            footerStatusText.Text = result.Message;
            RefreshWorkflowState();
            logger.Info($"IsoField Revit preview clear completed. Deleted={result.DeletedCount}.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or Autodesk.Revit.Exceptions.ApplicationException or Autodesk.Revit.Exceptions.ArgumentException)
        {
            logger.Error("Failed to clear IsoField Revit preview lines.", exception);
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось очистить линии предпросмотра в Revit. Используйте логи для диагностики.");
            footerStatusText.Text = "Не удалось очистить линии предпросмотра в Revit.";
        }
    }

    private void SelectHostElement()
    {
        footerStatusText.Text = "Выбор host-элемента поставлен в очередь Revit.";
        revitActions.Raise(SelectHostElementInRevitContext);
    }

    private void SelectHostElementInRevitContext()
    {
        if (uiDocument is null)
        {
            logger.Warning("IsoField host selection was requested without an open Revit document.");
            TaskDialog.Show("Армирование по изополям", "Откройте документ Revit перед выбором host-элемента.");
            return;
        }

        Visibility previousVisibility = Visibility;
        try
        {
            Visibility = Visibility.Hidden;
            IsoFieldHostElement hostElement = hostSelectionService.PickHost(uiDocument);
            selectedHostElement = hostElement;
            selectedHostViewId = RevitElementIds.GetValue(uiDocument.ActiveView.Id);
            ResetSlabBindingForHost();
            RefreshHostStatus();
            ClearRulePreview("Нажмите «Рассчитать раскладку» для выбранного host-элемента.");
            footerStatusText.Text = $"Host-элемент выбран: {hostElement.DisplayName}. Модель Revit не изменялась.";
            logger.Info($"IsoField host selected. Kind={hostElement.HostKind}; ElementId={hostElement.ElementId}; Name='{hostElement.Name}'.");
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            footerStatusText.Text = "Выбор host-элемента отменен.";
            logger.Info("IsoField host selection canceled.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or Autodesk.Revit.Exceptions.ApplicationException or Autodesk.Revit.Exceptions.ArgumentException)
        {
            logger.Error("Failed to select IsoField host element.", exception);
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось выбрать host-элемент. Выберите стену или плиту и используйте логи для диагностики.");
            footerStatusText.Text = "Не удалось выбрать host-элемент.";
        }
        finally
        {
            Visibility = previousVisibility;
            Activate();
        }
    }

    private void ClearHostElement()
    {
        selectedHostElement = null;
        selectedHostViewId = 0;
        ResetSlabBindingForHost();
        RefreshHostStatus();
        ClearRulePreview("Правила не рассчитаны: host-элемент сброшен.");
        footerStatusText.Text = "Host-элемент сброшен. Модель Revit не изменялась.";
        logger.Info("IsoField host selection cleared.");
    }

    private void RefreshHostStatus()
    {
        hostStatusText.Text = selectedHostElement is null
            ? "Host-элемент не выбран."
            : selectedHostElement.IsSlab && selectedHostElement.Geometry is null
                ? $"{selectedHostElement.DisplayName}. Горизонтальная верхняя грань не распознана."
                : selectedHostElement.DisplayName;
    }

    private void PickSlabControlPoint(int pointNumber)
    {
        footerStatusText.Text = $"Выбор контрольной точки {pointNumber} поставлен в очередь Revit.";
        revitActions.Raise(() => PickSlabControlPointInRevitContext(pointNumber));
    }

    private void PickSlabControlPointInRevitContext(int pointNumber)
    {
        if (uiDocument is null || selectedHostElement is null)
        {
            SetSlabBindingStatus(
                "Сначала выберите плиту в открытом документе Revit.",
                TrueBimUiSeverity.Warning);
            return;
        }

        Visibility previousVisibility = Visibility;
        try
        {
            Visibility = Visibility.Hidden;
            IsoFieldPoint point = hostSelectionService.PickSlabControlPoint(
                uiDocument,
                selectedHostElement,
                pointNumber);
            if (pointNumber == 1)
            {
                slabHostPoint1Feet = point;
                slabHostPoint1Text.Text = FormatSlabHostPoint(1, point);
            }
            else if (pointNumber == 2)
            {
                slabHostPoint2Feet = point;
                slabHostPoint2Text.Text = FormatSlabHostPoint(2, point);
            }
            else
            {
                slabHostPoint3Feet = point;
                slabHostPoint3Text.Text = FormatSlabHostPoint(3, point);
            }

            InvalidateSlabBinding();
            SetSlabBindingStatus(
                $"Точка {pointNumber} сохранена. Укажите оставшиеся точки или нажмите «Проверить привязку».",
                TrueBimUiSeverity.Info);
            footerStatusText.Text = $"Контрольная точка {pointNumber} выбрана. Модель Revit не изменялась.";
            logger.Info(
                $"IsoField slab control point selected. Point={pointNumber}; "
                + $"LocalFeet=({point.X}; {point.Y}); HostId={selectedHostElement.ElementId}.");
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            footerStatusText.Text = $"Выбор контрольной точки {pointNumber} отменён.";
            logger.Info($"IsoField slab control point selection canceled. Point={pointNumber}.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or Autodesk.Revit.Exceptions.ApplicationException or Autodesk.Revit.Exceptions.ArgumentException)
        {
            logger.Error($"Failed to select IsoField slab control point {pointNumber}.", exception);
            SetSlabBindingStatus(exception.Message, TrueBimUiSeverity.Danger);
            footerStatusText.Text = $"Не удалось выбрать контрольную точку {pointNumber}.";
        }
        finally
        {
            Visibility = previousVisibility;
            Activate();
            RefreshWorkflowState();
        }
    }

    private bool ApplySlabBinding(bool showDialogOnError, bool renderPreview = true)
    {
        if (currentRecognitionResult is null
            || selectedHostElement?.IsSlab != true
            || selectedHostElement.Geometry is null
            || slabHostPoint1Feet is null
            || slabHostPoint2Feet is null
            || slabHostPoint3Feet is null)
        {
            string message = "Для проверки нужны зоны, горизонтальная плита и три контрольные точки на её верхней грани.";
            SetSlabBindingStatus(message, TrueBimUiSeverity.Warning);
            if (showDialogOnError)
            {
                TaskDialog.Show("Армирование по изополям", message);
            }

            RefreshWorkflowState();
            return false;
        }

        if (!TryBuildSlabBindingInput(out IsoFieldSlabBindingInput input, out string errorMessage))
        {
            SetSlabBindingStatus(errorMessage, TrueBimUiSeverity.Danger);
            if (showDialogOnError)
            {
                TaskDialog.Show("Армирование по изополям", errorMessage);
            }

            RefreshWorkflowState();
            return false;
        }

        try
        {
            currentSlabBinding = slabBindingService.Analyze(
                currentRecognitionResult,
                selectedHostElement.Geometry,
                input);
            string status = currentSlabBinding.CanProceed
                ? $"Привязка проверена. Отклонение точки 3: {FormatNumber(currentSlabBinding.ThirdPointDeviationMillimeters)} мм; обрезано зон: {currentSlabBinding.ClippedZoneIds.Count}."
                : currentSlabBinding.RemovedZoneIds.Count > 0
                    ? $"Привязка заблокирована: полностью вне плиты осталось зон {currentSlabBinding.RemovedZoneIds.Count}."
                    : "Привязка заблокирована: проверьте третью точку и допустимую область плиты.";
            SetSlabBindingStatus(
                status,
                !currentSlabBinding.CanProceed
                    ? TrueBimUiSeverity.Danger
                    : currentSlabBinding.ClippedZoneIds.Count > 0
                        ? TrueBimUiSeverity.Warning
                        : TrueBimUiSeverity.Success,
                string.Join(Environment.NewLine, currentSlabBinding.Diagnostics));
            if (renderPreview)
            {
                RenderPreview(currentRecognitionResult);
            }

            ClearRulePreview(currentSlabBinding.CanProceed
                ? "Привязка плиты проверена. Рассчитайте правила заново."
                : "Правила заблокированы: исправьте привязку зон к плите.");
            footerStatusText.Text = currentSlabBinding.CanProceed
                ? "Read-only overlay зон и плиты построен. Модель Revit не изменялась."
                : "Привязка требует исправления. Модель Revit не изменялась.";
            logger.Info(
                $"IsoField slab binding analyzed. CanProceed={currentSlabBinding.CanProceed}; "
                + $"OutsideZones={currentSlabBinding.OutsideZoneCount}; "
                + $"RetainedAreaRatio={currentSlabBinding.RetainedAreaRatio}; "
                + $"ClippedZones={currentSlabBinding.ClippedZoneIds.Count}; "
                + $"RemovedZones={currentSlabBinding.RemovedZoneIds.Count}; "
                + $"ThirdPointDeviationMm={currentSlabBinding.ThirdPointDeviationMillimeters}; "
                + $"ScaleMmPerPixel={currentSlabBinding.Transform.MillimetersPerPixel}; "
                + $"RotationDegrees={currentSlabBinding.Transform.RotationDegrees}.");
            return currentSlabBinding.CanProceed;
        }
        catch (InvalidOperationException exception)
        {
            currentSlabBinding = null;
            SetSlabBindingStatus(exception.Message, TrueBimUiSeverity.Danger);
            if (showDialogOnError)
            {
                TaskDialog.Show("Армирование по изополям", exception.Message);
            }

            RefreshWorkflowState();
            return false;
        }
    }

    private bool TryBuildSlabBindingInput(
        out IsoFieldSlabBindingInput input,
        out string errorMessage)
    {
        input = new IsoFieldSlabBindingInput(
            new IsoFieldPoint(0, 0),
            new IsoFieldPoint(0, 0),
            slabHostPoint1Feet ?? new IsoFieldPoint(0, 0),
            slabHostPoint2Feet ?? new IsoFieldPoint(0, 0),
            slabMirrorImageYInput.IsChecked == true,
            new IsoFieldPoint(0, 0),
            slabHostPoint3Feet ?? new IsoFieldPoint(0, 0));
        if (!TryReadDouble(slabImagePoint1XInput, "Точка 1 / X", out double point1X, out errorMessage)
            || !TryReadDouble(slabImagePoint1YInput, "Точка 1 / Y", out double point1Y, out errorMessage)
            || !TryReadDouble(slabImagePoint2XInput, "Точка 2 / X", out double point2X, out errorMessage)
            || !TryReadDouble(slabImagePoint2YInput, "Точка 2 / Y", out double point2Y, out errorMessage)
            || !TryReadDouble(slabImagePoint3XInput, "Точка 3 / X", out double point3X, out errorMessage)
            || !TryReadDouble(slabImagePoint3YInput, "Точка 3 / Y", out double point3Y, out errorMessage))
        {
            return false;
        }

        input = input with
        {
            ImagePoint1 = new IsoFieldPoint(point1X, point1Y),
            ImagePoint2 = new IsoFieldPoint(point2X, point2Y),
            ImagePoint3 = new IsoFieldPoint(point3X, point3Y)
        };
        errorMessage = string.Empty;
        return true;
    }

    private void LoadSlabBindingProfile()
    {
        if (availableSlabBindingProfile is null)
        {
            SetSlabBindingStatus(
                "Для текущего документа, вида и плиты сохранённый профиль не найден.",
                TrueBimUiSeverity.Warning);
            return;
        }

        IsoFieldSlabBindingInput binding = availableSlabBindingProfile.Binding;
        slabImagePoint1XInput.Text = FormatNumber(binding.ImagePoint1.X);
        slabImagePoint1YInput.Text = FormatNumber(binding.ImagePoint1.Y);
        slabImagePoint2XInput.Text = FormatNumber(binding.ImagePoint2.X);
        slabImagePoint2YInput.Text = FormatNumber(binding.ImagePoint2.Y);
        slabImagePoint3XInput.Text = FormatNumber(binding.ImagePoint3!.X);
        slabImagePoint3YInput.Text = FormatNumber(binding.ImagePoint3.Y);
        slabMirrorImageYInput.IsChecked = binding.MirrorImageY;
        slabHostPoint1Feet = binding.HostPoint1Feet;
        slabHostPoint2Feet = binding.HostPoint2Feet;
        slabHostPoint3Feet = binding.HostPoint3Feet;
        slabHostPoint1Text.Text = FormatSlabHostPoint(1, binding.HostPoint1Feet);
        slabHostPoint2Text.Text = FormatSlabHostPoint(2, binding.HostPoint2Feet);
        slabHostPoint3Text.Text = FormatSlabHostPoint(3, binding.HostPoint3Feet!);
        currentSlabBinding = null;
        bool isValid = ApplySlabBinding(showDialogOnError: false);
        footerStatusText.Text = isValid
            ? "Профиль загружен и повторно проверен на текущих зонах. Модель Revit не изменялась."
            : "Профиль загружен, но не прошёл повторную проверку на текущих зонах.";
        logger.Info(
            $"IsoField slab binding profile loaded. HostId={availableSlabBindingProfile.HostElementId}; "
            + $"ViewId={availableSlabBindingProfile.ViewId}; Valid={isValid}.");
    }

    private void SaveSlabBindingProfile()
    {
        if (currentSlabBinding?.CanProceed != true
            || selectedHostElement?.IsSlab != true)
        {
            SetSlabBindingStatus(
                "Сначала выполните успешную проверку привязки по трём точкам.",
                TrueBimUiSeverity.Warning);
            return;
        }

        if (!TryBuildSlabBindingInput(out IsoFieldSlabBindingInput binding, out string errorMessage))
        {
            SetSlabBindingStatus(errorMessage, TrueBimUiSeverity.Warning);
            return;
        }

        IsoFieldSlabBindingProfile profile = new(
            documentKey,
            selectedHostViewId,
            selectedHostElement.ElementId,
            selectedHostElement.DisplayName,
            binding,
            DateTimeOffset.UtcNow);
        try
        {
            slabBindingProfileStorage.Save(profile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Error("Failed to save IsoField slab binding profile.", exception);
            SetSlabBindingStatus(
                "Не удалось сохранить профиль привязки. Проверьте доступ к папке настроек и лог TrueBIM.",
                TrueBimUiSeverity.Danger);
            return;
        }

        availableSlabBindingProfile = profile;
        SetSlabBindingStatus(
            $"Профиль сохранён для вида {selectedHostViewId} и плиты {selectedHostElement.ElementId}.",
            TrueBimUiSeverity.Success,
            slabBindingProfileStorage.SettingsPath);
        footerStatusText.Text = "Профиль привязки сохранён. Модель Revit не изменялась.";
        logger.Info(
            $"IsoField slab binding profile saved. HostId={profile.HostElementId}; "
            + $"ViewId={profile.ViewId}; Path='{slabBindingProfileStorage.SettingsPath}'.");
        RefreshWorkflowState();
    }

    private void InvalidateSlabBinding()
    {
        if (currentSlabBinding is null)
        {
            RefreshWorkflowState();
            return;
        }

        currentSlabBinding = null;
        if (currentRecognitionResult is not null)
        {
            RenderPreview(currentRecognitionResult);
        }

        ClearRulePreview("Параметры привязки изменены. Проверьте overlay заново.");
        SetSlabBindingStatus(
            "Параметры изменены. Нажмите «Проверить привязку» заново.",
            TrueBimUiSeverity.Warning);
    }

    private void ResetSlabBindingForSource(IsoFieldRecognitionResult? result)
    {
        currentSlabBinding = null;
        slabHostPoint1Feet = null;
        slabHostPoint2Feet = null;
        slabHostPoint3Feet = null;
        slabHostPoint1Text.Text = "Точка 1 на плите не указана.";
        slabHostPoint2Text.Text = "Точка 2 на плите не указана.";
        slabHostPoint3Text.Text = "Точка 3 на плите не указана.";
        if (result?.Polylines.Count > 0)
        {
            IsoFieldPoint[] points = result.Polylines.SelectMany(polyline => polyline.Points).ToArray();
            double minX = points.Min(point => point.X);
            double maxX = points.Max(point => point.X);
            double minY = points.Min(point => point.Y);
            double maxY = points.Max(point => point.Y);
            slabImagePoint1XInput.Text = FormatNumber(minX);
            slabImagePoint1YInput.Text = FormatNumber(minY);
            slabImagePoint2XInput.Text = FormatNumber(maxX > minX ? maxX : minX + 100);
            slabImagePoint2YInput.Text = FormatNumber(minY);
            slabImagePoint3XInput.Text = FormatNumber(minX);
            slabImagePoint3YInput.Text = FormatNumber(maxY > minY ? maxY : minY + 100);
        }

        string status = (result?.Polylines.Count > 0, selectedHostElement) switch
        {
            (false, { IsSlab: true }) => "Загрузите или распознайте зоны, затем задайте три пары контрольных точек.",
            (true, { IsSlab: true }) when availableSlabBindingProfile is not null => "Зоны загружены. Загрузите сохранённый профиль или задайте три точки заново.",
            (true, { IsSlab: true }) => "Укажите три соответствующие точки на верхней грани выбранной плиты.",
            _ => "Выберите горизонтальную плиту, затем задайте три пары контрольных точек."
        };
        SetSlabBindingStatus(status, TrueBimUiSeverity.Info);
    }

    private void ResetSlabBindingForHost()
    {
        currentSlabBinding = null;
        slabHostPoint1Feet = null;
        slabHostPoint2Feet = null;
        slabHostPoint3Feet = null;
        slabHostPoint1Text.Text = "Точка 1 на плите не указана.";
        slabHostPoint2Text.Text = "Точка 2 на плите не указана.";
        slabHostPoint3Text.Text = "Точка 3 на плите не указана.";
        availableSlabBindingProfile = selectedHostElement is { IsSlab: true, Geometry: not null }
            ? slabBindingProfileStorage.TryLoad(
                documentKey,
                selectedHostViewId,
                selectedHostElement.ElementId)
            : null;
        string status = selectedHostElement switch
        {
            null => "Выберите горизонтальную плиту, затем задайте три пары контрольных точек.",
            { IsSlab: false } => "Для стены трёхточечная привязка плиты не требуется.",
            { Geometry: null } => "У плиты не распознана горизонтальная верхняя грань; привязка и расчёт правил заблокированы.",
            _ when availableSlabBindingProfile is not null => "Для этой плиты и вида найден сохранённый профиль. Загрузите его или задайте три точки заново.",
            _ => "Плита готова. Укажите три соответствующие точки на её верхней грани."
        };
        SetSlabBindingStatus(
            status,
            selectedHostElement?.IsSlab == true && selectedHostElement.Geometry is null
                ? TrueBimUiSeverity.Danger
                : TrueBimUiSeverity.Info);
        if (currentRecognitionResult is not null)
        {
            RenderPreview(currentRecognitionResult);
        }
    }

    private void SetSlabBindingStatus(
        string message,
        TrueBimUiSeverity severity,
        string? toolTip = null)
    {
        slabBindingStatusText.Text = message;
        slabBindingStatusText.Foreground = TrueBimBrushes.ForSeverity(severity);
        slabBindingStatusText.ToolTip = toolTip;
    }

    private static string FormatSlabHostPoint(int pointNumber, IsoFieldPoint point)
    {
        return $"Точка {pointNumber} на плите: X={FormatNumber(point.X * 304.8)} мм; Y={FormatNumber(point.Y * 304.8)} мм.";
    }

    private bool ApplyCalibration(bool showDialogOnError)
    {
        if (!TryBuildCalibration(out IsoFieldCalibration calibration, out string errorMessage))
        {
            logger.Warning($"IsoField calibration validation failed: {errorMessage}");
            if (showDialogOnError)
            {
                TaskDialog.Show("Армирование по изополям", errorMessage);
            }

            footerStatusText.Text = "Калибровка не применена.";
            return false;
        }

        currentCalibration = calibration;
        RefreshCalibrationStatus();
        footerStatusText.Text = "Калибровка применена. Модель Revit не изменялась.";
        logger.Info($"IsoField calibration applied. Anchor=({calibration.ImageAnchor.X}; {calibration.ImageAnchor.Y}); MillimetersPerPixel={calibration.MillimetersPerPixel}; InvertY={calibration.InvertImageY}.");
        return true;
    }

    private bool TryBuildCalibration(out IsoFieldCalibration calibration, out string errorMessage)
    {
        calibration = currentCalibration;
        if (!TryReadDouble(calibrationAnchorXInput, "Якорь X", out double anchorX, out errorMessage)
            || !TryReadDouble(calibrationAnchorYInput, "Якорь Y", out double anchorY, out errorMessage)
            || !TryReadDouble(calibrationMillimetersPerPixelInput, "Мм/пикс", out double millimetersPerPixel, out errorMessage))
        {
            return false;
        }

        calibration = new IsoFieldCalibration(
            new IsoFieldPoint(anchorX, anchorY),
            0,
            0,
            millimetersPerPixel,
            calibrationInvertYInput.IsChecked == true);

        try
        {
            coordinateMapper.Validate(calibration);
            errorMessage = string.Empty;
            return true;
        }
        catch (InvalidOperationException exception)
        {
            errorMessage = exception.Message;
            return false;
        }
    }

    private void RefreshCalibrationStatus()
    {
        calibrationStatusText.Text = FormatCalibration(currentCalibration);
    }

    private void PreviewRebarRules()
    {
        if (currentRecognitionResult is null || currentRecognitionResult.Polylines.Count == 0)
        {
            logger.Warning("IsoField rebar rules preview was requested without recognition polylines.");
            TaskDialog.Show("Армирование по изополям", "Сначала выберите JSON-файл с контурами изополей.");
            ClearRulePreview("Правила не рассчитаны: нет контуров изополей.");
            return;
        }

        IsoFieldEngineeringSettings? engineeringSettings = null;
        if (selectedHostElement?.IsSlab == true
            && !TryBuildEngineeringSettings(out engineeringSettings, out string settingsError))
        {
            currentRulePreview = null;
            calculatedRulePreview = null;
            ruleOverrides.Clear();
            currentChangePlan = null;
            currentChangePlanFingerprint = null;
            RefreshRebarReviewRows();
            ruleStatusText.Text = settingsError;
            rebarCreationStatusText.Text = "Раскладка недоступна: исправьте инженерные параметры.";
            footerStatusText.Text = "Инженерные параметры раскладки требуют исправления.";
            logger.Warning($"IsoField engineering settings are invalid. {settingsError}");
            RefreshWorkflowState();
            return;
        }

        logger.Info($"IsoField rebar rules preview requested. Polylines={currentRecognitionResult.Polylines.Count}; HostSelected={selectedHostElement is not null}.");
        RebarRulePreviewResult preview = rebarRuleValidationService.BuildPreview(
            currentRecognitionResult,
            selectedHostElement,
            selectedSourceSet,
            currentSlabBinding,
            engineeringSettings);
        currentRulePreview = preview;
        calculatedRulePreview = preview;
        ruleOverrides.Clear();
        currentChangePlan = null;
        currentChangePlanFingerprint = null;
        RefreshRebarReviewRows();
        ruleStatusText.Text = FormatRulePreview(preview);
        bool mappingsReady = selectedSourceSet is null || selectedSourceSet.HasConfirmedLayerMappings;
        rebarCreationStatusText.Text = preview.CanCreateRebar && mappingsReady
            ? preview.IsEngineeringPreview
                ? $"Рассчитано {preview.EstimatedBarCount} стержней. Теперь нажмите «Сравнить с моделью» и проверьте таблицу."
                : "Готово к созданию пробного армирования после подтверждения."
            : preview.CanCreateRebar
                ? "Правила готовы, но назначение верх/низ не подтверждено."
                : "Армирование недоступно: проверьте диагностику правил и раскладки.";
        footerStatusText.Text = preview.CanCreateRebar && mappingsReady
            ? preview.IsEngineeringPreview
                ? $"Инженерная раскладка рассчитана: зон {preview.Items.Count}, стержней {preview.EstimatedBarCount}. Модель Revit не изменялась."
                : $"Правила армирования рассчитаны: {preview.Items.Count}. Модель Revit не изменялась."
            : preview.CanCreateRebar
                ? "Правила рассчитаны; подтвердите назначение верх/низ перед созданием."
                : "Правила армирования требуют проверки.";
        logger.Info($"IsoField rebar rules preview calculated. Items={preview.Items.Count}; EstimatedBars={preview.EstimatedBarCount}; Engineering={preview.IsEngineeringPreview}; Diagnostics={preview.Diagnostics.Count}; CanCreateRebar={preview.CanCreateRebar}.");
        if (currentSlabBinding is not null)
        {
            RenderPreview(currentRecognitionResult);
        }

        RefreshWorkflowState();
    }

    private bool TryBuildEngineeringSettings(
        out IsoFieldEngineeringSettings? settings,
        out string errorMessage)
    {
        settings = null;
        if (reinforcementModeInput.SelectedItem is not IsoFieldReinforcementModeOption modeOption)
        {
            errorMessage = "Выберите режим расчёта армирования.";
            return false;
        }

        if (!TryReadDouble(concreteCoverInput, "Защитный слой", out double cover, out errorMessage)
            || !TryReadDouble(boundaryOffsetInput, "Отступ от границ", out double boundaryOffset, out errorMessage)
            || !TryReadDouble(minimumBarLengthInput, "Минимальная длина", out double minimumLength, out errorMessage))
        {
            return false;
        }

        settings = new IsoFieldEngineeringSettings(
            modeOption.Mode,
            cover,
            boundaryOffset,
            minimumLength);
        errorMessage = string.Empty;
        return true;
    }

    private void InvalidateEngineeringRules()
    {
        if (currentRulePreview is null)
        {
            return;
        }

        ClearRulePreview("Инженерные параметры изменены. Рассчитайте раскладку заново.");
        RefreshWorkflowState();
    }

    private void CreateTestRebar()
    {
        rebarCreationStatusText.Text = "Применение изменений поставлено в очередь Revit.";
        revitActions.Raise(CreateTestRebarInRevitContext);
    }

    private void CompareEngineeringChanges()
    {
        rebarCreationStatusText.Text = "Сравнение с моделью поставлено в очередь Revit.";
        revitActions.Raise(CompareEngineeringChangesInRevitContext);
    }

    private void CompareEngineeringChangesInRevitContext()
    {
        ReadyRebarContext? context = ResolveReadyRebarContext();
        if (context is null)
        {
            return;
        }

        if (!context.Preview.IsEngineeringPreview)
        {
            TaskDialog.Show(
                "Армирование по изополям",
                "Сравнение по зонам доступно для инженерной раскладки горизонтальной плиты.");
            return;
        }

        IsoFieldRebarChangePlan? changePlan = TryPreviewEngineeringChanges(context);
        if (changePlan is null)
        {
            return;
        }

        SetCurrentChangePlan(changePlan);
        rebarCreationStatusText.Text = changePlan.CanApply
            ? "Сравнение с моделью: " + changePlan.Summary
            : "Изменения заблокированы: " + string.Join(" ", changePlan.Diagnostics);
        footerStatusText.Text = changePlan.CanApply
            ? "Раскладка сравнена с принадлежащей модулю арматурой. Проверьте таблицу; модель пока не изменялась."
            : "Сравнение выполнено, но план содержит ошибки.";
        logger.Info($"IsoField engineering rebar comparison completed. {changePlan.Summary} Diagnostics={changePlan.Diagnostics.Count}.");
    }

    private void CreateTestRebarInRevitContext()
    {
        ReadyRebarContext? context = ResolveReadyRebarContext();
        if (context is null)
        {
            return;
        }

        IsoFieldRebarChangePlan? changePlan = null;
        if (context.Preview.IsEngineeringPreview)
        {
            if (currentChangePlan is null || string.IsNullOrWhiteSpace(currentChangePlanFingerprint))
            {
                TaskDialog.Show(
                    "Армирование по изополям",
                    "Сначала нажмите «Сравнить с моделью» и проверьте таблицу зон и изменений.");
                rebarCreationStatusText.Text = "Изменения не применены: сравнение с моделью не выполнено.";
                return;
            }

            string expectedFingerprint = currentChangePlanFingerprint!;
            changePlan = TryPreviewEngineeringChanges(context);
            if (changePlan is null)
            {
                return;
            }

            string actualFingerprint = rebarChangePlanService.BuildFingerprint(changePlan);
            SetCurrentChangePlan(changePlan);
            if (!string.Equals(expectedFingerprint, actualFingerprint, StringComparison.Ordinal))
            {
                TaskDialog.Show(
                    "Армирование по изополям",
                    "Модель изменилась после последнего сравнения. Таблица обновлена; проверьте строки ещё раз перед применением.");
                rebarCreationStatusText.Text = "Изменения не применены: предыдущий результат сравнения устарел.";
                footerStatusText.Text = "Diff обновлён из модели. Требуется повторная проверка пользователем.";
                logger.Warning("IsoField engineering rebar apply blocked by a stale change-plan fingerprint.");
                return;
            }

            if (!changePlan.CanApply)
            {
                TaskDialog.Show(
                    "Армирование по изополям",
                    "Изменения заблокированы: " + string.Join(" ", changePlan.Diagnostics));
                rebarCreationStatusText.Text = "Изменения не применены: план содержит ошибки.";
                return;
            }

            if (!changePlan.HasChanges)
            {
                string message = $"Армирование уже соответствует расчётной раскладке. {changePlan.Summary}";
                TaskDialog.Show("Армирование по изополям", message);
                rebarCreationStatusText.Text = message;
                footerStatusText.Text = message;
                logger.Info($"IsoField engineering rebar apply skipped. {changePlan.Summary}");
                return;
            }
        }

        if (!ConfirmCreateTestRebar(context.Preview, context.HostElement, selectedSourceSet, changePlan))
        {
            rebarCreationStatusText.Text = "Применение изменений отменено.";
            footerStatusText.Text = "Применение изменений отменено пользователем.";
            logger.Info("IsoField test rebar creation canceled by user.");
            return;
        }

        try
        {
            logger.Info($"IsoField test rebar creation requested. HostKind={context.HostElement.HostKind}; HostId={context.HostElement.ElementId}; Rules={context.Preview.Items.Count}.");
            IsoFieldRebarCreationResult result = rebarCreationService.CreateTestRebar(
                uiDocument!,
                context.HostElement,
                context.Preview,
                currentSlabBinding);
            if (context.Preview.IsEngineeringPreview)
            {
                try
                {
                    IsoFieldRebarChangePlan appliedPlan = rebarCreationService.PreviewEngineeringChanges(
                        uiDocument!,
                        context.HostElement,
                        context.Preview,
                        currentSlabBinding);
                    SetCurrentChangePlan(appliedPlan);
                }
                catch (Exception exception) when (exception is InvalidOperationException or Autodesk.Revit.Exceptions.ApplicationException or Autodesk.Revit.Exceptions.ArgumentException)
                {
                    logger.Warning($"IsoField post-apply comparison failed: {exception.Message}");
                    SetCurrentChangePlan(null);
                }
            }

            rebarCreationStatusText.Text = result.Message;
            footerStatusText.Text = result.Message;
            logger.Info(
                $"IsoField rebar apply completed. Added={result.AddedCount}; Updated={result.UpdatedCount}; "
                + $"Deleted={result.DeletedCount}; Unchanged={result.UnchangedCount}; HostId={context.HostElement.ElementId}.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or Autodesk.Revit.Exceptions.ApplicationException or Autodesk.Revit.Exceptions.ArgumentException)
        {
            logger.Error("Failed to create IsoField test rebar.", exception);
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось создать армирование. Проверьте host-элемент, наличие точных диаметров RebarBarType в модели и логи.");
            rebarCreationStatusText.Text = "Армирование не создано: см. логи диагностики.";
            footerStatusText.Text = "Не удалось создать армирование.";
        }
    }

    private ReadyRebarContext? ResolveReadyRebarContext()
    {
        if (uiDocument is null)
        {
            logger.Warning("IsoField test rebar creation was requested without an open Revit document.");
            TaskDialog.Show("Армирование по изополям", "Откройте документ Revit перед созданием армирования.");
            return null;
        }

        if (selectedSourceSet is not null && !selectedSourceSet.HasConfirmedLayerMappings)
        {
            logger.Warning("IsoField test rebar creation was requested with unconfirmed layer mappings.");
            TaskDialog.Show(
                "Армирование по изополям",
                "Подтвердите назначение верх/низ для всех слоёв перед созданием армирования.");
            rebarCreationStatusText.Text = "Армирование не создано: назначение слоёв не подтверждено.";
            return null;
        }

        if (selectedHostElement is null)
        {
            logger.Warning("IsoField test rebar creation was requested without selected host element.");
            TaskDialog.Show("Армирование по изополям", "Сначала выберите стену или плиту как host-элемент.");
            rebarCreationStatusText.Text = "Армирование не создано: host-элемент не выбран.";
            return null;
        }

        if (currentRecognitionResult is null || currentRecognitionResult.Polylines.Count == 0)
        {
            logger.Warning("IsoField test rebar creation was requested without recognition polylines.");
            TaskDialog.Show("Армирование по изополям", "Сначала выберите JSON-файл с контурами изополей.");
            rebarCreationStatusText.Text = "Армирование не создано: нет контуров изополей.";
            return null;
        }

        RebarRulePreviewResult? preview = currentRulePreview;
        if (preview is null || !preview.CanCreateRebar)
        {
            PreviewRebarRules();
            preview = currentRulePreview;
        }

        if (preview is null || !preview.CanCreateRebar)
        {
            logger.Warning("IsoField test rebar creation blocked by invalid rule preview.");
            TaskDialog.Show("Армирование по изополям", "Перед созданием армирования исправьте диагностику правил и раскладки.");
            rebarCreationStatusText.Text = "Армирование не создано: правила не готовы.";
            return null;
        }

        return new ReadyRebarContext(preview, selectedHostElement);
    }

    private IsoFieldRebarChangePlan? TryPreviewEngineeringChanges(ReadyRebarContext context)
    {
        try
        {
            return rebarCreationService.PreviewEngineeringChanges(
                uiDocument!,
                context.HostElement,
                context.Preview,
                currentSlabBinding);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Autodesk.Revit.Exceptions.ApplicationException or Autodesk.Revit.Exceptions.ArgumentException)
        {
            logger.Error("Failed to preview IsoField engineering rebar changes.", exception);
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось сравнить раскладку с моделью. Проверьте host-элемент, точные диаметры RebarBarType и логи.");
            rebarCreationStatusText.Text = "Изменения не применены: сравнение с моделью не рассчитано.";
            footerStatusText.Text = "Не удалось рассчитать изменения армирования.";
            return null;
        }
    }

    private static bool ConfirmCreateTestRebar(
        RebarRulePreviewResult preview,
        IsoFieldHostElement hostElement,
        IsoFieldSourceSet? sourceSet,
        IsoFieldRebarChangePlan? changePlan)
    {
        RebarRulePreviewItem firstItem = preview.Items.First();
        bool isEngineering = preview.IsEngineeringPreview;
        string layerMappingText = sourceSet is null
            ? "Назначение слоёв: источник JSON."
            : "Назначение слоёв: " + string.Join(
                ", ",
                IsoFieldSourceSet.RequiredRoles.Select(role =>
                {
                    IsoFieldLayerMapping mapping = sourceSet.GetLayerMapping(role);
                    string face = mapping.Face == IsoFieldRebarFace.Bottom ? "низ" : "верх";
                    return $"{role}={face}";
                }));
        TaskDialog dialog = new("Армирование по изополям")
        {
            MainInstruction = isEngineering
                ? "Применить рассчитанные изменения армирования?"
                : "Создать пробное армирование в модели Revit?",
            MainContent = isEngineering
                ? BuildEngineeringConfirmationText(preview, hostElement, layerMappingText, firstItem, changePlan)
                : $"Host: {hostElement.DisplayName}{Environment.NewLine}{layerMappingText}{Environment.NewLine}Зон с правилами: {preview.Items.Count}{Environment.NewLine}Первое правило: {firstItem.DisplayName}{Environment.NewLine}Будет создано по одному пробному элементу на валидную зону. Действие изменит модель, но его можно отменить через Undo.",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No
        };

        return dialog.Show() == TaskDialogResult.Yes;
    }

    private static string BuildEngineeringConfirmationText(
        RebarRulePreviewResult preview,
        IsoFieldHostElement hostElement,
        string layerMappingText,
        RebarRulePreviewItem firstItem,
        IsoFieldRebarChangePlan? changePlan)
    {
        string mode = preview.EngineeringSettings?.Mode == IsoFieldReinforcementMode.AdditionalOverBase
            ? "Только дополнительное усиление. Первая сетка каждого сочетания считается уже существующей в модели."
            : "Полное сочетание внутри распознанных зон. Фоновая сетка вне зон не создаётся.";
        return $"Host: {hostElement.DisplayName}{Environment.NewLine}"
            + $"{layerMappingText}{Environment.NewLine}"
            + $"Режим: {mode}{Environment.NewLine}"
            + $"Зон: {preview.Items.Count}; отдельных стержней: {preview.EstimatedBarCount}.{Environment.NewLine}"
            + $"Изменения: {changePlan?.Summary ?? "diff не рассчитан"}{Environment.NewLine}"
            + $"Первое правило: {firstItem.DisplayName}{Environment.NewLine}"
            + "Модуль изменяет только элементы выбранной плиты с меткой TrueBIM и стабильным id; ручная арматура не затрагивается. Добавление, обновление и удаление выполняются одной транзакцией и отменяются через Undo.";
    }

    private void UpdateLegendPresentation(IsoFieldRecognitionResult result)
    {
        legendSummaryPanel.Children.Clear();
        foreach (IsoFieldLegend legend in result.EffectiveLegends.OrderBy(item => item.LayerRole))
        {
            StackPanel cardContent = new();
            cardContent.Children.Add(new TextBlock
            {
                Text = $"{legend.LayerRole?.ToString() ?? "Источник"} · {legend.Bands.Count} диапазонов",
                Foreground = TrueBimBrushes.TextPrimary,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing4)
            });
            cardContent.Children.Add(new TextBlock
            {
                Text = BuildLegendRangeSummary(legend),
                Foreground = legend.HasNumericRanges
                    ? TrueBimBrushes.TextSecondary
                    : TrueBimBrushes.TextMuted,
                FontSize = TrueBimTheme.CaptionFontSize,
                Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing4)
            });
            cardContent.Children.Add(new TextBlock
            {
                Text = BuildLegendReinforcementSummary(legend),
                Foreground = legend.HasReinforcementLabels
                    ? TrueBimBrushes.TextSecondary
                    : TrueBimBrushes.TextMuted,
                FontSize = TrueBimTheme.CaptionFontSize,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 280,
                Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
            });

            StackPanel swatches = new()
            {
                Orientation = Orientation.Horizontal
            };
            foreach (IsoFieldLegendBand band in legend.Bands)
            {
                SolidColorBrush fill = new(Color.FromRgb(band.Red, band.Green, band.Blue));
                fill.Freeze();
                swatches.Children.Add(new Border
                {
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 0, 2, 0),
                    Background = fill,
                    BorderBrush = TrueBimBrushes.Border,
                    BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
                    CornerRadius = new CornerRadius(2),
                    ToolTip = BuildLegendBandToolTip(legend, band)
                });
            }

            cardContent.Children.Add(swatches);
            legendSummaryPanel.Children.Add(new Border
            {
                Child = cardContent,
                Padding = new Thickness(TrueBimTheme.Spacing8),
                Margin = new Thickness(0, TrueBimTheme.Spacing4, TrueBimTheme.Spacing8, 0),
                Background = TrueBimBrushes.SurfaceAlt,
                BorderBrush = TrueBimBrushes.Border,
                BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
                CornerRadius = new CornerRadius(TrueBimTheme.Radius6)
            });
        }

        legendSummaryPanel.Visibility = legendSummaryPanel.Children.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static string BuildLegendRangeSummary(IsoFieldLegend legend)
    {
        if (!legend.HasNumericRanges)
        {
            return "Числа шкалы не распознаны";
        }

        IsoFieldLegendBand first = legend.Bands[0];
        IsoFieldLegendBand last = legend.Bands[legend.Bands.Count - 1];
        return $"{FormatNumber(first.MinimumValue!.Value)}–{FormatNumber(last.MaximumValue!.Value)} см²/м";
    }

    private static string BuildLegendReinforcementSummary(IsoFieldLegend legend)
    {
        if (!legend.HasReinforcementLabels)
        {
            return "Сочетания диаметр/шаг не распознаны";
        }

        IsoFieldLegendBoundary first = legend.EffectiveBoundaries[0];
        IsoFieldLegendBoundary last = legend.EffectiveBoundaries[legend.EffectiveBoundaries.Count - 1];
        return $"{legend.EffectiveBoundaries.Count} подписей · {first.ReinforcementLabel} … {last.ReinforcementLabel}";
    }

    private static string BuildLegendBandToolTip(IsoFieldLegend legend, IsoFieldLegendBand band)
    {
        string range = band.MinimumValue.HasValue && band.MaximumValue.HasValue
            ? $"{FormatNumber(band.MinimumValue.Value)}–{FormatNumber(band.MaximumValue.Value)} см²/м"
            : $"Уровень {band.Index + 1}; числовые границы не распознаны";
        string reinforcement = legend.HasReinforcementLabels
            ? $"Границы: {legend.EffectiveBoundaries[band.Index].ReinforcementLabel} → {legend.EffectiveBoundaries[band.Index + 1].ReinforcementLabel}"
            : "Сочетания диаметр/шаг не распознаны";
        return $"{range}{Environment.NewLine}{reinforcement}{Environment.NewLine}Цвет: {band.HexColor}";
    }

    private void ResetLegendPresentation()
    {
        legendSummaryPanel.Children.Clear();
        legendSummaryPanel.Visibility = Visibility.Collapsed;
    }

    private static ToolTip? CreateRecognitionDiagnosticsToolTip(IsoFieldRecognitionResult result)
    {
        if (result.Diagnostics.Count == 0)
        {
            return null;
        }

        return new ToolTip
        {
            Content = new ScrollViewer
            {
                MaxHeight = 360,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new TextBlock
                {
                    Width = 520,
                    Text = string.Join(Environment.NewLine, result.Diagnostics),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = TrueBimBrushes.TextPrimary,
                    LineHeight = 18
                }
            }
        };
    }

    private void RenderPreview(IsoFieldRecognitionResult result)
    {
        previewCanvas.Children.Clear();
        previewStatusText.ToolTip = null;
        if (currentSlabBinding is not null)
        {
            RenderSlabOverlay(currentSlabBinding, currentRulePreview);
            return;
        }

        IsoFieldPreviewLayout layout = previewLayoutService.Build(result, PreviewCanvasWidth, PreviewCanvasHeight);
        if (layout.Polylines.Count == 0)
        {
            previewCanvas.Children.Add(new TextBlock
            {
                Text = "Зоны не найдены",
                Foreground = TrueBimBrushes.TextMuted,
                FontWeight = FontWeights.SemiBold
            });
            Canvas.SetLeft(previewCanvas.Children[0], 16);
            Canvas.SetTop(previewCanvas.Children[0], 16);
            previewStatusText.Text = "Нет контуров для предпросмотра. Проверьте диагностику распознавания.";
            return;
        }

        Brush[] strokes =
        [
            TrueBimBrushes.Info,
            TrueBimBrushes.Success,
            TrueBimBrushes.Warning,
            TrueBimBrushes.Accent
        ];

        for (int index = 0; index < layout.Polylines.Count; index++)
        {
            IsoFieldPreviewPolyline source = layout.Polylines[index];
            WpfPolyline line = new()
            {
                Stroke = source.LayerRole.HasValue
                    ? strokes[(int)source.LayerRole.Value]
                    : strokes[index % strokes.Length],
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            };

            foreach (IsoFieldPoint point in source.Points)
            {
                line.Points.Add(new Point(point.X, point.Y));
            }

            previewCanvas.Children.Add(line);
        }

        previewStatusText.Text = $"Показано контуров: {layout.Polylines.Count}. Предпросмотр выполнен только в окне, модель Revit не изменялась.";
    }

    private void RenderSlabOverlay(
        IsoFieldSlabBindingAnalysis analysis,
        RebarRulePreviewResult? rulePreview)
    {
        IReadOnlyList<IsoFieldSlabRebarSegment> rebarSegments =
            rulePreview?.CanCreateRebar == true
            && rulePreview.IsEngineeringPreview
            && rulePreview.EngineeringSettings is not null
                ? slabRebarLayoutService.BuildSegments(
                    rulePreview.Items,
                    rulePreview.EngineeringSettings)
                : Array.Empty<IsoFieldSlabRebarSegment>();
        IsoFieldSlabOverlayLayout layout = slabOverlayLayoutService.Build(
            analysis,
            PreviewCanvasWidth,
            PreviewCanvasHeight,
            rebarSegments);
        WpfPolyline outerBoundary = CreatePreviewPolyline(
            layout.OuterBoundary,
            analysis.CanProceed ? TrueBimBrushes.Success : TrueBimBrushes.Danger,
            3);
        outerBoundary.ToolTip = "Внешний контур верхней грани плиты";
        previewCanvas.Children.Add(outerBoundary);

        foreach (IReadOnlyList<IsoFieldPoint> hole in layout.HoleBoundaries)
        {
            WpfPolyline holeBoundary = CreatePreviewPolyline(hole, TrueBimBrushes.Warning, 2);
            holeBoundary.StrokeDashArray = new DoubleCollection { 4, 3 };
            holeBoundary.ToolTip = "Отверстие плиты";
            previewCanvas.Children.Add(holeBoundary);
        }

        Brush[] strokes =
        [
            TrueBimBrushes.Info,
            TrueBimBrushes.Success,
            TrueBimBrushes.Warning,
            TrueBimBrushes.Accent
        ];
        for (int index = 0; index < layout.Zones.Count; index++)
        {
            IsoFieldSlabOverlayRegion zone = layout.Zones[index];
            Brush layerBrush = zone.LayerRole.HasValue
                ? strokes[(int)zone.LayerRole.Value]
                : strokes[index % strokes.Length];
            Brush fillBrush = layerBrush.Clone();
            fillBrush.Opacity = 0.18;
            WpfPath path = new()
            {
                Data = CreateOverlayRegionGeometry(zone),
                Fill = fillBrush,
                Stroke = zone.WasClipped ? TrueBimBrushes.Warning : layerBrush,
                StrokeThickness = zone.WasClipped ? 2.6 : 2,
                StrokeLineJoin = PenLineJoin.Round,
                ToolTip = zone.WasClipped
                    ? $"{zone.ZoneName ?? zone.SourceZoneId}{Environment.NewLine}Обрезано по плите; сохранено {(zone.RetainedAreaRatio * 100).ToString("0.#", CultureInfo.GetCultureInfo("ru-RU"))}% площади."
                    : zone.ZoneName ?? zone.SourceZoneId
            };
            if (zone.WasClipped)
            {
                path.StrokeDashArray = new DoubleCollection { 5, 2 };
            }

            previewCanvas.Children.Add(path);
        }

        foreach (IsoFieldPreviewPolyline removedZone in layout.RemovedZones)
        {
            WpfPolyline removedLine = CreatePreviewPolyline(
                removedZone.Points,
                TrueBimBrushes.Danger,
                3);
            removedLine.StrokeDashArray = new DoubleCollection { 4, 2 };
            removedLine.ToolTip = $"{removedZone.ZoneName ?? removedZone.Id}{Environment.NewLine}Зона полностью удалена отсечением и блокирует расчёт правил.";
            previewCanvas.Children.Add(removedLine);
        }

        foreach (IsoFieldSlabRebarSegment segment in layout.EffectiveRebarSegments)
        {
            Brush stroke = strokes[(int)segment.LayerRole];
            WpfPolyline barLine = CreatePreviewPolyline(
                [segment.StartFeet, segment.EndFeet],
                stroke,
                1.2);
            barLine.Opacity = 0.72;
            barLine.ToolTip = $"{segment.LayerRole} · {segment.Face} · {segment.Component.DisplayName}{Environment.NewLine}{segment.StableId}";
            previewCanvas.Children.Add(barLine);
        }

        for (int index = 0; index < layout.ControlPoints.Count; index++)
        {
            IsoFieldPoint point = layout.ControlPoints[index];
            bool isVerificationPoint = index == 2;
            Brush markerBrush = isVerificationPoint
                ? analysis.IsThirdPointValid ? TrueBimBrushes.Success : TrueBimBrushes.Danger
                : TrueBimBrushes.Accent;
            WpfEllipse marker = new()
            {
                Width = 9,
                Height = 9,
                Fill = markerBrush,
                Stroke = TrueBimBrushes.Surface,
                StrokeThickness = 1.5,
                ToolTip = $"Контрольная точка {index + 1}"
            };
            Canvas.SetLeft(marker, point.X - 4.5);
            Canvas.SetTop(marker, point.Y - 4.5);
            previewCanvas.Children.Add(marker);
            TextBlock label = new()
            {
                Text = (index + 1).ToString(CultureInfo.InvariantCulture),
                Foreground = markerBrush,
                FontWeight = FontWeights.Bold,
                FontSize = TrueBimTheme.CaptionFontSize
            };
            Canvas.SetLeft(label, point.X + 6);
            Canvas.SetTop(label, point.Y - 8);
            previewCanvas.Children.Add(label);
        }

        previewStatusText.Text = analysis.CanProceed
            ? layout.EffectiveRebarSegments.Count > 0
                ? $"Раскладка готова: зон {analysis.ClippedZones.Count}; стержней {layout.EffectiveRebarSegments.Count}; обрезано зон {analysis.ClippedZoneIds.Count}; отверстий {layout.HoleBoundaries.Count}."
                : $"Overlay готов: зон {analysis.ClippedZones.Count}; обрезано {analysis.ClippedZoneIds.Count}; сохранено {(analysis.RetainedAreaRatio * 100).ToString("0.#", CultureInfo.GetCultureInfo("ru-RU"))}% площади; отверстий {layout.HoleBoundaries.Count}."
            : analysis.RemovedZoneIds.Count > 0
                ? $"Overlay заблокирован: красным отмечено полностью потерянных зон {analysis.RemovedZoneIds.Count}."
                : $"Overlay заблокирован: отклонение третьей точки {FormatNumber(analysis.ThirdPointDeviationMillimeters)} мм при допуске {FormatNumber(analysis.ThirdPointToleranceMillimeters)} мм.";
        previewStatusText.ToolTip = string.Join(Environment.NewLine, analysis.Diagnostics);
    }

    private static PathGeometry CreateOverlayRegionGeometry(IsoFieldSlabOverlayRegion region)
    {
        PathGeometry geometry = new()
        {
            FillRule = FillRule.EvenOdd
        };
        AddOverlayFigure(geometry, region.OuterBoundary);
        foreach (IReadOnlyList<IsoFieldPoint> hole in region.HoleBoundaries)
        {
            AddOverlayFigure(geometry, hole);
        }

        return geometry;
    }

    private static void AddOverlayFigure(
        PathGeometry geometry,
        IReadOnlyList<IsoFieldPoint> points)
    {
        if (points.Count == 0)
        {
            return;
        }

        PathFigure figure = new()
        {
            StartPoint = new Point(points[0].X, points[0].Y),
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments.Add(new PolyLineSegment(
            new PointCollection(points.Skip(1).Select(point => new Point(point.X, point.Y))),
            isStroked: true));
        geometry.Figures.Add(figure);
    }

    private static WpfPolyline CreatePreviewPolyline(
        IReadOnlyList<IsoFieldPoint> points,
        Brush stroke,
        double strokeThickness)
    {
        WpfPolyline line = new()
        {
            Stroke = stroke,
            StrokeThickness = strokeThickness,
            StrokeLineJoin = PenLineJoin.Round
        };
        foreach (IsoFieldPoint point in points)
        {
            line.Points.Add(new Point(point.X, point.Y));
        }

        return line;
    }

    private void ClearPreview(string message)
    {
        currentRecognitionResult = null;
        ResetSlabBindingForSource(null);
        ResetLegendPresentation();
        recognitionStatusText.ToolTip = null;
        ClearRulePreview("Правила пока не рассчитаны.");
        previewCanvas.Children.Clear();
        previewCanvas.Children.Add(new TextBlock
        {
            Text = "Нет данных",
            Foreground = TrueBimBrushes.TextMuted,
            FontWeight = FontWeights.SemiBold
        });
        Canvas.SetLeft(previewCanvas.Children[0], 16);
        Canvas.SetTop(previewCanvas.Children[0], 16);
        previewStatusText.Text = message;
        previewStatusText.ToolTip = null;
    }

    private void ClearRulePreview(string message)
    {
        currentRulePreview = null;
        calculatedRulePreview = null;
        ruleOverrides.Clear();
        currentChangePlan = null;
        currentChangePlanFingerprint = null;
        RefreshRebarReviewRows();
        ruleStatusText.Text = message;
        rebarCreationStatusText.Text = "Армирование не создано: сначала рассчитайте валидную раскладку.";
        if (currentRecognitionResult is not null && currentSlabBinding is not null)
        {
            RenderPreview(currentRecognitionResult);
        }

        RefreshWorkflowState();
    }

    private void RefreshWorkflowState()
    {
        IsoFieldWorkflowState state = BuildWorkflowState();
        recognizeButton.IsEnabled = state.CanRunRecognition;
        bool isJsonSource = !string.IsNullOrWhiteSpace(selectedJsonPath);
        bool hasIncompleteSourceSet = selectedSourceSet is not null && !selectedSourceSet.IsComplete;
        TrueBimIcon recognitionIcon = !state.HasSource
            ? TrueBimIcon.Open
            : isJsonSource ? TrueBimIcon.Refresh : TrueBimIcon.Preview;
        string recognitionText = hasIncompleteSourceSet
            ? "Исправьте комплект"
            : !state.HasSource
            ? "Загрузить зоны"
            : isJsonSource ? "Перечитать JSON" : "Распознать 4 изображения";
        recognizeButton.Content = IconFactory.CreateButtonContent(recognitionIcon, recognitionText);
        recognizeButton.ToolTip = ResolveRecognitionToolTip(state);
        saveSourceSetManifestButton.IsEnabled = selectedSourceSet?.IsComplete == true;
        saveSourceSetManifestButton.ToolTip = saveSourceSetManifestButton.IsEnabled
            ? "Сохранить пути, размеры, SHA-256 и назначение верх/низ в воспроизводимый manifest."
            : "Сначала выберите и исправьте комплект из четырёх изображений.";

        showRevitPreviewButton.IsEnabled = state.CanShowRevitPreview;
        showRevitPreviewButton.ToolTip = state.CanShowRevitPreview
            ? "Создать управляемые линии предпросмотра на активном 2D-виде."
            : "Сначала загрузите зоны из JSON или распознанного изображения.";
        correctZonesButton.IsEnabled = state.HasZones;
        correctZonesButton.ToolTip = state.HasZones
            ? "Открыть таблицу ручной проверки: исключение, смена класса и объединение зон."
            : "Сначала загрузите зоны из JSON или распознайте комплект изображений.";
        clearRevitPreviewButton.IsEnabled = state.CanClearRevitPreview;
        clearRevitPreviewButton.ToolTip = state.CanClearRevitPreview
            ? "Удалить линии предпросмотра изополей на активном виде."
            : "В этой сессии нет линий предпросмотра для удаления.";

        selectHostButton.IsEnabled = uiDocument is not null;
        selectHostButton.ToolTip = uiDocument is null
            ? "Откройте документ Revit, чтобы выбрать стену или плиту."
            : "Выбрать стену или плиту как host для армирования.";
        clearHostButton.IsEnabled = state.HasHost;
        slabBindingExpander.IsExpanded = selectedHostElement?.IsSlab == true;
        bool canConfigureSlabBinding = state.HasZones
            && selectedHostElement?.IsSlab == true
            && selectedHostElement.Geometry is not null
            && uiDocument is not null;
        slabImagePoint1XInput.IsEnabled = canConfigureSlabBinding;
        slabImagePoint1YInput.IsEnabled = canConfigureSlabBinding;
        slabImagePoint2XInput.IsEnabled = canConfigureSlabBinding;
        slabImagePoint2YInput.IsEnabled = canConfigureSlabBinding;
        slabImagePoint3XInput.IsEnabled = canConfigureSlabBinding;
        slabImagePoint3YInput.IsEnabled = canConfigureSlabBinding;
        slabMirrorImageYInput.IsEnabled = canConfigureSlabBinding;
        pickSlabPoint1Button.IsEnabled = canConfigureSlabBinding;
        pickSlabPoint2Button.IsEnabled = canConfigureSlabBinding;
        pickSlabPoint3Button.IsEnabled = canConfigureSlabBinding;
        applySlabBindingButton.IsEnabled = canConfigureSlabBinding
            && slabHostPoint1Feet is not null
            && slabHostPoint2Feet is not null
            && slabHostPoint3Feet is not null;
        loadSlabBindingProfileButton.IsEnabled = canConfigureSlabBinding
            && availableSlabBindingProfile is not null;
        saveSlabBindingProfileButton.IsEnabled = currentSlabBinding?.CanProceed == true;
        string slabBindingToolTip = selectedHostElement switch
        {
            null => "Сначала выберите горизонтальную плиту.",
            { IsSlab: false } => "Для стены трёхточечная привязка плиты не требуется.",
            { Geometry: null } => "У выбранной плиты не распознана горизонтальная верхняя грань.",
            _ when !state.HasZones => "Сначала загрузите или распознайте зоны.",
            _ => "Укажите соответствующую точку на верхней грани выбранной плиты."
        };
        pickSlabPoint1Button.ToolTip = slabBindingToolTip;
        pickSlabPoint2Button.ToolTip = slabBindingToolTip;
        pickSlabPoint3Button.ToolTip = canConfigureSlabBinding
            ? "Укажите точку в стороне от линии первых двух точек, чтобы независимо проверить привязку."
            : slabBindingToolTip;
        applySlabBindingButton.ToolTip = applySlabBindingButton.IsEnabled
            ? "Проверить три точки, обрезать зоны по контуру и отверстиям и построить итоговый overlay."
            : "Сначала укажите все три контрольные точки на плите.";
        loadSlabBindingProfileButton.ToolTip = loadSlabBindingProfileButton.IsEnabled
            ? $"Загрузить профиль, сохранённый {availableSlabBindingProfile!.SavedAtUtc.ToLocalTime():g}. После загрузки зоны будут проверены заново."
            : "Для текущего документа, вида и выбранной плиты сохранённый профиль не найден.";
        saveSlabBindingProfileButton.ToolTip = saveSlabBindingProfileButton.IsEnabled
            ? "Сохранить три пары точек и отражение Y для текущего документа, вида и плиты."
            : "Сначала выполните успешную проверку привязки по трём точкам.";
        bool canConfigureEngineeringRules = state.HasZones
            && selectedHostElement?.IsSlab == true;
        reinforcementModeInput.IsEnabled = canConfigureEngineeringRules;
        concreteCoverInput.IsEnabled = canConfigureEngineeringRules;
        boundaryOffsetInput.IsEnabled = canConfigureEngineeringRules;
        minimumBarLengthInput.IsEnabled = canConfigureEngineeringRules;
        string engineeringToolTip = selectedHostElement?.IsSlab == true
            ? "Изменение параметра сбрасывает рассчитанную раскладку."
            : "Инженерная раскладка по отсечённым зонам доступна для горизонтальной плиты.";
        reinforcementModeInput.ToolTip = canConfigureEngineeringRules
            ? "Выберите создание только добавки поверх существующей базовой сетки либо полного сочетания внутри зон."
            : engineeringToolTip;
        concreteCoverInput.ToolTip = engineeringToolTip;
        boundaryOffsetInput.ToolTip = engineeringToolTip;
        minimumBarLengthInput.ToolTip = engineeringToolTip;
        previewRulesButton.IsEnabled = state.CanCalculateRules;
        previewRulesButton.ToolTip = state.CanCalculateRules
            ? selectedHostElement?.IsSlab == true
                ? "Проверить площадь в см²/м и рассчитать линии стержней внутри отсечённых зон без изменения Revit."
                : "Сформировать read-only preview правил армирования для загруженных зон."
            : state.HasHost && !state.HasValidHostBinding
                ? "Для плиты сначала выполните привязку по трём контрольным точкам; полностью потерянных зон быть не должно."
                : "Сначала загрузите зоны и выберите host-элемент.";
        bool isEngineeringPreview = currentRulePreview?.IsEngineeringPreview == true;
        compareChangesButton.IsEnabled = state.CanCreateRebar && isEngineeringPreview;
        compareChangesButton.ToolTip = compareChangesButton.IsEnabled
            ? $"Сравнить {currentRulePreview!.EstimatedBarCount} расчётных стержней с принадлежащей модулю арматурой без изменения модели."
            : state.CanCreateRebar
                ? "Сравнение по зонам доступно для инженерной раскладки горизонтальной плиты."
                : "Сначала рассчитайте валидную инженерную раскладку.";
        createTestRebarButton.IsEnabled = state.CanCreateRebar
            && (!isEngineeringPreview
                || currentChangePlan?.CanApply == true && currentChangePlan.HasChanges);
        createTestRebarButton.ToolTip = state.CanCreateRebar
            ? isEngineeringPreview
                ? currentChangePlan is null
                    ? "Сначала нажмите «Сравнить с моделью» и проверьте таблицу."
                    : !currentChangePlan.CanApply
                        ? "План изменений содержит ошибки; исправьте диагностику и повторите сравнение."
                        : !currentChangePlan.HasChanges
                            ? "Раскладка уже соответствует модели; применять нечего."
                            : $"Применить после подтверждения: {currentChangePlan.Summary}"
                : "Создать пробное армирование после отдельного подтверждения."
            : !state.HasConfirmedLayerMappings && state.HasSource
                ? "Подтвердите назначение верх/низ для всех расчётных слоёв."
                : state.HasHost && !state.HasValidHostBinding
                    ? "Проверьте трёхточечную привязку и отсечение зон по плите."
                : "Сначала рассчитайте раскладку без ошибок.";

        string nextAction = selectedSourceSet is not null && !selectedSourceSet.IsComplete
            ? FormatSourceSetIssues(selectedSourceSet)
            : state.NextAction;
        workflowSummaryText.Text = $"Готово {state.CompletedStepCount} из 5. {nextAction}";
        UpdateWorkflowStep(
            sourceStepText,
            state.HasSource,
            selectedSourceSet is null ? "Источник выбран" : "Комплект из 4 слоёв готов");
        UpdateWorkflowStep(
            mappingStepText,
            state.HasConfirmedLayerMappings,
            isJsonSource ? "Назначение слоёв не требуется" : "Верх/низ подтверждены");
        UpdateWorkflowStep(zonesStepText, state.HasZones, "Зоны загружены");
        string hostStepLabel = selectedHostElement switch
        {
            null => "Host выбран",
            { IsSlab: true } when state.HasValidHostBinding => "Плита привязана",
            { IsSlab: true } => "Плита выбрана, нужна привязка",
            _ => "Host выбран"
        };
        UpdateWorkflowStep(hostStepText, state.HasReadyHost, hostStepLabel);
        UpdateWorkflowStep(rulesStepText, state.HasValidRules, "Правила проверены");
        RefreshZoneRuleActions();
    }

    private IsoFieldWorkflowState BuildWorkflowState()
    {
        bool isJsonSource = !string.IsNullOrWhiteSpace(selectedJsonPath);
        bool hasSource = isJsonSource || selectedSourceSet?.IsComplete == true;
        bool hasConfirmedLayerMappings = isJsonSource || selectedSourceSet?.HasConfirmedLayerMappings == true;
        bool canProcessImages = !string.Equals(ResolveRecognitionRunnerName(), "Stub", StringComparison.OrdinalIgnoreCase);
        bool hasValidHostBinding = selectedHostElement?.IsSlab != true
            || currentSlabBinding?.CanProceed == true;
        return new IsoFieldWorkflowState(
            hasSource,
            currentRecognitionResult?.Polylines.Count > 0,
            selectedHostElement is not null,
            currentRulePreview?.CanCreateRebar == true,
            activeRevitPreviewIds.Count > 0,
            isJsonSource || selectedSourceSet?.IsComplete == true && canProcessImages,
            hasConfirmedLayerMappings,
            hasValidHostBinding);
    }

    private string ResolveRecognitionToolTip(IsoFieldWorkflowState state)
    {
        if (selectedSourceSet is not null && !selectedSourceSet.IsComplete)
        {
            return FormatSourceSetIssues(selectedSourceSet);
        }

        if (!state.HasSource)
        {
            return "Сначала выберите JSON или полный комплект из четырёх изображений.";
        }

        if (!state.CanProcessSource)
        {
            return "Обработчик изображений недоступен: выберите готовый JSON.";
        }

        return !string.IsNullOrWhiteSpace(selectedJsonPath)
            ? "Перечитать зоны из выбранного JSON."
            : $"Последовательно обработать четыре слоя с помощью «{ResolveRecognitionRunnerName()}».";
    }

    private static void UpdateWorkflowStep(TextBlock textBlock, bool isComplete, string label)
    {
        textBlock.Text = $"{(isComplete ? "✓" : "○")} {label}";
        textBlock.Foreground = isComplete ? TrueBimBrushes.Success : TrueBimBrushes.TextMuted;
        textBlock.FontWeight = isComplete ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private static StackPanel CreatePanelContent(string title)
    {
        StackPanel content = new()
        {
            Margin = TrueBimTheme.SectionPadding
        };
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = TrueBimTheme.SectionTitleFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12)
        });

        return content;
    }

    private static Border CreatePanel(UIElement child)
    {
        return new Border
        {
            BorderBrush = TrueBimBrushes.Border,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            Background = TrueBimBrushes.Surface,
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Child = child
        };
    }

    private static Button CreateActionButton(
        string text,
        TrueBimIcon icon,
        double minWidth,
        string toolTip,
        RoutedEventHandler clickHandler,
        TrueBimButtonStyleKind styleKind = TrueBimButtonStyleKind.Secondary)
    {
        Button button = new()
        {
            Content = IconFactory.CreateButtonContent(icon, text),
            MinWidth = minWidth,
            MinHeight = TrueBimTheme.ControlHeight36,
            Style = TrueBimStyles.CreateButtonStyle(styleKind),
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = toolTip
        };
        button.Click += clickHandler;
        ToolTipService.SetShowOnDisabled(button, true);
        return button;
    }

    private static TextBlock CreateWorkflowStepText(string text)
    {
        return new TextBlock
        {
            Text = $"○ {text}",
            Foreground = TrueBimBrushes.TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12)
        };
    }

    private static TextBlock CreateMutedText(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = TrueBimBrushes.TextSecondary,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0)
        };
    }

    private static WpfTextBox CreateCalibrationInput(double value)
    {
        return new WpfTextBox
        {
            Text = FormatNumber(value),
            Width = 110,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateTextBoxStyle(),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0)
        };
    }

    private static WpfTextBox CreateBindingInput(double value)
    {
        return new WpfTextBox
        {
            Text = FormatNumber(value),
            Width = 72,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateTextBoxStyle(),
            VerticalContentAlignment = VerticalAlignment.Center
        };
    }

    private static StackPanel CreateInputRow(string label, WpfTextBox input)
    {
        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };

        row.Children.Add(new TextBlock
        {
            Text = label,
            Width = 80,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = TrueBimBrushes.TextSecondary
        });
        row.Children.Add(input);
        return row;
    }

    private static Canvas CreatePreviewCanvas()
    {
        return new Canvas
        {
            Width = PreviewCanvasWidth,
            Height = PreviewCanvasHeight,
            ClipToBounds = true
        };
    }

    private Button CreateRevitPreviewButton()
    {
        return CreateActionButton(
            "Показать в Revit",
            TrueBimIcon.Apply,
            158,
            "Сначала загрузите зоны из JSON или распознанного изображения.",
            (_, _) => ShowRevitPreview());
    }

    private Button CreateClearRevitPreviewButton()
    {
        Button button = CreateActionButton(
            "Очистить",
            TrueBimIcon.Close,
            116,
            "В этой сессии нет линий предпросмотра для удаления.",
            (_, _) => ClearRevitPreview());
        button.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        return button;
    }

    private static bool IsJsonFile(string path)
    {
        return string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadDouble(WpfTextBox input, string label, out double value, out string message)
    {
        string text = input.Text?.Trim() ?? string.Empty;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
        {
            message = string.Empty;
            return true;
        }

        message = $"Поле \"{label}\" должно содержать число.";
        value = 0;
        return false;
    }

    private static string FormatCalibration(IsoFieldCalibration calibration)
    {
        return $"Якорь: {FormatNumber(calibration.ImageAnchor.X)}; {FormatNumber(calibration.ImageAnchor.Y)}. Масштаб: {FormatNumber(calibration.MillimetersPerPixel)} мм/пикс.";
    }

    private static string FormatRulePreview(RebarRulePreviewResult preview)
    {
        if (preview.Diagnostics.Count > 0)
        {
            return string.Join(Environment.NewLine, preview.Diagnostics);
        }

        if (preview.Items.Count == 0)
        {
            return "Правила не рассчитаны.";
        }

        string[] lines = preview.Items
            .Take(4)
            .Select(item => item.IsValid
                ? item.DisplayName
                : $"{item.ZoneName}: {string.Join("; ", item.Diagnostics)}")
            .ToArray();
        string suffix = preview.Items.Count > lines.Length
            ? $"{Environment.NewLine}Еще зон: {preview.Items.Count - lines.Length}."
            : string.Empty;
        string header = preview.IsEngineeringPreview
            ? $"Инженерная раскладка: зон {preview.Items.Count}, стержней {preview.EstimatedBarCount}."
            : $"Правил: {preview.Items.Count}.";
        return $"{header}{Environment.NewLine}{string.Join(Environment.NewLine, lines)}{suffix}";
    }

    private string ResolveRecognitionRunnerName()
    {
        return recognitionRunner is IIsoFieldRecognitionRunnerDiagnostics diagnostics
            ? diagnostics.RunnerName
            : recognitionRunner.GetType().Name;
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.###", CultureInfo.CurrentCulture);
    }

    private sealed record IsoFieldFaceOption(IsoFieldRebarFace Face, string Label);

    private sealed record IsoFieldReinforcementModeOption(
        IsoFieldReinforcementMode Mode,
        string Label);

    private sealed record IsoFieldReviewLayerOption(
        IsoFieldLayerRole? LayerRole,
        string Label);

    private sealed record IsoFieldReviewStatusOption(
        IsoFieldRebarReviewStatus? Status,
        string Label);

    private sealed record IsoFieldReviewNumberOption(
        double? Value,
        string Label);

    private sealed record ReadyRebarContext(
        RebarRulePreviewResult Preview,
        IsoFieldHostElement HostElement);

    private sealed record RoleDetectionPresentation(string Label, string ToolTip, Brush Foreground);
}
