using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TrueBIM.App.Modules.BimTools.FamilyReplacement.Models;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.BimTools.FamilyReplacement.UI;

public sealed class FamilyReplacementReportWindow : TrueBimWindow
{
    public FamilyReplacementReportWindow(FamilyReplacementResult result)
    {
        Title = "Замена семейств — результат";
        Icon = IconFactory.CreateImage(TrueBimIcon.FamilyManager, 32);
        Width = 1000;
        Height = 620;
        MinWidth = 740;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        DataGrid table = new()
        {
            AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
            CanUserDeleteRows = false, Style = TrueBimStyles.CreateDataGridStyle(),
            ItemsSource = result.Items.Select(item => new
            {
                item.SourceId, item.NewId,
                Status = item.Replaced ? "Заменён" : "Пропущен", item.Message
            }).ToList()
        };
        table.Columns.Add(new DataGridTextColumn { Header = "Исходный ID", Binding = new Binding("SourceId"), Width = 105 });
        table.Columns.Add(new DataGridTextColumn { Header = "Новый ID", Binding = new Binding("NewId"), Width = 100 });
        table.Columns.Add(new DataGridTextColumn { Header = "Результат", Binding = new Binding("Status"), Width = 100 });
        Style wrapping = new(typeof(TextBlock));
        wrapping.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        table.Columns.Add(new DataGridTextColumn
        {
            Header = "Подробности", Binding = new Binding("Message"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = wrapping
        });
        Button close = TrueBimUi.CreatePrimaryButton("Закрыть", TrueBimIcon.Close, (_, _) => DialogResult = true);
        close.IsCancel = true;
        ApplyTrueBimShell(
            TrueBimUi.CreateHeader("Результат замены", result.Summary, TrueBimIcon.FamilyManager),
            null, table,
            new TextBlock
            {
                Text = "Пропущенные экземпляры сохранены. Отчёт можно выделить и скопировать сочетанием Ctrl+C.",
                Foreground = TrueBimBrushes.TextSecondary, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            },
            TrueBimUi.CreateFooter(null, close));
    }
}
