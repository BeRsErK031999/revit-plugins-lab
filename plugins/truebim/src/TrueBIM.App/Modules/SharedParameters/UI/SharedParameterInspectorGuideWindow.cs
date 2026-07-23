using System.Windows;
using System.Windows.Controls;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.SharedParameters.UI;

public sealed class SharedParameterInspectorGuideWindow : TrueBimWindow
{
    private readonly SharedParameterGuidePage page;
    private readonly SharedParameterGuideTopic topic;

    public SharedParameterInspectorGuideWindow(
        SharedParameterGuideTopic topic = SharedParameterGuideTopic.Overview)
    {
        this.topic = topic;
        page = SharedParameterGuideCatalog.Get(topic);

        Title = page.Title;
        Width = 920;
        Height = 740;
        MinWidth = 760;
        MinHeight = 560;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = IconFactory.CreateImage(TrueBimIcon.Help, TrueBimTheme.IconSizeRibbon);

        Button closeButton = TrueBimUi.CreateSecondaryButton(
            "Закрыть",
            TrueBimIcon.Close,
            (_, _) => Close(),
            minWidth: 120);
        closeButton.IsCancel = true;
        closeButton.ToolTip = "Закрыть справку и вернуться к окну общих параметров.";

        ApplyTrueBimShell(
            TrueBimUi.CreateHeader(
                page.Title,
                page.Summary,
                TrueBimIcon.Help),
            null,
            CreateBody(),
            null,
            TrueBimUi.CreateFooter(CreateFooterHint(), closeButton));
    }

    private UIElement CreateBody()
    {
        StackPanel content = new();
        for (int index = 0; index < page.Sections.Count; index++)
        {
            SharedParameterGuideSection section = page.Sections[index];
            Border card = TrueBimUi.CreateSectionCard(
                section.Title,
                CreateItems(section.Items, IsNumberedSection(section.Title)));
            card.Margin = new Thickness(
                0,
                0,
                TrueBimTheme.Spacing8,
                index == page.Sections.Count - 1 ? 0 : TrueBimTheme.Spacing12);
            content.Children.Add(card);
        }

        return new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private TextBlock CreateFooterHint()
    {
        return new TextBlock
        {
            Text = topic == SharedParameterGuideTopic.Overview
                ? "Кнопки «?» рядом с действиями открывают короткую справку только по выбранному сценарию."
                : "Это контекстная справка по одному действию. Общая методичка доступна в шапке основного окна.",
            Foreground = TrueBimBrushes.TextSecondary,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static UIElement CreateItems(
        IReadOnlyList<string> items,
        bool numbered)
    {
        StackPanel stack = new();
        for (int index = 0; index < items.Count; index++)
        {
            Grid row = new()
            {
                Margin = new Thickness(
                    0,
                    0,
                    0,
                    index == items.Count - 1 ? 0 : TrueBimTheme.Spacing8)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border marker = new()
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = numbered
                    ? TrueBimBrushes.InfoBackground
                    : TrueBimBrushes.SurfaceAlt,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = numbered ? (index + 1).ToString() : "•",
                    Foreground = numbered
                        ? TrueBimBrushes.Info
                        : TrueBimBrushes.TextSecondary,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            row.Children.Add(marker);

            TextBlock text = new()
            {
                Text = items[index],
                Foreground = TrueBimBrushes.TextSecondary,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            stack.Children.Add(row);
        }

        return stack;
    }

    private static bool IsNumberedSection(string title)
    {
        return title.IndexOf("маршрут", StringComparison.CurrentCultureIgnoreCase) >= 0
            || title.IndexOf("Перед запуском", StringComparison.CurrentCultureIgnoreCase) >= 0
            || title.IndexOf("до записи", StringComparison.CurrentCultureIgnoreCase) >= 0;
    }
}
