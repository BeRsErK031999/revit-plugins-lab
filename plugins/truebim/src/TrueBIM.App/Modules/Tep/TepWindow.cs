using System.Globalization;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using TrueBIM.App.UI;

namespace TrueBIM.App.Modules.Tep;

public sealed class TepReviewProfile
{
    public string Project { get; set; } = "";
    public TepOptions Options { get; set; } = new();
    public List<TepItem> Items { get; set; } = [];
}

public sealed class TepWindow : TrueBimWindow
{
    private readonly Func<TepSourceSelection, TepSnapshot> _collect;
    private readonly Dictionary<string, ComboBox> _documents = [];
    private readonly TextBox _building = Input("ТЭП • Строительный объем");
    private readonly TextBox _footprint = Input("ТЭП • Площадь застройки вся");
    private readonly TextBox _section = Input("ТЭП • Площадь застройки отм. 4,500");
    private readonly TextBox _template = Input("");
    private readonly TextBox _residents = Input("40");
    private readonly TextBox _employees = Input("6");
    private readonly TextBox _basis = Input(new TepOptions().PopulationBasis);
    private readonly CheckBox _populationConfirmed = new() { Content = "Основание расчёта людей проверено для объекта в Бийске", Margin = new Thickness(8) };
    private readonly CheckBox _reviewed = new() { Content = "Классификация, состав источников и этажей проверены", Margin = new Thickness(8) };
    private readonly TextBlock _status = new() { Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _issues = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly DataGrid _rooms = Grid();
    private readonly DataGrid _areas = Grid();
    private readonly DataGrid _results = Grid();
    private readonly TabControl _tabs = new();
    private TepSnapshot? _snapshot;
    private TepCalculation? _calculation;

    public TepWindow(IReadOnlyList<TepDocumentSource> documents, Func<TepSourceSelection, TepSnapshot> collect)
    {
        _collect = collect;
        Title = "TrueBIM · Проверка ТЭП";
        Width = 1320; Height = 850; MinWidth = 1000; MinHeight = 650;
        StackPanel settings = new() { Margin = new Thickness(12) };
        settings.Children.Add(Label("Источники: помещения Б1 и Б2 берутся только по своей секции; помещения стилобата — из его файла. Зоны — из одного документа."));
        foreach (string part in new[] { TepParts.B1, TepParts.Stylobate, TepParts.B2, "Зоны" })
        {
            ComboBox box = new() { ItemsSource = documents, DisplayMemberPath = "Name", MinWidth = 520, Margin = new Thickness(4) };
            string marker = part == TepParts.Stylobate ? "СТЛБ" : part == "Зоны" ? "Б2" : part;
            box.SelectedItem = documents.FirstOrDefault(d => d.Name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
            box.SelectionChanged += (_, _) => InvalidateCollection();
            _documents[part] = box;
            settings.Children.Add(Field(part, box));
        }
        settings.Children.Add(Field("Зоны площади и объёма", _building));
        settings.Children.Add(Field("Площадь застройки", _footprint));
        settings.Children.Add(Field("Сечение +4,500", _section));
        foreach (TextBox text in new[] { _building, _footprint, _section }) text.TextChanged += (_, _) => InvalidateCollection();
        settings.Children.Add(Label("Введите точные имена спецификаций зон. Читается последняя стадия каждого документа; исключённые строки остаются видны. Повторяющиеся зоны других файлов не складываются."));
        settings.Children.Add(Button("Прочитать модели", ReadModels));
        settings.Children.Add(Field("Исходная таблица Excel", _template));
        settings.Children.Add(Button("Выбрать ТЭП.xlsx", () =>
        {
            OpenFileDialog dialog = new() { Filter = "Excel (*.xlsx)|*.xlsx", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) _template.Text = dialog.FileName;
        }));
        settings.Children.Add(Field("Жильцы: м² квартиры / чел.", _residents));
        settings.Children.Add(Field("Сотрудники: м² рабочих помещений / чел.", _employees));
        _basis.AcceptsReturn = true; _basis.Height = 70; _basis.TextWrapping = TextWrapping.Wrap;
        settings.Children.Add(Field("Основание расчёта людей", _basis));
        settings.Children.Add(_populationConfirmed);
        settings.Children.Add(Label("40 и 6 перенесены из исходного Excel. До подтверждения применимого основания расчёт людей недоступен. «Премиум» автоматически не приравнивается к нормативному классу жилья."));
        foreach (TextBox text in new[] { _basis, _residents, _employees })
            text.TextChanged += (_, _) => _populationConfirmed.IsChecked = false;
        _tabs.Items.Add(new TabItem { Header = "Настройки", Content = new ScrollViewer { Content = settings, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        ConfigureItems(_rooms, false); ConfigureItems(_areas, true);
        _tabs.Items.Add(new TabItem { Header = "Помещения и машиноместа", Content = Searchable(_rooms) });
        _tabs.Items.Add(new TabItem { Header = "Зоны и этажи", Content = Searchable(_areas) });
        _results.IsReadOnly = true;
        foreach (var column in new[] { ("Показатель", "Name", 370), ("Ед.", "Unit", 55), ("Б1", "B1", 110), ("Стилобат", "Stylobate", 110), ("Б2", "B2", 110), ("Итог", "Total", 130), ("Пояснения", "Notes", 550) })
            TextColumn(_results, column.Item1, column.Item2, column.Item3, true);
        foreach (DataGridTextColumn column in _results.Columns.OfType<DataGridTextColumn>().Where(c => Equals(c.Header, "Показатель") || Equals(c.Header, "Пояснения")))
            column.ElementStyle = new Style(typeof(TextBlock))
            {
                Setters = { new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap), new Setter(FrameworkElement.MarginProperty, new Thickness(4, 2, 4, 2)) }
            };
        _tabs.Items.Add(new TabItem { Header = "Результат", Content = _results });
        _tabs.Items.Add(new TabItem { Header = "Диагностика", Content = _issues });
        StackPanel commands = new() { Orientation = Orientation.Horizontal };
        commands.Children.Add(Button("Рассчитать", Recalculate));
        commands.Children.Add(Button("Экспорт Excel", Export));
        commands.Children.Add(Button("Сохранить профиль", SaveProfile));
        commands.Children.Add(Button("Загрузить профиль", LoadProfile));
        StackPanel bottom = new();
        bottom.Children.Add(_reviewed); bottom.Children.Add(_status);
        ApplyTrueBimShell(Label("Проверка ТЭП · площади, объёмы, квартиры и помещения"), commands, _tabs, bottom);
        _status.Text = "Выберите документы и прочитайте модели. Все изменения классификации действуют только в расчёте.";
    }

    private void InvalidateCollection()
    {
        _snapshot = null; _calculation = null;
        _rooms.ItemsSource = null; _areas.ItemsSource = null; _results.ItemsSource = null;
        _reviewed.IsChecked = false;
        _status.Text = "Источники изменены. Нажмите «Прочитать модели».";
    }

    private void ReadModels() => Guard(() =>
    {
        TepDocumentSource? Doc(string part) => _documents[part].SelectedItem as TepDocumentSource;
        _snapshot = _collect(new TepSourceSelection
        {
            B1 = Doc(TepParts.B1), B2 = Doc(TepParts.B2), Stylobate = Doc(TepParts.Stylobate), Areas = Doc("Зоны"),
            BuildingSchedule = _building.Text.Trim(), FootprintSchedule = _footprint.Text.Trim(), SectionSchedule = _section.Text.Trim()
        });
        _reviewed.IsChecked = false;
        BindItems();
        Calculate();
        _tabs.SelectedIndex = 1;
    });

    private void BindItems()
    {
        _rooms.ItemsSource = _snapshot!.Items.Where(i => i.Kind != "Зона").ToList();
        _areas.ItemsSource = _snapshot.Items.Where(i => i.Kind == "Зона").ToList();
    }

    private TepOptions Options()
    {
        double Parse(TextBox box) => double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value : throw new InvalidOperationException("Удельная площадь должна быть числом.");
        return new TepOptions
        {
            ResidentsAreaPerPerson = Parse(_residents), EmployeesAreaPerPerson = Parse(_employees),
            PopulationBasis = _basis.Text, PopulationBasisConfirmed = _populationConfirmed.IsChecked == true,
            ClassificationReviewed = _reviewed.IsChecked == true
        };
    }

    private void Calculate()
    {
        if (_snapshot is null) throw new InvalidOperationException("Сначала прочитайте модели.");
        foreach (DataGrid grid in new[] { _rooms, _areas })
            if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true))
                throw new InvalidOperationException("Исправьте значение в редактируемой ячейке.");
        _calculation = new TepCalculator().Calculate(_snapshot, Options());
        _results.ItemsSource = _calculation.Rows;
        _issues.Text = string.Join(Environment.NewLine + Environment.NewLine, _calculation.Issues);
        _status.Text = (_reviewed.IsChecked == true ? "Классификация подтверждена." : "ПРЕДВАРИТЕЛЬНО: проверьте классификацию.") +
            " Выбрано источников: " + _snapshot.Items.Count(i => i.Included && i.Role != TepRoles.Ignore) +
            ". Замечаний: " + _calculation.Issues.Count + ". Ячеек без данных: " + _calculation.Rows.Sum(r => r.Values.Values.Count(v => v.Status == "Нет данных")) +
            ". Неполных результатов: " + _calculation.Rows.Sum(r => r.Values.Values.Count(v => v.IsPartial && v.Number.HasValue)) +
            ". * — неполный расчёт: помещения с нулевой площадью пропущены; см. пояснения и диагностику.";
    }
    private void Recalculate() => Guard(() => { Calculate(); _tabs.SelectedIndex = 3; });

    private void Export() => Guard(() =>
    {
        Calculate();
        if (!File.Exists(_template.Text)) throw new InvalidOperationException("Выберите исходный Excel во вкладке «Настройки».");
        SaveFileDialog dialog = new() { Filter = "Excel (*.xlsx)|*.xlsx", FileName = "ТЭП_проверка_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx" };
        if (dialog.ShowDialog(this) != true) return;
        new TepExcel().Export(_template.Text, dialog.FileName, _snapshot!, Options(), _calculation!);
        _status.Text = "Сохранено: " + dialog.FileName + ". Исходная таблица, расчёт, сверка, диагностика и источники находятся в одном файле.";
    });

    private void SaveProfile() => Guard(() =>
    {
        Calculate();
        SaveFileDialog dialog = new() { Filter = "Профиль ТЭП (*.json)|*.json", FileName = "ТЭП_профиль.json" };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(new TepReviewProfile { Project = _snapshot!.Project, Options = Options(), Items = _snapshot.Items },
            new JsonSerializerOptions { WriteIndented = true }));
        _status.Text = "Профиль сохранён: " + dialog.FileName;
    });

    private void LoadProfile() => Guard(() =>
    {
        if (_snapshot is null) throw new InvalidOperationException("Сначала прочитайте модели.");
        OpenFileDialog dialog = new() { Filter = "Профиль ТЭП (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        TepReviewProfile profile = JsonSerializer.Deserialize<TepReviewProfile>(File.ReadAllText(dialog.FileName))
            ?? throw new InvalidDataException("Пустой профиль.");
        if (profile.Project != _snapshot.Project) throw new InvalidDataException("Профиль относится к другому проекту.");
        Dictionary<string, TepItem> saved = profile.Items.ToDictionary(i => i.Key);
        int matched = 0;
        foreach (TepItem item in _snapshot.Items)
        {
            if (!saved.TryGetValue(item.Key, out TepItem? old)) continue;
            matched++;
            item.Included = old.Included; item.Part = old.Part; item.Role = old.Role;
            item.Living = old.Living; item.Cold = old.Cold; item.Coefficient = old.Coefficient;
            item.Group = old.Group; item.RoomsInApartment = old.RoomsInApartment;
            item.UsefulOffice = old.UsefulOffice; item.CalculatedOffice = old.CalculatedOffice;
            item.EmployeeArea = old.EmployeeArea;
            item.RoomTypeKnown = old.RoomTypeKnown;
            item.IncludeVolume = old.IncludeVolume; item.CountAsFloor = old.CountAsFloor; item.CountAsStorey = old.CountAsStorey;
            // Geometry and validation errors always come from the current model.
        }
        _residents.Text = profile.Options.ResidentsAreaPerPerson.ToString(CultureInfo.CurrentCulture);
        _employees.Text = profile.Options.EmployeesAreaPerPerson.ToString(CultureInfo.CurrentCulture);
        _basis.Text = profile.Options.PopulationBasis;
        _populationConfirmed.IsChecked = false; _reviewed.IsChecked = false;
        BindItems(); Calculate();
        _status.Text = "Сопоставлено " + matched + " из " + _snapshot.Items.Count + ". Площади и высоты прочитаны заново; подтвердите классификацию и основание.";
    });

    private void ConfigureItems(DataGrid grid, bool areas)
    {
        CheckColumn(grid, "Учёт", "Included");
        grid.Columns.Add(new DataGridComboBoxColumn { Header = "Часть", ItemsSource = TepParts.All, SelectedItemBinding = Binding("Part"), Width = 110 });
        grid.Columns.Add(new DataGridComboBoxColumn { Header = "Категория", ItemsSource = TepRoles.All, SelectedItemBinding = Binding("Role"), Width = 210 });
        TextColumn(grid, "Имя", "Name", 210, true);
        TextColumn(grid, "ID", "ElementId", 85, true);
        TextColumn(grid, "Площадь, м²", "Area", 100, true, "0.000");
        TextColumn(grid, "Уровень", "Level", 160, true);
        if (areas)
        {
            TextColumn(grid, "Высота, м", "Height", 85, true, "0.000");
            CheckColumn(grid, "Объём", "IncludeVolume");
            CheckColumn(grid, "Этажность", "CountAsStorey");
            CheckColumn(grid, "Кол-во этажей", "CountAsFloor");
            TextColumn(grid, "Схема", "Scheme", 170, true);
            TextColumn(grid, "Спецификации", "Schedules", 290, true);
        }
        else
        {
            TextColumn(grid, "Группа / квартира", "Group", 140);
            TextColumn(grid, "Комнат", "RoomsInApartment", 65);
            CheckColumn(grid, "Жилое", "Living"); CheckColumn(grid, "Холодное", "Cold");
            CheckColumn(grid, "Тип проверен", "RoomTypeKnown");
            TextColumn(grid, "Коэфф.", "Coefficient", 75);
            CheckColumn(grid, "Расчётная офисов", "CalculatedOffice");
            CheckColumn(grid, "Полезная офисов", "UsefulOffice");
            CheckColumn(grid, "Рабочие места", "EmployeeArea");
            TextColumn(grid, "Номер", "Number", 90, true);
        }
        TextColumn(grid, "Ошибка источника", "Problem", 340, true);
        TextColumn(grid, "Проверить", "Review", 400, true);
        TextColumn(grid, "Документ", "Document", 280, true);
        grid.BeginningEdit += (_, _) => _reviewed.IsChecked = false;
    }

    private static UIElement Searchable(DataGrid grid)
    {
        DockPanel panel = new();
        TextBox search = Input("");
        search.ToolTip = "Поиск по имени, ID, группе, категории, части или документу";
        search.TextChanged += (_, _) =>
        {
            if (grid.ItemsSource is null) return;
            ICollectionView view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
            view.Filter = value => value is TepItem i && (i.Name + " " + i.ElementId + " " + i.Group + " " + i.Role + " " + i.Part + " " + i.Document)
                .IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0;
        };
        DockPanel.SetDock(search, Dock.Top); panel.Children.Add(search); panel.Children.Add(grid);
        return panel;
    }
    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Проверка ТЭП", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private static DataGrid Grid() => new() { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true, Margin = new Thickness(6) };
    private static TextBox Input(string text) => new() { Text = text, Margin = new Thickness(4), MinWidth = 150 };
    private static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private static Binding Binding(string path) => new(path) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus, ValidatesOnExceptions = true };
    private static void TextColumn(DataGrid grid, string header, string path, double width, bool readOnly = false, string? format = null) =>
        grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path) { StringFormat = format, ValidatesOnExceptions = true }, Width = width, IsReadOnly = readOnly });
    private static void CheckColumn(DataGrid grid, string header, string path) =>
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = header, Binding = Binding(path), Width = DataGridLength.Auto });
    private static UIElement Field(string label, UIElement control)
    {
        DockPanel panel = new();
        TextBlock text = Label(label); text.Width = 260; DockPanel.SetDock(text, Dock.Left);
        panel.Children.Add(text); panel.Children.Add(control); return panel;
    }
    private static Button Button(string title, Action action)
    {
        Button button = new() { Content = title, Margin = new Thickness(6), Padding = new Thickness(12, 6, 12, 6) };
        button.Click += (_, _) => action(); return button;
    }
}
