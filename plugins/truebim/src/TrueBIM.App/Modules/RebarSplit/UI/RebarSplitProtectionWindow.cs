using System.Windows;
using System.Windows.Controls;
using TrueBIM.App.Modules.RebarSplit.Models;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.RebarSplit.UI;

public sealed class RebarSplitProtectionWindow : TrueBimWindow
{
    public RebarSplitProtectionWindow(IReadOnlyList<RebarSplitProtectionIssue> issues, bool groupsOnly = false)
    {
        Title = groupsOnly ? "Разбить арматуру — группы" : "Разбить арматуру — закрепление";
        Width = 760;
        Height = groupsOnly ? 700 : 690;
        MinWidth = 620;
        MinHeight = 590;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = IconFactory.CreateImage(TrueBimIcon.RebarSplit, 32);
        StackPanel body = new();
        body.Children.Add(new TextBlock
        {
            Text = (groupsOnly ? "Найдены наборы внутри групп. " : "Найдены закреплённые наборы. ")
                + "Выбранное действие применяется ко всем перечисленным наборам.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        body.Children.Add(new TextBox
        {
            Text = string.Join(Environment.NewLine, issues.Select(issue => $"ID {issue.SourceId}: "
                + string.Join("; ", new[]
                {
                    issue.Pinned ? "закреплён" : null,
                    issue.GroupId.HasValue ? $"группа «{issue.GroupName}» (ID {issue.GroupId})" : null
                }.Where(text => text is not null)))),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 130,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 6)
        });
        body.Children.Add(new TextBlock
        {
            Text = "Текст можно выделить и скопировать: Ctrl+A, Ctrl+C.",
            Foreground = TrueBimBrushes.TextSecondary,
            Margin = new Thickness(0, 0, 0, 16)
        });
        if (groupsOnly)
            body.Children.Add(new TextBlock
            {
                Text = "Соответствующие наборы обрабатываются во всех экземплярах типа группы, включая невыбранные. "
                    + "Все их ID перечислены выше. При извлечении затронутые экземпляры и необходимые родительские группы "
                    + "будут разгруппированы. Остальные элементы сохранят свои ID, положение и свойства, но выйдут из этих групп. "
                    + "Новые группы не создаются. Для закреплённых наборов действие выбирается отдельно. "
                    + "Опция «Сохранять аннотации» переносит поддерживаемые марки и размеры. "
                    + "Присоединённые группы узлов и другое оформление могут быть потеряны; замечания будут перечислены в результате.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = TrueBimBrushes.TextSecondary,
                Margin = new Thickness(0, 0, 0, 12)
            });
        if (groupsOnly)
        {
            AddAction(body, "Извлечь из группы и разбить", "Разгруппировать затронутые группы и разбить перечисленные наборы.",
                () => GroupAction = RebarSplitGroupAction.Extract);
            AddAction(body, "Пропустить обработку элементов", "Сохранить перечисленные наборы и их группы без изменений; обработать остальные.",
                () => GroupAction = RebarSplitGroupAction.Skip);
        }
        else
        {
            AddAction(body, "Пропустить проблемные элементы", "Оставить закреплённые наборы без изменений и разбить остальные.",
                () => Action = RebarSplitProtectionAction.Skip);
            AddAction(body, "Обработать с сохранением закрепления", "Все новые стержни останутся закреплёнными.",
                () => Action = RebarSplitProtectionAction.PreserveStatus);
            AddAction(body, "Обработать без сохранения закрепления", "Новые стержни будут откреплены.",
                () => Action = RebarSplitProtectionAction.RemoveStatus);
        }
        Button cancel = TrueBimUi.CreateSecondaryButton("Отменить всю операцию", TrueBimIcon.Close);
        cancel.IsCancel = true;
        cancel.IsDefault = true;
        ApplyTrueBimShell(TrueBimUi.CreateHeader(groupsOnly ? "Наборы находятся в группе" : "Наборы закреплены",
                $"Проблемных наборов: {issues.Count}.", TrueBimIcon.RebarSplit),
            null, new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            new TextBlock { Text = "Закрытие окна или Esc полностью отменяет операцию.", TextWrapping = TextWrapping.Wrap },
            TrueBimUi.CreateFooter(null, cancel));
    }

    public RebarSplitProtectionAction Action { get; private set; } = RebarSplitProtectionAction.Cancel;
    public RebarSplitGroupAction GroupAction { get; private set; } = RebarSplitGroupAction.Cancel;

    private void AddAction(Panel panel, string title, string description, Action select)
    {
        StackPanel content = new();
        content.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap,
            Foreground = TrueBimBrushes.TextSecondary, Margin = new Thickness(0, 3, 0, 0) });
        Button button = new() { Content = content, HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 8) };
        button.Click += (_, _) => { select(); DialogResult = true; };
        panel.Children.Add(button);
    }
}
