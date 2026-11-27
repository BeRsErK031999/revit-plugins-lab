using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.FinishSchedule.UI;

public sealed class FinishScheduleWarningsWindow : TrueBimWindow
{
    private readonly FinishScheduleUserNotice notice;
    private readonly Button closeButton;

    public long? SelectedElementId { get; private set; }

    public FinishScheduleWarningsWindow(FinishScheduleUserNotice notice, IReadOnlyList<FinishModelCheck>? checks = null)
    {
        this.notice = notice ?? throw new ArgumentNullException(nameof(notice));
        closeButton = TrueBimUi.CreatePrimaryButton(
            "Понятно",
            TrueBimIcon.Check,
            (_, _) => Close(),
            minWidth: 110);
        closeButton.IsDefault = true;
        closeButton.IsCancel = true;

        Title = "Результат расчёта ведомости";
        Icon = IconFactory.CreateImage(TrueBimIcon.Warning, TrueBimTheme.IconSizeRibbon);
        Width = 720;
        Height = 620;
        MinWidth = 580;
        MinHeight = 440;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        ApplyTrueBimShell(
            TrueBimUi.CreateHeader(
                notice.Title,
                "Короткая сводка без технических сообщений Revit",
                notice.Severity is FinishScheduleUserNoticeSeverity.Warning or FinishScheduleUserNoticeSeverity.Danger
                    ? TrueBimIcon.Warning
                    : TrueBimIcon.Check),
            commandBar: null,
            body: checks is { Count: > 0 } ? CreateReviewTabs(checks) : CreateBody(),
            status: CreateStatusText(),
            footer: TrueBimUi.CreateFooter(status: null, closeButton));

        Loaded += (_, _) => closeButton.Focus();
    }

    private UIElement CreateReviewTabs(IReadOnlyList<FinishModelCheck> checks)
    {
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Сводка", Content = CreateBody() });
        var panel = new DockPanel { Margin = new Thickness(8) };
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 6) };
        var label = new Label { Content = "Номер помещения, ID или текст замечания", Target = search };
        var filter = new ComboBox { ItemsSource = new[] { "Все случаи", "Требуют исправления", "Касания с выбранным владельцем" }, SelectedIndex = 0 };
        var controls = new StackPanel();
        controls.Children.Add(label);
        controls.Children.Add(search);
        controls.Children.Add(filter);
        DockPanel.SetDock(controls, Dock.Top);
        panel.Children.Add(controls);
        var list = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        var wrap = new FrameworkElementFactory(typeof(TextBlock));
        wrap.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        wrap.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(FinishModelCheck.Text)));
        list.ItemTemplate = new DataTemplate { VisualTree = wrap };
        var show = new Button { Content = "Показать выбранный элемент в Revit", IsEnabled = false, Margin = new Thickness(0, 6, 0, 0) };
        show.Click += (_, _) => { if (list.SelectedItem is FinishModelCheck row) { SelectedElementId = row.ElementId; Close(); } };
        list.SelectionChanged += (_, _) => show.IsEnabled = list.SelectedItem != null;
        DockPanel.SetDock(show, Dock.Bottom);
        panel.Children.Add(show);
        panel.Children.Add(list);
        void Refresh() => list.ItemsSource = checks.Where(row =>
            (filter.SelectedIndex == 0 || (filter.SelectedIndex == 1) == row.NeedsCorrection) &&
            row.Text.IndexOf(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0).ToArray();
        search.TextChanged += (_, _) => Refresh();
        filter.SelectionChanged += (_, _) => Refresh();
        Refresh();
        tabs.Items.Add(new TabItem { Header = $"Элементы ({checks.Count})", Content = panel });
        return tabs;
    }

    private UIElement CreateBody()
    {
        StackPanel content = new()
        {
            MaxWidth = 660,
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing4, 0)
        };

        Border message = TrueBimUi.CreateInfoBanner(notice.Message, MapSeverity(notice.Severity));
        message.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing16);
        content.Children.Add(message);

        if (notice.SummaryItems.Count > 0)
        {
            Border summary = TrueBimUi.CreateSectionCard(
                "Что вошло в расчёт",
                CreateList(notice.SummaryItems, TrueBimBrushes.TextPrimary));
            summary.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
            content.Children.Add(summary);
        }

        string warningTitle = notice.WarningItems.Count == 0
            ? "Замечаний нет"
            : "Что стоит проверить";
        UIElement warningContent = notice.WarningItems.Count == 0
            ? new TextBlock
            {
                Text = "Плагин не нашёл проблем, влияющих на ведомость.",
                Foreground = TrueBimBrushes.Success,
                TextWrapping = TextWrapping.Wrap
            }
            : CreateList(notice.WarningItems, TrueBimBrushes.TextPrimary, TrueBimBrushes.Warning);
        content.Children.Add(TrueBimUi.CreateSectionCard(warningTitle, warningContent));

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content
        };
    }

    private static UIElement CreateList(
        IEnumerable<string> items,
        Brush textBrush,
        Brush? markerBrush = null)
    {
        StackPanel list = new();
        string[] values = items.ToArray();
        for (int index = 0; index < values.Length; index++)
        {
            Grid row = new()
            {
                Margin = index == values.Length - 1
                    ? new Thickness(0)
                    : new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock
            {
                Text = "•",
                FontWeight = FontWeights.SemiBold,
                Foreground = markerBrush ?? TrueBimBrushes.Info,
                Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, 0)
            });
            TextBlock text = new()
            {
                Text = values[index],
                TextWrapping = TextWrapping.Wrap,
                Foreground = textBrush
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            list.Children.Add(row);
        }

        return list;
    }

    private TextBlock CreateStatusText()
    {
        return new TextBlock
        {
            Text = "Подробный технический отчёт можно скопировать в основном окне.",
            Foreground = TrueBimBrushes.TextSecondary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0)
        };
    }

    private static TrueBimUiSeverity MapSeverity(FinishScheduleUserNoticeSeverity severity)
    {
        return severity switch
        {
            FinishScheduleUserNoticeSeverity.Success => TrueBimUiSeverity.Success,
            FinishScheduleUserNoticeSeverity.Warning => TrueBimUiSeverity.Warning,
            FinishScheduleUserNoticeSeverity.Danger => TrueBimUiSeverity.Danger,
            _ => TrueBimUiSeverity.Info
        };
    }
}
