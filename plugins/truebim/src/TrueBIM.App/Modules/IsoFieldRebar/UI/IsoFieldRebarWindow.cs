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
    private readonly Button showRevitPreviewButton;
    private readonly Button clearRevitPreviewButton;
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
        recognitionStatusText = CreateMutedText("Распознавание пока не запускалось.");
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
        rebarCreationStatusText = CreateMutedText("Тестовая арматура пока не создана.");
        previewStatusText = CreateMutedText("Контуры пока не загружены.");
        previewCanvas = CreatePreviewCanvas();
        showRevitPreviewButton = CreateRevitPreviewButton();
        clearRevitPreviewButton = CreateClearRevitPreviewButton();
        footerStatusText = CreateMutedText("Линии предпросмотра создаются только по явной кнопке.");

        Title = "Армирование по изополям";
        Icon = IconFactory.CreateImage(TrueBimIcon.IsoFieldRebar, 32);
        Width = 840;
        Height = 840;
        MinWidth = 760;
        MinHeight = 760;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();
        ClearPreview("Контуры пока не загружены.");

        this.logger.Info("IsoField Rebar window opened.");
    }

    private UIElement CreateContent()
    {
        WpfGrid body = new();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Border filePanel = CreateFilePanel();
        WpfGrid.SetColumn(filePanel, 0);
        body.Children.Add(filePanel);

        Border nextStepsPanel = CreateNextStepsPanel();
        WpfGrid.SetColumn(nextStepsPanel, 1);
        WpfGrid.SetRowSpan(nextStepsPanel, 6);
        nextStepsPanel.Margin = new Thickness(TrueBimTheme.Spacing12, 0, 0, 0);
        body.Children.Add(nextStepsPanel);

        Border recognitionPanel = CreateRecognitionPanel();
        WpfGrid.SetRow(recognitionPanel, 1);
        recognitionPanel.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        body.Children.Add(recognitionPanel);

        Border hostPanel = CreateHostPanel();
        WpfGrid.SetRow(hostPanel, 2);
        hostPanel.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        body.Children.Add(hostPanel);

        Border calibrationPanel = CreateCalibrationPanel();
        WpfGrid.SetRow(calibrationPanel, 3);
        calibrationPanel.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        body.Children.Add(calibrationPanel);

        Border rulePanel = CreateRulePanel();
        WpfGrid.SetRow(rulePanel, 4);
        rulePanel.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        body.Children.Add(rulePanel);

        Border previewPanel = CreatePreviewPanel();
        WpfGrid.SetRow(previewPanel, 5);
        previewPanel.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        body.Children.Add(previewPanel);

        return BuildShell(
            header: TrueBimUi.CreateHeader(
                Title,
                $"Активный документ: {documentTitle}. Безопасный сценарий: файл, preview, host, правила и тестовая арматура только после подтверждения.",
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
        content.Children.Add(CreateMutedText("Нажмите, чтобы открыть справку с картинками: входной файл, preview, выбор host, расчет правил и пример тестовой арматуры."));
        content.Children.Add(CreateMutedText("До кнопки «Создать тестовую» модуль работает в безопасном режиме без записи арматуры в модель."));

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
        StackPanel content = CreatePanelContent("Источник изополей");

        Button chooseButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Open, "Выбрать файл"),
            MinWidth = 140,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateButtonStyle(),
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Выбрать изображение или JSON-файл изополей."
        };
        chooseButton.Click += (_, _) => ChooseSourceFile();
        content.Children.Add(chooseButton);

        selectedFileText.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        content.Children.Add(selectedFileText);

        return CreatePanel(content);
    }

    private Border CreatePreviewPanel()
    {
        StackPanel content = CreatePanelContent("Предпросмотр контуров");

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
        StackPanel content = CreatePanelContent("Host-элемент");

        StackPanel buttonRow = new()
        {
            Orientation = Orientation.Horizontal
        };

        Button selectHostButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Apply, "Выбрать стену/плиту"),
            MinWidth = 180,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateButtonStyle(),
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Выбрать стену или плиту как будущий host для армирования."
        };
        selectHostButton.Click += (_, _) => SelectHostElement();
        buttonRow.Children.Add(selectHostButton);

        Button clearHostButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Сбросить"),
            MinWidth = 110,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateButtonStyle(),
            Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0),
            ToolTip = "Сбросить выбранный host-элемент."
        };
        clearHostButton.Click += (_, _) => ClearHostElement();
        buttonRow.Children.Add(clearHostButton);

        content.Children.Add(buttonRow);

        hostStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        content.Children.Add(hostStatusText);

        return CreatePanel(content);
    }

    private Border CreateCalibrationPanel()
    {
        StackPanel content = CreatePanelContent("Калибровка");

        StackPanel rows = new();
        rows.Children.Add(CreateInputRow("Якорь X", calibrationAnchorXInput));
        rows.Children.Add(CreateInputRow("Якорь Y", calibrationAnchorYInput));
        rows.Children.Add(CreateInputRow("Мм/пикс", calibrationMillimetersPerPixelInput));
        rows.Children.Add(calibrationInvertYInput);
        content.Children.Add(rows);

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
        content.Children.Add(applyCalibrationButton);

        calibrationStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        content.Children.Add(calibrationStatusText);

        return CreatePanel(content);
    }

    private Border CreateRulePanel()
    {
        StackPanel content = CreatePanelContent("Правила армирования");

        StackPanel buttonRow = new()
        {
            Orientation = Orientation.Horizontal
        };

        Button previewRulesButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Preview, "Рассчитать правила"),
            MinWidth = 170,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateButtonStyle(),
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Сформировать read-only preview правил армирования для распознанных зон."
        };
        previewRulesButton.Click += (_, _) => PreviewRebarRules();
        buttonRow.Children.Add(previewRulesButton);

        Button createTestRebarButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Apply, "Создать тестовую"),
            MinWidth = 160,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateButtonStyle(TrueBimButtonStyleKind.Primary),
            Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Создать тестовую арматуру на выбранном host-элементе после явного подтверждения."
        };
        createTestRebarButton.Click += (_, _) => CreateTestRebar();
        buttonRow.Children.Add(createTestRebarButton);

        content.Children.Add(buttonRow);

        ruleStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        content.Children.Add(ruleStatusText);
        rebarCreationStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        content.Children.Add(rebarCreationStatusText);

        return CreatePanel(content);
    }

    private Border CreateRecognitionPanel()
    {
        StackPanel content = CreatePanelContent("Распознавание");

        Button stubButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Preview, "Распознать файл"),
            MinWidth = 170,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateButtonStyle(),
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Запустить настроенный CLI-worker или безопасную заглушку без изменения модели."
        };
        stubButton.Click += (_, _) => RunRecognition();
        content.Children.Add(stubButton);

        recognitionStatusText.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        content.Children.Add(recognitionStatusText);

        return CreatePanel(content);
    }

    private static Border CreateNextStepsPanel()
    {
        StackPanel content = CreatePanelContent("Будущие шаги");

        content.Children.Add(CreateStep("Экранный preview контуров", false, true));
        content.Children.Add(CreateStep("Линии предпросмотра в Revit", false, true));
        content.Children.Add(CreateStep("Выбор стены или плиты", false, true));
        content.Children.Add(CreateStep("Калибровка координат", false, true));
        content.Children.Add(CreateStep("Правила армирования", false, true));
        content.Children.Add(CreateStep("Тестовая арматура", false, true));

        TextBlock note = CreateMutedText("Тестовая арматура создается только по отдельной кнопке и после подтверждения пользователя.");
        note.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
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
            selectedFileText.Text = selectedPath;
            logger.Info($"IsoField source file selected: {Path.GetFileName(selectedPath)}.");
            if (IsJsonFile(selectedPath))
            {
                ReadJsonSource(selectedPath);
            }
            else
            {
                recognitionStatusText.Text = "Файл выбран. Нажмите «Распознать файл», чтобы запустить настроенный runner.";
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
            ? "Готово к созданию тестовой арматуры после подтверждения."
            : "Тестовая арматура недоступна: проверьте диагностику правил.";
        footerStatusText.Text = preview.CanCreateRebar
            ? $"Правила армирования рассчитаны: {preview.Items.Count}. Модель Revit не изменялась."
            : "Правила армирования требуют проверки.";
        logger.Info($"IsoField rebar rules preview calculated. Items={preview.Items.Count}; Diagnostics={preview.Diagnostics.Count}; CanCreateRebar={preview.CanCreateRebar}.");
    }

    private void CreateTestRebar()
    {
        rebarCreationStatusText.Text = "Создание тестовой арматуры поставлено в очередь Revit.";
        revitActions.Raise(CreateTestRebarInRevitContext);
    }

    private void CreateTestRebarInRevitContext()
    {
        if (uiDocument is null)
        {
            logger.Warning("IsoField test rebar creation was requested without an open Revit document.");
            TaskDialog.Show("Армирование по изополям", "Откройте документ Revit перед созданием тестовой арматуры.");
            return;
        }

        if (selectedHostElement is null)
        {
            logger.Warning("IsoField test rebar creation was requested without selected host element.");
            TaskDialog.Show("Армирование по изополям", "Сначала выберите стену или плиту как host-элемент.");
            rebarCreationStatusText.Text = "Тестовая арматура не создана: host-элемент не выбран.";
            return;
        }

        if (currentRecognitionResult is null || currentRecognitionResult.Polylines.Count == 0)
        {
            logger.Warning("IsoField test rebar creation was requested without recognition polylines.");
            TaskDialog.Show("Армирование по изополям", "Сначала выберите JSON-файл с контурами изополей.");
            rebarCreationStatusText.Text = "Тестовая арматура не создана: нет контуров изополей.";
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
            TaskDialog.Show("Армирование по изополям", "Перед созданием тестовой арматуры исправьте диагностику правил.");
            rebarCreationStatusText.Text = "Тестовая арматура не создана: правила не готовы.";
            return;
        }

        if (!ConfirmCreateTestRebar(preview, selectedHostElement))
        {
            rebarCreationStatusText.Text = "Создание тестовой арматуры отменено.";
            footerStatusText.Text = "Создание тестовой арматуры отменено пользователем.";
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
                "Не удалось создать тестовую арматуру. Проверьте host-элемент, наличие RebarBarType в модели и логи.");
            rebarCreationStatusText.Text = "Тестовая арматура не создана: см. логи диагностики.";
            footerStatusText.Text = "Не удалось создать тестовую арматуру.";
        }
    }

    private static bool ConfirmCreateTestRebar(RebarRulePreviewResult preview, IsoFieldHostElement hostElement)
    {
        RebarRulePreviewItem firstItem = preview.Items.First();
        TaskDialog dialog = new("Армирование по изополям")
        {
            MainInstruction = "Создать тестовую арматуру в модели Revit?",
            MainContent = $"Host: {hostElement.DisplayName}{Environment.NewLine}Зон с правилами: {preview.Items.Count}{Environment.NewLine}Первое правило: {firstItem.DisplayName}{Environment.NewLine}Для поддержанной стены или плиты будет создано по одному тестовому элементу на валидную зону. Это действие изменит модель, но его можно отменить через Undo.",
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
        rebarCreationStatusText.Text = "Тестовая арматура не создана: сначала рассчитайте валидные правила.";
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

    private static CheckBox CreateStep(string text, bool isEnabled, bool isChecked = false)
    {
        return new CheckBox
        {
            Content = text,
            IsChecked = isChecked,
            IsEnabled = isEnabled,
            Style = TrueBimStyles.CreateCheckBoxStyle(),
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
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
        Button button = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Apply, "Показать в Revit"),
            MinWidth = 150,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateButtonStyle(),
            ToolTip = "Создать управляемые линии предпросмотра на активном 2D-виде."
        };
        button.Click += (_, _) => ShowRevitPreview();
        return button;
    }

    private Button CreateClearRevitPreviewButton()
    {
        Button button = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Очистить"),
            MinWidth = 110,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateButtonStyle(),
            Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0),
            ToolTip = "Удалить линии предпросмотра изополей на активном виде."
        };
        button.Click += (_, _) => ClearRevitPreview();
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
