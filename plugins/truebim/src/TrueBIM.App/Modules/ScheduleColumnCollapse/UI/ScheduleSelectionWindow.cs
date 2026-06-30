using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Models;
using TrueBIM.App.UI;

namespace TrueBIM.App.Modules.ScheduleColumnCollapse.UI;

public sealed class ScheduleSelectionWindow : Window
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
            Margin = new Thickness(16)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock message = new()
        {
            Text = contextMessage,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(message, 0);
        root.Children.Add(message);

        searchBox.Height = 28;
        searchBox.Margin = new Thickness(0, 0, 0, 10);
        searchBox.ToolTip = "Фильтр по названию спецификации.";
        searchBox.TextChanged += (_, _) => RefreshList();
        Grid.SetRow(searchBox, 1);
        root.Children.Add(searchBox);

        scheduleList.BorderThickness = new Thickness(1);
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
            Margin = new Thickness(0, 12, 0, 0)
        };

        Button okButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Apply, "Выбрать"),
            MinWidth = 110,
            Height = 32,
            IsDefault = true
        };
        okButton.Click += (_, _) => AcceptSelection();
        footer.Children.Add(okButton);

        Button cancelButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Отмена"),
            MinWidth = 110,
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true
        };
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
            Margin = new Thickness(6)
        };
        panel.Children.Add(new TextBlock
        {
            Text = schedule.Name,
            FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = schedule.Context,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 3, 0, 0)
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
