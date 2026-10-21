using System.Windows;
using System.Windows.Controls;
using TrueBIM.App.UI;

namespace TrueBIM.App.Modules.FinishSchedule.UI;

public sealed class FinishMissingDescriptionWindow : TrueBimWindow
{
    public FinishMissingDescriptionWindow(string parameterName, IReadOnlyList<string> types)
    {
        Title = "Не заполнено описание отделки";
        Width = 780;
        Height = 480;
        MinWidth = 550;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        DockPanel panel = new() { Margin = new Thickness(16) };
        TextBlock message = new()
        {
            Text = $"У размещённой в модели отделки не заполнен параметр типа «{parameterName}».\n"
                + "Текст списка можно выделить мышью и скопировать. Продолжить с отметкой [Описание не задано]?",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(message, Dock.Top);
        panel.Children.Add(message);
        StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button cancel = new() { Content = "Отмена", IsCancel = true, MinWidth = 110, Margin = new Thickness(8) };
        Button proceed = new() { Content = "Продолжить", MinWidth = 130, Margin = new Thickness(8) };
        proceed.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(proceed);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(new TextBox
        {
            Text = string.Join(Environment.NewLine, types), IsReadOnly = true, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        Content = panel;
    }
}
