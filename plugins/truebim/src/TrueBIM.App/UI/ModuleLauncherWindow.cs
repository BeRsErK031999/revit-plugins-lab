using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.Modules;

namespace TrueBIM.App.UI;

public sealed class ModuleLauncherWindow : Window
{
    public ModuleLauncherWindow(IEnumerable<ITrueBimModule> modules)
    {
        Title = "TrueBIM";
        Width = 520;
        Height = 360;
        MinWidth = 420;
        MinHeight = 280;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent(modules.ToList());
    }

    private UIElement CreateContent(IReadOnlyCollection<ITrueBimModule> modules)
    {
        Grid root = new()
        {
            Margin = new Thickness(20)
        };

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock title = new()
        {
            Text = "Modules",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 16)
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        ListBox moduleList = new()
        {
            BorderThickness = new Thickness(1),
            ItemsSource = modules.Select(CreateModuleItem).ToList()
        };
        Grid.SetRow(moduleList, 1);
        root.Children.Add(moduleList);

        Button closeButton = new()
        {
            Content = "Close",
            MinWidth = 96,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
            IsCancel = true
        };
        closeButton.Click += (_, _) => Close();
        Grid.SetRow(closeButton, 2);
        root.Children.Add(closeButton);

        return root;
    }

    private static ListBoxItem CreateModuleItem(ITrueBimModule module)
    {
        StackPanel panel = new()
        {
            Margin = new Thickness(8)
        };

        TextBlock name = new()
        {
            Text = module.DisplayName,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold
        };
        panel.Children.Add(name);

        TextBlock description = new()
        {
            Text = module.Description,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray
        };
        panel.Children.Add(description);

        TextBlock status = new()
        {
            Text = module.IsEnabledByDefault ? "Enabled" : "Disabled",
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = module.IsEnabledByDefault ? Brushes.DarkGreen : Brushes.DarkRed
        };
        panel.Children.Add(status);

        return new ListBoxItem
        {
            Content = panel,
            IsEnabled = module.IsEnabledByDefault
        };
    }
}
