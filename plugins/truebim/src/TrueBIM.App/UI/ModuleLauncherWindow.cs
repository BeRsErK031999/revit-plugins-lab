using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.Modules;

namespace TrueBIM.App.UI;

public sealed class ModuleLauncherWindow : Window
{
    private readonly IReadOnlyDictionary<string, Action<Window>> moduleActions;
    private readonly Action<string, bool> setModuleEnabled;
    private readonly Action<Window> openLogs;

    public ModuleLauncherWindow(
        IEnumerable<ModuleRegistryEntry> modules,
        IReadOnlyDictionary<string, Action<Window>> moduleActions,
        Action<string, bool> setModuleEnabled,
        Action<Window> openLogs)
    {
        this.moduleActions = moduleActions;
        this.setModuleEnabled = setModuleEnabled;
        this.openLogs = openLogs;
        Title = "TrueBIM";
        Icon = IconFactory.CreateImage(TrueBimIcon.App, 32);
        Width = 560;
        Height = 360;
        MinWidth = 500;
        MinHeight = 280;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent(modules.ToList());
    }

    private UIElement CreateContent(IReadOnlyCollection<ModuleRegistryEntry> modules)
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
            Text = "Модули",
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
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Logs, "Логи"),
            MinWidth = 110,
            Height = 32,
            ToolTip = "Открыть файл логов TrueBIM."
        };
        logsButton.Click += (_, _) => openLogs(this);
        footer.Children.Add(logsButton);

        Button closeButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Закрыть"),
            MinWidth = 110,
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true,
            ToolTip = "Закрыть окно TrueBIM."
        };
        closeButton.Click += (_, _) => Close();
        footer.Children.Add(closeButton);

        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        return root;
    }

    private ListBoxItem CreateModuleItem(ModuleRegistryEntry module)
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
            Text = LocalizeModuleText(module.DisplayName),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold
        };
        moduleDetails.Children.Add(name);

        TextBlock description = new()
        {
            Text = LocalizeModuleText(module.Description),
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray
        };
        moduleDetails.Children.Add(description);

        CheckBox enabledToggle = new()
        {
            Content = "Включено",
            IsChecked = module.IsEnabled,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = module.IsEnabled ? Brushes.DarkGreen : Brushes.DarkRed,
            ToolTip = "Включает или отключает модуль в launcher."
        };
        moduleDetails.Children.Add(enabledToggle);

        Grid.SetColumn(moduleDetails, 0);
        panel.Children.Add(moduleDetails);

        Button openButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Open, "Открыть"),
            MinWidth = 110,
            Height = 30,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = module.IsEnabled && moduleActions.ContainsKey(module.Id),
            ToolTip = "Открыть выбранный модуль."
        };
        openButton.Click += (_, _) => moduleActions[module.Id](this);
        enabledToggle.Checked += (_, _) => UpdateModuleEnabled(module, enabledToggle, openButton, isEnabled: true);
        enabledToggle.Unchecked += (_, _) => UpdateModuleEnabled(module, enabledToggle, openButton, isEnabled: false);
        Grid.SetColumn(openButton, 1);
        panel.Children.Add(openButton);

        return new ListBoxItem
        {
            Content = panel,
            IsEnabled = true
        };
    }

    private void UpdateModuleEnabled(
        ModuleRegistryEntry module,
        CheckBox enabledToggle,
        Button openButton,
        bool isEnabled)
    {
        setModuleEnabled(module.Id, isEnabled);
        enabledToggle.Foreground = isEnabled ? Brushes.DarkGreen : Brushes.DarkRed;
        openButton.IsEnabled = isEnabled && moduleActions.ContainsKey(module.Id);
    }

    private static string LocalizeModuleText(string text)
    {
        return text switch
        {
            "Sheet Numbering" => "Нумерация листов",
            "Renumber Revit sheets with preview and duplicate protection." => "Перенумерация листов Revit с предпросмотром и защитой от дублей.",
            _ => text
        };
    }
}
