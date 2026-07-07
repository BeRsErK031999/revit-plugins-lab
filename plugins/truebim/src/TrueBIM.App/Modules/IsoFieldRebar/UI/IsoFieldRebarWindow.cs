using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Revit;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using WpfGrid = System.Windows.Controls.Grid;
using WpfPolyline = System.Windows.Shapes.Polyline;
using ElementId = Autodesk.Revit.DB.ElementId;

namespace TrueBIM.App.Modules.IsoFieldRebar.UI;

public sealed class IsoFieldRebarWindow : Window
{
    private readonly string documentTitle;
    private readonly UIDocument? uiDocument;
    private readonly IIsoFieldFilePicker filePicker;
    private readonly IIsoFieldJsonReader jsonReader;
    private readonly IIsoFieldRecognitionRunner recognitionRunner;
    private readonly IsoFieldRevitPreviewService revitPreviewService;
    private readonly IsoFieldPreviewLayoutService previewLayoutService = new();
    private readonly ITrueBimLogger logger;
    private readonly TextBlock selectedFileText;
    private readonly TextBlock recognitionStatusText;
    private readonly TextBlock previewStatusText;
    private readonly TextBlock footerStatusText;
    private readonly Canvas previewCanvas;
    private readonly Button showRevitPreviewButton;
    private readonly Button clearRevitPreviewButton;
    private string? selectedFilePath;
    private IsoFieldRecognitionResult? currentRecognitionResult;
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
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        selectedFileText = CreateMutedText("Файл не выбран.");
        recognitionStatusText = CreateMutedText("Распознавание пока не запускалось.");
        previewStatusText = CreateMutedText("Контуры пока не загружены.");
        previewCanvas = CreatePreviewCanvas();
        showRevitPreviewButton = CreateRevitPreviewButton();
        clearRevitPreviewButton = CreateClearRevitPreviewButton();
        footerStatusText = CreateMutedText("Линии предпросмотра создаются только по явной кнопке.");

        Title = "Армирование по изополям";
        Icon = IconFactory.CreateImage(TrueBimIcon.IsoFieldRebar, 32);
        Width = 840;
        Height = 560;
        MinWidth = 760;
        MinHeight = 500;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();
        ClearPreview("Контуры пока не загружены.");

        this.logger.Info("IsoField Rebar window opened.");
    }

    private UIElement CreateContent()
    {
        DockPanel root = new()
        {
            Margin = new Thickness(18)
        };

        UIElement footer = CreateFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        StackPanel header = CreateHeader();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        WpfGrid body = new();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Border filePanel = CreateFilePanel();
        WpfGrid.SetColumn(filePanel, 0);
        body.Children.Add(filePanel);

        Border nextStepsPanel = CreateNextStepsPanel();
        WpfGrid.SetColumn(nextStepsPanel, 1);
        WpfGrid.SetRowSpan(nextStepsPanel, 3);
        nextStepsPanel.Margin = new Thickness(14, 0, 0, 0);
        body.Children.Add(nextStepsPanel);

        Border recognitionPanel = CreateRecognitionPanel();
        WpfGrid.SetRow(recognitionPanel, 1);
        recognitionPanel.Margin = new Thickness(0, 14, 0, 0);
        body.Children.Add(recognitionPanel);

        Border previewPanel = CreatePreviewPanel();
        WpfGrid.SetRow(previewPanel, 2);
        previewPanel.Margin = new Thickness(0, 14, 0, 0);
        body.Children.Add(previewPanel);

        root.Children.Add(body);
        return root;
    }

    private StackPanel CreateHeader()
    {
        StackPanel header = new()
        {
            Margin = new Thickness(0, 0, 0, 16)
        };

        StackPanel titleRow = new()
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleRow.Children.Add(IconFactory.Create(TrueBimIcon.IsoFieldRebar, 28));
        titleRow.Children.Add(new TextBlock
        {
            Text = "Армирование по изополям",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(titleRow);

        header.Children.Add(CreateMutedText(
            "Выберите изображение или JSON-файл изополей. JSON-контуры можно просмотреть в окне без изменения модели Revit."));
        header.Children.Add(CreateMutedText($"Активный документ: {documentTitle}."));

        return header;
    }

    private Border CreateFilePanel()
    {
        StackPanel content = CreatePanelContent("Источник изополей");

        Button chooseButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Open, "Выбрать файл"),
            MinWidth = 140,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Выбрать изображение или JSON-файл изополей."
        };
        chooseButton.Click += (_, _) => ChooseSourceFile();
        content.Children.Add(chooseButton);

        selectedFileText.Margin = new Thickness(0, 12, 0, 0);
        content.Children.Add(selectedFileText);

        return CreatePanel(content);
    }

    private Border CreatePreviewPanel()
    {
        StackPanel content = CreatePanelContent("Предпросмотр контуров");

        Border canvasBorder = new()
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(218, 224, 232)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            Child = previewCanvas
        };
        content.Children.Add(canvasBorder);

        StackPanel buttonRow = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0)
        };
        buttonRow.Children.Add(showRevitPreviewButton);
        buttonRow.Children.Add(clearRevitPreviewButton);
        content.Children.Add(buttonRow);

        previewStatusText.Margin = new Thickness(0, 10, 0, 0);
        content.Children.Add(previewStatusText);

        return CreatePanel(content);
    }

    private Border CreateRecognitionPanel()
    {
        StackPanel content = CreatePanelContent("Распознавание");

        Button stubButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Preview, "Проверить заглушку"),
            MinWidth = 170,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Запустить только stub-runner без OpenCV, Python и изменения модели."
        };
        stubButton.Click += (_, _) => RunRecognitionStub();
        content.Children.Add(stubButton);

        recognitionStatusText.Margin = new Thickness(0, 12, 0, 0);
        content.Children.Add(recognitionStatusText);

        return CreatePanel(content);
    }

    private static Border CreateNextStepsPanel()
    {
        StackPanel content = CreatePanelContent("Будущие шаги");

        content.Children.Add(CreateStep("Экранный preview контуров", false, true));
        content.Children.Add(CreateStep("Линии предпросмотра в Revit", false, true));
        content.Children.Add(CreateStep("Выбор стены или плиты", false));
        content.Children.Add(CreateStep("Создание арматуры", false));

        TextBlock note = CreateMutedText("Линии предпросмотра создаются только по кнопке и могут быть очищены модулем. Выбор host-элемента и создание арматуры будут отдельными срезами.");
        note.Margin = new Thickness(0, 12, 0, 0);
        content.Children.Add(note);

        return CreatePanel(content);
    }

    private UIElement CreateFooter()
    {
        DockPanel footer = new()
        {
            LastChildFill = true,
            Margin = new Thickness(0, 16, 0, 0)
        };

        Button closeButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Закрыть"),
            MinWidth = 120,
            Height = 32,
            IsCancel = true,
            ToolTip = "Закрыть окно."
        };
        closeButton.Click += (_, _) => Close();
        DockPanel.SetDock(closeButton, Dock.Right);
        footer.Children.Add(closeButton);

        footer.Children.Add(footerStatusText);
        return footer;
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
                recognitionStatusText.Text = "Файл выбран. Реальное распознавание пока не подключено.";
                ClearPreview("Выбран файл изображения. Контуры появятся после JSON или будущего распознавания.");
                footerStatusText.Text = "Файл изополей выбран. Модель Revit не изменялась.";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            logger.Error("Failed to select IsoField source file.", exception);
            ClearPreview("Контуры не загружены из выбранного файла.");
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось выбрать файл изополей. Используйте логи для диагностики.");
            footerStatusText.Text = "Не удалось выбрать файл.";
        }
    }

    private void ReadJsonSource(string path)
    {
        IsoFieldRecognitionResult result = jsonReader.Read(path);
        currentRecognitionResult = result;
        recognitionStatusText.Text = $"JSON прочитан. Контуров: {result.Polylines.Count}. Диагностик: {result.Diagnostics.Count}.";
        RenderPreview(result);
        footerStatusText.Text = "JSON-контракт изополей прочитан. Модель Revit не изменялась.";
        logger.Info($"IsoField recognition JSON read. Polylines: {result.Polylines.Count}, diagnostics: {result.Diagnostics.Count}.");
    }

    private void RunRecognitionStub()
    {
        try
        {
            IsoFieldRecognitionResult result = recognitionRunner.Run(selectedFilePath);
            currentRecognitionResult = result;
            recognitionStatusText.Text = $"Заглушка выполнена. Контуров: {result.Polylines.Count}. Диагностик: {result.Diagnostics.Count}.";
            RenderPreview(result);
            footerStatusText.Text = "Stub-распознавание завершено. OpenCV/Python не запускались, модель Revit не изменялась.";
            logger.Info($"IsoField recognition stub completed. Polylines: {result.Polylines.Count}, diagnostics: {result.Diagnostics.Count}.");
        }
        catch (Exception exception)
        {
            logger.Error("Failed to run IsoField recognition stub.", exception);
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось выполнить заглушку распознавания. Используйте логи для диагностики.");
            footerStatusText.Text = "Не удалось выполнить заглушку распознавания.";
        }
    }

    private void ShowRevitPreview()
    {
        if (uiDocument is null)
        {
            TaskDialog.Show("Армирование по изополям", "Откройте документ Revit перед созданием линий предпросмотра.");
            return;
        }

        if (currentRecognitionResult is null || currentRecognitionResult.Polylines.Count == 0)
        {
            TaskDialog.Show("Армирование по изополям", "Сначала выберите JSON-файл с контурами изополей.");
            return;
        }

        try
        {
            IsoFieldRevitPreviewResult result = revitPreviewService.Show(
                uiDocument,
                currentRecognitionResult,
                activeRevitPreviewIds);
            activeRevitPreviewIds = result.CreatedElementIds;
            footerStatusText.Text = result.Message;
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
        if (uiDocument is null)
        {
            TaskDialog.Show("Армирование по изополям", "Откройте документ Revit перед очисткой линий предпросмотра.");
            return;
        }

        try
        {
            IsoFieldRevitPreviewResult result = revitPreviewService.Clear(uiDocument, activeRevitPreviewIds);
            activeRevitPreviewIds = Array.Empty<ElementId>();
            footerStatusText.Text = result.Message;
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
            new SolidColorBrush(Color.FromRgb(28, 103, 176)),
            new SolidColorBrush(Color.FromRgb(35, 132, 93)),
            new SolidColorBrush(Color.FromRgb(168, 92, 42)),
            new SolidColorBrush(Color.FromRgb(128, 77, 156))
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
        previewCanvas.Children.Clear();
        previewCanvas.Children.Add(new TextBlock
        {
            Text = "Нет данных",
            Foreground = Brushes.Gray,
            FontWeight = FontWeights.SemiBold
        });
        Canvas.SetLeft(previewCanvas.Children[0], 16);
        Canvas.SetTop(previewCanvas.Children[0], 16);
        previewStatusText.Text = message;
    }

    private static StackPanel CreatePanelContent(string title)
    {
        StackPanel content = new()
        {
            Margin = new Thickness(14)
        };
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        });

        return content;
    }

    private static Border CreatePanel(UIElement child)
    {
        return new Border
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
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
            Margin = new Thickness(0, 0, 0, 10)
        };
    }

    private static TextBlock CreateMutedText(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };
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
            Height = 32,
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
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Удалить линии предпросмотра изополей на активном виде."
        };
        button.Click += (_, _) => ClearRevitPreview();
        return button;
    }

    private static bool IsJsonFile(string path)
    {
        return string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);
    }
}
