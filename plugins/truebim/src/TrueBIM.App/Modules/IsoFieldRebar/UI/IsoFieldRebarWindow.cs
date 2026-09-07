using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
    private readonly IsoFieldRebarZoneMergeService rebarZoneMergeService = new();
    private readonly IsoFieldRebarReportService rebarReportService = new();
    private readonly IsoFieldRebarQualityService rebarQualityService = new();
    private readonly IsoFieldHostSupportService hostSupportService = new();
    private readonly ObservableCollection<IsoFieldRebarReviewRow> rebarReviewRows = new();
    private readonly Dictionary<string, IsoFieldRebarRuleOverride> ruleOverrides = new(StringComparer.Ordinal);
    private readonly List<IsoFieldRebarZoneMerge> zoneMerges = new();
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
    private readonly Button recognizeBottomButton;
    private readonly Button correctZonesButton;
    private readonly Button showRevitPreviewButton;
    private readonly Button clearRevitPreviewButton;
    private readonly Button selectHostButton;
    private readonly Button clearHostButton;
    private readonly Button previewRulesButton;
    private readonly Button compareChangesButton;
    private readonly Button exportReportButton;
    private readonly Button createTestRebarButton;
    private readonly Button editZoneRuleButton;
    private readonly Button mergeZonesButton;
    private readonly Button unmergeZonesButton;
    private readonly Button excludeEmptyZonesButton;
    private readonly Button resetZoneRulesButton;
    private readonly Button saveSourceSetManifestButton;
    private readonly TextBlock workflowSummaryText;
    private readonly TextBlock workflowStageText;
    private readonly TextBlock workflowActionText;
    private readonly Button workflowActionButton;
    private readonly TextBlock sourceStepText;
    private readonly TextBlock mappingStepText;
    private readonly TextBlock zonesStepText;
    private readonly TextBlock hostStepText;
    private readonly TextBlock rulesStepText;
    private readonly TextBlock comparisonStepText;
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
    private readonly TextBlock qualityStatusText;
    private readonly StackPanel qualityIssuesPanel;
    private readonly CheckBox qualityWarningsAcceptedInput;
    private readonly Border completionSummaryPanel;
    private readonly TextBlock completionSummaryText;
    private readonly TextBlock completionArtifactsText;
    private readonly Button saveCompletionReportButton;
    private readonly Button openLastReportButton;
    private readonly Button openLogButton;
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
    private RebarRulePreviewResult? configuredRulePreview;
    private IsoFieldRebarChangePlan? currentChangePlan;
    private string? currentChangePlanFingerprint;
    private IsoFieldRebarQualityResult? currentQualityResult;
    private string? familyPreflightError;
    private bool areQualityWarningsAccepted;
    private IsoFieldRebarReportSaveResult? lastReportSaveResult;
    private IsoFieldRebarCreationResult? lastApplicationResult;
    private DateTimeOffset? lastApplicationCompletedAtUtc;
    private IsoFieldHostElement? lastApplicationHost;
    private int applicationRevision;
    private int lastReportApplicationRevision = -1;
    private bool isApplyConfirmed;
    private WorkflowPrimaryAction workflowPrimaryAction;
    private IReadOnlyList<ElementId> activeRevitPreviewIds = Array.Empty<ElementId>();
    private const double PreviewCanvasWidth = 430;
    private const double PreviewCanvasHeight = 180;
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
        new(null, "Все карты"),
        new(IsoFieldLayerRole.As1X, "X, карта 1"),
        new(IsoFieldLayerRole.As2X, "X, карта 2"),
        new(IsoFieldLayerRole.As3Y, "Y, карта 1"),
        new(IsoFieldLayerRole.As4Y, "Y, карта 2")
    ];
    private static readonly IReadOnlyList<IsoFieldSourceRoleOption> SourceRoleOptions =
    [
        new(IsoFieldLayerRole.As1X, "X, карта 1"),
        new(IsoFieldLayerRole.As2X, "X, карта 2"),
        new(IsoFieldLayerRole.As3Y, "Y, карта 1"),
        new(IsoFieldLayerRole.As4Y, "Y, карта 2")
    ];
    private static readonly IReadOnlyList<IsoFieldReviewStatusOption> ReviewStatusOptions =
    [
        new(null, "Все результаты"),
        new(IsoFieldRebarReviewStatus.NotCompared, "Не сравнено"),
        new(IsoFieldRebarReviewStatus.Add, "Добавить"),
        new(IsoFieldRebarReviewStatus.Update, "Обновить"),
        new(IsoFieldRebarReviewStatus.Delete, "Удалить"),
        new(IsoFieldRebarReviewStatus.Unchanged, "Без изменений"),
        new(IsoFieldRebarReviewStatus.Mixed, "Несколько изменений"),
        new(IsoFieldRebarReviewStatus.Invalid, "Ошибка"),
        new(IsoFieldRebarReviewStatus.Excluded, "Исключена")
    ];
    private static readonly IReadOnlyList<IsoFieldReviewNumberOption> ReviewConfidenceOptions =
    [
        new(null, "Любая точность"),
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
        recognitionStatusText = CreateMutedText("Выберите четыре карты для расчёта арматуры или готовый файл с зонами для ознакомления.");
        hostStatusText = CreateMutedText("Стена или плита не выбрана.");
        calibrationAnchorXInput = CreateCalibrationInput(currentCalibration.ImageAnchor.X);
        calibrationAnchorYInput = CreateCalibrationInput(currentCalibration.ImageAnchor.Y);
        calibrationMillimetersPerPixelInput = CreateCalibrationInput(currentCalibration.MillimetersPerPixel);
        calibrationInvertYInput = new CheckBox
        {
            Content = "На карте вертикаль направлена вниз",
            IsChecked = currentCalibration.InvertImageY,
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0),
            Style = TrueBimStyles.CreateCheckBoxStyle(),
            ToolTip = "Включите, если верх карты на виде должен быть направлен вниз. Эта настройка относится только к вспомогательным линиям."
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
            Content = "Перевернуть карту по вертикали",
            IsChecked = true,
            Style = TrueBimStyles.CreateCheckBoxStyle(),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing16, TrueBimTheme.Spacing8),
            ToolTip = "Включите, если вертикальное направление на карте противоположно направлению на стене или плите. После изменения проверьте привязку заново."
        };
        pickSlabPoint1Button = CreateActionButton(
            "Указать точку 1 на конструкции",
            TrueBimIcon.Apply,
            158,
            "Сначала загрузите зоны и выберите прямую стену или горизонтальную плиту.",
            (_, _) => PickSlabControlPoint(1));
        pickSlabPoint2Button = CreateActionButton(
            "Указать точку 2 на конструкции",
            TrueBimIcon.Apply,
            158,
            "Сначала загрузите зоны и выберите прямую стену или горизонтальную плиту.",
            (_, _) => PickSlabControlPoint(2));
        pickSlabPoint3Button = CreateActionButton(
            "Указать точку 3 на конструкции",
            TrueBimIcon.Apply,
            158,
            "Третья точка задаёт масштаб вдоль второй стороны и проверяет порядок углов и зеркальность карты.",
            (_, _) => PickSlabControlPoint(3));
        applySlabBindingButton = CreateActionButton(
            "Проверить привязку",
            TrueBimIcon.Preview,
            176,
            "Введите координаты трёх точек на карте и укажите те же точки на выбранной конструкции.",
            (_, _) => ApplySlabBinding(showDialogOnError: false));
        loadSlabBindingProfileButton = CreateActionButton(
            "Восстановить привязку",
            TrueBimIcon.Import,
            164,
            "Восстановить последнюю сохранённую привязку для этого проекта, вида и выбранной конструкции.",
            (_, _) => LoadSlabBindingProfile());
        saveSlabBindingProfileButton = CreateActionButton(
            "Сохранить привязку",
            TrueBimIcon.Export,
            164,
            "Сначала выполните успешную проверку привязки.",
            (_, _) => SaveSlabBindingProfile());
        slabHostPoint1Text = CreateMutedText("Точка 1 на конструкции не указана.");
        slabHostPoint2Text = CreateMutedText("Точка 2 на конструкции не указана.");
        slabHostPoint3Text = CreateMutedText("Точка 3 на конструкции не указана.");
        slabBindingStatusText = CreateMutedText("Выберите поддерживаемую прямую стену или горизонтальную плиту, затем задайте три пары контрольных точек.");
        showRevitPreviewButton = CreateRevitPreviewButton();
        clearRevitPreviewButton = CreateClearRevitPreviewButton();
        slabBindingExpander = CreateSlabBindingPanel();
        reinforcementModeInput = new WpfComboBox
        {
            ItemsSource = ReinforcementModeOptions,
            DisplayMemberPath = nameof(IsoFieldReinforcementModeOption.Label),
            SelectedIndex = 0,
            MinWidth = 292,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateComboBoxStyle(),
            ToolTip = "В режиме усиления первый набор стержней считается существующей базовой сеткой и повторно не создаётся."
        };
        ForwardMouseWheelToPage(reinforcementModeInput);
        concreteCoverInput = CreateBindingInput(IsoFieldEngineeringSettings.Default.ConcreteCoverMillimeters);
        boundaryOffsetInput = CreateBindingInput(IsoFieldEngineeringSettings.Default.BoundaryOffsetMillimeters);
        minimumBarLengthInput = CreateBindingInput(IsoFieldEngineeringSettings.Default.MinimumBarLengthMillimeters);
        ToolTipService.SetShowOnDisabled(slabImagePoint1XInput, true);
        ToolTipService.SetShowOnDisabled(slabImagePoint1YInput, true);
        ToolTipService.SetShowOnDisabled(slabImagePoint2XInput, true);
        ToolTipService.SetShowOnDisabled(slabImagePoint2YInput, true);
        ToolTipService.SetShowOnDisabled(slabImagePoint3XInput, true);
        ToolTipService.SetShowOnDisabled(slabImagePoint3YInput, true);
        ToolTipService.SetShowOnDisabled(slabMirrorImageYInput, true);
        ToolTipService.SetShowOnDisabled(reinforcementModeInput, true);
        ToolTipService.SetShowOnDisabled(concreteCoverInput, true);
        ToolTipService.SetShowOnDisabled(boundaryOffsetInput, true);
        ToolTipService.SetShowOnDisabled(minimumBarLengthInput, true);
        reviewSearchInput = new WpfTextBox
        {
            Width = 180,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateTextBoxStyle(),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Найти зону по её названию или внутреннему номеру."
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
        qualityStatusText = CreateMutedText("Контроль качества будет выполнен после расчёта раскладки.");
        qualityIssuesPanel = new StackPanel
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0)
        };
        qualityWarningsAcceptedInput = new CheckBox
        {
            Content = "Я проверил отмеченные места и разрешаю применить эту раскладку",
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0),
            Style = TrueBimStyles.CreateCheckBoxStyle(),
            Visibility = Visibility.Collapsed,
            ToolTip = "Подтверждение действует только для текущего расчёта. Если карты, привязка или настройки изменятся, предупреждения нужно проверить заново."
        };
        rebarReviewGrid = CreateRebarReviewGrid();
        editZoneRuleButton = CreateActionButton(
            "Настроить выбранную",
            TrueBimIcon.Settings,
            184,
            "Выберите расчётную строку зоны в таблице.",
            (_, _) => EditSelectedZoneRule());
        mergeZonesButton = CreateActionButton(
            "Объединить выбранные",
            TrueBimIcon.JoinCut,
            196,
            "Выделите не менее двух соседних зон, удерживая клавишу выбора нескольких строк.",
            (_, _) => MergeSelectedZones());
        unmergeZonesButton = CreateActionButton(
            "Разъединить",
            TrueBimIcon.Close,
            146,
            "Выберите ранее объединённую строку.",
            (_, _) => UnmergeSelectedZones());
        excludeEmptyZonesButton = CreateActionButton(
            "Исключить без стержней",
            TrueBimIcon.Close,
            210,
            "Кнопка станет доступна, если после отступов и проверки минимальной длины найдутся пустые фрагменты зон.",
            (_, _) => ExcludeZonesWithoutBars());
        resetZoneRulesButton = CreateActionButton(
            "Сбросить всё",
            TrueBimIcon.Refresh,
            146,
            "Ручных правил и объединений пока нет.",
            (_, _) => ResetManualZoneConfiguration());
        ruleStatusText = CreateMutedText("Правила пока не рассчитаны.");
        rebarCreationStatusText = CreateMutedText("Раскладка армирования пока не создана.");
        rebarCreationStatusText.MaxHeight = 72;
        rebarCreationStatusText.TextTrimming = TextTrimming.CharacterEllipsis;
        rebarCreationStatusText.SetBinding(
            FrameworkElement.ToolTipProperty,
            new WpfBinding(nameof(TextBlock.Text))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.Self)
            });
        previewStatusText = CreateMutedText("Контуры пока не загружены.");
        previewCanvas = CreatePreviewCanvas();
        recognizeButton = CreateActionButton(
            "Загрузить зоны",
            TrueBimIcon.Preview,
            176,
            "Сначала выберите готовые зоны или полный комплект из четырёх карт.",
            (_, _) => RunRecognition());
        recognizeBottomButton = CreateActionButton(
            "Продолжить: найти зоны на 4 картах",
            TrueBimIcon.Preview,
            286,
            "Назначьте сторону для каждой карты, затем запустите поиск зон.",
            (_, _) => RunRecognition(),
            TrueBimButtonStyleKind.Primary);
        recognizeBottomButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        recognizeBottomButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        correctZonesButton = CreateActionButton(
            "Исправить зоны",
            TrueBimIcon.Settings,
            152,
            "Сначала загрузите или распознайте зоны.",
            (_, _) => CorrectZones());
        selectHostButton = CreateActionButton(
            "Выбрать стену/плиту",
            TrueBimIcon.Apply,
            190,
            "Указать стену или плиту, на которой будет размещена арматура.",
            (_, _) => SelectHostElement());
        clearHostButton = CreateActionButton(
            "Сбросить",
            TrueBimIcon.Close,
            116,
            "Отменить выбор стены или плиты.",
            (_, _) => ClearHostElement());
        previewRulesButton = CreateActionButton(
            "Рассчитать раскладку",
            TrueBimIcon.Preview,
            188,
            "Сначала загрузите зоны и выберите стену или плиту.",
            (_, _) => PreviewRebarRulesSafely());
        compareChangesButton = CreateActionButton(
            "Сравнить с моделью",
            TrueBimIcon.Refresh,
            196,
            "Сначала рассчитайте раскладку без ошибок.",
            (_, _) => CompareEngineeringChanges());
        exportReportButton = CreateActionButton(
            "Сохранить отчёт",
            TrueBimIcon.Export,
            164,
            "Сначала рассчитайте раскладку без ошибок.",
            (_, _) => ExportRebarReport());
        completionSummaryText = CreateMutedText("Итог применения появится после успешной записи в модель.");
        completionArtifactsText = CreateMutedText("Итоговый отчёт ещё не сохранён.");
        saveCompletionReportButton = CreateActionButton(
            "Сохранить итоговый отчёт",
            TrueBimIcon.Export,
            214,
            "Сохранить подробный отчёт и таблицу с итогом последнего применения.",
            (_, _) => SaveCompletionReport());
        openLastReportButton = CreateActionButton(
            "Открыть отчёт",
            TrueBimIcon.Open,
            158,
            "Последний отчёт ещё не сохранён.",
            (_, _) => OpenLastReport());
        openLogButton = CreateActionButton(
            "Открыть журнал работы",
            TrueBimIcon.Logs,
            142,
            "Открыть журнал работы модуля. Он поможет разобраться, если возникла ошибка.",
            (_, _) => OpenIsoFieldLog());
        completionSummaryPanel = CreateCompletionSummaryPanel();
        createTestRebarButton = CreateActionButton(
            "Применить изменения",
            TrueBimIcon.Apply,
            244,
            "Сначала рассчитайте раскладку и отдельно сравните её с моделью.",
            (_, _) => CreateTestRebar(),
            TrueBimButtonStyleKind.Primary);
        saveSourceSetManifestButton = CreateActionButton(
            "Сохранить комплект",
            TrueBimIcon.Export,
            168,
            "Сначала выберите и проверьте четыре карты изополей.",
            (_, _) => SaveSourceSetManifest());
        workflowSummaryText = CreateMutedText(
            $"Выполнено 0 из {IsoFieldWorkflowState.RequiredStepCount} обязательных проверок.");
        workflowStageText = new TextBlock
        {
            Text = "Этап 1 из 4 · Карты изополей",
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary,
            TextWrapping = TextWrapping.Wrap
        };
        workflowActionText = CreateMutedText("Выберите комплект из четырёх карт.");
        workflowActionButton = CreateActionButton(
            "Выбрать карты",
            TrueBimIcon.Open,
            0,
            "Выберите четыре карты изополей.",
            (_, _) => RunWorkflowPrimaryAction(),
            TrueBimButtonStyleKind.Primary);
        workflowActionButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        workflowActionButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        sourceStepText = CreateWorkflowStepText("Источник выбран");
        mappingStepText = CreateWorkflowStepText("Грани назначены");
        zonesStepText = CreateWorkflowStepText("Зоны загружены");
        hostStepText = CreateWorkflowStepText("Конструкция выбрана и привязана");
        rulesStepText = CreateWorkflowStepText("Раскладка проверена");
        comparisonStepText = CreateWorkflowStepText("Сравнение с моделью выполнено");
        layerMappingStatusText = CreateMutedText("Назначение верх/низ появится после выбора комплекта изображений.");
        manifestStatusText = CreateMutedText("Комплект ещё не сохранён.");
        footerStatusText = CreateMutedText("Арматура изменится только после отдельного сравнения и подтверждения.");

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
        qualityWarningsAcceptedInput.Checked += (_, _) => SetQualityWarningsAccepted(true);
        qualityWarningsAcceptedInput.Unchecked += (_, _) => SetQualityWarningsAccepted(false);

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
        WpfGrid mainContent = new();
        mainContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Border filePanel = CreateFilePanel();
        mainContent.Children.Add(filePanel);

        Border previewPanel = CreatePreviewPanel();
        WpfGrid.SetRow(previewPanel, 1);
        previewPanel.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        mainContent.Children.Add(previewPanel);

        Border hostPanel = CreateHostPanel();
        WpfGrid.SetRow(hostPanel, 2);
        hostPanel.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        mainContent.Children.Add(hostPanel);

        Border rulePanel = CreateRulePanel();
        WpfGrid.SetRow(rulePanel, 3);
        rulePanel.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        mainContent.Children.Add(rulePanel);

        WpfGrid workspace = new();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(264) });

        ScrollViewer mainScrollViewer = CreateScrollableBody(mainContent);
        workspace.Children.Add(mainScrollViewer);

        Border workflowPanel = CreateWorkflowPanel();
        WpfGrid.SetColumn(workflowPanel, 1);
        workflowPanel.Margin = new Thickness(TrueBimTheme.Spacing12, 0, 0, 0);
        workflowPanel.VerticalAlignment = VerticalAlignment.Top;
        workspace.Children.Add(workflowPanel);

        return BuildShell(
            header: TrueBimUi.CreateHeader(
                Title,
                $"Открытый проект: {documentTitle}. Идите по шагам сверху вниз: загрузите карты, проверьте зоны, выберите конструкцию, рассчитайте раскладку и только затем примените изменения.",
                TrueBimIcon.IsoFieldRebar),
            commandBar: TrueBimUi.CreateCommandBar(CreateGuideButton()),
            body: workspace,
            status: null,
            footer: CreateFooter());
    }

    private static ScrollViewer CreateScrollableBody(UIElement body)
    {
        return new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
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
            Text = "Как работать с армированием по изополям",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        content.Children.Add(CreateMutedText("Нажмите, чтобы открыть пошаговую инструкцию: какие карты выбрать, как совместить их с конструкцией и как проверить результат."));
        content.Children.Add(CreateMutedText("Арматура меняется только после кнопки «Применить изменения» и отдельного подтверждения. Кнопка «Показать линии на виде» добавляет лишь вспомогательные линии, которые можно удалить."));

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
        StackPanel content = CreatePanelContent("1. Загрузите карты изополей");
        content.Children.Add(TrueBimUi.CreateInfoBanner(
            "Для расчёта выберите сразу четыре карты. В именах файлов должны встречаться метки As1X, As2X, As3Y и As4Y; остальная часть имени и регистр не важны. Пример: «Плита_As1X1.png». По стандартной схеме модуль сразу назначит As1/As3 на низ, As2/As4 на верх — этот вариант можно изменить в таблице.",
            TrueBimUiSeverity.Neutral));

        WrapPanel buttonRow = new();

        Button chooseButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Open, "Выбрать карты или готовые зоны"),
            MinWidth = 214,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateButtonStyle(),
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Выберите сразу четыре карты изополей, ранее сохранённый комплект или готовый файл с контурами зон."
        };
        chooseButton.Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, TrueBimTheme.Spacing8);
        chooseButton.Click += (_, _) => ChooseSourceFile();
        buttonRow.Children.Add(chooseButton);
        recognizeButton.Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, TrueBimTheme.Spacing8);
        buttonRow.Children.Add(recognizeButton);
        saveSourceSetManifestButton.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8);
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

        Border continuePanel = TrueBimUi.CreateInfoBanner(
            new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "Следующий шаг",
                        FontWeight = FontWeights.SemiBold,
                        Foreground = TrueBimBrushes.TextPrimary
                    },
                    new TextBlock
                    {
                        Text = "Проверьте автоматически назначенные грани, при необходимости измените их и нажмите кнопку ниже. Искать эту команду в начале списка не нужно.",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = TrueBimBrushes.TextSecondary,
                        Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, TrueBimTheme.Spacing8)
                    },
                    recognizeBottomButton
                }
            },
            TrueBimUiSeverity.Info);
        continuePanel.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        content.Children.Add(continuePanel);

        return CreatePanel(content);
    }

    private Border CreatePreviewPanel()
    {
        StackPanel content = CreatePanelContent("2. Проверьте найденные зоны");

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
        content.Children.Add(buttonRow);

        previewStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        content.Children.Add(previewStatusText);

        return CreatePanel(content);
    }

    private Border CreateHostPanel()
    {
        StackPanel content = CreatePanelContent("3. Выберите стену или плиту");

        StackPanel buttonRow = new()
        {
            Orientation = Orientation.Horizontal
        };

        buttonRow.Children.Add(selectHostButton);

        clearHostButton.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8);
        buttonRow.Children.Add(clearHostButton);

        content.Children.Add(buttonRow);

        hostStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        content.Children.Add(hostStatusText);
        content.Children.Add(slabBindingExpander);

        return CreatePanel(content);
    }

    private Border CreateCalibrationPanel()
    {
        StackPanel content = CreatePanelContent("Дополнительно");

        StackPanel calibrationContent = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0)
        };

        StackPanel rows = new();
        rows.Children.Add(CreateInputRow("Начало по горизонтали", calibrationAnchorXInput));
        rows.Children.Add(CreateInputRow("Начало по вертикали", calibrationAnchorYInput));
        rows.Children.Add(CreateInputRow("Масштаб, мм на точку", calibrationMillimetersPerPixelInput));
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
            ToolTip = "Проверить масштаб и положение вспомогательных линий."
        };
        applyCalibrationButton.Click += (_, _) => ApplyCalibration(showDialogOnError: true);
        calibrationContent.Children.Add(applyCalibrationButton);

        calibrationStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        calibrationContent.Children.Add(calibrationStatusText);

        content.Children.Add(new Expander
        {
            Header = "Настройка вспомогательных линий на виде",
            Content = calibrationContent,
            IsExpanded = false,
            ToolTip = "Эти значения влияют только на вспомогательные линии на виде и не используются при расчёте арматуры."
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
            "Выберите три одинаковых угла на карте и на плите: точку 1 — общий угол, точку 2 — далеко вдоль одной стороны, точку 3 — вдоль соседней стороны. Третья точка задаёт второй масштаб; небольшое различие масштаба X/Y на изображении компенсируется автоматически.",
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
        applySlabBindingButton.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8);
        actions.Children.Add(applySlabBindingButton);
        content.Children.Add(actions);

        WrapPanel profileActions = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0)
        };
        profileActions.Children.Add(loadSlabBindingProfileButton);
        saveSlabBindingProfileButton.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8);
        profileActions.Children.Add(saveSlabBindingProfileButton);
        content.Children.Add(profileActions);

        slabBindingStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        content.Children.Add(slabBindingStatusText);

        content.Children.Add(new TextBlock
        {
            Text = "Проверка на виде Revit",
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary,
            Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, TrueBimTheme.Spacing4)
        });
        TextBlock previewNote = CreateMutedText(
            "После успешной привязки вспомогательные линии строятся в координатах выбранной конструкции и обрезаются по её контуру.");
        previewNote.Margin = new Thickness(0);
        content.Children.Add(previewNote);
        WrapPanel previewActions = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0)
        };
        previewActions.Children.Add(showRevitPreviewButton);
        clearRevitPreviewButton.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8);
        previewActions.Children.Add(clearRevitPreviewButton);
        content.Children.Add(previewActions);
        return new Expander
        {
            Header = "Совмещение карты с конструкцией по трём точкам",
            Content = content,
            IsExpanded = true,
            ToolTip = "Три пары одинаковых точек помогают правильно совместить карту с выбранной стеной или плитой и обрезать зоны по её границам и отверстиям."
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
        pickButton.Margin = new Thickness(TrueBimTheme.Spacing12, 0, 0, TrueBimTheme.Spacing8);
        row.Children.Add(pickButton);
        return row;
    }

    private Border CreateRulePanel()
    {
        StackPanel content = CreatePanelContent("4. Рассчитайте и проверьте арматуру");
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
            "Отступ арматуры от поверхности, мм",
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
            "По толщине конструкции стержни направления X располагаются ближе к выбранной грани, а стержни направления Y — глубже с зазором 5 мм. Для стены используются внутренняя и наружная стороны, для плиты — низ и верх. Перед выпуском обязательно проверьте раскладку по стандартам проекта.");
        layerOrderNote.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        settings.Children.Add(layerOrderNote);
        content.Children.Add(new Expander
        {
            Header = "Параметры раскладки",
            Content = settings,
            IsExpanded = true,
            ToolTip = "Параметры влияют на расчёт количества и фактическое положение стержней."
        });
        content.Children.Add(new TextBlock
        {
            Text = "1. Выполните расчёт",
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary,
            Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, TrueBimTheme.Spacing4)
        });
        previewRulesButton.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing4);
        content.Children.Add(previewRulesButton);
        content.Children.Add(CreateQualityCheckPanel());

        StackPanel finalActions = new();
        finalActions.Children.Add(new TextBlock
        {
            Text = "2. Сверьте результат и только затем примените",
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary
        });
        TextBlock finalActionsNote = CreateMutedText(
            "Сравнение и отчёт не изменяют модель. Нижняя кнопка применения — единственное действие, которое создаёт или обновляет семейства дополнительного армирования.");
        finalActionsNote.Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, TrueBimTheme.Spacing8);
        finalActions.Children.Add(finalActionsNote);

        WrapPanel reviewActions = new();

        compareChangesButton.Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, TrueBimTheme.Spacing8);
        reviewActions.Children.Add(compareChangesButton);

        exportReportButton.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8);
        reviewActions.Children.Add(exportReportButton);
        finalActions.Children.Add(reviewActions);

        createTestRebarButton.Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, 0);
        createTestRebarButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        createTestRebarButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        finalActions.Children.Add(createTestRebarButton);

        Border finalActionsPanel = new()
        {
            Background = TrueBimBrushes.SurfaceAlt,
            BorderBrush = TrueBimBrushes.Border,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Padding = new Thickness(TrueBimTheme.Spacing12),
            Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0),
            Child = finalActions
        };
        content.Children.Add(finalActionsPanel);

        ruleStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        content.Children.Add(ruleStatusText);
        rebarCreationStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        content.Children.Add(rebarCreationStatusText);
        content.Children.Add(completionSummaryPanel);
        content.Children.Add(CreateRebarReviewPanel());

        return CreatePanel(content);
    }

    private Border CreateCompletionSummaryPanel()
    {
        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            Text = "Последнее применение",
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary,
            TextWrapping = TextWrapping.Wrap
        });
        completionSummaryText.Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, 0);
        content.Children.Add(completionSummaryText);
        completionArtifactsText.Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, 0);
        content.Children.Add(completionArtifactsText);

        WrapPanel actions = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0)
        };
        actions.Children.Add(saveCompletionReportButton);
        openLastReportButton.Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, TrueBimTheme.Spacing8);
        actions.Children.Add(openLastReportButton);
        openLogButton.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8);
        actions.Children.Add(openLogButton);
        content.Children.Add(actions);

        Border panel = TrueBimUi.CreateInfoBanner(content, TrueBimUiSeverity.Success);
        panel.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        panel.Visibility = Visibility.Collapsed;
        return panel;
    }

    private UIElement CreateQualityCheckPanel()
    {
        StackPanel content = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0)
        };
        content.Children.Add(qualityStatusText);
        content.Children.Add(qualityIssuesPanel);
        content.Children.Add(qualityWarningsAcceptedInput);
        return new Expander
        {
            Header = "Проверка зон и арматуры",
            Content = content,
            IsExpanded = true,
            ToolTip = "Проверяет, все ли четыре карты покрывают конструкцию, не пересекаются ли зоны одной карты, не выходят ли они за границы и хватает ли площади арматуры."
        };
    }

    private UIElement CreateRebarReviewPanel()
    {
        StackPanel content = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0)
        };
        content.Children.Add(TrueBimUi.CreateInfoBanner(
            "До применения проверьте каждую строку и итоговые количества. Чтобы объединить соседние зоны, выделите их вместе. Отбор строк меняет только таблицу и не влияет на расчёт.",
            TrueBimUiSeverity.Info));

        WrapPanel filters = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, TrueBimTheme.Spacing8)
        };
        filters.Children.Add(CreateReviewFilterField("Поиск", reviewSearchInput));
        filters.Children.Add(CreateReviewFilterField("Карта", reviewLayerFilter));
        filters.Children.Add(CreateReviewFilterField("Результат", reviewStatusFilter));
        filters.Children.Add(CreateReviewFilterField("Диаметр", reviewDiameterFilter));
        filters.Children.Add(CreateReviewFilterField("Шаг", reviewSpacingFilter));
        filters.Children.Add(CreateReviewFilterField("Распознано", reviewConfidenceFilter));
        content.Children.Add(filters);

        WrapPanel zoneActions = new()
        {
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };
        zoneActions.Children.Add(editZoneRuleButton);
        mergeZonesButton.Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, TrueBimTheme.Spacing8);
        zoneActions.Children.Add(mergeZonesButton);
        unmergeZonesButton.Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, TrueBimTheme.Spacing8);
        zoneActions.Children.Add(unmergeZonesButton);
        excludeEmptyZonesButton.Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, TrueBimTheme.Spacing8);
        zoneActions.Children.Add(excludeEmptyZonesButton);
        resetZoneRulesButton.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8);
        zoneActions.Children.Add(resetZoneRulesButton);
        content.Children.Add(zoneActions);
        content.Children.Add(rebarReviewGrid);
        content.Children.Add(reviewSummaryText);

        return new Expander
        {
            Header = "Проверка зон и изменений",
            Content = content,
            IsExpanded = true,
            ToolTip = "Таблица позволяет настроить, исключить или объединить зоны, а после сравнения показывает, какие стержни будут добавлены, изменены или удалены."
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
            ColumnHeaderHeight = 44,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            IsReadOnly = true,
            MinHeight = 220,
            MaxHeight = 340,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            FrozenColumnCount = 2,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Style = TrueBimStyles.CreateDataGridStyle(),
            ItemsSource = rebarReviewRows
        };
        grid.Columns.Add(CreateReviewColumn("Карта", nameof(IsoFieldRebarReviewRow.LayerText), 76));
        grid.Columns.Add(CreateReviewColumn("Зона", nameof(IsoFieldRebarReviewRow.ZoneName), new DataGridLength(1, DataGridLengthUnitType.Star), 160));
        grid.Columns.Add(CreateReviewColumn("Результат", nameof(IsoFieldRebarReviewRow.StatusText), 112));
        grid.Columns.Add(CreateReviewColumn("Направление / грань", nameof(IsoFieldRebarReviewRow.FaceDirectionText), 142));
        grid.Columns.Add(CreateReviewColumn("Армирование", nameof(IsoFieldRebarReviewRow.ReinforcementText), 150));
        grid.Columns.Add(CreateReviewColumn("Площадь", nameof(IsoFieldRebarReviewRow.AreaText), 108));
        grid.Columns.Add(CreateReviewColumn("Стержни", nameof(IsoFieldRebarReviewRow.EstimatedBarCountText), 72));
        grid.Columns.Add(CreateReviewColumn("Распознано", nameof(IsoFieldRebarReviewRow.ConfidenceText), 92));
        grid.Columns.Add(CreateReviewColumn("Настройка", nameof(IsoFieldRebarReviewRow.SettingText), 160));
        grid.Columns.Add(CreateReviewColumn("Изменения семейств", nameof(IsoFieldRebarReviewRow.ChangeSummary), 168));
        return grid;
    }

    private static DataGridTextColumn CreateReviewColumn(
        string header,
        string bindingPath,
        double width)
    {
        return CreateReviewColumn(header, bindingPath, new DataGridLength(width), width);
    }

    private static DataGridTextColumn CreateReviewColumn(
        string header,
        string bindingPath,
        DataGridLength width,
        double minWidth)
    {
        Style elementStyle = new(typeof(TextBlock));
        elementStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        elementStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.NoWrap));
        elementStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new WpfBinding(bindingPath)));
        return new DataGridTextColumn
        {
            Header = new TextBlock
            {
                Text = header,
                TextWrapping = TextWrapping.Wrap,
                ToolTip = header,
                VerticalAlignment = VerticalAlignment.Center
            },
            Binding = new WpfBinding(bindingPath),
            Width = width,
            MinWidth = minWidth,
            ElementStyle = elementStyle
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
        WpfComboBox comboBox = new()
        {
            ItemsSource = itemsSource,
            DisplayMemberPath = displayMemberPath,
            SelectedIndex = 0,
            Width = width,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateComboBoxStyle()
        };
        ForwardMouseWheelToPage(comboBox);
        return comboBox;
    }

    private static void ForwardMouseWheelToPage(WpfComboBox comboBox)
    {
        comboBox.PreviewMouseWheel += (_, eventArgs) =>
        {
            if (comboBox.IsDropDownOpen)
            {
                return;
            }

            DependencyObject? current = VisualTreeHelper.GetParent(comboBox);
            while (current is not null && current is not ScrollViewer)
            {
                current = VisualTreeHelper.GetParent(current);
            }

            if (current is not ScrollViewer scrollViewer)
            {
                return;
            }

            eventArgs.Handled = true;
            int lines = Math.Max(1, Math.Min(3, SystemParameters.WheelScrollLines));
            for (int index = 0; index < lines; index++)
            {
                if (eventArgs.Delta > 0)
                {
                    scrollViewer.LineUp();
                }
                else
                {
                    scrollViewer.LineDown();
                }
            }
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
            RefreshZoneRuleActions();
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
        RefreshZoneRuleActions();
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
        string mergeSummary = zoneMerges.Count > 0
            ? $"Объединений: {zoneMerges.Count}. "
            : string.Empty;
        reviewSummaryText.Text = $"Зон: {rebarReviewRows.Count}; показано: {visibleCount}. {overrideSummary}{mergeSummary}{planSummary}";
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
        if (rebarReviewGrid.SelectedItems.Count != 1
            || rebarReviewGrid.SelectedItem is not IsoFieldRebarReviewRow selectedRow
            || selectedRow.IsMerged
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

    private void MergeSelectedZones()
    {
        if (configuredRulePreview is null)
        {
            return;
        }

        IsoFieldRebarReviewRow[] selectedRows = rebarReviewGrid.SelectedItems
            .OfType<IsoFieldRebarReviewRow>()
            .ToArray();
        if (selectedRows.Length < 2)
        {
            return;
        }

        try
        {
            IsoFieldRebarZoneMerge merge = rebarZoneMergeService.CreateMerge(
                configuredRulePreview,
                selectedRows.Select(row => row.ZoneId).ToArray());
            IsoFieldRebarZoneMerge[] candidateMerges = zoneMerges
                .Concat([merge])
                .ToArray();
            RebarRulePreviewResult mergedPreview = rebarZoneMergeService.Apply(
                configuredRulePreview,
                candidateMerges);
            zoneMerges.Add(merge);
            ApplyManualPreview(
                mergedPreview,
                $"Объединено зон: {merge.SourceZoneIds.Count}. Проверьте непрерывную раскладку и выполните сравнение заново.");
            logger.Info($"IsoField engineering zones merged. MergeId={merge.MergedZoneId}; Sources={string.Join(",", merge.SourceZoneIds)}; MergeGroups={zoneMerges.Count}.");
        }
        catch (InvalidOperationException exception)
        {
            rebarCreationStatusText.Text = exception.Message;
            footerStatusText.Text = "Зоны не объединены. Модель Revit не изменялась.";
            TaskDialog.Show("Армирование по изополям", exception.Message);
            logger.Warning($"IsoField engineering zone merge rejected. {exception.Message}");
        }
    }

    private void UnmergeSelectedZones()
    {
        if (configuredRulePreview is null || zoneMerges.Count == 0)
        {
            return;
        }

        HashSet<string> selectedMergeIds = rebarReviewGrid.SelectedItems
            .OfType<IsoFieldRebarReviewRow>()
            .Where(row => row.IsMerged)
            .Select(row => row.ZoneId)
            .ToHashSet(StringComparer.Ordinal);
        int removed = zoneMerges.RemoveAll(merge => selectedMergeIds.Contains(merge.MergedZoneId));
        if (removed == 0)
        {
            return;
        }

        RebarRulePreviewResult preview = rebarZoneMergeService.Apply(
            configuredRulePreview,
            zoneMerges);
        ApplyManualPreview(
            preview,
            $"Снято объединений: {removed}. Исходные зоны восстановлены в расчёте.");
        logger.Info($"IsoField engineering zone merges removed. Removed={removed}; Remaining={zoneMerges.Count}.");
    }

    private void ExcludeZonesWithoutBars()
    {
        RebarRulePreviewItem[] emptyItems = GetZonesWithoutBars();
        if (emptyItems.Length == 0)
        {
            return;
        }

        foreach (RebarRulePreviewItem item in emptyItems)
        {
            ruleOverrides[item.ZoneId] = new IsoFieldRebarRuleOverride(
                item.ZoneId,
                false,
                item.Rule.ReinforcementLabel ?? string.Empty);
        }

        ApplyZoneRuleOverrides();
        rebarCreationStatusText.Text =
            $"Исключено пустых фрагментов: {emptyItems.Length}. Проверьте предупреждения и выполните сравнение с моделью.";
        logger.Info(
            $"IsoField empty reinforcement zones excluded in batch. Count={emptyItems.Length}; "
            + $"ZoneIds={string.Join(",", emptyItems.Select(item => item.ZoneId))}.");
    }

    private RebarRulePreviewItem[] GetZonesWithoutBars()
    {
        return currentRulePreview?.Items
            .Where(item => item.IsIncluded
                && item.Diagnostics.Any(diagnostic => diagnostic.IndexOf(
                    "не осталось стержней",
                    StringComparison.OrdinalIgnoreCase) >= 0))
            .ToArray()
            ?? Array.Empty<RebarRulePreviewItem>();
    }

    private void ResetManualZoneConfiguration()
    {
        if (ruleOverrides.Count == 0 && zoneMerges.Count == 0)
        {
            return;
        }

        TaskDialog dialog = new("Армирование по изополям")
        {
            MainInstruction = "Сбросить все ручные изменения раскладки?",
            MainContent = $"Будут удалены настройки правил: {ruleOverrides.Count}; объединения: {zoneMerges.Count}. Исходные расчётные зоны восстановятся, сравнение с моделью потребуется выполнить заново.",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No
        };
        if (dialog.Show() != TaskDialogResult.Yes)
        {
            return;
        }

        ruleOverrides.Clear();
        zoneMerges.Clear();
        ApplyZoneRuleOverrides();
    }

    private void ApplyZoneRuleOverrides()
    {
        if (calculatedRulePreview is null)
        {
            return;
        }

        configuredRulePreview = rebarRuleOverrideService.Apply(calculatedRulePreview, ruleOverrides);
        currentRulePreview = rebarZoneMergeService.Apply(configuredRulePreview, zoneMerges);
        ApplyManualPreview(
            currentRulePreview,
            currentRulePreview.CanCreateRebar
                ? $"Ручные правила: {ruleOverrides.Count}; объединения: {zoneMerges.Count}. Выполните сравнение с моделью заново."
                : "Ручная конфигурация содержит ошибки. Исправьте строки, выделенные в таблице.");
        logger.Info($"IsoField manual zone configuration applied. Overrides={ruleOverrides.Count}; MergeGroups={zoneMerges.Count}; ActiveZones={currentRulePreview.ActiveItems.Count}; EstimatedBars={currentRulePreview.EstimatedBarCount}; CanCreate={currentRulePreview.CanCreateRebar}.");
    }

    private void ApplyManualPreview(
        RebarRulePreviewResult preview,
        string statusMessage)
    {
        ResetCompletionSummaryForWorkflowChange();
        currentRulePreview = preview;
        currentChangePlan = null;
        currentChangePlanFingerprint = null;
        familyPreflightError = null;
        EvaluateQualityCheck();
        ruleStatusText.Text = FormatRulePreview(currentRulePreview);
        rebarCreationStatusText.Text = statusMessage;
        footerStatusText.Text = "Раскладка пересчитана после ручных изменений. Модель Revit не изменялась.";
        if (currentRecognitionResult is not null && currentSlabBinding is not null)
        {
            RenderPreview(currentRecognitionResult);
        }

        RefreshRebarReviewRows();
        RefreshWorkflowState();
    }

    private void EvaluateQualityCheck()
    {
        areQualityWarningsAccepted = false;
        currentQualityResult = currentRulePreview?.IsEngineeringPreview == true
            && currentSlabBinding is not null
            ? rebarQualityService.Analyze(currentRulePreview, currentSlabBinding)
            : null;
        qualityWarningsAcceptedInput.IsChecked = false;
        UpdateQualityCheckPresentation();
        if (currentQualityResult is not null)
        {
            logger.Info(
                $"IsoField geometry quality evaluated. Blocking={currentQualityResult.BlockingIssues.Count}; "
                + $"Warnings={currentQualityResult.Warnings.Count}; Fingerprint={currentQualityResult.Fingerprint}.");
        }
    }

    private void ResetQualityCheck()
    {
        currentQualityResult = null;
        areQualityWarningsAccepted = false;
        qualityWarningsAcceptedInput.IsChecked = false;
        UpdateQualityCheckPresentation();
    }

    private void SetQualityWarningsAccepted(bool accepted)
    {
        areQualityWarningsAccepted = accepted
            && currentQualityResult?.Warnings.Count > 0;
        UpdateQualityCheckPresentation();
        RefreshWorkflowState();
        if (areQualityWarningsAccepted)
        {
            logger.Info(
                $"IsoField geometry quality warnings accepted by user. "
                + $"Warnings={currentQualityResult!.Warnings.Count}; Fingerprint={currentQualityResult.Fingerprint}.");
        }
    }

    private void UpdateQualityCheckPresentation()
    {
        qualityIssuesPanel.Children.Clear();
        if (currentQualityResult is null)
        {
            qualityStatusText.Text = "Проверка будет выполнена автоматически после расчёта раскладки.";
            qualityStatusText.Foreground = TrueBimBrushes.TextMuted;
            qualityStatusText.ToolTip = null;
            qualityWarningsAcceptedInput.Visibility = Visibility.Collapsed;
            qualityWarningsAcceptedInput.IsEnabled = false;
            return;
        }

        int blockingCount = currentQualityResult.BlockingIssues.Count;
        int warningCount = currentQualityResult.Warnings.Count;
        TrueBimUiSeverity severity = blockingCount > 0
            ? TrueBimUiSeverity.Danger
            : warningCount > 0
                ? TrueBimUiSeverity.Warning
                : TrueBimUiSeverity.Success;
        qualityStatusText.Text = blockingCount > 0
            ? $"Проверка заблокировала раскладку: ошибок {blockingCount}, предупреждений {warningCount}."
            : warningCount > 0
                ? areQualityWarningsAccepted
                ? $"Проверка: предупреждений {warningCount}; пользователь подтвердил текущую раскладку."
                : $"Проверка: предупреждений {warningCount}. Просмотрите список и подтвердите решение."
            : "Проверка пройдена: пересечений, выхода за границы конструкции и недостатка арматуры не найдено.";
        qualityStatusText.Foreground = TrueBimBrushes.ForSeverity(severity);
        string coverageText = string.Join(
            " · ",
            currentQualityResult.LayerCoverage.Select(coverage =>
                $"{FormatLayerRole(coverage.LayerRole)} {coverage.CoverageRatio:P0}"));
        qualityStatusText.ToolTip = "Покрытие выбранной конструкции: " + coverageText;

        const int visibleIssueCount = 8;
        foreach (IsoFieldRebarQualityIssue issue in currentQualityResult.Issues.Take(visibleIssueCount))
        {
            qualityIssuesPanel.Children.Add(new TextBlock
            {
                Text = issue.Severity == IsoFieldRebarQualitySeverity.Blocking
                    ? "Ошибка: " + issue.Message
                    : "Предупреждение: " + issue.Message,
                Foreground = issue.Severity == IsoFieldRebarQualitySeverity.Blocking
                    ? TrueBimBrushes.Danger
                    : TrueBimBrushes.Warning,
                Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing4),
                TextWrapping = TextWrapping.Wrap
            });
        }

        int hiddenIssueCount = Math.Max(0, currentQualityResult.Issues.Count - visibleIssueCount);
        if (hiddenIssueCount > 0)
        {
            qualityIssuesPanel.Children.Add(CreateMutedText(
                $"Ещё сообщений: {hiddenIssueCount}. Полный список будет сохранён в подробном отчёте."));
        }

        qualityWarningsAcceptedInput.Visibility = warningCount > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        qualityWarningsAcceptedInput.IsEnabled = warningCount > 0;
    }

    private void RefreshZoneRuleActions()
    {
        IsoFieldRebarReviewRow[] selectedRows = rebarReviewGrid?.SelectedItems
            .OfType<IsoFieldRebarReviewRow>()
            .ToArray()
            ?? Array.Empty<IsoFieldRebarReviewRow>();
        IsoFieldRebarReviewRow? selectedRow = selectedRows.Length == 1 ? selectedRows[0] : null;
        bool canEdit = selectedRow is not null
            && !selectedRow.IsMerged
            && calculatedRulePreview?.EngineeringSettings is not null
            && calculatedRulePreview.Items.Any(item =>
                string.Equals(item.ZoneId, selectedRow.ZoneId, StringComparison.Ordinal)
                && item.Rule.LayerRole == selectedRow.LayerRole);
        editZoneRuleButton.IsEnabled = canEdit;
        editZoneRuleButton.ToolTip = canEdit
            ? "Изменить сочетание диаметр/шаг или исключить выбранную зону до сравнения с моделью."
            : selectedRow?.IsMerged == true
                ? "Сначала разъедините строку, затем настройте исходные зоны по отдельности."
                : "Выберите одну расчётную строку зоны. Ранее созданные зоны только на удаление не редактируются.";

        bool canMerge = selectedRows.Length >= 2
            && configuredRulePreview is not null
            && selectedRows.All(row => !row.IsMerged
                && configuredRulePreview.Items.Any(item =>
                    string.Equals(item.ZoneId, row.ZoneId, StringComparison.Ordinal)));
        mergeZonesButton.IsEnabled = canMerge;
        mergeZonesButton.ToolTip = canMerge
            ? $"Объединить выбранные зоны: {selectedRows.Length}. Допустимы одинаковые правила и единый непрерывный регион."
            : "Выделите не менее двух исходных расчётных зон, удерживая клавишу выбора нескольких строк. Объединённые и устаревшие строки не подходят.";

        bool canUnmerge = selectedRows.Length > 0
            && selectedRows.All(row => row.IsMerged
                && zoneMerges.Any(merge => string.Equals(
                    merge.MergedZoneId,
                    row.ZoneId,
                    StringComparison.Ordinal)));
        unmergeZonesButton.IsEnabled = canUnmerge;
        unmergeZonesButton.ToolTip = canUnmerge
            ? $"Восстановить исходные зоны для объединений: {selectedRows.Length}."
            : "Выберите одну или несколько объединённых строк.";

        int emptyZoneCount = GetZonesWithoutBars().Length;
        excludeEmptyZonesButton.IsEnabled = emptyZoneCount > 0;
        excludeEmptyZonesButton.Content = emptyZoneCount > 0
            ? $"Исключить без стержней ({emptyZoneCount})"
            : "Исключить без стержней";
        excludeEmptyZonesButton.ToolTip = emptyZoneCount > 0
            ? $"Явно исключить пустые фрагменты зон: {emptyZoneCount}. Модель Revit не изменится."
            : "Пустых фрагментов, которые можно исключить пакетно, нет.";

        resetZoneRulesButton.IsEnabled = ruleOverrides.Count > 0 || zoneMerges.Count > 0;
        resetZoneRulesButton.ToolTip = resetZoneRulesButton.IsEnabled
            ? $"Сбросить ручные правила: {ruleOverrides.Count}; объединения: {zoneMerges.Count}."
            : "Ручных правил и объединений пока нет.";
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
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing12, TrueBimTheme.Spacing8),
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

        StackPanel nextActionContent = new();
        nextActionContent.Children.Add(workflowStageText);
        workflowActionText.Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, TrueBimTheme.Spacing8);
        nextActionContent.Children.Add(workflowActionText);
        nextActionContent.Children.Add(workflowActionButton);
        Border nextActionPanel = TrueBimUi.CreateInfoBanner(
            nextActionContent,
            TrueBimUiSeverity.Info);
        nextActionPanel.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        content.Children.Add(nextActionPanel);

        workflowSummaryText.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        content.Children.Add(workflowSummaryText);
        content.Children.Add(sourceStepText);
        content.Children.Add(mappingStepText);
        content.Children.Add(zonesStepText);
        content.Children.Add(hostStepText);
        content.Children.Add(rulesStepText);
        content.Children.Add(comparisonStepText);

        TextBlock note = CreateMutedText("Кнопка выше всегда показывает следующее обязательное действие. Применение станет доступно только после отдельного сравнения с моделью. Арматуру, созданную вручную, модуль не изменяет.");
        note.Margin = new Thickness(0, TrueBimTheme.Spacing16, 0, 0);
        content.Children.Add(note);

        return CreatePanel(content);
    }

    private void RunWorkflowPrimaryAction()
    {
        switch (workflowPrimaryAction)
        {
            case WorkflowPrimaryAction.ChooseSource:
                ChooseSourceFile();
                break;
            case WorkflowPrimaryAction.ShowSourceMappings:
                sourceSetRows.BringIntoView();
                footerStatusText.Text = "Проверьте назначения в таблице карт и при необходимости измените значения в столбцах «Карта» и «Грань».";
                break;
            case WorkflowPrimaryAction.RunRecognition:
                RunRecognition();
                break;
            case WorkflowPrimaryAction.SelectHost:
                SelectHostElement();
                break;
            case WorkflowPrimaryAction.ShowBinding:
                slabBindingExpander.IsExpanded = true;
                slabBindingExpander.BringIntoView();
                footerStatusText.Text = "Укажите три точки на карте и те же три точки на конструкции, затем нажмите «Проверить привязку».";
                break;
            case WorkflowPrimaryAction.LoadBinding:
                LoadSlabBindingProfile();
                break;
            case WorkflowPrimaryAction.CalculateRules:
                PreviewRebarRulesSafely();
                break;
            case WorkflowPrimaryAction.ExcludeEmptyZones:
                ExcludeZonesWithoutBars();
                break;
            case WorkflowPrimaryAction.CompareWithModel:
                CompareEngineeringChanges();
                break;
            case WorkflowPrimaryAction.ApplyChanges:
                CreateTestRebar();
                break;
            case WorkflowPrimaryAction.SaveCompletionReport:
                SaveCompletionReport();
                break;
        }
    }

    private void UpdateWorkflowGuidance(
        IsoFieldWorkflowState state,
        bool hasCompletedCurrentWorkflow,
        bool completionReportIsCurrent,
        int qualityBlockingCount,
        int qualityWarningCount)
    {
        string stage;
        string action;
        string buttonText;
        TrueBimIcon buttonIcon;
        WorkflowPrimaryAction primaryAction;
        bool isEnabled;
        int emptyZoneCount = GetZonesWithoutBars().Length;

        if (hasCompletedCurrentWorkflow)
        {
            stage = "Готово · Изменения применены";
            if (completionReportIsCurrent)
            {
                action = "Итоговый отчёт сохранён. Можно закрыть окно или выбрать новый комплект карт.";
                buttonText = "Работа завершена";
                buttonIcon = TrueBimIcon.Apply;
                primaryAction = WorkflowPrimaryAction.None;
                isEnabled = false;
            }
            else
            {
                action = "Последний обязательный результат — сохраните итоговый JSON/CSV-отчёт.";
                buttonText = "Сохранить итоговый отчёт";
                buttonIcon = TrueBimIcon.Export;
                primaryAction = WorkflowPrimaryAction.SaveCompletionReport;
                isEnabled = saveCompletionReportButton.IsEnabled;
            }
        }
        else if (selectedSourceSet is { IsComplete: false } incompleteSourceSet)
        {
            stage = "Этап 1 из 4 · Карты изополей";
            action = FormatSourceSetIssues(incompleteSourceSet);
            bool canRepairInTable = incompleteSourceSet.Files.Count == IsoFieldSourceSet.RequiredRoles.Count
                && incompleteSourceSet.Files.All(file => file.HasValidImageSize)
                && incompleteSourceSet.HasConsistentImageSize;
            buttonText = canRepairInTable ? "Показать таблицу карт" : "Выбрать карты заново";
            buttonIcon = canRepairInTable ? TrueBimIcon.Settings : TrueBimIcon.Open;
            primaryAction = canRepairInTable
                ? WorkflowPrimaryAction.ShowSourceMappings
                : WorkflowPrimaryAction.ChooseSource;
            isEnabled = true;
        }
        else if (!state.HasSource)
        {
            stage = "Этап 1 из 4 · Карты изополей";
            action = "Выберите сразу четыре карты с метками As1X, As2X, As3Y и As4Y в именах.";
            buttonText = "Выбрать 4 карты";
            buttonIcon = TrueBimIcon.Open;
            primaryAction = WorkflowPrimaryAction.ChooseSource;
            isEnabled = true;
        }
        else if (!state.HasConfirmedLayerMappings)
        {
            stage = "Этап 1 из 4 · Проверьте стороны";
            action = "Исправьте столбец «Грань» в таблице слева: для X и Y нужна одна нижняя и одна верхняя карта.";
            buttonText = "Показать таблицу карт";
            buttonIcon = TrueBimIcon.Settings;
            primaryAction = WorkflowPrimaryAction.ShowSourceMappings;
            isEnabled = true;
        }
        else if (!state.HasZones)
        {
            stage = "Этап 1 из 4 · Поиск зон";
            action = "Комплект и стороны готовы. Запустите распознавание четырёх карт; модель Revit не изменится.";
            buttonText = "Найти зоны на 4 картах";
            buttonIcon = TrueBimIcon.Preview;
            primaryAction = WorkflowPrimaryAction.RunRecognition;
            isEnabled = state.CanRunRecognition;
        }
        else if (!state.HasHost || !state.HasSupportedHostGeometry)
        {
            stage = "Этап 3 из 4 · Конструкция";
            action = state.HasHost
                ? "Выбранная конструкция не поддерживается. Укажите прямую обычную стену или горизонтальную плиту."
                : "Зоны готовы. Теперь укажите прямую стену или горизонтальную плиту в модели.";
            buttonText = state.HasHost ? "Выбрать другую конструкцию" : "Выбрать стену/плиту";
            buttonIcon = TrueBimIcon.Apply;
            primaryAction = WorkflowPrimaryAction.SelectHost;
            isEnabled = uiDocument is not null;
        }
        else if (!state.HasValidHostBinding)
        {
            stage = "Этап 3 из 4 · Совмещение";
            if (availableSlabBindingProfile is not null)
            {
                action = "Для этой конструкции найдена сохранённая привязка. Восстановите её и проверьте наложение зон.";
                buttonText = "Восстановить привязку";
                buttonIcon = TrueBimIcon.Import;
                primaryAction = WorkflowPrimaryAction.LoadBinding;
                isEnabled = loadSlabBindingProfileButton.IsEnabled;
            }
            else
            {
                action = "Укажите три одинаковые точки на карте и конструкции, затем нажмите «Проверить привязку».";
                buttonText = "Открыть привязку по 3 точкам";
                buttonIcon = TrueBimIcon.Settings;
                primaryAction = WorkflowPrimaryAction.ShowBinding;
                isEnabled = true;
            }
        }
        else if (emptyZoneCount > 0)
        {
            stage = "Этап 4 из 4 · Пустые фрагменты зон";
            action = $"После отступа и проверки минимальной длины в {emptyZoneCount} фрагментах не осталось стержней. "
                + $"Нажмите «Исключить без стержней ({emptyZoneCount})»: команда исключит их только из расчёта и не изменит модель Revit. "
                + "Вместо исключения можно объединить фрагменты либо уменьшить отступ или минимальную длину и пересчитать.";
            buttonText = $"Исключить без стержней ({emptyZoneCount})";
            buttonIcon = TrueBimIcon.Close;
            primaryAction = WorkflowPrimaryAction.ExcludeEmptyZones;
            isEnabled = true;
        }
        else if (qualityBlockingCount > 0)
        {
            stage = "Этап 4 из 4 · Исправьте ошибки";
            action = $"Проверка нашла ошибок: {qualityBlockingCount}. Исправьте зоны или параметры и пересчитайте раскладку.";
            buttonText = "Пересчитать раскладку";
            buttonIcon = TrueBimIcon.Refresh;
            primaryAction = WorkflowPrimaryAction.CalculateRules;
            isEnabled = state.CanCalculateRules;
        }
        else if (!state.HasValidRules)
        {
            stage = "Этап 4 из 4 · Расчёт";
            action = "Нажмите «Рассчитать раскладку», затем проверьте количество и сообщения контроля качества.";
            buttonText = "Рассчитать раскладку";
            buttonIcon = TrueBimIcon.Preview;
            primaryAction = WorkflowPrimaryAction.CalculateRules;
            isEnabled = state.CanCalculateRules;
        }
        else if (!state.HasComparedWithModel)
        {
            stage = "Этап 4 из 4 · Обязательное сравнение";
            action = !string.IsNullOrWhiteSpace(familyPreflightError)
                ? familyPreflightError! + " После загрузки или исправления семейства повторите сравнение."
                : "Раскладка рассчитана. Нажмите «Сравнить с моделью»: команда только читает модель и покажет, какие экземпляры массивов изменятся.";
            buttonText = string.IsNullOrWhiteSpace(familyPreflightError)
                ? "Сравнить с моделью"
                : "Повторить сравнение";
            buttonIcon = TrueBimIcon.Refresh;
            primaryAction = WorkflowPrimaryAction.CompareWithModel;
            isEnabled = state.CanCompareWithModel;
        }
        else if (qualityWarningCount > 0 && !areQualityWarningsAccepted)
        {
            stage = "Этап 4 из 4 · Решение по предупреждениям";
            action = $"Проверьте предупреждения ({qualityWarningCount}) слева и установите флажок подтверждения. После этого станет доступно применение.";
            buttonText = "Подтвердите предупреждения слева";
            buttonIcon = TrueBimIcon.Settings;
            primaryAction = WorkflowPrimaryAction.None;
            isEnabled = false;
        }
        else if (currentChangePlan?.CanApply == true && currentChangePlan.HasChanges)
        {
            stage = "Этап 4 из 4 · Применение";
            int plannedFamilyCount = CountPlannedFamilyInstances(currentChangePlan);
            action = $"Расчётных стержней: {currentRulePreview?.EstimatedBarCount ?? 0}; "
                + $"экземпляров семейств-массивов: {plannedFamilyCount}. "
                + $"{currentChangePlan.Summary} Проверьте таблицу и примените изменения.";
            buttonText = "Применить изменения";
            buttonIcon = TrueBimIcon.Apply;
            primaryAction = WorkflowPrimaryAction.ApplyChanges;
            isEnabled = createTestRebarButton.IsEnabled;
        }
        else if (currentChangePlan?.CanApply == true)
        {
            stage = "Готово · Модель уже соответствует расчёту";
            int plannedFamilyCount = CountPlannedFamilyInstances(currentChangePlan);
            action = $"Расчётных стержней: {currentRulePreview?.EstimatedBarCount ?? 0}; "
                + $"экземпляров семейств-массивов: {plannedFamilyCount}. "
                + currentChangePlan.Summary
                + " Применять нечего; при необходимости сохраните отчёт.";
            buttonText = "Изменений нет";
            buttonIcon = TrueBimIcon.Apply;
            primaryAction = WorkflowPrimaryAction.None;
            isEnabled = false;
        }
        else
        {
            stage = "Этап 4 из 4 · Сравнение заблокировано";
            action = currentChangePlan is null
                ? state.NextAction
                : string.Join(" ", currentChangePlan.Diagnostics);
            buttonText = "Повторить сравнение";
            buttonIcon = TrueBimIcon.Refresh;
            primaryAction = WorkflowPrimaryAction.CompareWithModel;
            isEnabled = state.CanCompareWithModel;
        }

        workflowStageText.Text = stage;
        workflowActionText.Text = action;
        workflowActionButton.Content = IconFactory.CreateButtonContent(buttonIcon, buttonText);
        workflowActionButton.IsEnabled = isEnabled;
        workflowActionButton.ToolTip = action;
        ToolTipService.SetShowOnDisabled(workflowActionButton, true);
        workflowPrimaryAction = primaryAction;
    }

    private static int CountPlannedFamilyInstances(IsoFieldRebarChangePlan changePlan)
    {
        return changePlan.Changes.Count(change => change.PlannedItem is not null);
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
                    selectedFileText.Text = "Выбор отклонён: готовый файл с зонами нельзя выбирать вместе с картами.";
                    selectedFileText.Foreground = TrueBimBrushes.Danger;
                    selectedFileText.ToolTip = null;
                    ClearPreview("Контуры не загружены: выберите один готовый файл с зонами или четыре карты.");
                    recognitionStatusText.Text = "Готовые зоны нужно выбирать отдельно от комплекта карт.";
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
                selectedFileText.Text = $"Готовые зоны: {Path.GetFileName(selectedPath)}";
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
                "Не удалось выбрать файл изополей. Откройте журнал работы, чтобы узнать подробности.");
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
        ClearPreview("Сохранённый комплект восстановлен. Зоны нужно найти заново.");
        footerStatusText.Text = selectedSourceSet.IsComplete
                ? "Сохранённый комплект загружен и проверен. Модель Revit не изменялась."
                : "Сохранённый комплект загружен, но исходные карты не прошли проверку.";
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
                footerStatusText.Text = "Комплект не сохранён: сначала исправьте выбранные карты.";
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
                footerStatusText.Text = "Сохранение комплекта отменено.";
                logger.Info("IsoField source-set manifest save canceled.");
                return;
            }

            sourceSetManifestService.Save(selectedSourceSet, manifestPath!);
            selectedSourceSetManifestPath = manifestPath;
            isSourceSetManifestDirty = false;
            UpdateManifestStatus();
            footerStatusText.Text = "Комплект карт сохранён. Модель Revit не изменялась.";
            logger.Info($"IsoField source-set manifest saved. File={Path.GetFileName(manifestPath)}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.Error("Failed to save IsoField source-set manifest.", exception);
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось сохранить комплект карт. Откройте журнал работы, чтобы узнать подробности.");
            footerStatusText.Text = "Не удалось сохранить комплект карт.";
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
            ? $"Комплект готов: назначены все 4 карты, по заголовкам подтверждено {headerRoleCount} из 4. Стартовые грани заполнены автоматически."
            : $"Комплект не готов: назначено карт {assignedCount} из 4.";
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
            ? "Комплект проверен. Проверьте предложенные грани и нажмите «Найти зоны на 4 картах»."
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
            ? selectedHostElement?.IsWall == true
                ? "Назначение готово: для X и Y выбрано по одной внутренней и наружной карте стены. Проверьте стартовый вариант."
                : selectedHostElement?.IsSlab == true
                    ? "Назначение готово: As1/As3 — низ, As2/As4 — верх плиты. При необходимости измените его."
                    : "Стартовое назначение заполнено: As1/As3 — сторона 1, As2/As4 — сторона 2. После выбора конструкции проверьте подписи."
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
            manifestStatusText.Text = "Комплект ещё не сохранён.";
            manifestStatusText.Foreground = TrueBimBrushes.TextMuted;
            manifestStatusText.ToolTip = null;
            return;
        }

        manifestStatusText.Text = isSourceSetManifestDirty
            ? $"Комплект изменён после загрузки {Path.GetFileName(selectedSourceSetManifestPath)} — сохраните его заново."
            : $"Сохранённый комплект: {Path.GetFileName(selectedSourceSetManifestPath)}";
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
            ItemsSource = SourceRoleOptions,
            DisplayMemberPath = nameof(IsoFieldSourceRoleOption.Label),
            MinHeight = TrueBimTheme.ControlHeight32,
            VerticalAlignment = VerticalAlignment.Center,
            Style = TrueBimStyles.CreateComboBoxStyle(),
            ToolTip = roleDetection.ToolTip
        };
        ForwardMouseWheelToPage(roleSelector);
        if (sourceFile.Role.HasValue)
        {
            roleSelector.SelectedItem = SourceRoleOptions.First(option => option.Role == sourceFile.Role.Value);
        }

        roleSelector.SelectionChanged += (_, _) =>
        {
            if (roleSelector.SelectedItem is IsoFieldSourceRoleOption option && sourceFile.Role != option.Role)
            {
                AssignSourceRole(sourceFile.FilePath, option.Role);
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
        IReadOnlyList<IsoFieldFaceOption> layerFaceOptions = BuildLayerFaceOptions();
        WpfComboBox faceSelector = new()
        {
            ItemsSource = layerFaceOptions,
            DisplayMemberPath = nameof(IsoFieldFaceOption.Label),
            MinHeight = TrueBimTheme.ControlHeight32,
            VerticalAlignment = VerticalAlignment.Center,
            Style = TrueBimStyles.CreateComboBoxStyle(),
            IsEnabled = selectedSourceSet?.IsComplete == true && sourceFile.Role.HasValue,
            ToolTip = selectedSourceSet?.IsComplete == true
                ? selectedHostElement?.IsWall == true
                    ? "Укажите внутреннюю или наружную сторону стены для этой карты."
                    : selectedHostElement?.IsSlab == true
                        ? "Укажите верхнюю или нижнюю сторону плиты для этой карты."
                        : "Назначьте сторону 1 или 2; после выбора стены или плиты подписи станут понятнее."
                : "Сначала исправьте состав и роли комплекта."
        };
        ForwardMouseWheelToPage(faceSelector);
        faceSelector.SelectedItem = layerFaceOptions.First(option => option.Face == (mapping?.Face ?? IsoFieldRebarFace.Unconfirmed));
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
            ? $" Распознано: {detection.HeaderConfidence.Value:P0}."
            : string.Empty;
        return detection.Kind switch
        {
            IsoFieldRoleDetectionKind.FileNameAndHeader => new RoleDetectionPresentation(
                "Карта определена по имени и заголовку",
                $"Имя файла и заголовок карты совпадают: {FormatLayerRole(detection.HeaderRole!.Value)}.{confidence}",
                TrueBimBrushes.Success),
            IsoFieldRoleDetectionKind.Header => new RoleDetectionPresentation(
                "Карта определена по заголовку",
                $"В имени файла назначение не указано; по заголовку определено: {FormatLayerRole(detection.HeaderRole!.Value)}.{confidence}",
                TrueBimBrushes.Success),
            IsoFieldRoleDetectionKind.FileName => new RoleDetectionPresentation(
                "Карта определена только по имени файла",
                $"Не удалось прочитать заголовок карты; по имени файла определено: {FormatLayerRole(detection.FileNameRole!.Value)}. Проверьте назначение.",
                TrueBimBrushes.Warning),
            IsoFieldRoleDetectionKind.Conflict => new RoleDetectionPresentation(
                "Имя и заголовок не совпадают",
                $"Имя файла указывает «{FormatLayerRole(detection.FileNameRole!.Value)}», а заголовок — «{FormatLayerRole(detection.HeaderRole!.Value)}».{confidence} Выберите правильное назначение вручную.",
                TrueBimBrushes.Danger),
            IsoFieldRoleDetectionKind.Manual => new RoleDetectionPresentation(
                "Карта назначена вручную",
                "Назначение карты подтверждено вручную. При смене файла проверьте его заново.",
                TrueBimBrushes.Warning),
            IsoFieldRoleDetectionKind.Manifest => new RoleDetectionPresentation(
                "Карта взята из сохранённого комплекта",
                "Назначение восстановлено из ранее проверенного комплекта карт.",
                TrueBimBrushes.Success),
            _ => new RoleDetectionPresentation(
                "Карта не определена",
                "Назначение не найдено ни в имени файла, ни в заголовке карты. Выберите его вручную.",
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

        TextBlock roleHeader = CreateMutedText("Карта");
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
        ClearPreview("Назначение карты изменено. Найдите зоны на четырёх картах заново.");
        footerStatusText.Text = selectedSourceSet.IsComplete
            ? "Назначение карт исправлено; комплект готов."
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
            ? "Назначение сторон подтверждено для всех карт."
            : "Сторона карты изменена; заполните оставшиеся карты.";
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
            ? "Укажите назначение каждой из четырёх карт."
            : string.Join(" ", sourceSet.ValidationMessages);
    }

    private void ReadJsonSource(string path)
    {
        logger.Info($"IsoField JSON source read started: {Path.GetFileName(path)}.");
        IsoFieldRecognitionResult result = jsonReader.Read(path);
        currentRecognitionResult = result;
        ResetSlabBindingForSource(result);
        recognitionStatusText.Text = $"Готовые зоны загружены. Контуров: {result.Polylines.Count}. Замечаний: {result.Diagnostics.Count}.";
        recognitionStatusText.ToolTip = CreateRecognitionDiagnosticsToolTip(result);
        UpdateLegendPresentation(result);
        RenderPreview(result);
        ClearRulePreview("Для расчёта арматуры нужен комплект из четырёх карт и выбранная стена или плита.");
        footerStatusText.Text = "Готовые зоны загружены для просмотра. Модель Revit не изменялась.";
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
                    "Выберите четыре карты и укажите для каждой направление и номер.");
                footerStatusText.Text = "Обработка не запущена: комплект не готов.";
                return;
            }

            logger.Info(
                $"IsoField source set recognition started. Runner={ResolveRecognitionRunnerName()}; "
                + $"Files={selectedSourceSet.Files.Count}.");
            IsoFieldRecognitionResult result = sourceSetRecognitionService.Run(selectedSourceSet, recognitionRunner);
            currentRecognitionResult = result;
            ResetSlabBindingForSource(result);
            recognitionStatusText.Text = $"Четыре карты обработаны. Найдено контуров: {result.Polylines.Count}. Цветовых шкал: {result.EffectiveLegends.Count} из 4. Замечаний: {result.Diagnostics.Count}.";
            recognitionStatusText.ToolTip = CreateRecognitionDiagnosticsToolTip(result);
            UpdateLegendPresentation(result);
            RenderPreview(result);
            ClearRulePreview(result.Polylines.Count == 0
                ? "Раскладка не рассчитана: на картах не найдены зоны."
                : "После выбора и привязки стены или плиты нажмите «Рассчитать раскладку».");
            footerStatusText.Text = "Поиск зон завершён. Модель Revit не изменялась.";
            logger.Info(
                $"IsoField source set recognition completed. Polylines={result.Polylines.Count}; "
                + $"Legends={result.EffectiveLegends.Count}; Diagnostics={result.Diagnostics.Count}.");
        }
        catch (Exception exception)
        {
            logger.Error("Failed to run IsoField recognition.", exception);
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось найти зоны на картах. Откройте журнал работы, чтобы узнать подробности.");
            footerStatusText.Text = "Не удалось найти зоны на картах.";
        }
    }

    private void ShowRevitPreview()
    {
        footerStatusText.Text = "Создание вспомогательных линий передано в Revit.";
        revitActions.Raise(ShowRevitPreviewInRevitContext);
    }

    private void CorrectZones()
    {
        if (currentRecognitionResult is null || currentRecognitionResult.Polylines.Count == 0)
        {
            footerStatusText.Text = "Исправление недоступно: сначала загрузите зоны.";
            logger.Warning("IsoField zone correction was requested without recognition polylines.");
            return;
        }

        int sourceCount = currentRecognitionResult.Polylines.Count;
        bool hadSlabBinding = currentSlabBinding is not null;
        IsoFieldZoneCorrectionWindow correctionWindow = new(
            currentRecognitionResult,
            currentSlabBinding?.RemovedZoneIds)
        {
            Owner = this
        };
        if (correctionWindow.ShowDialog() != true || correctionWindow.Result is null)
        {
            footerStatusText.Text = "Исправление зон отменено. Текущий результат не изменён.";
            logger.Info("IsoField zone correction canceled.");
            return;
        }

        currentRecognitionResult = correctionWindow.Result;
        recognitionStatusText.Text = $"Зоны проверены вручную. Было: {sourceCount}; стало: {currentRecognitionResult.Polylines.Count}. Замечаний: {currentRecognitionResult.Diagnostics.Count}.";
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
        ClearRulePreview("Раскладка сброшена после исправления зон. Рассчитайте её заново для выбранной стены или плиты.");
        footerStatusText.Text = activeRevitPreviewIds.Count > 0
            ? "Зоны обновлены. Повторно нажмите «Показать линии на виде», чтобы заменить старые вспомогательные линии."
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
            TaskDialog.Show("Армирование по изополям", "Откройте документ Revit перед созданием вспомогательных линий.");
            return;
        }

        if (currentRecognitionResult is null || currentRecognitionResult.Polylines.Count == 0)
        {
            logger.Warning("IsoField Revit preview was requested without recognition polylines.");
            TaskDialog.Show("Армирование по изополям", "Сначала загрузите карты изополей или готовый файл с зонами.");
            return;
        }

        if (currentSlabBinding?.CanProceed != true)
        {
            const string message = "Сначала выберите конструкцию, укажите три соответствующих угла и выполните «Проверить привязку». Линии без проверенной привязки больше не создаются.";
            logger.Warning("IsoField bound Revit preview was requested without a valid host binding.");
            TaskDialog.Show("Армирование по изополям", message);
            footerStatusText.Text = message;
            return;
        }

        try
        {
            logger.Info($"IsoField bound Revit preview requested. Zones={currentSlabBinding.ClippedZones.Count}; ExistingPreviewIds={activeRevitPreviewIds.Count}.");
            IsoFieldRevitPreviewResult result = revitPreviewService.Show(
                uiDocument,
                currentSlabBinding,
                activeRevitPreviewIds);
            activeRevitPreviewIds = result.CreatedElementIds;
            footerStatusText.Text = result.Message;
            RefreshWorkflowState();
            logger.Info($"IsoField bound Revit preview command completed. Created={result.CreatedCount}; Deleted={result.DeletedCount}.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or Autodesk.Revit.Exceptions.ApplicationException or Autodesk.Revit.Exceptions.ArgumentException)
        {
            logger.Error("Failed to create IsoField Revit preview lines.", exception);
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось создать вспомогательные линии в Revit. Откройте план, разрез, фасад или чертёжный вид и повторите попытку. Подробности есть в журнале работы.");
            footerStatusText.Text = "Не удалось создать вспомогательные линии в Revit.";
        }
    }

    private void ClearRevitPreview()
    {
        footerStatusText.Text = "Удаление вспомогательных линий передано в Revit.";
        revitActions.Raise(ClearRevitPreviewInRevitContext);
    }

    private void ClearRevitPreviewInRevitContext()
    {
        if (uiDocument is null)
        {
            logger.Warning("IsoField Revit preview clear was requested without an open Revit document.");
            TaskDialog.Show("Армирование по изополям", "Откройте документ Revit перед удалением вспомогательных линий.");
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
                "Не удалось удалить вспомогательные линии в Revit. Откройте журнал работы, чтобы узнать подробности.");
            footerStatusText.Text = "Не удалось удалить вспомогательные линии в Revit.";
        }
    }

    private void SelectHostElement()
    {
        footerStatusText.Text = "Перейдите в Revit и выберите стену или плиту.";
        revitActions.Raise(SelectHostElementInRevitContext);
    }

    private void SelectHostElementInRevitContext()
    {
        if (uiDocument is null)
        {
            logger.Warning("IsoField host selection was requested without an open Revit document.");
            TaskDialog.Show("Армирование по изополям", "Откройте проект Revit, затем выберите стену или плиту.");
            return;
        }

        if (uiDocument.ActiveView is Autodesk.Revit.DB.ViewSheet)
        {
            const string message = "Откройте план, разрез или 3D-вид, затем повторите выбор стены или плиты. На листе выбирать конструкцию нельзя.";
            logger.Info("IsoField host selection deferred because the active view is a sheet.");
            TaskDialog.Show("Армирование по изополям", message);
            footerStatusText.Text = message;
            RefreshWorkflowState();
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
            UpdateSourceSetPresentation();
            ClearRulePreview("Совместите карты с выбранной конструкцией, затем рассчитайте раскладку.");
            IsoFieldHostSupportResult support = hostSupportService.Analyze(hostElement);
            footerStatusText.Text = support.IsSupported
                ? $"Конструкция выбрана: {hostElement.DisplayName}. Модель Revit не изменялась."
                : $"Конструкция выбрана, но расчёт и создание арматуры недоступны: {support.Message}";
            logger.Info(
                $"IsoField host selected. Kind={hostElement.HostKind}; ElementId={hostElement.ElementId}; "
                + $"Name='{hostElement.Name}'; GeometryProfile={hostElement.GeometryProfile}; "
                + $"SupportMode={support.Mode}; SupportCode={support.Code}.");
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            footerStatusText.Text = "Выбор стены или плиты отменён.";
            logger.Info("IsoField host selection canceled.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or Autodesk.Revit.Exceptions.ApplicationException or Autodesk.Revit.Exceptions.ArgumentException)
        {
            logger.Error("Failed to select IsoField host element.", exception);
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось выбрать конструкцию. Выберите стену или плиту. Если ошибка повторится, откройте журнал работы.");
            footerStatusText.Text = "Не удалось выбрать стену или плиту.";
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
        UpdateSourceSetPresentation();
        ClearRulePreview("Раскладка не рассчитана: стена или плита больше не выбрана.");
        footerStatusText.Text = "Выбор стены или плиты сброшен. Модель Revit не изменялась.";
        logger.Info("IsoField host selection cleared.");
    }

    private void RefreshHostStatus()
    {
        if (selectedHostElement is null)
        {
            hostStatusText.Text = "Стена или плита не выбрана.";
            hostStatusText.Foreground = TrueBimBrushes.TextSecondary;
            hostStatusText.ToolTip = null;
            return;
        }

        IsoFieldHostSupportResult support = hostSupportService.Analyze(selectedHostElement);
        hostStatusText.Text = $"{selectedHostElement.DisplayName}. {support.Message}";
        hostStatusText.Foreground = support.IsSupported
            ? TrueBimBrushes.TextSecondary
            : TrueBimBrushes.Danger;
        hostStatusText.ToolTip = support.Message;
    }

    private void PickSlabControlPoint(int pointNumber)
    {
        footerStatusText.Text = $"Перейдите в Revit и укажите контрольную точку {pointNumber}.";
        revitActions.Raise(() => PickSlabControlPointInRevitContext(pointNumber));
    }

    private void PickSlabControlPointInRevitContext(int pointNumber)
    {
        if (uiDocument is null || selectedHostElement is null)
        {
            SetSlabBindingStatus(
                "Сначала выберите поддерживаемую стену или плиту в открытом документе Revit.",
                TrueBimUiSeverity.Warning);
            return;
        }

        Visibility previousVisibility = Visibility;
        try
        {
            Visibility = Visibility.Hidden;
            IsoFieldPoint point = hostSelectionService.PickPlanarControlPoint(
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
                $"Точка {pointNumber} привязана к ближайшему углу конструкции. Укажите оставшиеся точки или нажмите «Проверить привязку».",
                TrueBimUiSeverity.Info);
            footerStatusText.Text = $"Контрольная точка {pointNumber} привязана к углу. Модель Revit не изменялась.";
            logger.Info(
                $"IsoField planar host control point selected. Point={pointNumber}; "
                + $"LocalFeet=({point.X}; {point.Y}); HostId={selectedHostElement.ElementId}.");
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            footerStatusText.Text = $"Выбор контрольной точки {pointNumber} отменён.";
            logger.Info($"IsoField planar host control point selection canceled. Point={pointNumber}.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or Autodesk.Revit.Exceptions.ApplicationException or Autodesk.Revit.Exceptions.ArgumentException)
        {
            logger.Error($"Failed to select IsoField planar host control point {pointNumber}.", exception);
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
            || selectedHostElement is null
            || selectedHostElement.Geometry is null
            || slabHostPoint1Feet is null
            || slabHostPoint2Feet is null
            || slabHostPoint3Feet is null)
        {
            string message = "Для проверки загрузите зоны, выберите прямую стену или горизонтальную плиту и укажите на ней три контрольные точки.";
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
                ? $"Привязка по трём точкам проверена. Масштаб X: {FormatNumber(currentSlabBinding.Transform.MillimetersPerPixel)} мм/точку; "
                    + $"Y: {FormatNumber(currentSlabBinding.Transform.SecondaryMillimetersPerPixel)} мм/точку; "
                    + $"обрезано зон: {currentSlabBinding.ClippedZoneIds.Count}; исключено: {currentSlabBinding.RemovedZoneIds.Count}."
                : !currentSlabBinding.AreControlPointsInside
                    ? "Привязка не принята: одна или несколько контрольных точек находятся за границей конструкции."
                    : !currentSlabBinding.IsThirdPointValid
                        ? "Привязка не принята: три точки не задают согласованную геометрию. Проверьте соответствие и порядок углов, затем переключатель переворота."
                        : currentSlabBinding.RemovedZoneIds.Count > 0
                            ? $"Привязка не принята: за границами конструкции осталось зон {currentSlabBinding.RemovedZoneIds.Count}."
                            : "Привязка не принята. Подробности показаны во всплывающей подсказке статуса.";
            SetSlabBindingStatus(
                status,
                !currentSlabBinding.CanProceed
                    ? TrueBimUiSeverity.Danger
                    : currentSlabBinding.ClippedZoneIds.Count > 0
                        || currentSlabBinding.RemovedZoneIds.Count > 0
                        ? TrueBimUiSeverity.Warning
                        : TrueBimUiSeverity.Success,
                string.Join(Environment.NewLine, currentSlabBinding.Diagnostics));
            if (renderPreview)
            {
                RenderPreview(currentRecognitionResult);
            }

            ClearRulePreview(currentSlabBinding.CanProceed
            ? "Привязка проверена. Рассчитайте раскладку заново."
            : "Расчёт недоступен: исправьте совмещение зон с конструкцией.");
            footerStatusText.Text = currentSlabBinding.CanProceed
            ? "Схема совмещения зон с конструкцией построена. Модель Revit не изменялась."
            : "Совмещение требует исправления. Модель Revit не изменялась.";
            logger.Info(
                $"IsoField planar host binding analyzed. CanProceed={currentSlabBinding.CanProceed}; "
                + $"OutsideZones={currentSlabBinding.OutsideZoneCount}; "
                + $"RetainedAreaRatio={currentSlabBinding.RetainedAreaRatio}; "
                + $"ClippedZones={currentSlabBinding.ClippedZoneIds.Count}; "
                + $"RemovedZones={currentSlabBinding.RemovedZoneIds.Count}; "
                + $"ThirdPointDeviationMm={currentSlabBinding.ThirdPointDeviationMillimeters}; "
                + $"ScaleXMmPerPixel={currentSlabBinding.Transform.MillimetersPerPixel}; "
                + $"ScaleYMmPerPixel={currentSlabBinding.Transform.SecondaryMillimetersPerPixel}; "
                + $"AxisScaleDifferencePercent={currentSlabBinding.Transform.AxisScaleDifferencePercent}; "
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
        IsoFieldSourceFile? referenceImage = selectedSourceSet?.Files
            .FirstOrDefault(file => file.HasValidImageSize);
        if (referenceImage?.PixelWidth is int imageWidth
            && referenceImage.PixelHeight is int imageHeight)
        {
            (string Label, IsoFieldPoint Point)[] imagePoints =
            [
                ("Точка 1", input.ImagePoint1),
                ("Точка 2", input.ImagePoint2),
                ("Точка 3", input.ImagePoint3!)
            ];
            (string Label, IsoFieldPoint Point)? outsidePoint = imagePoints
                .Where(item =>
                    item.Point.X < 0
                    || item.Point.Y < 0
                    || item.Point.X > imageWidth
                    || item.Point.Y > imageHeight)
                .Select(item => ((string Label, IsoFieldPoint Point)?)item)
                .FirstOrDefault();
            if (outsidePoint.HasValue)
            {
                errorMessage = $"{outsidePoint.Value.Label} на карте находится за пределами изображения {imageWidth}×{imageHeight}. Допустимы X от 0 до {imageWidth} и Y от 0 до {imageHeight}.";
                return false;
            }
        }

        errorMessage = string.Empty;
        return true;
    }

    private void LoadSlabBindingProfile()
    {
        if (availableSlabBindingProfile is null)
        {
            SetSlabBindingStatus(
                "Для этого проекта, вида и выбранной конструкции сохранённая привязка не найдена.",
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
            ? "Сохранённая привязка восстановлена и проверена на текущих зонах. Модель Revit не изменялась."
            : "Сохранённая привязка восстановлена, но не подошла к текущим зонам.";
        logger.Info(
            $"IsoField planar host binding profile loaded. HostId={availableSlabBindingProfile.HostElementId}; "
            + $"ViewId={availableSlabBindingProfile.ViewId}; Valid={isValid}.");
    }

    private void SaveSlabBindingProfile()
    {
        if (currentSlabBinding?.CanProceed != true
            || selectedHostElement?.Geometry is null)
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
            logger.Error("Failed to save IsoField planar host binding profile.", exception);
            SetSlabBindingStatus(
                "Не удалось сохранить привязку. Проверьте доступ к папке настроек и журнал работы TrueBIM.",
                TrueBimUiSeverity.Danger);
            return;
        }

        availableSlabBindingProfile = profile;
        SetSlabBindingStatus(
            "Привязка сохранена для текущего вида и выбранной конструкции.",
            TrueBimUiSeverity.Success,
            slabBindingProfileStorage.SettingsPath);
        footerStatusText.Text = "Привязка сохранена. Модель Revit не изменялась.";
        logger.Info(
            $"IsoField planar host binding profile saved. HostId={profile.HostElementId}; "
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

        ClearRulePreview("Параметры привязки изменены. Проверьте совмещение заново.");
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
        slabHostPoint1Text.Text = "Точка 1 на конструкции не указана.";
        slabHostPoint2Text.Text = "Точка 2 на конструкции не указана.";
        slabHostPoint3Text.Text = "Точка 3 на конструкции не указана.";
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
            (false, { Geometry: not null }) => "Загрузите или распознайте зоны, затем задайте три пары контрольных точек.",
            (true, { Geometry: not null }) when availableSlabBindingProfile is not null => "Зоны загружены. Восстановите сохранённую привязку или задайте три точки заново.",
            (true, { IsWall: true, Geometry: not null }) => "Укажите три соответствующие точки на наружной плоскости выбранной стены.",
            (true, { IsSlab: true, Geometry: not null }) => "Укажите три соответствующие точки на верхней грани выбранной плиты.",
            _ => "Выберите поддерживаемую прямую стену или горизонтальную плиту, затем задайте три пары контрольных точек."
        };
        SetSlabBindingStatus(status, TrueBimUiSeverity.Info);
    }

    private void ResetSlabBindingForHost()
    {
        currentSlabBinding = null;
        slabHostPoint1Feet = null;
        slabHostPoint2Feet = null;
        slabHostPoint3Feet = null;
        slabHostPoint1Text.Text = "Точка 1 на конструкции не указана.";
        slabHostPoint2Text.Text = "Точка 2 на конструкции не указана.";
        slabHostPoint3Text.Text = "Точка 3 на конструкции не указана.";
        availableSlabBindingProfile = selectedHostElement is { Geometry: not null }
            ? slabBindingProfileStorage.TryLoad(
                documentKey,
                selectedHostViewId,
                selectedHostElement.ElementId)
            : null;
        string status = selectedHostElement switch
        {
            null => "Выберите поддерживаемую прямую стену или горизонтальную плиту, затем задайте три пары контрольных точек.",
            { Geometry: null } => "Не удалось определить ровную опорную поверхность конструкции. Привязка и расчёт недоступны.",
            _ when availableSlabBindingProfile is not null => "Для этой конструкции и вида найдена сохранённая привязка. Восстановите её или задайте три точки заново.",
            { IsWall: true } => "Стена готова. Укажите три соответствующие точки на её наружной плоскости.",
            _ => "Плита готова. Укажите три соответствующие точки на её верхней грани."
        };
        SetSlabBindingStatus(
            status,
            selectedHostElement is not null && selectedHostElement.Geometry is null
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
        return $"Точка {pointNumber} на конструкции: по горизонтали {FormatNumber(point.X * 304.8)} мм; по вертикали {FormatNumber(point.Y * 304.8)} мм.";
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

            footerStatusText.Text = "Не удалось применить начало и масштаб.";
            return false;
        }

        currentCalibration = calibration;
        RefreshCalibrationStatus();
        footerStatusText.Text = "Начало и масштаб применены. Модель Revit не изменялась.";
        logger.Info($"IsoField calibration applied. Anchor=({calibration.ImageAnchor.X}; {calibration.ImageAnchor.Y}); MillimetersPerPixel={calibration.MillimetersPerPixel}; InvertY={calibration.InvertImageY}.");
        return true;
    }

    private bool TryBuildCalibration(out IsoFieldCalibration calibration, out string errorMessage)
    {
        calibration = currentCalibration;
        if (!TryReadDouble(calibrationAnchorXInput, "Якорь X", out double anchorX, out errorMessage)
            || !TryReadDouble(calibrationAnchorYInput, "Якорь Y", out double anchorY, out errorMessage)
            || !TryReadDouble(calibrationMillimetersPerPixelInput, "Масштаб", out double millimetersPerPixel, out errorMessage))
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
        ResetCompletionSummaryForWorkflowChange();
        if (currentRecognitionResult is null || currentRecognitionResult.Polylines.Count == 0)
        {
            logger.Warning("IsoField rebar rules preview was requested without recognition polylines.");
            TaskDialog.Show("Армирование по изополям", "Сначала загрузите карты изополей или готовый файл с зонами.");
            ClearRulePreview("Правила не рассчитаны: нет контуров изополей.");
            return;
        }

        if (selectedHostElement is not null)
        {
            IsoFieldHostSupportResult support = hostSupportService.Analyze(selectedHostElement);
            if (!support.CanCalculateRules)
            {
                ClearRulePreview(support.Message);
                rebarCreationStatusText.Text = "Раскладка недоступна: выбранная стена или плита не подходит для расчёта.";
                footerStatusText.Text = support.Message;
                logger.Warning(
                    $"IsoField rule preview blocked by host preflight. HostId={selectedHostElement.ElementId}; "
                    + $"GeometryProfile={selectedHostElement.GeometryProfile}; SupportCode={support.Code}.");
                return;
            }
        }

        IsoFieldEngineeringSettings? engineeringSettings = null;
        if (selectedHostElement is not null
            && hostSupportService.Analyze(selectedHostElement).RequiresPlanarBinding
            && !TryBuildEngineeringSettings(out engineeringSettings, out string settingsError))
        {
            currentRulePreview = null;
            calculatedRulePreview = null;
            configuredRulePreview = null;
            ruleOverrides.Clear();
            zoneMerges.Clear();
            currentChangePlan = null;
            currentChangePlanFingerprint = null;
            familyPreflightError = null;
            ResetQualityCheck();
            RefreshRebarReviewRows();
            ruleStatusText.Text = settingsError;
            rebarCreationStatusText.Text = "Раскладка недоступна: исправьте параметры.";
            footerStatusText.Text = "Параметры раскладки требуют исправления.";
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
        configuredRulePreview = preview;
        ruleOverrides.Clear();
        zoneMerges.Clear();
        currentChangePlan = null;
        currentChangePlanFingerprint = null;
        EvaluateQualityCheck();
        RefreshRebarReviewRows();
        ruleStatusText.Text = FormatRulePreview(preview);
        bool mappingsReady = selectedSourceSet is null || selectedSourceSet.HasConfirmedLayerMappings;
        int qualityBlockingCount = currentQualityResult?.BlockingIssues.Count ?? 0;
        int qualityWarningCount = currentQualityResult?.Warnings.Count ?? 0;
        rebarCreationStatusText.Text = preview.CanCreateRebar && mappingsReady
            ? preview.IsEngineeringPreview
                ? qualityBlockingCount > 0
                ? $"Раскладка рассчитана, но проверка нашла ошибок: {qualityBlockingCount}."
                    : qualityWarningCount > 0
                    ? $"Рассчитано {preview.EstimatedBarCount} стержней. Проверьте предупреждения: {qualityWarningCount}. Сравнение с моделью ничего не изменит."
                    : $"Рассчитано {preview.EstimatedBarCount} стержней. Проверка пройдена; сравните раскладку с моделью."
                : "Готово к созданию пробного армирования после подтверждения."
            : preview.CanCreateRebar
                ? "Правила готовы, но назначение верх/низ не подтверждено."
            : "Армирование недоступно: исправьте замечания к зонам и раскладке.";
        footerStatusText.Text = preview.CanCreateRebar && mappingsReady
            ? preview.IsEngineeringPreview
            ? $"Раскладка рассчитана: зон {preview.Items.Count}, стержней {preview.EstimatedBarCount}; ошибок {qualityBlockingCount}, предупреждений {qualityWarningCount}. Модель Revit не изменялась."
                : $"Правила армирования рассчитаны: {preview.Items.Count}. Модель Revit не изменялась."
            : preview.CanCreateRebar
                ? "Правила рассчитаны; подтвердите назначение верх/низ перед созданием."
                : "Правила армирования требуют проверки.";
        logger.Info(
            $"IsoField rebar rules preview calculated. Items={preview.Items.Count}; EstimatedBars={preview.EstimatedBarCount}; "
            + $"Engineering={preview.IsEngineeringPreview}; Diagnostics={preview.Diagnostics.Count}; "
            + $"QualityBlocking={qualityBlockingCount}; QualityWarnings={qualityWarningCount}; CanCreateRebar={preview.CanCreateRebar}.");
        if (currentSlabBinding is not null)
        {
            RenderPreview(currentRecognitionResult);
        }

        RefreshWorkflowState();
    }

    private void PreviewRebarRulesSafely()
    {
        try
        {
            PreviewRebarRules();
        }
        catch (Exception exception)
        {
            logger.Error("Failed to calculate IsoField rebar rules preview.", exception);
            ClearRulePreview("Не удалось рассчитать раскладку. Подробности сохранены в журнале работы.");
            rebarCreationStatusText.Text = "Раскладка не рассчитана из-за внутренней ошибки плагина.";
            footerStatusText.Text = "Ошибка расчёта перехвачена; модель Revit не изменялась.";
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось рассчитать раскладку. Модель Revit не изменялась. Подробности сохранены в журнале работы.");
            RefreshWorkflowState();
        }
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

        if (!TryReadDouble(concreteCoverInput, "Отступ арматуры от поверхности", out double cover, out errorMessage)
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

        ClearRulePreview("Параметры изменены. Рассчитайте раскладку заново.");
        RefreshWorkflowState();
    }

    private void CreateTestRebar()
    {
        ReadyRebarContext? context = ResolveReadyRebarContext();
        if (context is null)
        {
            return;
        }

        if (context.Preview.IsEngineeringPreview
            && (currentChangePlan is null || string.IsNullOrWhiteSpace(currentChangePlanFingerprint)))
        {
            rebarCreationStatusText.Text = "Сначала нажмите «Сравнить с моделью» и проверьте список изменений семейств.";
            return;
        }

        if (!ConfirmCreateTestRebar(
            context.Preview,
            context.HostElement,
            selectedSourceSet,
            currentChangePlan))
        {
            rebarCreationStatusText.Text = "Применение изменений отменено.";
            footerStatusText.Text = "Применение изменений отменено пользователем.";
            logger.Info("IsoField array-family creation canceled by user.");
            return;
        }

        isApplyConfirmed = true;
        rebarCreationStatusText.Text = "Подождите: Revit применяет изменения.";
        revitActions.Raise(CreateTestRebarInRevitContext);
    }

    private void CompareEngineeringChanges()
    {
        familyPreflightError = null;
        SetCurrentChangePlan(null);
        rebarCreationStatusText.Text = "Подождите: Revit сравнивает раскладку с моделью.";
        revitActions.Raise(CompareEngineeringChangesInRevitContext);
    }

    private void ExportRebarReport(bool reuseLastPath = false)
    {
        if (currentRulePreview?.IsEngineeringPreview != true
            || currentQualityResult is null
            || currentRecognitionResult is null
            || selectedHostElement is null)
        {
            return;
        }

        string? jsonPath = reuseLastPath
            ? lastReportSaveResult?.JsonPath
            : null;
        if (string.IsNullOrWhiteSpace(jsonPath))
        {
            string? initialDirectory = ResolveReportInitialDirectory();
            string suggestedFileName = string.Concat(
                IsoFieldRebarReportService.DefaultFileNamePrefix,
                "-",
                DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture),
                ".json");
            jsonPath = filePicker.PickRebarReportSavePath(
                initialDirectory,
                suggestedFileName);
        }

        if (string.IsNullOrWhiteSpace(jsonPath))
        {
            footerStatusText.Text = "Сохранение отчёта отменено.";
            logger.Info("IsoField rebar report export canceled.");
            return;
        }

        try
        {
            IsoFieldRebarReport report = rebarReportService.Build(
                new IsoFieldRebarReportRequest(
                    documentTitle,
                    documentKey,
                    selectedHostElement,
                    currentRulePreview,
                    currentRecognitionResult,
                    BuildReportSourceInputs(),
                    selectedSourceSet is null ? "RecognitionJson" : "ImageSourceSet",
                    ResolveRecognitionRunnerName(),
                    ResolveRecognitionRunnerVersion(),
                    GetType().Assembly.GetName().Version?.ToString() ?? "неизвестно",
                    currentCalibration,
                    currentSlabBinding,
                    availableSlabBindingProfile,
                    currentChangePlan,
                    selectedSourceSetManifestPath,
                    currentQualityResult,
                    areQualityWarningsAccepted,
                    lastApplicationResult,
                    lastApplicationCompletedAtUtc));
            IsoFieldRebarReportSaveResult result = rebarReportService.Save(report, jsonPath!);
            lastReportSaveResult = result;
            lastReportApplicationRevision = applicationRevision;
            UpdateCompletionSummary();
            RefreshWorkflowState();
            string comparisonText = report.ChangeSummary.Compared
                ? "сравнение с моделью выполнено"
                : "сравнение с моделью ещё не выполнено";
            string qualityText = report.QualityCheck.BlockingErrorCount > 0
                ? $"ошибок проверки {report.QualityCheck.BlockingErrorCount}"
                : report.QualityCheck.WarningCount > 0
                    ? $"предупреждений {report.QualityCheck.WarningCount}"
                    : "проверка пройдена";
            rebarCreationStatusText.Text =
                $"Отчёт сохранён: зон {report.Zones.Count}, карт {report.LayerTotals.Count}; {qualityText}; {comparisonText}.";
            footerStatusText.Text =
                $"Сохранены подробный отчёт и таблица: {Path.GetFileNameWithoutExtension(result.JsonPath)}. Модель Revit не изменялась.";
            TaskDialog dialog = new("Армирование по изополям")
            {
                MainInstruction = "Отчёт сохранён",
                MainContent = $"Подробный отчёт: {result.JsonPath}{Environment.NewLine}Таблица: {result.CsvPath}{Environment.NewLine}Зон: {report.Zones.Count}; карт: {report.LayerTotals.Count}; {qualityText}; {comparisonText}."
            };
            dialog.Show();
            logger.Info(
                $"IsoField rebar report exported. Json='{result.JsonPath}'; Csv='{result.CsvPath}'; "
                + $"Zones={report.Zones.Count}; Layers={report.LayerTotals.Count}; "
                + $"QualityBlocking={report.QualityCheck.BlockingErrorCount}; QualityWarnings={report.QualityCheck.WarningCount}; "
                + $"QualityWarningsAccepted={report.QualityCheck.WarningsAccepted}; Compared={report.ChangeSummary.Compared}.");
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException)
        {
            logger.Error("Failed to export IsoField rebar report.", exception);
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось сохранить отчёт. Проверьте выбранную папку и доступ к исходным файлам. Подробности есть в журнале работы.");
            rebarCreationStatusText.Text = "Отчёт не сохранён. Откройте журнал работы, чтобы узнать подробности.";
            footerStatusText.Text = "Не удалось сохранить отчёт. Модель Revit не изменялась.";
        }
    }

    private void SaveCompletionReport()
    {
        ExportRebarReport(reuseLastPath: lastReportSaveResult is not null);
    }

    private void OpenLastReport()
    {
        string? reportPath = lastReportSaveResult?.JsonPath;
        if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath))
        {
            TaskDialog.Show(
                "Армирование по изополям",
                "Последний подробный отчёт не найден. Сохраните итоговый отчёт ещё раз.");
            UpdateCompletionSummary();
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = reportPath,
                UseShellExecute = true
            });
            logger.Info($"IsoField rebar report opened. Path='{reportPath}'.");
        }
        catch (Exception exception) when (exception is Win32Exception
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException)
        {
            logger.Error("Failed to open IsoField rebar report.", exception);
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось открыть последний отчёт. Проверьте, существует ли файл и есть ли программа для его просмотра.");
        }
    }

    private void OpenIsoFieldLog()
    {
        try
        {
            FileTrueBimLogger fileLogger = logger as FileTrueBimLogger
                ?? new FileTrueBimLogger(new TrueBimLogPaths());
            new TrueBimLogFileOpener(fileLogger).OpenLogFile();
        }
        catch (Exception exception) when (exception is Win32Exception
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException)
        {
            logger.Error("Failed to open TrueBIM log from IsoField rebar.", exception);
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось открыть журнал работы TrueBIM. Проверьте доступ к папке журналов программы.");
        }
    }

    private void ShowCompletionSummary(
        IsoFieldRebarCreationResult result,
        IsoFieldHostElement hostElement)
    {
        lastApplicationResult = result;
        lastApplicationCompletedAtUtc = DateTimeOffset.UtcNow;
        lastApplicationHost = hostElement;
        applicationRevision++;
        completionSummaryPanel.Visibility = Visibility.Visible;
        UpdateCompletionSummary();
    }

    private void UpdateCompletionSummary()
    {
        if (lastApplicationResult is null
            || lastApplicationCompletedAtUtc is null
            || lastApplicationHost is null)
        {
            completionSummaryPanel.Visibility = Visibility.Collapsed;
            return;
        }

        bool hasReportPath = !string.IsNullOrWhiteSpace(lastReportSaveResult?.JsonPath);
        bool jsonReportExists = hasReportPath
            && File.Exists(lastReportSaveResult!.JsonPath);
        bool reportPairExists = jsonReportExists
            && File.Exists(lastReportSaveResult!.CsvPath);
        bool reportIsCurrent = reportPairExists
            && lastReportApplicationRevision == applicationRevision;
        bool canSaveReport = currentRulePreview?.IsEngineeringPreview == true
            && currentQualityResult is not null
            && currentRecognitionResult is not null
            && selectedHostElement is not null;
        string completedAt = lastApplicationCompletedAtUtc.Value
            .ToLocalTime()
            .ToString("g", CultureInfo.CurrentCulture);
        completionSummaryText.Text =
            $"Добавлено: {lastApplicationResult.AddedCount} · обновлено: {lastApplicationResult.UpdatedCount} · "
            + $"удалено: {lastApplicationResult.DeletedCount} · без изменений: {lastApplicationResult.UnchangedCount}.";
        string reportStatus = reportIsCurrent
            ? $"Итоговый отчёт актуален: {Path.GetFileName(lastReportSaveResult!.JsonPath)}."
            : hasReportPath && !reportPairExists
                ? $"Файлы последнего отчёта неполны: {Path.GetFileName(lastReportSaveResult!.JsonPath)}. Сохраните их повторно."
            : hasReportPath
                ? $"Последний отчёт создан до этого применения: {Path.GetFileName(lastReportSaveResult!.JsonPath)}. Обновите его перед передачей."
                : "Итоговый отчёт ещё не сохранён.";
        completionArtifactsText.Text =
            $"{lastApplicationHost.DisplayName} · {completedAt}. {reportStatus} Журнал работы доступен по кнопке ниже.";

        saveCompletionReportButton.Visibility = reportIsCurrent
            ? Visibility.Collapsed
            : Visibility.Visible;
        saveCompletionReportButton.IsEnabled = canSaveReport;
        saveCompletionReportButton.Content = IconFactory.CreateButtonContent(
            TrueBimIcon.Export,
            hasReportPath ? "Обновить итоговый отчёт" : "Сохранить итоговый отчёт");
        saveCompletionReportButton.ToolTip = !canSaveReport
            ? "Итоговый подробный отчёт и таблица доступны для рассчитанной раскладки. Журнал работы можно открыть отдельно."
            : hasReportPath
                ? $"Обновить подробный отчёт и таблицу по пути {lastReportSaveResult!.JsonPath}."
                : "Сохранить подробный отчёт и таблицу с итогом последнего применения.";
        openLastReportButton.IsEnabled = jsonReportExists;
        openLastReportButton.ToolTip = jsonReportExists
            ? $"Открыть {lastReportSaveResult!.JsonPath}."
            : "Последний подробный отчёт не найден. Сначала сохраните итоговый отчёт.";
        openLogButton.ToolTip = $"Открыть {new TrueBimLogPaths().CurrentLogFile}.";
        completionSummaryPanel.Visibility = Visibility.Visible;
    }

    private void ResetCompletionSummaryForWorkflowChange()
    {
        lastApplicationResult = null;
        lastApplicationCompletedAtUtc = null;
        lastApplicationHost = null;
        completionSummaryPanel.Visibility = Visibility.Collapsed;
    }

    private IReadOnlyList<IsoFieldRebarReportSourceInput> BuildReportSourceInputs()
    {
        if (selectedSourceSet is not null)
        {
            return selectedSourceSet.Files
                .Select(file => new IsoFieldRebarReportSourceInput(
                    file.FilePath,
                    file.Role,
                    file.PixelWidth,
                    file.PixelHeight))
                .ToArray();
        }

        return string.IsNullOrWhiteSpace(selectedJsonPath)
            ? Array.Empty<IsoFieldRebarReportSourceInput>()
            : [new IsoFieldRebarReportSourceInput(selectedJsonPath!)];
    }

    private string? ResolveReportInitialDirectory()
    {
        string? path = selectedSourceSetManifestPath
            ?? selectedJsonPath
            ?? selectedSourceSet?.Files.FirstOrDefault()?.FilePath;
        string? directory = string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            return directory;
        }

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Directory.Exists(documents) ? documents : null;
    }

    private void CompareEngineeringChangesInRevitContext()
    {
        ReadyRebarContext? context = ResolveReadyRebarContext(requireWarningAcceptance: false);
        if (context is null)
        {
            return;
        }

        if (!context.Preview.IsEngineeringPreview)
        {
            TaskDialog.Show(
                "Армирование по изополям",
                "Сравнение доступно после расчёта раскладки для прямой стены или горизонтальной плиты.");
            return;
        }

        IsoFieldRebarChangePlan? changePlan = TryPreviewEngineeringChanges(context);
        if (changePlan is null)
        {
            return;
        }

        familyPreflightError = null;
        SetCurrentChangePlan(changePlan);
        int plannedFamilyCount = CountPlannedFamilyInstances(changePlan);
        rebarCreationStatusText.Text = changePlan.CanApply
            ? $"Расчётных стержней: {context.Preview.EstimatedBarCount}; "
                + $"экземпляров семейств-массивов: {plannedFamilyCount}. "
                + changePlan.Summary
            : "Изменения заблокированы: " + string.Join(" ", changePlan.Diagnostics);
        footerStatusText.Text = changePlan.CanApply
            ? "Семейства дополнительного армирования сравнены с экземплярами модуля. Проверьте таблицу; модель пока не изменялась."
            : "Сравнение выполнено, но план содержит ошибки.";
        logger.Info($"IsoField engineering array-family comparison completed. {changePlan.Summary} Diagnostics={changePlan.Diagnostics.Count}.");
    }

    private void CreateTestRebarInRevitContext()
    {
        if (!isApplyConfirmed)
        {
            return;
        }

        isApplyConfirmed = false;
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
                footerStatusText.Text = "Список изменений обновлён по текущей модели. Проверьте таблицу ещё раз.";
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
                string message = $"Семейства дополнительного армирования уже соответствуют расчётной раскладке. {changePlan.Summary}";
                TaskDialog.Show("Армирование по изополям", message);
                rebarCreationStatusText.Text = message;
                footerStatusText.Text = message;
                logger.Info($"IsoField engineering array-family apply skipped. {changePlan.Summary}");
                return;
            }
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
            ShowCompletionSummary(result, context.HostElement);
            logger.Info(
                $"IsoField rebar apply completed. Added={result.AddedCount}; Updated={result.UpdatedCount}; "
                + $"Deleted={result.DeletedCount}; Unchanged={result.UnchangedCount}; HostId={context.HostElement.ElementId}.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or Autodesk.Revit.Exceptions.ApplicationException or Autodesk.Revit.Exceptions.ArgumentException)
        {
            logger.Error("Failed to create IsoField array families.", exception);
            familyPreflightError = exception.Message;
            rebarCreationStatusText.Text = exception.Message;
            footerStatusText.Text = "Семейства дополнительного армирования не созданы; транзакция отменена.";
            RefreshWorkflowState();
        }
    }

    private ReadyRebarContext? ResolveReadyRebarContext(
        bool requireWarningAcceptance = true)
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
                "Подтвердите сторону конструкции для каждой карты перед созданием армирования.");
            rebarCreationStatusText.Text = "Армирование не создано: назначение карт не подтверждено.";
            return null;
        }

        if (selectedHostElement is null)
        {
            logger.Warning("IsoField test rebar creation was requested without selected host element.");
            TaskDialog.Show("Армирование по изополям", "Сначала выберите стену или плиту для размещения арматуры.");
            rebarCreationStatusText.Text = "Армирование не создано: стена или плита не выбрана.";
            return null;
        }

        IsoFieldHostSupportResult hostSupport = hostSupportService.Analyze(selectedHostElement);
        if (!hostSupport.CanApplyRebar)
        {
            logger.Warning(
                $"IsoField apply blocked by host preflight. HostId={selectedHostElement.ElementId}; "
                + $"GeometryProfile={selectedHostElement.GeometryProfile}; SupportCode={hostSupport.Code}.");
            TaskDialog.Show("Армирование по изополям", hostSupport.Message);
            rebarCreationStatusText.Text = "Армирование не создано: выбранная конструкция не подходит для расчёта.";
            footerStatusText.Text = hostSupport.Message;
            return null;
        }

        if (currentRecognitionResult is null || currentRecognitionResult.Polylines.Count == 0)
        {
            logger.Warning("IsoField test rebar creation was requested without recognition polylines.");
            TaskDialog.Show("Армирование по изополям", "Сначала загрузите карты изополей или готовый файл с зонами.");
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
            TaskDialog.Show("Армирование по изополям", "Перед созданием армирования исправьте замечания к зонам и раскладке.");
            rebarCreationStatusText.Text = "Армирование не создано: правила не готовы.";
            return null;
        }

        if (preview.IsEngineeringPreview)
        {
            if (currentQualityResult is null)
            {
                EvaluateQualityCheck();
            }

            if (currentQualityResult is null || !currentQualityResult.CanCompare)
            {
                int blockingCount = currentQualityResult?.BlockingIssues.Count ?? 1;
                logger.Warning($"IsoField action blocked by geometry quality errors. Blocking={blockingCount}.");
                TaskDialog.Show(
                    "Армирование по изополям",
                    $"Проверка нашла ошибки: {blockingCount}. Исправьте границы зон или параметры арматуры и пересчитайте раскладку.");
                rebarCreationStatusText.Text = "Исправьте ошибки в разделе «Проверка зон и арматуры».";
                return null;
            }

            if (requireWarningAcceptance
                && !currentQualityResult.CanApply(areQualityWarningsAccepted))
            {
                logger.Warning(
                    $"IsoField apply blocked by unaccepted geometry quality warnings. Warnings={currentQualityResult.Warnings.Count}.");
                TaskDialog.Show(
                    "Армирование по изополям",
                $"Проверьте предупреждения ({currentQualityResult.Warnings.Count}) и подтвердите их в окне перед применением изменений.");
                rebarCreationStatusText.Text = "Изменения не применены: предупреждения не подтверждены.";
                return null;
            }
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
            familyPreflightError = exception.Message;
            SetCurrentChangePlan(null);
            rebarCreationStatusText.Text = exception.Message;
            footerStatusText.Text = "Сравнение остановлено: не хватает подходящего семейства или его типа.";
            return null;
        }
    }

    private bool ConfirmCreateTestRebar(
        RebarRulePreviewResult preview,
        IsoFieldHostElement hostElement,
        IsoFieldSourceSet? sourceSet,
        IsoFieldRebarChangePlan? changePlan)
    {
        RebarRulePreviewItem firstItem = preview.Items.First();
        bool isEngineering = preview.IsEngineeringPreview;
        string layerMappingText = sourceSet is null
            ? "Назначение карт: загружены готовые зоны."
            : "Назначение карт: " + string.Join(
                ", ",
                IsoFieldSourceSet.RequiredRoles.Select(role =>
                {
                    IsoFieldLayerMapping mapping = sourceSet.GetLayerMapping(role);
                    string face = FormatFace(hostElement, mapping.Face);
                    return $"{FormatLayerRole(role)} — {face}";
                }));
        string instruction = isEngineering
            ? "Применить рассчитанные изменения семейств дополнительного армирования?"
            : "Создать пробные семейства дополнительного армирования в модели Revit?";
        string content = isEngineering
            ? BuildEngineeringConfirmationText(preview, hostElement, layerMappingText, firstItem, changePlan)
            : $"Конструкция: {hostElement.DisplayName}{Environment.NewLine}{layerMappingText}{Environment.NewLine}Зон с правилами: {preview.Items.Count}{Environment.NewLine}Первое правило: {firstItem.DisplayName}{Environment.NewLine}Действие изменит модель, но его можно отменить стандартной командой отмены Revit.";
        return MessageBox.Show(
            this,
            instruction + Environment.NewLine + Environment.NewLine + content,
            "Армирование по изополям",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
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
        return $"Конструкция: {hostElement.DisplayName}{Environment.NewLine}"
            + $"{layerMappingText}{Environment.NewLine}"
            + $"Режим: {mode}{Environment.NewLine}"
            + $"Зон: {preview.Items.Count}; расчётных стержней внутри семейств: {preview.EstimatedBarCount}.{Environment.NewLine}"
            + $"Изменения экземпляров семейств: {changePlan?.Summary ?? "сравнение не выполнено"}{Environment.NewLine}"
            + $"Первое правило: {firstItem.DisplayName}{Environment.NewLine}"
            + "Модуль создаёт только семейства (Массив • У) дополнительного армирования и изменяет только экземпляры, которые ранее создал сам для выбранной конструкции. Ручные семейства и отдельные стержни не затрагиваются. Все изменения выполняются вместе и отменяются одной стандартной командой отмены Revit.";
    }

    private void UpdateLegendPresentation(IsoFieldRecognitionResult result)
    {
        legendSummaryPanel.Children.Clear();
        foreach (IsoFieldLegend legend in result.EffectiveLegends.OrderBy(item => item.LayerRole))
        {
            StackPanel cardContent = new();
            cardContent.Children.Add(new TextBlock
            {
                Text = $"{(legend.LayerRole.HasValue ? FormatLayerRole(legend.LayerRole.Value) : "Источник")} · {legend.Bands.Count} диапазонов",
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
        return $"{legend.EffectiveBoundaries.Count} подписей · {FormatReinforcementLabel(first.ReinforcementLabel)} … {FormatReinforcementLabel(last.ReinforcementLabel)}";
    }

    private static string BuildLegendBandToolTip(IsoFieldLegend legend, IsoFieldLegendBand band)
    {
        string range = band.MinimumValue.HasValue && band.MaximumValue.HasValue
            ? $"{FormatNumber(band.MinimumValue.Value)}–{FormatNumber(band.MaximumValue.Value)} см²/м"
            : $"Уровень {band.Index + 1}; числовые границы не распознаны";
        string reinforcement = legend.HasReinforcementLabels
            ? $"Границы: {FormatReinforcementLabel(legend.EffectiveBoundaries[band.Index].ReinforcementLabel)} → {FormatReinforcementLabel(legend.EffectiveBoundaries[band.Index + 1].ReinforcementLabel)}"
            : "Сочетания диаметр/шаг не распознаны";
        return $"{range}{Environment.NewLine}{reinforcement}";
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
            previewStatusText.Text = "Контуры не найдены. Проверьте замечания к обработке карт.";
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

        previewStatusText.Text = $"Показано контуров: {layout.Polylines.Count}. Контуры показаны только в окне, модель Revit не изменялась.";
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
        outerBoundary.ToolTip = "Внешняя граница выбранной конструкции";
        previewCanvas.Children.Add(outerBoundary);

        foreach (IReadOnlyList<IsoFieldPoint> hole in layout.HoleBoundaries)
        {
            WpfPolyline holeBoundary = CreatePreviewPolyline(hole, TrueBimBrushes.Warning, 2);
            holeBoundary.StrokeDashArray = new DoubleCollection { 4, 3 };
            holeBoundary.ToolTip = "Отверстие в выбранной конструкции";
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
                    ? $"{zone.ZoneName ?? zone.SourceZoneId}{Environment.NewLine}Обрезано по границе конструкции; сохранено {(zone.RetainedAreaRatio * 100).ToString("0.#", CultureInfo.GetCultureInfo("ru-RU"))}% площади."
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
            removedLine.ToolTip = analysis.CanProceed
                ? $"{removedZone.ZoneName ?? removedZone.Id}{Environment.NewLine}Зона полностью вне конструкции: исключена из расчёта и добавлена в предупреждения проверки."
                : $"{removedZone.ZoneName ?? removedZone.Id}{Environment.NewLine}Зона полностью вне конструкции; проверьте совмещение.";
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
            barLine.ToolTip = $"{FormatLayerRole(segment.LayerRole)} · {FormatFace(selectedHostElement, segment.Face)} · {segment.Component.UserDisplayName}";
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
                ? $"Раскладка готова: зон {analysis.ClippedZones.Count - analysis.RemovedZoneIds.Count}; стержней {layout.EffectiveRebarSegments.Count}; обрезано зон {analysis.ClippedZoneIds.Count}; исключено зон {analysis.RemovedZoneIds.Count}; отверстий {layout.HoleBoundaries.Count}."
                : $"Совмещение готово: зон {analysis.ClippedZones.Count - analysis.RemovedZoneIds.Count}; обрезано {analysis.ClippedZoneIds.Count}; исключено {analysis.RemovedZoneIds.Count}; сохранено {(analysis.RetainedAreaRatio * 100).ToString("0.#", CultureInfo.GetCultureInfo("ru-RU"))}% площади; отверстий {layout.HoleBoundaries.Count}."
            : analysis.RemovedZoneIds.Count > 0
                ? $"Совмещение не принято: красным отмечены зоны за пределами конструкции — {analysis.RemovedZoneIds.Count}."
                : !analysis.AreControlPointsInside
                    ? "Совмещение не принято: одна или несколько контрольных точек находятся за границей конструкции. Перенесите отмеченные точки внутрь контура."
                    : "Совмещение не принято: проверьте, что точки 1–3 выбраны на одинаковых углах карты и плиты в одном порядке. Если сторона получилась зеркальной, переключите переворот карты.";
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
        ResetCompletionSummaryForWorkflowChange();
        currentRulePreview = null;
        calculatedRulePreview = null;
        configuredRulePreview = null;
        ruleOverrides.Clear();
        zoneMerges.Clear();
        currentChangePlan = null;
        currentChangePlanFingerprint = null;
        familyPreflightError = null;
        ResetQualityCheck();
        RefreshRebarReviewRows();
        ruleStatusText.Text = message;
        rebarCreationStatusText.Text = "Армирование не создано: сначала рассчитайте раскладку без ошибок.";
        if (currentRecognitionResult is not null && currentSlabBinding is not null)
        {
            RenderPreview(currentRecognitionResult);
        }

        RefreshWorkflowState();
    }

    private void RefreshWorkflowState()
    {
        IsoFieldWorkflowState state = BuildWorkflowState();
        IsoFieldHostSupportResult? hostSupport = selectedHostElement is null
            ? null
            : hostSupportService.Analyze(selectedHostElement);
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
            : isJsonSource ? "Перечитать готовые зоны" : "Найти зоны на 4 картах";
        recognizeButton.Content = IconFactory.CreateButtonContent(recognitionIcon, recognitionText);
        recognizeButton.ToolTip = ResolveRecognitionToolTip(state);
        recognizeBottomButton.IsEnabled = state.CanRunRecognition;
        recognizeBottomButton.Content = IconFactory.CreateButtonContent(
            recognitionIcon,
            state.HasZones
                ? "Повторно найти зоны на 4 картах"
                : "Продолжить: найти зоны на 4 картах");
        recognizeBottomButton.ToolTip = ResolveRecognitionToolTip(state);
        saveSourceSetManifestButton.IsEnabled = selectedSourceSet?.IsComplete == true;
        saveSourceSetManifestButton.ToolTip = saveSourceSetManifestButton.IsEnabled
            ? "Сохранить выбранные карты и назначение сторон, чтобы позже быстро восстановить комплект."
            : "Сначала выберите и исправьте комплект из четырёх изображений.";

        showRevitPreviewButton.IsEnabled = state.CanShowRevitPreview;
        showRevitPreviewButton.ToolTip = state.CanShowRevitPreview
            ? "Показать на текущем плане, разрезе или фасаде контуры зон, привязанные и обрезанные по выбранной конструкции."
            : state.HasZones && state.HasHost
                ? "Сначала укажите три соответствующих угла и выполните «Проверить привязку»."
                : "Сначала найдите зоны и выберите конструкцию.";
        correctZonesButton.IsEnabled = state.HasZones;
        correctZonesButton.ToolTip = state.HasZones
            ? "Открыть таблицу ручной проверки: исключение, выбор диапазона площади и объединение зон."
            : "Сначала загрузите готовые зоны или найдите их на комплекте карт.";
        clearRevitPreviewButton.IsEnabled = state.CanClearRevitPreview;
        clearRevitPreviewButton.ToolTip = state.CanClearRevitPreview
            ? "Удалить вспомогательные линии изополей с текущего вида."
            : "На текущем виде нет вспомогательных линий для удаления.";

        selectHostButton.IsEnabled = uiDocument is not null;
        selectHostButton.ToolTip = uiDocument is null
            ? "Откройте документ Revit, чтобы выбрать стену или плиту."
            : "Выбрать конструкцию. Поддерживаются горизонтальные плиты и прямые обычные стены.";
        clearHostButton.IsEnabled = state.HasHost;
        slabBindingExpander.IsExpanded = hostSupport?.RequiresPlanarBinding == true;
        bool canConfigureSlabBinding = state.HasZones
            && hostSupport?.RequiresPlanarBinding == true
            && selectedHostElement?.Geometry is not null
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
        saveSlabBindingProfileButton.IsEnabled = hostSupport?.IsSupported == true
            && currentSlabBinding?.CanProceed == true;
        string slabBindingToolTip = hostSupport is { IsSupported: false }
            ? hostSupport.Message
            : selectedHostElement switch
            {
                null => "Сначала выберите поддерживаемую прямую стену или горизонтальную плиту.",
                { Geometry: null } => "У выбранной конструкции не удалось определить ровную опорную поверхность.",
                _ when !state.HasZones => "Сначала загрузите или распознайте зоны.",
                { IsWall: true } => "Укажите соответствующую точку на наружной плоскости выбранной стены.",
                _ => "Укажите соответствующую точку на верхней грани выбранной плиты."
            };
        slabImagePoint1XInput.ToolTip = slabBindingToolTip;
        slabImagePoint1YInput.ToolTip = slabBindingToolTip;
        slabImagePoint2XInput.ToolTip = slabBindingToolTip;
        slabImagePoint2YInput.ToolTip = slabBindingToolTip;
        slabImagePoint3XInput.ToolTip = slabBindingToolTip;
        slabImagePoint3YInput.ToolTip = slabBindingToolTip;
        slabMirrorImageYInput.ToolTip = canConfigureSlabBinding
            ? "Используйте для карт, где ось Y направлена вниз. Изменение требует повторной проверки привязки."
            : slabBindingToolTip;
        pickSlabPoint1Button.ToolTip = slabBindingToolTip;
        pickSlabPoint2Button.ToolTip = slabBindingToolTip;
        pickSlabPoint3Button.ToolTip = canConfigureSlabBinding
            ? "Укажите точку в стороне от линии первых двух точек, чтобы независимо проверить привязку."
            : slabBindingToolTip;
        applySlabBindingButton.ToolTip = hostSupport is { IsSupported: false }
            ? hostSupport.Message
            : applySlabBindingButton.IsEnabled
            ? "Проверить три пары точек, обрезать зоны по границам и отверстиям конструкции и показать совмещение."
            : "Сначала укажите все три контрольные точки на конструкции.";
        loadSlabBindingProfileButton.ToolTip = hostSupport is { IsSupported: false }
            ? hostSupport.Message
            : loadSlabBindingProfileButton.IsEnabled
            ? $"Восстановить привязку, сохранённую {availableSlabBindingProfile!.SavedAtUtc.ToLocalTime():g}. После восстановления зоны будут проверены заново."
            : "Для текущего проекта, вида и выбранной конструкции сохранённая привязка не найдена.";
        saveSlabBindingProfileButton.ToolTip = hostSupport is { IsSupported: false }
            ? hostSupport.Message
            : saveSlabBindingProfileButton.IsEnabled
            ? "Сохранить три пары точек и направление карты для текущего проекта, вида и выбранной конструкции."
                : "Сначала выполните успешную проверку привязки по трём точкам.";
        bool canConfigureEngineeringRules = state.HasZones
            && hostSupport?.RequiresPlanarBinding == true;
        reinforcementModeInput.IsEnabled = canConfigureEngineeringRules;
        concreteCoverInput.IsEnabled = canConfigureEngineeringRules;
        boundaryOffsetInput.IsEnabled = canConfigureEngineeringRules;
        minimumBarLengthInput.IsEnabled = canConfigureEngineeringRules;
        string engineeringToolTip = hostSupport is { IsSupported: false }
            ? hostSupport.Message
            : hostSupport?.RequiresPlanarBinding == true
                ? "Изменение параметра сбрасывает рассчитанную раскладку."
            : "Раскладка по обрезанным зонам доступна для прямой стены или горизонтальной плиты.";
        reinforcementModeInput.ToolTip = canConfigureEngineeringRules
            ? "Выберите создание только добавки поверх существующей базовой сетки либо полного сочетания внутри зон."
            : engineeringToolTip;
        concreteCoverInput.ToolTip = engineeringToolTip;
        boundaryOffsetInput.ToolTip = engineeringToolTip;
        minimumBarLengthInput.ToolTip = engineeringToolTip;
        previewRulesButton.IsEnabled = state.CanCalculateRules;
        previewRulesButton.ToolTip = state.CanCalculateRules
            ? "Проверить площадь в см²/м и рассчитать линии стержней внутри отсечённых зон без изменения Revit."
            : state.HasHost && !state.HasSupportedHostGeometry
                ? hostSupport?.Message
            : state.HasHost && !state.HasValidHostBinding
            ? "Сначала совместите карты с конструкцией по трём точкам и проверьте отсечение по её границам."
            : "Сначала загрузите зоны и выберите стену или плиту.";
        bool isEngineeringPreview = currentRulePreview?.IsEngineeringPreview == true;
        int qualityBlockingCount = currentQualityResult?.BlockingIssues.Count ?? 0;
        int qualityWarningCount = currentQualityResult?.Warnings.Count ?? 0;
        bool qualityCanCompare = !isEngineeringPreview
            || currentQualityResult?.CanCompare == true;
        bool qualityCanApply = !isEngineeringPreview
            || currentQualityResult?.CanApply(areQualityWarningsAccepted) == true;
        compareChangesButton.IsEnabled = state.CanCompareWithModel
            && isEngineeringPreview
            && qualityCanCompare;
        compareChangesButton.ToolTip = state.HasHost && !state.HasSupportedHostGeometry
            ? hostSupport?.Message
            : compareChangesButton.IsEnabled
                ? $"Собрать {currentRulePreview!.EstimatedBarCount} расчётных стержней в семейства (Массив • У) и сравнить их с экземплярами модуля без изменения модели."
                : qualityBlockingCount > 0
                    ? $"Проверка нашла ошибки: {qualityBlockingCount}. Исправьте границы зон или параметры арматуры."
                : state.CanCompareWithModel
                    ? "Сравнение доступно после расчёта раскладки для прямой стены или горизонтальной плиты."
                    : "Сначала рассчитайте раскладку без ошибок.";
        exportReportButton.IsEnabled = isEngineeringPreview
            && state.HasSupportedHostGeometry
            && currentQualityResult is not null
            && currentRecognitionResult is not null
            && selectedHostElement is not null;
        exportReportButton.ToolTip = state.HasHost && !state.HasSupportedHostGeometry
            ? hostSupport?.Message
            : exportReportButton.IsEnabled
                ? currentChangePlan is null
                    ? "Сохранить подробный отчёт и таблицу по зонам, картам и результатам проверки. В отчёте будет отмечено, что сравнение с моделью ещё не выполнено."
                    : $"Сохранить подробный отчёт и таблицу с картами, привязкой, настройками, проверками и списком изменений: {currentChangePlan.Summary}"
                : "Сначала рассчитайте раскладку и дождитесь автоматической проверки.";
        createTestRebarButton.IsEnabled = state.CanCreateRebar
            && qualityCanApply
            && (!isEngineeringPreview
                || currentChangePlan?.CanApply == true && currentChangePlan.HasChanges);
        createTestRebarButton.ToolTip = state.HasHost && !state.HasSupportedHostGeometry
            ? hostSupport?.Message
            : qualityBlockingCount > 0
                ? $"Применение заблокировано: ошибок проверки {qualityBlockingCount}."
            : qualityWarningCount > 0 && !areQualityWarningsAccepted
                ? $"Проверьте и подтвердите предупреждения: {qualityWarningCount}."
            : state.CanCompareWithModel
            ? !state.HasComparedWithModel
                ? "Сначала нажмите «Сравнить с моделью» и проверьте таблицу."
                : isEngineeringPreview
                    ? currentChangePlan is { CanApply: false }
                        ? "Список изменений содержит ошибки; исправьте замечания и повторите сравнение."
                        : currentChangePlan is { HasChanges: false }
                            ? "Раскладка уже соответствует модели; применять нечего."
                            : $"Применить семейства после подтверждения: {currentChangePlan?.Summary}"
                    : "Создать пробные семейства дополнительного армирования после отдельного подтверждения."
            : !state.HasConfirmedLayerMappings && state.HasSource
                ? selectedHostElement?.IsWall == true
                    ? "Подтвердите внутреннюю или наружную сторону для каждой карты."
                    : "Подтвердите низ или верх для каждой карты."
                : state.HasHost && !state.HasValidHostBinding
                    ? "Проверьте совмещение по трём точкам и обрезку зон по границам конструкции."
                : "Сначала рассчитайте раскладку без ошибок.";

        bool hasCompletedCurrentWorkflow = lastApplicationResult is not null
            && lastApplicationHost?.ElementId == selectedHostElement?.ElementId;
        bool completionReportIsCurrent = hasCompletedCurrentWorkflow
            && !string.IsNullOrWhiteSpace(lastReportSaveResult?.JsonPath)
            && File.Exists(lastReportSaveResult!.JsonPath)
            && File.Exists(lastReportSaveResult.CsvPath)
            && lastReportApplicationRevision == applicationRevision;
        bool areRulesReady = state.HasValidRules
            && qualityCanApply
            && string.IsNullOrWhiteSpace(familyPreflightError);
        int completedStepCount = state.CompletedStepCount
            - (state.HasValidRules && !areRulesReady ? 1 : 0);
        workflowSummaryText.Text = hasCompletedCurrentWorkflow
            ? $"Все {IsoFieldWorkflowState.RequiredStepCount} обязательных проверок завершены."
            : $"Выполнено {completedStepCount} из {IsoFieldWorkflowState.RequiredStepCount} обязательных проверок.";
        UpdateWorkflowStep(
            sourceStepText,
            state.HasSource,
            selectedSourceSet is null ? "Готовые зоны выбраны" : "Комплект из 4 карт готов");
        UpdateWorkflowStep(
            mappingStepText,
            state.HasConfirmedLayerMappings,
            isJsonSource
                ? "Назначение карт не требуется"
                : selectedHostElement?.IsWall == true
                    ? "Внутренняя/наружная назначены"
                    : selectedHostElement?.IsSlab == true
                        ? "Верх/низ назначены"
                        : "Стороны назначены автоматически");
        UpdateWorkflowStep(zonesStepText, state.HasZones, "Зоны загружены");
        string hostStepLabel = selectedHostElement switch
        {
            null => "Конструкция выбрана",
            _ when !state.HasSupportedHostGeometry => "Конструкция не поддерживается",
            _ when state.HasValidHostBinding => "Конструкция привязана",
            _ => "Конструкция выбрана, нужна привязка"
        };
        UpdateWorkflowStep(hostStepText, state.HasReadyHost, hostStepLabel);
        UpdateWorkflowStep(
            rulesStepText,
            areRulesReady,
            !string.IsNullOrWhiteSpace(familyPreflightError)
                ? "Не найден подходящий тип семейства"
                : qualityBlockingCount > 0
                ? "Раскладка заблокирована проверкой"
                : qualityWarningCount > 0 && !areQualityWarningsAccepted
                    ? "Ожидается решение по предупреждениям"
                     : "Раскладка проверена");
        UpdateWorkflowStep(
            comparisonStepText,
            state.HasComparedWithModel,
            state.HasComparedWithModel
                ? "Сравнение с моделью выполнено"
                : "Нужно сравнить с моделью");
        UpdateWorkflowGuidance(
            state,
            hasCompletedCurrentWorkflow,
            completionReportIsCurrent,
            qualityBlockingCount,
            qualityWarningCount);
        RefreshZoneRuleActions();
    }

    private IsoFieldWorkflowState BuildWorkflowState()
    {
        bool isJsonSource = !string.IsNullOrWhiteSpace(selectedJsonPath);
        bool hasSource = isJsonSource || selectedSourceSet?.IsComplete == true;
        bool hasConfirmedLayerMappings = isJsonSource || selectedSourceSet?.HasConfirmedLayerMappings == true;
        bool canProcessImages = recognitionRunner is not StubIsoFieldRecognitionRunner;
        bool hasSupportedHostGeometry = selectedHostElement is not null
            && hostSupportService.Analyze(selectedHostElement).IsSupported;
        bool requiresPlanarBinding = selectedHostElement is not null
            && hostSupportService.Analyze(selectedHostElement).RequiresPlanarBinding;
        bool hasValidHostBinding = !requiresPlanarBinding
            || currentSlabBinding?.CanProceed == true;
        return new IsoFieldWorkflowState(
            hasSource,
            currentRecognitionResult?.Polylines.Count > 0,
            selectedHostElement is not null,
            currentRulePreview?.CanCreateRebar == true,
            activeRevitPreviewIds.Count > 0,
            isJsonSource || selectedSourceSet?.IsComplete == true && canProcessImages,
            hasConfirmedLayerMappings,
            hasValidHostBinding,
            hasSupportedHostGeometry,
            currentChangePlan is not null);
    }

    private string ResolveRecognitionToolTip(IsoFieldWorkflowState state)
    {
        if (selectedSourceSet is not null && !selectedSourceSet.IsComplete)
        {
            return FormatSourceSetIssues(selectedSourceSet);
        }

        if (!state.HasSource)
        {
            return "Сначала выберите готовые зоны или полный комплект из четырёх карт.";
        }

        if (!state.CanProcessSource)
        {
            return "Не удалось обработать карты: выберите готовый файл с зонами.";
        }

        return !string.IsNullOrWhiteSpace(selectedJsonPath)
            ? "Перечитать зоны из выбранного готового файла."
            : "Найти зоны на четырёх картах изополей.";
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
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, TrueBimTheme.Spacing8),
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
            "Показать зоны на конструкции",
            TrueBimIcon.Apply,
            220,
            "Сначала проверьте привязку к конструкции. Кнопка покажет на текущем виде привязанные и обрезанные зоны.",
            (_, _) => ShowRevitPreview());
    }

    private Button CreateClearRevitPreviewButton()
    {
        Button button = CreateActionButton(
            "Удалить линии с вида",
            TrueBimIcon.Close,
            116,
            "На текущем виде нет вспомогательных линий для удаления.",
            (_, _) => ClearRevitPreview());
        button.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8);
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
        return $"Начальная точка: {FormatNumber(calibration.ImageAnchor.X)}; {FormatNumber(calibration.ImageAnchor.Y)}. Масштаб: {FormatNumber(calibration.MillimetersPerPixel)} мм на точку изображения.";
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
            ? $"Раскладка: зон {preview.Items.Count}, стержней {preview.EstimatedBarCount}."
            : $"Правил: {preview.Items.Count}.";
        return $"{header}{Environment.NewLine}{string.Join(Environment.NewLine, lines)}{suffix}";
    }

    private string ResolveRecognitionRunnerName()
    {
        return recognitionRunner is IIsoFieldRecognitionRunnerDiagnostics diagnostics
            ? diagnostics.RunnerName
            : recognitionRunner.GetType().Name;
    }

    private string ResolveRecognitionRunnerVersion()
    {
        return recognitionRunner is IIsoFieldRecognitionRunnerDiagnostics diagnostics
            ? diagnostics.RunnerVersion
            : recognitionRunner.GetType().Assembly.GetName().Version?.ToString() ?? "неизвестно";
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.###", CultureInfo.CurrentCulture);
    }

    private static string FormatReinforcementLabel(string? label)
    {
        return new IsoFieldReinforcementCombinationService().FormatForDisplay(label);
    }

    private static string FormatFace(
        IsoFieldHostElement? hostElement,
        IsoFieldRebarFace face)
    {
        if (hostElement?.IsWall == true)
        {
            return face == IsoFieldRebarFace.Bottom ? "внутренняя" : "наружная";
        }

        if (hostElement?.IsSlab == true)
        {
            return face == IsoFieldRebarFace.Bottom ? "низ" : "верх";
        }

        return face == IsoFieldRebarFace.Bottom ? "сторона 1" : "сторона 2";
    }

    private static string FormatLayerRole(IsoFieldLayerRole role)
    {
        return role switch
        {
            IsoFieldLayerRole.As1X => "направление X, карта 1",
            IsoFieldLayerRole.As2X => "направление X, карта 2",
            IsoFieldLayerRole.As3Y => "направление Y, карта 1",
            IsoFieldLayerRole.As4Y => "направление Y, карта 2",
            _ => "карта не определена"
        };
    }

    private IReadOnlyList<IsoFieldFaceOption> BuildLayerFaceOptions()
    {
        if (selectedHostElement?.IsWall == true)
        {
            return
            [
                new(IsoFieldRebarFace.Unconfirmed, "Не задано"),
                new(IsoFieldRebarFace.Bottom, "Внутренняя"),
                new(IsoFieldRebarFace.Top, "Наружная")
            ];
        }

        if (selectedHostElement?.IsSlab == true)
        {
            return
            [
                new(IsoFieldRebarFace.Unconfirmed, "Не задано"),
                new(IsoFieldRebarFace.Bottom, "Низ"),
                new(IsoFieldRebarFace.Top, "Верх")
            ];
        }

        return
        [
            new(IsoFieldRebarFace.Unconfirmed, "Не задано"),
            new(IsoFieldRebarFace.Bottom, "1 · Низ/внутр."),
            new(IsoFieldRebarFace.Top, "2 · Верх/наруж.")
        ];
    }

    private enum WorkflowPrimaryAction
    {
        None,
        ChooseSource,
        ShowSourceMappings,
        RunRecognition,
        SelectHost,
        ShowBinding,
        LoadBinding,
        CalculateRules,
        ExcludeEmptyZones,
        CompareWithModel,
        ApplyChanges,
        SaveCompletionReport
    }

    private sealed record IsoFieldFaceOption(IsoFieldRebarFace Face, string Label);

    private sealed record IsoFieldReinforcementModeOption(
        IsoFieldReinforcementMode Mode,
        string Label);

    private sealed record IsoFieldReviewLayerOption(
        IsoFieldLayerRole? LayerRole,
        string Label);

    private sealed record IsoFieldSourceRoleOption(
        IsoFieldLayerRole Role,
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
