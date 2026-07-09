using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Models;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.ScheduleColumnCollapse.UI;

public sealed class ScheduleSelectionWindow : TrueBimWindow
{
    private readonly IReadOnlyList<ScheduleSelectionItem> allSchedules;
    private readonly ListBox scheduleList = new();
    private readonly TextBox searchBox = new();

    public ScheduleSelectionWindow(
        IEnumerable<ScheduleSelectionItem> schedules,
        string contextMessage,
        IntPtr ownerWindowHandle)
    {
        allSchedules = schedules.ToList();

        Title = "Выбор спецификации";
        Icon = IconFactory.CreateImage(TrueBimIcon.ScheduleCollapse, 32);
        Width = 520;
        Height = 420;
        MinWidth = 440;
        MinHeight = 320;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent(contextMessage);

        if (ownerWindowHandle != IntPtr.Zero)
        {
            new WindowInteropHelper(this)
            {
                Owner = ownerWindowHandle
            };
        }

        RefreshList();
    }

    public ScheduleSelectionItem? SelectedSchedule { get; private set; }

    private UIElement CreateContent(string contextMessage)
    {
        Grid root = new()
        {
            Margin = TrueBimTheme.WindowPadding
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock message = new()
        {
            Text = contextMessage,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TrueBimBrushes.TextSecondary,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };
        Grid.SetRow(message, 0);
        root.Children.Add(message);

        searchBox.MinHeight = TrueBimTheme.ControlHeight32;
        searchBox.Style = TrueBimStyles.CreateTextBoxStyle();
        searchBox.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8);
        searchBox.ToolTip = "Фильтр по названию спецификации.";
        searchBox.TextChanged += (_, _) => RefreshList();
        Grid.SetRow(searchBox, 1);
        root.Children.Add(searchBox);

        scheduleList.Style = TrueBimStyles.CreateListBoxStyle();
        scheduleList.MouseDoubleClick += (_, _) => AcceptSelection();
        scheduleList.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                AcceptSelection();
                args.Handled = true;
            }
        };
        Grid.SetRow(scheduleList, 2);
        root.Children.Add(scheduleList);

        StackPanel footer = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0)
        };

        Button okButton = TrueBimUi.CreatePrimaryButton("Выбрать", TrueBimIcon.Apply, minWidth: 110);
        okButton.IsDefault = true;
        okButton.Click += (_, _) => AcceptSelection();
        footer.Children.Add(okButton);

        Button cancelButton = TrueBimUi.CreateSecondaryButton("Отмена", TrueBimIcon.Close, minWidth: 110);
        cancelButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        cancelButton.IsCancel = true;
        cancelButton.Click += (_, _) => DialogResult = false;
        footer.Children.Add(cancelButton);

        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        return root;
    }

    private void RefreshList()
    {
        string filter = searchBox.Text.Trim();
        IEnumerable<ScheduleSelectionItem> filtered = allSchedules;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            filtered = filtered.Where(schedule =>
                schedule.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
                || schedule.Context.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        List<ListBoxItem> items = filtered
            .Select(CreateListItem)
            .ToList();

        scheduleList.ItemsSource = items;
        if (items.Count > 0)
        {
            scheduleList.SelectedIndex = 0;
        }
    }

    private static ListBoxItem CreateListItem(ScheduleSelectionItem schedule)
    {
        StackPanel panel = new()
        {
            Margin = new Thickness(TrueBimTheme.Spacing8, TrueBimTheme.Spacing4, TrueBimTheme.Spacing8, TrueBimTheme.Spacing4)
        };
        panel.Children.Add(new TextBlock
        {
            Text = schedule.Name,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary
        });
        panel.Children.Add(new TextBlock
        {
            Text = schedule.Context,
            Foreground = TrueBimBrushes.TextSecondary,
            Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, 0)
        });

        return new ListBoxItem
        {
            Content = panel,
            Tag = schedule
        };
    }

    private void AcceptSelection()
    {
        if (scheduleList.SelectedItem is not ListBoxItem { Tag: ScheduleSelectionItem schedule })
        {
            return;
        }

        SelectedSchedule = schedule;
        DialogResult = true;
    }
}
