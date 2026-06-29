using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.Modules;

namespace TrueBIM.App.UI;

public sealed class ModuleLauncherWindow : Window
{
    private readonly IReadOnlyDictionary<string, Action<Window>> moduleActions;
    private readonly Action<Window> openLogs;

    public ModuleLauncherWindow(
        IEnumerable<ITrueBimModule> modules,
        IReadOnlyDictionary<string, Action<Window>> moduleActions,
        Action<Window> openLogs)
    {
        this.moduleActions = moduleActions;
        this.openLogs = openLogs;
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

        StackPanel footer = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        Button logsButton = new()
        {
            Content = "Logs",
            MinWidth = 96,
            Height = 32
        };
        logsButton.Click += (_, _) => openLogs(this);
        footer.Children.Add(logsButton);

        Button closeButton = new()
        {
            Content = "Close",
            MinWidth = 96,
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true
        };
        closeButton.Click += (_, _) => Close();
        footer.Children.Add(closeButton);

        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        return root;
    }

    private ListBoxItem CreateModuleItem(ITrueBimModule module)
    {
        Grid panel = new()
        {
            Margin = new Thickness(8)
        };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        StackPanel moduleDetails = new();

        TextBlock name = new()
        {
            Text = module.DisplayName,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold
        };
        moduleDetails.Children.Add(name);

        TextBlock description = new()
        {
            Text = module.Description,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray
        };
        moduleDetails.Children.Add(description);

        TextBlock status = new()
        {
            Text = module.IsEnabledByDefault ? "Enabled" : "Disabled",
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = module.IsEnabledByDefault ? Brushes.DarkGreen : Brushes.DarkRed
        };
        moduleDetails.Children.Add(status);

        Grid.SetColumn(moduleDetails, 0);
        panel.Children.Add(moduleDetails);

        Button openButton = new()
        {
            Content = "Open",
            MinWidth = 80,
            Height = 30,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = module.IsEnabledByDefault && moduleActions.ContainsKey(module.Id)
        };
        openButton.Click += (_, _) => moduleActions[module.Id](this);
        Grid.SetColumn(openButton, 1);
        panel.Children.Add(openButton);

        return new ListBoxItem
        {
            Content = panel,
            IsEnabled = module.IsEnabledByDefault
        };
    }
}
