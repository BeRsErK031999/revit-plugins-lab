using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using WpfGrid = System.Windows.Controls.Grid;

namespace TrueBIM.App.Modules.IsoFieldRebar.UI;

public sealed class IsoFieldRebarWindow : Window
{
    private readonly string documentTitle;
    private readonly IIsoFieldFilePicker filePicker;
    private readonly IIsoFieldJsonReader jsonReader;
    private readonly IIsoFieldRecognitionRunner recognitionRunner;
    private readonly ITrueBimLogger logger;
    private readonly TextBlock selectedFileText;
    private readonly TextBlock recognitionStatusText;
    private readonly TextBlock footerStatusText;
    private string? selectedFilePath;

    public IsoFieldRebarWindow(
        string? documentTitle,
        IIsoFieldFilePicker filePicker,
        IIsoFieldJsonReader jsonReader,
        IIsoFieldRecognitionRunner recognitionRunner,
        ITrueBimLogger logger)
    {
        this.documentTitle = string.IsNullOrWhiteSpace(documentTitle)
            ? "документ не открыт"
            : documentTitle!;
        this.filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
        this.jsonReader = jsonReader ?? throw new ArgumentNullException(nameof(jsonReader));
        this.recognitionRunner = recognitionRunner ?? throw new ArgumentNullException(nameof(recognitionRunner));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        selectedFileText = CreateMutedText("Файл не выбран.");
        recognitionStatusText = CreateMutedText("Распознавание пока не запускалось.");
        footerStatusText = CreateMutedText("Модель Revit в этом срезе не изменяется.");

        Title = "Армирование по изополям";
        Icon = IconFactory.CreateImage(TrueBimIcon.IsoFieldRebar, 32);
        Width = 840;
        Height = 560;
        MinWidth = 760;
        MinHeight = 500;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();

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
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Border filePanel = CreateFilePanel();
        WpfGrid.SetColumn(filePanel, 0);
        body.Children.Add(filePanel);

        Border nextStepsPanel = CreateNextStepsPanel();
        WpfGrid.SetColumn(nextStepsPanel, 1);
        WpfGrid.SetRowSpan(nextStepsPanel, 2);
        nextStepsPanel.Margin = new Thickness(14, 0, 0, 0);
        body.Children.Add(nextStepsPanel);

        Border recognitionPanel = CreateRecognitionPanel();
        WpfGrid.SetRow(recognitionPanel, 1);
        recognitionPanel.Margin = new Thickness(0, 14, 0, 0);
        body.Children.Add(recognitionPanel);

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
            "Выберите изображение или JSON-файл изополей. На этом этапе модуль только хранит выбранный путь и показывает безопасный stub-статус."));
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

        content.Children.Add(CreateStep("Предпросмотр контуров", false));
        content.Children.Add(CreateStep("Выбор стены или плиты", false));
        content.Children.Add(CreateStep("Калибровка координат", false));
        content.Children.Add(CreateStep("Создание арматуры", false));

        TextBlock note = CreateMutedText("Эти действия появятся отдельными безопасными срезами после фиксации JSON-контракта и preview-логики.");
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
                footerStatusText.Text = "Файл изополей выбран. Модель Revit не изменялась.";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            logger.Error("Failed to select IsoField source file.", exception);
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось выбрать файл изополей. Используйте логи для диагностики.");
            footerStatusText.Text = "Не удалось выбрать файл.";
        }
    }

    private void ReadJsonSource(string path)
    {
        IsoFieldRecognitionResult result = jsonReader.Read(path);
        recognitionStatusText.Text = $"JSON прочитан. Контуров: {result.Polylines.Count}. Диагностик: {result.Diagnostics.Count}.";
        footerStatusText.Text = "JSON-контракт изополей прочитан. Модель Revit не изменялась.";
        logger.Info($"IsoField recognition JSON read. Polylines: {result.Polylines.Count}, diagnostics: {result.Diagnostics.Count}.");
    }

    private void RunRecognitionStub()
    {
        try
        {
            IsoFieldRecognitionResult result = recognitionRunner.Run(selectedFilePath);
            recognitionStatusText.Text = $"Заглушка выполнена. Контуров: {result.Polylines.Count}. Диагностик: {result.Diagnostics.Count}.";
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

    private static CheckBox CreateStep(string text, bool isEnabled)
    {
        return new CheckBox
        {
            Content = text,
            IsChecked = false,
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

    private static bool IsJsonFile(string path)
    {
        return string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);
    }
}
