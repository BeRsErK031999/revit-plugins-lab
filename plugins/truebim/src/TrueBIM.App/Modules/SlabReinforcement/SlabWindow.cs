#if REVIT2024_OR_GREATER && NET48
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using TrueBIM.App.UI;
using ComboBox = System.Windows.Controls.ComboBox;
using Control = System.Windows.Controls.Control;
using Grid = System.Windows.Controls.Grid;

namespace TrueBIM.App.Modules.SlabReinforcement;

internal sealed class SlabWindow : TrueBimWindow
{
    private sealed record BarChoice(RebarBarType Type)
    {
        public override string ToString() => Type.Name;
    }

    private sealed record FrameChoice(FamilySymbol Type)
    {
        public override string ToString() => Type.FamilyName + " · " + Type.Name;
    }

    internal SlabOptions? Settings { get; private set; }

    private readonly StackPanel body = new();
    private readonly ComboBox stirrup = new();
    private readonly ComboBox[] bars = new ComboBox[4];
    private readonly TextBox[] depths = new TextBox[4];
    private readonly TextBox step = Number("200"), cover = Number("30"), manual = Number("650"), rbt = Number(""), rs = Number("435"), stepX = Number("1200"), stepY = Number("1200"), mark = Number("");
    private readonly ComboBox frame = new()
    {
        MinWidth = 250
    };
    private readonly CheckBox auto = new()
    {
        Content = "Рассчитать длину ветви по СП 63",
        IsChecked = true
    };
    private readonly CheckBox frames = new()
    {
        Content = "Расставить поддерживающие каркасы"
    };
    private readonly CheckBox replaceFrames = new()
    {
        Content = "Заменить ранее расставленные каркасы"
    };
    private readonly CheckBox removeManual = new();
    private readonly TextBlock error = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = System.Windows.Media.Brushes.DarkRed
    };
    internal SlabWindow(Floor floor, int manualCount, int oldFrames)
    {
        Title = "Армирование плиты";
        Width = 675;
        Height = 755;
        MinHeight = 490;
        MaxHeight = SystemParameters.WorkArea.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel
        {
            Margin = new Thickness(16)
        };
        var footer = new StackPanel();
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var ok = new Button
        {
            Content = "Армировать",
            MinWidth = 108,
            Padding = new Thickness(8, 4, 8, 4),
            IsDefault = true
        };
        var cancel = new Button
        {
            Content = "Отмена",
            MinWidth = 80,
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true
        };
        ok.Click += (_, _) => Accept();
        actions.Children.Add(ok);
        actions.Children.Add(cancel);
        footer.Children.Add(error);
        footer.Children.Add(actions);
        DockPanel.SetDock(footer, Dock.Bottom);
        panel.Children.Add(footer);
        panel.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = panel;
        Heading($"Плита {floor.Id} · {floor.Name}");
        var types = new FilteredElementCollector(floor.Document).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().OrderBy(t => t.Name).Select(t => new BarChoice(t)).ToList();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });
        string[] labels =
        {
            "Верх · буквы",
            "Верх · цифры",
            "Низ · буквы",
            "Низ · цифры"
        };
        string[] hints =
        {
            "Верх. Букв.",
            "Верх. Цифр.",
            "Низ. Букв.",
            "Низ. Цифр."
        };
        grid.RowDefinitions.Add(new RowDefinition());
        Cell(grid, new TextBlock { Text = "Слой" }, 0, 0);
        Cell(grid, new TextBlock { Text = "Тип стержня" }, 0, 1);
        Cell(grid, new TextBlock { Text = "До оси, мм" }, 0, 2);
        for (int i = 0; i < 4; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            bars[i] = new ComboBox
            {
                ItemsSource = types,
                Margin = new Thickness(0, 2, 8, 2),
                MinWidth = 200
            };
            bars[i].SelectedItem = types.FirstOrDefault(b => b.Type.Name.Contains(hints[i]) && Math.Abs(b.Type.BarNominalDiameter / SlabGeometry.Mm - 10) < .1 && b.Type.Name.Contains("п.м.")) ?? types.FirstOrDefault();
            depths[i] = Number(i == 0 || i == 3 ? "45" : "35");
            Cell(grid, new TextBlock { Text = labels[i], VerticalAlignment = VerticalAlignment.Center }, i + 1, 0);
            Cell(grid, bars[i], i + 1, 1);
            Cell(grid, depths[i], i + 1, 2);
        }

        grid.RowDefinitions.Add(new RowDefinition());
        stirrup.ItemsSource = types;
        stirrup.SelectedItem = types.FirstOrDefault(b => OpeningBarTypes.IsBend(b.Type.Name) &&
            Math.Abs(b.Type.BarNominalDiameter / SlabGeometry.Mm - 10) < 0.1) ?? bars[0].SelectedItem;
        stirrup.Margin = new Thickness(0, 2, 8, 2);
        Cell(grid, new TextBlock { Text = "Хомуты Х1", VerticalAlignment = VerticalAlignment.Center }, 5, 0);
        Cell(grid, stirrup, 5, 1);
        body.Children.Add(grid);
        Note("После нажатия «Армировать» выделите грань плиты вдоль буквенных осей.");
        Row(body, "Шаг для всех четырёх слоёв, мм", step);
        Row(body, "Защитный слой у торцов и отверстий, мм", cover);
        Note("Боковой отступ до оси крайнего ряда — 50 мм.");
        Heading("Торцевые стержни · форма 200");
        body.Children.Add(auto);
        bool sampleConcrete = System.Text.RegularExpressions.Regex.IsMatch(floor.Name, @"(?i)(?:^|\W)[BВ]40(?:\W|$)");
        if (sampleConcrete)
            rbt.Text = "1,40";
        var calc = new StackPanel();
        Row(calc, "Rbt бетона с учётом условий работы, МПа", rbt);
        Row(calc, "Rs выбранной арматуры, МПа", rs);
        body.Children.Add(calc);
        Note("Для автоматического режима приняты периодический профиль, η₁ = 2,5, α₂ = 2,0 и отношение площадей 1,0.");
        var length = new StackPanel();
        Row(length, "Длина одной ветви вручную, мм", manual);
        body.Children.Add(length);
        Toggle(auto, calc, length);
        Heading("Каркасы");
        body.Children.Add(frames);
        var framePanel = new StackPanel();
        var symbols = new FilteredElementCollector(floor.Document).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_GenericModel).Cast<FamilySymbol>().Where(s => s.Family.FamilyPlacementType == FamilyPlacementType.WorkPlaneBased).OrderBy(s => s.FamilyName).ThenBy(s => s.Name).Select(s => new FrameChoice(s)).ToList();
        frame.ItemsSource = symbols;
        frame.SelectedItem = symbols.FirstOrDefault(s => s.Type.FamilyName == "(Сборка) Каркас • 303" && s.Type.Name == "Крф-2");
        if (frame.SelectedItem is FrameChoice selected)
            mark.Text = selected.Type.Name;
        Row(framePanel, "Семейство и тип", frame);
        Row(framePanel, "Марка экземпляра", mark);
        Row(framePanel, "Шаг по буквенным осям, мм", stepX);
        Row(framePanel, "Шаг по цифровым осям, мм", stepY);
        body.Children.Add(framePanel);
        Toggle(frames, framePanel);
        if (oldFrames > 0)
        {
            replaceFrames.Content = $"Удалить прежние каркасы ({oldFrames}). При сохранении новые не добавляются.";
            body.Children.Add(replaceFrames);
        }

        if (manualCount > 0)
        {
            removeManual.Content = $"Удалить ручное фоновое армирование этой плиты ({manualCount} эл.)";
            body.Children.Add(removeManual);
            Note("Отбор по двум словам в параметре типа «• Фильтр» — «Перекрытия» и «Фоновая».");
        }
    }

    private static void Toggle(CheckBox check, UIElement yes, UIElement? no = null)
    {
        void Update()
        {
            bool on = check.IsChecked == true;
            yes.Visibility = on ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            if (no != null)
                no.Visibility = on ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        }

        Update();
        check.Checked += (_, _) => Update();
        check.Unchecked += (_, _) => Update();
    }

    internal static TextBox Number(string value) => new()
    {
        Text = value,
        MinWidth = 80,
        Margin = new Thickness(0, 2, 2, 2)
    };
    private void Heading(string s) => body.Children.Add(new TextBlock { Text = s, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 9, 0, 4) });
    private void Note(string s) => body.Children.Add(new TextBlock { Text = s, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 6) });
    internal static void Cell(Grid grid, UIElement item, int row, int col)
    {
        Grid.SetRow(item, row);
        Grid.SetColumn(item, col);
        grid.Children.Add(item);
    }

    internal static void Row(StackPanel panel, string title, Control field)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Cell(g, new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        Cell(g, field, 0, 1);
        panel.Children.Add(g);
    }

    internal static double Read(TextBox field)
    {
        if (!double.TryParse(field.Text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) || v <= 0 || double.IsInfinity(v))
            throw new InvalidOperationException("Введите положительное числовое значение.");
        return v;
    }

    private void Accept()
    {
        try
        {
            bool calc = auto.IsChecked == true, addFrames = frames.IsChecked == true;
            var options = new SlabOptions(bars.Select(b => (b.SelectedItem as BarChoice)?.Type ?? throw new InvalidOperationException("Выберите четыре типа арматуры.")).ToArray(), depths.Select(Read).ToArray(), Read(step), Read(cover), calc ? null : Read(manual), calc ? Read(rbt) : 0, calc ? Read(rs) : 0)
            {
                StirrupBar = (stirrup.SelectedItem as BarChoice)?.Type ?? throw new InvalidOperationException("Выберите типоразмер хомутов Х1."),
                ReplaceManual = removeManual.IsChecked == true,
                ReplaceFrames = replaceFrames.IsChecked == true,
                PlaceFrames = addFrames,
                FrameType = (frame.SelectedItem as FrameChoice)?.Type,
                FrameX = addFrames ? Read(stepX) : 0,
                FrameY = addFrames ? Read(stepY) : 0,
                Mark = mark.Text.Trim()
            };
            Settings = options;
            DialogResult = true;
        }
        catch (InvalidOperationException ex)
        {
            error.Text = ex.Message;
        }
    }
}
#endif
