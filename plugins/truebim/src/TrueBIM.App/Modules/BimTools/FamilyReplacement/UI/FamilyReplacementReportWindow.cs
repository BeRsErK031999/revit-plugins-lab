using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Globalization;
using System.Runtime.InteropServices;
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
        ShowInTaskbar = false;
        DataGrid table = new()
        {
            AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
            CanUserDeleteRows = false, Style = TrueBimStyles.CreateDataGridStyle(),
            SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
            SelectionMode = DataGridSelectionMode.Extended,
            ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader,
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
        TextBlock hint = new()
        {
            Text = "Окно можно оставить открытым и работать в Revit. Выделите ячейку ID и нажмите Ctrl+C; поиск в Revit — «Выбрать по коду». Для нескольких ячеек используйте Ctrl или Shift.",
            Foreground = TrueBimBrushes.TextSecondary, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        };
        void CopyText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            try { Clipboard.SetText(text); }
            catch (ExternalException) { hint.Text = "Буфер обмена занят другим приложением. Повторите копирование."; }
        }
        Button copyId = TrueBimUi.CreateSecondaryButton("Копировать ID", TrueBimIcon.Apply, (_, _) =>
        {
            DataGridCellInfo cell = table.CurrentCell;
            string? property = ((cell.Column as DataGridBoundColumn)?.Binding as Binding)?.Path.Path;
            if (!cell.IsValid || property is not ("SourceId" or "NewId"))
            {
                hint.Text = "Выберите ячейку в столбце «Исходный ID» или «Новый ID».";
                return;
            }
            CopyText(Convert.ToString(cell.Item.GetType().GetProperty(property)?.GetValue(cell.Item), CultureInfo.InvariantCulture) ?? string.Empty);
        });
        Button copySkipped = TrueBimUi.CreateSecondaryButton("ID всех пропущенных", TrueBimIcon.Apply, (_, _) =>
            CopyText(string.Join("; ", result.Items.Where(item => !item.Replaced).Select(item => item.SourceId))));
        copySkipped.IsEnabled = result.Skipped > 0;
        Button close = TrueBimUi.CreatePrimaryButton("Закрыть", TrueBimIcon.Close, (_, _) => Close());
        PreviewKeyDown += (_, args) => { if (args.Key == System.Windows.Input.Key.Escape) Close(); };
        close.IsCancel = true;
        ApplyTrueBimShell(
            TrueBimUi.CreateHeader("Результат замены", result.Summary, TrueBimIcon.FamilyManager),
            null, table,
            hint,
            TrueBimUi.CreateFooter(null, copyId, copySkipped, close));
    }
}
