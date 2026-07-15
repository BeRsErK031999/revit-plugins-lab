using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Revit;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfPolyline = System.Windows.Shapes.Polyline;
using ElementId = Autodesk.Revit.DB.ElementId;

namespace TrueBIM.App.Modules.IsoFieldRebar.UI;

public sealed class IsoFieldRebarWindow : TrueBimWindow
{
    private readonly string documentTitle;
    private readonly UIDocument? uiDocument;
    private readonly IIsoFieldFilePicker filePicker;
    private readonly IIsoFieldJsonReader jsonReader;
    private readonly IIsoFieldRecognitionRunner recognitionRunner;
    private readonly IsoFieldRevitPreviewService revitPreviewService;
    private readonly IsoFieldHostSelectionService hostSelectionService;
    private readonly IsoFieldRebarCreationService rebarCreationService;
    private readonly IsoFieldCoordinateMapper coordinateMapper = new();
    private readonly IsoFieldPreviewLayoutService previewLayoutService = new();
    private readonly RebarRuleValidationService rebarRuleValidationService = new();
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
    private readonly Button showRevitPreviewButton;
    private readonly Button clearRevitPreviewButton;
    private readonly Button selectHostButton;
    private readonly Button clearHostButton;
    private readonly Button previewRulesButton;
    private readonly Button createTestRebarButton;
    private readonly TextBlock workflowSummaryText;
    private readonly TextBlock sourceStepText;
    private readonly TextBlock zonesStepText;
    private readonly TextBlock hostStepText;
    private readonly TextBlock rulesStepText;
    private readonly WpfTextBox calibrationAnchorXInput;
    private readonly WpfTextBox calibrationAnchorYInput;
    private readonly WpfTextBox calibrationMillimetersPerPixelInput;
    private readonly CheckBox calibrationInvertYInput;
    private string? selectedFilePath;
    private IsoFieldRecognitionResult? currentRecognitionResult;
    private IsoFieldHostElement? selectedHostElement;
    private IsoFieldCalibration currentCalibration = IsoFieldCalibration.Default;
    private RebarRulePreviewResult? currentRulePreview;
    private IReadOnlyList<ElementId> activeRevitPreviewIds = Array.Empty<ElementId>();
    private const double PreviewCanvasWidth = 430;
    private const double PreviewCanvasHeight = 180;

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
        this.filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
        this.jsonReader = jsonReader ?? throw new ArgumentNullException(nameof(jsonReader));
        this.recognitionRunner = recognitionRunner ?? throw new ArgumentNullException(nameof(recognitionRunner));
        this.revitPreviewService = revitPreviewService ?? throw new ArgumentNullException(nameof(revitPreviewService));
        this.hostSelectionService = hostSelectionService ?? throw new ArgumentNullException(nameof(hostSelectionService));
        this.rebarCreationService = rebarCreationService ?? throw new ArgumentNullException(nameof(rebarCreationService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        revitActions = new RevitActionDispatcher("армирование по изополям", this.logger);

        selectedFileText = CreateMutedText("Файл не выбран.");
        recognitionStatusText = CreateMutedText($"JSON загружается сразу. Обработчик изображений: {ResolveRecognitionRunnerName()}.");
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
        ruleStatusText = CreateMutedText("Правила пока не рассчитаны.");
        rebarCreationStatusText = CreateMutedText("Пробное армирование пока не создано.");
        previewStatusText = CreateMutedText("Контуры пока не загружены.");
        previewCanvas = CreatePreviewCanvas();
        recognizeButton = CreateActionButton(
            "Загрузить зоны",
            TrueBimIcon.Preview,
            176,
            "Сначала выберите JSON или изображение изополей.",
            (_, _) => RunRecognition());
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
            "Рассчитать правила",
            TrueBimIcon.Preview,
            176,
            "Сначала загрузите зоны и выберите host-элемент.",
            (_, _) => PreviewRebarRules());
        createTestRebarButton = CreateActionButton(
            "Создать пробное армирование",
            TrueBimIcon.Apply,
            226,
            "Сначала рассчитайте валидные правила армирования.",
            (_, _) => CreateTestRebar(),
            TrueBimButtonStyleKind.Primary);
        workflowSummaryText = CreateMutedText("Готово 0 из 4 обязательных шагов.");
        sourceStepText = CreateWorkflowStepText("Источник выбран");
        zonesStepText = CreateWorkflowStepText("Зоны загружены");
        hostStepText = CreateWorkflowStepText("Host выбран");
        rulesStepText = CreateWorkflowStepText("Правила проверены");
        footerStatusText = CreateMutedText("Линии предпросмотра создаются только по явной кнопке.");

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
                $"Активный документ: {documentTitle}. Последовательный сценарий: источник, зоны, host, правила и пробное армирование после подтверждения.",
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
        content.Children.Add(CreateMutedText("Нажмите, чтобы открыть справку с картинками: входной файл, preview, выбор host, расчет правил и пример пробного армирования."));
        content.Children.Add(CreateMutedText("До кнопки «Создать пробное армирование» модуль работает в безопасном режиме без записи арматуры в модель."));

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

        StackPanel buttonRow = new()
        {
            Orientation = Orientation.Horizontal
        };

        Button chooseButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Open, "Выбрать JSON/изображение"),
            MinWidth = 214,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateButtonStyle(),
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Выбрать готовый JSON зон или изображение изополей для настроенного worker."
        };
        chooseButton.Click += (_, _) => ChooseSourceFile();
        buttonRow.Children.Add(chooseButton);
        recognizeButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        buttonRow.Children.Add(recognizeButton);
        content.Children.Add(buttonRow);

        selectedFileText.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        content.Children.Add(selectedFileText);
        recognitionStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        content.Children.Add(recognitionStatusText);

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
            Header = "Калибровка координат для линий на виде Revit",
            Content = calibrationContent,
            IsExpanded = false,
            ToolTip = "Откройте только если координаты JSON нужно совместить с активным видом Revit."
        });

        return CreatePanel(content);
    }

    private Border CreateRulePanel()
    {
        StackPanel content = CreatePanelContent("4. Правила и создание");

        StackPanel buttonRow = new()
        {
            Orientation = Orientation.Horizontal
        };

        buttonRow.Children.Add(previewRulesButton);

        createTestRebarButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        buttonRow.Children.Add(createTestRebarButton);

        content.Children.Add(buttonRow);

        ruleStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        content.Children.Add(ruleStatusText);
        rebarCreationStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        content.Children.Add(rebarCreationStatusText);

        return CreatePanel(content);
    }

    private Border CreateWorkflowPanel()
    {
        StackPanel content = CreatePanelContent("Готовность");

        workflowSummaryText.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        content.Children.Add(TrueBimUi.CreateInfoBanner(workflowSummaryText));
        content.Children.Add(sourceStepText);
        content.Children.Add(zonesStepText);
        content.Children.Add(hostStepText);
        content.Children.Add(rulesStepText);

        TextBlock note = CreateMutedText("Создание доступно только после проверки всех обязательных шагов и отдельного подтверждения. Текущий алгоритм формирует пробное, а не производственное армирование.");
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
            string? path = filePicker.PickIsoFieldSourceFile();
            if (string.IsNullOrWhiteSpace(path))
            {
                footerStatusText.Text = "Выбор файла отменен.";
                logger.Info("IsoField source file selection canceled.");
                return;
            }

            string selectedPath = path!;
            selectedFilePath = selectedPath;
            selectedFileText.Text = $"Выбран: {Path.GetFileName(selectedPath)}";
            selectedFileText.ToolTip = selectedPath;
            logger.Info($"IsoField source file selected: {Path.GetFileName(selectedPath)}.");
            if (IsJsonFile(selectedPath))
            {
                ReadJsonSource(selectedPath);
            }
            else
            {
                recognitionStatusText.Text = "Изображение выбрано. Нажмите «Распознать изображение», если CLI worker настроен.";
                ClearPreview("Выбран файл изображения. Контуры появятся после JSON или распознавания.");
                footerStatusText.Text = "Файл изополей выбран. Модель Revit не изменялась.";
                logger.Info($"IsoField image source is ready for recognition. Extension='{Path.GetExtension(selectedPath)}'.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            logger.Error("Failed to select IsoField source file.", exception);
            ClearPreview("Контуры не загружены из выбранного файла.");
            ClearRulePreview("Правила не рассчитаны: контуры не загружены.");
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось выбрать файл изополей. Используйте логи для диагностики.");
            footerStatusText.Text = "Не удалось выбрать файл.";
        }
    }

    private void ReadJsonSource(string path)
    {
        logger.Info($"IsoField JSON source read started: {Path.GetFileName(path)}.");
        IsoFieldRecognitionResult result = jsonReader.Read(path);
        currentRecognitionResult = result;
        recognitionStatusText.Text = $"JSON прочитан. Контуров: {result.Polylines.Count}. Диагностик: {result.Diagnostics.Count}.";
        RenderPreview(result);
        ClearRulePreview("Нажмите «Рассчитать правила» после выбора host-элемента.");
        footerStatusText.Text = "JSON-контракт изополей прочитан. Модель Revit не изменялась.";
        logger.Info($"IsoField recognition JSON read. Polylines: {result.Polylines.Count}, diagnostics: {result.Diagnostics.Count}.");
    }

    private void RunRecognition()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(selectedFilePath))
            {
                logger.Warning("IsoField recognition was requested without selected source file.");
                TaskDialog.Show("Армирование по изополям", "Сначала выберите файл изополей.");
                footerStatusText.Text = "Распознавание не запущено: файл не выбран.";
                return;
            }

            logger.Info($"IsoField recognition started. Runner={ResolveRecognitionRunnerName()}; Source={Path.GetFileName(selectedFilePath)}.");
            IsoFieldRecognitionResult result = recognitionRunner.Run(selectedFilePath);
            currentRecognitionResult = result;
            recognitionStatusText.Text = $"Распознавание выполнено. Контуров: {result.Polylines.Count}. Диагностик: {result.Diagnostics.Count}.";
            RenderPreview(result);
            ClearRulePreview(result.Polylines.Count == 0
                ? "Правила не рассчитаны: результат распознавания не содержит зон."
                : "Нажмите «Рассчитать правила» после выбора host-элемента.");
            footerStatusText.Text = "Распознавание завершено. Модель Revit не изменялась.";
            logger.Info($"IsoField recognition completed. Polylines: {result.Polylines.Count}, diagnostics: {result.Diagnostics.Count}.");
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
            RefreshHostStatus();
            ClearRulePreview("Нажмите «Рассчитать правила» для выбранного host-элемента.");
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
        RefreshHostStatus();
        ClearRulePreview("Правила не рассчитаны: host-элемент сброшен.");
        footerStatusText.Text = "Host-элемент сброшен. Модель Revit не изменялась.";
        logger.Info("IsoField host selection cleared.");
    }

    private void RefreshHostStatus()
    {
        hostStatusText.Text = selectedHostElement is null
            ? "Host-элемент не выбран."
            : selectedHostElement.DisplayName;
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

        logger.Info($"IsoField rebar rules preview requested. Polylines={currentRecognitionResult.Polylines.Count}; HostSelected={selectedHostElement is not null}.");
        RebarRulePreviewResult preview = rebarRuleValidationService.BuildPreview(
            currentRecognitionResult,
            selectedHostElement);
        currentRulePreview = preview;
        ruleStatusText.Text = FormatRulePreview(preview);
        rebarCreationStatusText.Text = preview.CanCreateRebar
            ? "Готово к созданию пробного армирования после подтверждения."
            : "Пробное армирование недоступно: проверьте диагностику правил.";
        footerStatusText.Text = preview.CanCreateRebar
            ? $"Правила армирования рассчитаны: {preview.Items.Count}. Модель Revit не изменялась."
            : "Правила армирования требуют проверки.";
        logger.Info($"IsoField rebar rules preview calculated. Items={preview.Items.Count}; Diagnostics={preview.Diagnostics.Count}; CanCreateRebar={preview.CanCreateRebar}.");
        RefreshWorkflowState();
    }

    private void CreateTestRebar()
    {
        rebarCreationStatusText.Text = "Создание пробного армирования поставлено в очередь Revit.";
        revitActions.Raise(CreateTestRebarInRevitContext);
    }

    private void CreateTestRebarInRevitContext()
    {
        if (uiDocument is null)
        {
            logger.Warning("IsoField test rebar creation was requested without an open Revit document.");
            TaskDialog.Show("Армирование по изополям", "Откройте документ Revit перед созданием пробного армирования.");
            return;
        }

        if (selectedHostElement is null)
        {
            logger.Warning("IsoField test rebar creation was requested without selected host element.");
            TaskDialog.Show("Армирование по изополям", "Сначала выберите стену или плиту как host-элемент.");
            rebarCreationStatusText.Text = "Пробное армирование не создано: host-элемент не выбран.";
            return;
        }

        if (currentRecognitionResult is null || currentRecognitionResult.Polylines.Count == 0)
        {
            logger.Warning("IsoField test rebar creation was requested without recognition polylines.");
            TaskDialog.Show("Армирование по изополям", "Сначала выберите JSON-файл с контурами изополей.");
            rebarCreationStatusText.Text = "Пробное армирование не создано: нет контуров изополей.";
            return;
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
            TaskDialog.Show("Армирование по изополям", "Перед созданием пробного армирования исправьте диагностику правил.");
            rebarCreationStatusText.Text = "Пробное армирование не создано: правила не готовы.";
            return;
        }

        if (!ConfirmCreateTestRebar(preview, selectedHostElement))
        {
            rebarCreationStatusText.Text = "Создание пробного армирования отменено.";
            footerStatusText.Text = "Создание пробного армирования отменено пользователем.";
            logger.Info("IsoField test rebar creation canceled by user.");
            return;
        }

        try
        {
            logger.Info($"IsoField test rebar creation requested. HostKind={selectedHostElement.HostKind}; HostId={selectedHostElement.ElementId}; Rules={preview.Items.Count}.");
            IsoFieldRebarCreationResult result = rebarCreationService.CreateTestRebar(
                uiDocument,
                selectedHostElement,
                preview);
            rebarCreationStatusText.Text = result.Message;
            footerStatusText.Text = result.Message;
            logger.Info($"IsoField test rebar creation completed. Count={result.CreatedCount}; HostId={selectedHostElement.ElementId}.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or Autodesk.Revit.Exceptions.ApplicationException or Autodesk.Revit.Exceptions.ArgumentException)
        {
            logger.Error("Failed to create IsoField test rebar.", exception);
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось создать пробное армирование. Проверьте host-элемент, наличие RebarBarType в модели и логи.");
            rebarCreationStatusText.Text = "Пробное армирование не создано: см. логи диагностики.";
            footerStatusText.Text = "Не удалось создать пробное армирование.";
        }
    }

    private static bool ConfirmCreateTestRebar(RebarRulePreviewResult preview, IsoFieldHostElement hostElement)
    {
        RebarRulePreviewItem firstItem = preview.Items.First();
        TaskDialog dialog = new("Армирование по изополям")
        {
            MainInstruction = "Создать пробное армирование в модели Revit?",
            MainContent = $"Host: {hostElement.DisplayName}{Environment.NewLine}Зон с правилами: {preview.Items.Count}{Environment.NewLine}Первое правило: {firstItem.DisplayName}{Environment.NewLine}Будет создано по одному пробному элементу на валидную зону. Это не производственная раскладка. Действие изменит модель, но его можно отменить через Undo.",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No
        };

        return dialog.Show() == TaskDialogResult.Yes;
    }

    private void RenderPreview(IsoFieldRecognitionResult result)
    {
        previewCanvas.Children.Clear();
        IsoFieldPreviewLayout layout = previewLayoutService.Build(result, PreviewCanvasWidth, PreviewCanvasHeight);
        if (layout.Polylines.Count == 0)
        {
            ClearPreview("Нет контуров для предпросмотра.");
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
                Stroke = strokes[index % strokes.Length],
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

    private void ClearPreview(string message)
    {
        currentRecognitionResult = null;
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
    }

    private void ClearRulePreview(string message)
    {
        currentRulePreview = null;
        ruleStatusText.Text = message;
        rebarCreationStatusText.Text = "Пробное армирование не создано: сначала рассчитайте валидные правила.";
        RefreshWorkflowState();
    }

    private void RefreshWorkflowState()
    {
        IsoFieldWorkflowState state = BuildWorkflowState();
        recognizeButton.IsEnabled = state.CanRunRecognition;
        bool isJsonSource = state.HasSource && IsJsonFile(selectedFilePath!);
        TrueBimIcon recognitionIcon = !state.HasSource
            ? TrueBimIcon.Open
            : isJsonSource ? TrueBimIcon.Refresh : TrueBimIcon.Preview;
        string recognitionText = !state.HasSource
            ? "Загрузить зоны"
            : isJsonSource ? "Перечитать JSON" : "Распознать изображение";
        recognizeButton.Content = IconFactory.CreateButtonContent(recognitionIcon, recognitionText);
        recognizeButton.ToolTip = ResolveRecognitionToolTip(state);

        showRevitPreviewButton.IsEnabled = state.CanShowRevitPreview;
        showRevitPreviewButton.ToolTip = state.CanShowRevitPreview
            ? "Создать управляемые линии предпросмотра на активном 2D-виде."
            : "Сначала загрузите зоны из JSON или распознанного изображения.";
        clearRevitPreviewButton.IsEnabled = state.CanClearRevitPreview;
        clearRevitPreviewButton.ToolTip = state.CanClearRevitPreview
            ? "Удалить линии предпросмотра изополей на активном виде."
            : "В этой сессии нет линий предпросмотра для удаления.";

        selectHostButton.IsEnabled = uiDocument is not null;
        selectHostButton.ToolTip = uiDocument is null
            ? "Откройте документ Revit, чтобы выбрать стену или плиту."
            : "Выбрать стену или плиту как host для армирования.";
        clearHostButton.IsEnabled = state.HasHost;
        previewRulesButton.IsEnabled = state.CanCalculateRules;
        previewRulesButton.ToolTip = state.CanCalculateRules
            ? "Сформировать read-only preview правил армирования для загруженных зон."
            : "Сначала загрузите зоны и выберите host-элемент.";
        createTestRebarButton.IsEnabled = state.CanCreateRebar;
        createTestRebarButton.ToolTip = state.CanCreateRebar
            ? "Создать пробное армирование после отдельного подтверждения."
            : "Сначала рассчитайте правила без ошибок.";

        workflowSummaryText.Text = $"Готово {state.CompletedStepCount} из 4. {state.NextAction}";
        UpdateWorkflowStep(sourceStepText, state.HasSource, "Источник выбран");
        UpdateWorkflowStep(zonesStepText, state.HasZones, "Зоны загружены");
        UpdateWorkflowStep(hostStepText, state.HasHost, "Host выбран");
        UpdateWorkflowStep(rulesStepText, state.HasValidRules, "Правила проверены");
    }

    private IsoFieldWorkflowState BuildWorkflowState()
    {
        bool hasSource = !string.IsNullOrWhiteSpace(selectedFilePath);
        bool isJsonSource = hasSource && IsJsonFile(selectedFilePath!);
        bool hasConfiguredWorker = !string.Equals(ResolveRecognitionRunnerName(), "Stub", StringComparison.OrdinalIgnoreCase);
        return new IsoFieldWorkflowState(
            hasSource,
            currentRecognitionResult?.Polylines.Count > 0,
            selectedHostElement is not null,
            currentRulePreview?.CanCreateRebar == true,
            activeRevitPreviewIds.Count > 0,
            isJsonSource || hasConfiguredWorker);
    }

    private static string ResolveRecognitionToolTip(IsoFieldWorkflowState state)
    {
        if (!state.HasSource)
        {
            return "Сначала выберите JSON или изображение изополей.";
        }

        if (!state.CanProcessSource)
        {
            return "Распознавание изображений не настроено: выберите готовый JSON или настройте CLI worker.";
        }

        return "Загрузить зоны из JSON или запустить настроенный CLI worker для изображения.";
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
        return $"Правил: {preview.Items.Count}.{Environment.NewLine}{string.Join(Environment.NewLine, lines)}{suffix}";
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
}
