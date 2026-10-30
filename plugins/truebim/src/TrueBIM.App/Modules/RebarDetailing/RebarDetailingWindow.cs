using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;
using Autodesk.Revit.DB;
using Grid = System.Windows.Controls.Grid;
using Binding = System.Windows.Data.Binding;

namespace TrueBIM.App.Modules.RebarDetailing;

public sealed class RebarDetailingWindow : TrueBimWindow
{
    private readonly IReadOnlyList<DetailingViewRow> rows;
    private readonly CheckBox tags = Check("Марки арматуры"), concrete = Check("Размеры бетона"), bars = Check("Шаг и привязки арматуры"), spots = Check("Высотные отметки"), replace = Check("Заменить существующие размеры, отметки и марки арматуры");
    private readonly ComboBox tagType = Combo(), dimType = Combo(), spotType = Combo();
    private readonly ComboBox variant = new() { MinHeight = 28, ItemsSource = new[] { "Прямой", "Г-образный в одну сторону", "Г-образный в разные стороны" }, SelectedIndex = 0 };
    private readonly TextBox upperTop = new() { Text = "Плита" }, upperBottom = new() { Text = "перекрытия" },
        lowerTop = new() { Text = "Фундаментная плита" }, lowerBottom = new() { Text = "{том}" };
    private readonly CheckBox details = Check("Обрывы, швы и подписи плит"), quantities = Check("Записать количество в «Комментарии» арматуры");
    public RebarDetailingWindow(Document doc, IReadOnlyList<DetailingViewRow> rows, RebarDetailingService service)
    {
        this.rows = rows;
        Title = "Оформление вертикальных сечений"; Width = 1060; Height = 900; MinWidth = 850; MinHeight = 780;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Grid body = new(); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition());
        StackPanel settings = new();
        WrapPanel choices = new() { Margin = new Thickness(0, 0, 0, 8) };
        foreach (CheckBox option in new[] { tags, concrete, bars, spots, details }) choices.Children.Add(option);
        settings.Children.Add(choices);
        Add("Резервная марка арматуры", "tag", tagType); Add("Линейный размер", "dimension", dimType);
        Add("Резервная высотная отметка", "spot", spotType);
        AddSetting("Вариант вертикального сечения", variant);
        AddNoteSettings("Выноска сверху", upperTop, upperBottom);
        AddNoteSettings("Выноска снизу", lowerTop, lowerBottom);
        settings.Children.Add(new TextBlock { Text = "В каждой выноске: верхняя строка / нижняя строка. {том} — марка связанного тома; две пустые строки отключают выноску.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });
        settings.Children.Add(replace);
        settings.Children.Add(quantities);
        settings.Children.Add(new TextBlock { Text = "Марки подбираются по назначению стержней. Количество записывается только в пустые комментарии или вместо прежнего количества; текстовые комментарии сохраняются. Существующие ручные обрывы и подписи сохраняются.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 10) });
        body.Children.Add(settings);
        DataGrid table = new() { AutoGenerateColumns = false, ItemsSource = rows, CanUserAddRows = false, CanUserDeleteRows = false, Margin = new Thickness(0, 4, 0, 8) };
        table.Columns.Add(new DataGridCheckBoxColumn { Header = "✓", Binding = new Binding(nameof(DetailingViewRow.Selected)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 40 });
        AddColumn("Вид", nameof(DetailingViewRow.Name), 210); AddColumn("Тип", nameof(DetailingViewRow.Kind), 150);
        AddColumn("1:", nameof(DetailingViewRow.Scale), 45); AddColumn("Арматура", nameof(DetailingViewRow.Bars), 70);
        AddColumn("Бетон", nameof(DetailingViewRow.Hosts), 60); AddColumn("Аннотации", nameof(DetailingViewRow.Existing), 80);
        table.Columns.Add(new DataGridTextColumn { Header = "Проверка", Binding = new Binding(nameof(DetailingViewRow.Status)), IsReadOnly = true, MinWidth = 220, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Grid.SetRow(table, 1); body.Children.Add(table);
        Button run = TrueBimUi.CreatePrimaryButton("Оформить выбранные", TrueBimIcon.Apply, (_, _) =>
        {
            table.CommitEdit(); table.CommitEdit(DataGridEditingUnit.Row, true);
            if (!rows.Any(r => r.Selected)) { MessageBox.Show(this, "Отметьте хотя бы один вид.", Title); return; }
            if (!tags.IsChecked.GetValueOrDefault() && !concrete.IsChecked.GetValueOrDefault() && !bars.IsChecked.GetValueOrDefault() && !spots.IsChecked.GetValueOrDefault() && !details.IsChecked.GetValueOrDefault())
            { MessageBox.Show(this, "Включите хотя бы один вид оформления.", Title); return; }
            DialogResult = true;
        });
        Button cancel = TrueBimUi.CreateSecondaryButton("Отмена", TrueBimIcon.Close); cancel.IsCancel = true;
        ApplyTrueBimShell(TrueBimUi.CreateHeader(Title, "Готовые вертикальные сечения · стены, пилоны, колонны", TrueBimIcon.RebarSplit),
            null, body, new TextBlock { Text = "Типы подбираются по оформлению выбранных видов. При ошибке все изменения проблемного вида отменяются.", TextWrapping = TextWrapping.Wrap }, TrueBimUi.CreateFooter(null, run, cancel));
        void Add(string label, string kind, ComboBox combo)
        {
            var types = service.Types(doc, kind); combo.ItemsSource = types;
            combo.SelectedItem = types.FirstOrDefault(t => t.Id == service.PreferredType(rows.Select(r => r.View), types, kind));
            AddSetting(label, combo);
        }
        void AddSetting(string label, FrameworkElement control)
        {
            Grid line = new() { Margin = new Thickness(0, 2, 0, 2) }; line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) }); line.ColumnDefinitions.Add(new ColumnDefinition());
            line.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(control, 1); line.Children.Add(control); settings.Children.Add(line);
        }
        void AddNoteSettings(string label, TextBox top, TextBox bottom)
        {
            Grid fields = new(); fields.ColumnDefinitions.Add(new ColumnDefinition()); fields.ColumnDefinitions.Add(new ColumnDefinition());
            top.MinHeight = bottom.MinHeight = 28; top.Margin = new Thickness(0, 0, 8, 0);
            top.ToolTip = label + ": верхняя строка"; bottom.ToolTip = label + ": нижняя строка";
            System.Windows.Automation.AutomationProperties.SetName(top, label + ": верхняя строка");
            System.Windows.Automation.AutomationProperties.SetName(bottom, label + ": нижняя строка");
            fields.Children.Add(top); Grid.SetColumn(bottom, 1); fields.Children.Add(bottom); AddSetting(label, fields);
        }
        void AddColumn(string title, string path, double width) => table.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path), Width = width, IsReadOnly = true });
    }
    public IReadOnlyList<View> SelectedViews => rows.Where(r => r.Selected).Select(r => r.View).ToList();
    public DetailingOptions Options => new() { Tags = tags.IsChecked == true, ConcreteDimensions = concrete.IsChecked == true, BarDimensions = bars.IsChecked == true, Elevations = spots.IsChecked == true,
        ReplaceExisting = replace.IsChecked == true, Details = details.IsChecked == true, WriteQuantities = quantities.IsChecked == true,
        Variant = (DetailingVariant)variant.SelectedIndex, UpperNoteTop = upperTop.Text, UpperNoteBottom = upperBottom.Text,
        LowerNoteTop = lowerTop.Text, LowerNoteBottom = lowerBottom.Text,
        TagTypeId = Id(tagType), DimensionTypeId = Id(dimType), SpotTypeId = Id(spotType) };
    private static ElementId Id(ComboBox box) => (box.SelectedItem as DetailingType)?.Id ?? ElementId.InvalidElementId;
    private static ComboBox Combo() => new() { DisplayMemberPath = nameof(DetailingType.Name), MinHeight = 28, IsTextSearchEnabled = true };
    private static CheckBox Check(string text) => new() { Content = text, IsChecked = true, Margin = new Thickness(0, 4, 16, 4), Style = TrueBimStyles.CreateCheckBoxStyle() };
}
