using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.Modules.BimTools.ParameterAudit.Services;
using TrueBIM.App.UI;
using WpfGrid = System.Windows.Controls.Grid;

namespace TrueBIM.App.Modules.BimTools.ParameterAudit.UI;

public sealed class ParameterAuditGuideWindow : TrueBimWindow
{
    public ParameterAuditGuideWindow()
    {
        Title = ParameterAuditGuideCatalog.Title;
        Icon = IconFactory.CreateImage(TrueBimIcon.Help, 32);
        Width = 820;
        Height = 720;
        MinWidth = 660;
        MinHeight = 520;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();
    }

    private UIElement CreateContent()
    {
        WpfGrid root = new()
        {
            Margin = new Thickness(18)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        StackPanel header = new();
        header.Children.Add(new TextBlock
        {
            Text = ParameterAuditGuideCatalog.Title,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = ParameterAuditGuideCatalog.Summary,
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 14)
        });
        root.Children.Add(header);

        ScrollViewer scroll = new()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        StackPanel sections = new();
        foreach (ParameterAuditGuideSection section in ParameterAuditGuideCatalog.Sections)
        {
            sections.Children.Add(CreateSection(section));
        }

        scroll.Content = sections;
        WpfGrid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        Button closeButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Закрыть"),
            Width = 120,
            Height = 32,
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsCancel = true
        };
        closeButton.Click += (_, _) => Close();
        WpfGrid.SetRow(closeButton, 2);
        root.Children.Add(closeButton);
        return root;
    }

    private static UIElement CreateSection(ParameterAuditGuideSection section)
    {
        Border card = new()
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(218, 222, 228)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 8, 12)
        };
        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            Text = section.Title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        for (int index = 0; index < section.Steps.Count; index++)
        {
            WpfGrid row = new()
            {
                Margin = new Thickness(0, 3, 0, 3)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            TextBlock number = new()
            {
                Text = $"{index + 1}.",
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.SteelBlue,
                Margin = new Thickness(0, 0, 8, 0)
            };
            row.Children.Add(number);
            TextBlock text = new()
            {
                Text = section.Steps[index],
                TextWrapping = TextWrapping.Wrap
            };
            WpfGrid.SetColumn(text, 1);
            row.Children.Add(text);
            content.Children.Add(row);
        }

        card.Child = content;
        return card;
    }
}
