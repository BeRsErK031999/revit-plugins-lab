#if REVIT2024_OR_GREATER && NET48
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using TrueBIM.App.UI;
using Grid = System.Windows.Controls.Grid;
using static TrueBIM.App.Modules.SlabReinforcement.SlabWindow;

namespace TrueBIM.App.Modules.SlabReinforcement;

internal sealed class SlabOpeningWindow : TrueBimWindow
{
    internal OpeningSettings? Settings { get; private set; }

    internal SlabOpeningWindow(Floor floor, List<OpeningLayer>[] found)
    {
        Title = $"Обрамление отверстий · плита {floor.Id}";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel { Margin = new Thickness(16) };
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var a = Number("50"); var b = Number("100"); var cover = Number("30");
        var rs = Number("435");
        var rbt = Number(System.Text.RegularExpressions.Regex.IsMatch(floor.Name, @"(?i)(?:^|\W)[BВ]40(?:\W|$)") ? "1,40" : "");
        Row(body, "От грани отверстия до оси первого стержня a, мм", a);
        Row(body, "Между осями стержней b, мм", b);
        Row(body, "Защитный слой у торцов и отверстий, мм", cover);
        Row(body, "Rs арматуры, МПа", rs);
        Row(body, "Rbt бетона с учётом условий работы, МПа", rbt);
        body.Children.Add(new TextBlock { Text = "Фоновые стержни · диаметр и расстояние до оси от соответствующей грани плиты",
            FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,6) });
        var grid = new Grid(); body.Children.Add(grid);
        foreach (double width in new[] { 1.6, 1, 1 })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition());
        Cell(grid, new TextBlock { Text = "Слой" }, 0, 0);
        Cell(grid, new TextBlock { Text = "Ø, мм" }, 0, 1);
        Cell(grid, new TextBlock { Text = "До оси, мм" }, 0, 2);
        var layers = new Func<OpeningLayer>[4];
        string[] labels = { "Верх · буквы", "Верх · цифры", "Низ · буквы", "Низ · цифры" };
        for (int i = 0; i < found.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            Cell(grid, new TextBlock { Text = labels[i], VerticalAlignment = VerticalAlignment.Center }, i + 1, 0);
            if (found[i].Count > 1)
            {
                var select = new ComboBox { ItemsSource = found[i].Select(v => new { Layer = v, Name = $"Ø{v.Diameter:g} · до оси {v.Depth:g} мм" }), DisplayMemberPath = "Name", SelectedValuePath = "Layer" };
                Grid.SetColumnSpan(select, 2); Cell(grid, select, i + 1, 1);
                layers[i] = () => select.SelectedValue as OpeningLayer ?? throw new InvalidOperationException("Выберите фон для каждого слоя.");
                continue;
            }
            var value = found[i].SingleOrDefault();
            var dia = Number(value?.Diameter.ToString("g") ?? "");
            var depth = Number(value?.Depth.ToString("g") ?? "");
            dia.IsReadOnly = depth.IsReadOnly = value != null;
            Cell(grid, dia, i + 1, 1); Cell(grid, depth, i + 1, 2);
            layers[i] = () => new OpeningLayer(Read(dia), Read(depth));
        }
        if (found.Any(v => v.Count == 0)) body.Children.Add(new TextBlock { Text = "Для слоёв без найденного фона введите проектные значения.", TextWrapping = TextWrapping.Wrap });
        var error = new TextBlock { Foreground = System.Windows.Media.Brushes.DarkRed, TextWrapping = TextWrapping.Wrap };
        body.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,10,0,0) };
        body.Children.Add(actions);
        var ok = new Button { Content = "Обрамить отверстия", IsDefault = true, Padding = new Thickness(8,4,8,4) };
        actions.Children.Add(ok);
        actions.Children.Add(new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(8,0,0,0), Padding = new Thickness(8,4,8,4) });
        ok.Click += (_, _) =>
        {
            try
            {
                Settings = new OpeningSettings(Read(a), Read(b), Read(cover), Read(rs), Read(rbt), layers.Select(read => read()).ToArray());
                DialogResult = true;
            }
            catch (InvalidOperationException ex) { error.Text = ex.Message; }
        };
    }
}
#endif
