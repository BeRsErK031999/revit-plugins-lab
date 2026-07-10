using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Models;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.ScheduleColumnCollapse.UI;

public sealed class ScheduleSourceSelectionWindow : TrueBimWindow
{
    private readonly RadioButton activeViewOption = new();
    private readonly RadioButton projectBrowserOption = new();
    private readonly RadioButton listSelectionOption = new();

    public ScheduleSourceSelectionWindow(
        string? activeScheduleName,
        IReadOnlyCollection<string> selectedScheduleNames,
        IntPtr ownerWindowHandle)
    {
        Title = "Свернуть ВРС";
        Icon = IconFactory.CreateImage(TrueBimIcon.ScheduleCollapse, 32);
        Width = 600;
        Height = 430;
        MinWidth = 600;
        MinHeight = 430;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent(activeScheduleName, selectedScheduleNames);

        if (ownerWindowHandle != IntPtr.Zero)
        {
            new WindowInteropHelper(this)
            {
                Owner = ownerWindowHandle
            };
        }
    }

    public ScheduleSourceSelection SelectedSource { get; private set; } = ScheduleSourceSelection.ActiveView;

    private UIElement CreateContent(string? activeScheduleName, IReadOnlyCollection<string> selectedScheduleNames)
    {
        Grid root = new()
        {
            Margin = TrueBimTheme.WindowPadding
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock title = new()
        {
            Text = "Как выбрать спецификацию?",
            FontSize = TrueBimTheme.SectionTitleFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12)
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        StackPanel options = new()
        {
            Orientation = Orientation.Vertical
        };
        ConfigureOption(
            activeViewOption,
            "В активном окне",
            activeScheduleName is null
                ? "Активное окно сейчас не является спецификацией."
                : $"Будет обработана: {activeScheduleName}",
            activeScheduleName is not null);
        activeViewOption.IsChecked = activeScheduleName is not null;
        options.Children.Add(CreateOptionBlock(activeViewOption));

        ConfigureOption(
            projectBrowserOption,
            "Выбранная в диспетчере проекта",
            GetProjectBrowserHint(selectedScheduleNames),
            selectedScheduleNames.Count == 1);
        projectBrowserOption.IsChecked = activeScheduleName is null && selectedScheduleNames.Count == 1;
        options.Children.Add(CreateOptionBlock(projectBrowserOption));

        ConfigureOption(
            listSelectionOption,
            "Выбрать из списка с поиском",
            "После подтверждения откроется список всех спецификаций документа.",
            isEnabled: true);
        listSelectionOption.IsChecked = activeScheduleName is null && selectedScheduleNames.Count != 1;
        options.Children.Add(CreateOptionBlock(listSelectionOption));

        Grid.SetRow(options, 1);
        root.Children.Add(options);

        StackPanel footer = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0)
        };

        Button confirmButton = TrueBimUi.CreatePrimaryButton("Подтвердить", TrueBimIcon.Apply, minWidth: 128);
        confirmButton.IsDefault = true;
        confirmButton.Click += (_, _) => AcceptSelection();
        footer.Children.Add(confirmButton);

        Button cancelButton = TrueBimUi.CreateSecondaryButton("Отмена", TrueBimIcon.Close, minWidth: 110);
        cancelButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        cancelButton.IsCancel = true;
        cancelButton.Click += (_, _) => DialogResult = false;
        footer.Children.Add(cancelButton);

        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        return root;
    }

    private static void ConfigureOption(RadioButton option, string title, string hint, bool isEnabled)
    {
        option.Content = CreateOptionContent(title, hint);
        option.GroupName = "ScheduleSourceSelection";
        option.IsEnabled = isEnabled;
        option.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8);
        option.VerticalContentAlignment = VerticalAlignment.Top;
    }

    private static UIElement CreateOptionBlock(RadioButton option)
    {
        Border border = new()
        {
            Background = TrueBimBrushes.Surface,
            BorderBrush = TrueBimBrushes.Border,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Padding = new Thickness(TrueBimTheme.Spacing12),
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8),
            Child = option
        };

        return border;
    }

    private static UIElement CreateOptionContent(string title, string hint)
    {
        StackPanel panel = new();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary
        });
        panel.Children.Add(new TextBlock
        {
            Text = hint,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TrueBimBrushes.TextSecondary,
            Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, 0)
        });

        return panel;
    }

    private static string GetProjectBrowserHint(IReadOnlyCollection<string> selectedScheduleNames)
    {
        return selectedScheduleNames.Count switch
        {
            0 => "В диспетчере проекта спецификация не выбрана.",
            1 => $"Будет обработана: {selectedScheduleNames.First()}",
            _ => $"Выбрано спецификаций: {selectedScheduleNames.Count}. Оставьте выбранной одну спецификацию."
        };
    }

    private void AcceptSelection()
    {
        if (activeViewOption.IsChecked == true)
        {
            SelectedSource = ScheduleSourceSelection.ActiveView;
        }
        else if (projectBrowserOption.IsChecked == true)
        {
            SelectedSource = ScheduleSourceSelection.ProjectBrowserSelection;
        }
        else
        {
            SelectedSource = ScheduleSourceSelection.ListSelection;
        }

        DialogResult = true;
    }
}
