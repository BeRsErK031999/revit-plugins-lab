using System.Windows;
using System.Windows.Controls;
using TrueBIM.App.Modules.RebarSplit.Models;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.RebarSplit.UI;

public sealed class RebarSplitWindow : TrueBimWindow
{
    private readonly RadioButton selected = new() { Content = "Выбранные элементы", IsChecked = true, Margin = new Thickness(0, 0, 0, 12) };
    private readonly CheckBox annotations = new() { Content = "Сохранять аннотации", IsChecked = true, Margin = new Thickness(0, 22, 0, 6) };

    public RebarSplitWindow(int selectedCount)
    {
        Title = "Разбить арматуру";
        Width = 590;
        Height = 360;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = IconFactory.CreateImage(TrueBimIcon.RebarSplit, 32);
        StackPanel body = new();
        body.Children.Add(selected);
        body.Children.Add(new RadioButton { Content = "Все наборы переменных стержней" });
        annotations.Style = TrueBimStyles.CreateCheckBoxStyle();
        body.Children.Add(annotations);
        body.Children.Add(new TextBlock
        {
            Text = "Попытаться перенести марки, размеры и мультиарматурные аннотации. Обработка может занять больше времени.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = TrueBimBrushes.TextSecondary
        });
        Button run = TrueBimUi.CreatePrimaryButton("Разбить", TrueBimIcon.RebarSplit, (_, _) => DialogResult = true);
        run.IsDefault = true;
        Button cancel = TrueBimUi.CreateSecondaryButton("Отмена", TrueBimIcon.Close);
        cancel.IsCancel = true;
        ApplyTrueBimShell(TrueBimUi.CreateHeader("Разбить арматуру", "Создать отдельный элемент для каждого стержня набора.", TrueBimIcon.RebarSplit),
            null, body, new TextBlock
            {
                Text = selectedCount > 0 ? $"Выбрано элементов: {selectedCount}. Одиночные стержни будут пропущены."
                    : "Для первого режима после нажатия «Разбить» выберите наборы в модели.",
                TextWrapping = TextWrapping.Wrap
            }, TrueBimUi.CreateFooter(null, run, cancel));
    }

    public RebarSplitOptions Options => new(selected.IsChecked == true ? RebarSplitScope.Selection : RebarSplitScope.VaryingSetsInProject,
        annotations.IsChecked == true);
}
