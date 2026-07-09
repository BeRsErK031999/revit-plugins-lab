using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.Modules.BimTools.Common.Models;
using TrueBIM.App.Modules.BimTools.Common.Services.Export;
using TrueBIM.App.Modules.BimTools.Common.Services.Reports;
using TrueBIM.App.Modules.BimTools.Common.Services.Storage;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using WpfGrid = System.Windows.Controls.Grid;

namespace TrueBIM.App.Modules.BimTools.UI;

public sealed class BimToolPlaceholderWindow : TrueBimWindow
{
    private readonly BimToolPlaceholderDefinition definition;
    private readonly string documentTitle;
    private readonly ITrueBimLogger logger;
    private readonly JsonSettingsStorage settingsStorage;
    private readonly ReportService reportService = new();
    private readonly CsvExportService csvExportService = new();
    private readonly string settingsPath;
    private BimToolShellState state;
    private TextBlock? statusText;

    public BimToolPlaceholderWindow(
        BimToolPlaceholderDefinition definition,
        string? documentTitle,
        ITrueBimLogger logger)
    {
        this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        this.documentTitle = string.IsNullOrWhiteSpace(documentTitle)
            ? "документ не открыт"
            : documentTitle!;
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        settingsStorage = new JsonSettingsStorage(this.logger);
        settingsPath = JsonSettingsStorage.CreateDefaultSettingsPath(definition.SettingsKey);
        state = settingsStorage.LoadOrDefault(settingsPath, CreateDefaultState);
        MarkOpened();

        Title = definition.Title;
        Icon = IconFactory.CreateImage(definition.Icon, 32);
        Width = 900;
        Height = 650;
        MinWidth = 820;
        MinHeight = 560;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();

        this.logger.Info($"BIM tool scaffold opened: {definition.Title}.");
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
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Border workflowPanel = CreatePanel("Сценарий", definition.WorkflowSteps);
        workflowPanel.Margin = new Thickness(0, 0, 14, 0);
        body.Children.Add(workflowPanel);

        Border scopePanel = CreatePanel("Следующий срез", definition.ImplementationScope);
        WpfGrid.SetColumn(scopePanel, 1);
        body.Children.Add(scopePanel);

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
        titleRow.Children.Add(IconFactory.Create(definition.Icon, 28));
        titleRow.Children.Add(new TextBlock
        {
            Text = definition.Title,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(titleRow);

        header.Children.Add(new TextBlock
        {
            Text = definition.Description,
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });

        header.Children.Add(new TextBlock
        {
            Text = $"Активный документ: {documentTitle}",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });

        return header;
    }

    private static Border CreatePanel(string title, IReadOnlyList<string> rows)
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

        for (int index = 0; index < rows.Count; index++)
        {
            content.Children.Add(CreateRow(index + 1, rows[index]));
        }

        return new Border
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = content
        };
    }

    private static UIElement CreateRow(int number, string text)
    {
        WpfGrid row = new()
        {
            Margin = new Thickness(0, 0, 0, 10)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Border marker = new()
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromRgb(236, 240, 245)),
            Child = new TextBlock
            {
                Text = number.ToString(System.Globalization.CultureInfo.CurrentCulture),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            }
        };
        row.Children.Add(marker);

        TextBlock textBlock = new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        WpfGrid.SetColumn(textBlock, 1);
        row.Children.Add(textBlock);

        return row;
    }

    private UIElement CreateFooter()
    {
        WpfGrid footer = new()
        {
            Margin = new Thickness(0, 16, 0, 0)
        };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        statusText = new TextBlock
        {
            Text = $"Каркас команды готов. Настройки сохранены: {settingsPath}",
            Foreground = Brushes.DimGray,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        footer.Children.Add(statusText);

        Button previewButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Preview, "Предпросмотр"),
            MinWidth = 140,
            Height = 32,
            Margin = new Thickness(10, 0, 0, 0),
            ToolTip = "Зафиксировать безопасный preview-запрос каркаса."
        };
        previewButton.Click += (_, _) => RecordScaffoldAction("Предпросмотр", isExecute: false);
        WpfGrid.SetColumn(previewButton, 1);
        footer.Children.Add(previewButton);

        Button executeButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Apply, "Выполнить"),
            MinWidth = 120,
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Зафиксировать безопасный execute-запрос каркаса без изменений модели."
        };
        executeButton.Click += (_, _) => RecordScaffoldAction("Выполнить", isExecute: true);
        WpfGrid.SetColumn(executeButton, 2);
        footer.Children.Add(executeButton);

        Button closeButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Закрыть"),
            MinWidth = 120,
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true,
            ToolTip = "Закрыть окно."
        };
        closeButton.Click += (_, _) => Close();
        WpfGrid.SetColumn(closeButton, 3);
        footer.Children.Add(closeButton);

        return footer;
    }

    private BimToolShellState CreateDefaultState()
    {
        return new BimToolShellState
        {
            ToolTitle = definition.Title,
            DocumentTitle = documentTitle
        };
    }

    private void MarkOpened()
    {
        state.ToolTitle = definition.Title;
        state.DocumentTitle = documentTitle;
        state.LastOpenedAtUtc = DateTimeOffset.UtcNow;
        settingsStorage.Save(settingsPath, state);
    }

    private void RecordScaffoldAction(string actionName, bool isExecute)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (isExecute)
        {
            state.ExecuteRequestCount++;
            state.LastExecuteAtUtc = now;
        }
        else
        {
            state.PreviewRequestCount++;
            state.LastPreviewAtUtc = now;
        }

        state.ToolTitle = definition.Title;
        state.DocumentTitle = documentTitle;
        settingsStorage.Save(settingsPath, state);

        BimReport report = reportService.CreateScaffoldReport(definition.Title, documentTitle, actionName);
        string csv = reportService.FormatCsv(report, csvExportService);
        logger.Info($"BIM tool scaffold action recorded: {definition.Title}, action={actionName}, csvLength={csv.Length}.");

        if (statusText is not null)
        {
            statusText.Text = isExecute
                ? $"Команда '{actionName}' зафиксирована. Модель не изменялась. Настройки: {settingsPath}"
                : $"Предпросмотр каркаса зафиксирован. Модель не изменялась. Настройки: {settingsPath}";
        }
    }
}
