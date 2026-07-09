using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace TrueBIM.App.UI.DesignSystem;

public enum TrueBimButtonStyleKind
{
    Primary,
    Secondary,
    Danger,
    Ghost
}

public static class TrueBimStyles
{
    public const string PrimaryButtonStyleKey = "TrueBim.PrimaryButtonStyle";
    public const string SecondaryButtonStyleKey = "TrueBim.SecondaryButtonStyle";
    public const string DangerButtonStyleKey = "TrueBim.DangerButtonStyle";
    public const string GhostButtonStyleKey = "TrueBim.GhostButtonStyle";
    public const string TextBoxStyleKey = "TrueBim.TextBoxStyle";
    public const string ComboBoxStyleKey = "TrueBim.ComboBoxStyle";
    public const string CheckBoxStyleKey = "TrueBim.CheckBoxStyle";
    public const string DataGridStyleKey = "TrueBim.DataGridStyle";
    public const string GroupBoxStyleKey = "TrueBim.GroupBoxStyle";
    public const string TabControlStyleKey = "TrueBim.TabControlStyle";
    public const string ListBoxStyleKey = "TrueBim.ListBoxStyle";
    public const string ListBoxItemStyleKey = "TrueBim.ListBoxItemStyle";

    public static void RegisterLocalResources(ResourceDictionary resources)
    {
        resources[PrimaryButtonStyleKey] = CreateButtonStyle(TrueBimButtonStyleKind.Primary);
        resources[SecondaryButtonStyleKey] = CreateButtonStyle(TrueBimButtonStyleKind.Secondary);
        resources[DangerButtonStyleKey] = CreateButtonStyle(TrueBimButtonStyleKind.Danger);
        resources[GhostButtonStyleKey] = CreateButtonStyle(TrueBimButtonStyleKind.Ghost);
        resources[TextBoxStyleKey] = CreateTextBoxStyle();
        resources[ComboBoxStyleKey] = CreateComboBoxStyle();
        resources[CheckBoxStyleKey] = CreateCheckBoxStyle();
        resources[DataGridStyleKey] = CreateDataGridStyle();
        resources[GroupBoxStyleKey] = CreateGroupBoxStyle();
        resources[TabControlStyleKey] = CreateTabControlStyle();
        resources[ListBoxStyleKey] = CreateListBoxStyle();
        resources[ListBoxItemStyleKey] = CreateListBoxItemStyle();
    }

    public static Style CreateButtonStyle(TrueBimButtonStyleKind kind = TrueBimButtonStyleKind.Secondary)
    {
        ButtonPalette palette = GetButtonPalette(kind);
        Style style = new(typeof(Button));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, TrueBimTheme.ControlHeight32));
        style.Setters.Add(new Setter(Control.PaddingProperty, TrueBimTheme.ControlPadding));
        style.Setters.Add(new Setter(Control.BackgroundProperty, palette.Background));
        style.Setters.Add(new Setter(Control.ForegroundProperty, palette.Foreground));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, palette.Border));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(TrueBimTheme.BorderWidth)));
        style.Setters.Add(new Setter(Control.FontWeightProperty, kind == TrueBimButtonStyleKind.Primary ? FontWeights.SemiBold : FontWeights.Normal));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(Control.TemplateProperty, CreateButtonTemplate()));
        style.Triggers.Add(new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true,
            Setters =
            {
                new Setter(Control.BackgroundProperty, palette.HoverBackground),
                new Setter(Control.BorderBrushProperty, palette.HoverBorder)
            }
        });
        style.Triggers.Add(new Trigger
        {
            Property = ButtonBase.IsPressedProperty,
            Value = true,
            Setters =
            {
                new Setter(Control.BackgroundProperty, palette.PressedBackground),
                new Setter(Control.BorderBrushProperty, palette.PressedBorder)
            }
        });
        style.Triggers.Add(new Trigger
        {
            Property = UIElement.IsEnabledProperty,
            Value = false,
            Setters =
            {
                new Setter(Control.BackgroundProperty, TrueBimBrushes.DisabledSurface),
                new Setter(Control.ForegroundProperty, TrueBimBrushes.Disabled),
                new Setter(Control.BorderBrushProperty, TrueBimBrushes.Border),
                new Setter(FrameworkElement.CursorProperty, Cursors.Arrow)
            }
        });

        return style;
    }

    public static Style CreateTextBoxStyle()
    {
        Style style = new(typeof(TextBox));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, TrueBimTheme.ControlHeight32));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(TrueBimTheme.Spacing8, 0, TrueBimTheme.Spacing8, 0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, TrueBimBrushes.Surface));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TrueBimBrushes.TextPrimary));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, TrueBimBrushes.Border));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(TrueBimTheme.BorderWidth)));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        return style;
    }

    public static Style CreateComboBoxStyle()
    {
        Style style = new(typeof(ComboBox));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, TrueBimTheme.ControlHeight32));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(TrueBimTheme.Spacing8, 0, TrueBimTheme.Spacing8, 0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, TrueBimBrushes.Surface));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TrueBimBrushes.TextPrimary));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, TrueBimBrushes.Border));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(TrueBimTheme.BorderWidth)));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        return style;
    }

    public static Style CreateCheckBoxStyle()
    {
        Style style = new(typeof(CheckBox));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TrueBimBrushes.TextPrimary));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        return style;
    }

    public static Style CreateDataGridStyle()
    {
        Style style = new(typeof(DataGrid));
        style.Setters.Add(new Setter(Control.BackgroundProperty, TrueBimBrushes.Surface));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, TrueBimBrushes.Border));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(TrueBimTheme.BorderWidth)));
        style.Setters.Add(new Setter(DataGrid.RowHeightProperty, 34.0));
        style.Setters.Add(new Setter(DataGrid.HeadersVisibilityProperty, DataGridHeadersVisibility.Column));
        style.Setters.Add(new Setter(DataGrid.GridLinesVisibilityProperty, DataGridGridLinesVisibility.Horizontal));
        style.Setters.Add(new Setter(DataGrid.HorizontalGridLinesBrushProperty, TrueBimBrushes.Border));
        style.Setters.Add(new Setter(DataGrid.VerticalGridLinesBrushProperty, TrueBimBrushes.Border));
        style.Setters.Add(new Setter(DataGrid.AlternatingRowBackgroundProperty, TrueBimBrushes.SurfaceAlt));
        style.Setters.Add(new Setter(DataGrid.ColumnHeaderStyleProperty, CreateDataGridColumnHeaderStyle()));
        style.Setters.Add(new Setter(DataGrid.RowStyleProperty, CreateDataGridRowStyle()));
        return style;
    }

    public static Style CreateGroupBoxStyle()
    {
        Style style = new(typeof(GroupBox));
        style.Setters.Add(new Setter(Control.BackgroundProperty, TrueBimBrushes.Surface));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, TrueBimBrushes.Border));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(TrueBimTheme.BorderWidth)));
        style.Setters.Add(new Setter(Control.PaddingProperty, TrueBimTheme.SectionPadding));
        return style;
    }

    public static Style CreateTabControlStyle()
    {
        Style style = new(typeof(TabControl));
        style.Setters.Add(new Setter(Control.BackgroundProperty, TrueBimBrushes.Surface));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, TrueBimBrushes.Border));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(TrueBimTheme.Spacing4)));
        return style;
    }

    public static Style CreateListBoxStyle()
    {
        Style style = new(typeof(ListBox));
        style.Setters.Add(new Setter(Control.BackgroundProperty, TrueBimBrushes.Surface));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, TrueBimBrushes.Border));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(TrueBimTheme.BorderWidth)));
        style.Setters.Add(new Setter(ListBox.ItemContainerStyleProperty, CreateListBoxItemStyle()));
        return style;
    }

    public static Style CreateListBoxItemStyle()
    {
        Style style = new(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        return style;
    }

    private static Style CreateDataGridColumnHeaderStyle()
    {
        Style style = new(typeof(DataGridColumnHeader));
        style.Setters.Add(new Setter(Control.BackgroundProperty, TrueBimBrushes.SurfaceAlt));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TrueBimBrushes.TextSecondary));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, TrueBimBrushes.Border));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, TrueBimTheme.BorderWidth)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(TrueBimTheme.Spacing8, 0, TrueBimTheme.Spacing8, 0)));
        return style;
    }

    private static Style CreateDataGridRowStyle()
    {
        Style style = new(typeof(DataGridRow));
        style.Setters.Add(new Setter(Control.BackgroundProperty, TrueBimBrushes.Surface));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TrueBimBrushes.TextPrimary));
        style.Triggers.Add(new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true,
            Setters =
            {
                new Setter(Control.BackgroundProperty, TrueBimBrushes.InfoBackground)
            }
        });
        style.Triggers.Add(new Trigger
        {
            Property = DataGridRow.IsSelectedProperty,
            Value = true,
            Setters =
            {
                new Setter(Control.BackgroundProperty, TrueBimBrushes.InfoBackground),
                new Setter(Control.ForegroundProperty, TrueBimBrushes.TextPrimary)
            }
        });
        return style;
    }

    private static ControlTemplate CreateButtonTemplate()
    {
        FrameworkElementFactory chrome = new(typeof(Border));
        chrome.Name = "Chrome";
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(TrueBimTheme.Radius6));
        chrome.SetBinding(Border.BackgroundProperty, CreateTemplateBinding(Control.BackgroundProperty));
        chrome.SetBinding(Border.BorderBrushProperty, CreateTemplateBinding(Control.BorderBrushProperty));
        chrome.SetBinding(Border.BorderThicknessProperty, CreateTemplateBinding(Control.BorderThicknessProperty));
        chrome.SetBinding(Border.PaddingProperty, CreateTemplateBinding(Control.PaddingProperty));

        FrameworkElementFactory content = new(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetBinding(ContentPresenter.ContentProperty, CreateTemplateBinding(ContentControl.ContentProperty));
        content.SetBinding(ContentPresenter.ContentTemplateProperty, CreateTemplateBinding(ContentControl.ContentTemplateProperty));
        chrome.AppendChild(content);

        return new ControlTemplate(typeof(Button))
        {
            VisualTree = chrome
        };
    }

    private static Binding CreateTemplateBinding(DependencyProperty property)
    {
        return new Binding
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
            Path = new PropertyPath(property)
        };
    }

    private static ButtonPalette GetButtonPalette(TrueBimButtonStyleKind kind)
    {
        return kind switch
        {
            TrueBimButtonStyleKind.Primary => new ButtonPalette(
                TrueBimBrushes.Primary,
                TrueBimBrushes.PrimaryHover,
                TrueBimBrushes.PrimaryPressed,
                TrueBimBrushes.Primary,
                TrueBimBrushes.PrimaryHover,
                TrueBimBrushes.PrimaryPressed,
                Brushes.White),
            TrueBimButtonStyleKind.Danger => new ButtonPalette(
                TrueBimBrushes.DangerBackground,
                TrueBimBrushes.DangerBackground,
                TrueBimBrushes.Danger,
                TrueBimBrushes.Danger,
                TrueBimBrushes.Danger,
                TrueBimBrushes.Danger,
                TrueBimBrushes.Danger),
            TrueBimButtonStyleKind.Ghost => new ButtonPalette(
                Brushes.Transparent,
                TrueBimBrushes.SurfaceAlt,
                TrueBimBrushes.Border,
                Brushes.Transparent,
                TrueBimBrushes.Border,
                TrueBimBrushes.Border,
                TrueBimBrushes.TextSecondary),
            _ => new ButtonPalette(
                TrueBimBrushes.Surface,
                TrueBimBrushes.SurfaceAlt,
                TrueBimBrushes.Border,
                TrueBimBrushes.Border,
                TrueBimBrushes.Accent,
                TrueBimBrushes.Accent,
                TrueBimBrushes.TextPrimary)
        };
    }

    private sealed record ButtonPalette(
        Brush Background,
        Brush HoverBackground,
        Brush PressedBackground,
        Brush Border,
        Brush HoverBorder,
        Brush PressedBorder,
        Brush Foreground);
}
