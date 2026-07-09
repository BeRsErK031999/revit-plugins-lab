using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TrueBIM.App.UI.DesignSystem;

public static class TrueBimUi
{
    public static Grid CreateWindowShell(
        UIElement? header,
        UIElement? commandBar,
        UIElement body,
        UIElement? status,
        UIElement? footer)
    {
        Grid shell = new()
        {
            Margin = TrueBimTheme.WindowPadding
        };

        if (header is not null)
        {
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(header, shell.RowDefinitions.Count - 1);
            shell.Children.Add(header);
        }

        if (commandBar is not null)
        {
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(commandBar, shell.RowDefinitions.Count - 1);
            shell.Children.Add(commandBar);
        }

        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, shell.RowDefinitions.Count - 1);
        shell.Children.Add(body);

        if (status is not null)
        {
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(status, shell.RowDefinitions.Count - 1);
            shell.Children.Add(status);
        }

        if (footer is not null)
        {
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(footer, shell.RowDefinitions.Count - 1);
            shell.Children.Add(footer);
        }

        return shell;
    }

    public static UIElement CreateHeader(string title, string description, TrueBimIcon icon)
    {
        Grid header = new()
        {
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing16),
            MinHeight = TrueBimTheme.HeaderHeight
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Border iconFrame = new()
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(TrueBimTheme.Radius12),
            Background = TrueBimBrushes.InfoBackground,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new Image
            {
                Source = IconFactory.CreateImage(icon, TrueBimTheme.PrimaryColor),
                Width = TrueBimTheme.IconSizeHeader,
                Height = TrueBimTheme.IconSizeHeader,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        header.Children.Add(iconFrame);

        StackPanel text = new()
        {
            Margin = new Thickness(TrueBimTheme.Spacing12, 0, 0, 0)
        };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = TrueBimTheme.WindowTitleFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary
        });
        text.Children.Add(new TextBlock
        {
            Text = description,
            Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = TrueBimBrushes.TextSecondary
        });
        Grid.SetColumn(text, 1);
        header.Children.Add(text);
        return header;
    }

    public static StackPanel CreateCommandBar(params UIElement[] items)
    {
        StackPanel bar = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12)
        };
        AddSpacedChildren(bar, items, TrueBimTheme.Spacing8);
        return bar;
    }

    public static Grid CreateFooter(UIElement? status, params UIElement[] actions)
    {
        Grid footer = new()
        {
            MinHeight = TrueBimTheme.FooterHeight,
            Margin = new Thickness(0, TrueBimTheme.Spacing16, 0, 0)
        };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (status is not null)
        {
            Grid.SetColumn(status, 0);
            footer.Children.Add(status);
        }

        StackPanel actionPanel = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        AddSpacedChildren(actionPanel, actions, TrueBimTheme.Spacing8);
        Grid.SetColumn(actionPanel, 1);
        footer.Children.Add(actionPanel);
        return footer;
    }

    public static Border CreateSectionCard(string title, UIElement content)
    {
        StackPanel stack = new()
        {
            Margin = TrueBimTheme.SectionPadding
        };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = TrueBimTheme.SectionTitleFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12)
        });
        stack.Children.Add(content);

        return new Border
        {
            Background = TrueBimBrushes.Surface,
            BorderBrush = TrueBimBrushes.Border,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Child = stack
        };
    }

    public static Border CreateStatusBadge(string text, TrueBimUiSeverity severity)
    {
        return new Border
        {
            Background = TrueBimBrushes.BackgroundForSeverity(severity),
            BorderBrush = TrueBimBrushes.ForSeverity(severity),
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(10),
            Padding = TrueBimTheme.BadgePadding,
            Child = new TextBlock
            {
                Text = text,
                Foreground = TrueBimBrushes.ForSeverity(severity),
                FontSize = TrueBimTheme.CaptionFontSize,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    public static Border CreateInfoBanner(string text, TrueBimUiSeverity severity = TrueBimUiSeverity.Info)
    {
        return CreateInfoBanner(new TextBlock
        {
            Text = text,
            Foreground = TrueBimBrushes.TextPrimary,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        }, severity);
    }

    public static Border CreateInfoBanner(UIElement contentElement, TrueBimUiSeverity severity = TrueBimUiSeverity.Info)
    {
        DockPanel content = new()
        {
            LastChildFill = true
        };
        content.Children.Add(IconFactory.Create(GetSeverityIcon(severity), TrueBimBrushes.ForSeverity(severity).Color, TrueBimTheme.IconSizeSmall));
        content.Children.Add(contentElement);

        return new Border
        {
            Background = TrueBimBrushes.BackgroundForSeverity(severity),
            BorderBrush = TrueBimBrushes.ForSeverity(severity),
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Padding = new Thickness(TrueBimTheme.Spacing12),
            Child = content
        };
    }

    public static Button CreatePrimaryButton(string text, TrueBimIcon icon, RoutedEventHandler? clickHandler = null, bool isEnabled = true, double minWidth = 110)
    {
        return CreateButton(text, icon, TrueBimButtonStyleKind.Primary, Colors.White, clickHandler, isEnabled, minWidth);
    }

    public static Button CreateSecondaryButton(string text, TrueBimIcon icon, RoutedEventHandler? clickHandler = null, bool isEnabled = true, double minWidth = 110)
    {
        return CreateButton(text, icon, TrueBimButtonStyleKind.Secondary, TrueBimTheme.TextSecondaryColor, clickHandler, isEnabled, minWidth);
    }

    public static Button CreateDangerButton(string text, TrueBimIcon icon, RoutedEventHandler? clickHandler = null, bool isEnabled = true, double minWidth = 110)
    {
        return CreateButton(text, icon, TrueBimButtonStyleKind.Danger, TrueBimTheme.DangerColor, clickHandler, isEnabled, minWidth);
    }

    public static TextBox CreateSearchBox(string? tooltip = null)
    {
        return new TextBox
        {
            MinWidth = 180,
            Style = TrueBimStyles.CreateTextBoxStyle(),
            ToolTip = tooltip ?? "Поиск"
        };
    }

    public static TextBlock CreateFieldLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = TrueBimBrushes.TextSecondary,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing4)
        };
    }

    public static Grid CreateSettingsRow(string label, string description, Control control)
    {
        Grid row = new()
        {
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        StackPanel text = new();
        text.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary
        });
        text.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = TrueBimBrushes.TextSecondary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, TrueBimTheme.Spacing4, TrueBimTheme.Spacing16, 0)
        });
        row.Children.Add(text);

        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private static Button CreateButton(
        string text,
        TrueBimIcon icon,
        TrueBimButtonStyleKind kind,
        Color iconColor,
        RoutedEventHandler? clickHandler,
        bool isEnabled,
        double minWidth)
    {
        Button button = new()
        {
            Content = IconFactory.CreateButtonContent(icon, text, iconColor, TrueBimTheme.IconSizeSmall),
            MinWidth = minWidth,
            Style = TrueBimStyles.CreateButtonStyle(kind),
            IsEnabled = isEnabled
        };
        if (clickHandler is not null)
        {
            button.Click += clickHandler;
        }

        return button;
    }

    private static void AddSpacedChildren(Panel panel, IReadOnlyList<UIElement> items, double gap)
    {
        for (int index = 0; index < items.Count; index++)
        {
            UIElement item = items[index];
            if (index > 0 && item is FrameworkElement element)
            {
                element.Margin = new Thickness(gap, 0, 0, 0);
            }

            panel.Children.Add(item);
        }
    }

    private static TrueBimIcon GetSeverityIcon(TrueBimUiSeverity severity)
    {
        return severity switch
        {
            TrueBimUiSeverity.Success => TrueBimIcon.Check,
            TrueBimUiSeverity.Warning => TrueBimIcon.Warning,
            TrueBimUiSeverity.Danger => TrueBimIcon.Error,
            _ => TrueBimIcon.Info
        };
    }
}
