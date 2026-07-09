using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.Modules;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.UI;

public sealed class ModuleLauncherWindow : TrueBimWindow
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
        ApplyTrueBimShell(
            header: TrueBimUi.CreateHeader(
                "Модули TrueBIM",
                "Запуск инструментов, диагностика и управление доступностью модулей в текущей сессии Revit.",
                TrueBimIcon.App),
            commandBar: null,
            body: CreateContent(modules.ToList()),
            status: null,
            footer: CreateFooter());
    }

    private UIElement CreateContent(IReadOnlyCollection<ModuleRegistryEntry> modules)
    {
        ListBox moduleList = new()
        {
            Style = TrueBimStyles.CreateListBoxStyle(),
            ItemsSource = modules.Select(CreateModuleItem).ToList()
        };

        return TrueBimUi.CreateSectionCard("Доступные инструменты", moduleList);
    }

    private UIElement CreateFooter()
    {
        TextBlock status = new()
        {
            Text = "Отключенный модуль остается видимым в списке, но не запускается из launcher.",
            Foreground = TrueBimBrushes.TextSecondary,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        Button logsButton = TrueBimUi.CreateSecondaryButton("Логи", TrueBimIcon.Logs, (_, _) => openLogs(this));
        logsButton.ToolTip = "Открыть файл логов TrueBIM.";

        Button closeButton = TrueBimUi.CreateSecondaryButton("Закрыть", TrueBimIcon.Close, (_, _) => Close());
        closeButton.IsCancel = true;
        closeButton.ToolTip = "Закрыть окно TrueBIM.";

        return TrueBimUi.CreateFooter(status, logsButton, closeButton);
    }

    private ListBoxItem CreateModuleItem(ModuleRegistryEntry module)
    {
        Grid panel = new()
        {
            Margin = new Thickness(TrueBimTheme.Spacing12)
        };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Image moduleIcon = new()
        {
            Source = IconFactory.CreateImage(module.Icon, TrueBimTheme.PrimaryColor),
            Width = TrueBimTheme.IconSizeHeader,
            Height = TrueBimTheme.IconSizeHeader,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, TrueBimTheme.Spacing12, 0)
        };
        Grid.SetColumn(moduleIcon, 0);
        panel.Children.Add(moduleIcon);

        StackPanel moduleDetails = new();

        TextBlock name = new()
        {
            Text = LocalizeModuleText(module.DisplayName),
            FontSize = TrueBimTheme.SectionTitleFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary
        };
        moduleDetails.Children.Add(name);

        TextBlock description = new()
        {
            Text = LocalizeModuleText(module.Description),
            Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = TrueBimBrushes.TextSecondary
        };
        moduleDetails.Children.Add(description);

        CheckBox enabledToggle = new()
        {
            Content = "Включено",
            IsChecked = module.IsEnabled,
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0),
            Foreground = module.IsEnabled ? TrueBimBrushes.Success : TrueBimBrushes.Danger,
            Style = TrueBimStyles.CreateCheckBoxStyle(),
            ToolTip = "Включает или отключает модуль в launcher."
        };
        moduleDetails.Children.Add(enabledToggle);

        Grid.SetColumn(moduleDetails, 1);
        panel.Children.Add(moduleDetails);

        Button openButton = TrueBimUi.CreatePrimaryButton(
            "Открыть",
            TrueBimIcon.Open,
            clickHandler: null,
            isEnabled: module.IsEnabled && moduleActions.ContainsKey(module.Id));
        openButton.Margin = new Thickness(TrueBimTheme.Spacing16, 0, 0, 0);
        openButton.VerticalAlignment = VerticalAlignment.Center;
        openButton.ToolTip = "Открыть выбранный модуль.";
        openButton.Click += (_, _) => moduleActions[module.Id](this);
        enabledToggle.Checked += (_, _) => UpdateModuleEnabled(module, enabledToggle, openButton, isEnabled: true);
        enabledToggle.Unchecked += (_, _) => UpdateModuleEnabled(module, enabledToggle, openButton, isEnabled: false);
        Grid.SetColumn(openButton, 2);
        panel.Children.Add(openButton);

        Border card = new()
        {
            Background = TrueBimBrushes.Surface,
            BorderBrush = TrueBimBrushes.Border,
            BorderThickness = new Thickness(0, 0, 0, TrueBimTheme.BorderWidth),
            Child = panel
        };

        return new ListBoxItem
        {
            Content = card,
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
        enabledToggle.Foreground = isEnabled ? TrueBimBrushes.Success : TrueBimBrushes.Danger;
        openButton.IsEnabled = isEnabled && moduleActions.ContainsKey(module.Id);
    }

    private static string LocalizeModuleText(string text)
    {
        return text switch
        {
            "Sheet Numbering" => "Нумератор листов",
            "Нумерация листов" => "Нумератор листов",
            "Renumber Revit sheets with preview and duplicate protection." => "Перенумерация листов Revit с предпросмотром и защитой от дублей.",
            _ => text
        };
    }
}
