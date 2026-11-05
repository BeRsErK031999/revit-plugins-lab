using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using WpfGrid = System.Windows.Controls.Grid;
using WpfBinding = System.Windows.Data.Binding;
using Panel = System.Windows.Controls.Panel;
using Color = System.Windows.Media.Color;

namespace TrueBIM.App.Modules.GeometricFinishes;

public sealed class FinishMappingRow : INotifyPropertyChanged
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int Rooms { get; set; }
    public List<FinishTypeOption> Types { get; set; } = new();
    private string typeUniqueId = "";
    private bool enabled = true;
    private bool roomBounding;
    public string TypeUniqueId { get => typeUniqueId; set { typeUniqueId = value ?? ""; Changed(nameof(TypeUniqueId)); } }
    public bool Enabled { get => enabled; set { enabled = value; Changed(nameof(Enabled)); } }
    public bool RoomBounding { get => roomBounding; set { roomBounding = value; Changed(nameof(RoomBounding)); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class FinishWindow : Window
{
    private readonly FinishCatalog catalog;
    private readonly FinishSettings settings;
    private readonly string settingsPath;
    private readonly ComboBox scope = Combo("RoomScope"), level = Combo("RoomLevel"), grouping = Combo("Grouping"), elevation = Combo("Elevation");
    private readonly ComboBox typeParameter = Combo("TypeParameter"), roomParameter = Combo("RoomParameter"), finishParameter = Combo("FinishParameter"), bulkType = Combo("BulkType");
    private readonly TextBox roomFilter = Text("RoomFilter"), offset = Text("Offset"), maxThickness = Text("MaxThickness"), typeValue = Text("TypeParameterValue");
    private readonly CheckBox limitThickness = new() { Content = "Максимальная толщина, мм", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox join = new() { Content = "Соединять со стеной основы", Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox ceilingAsFloor = new() { Content = "Моделировать потолки перекрытиями", Margin = new Thickness(0, 5, 0, 0) };
    private readonly DataGrid table = new() { Name = "FinishAssignments", AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
        HeadersVisibility = DataGridHeadersVisibility.Column, RowHeaderWidth = 0, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        SelectionMode = DataGridSelectionMode.Extended, MinHeight = 130, RowHeight = 36, Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(210, 218, 229)), BorderThickness = new Thickness(1) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock counts = new() { Foreground = Brushes.DimGray, Margin = new Thickness(0, 5, 0, 5) };
    private readonly ListBox issueList = new() { MaxHeight = 100, MinHeight = 50, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Expander issuePanel = new() { Header = "Замечания", Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button create = Button("Создать", "CreateFinish");
    private readonly Button close = Button("Закрыть", "CloseFinish");
    private readonly List<Button> settingsButtons = new();
    private readonly WpfGrid settingsGrid = new();
    private IReadOnlyList<FinishTypeOption> allTypes = Array.Empty<FinishTypeOption>();
    private List<FinishMappingRow> rows = new();
    private bool ready, running, cancelRequested;
    public IReadOnlyList<ElementId> RequestedRoomIds { get; private set; } = Array.Empty<ElementId>();
    public FinishResult? LastResult { get; private set; }

    public FinishWindow(FinishCatalog catalog, FinishKind kind)
    {
        this.catalog = catalog;
        settingsPath = FinishSettingsStore.PathFor(string.IsNullOrEmpty(catalog.Document.PathName) ? catalog.Document.Title : catalog.Document.PathName, kind);
        settings = FinishSettingsStore.Load(settingsPath, kind, out string? loadWarning);
#if !REVIT2022_OR_GREATER
        if (kind == FinishKind.Ceilings) settings.CeilingAsFloor = true;
#endif
        Title = FinishService.Title(kind) + " · TrueBIM";
        Width = 1080; Height = 760; MinWidth = 880; MinHeight = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
        Background = new SolidColorBrush(Color.FromRgb(245, 247, 250));
        Foreground = new SolidColorBrush(Color.FromRgb(24, 36, 51));
        BuildLayout(kind);
        SetOptions(scope, new[] { ("Project", "Все в проекте"), ("ActiveView", "Все на активном виде"), ("Selected", "Выбранные помещения"), ("Level", "Все на этаже") });
        if (catalog.HasSelectedRooms) settings.Scope = FinishScope.Selected;
        scope.SelectedValue = settings.Scope.ToString();
        level.ItemsSource = catalog.Levels.Select(l => new FinishOption { Key = l.UniqueId, Name = l.Name }).ToArray();
        level.SelectedValue = settings.LevelUniqueId;
        if (level.SelectedIndex < 0) level.SelectedIndex = 0;
        SetOptions(grouping, new[] { ("WallType", "По типу стены основы"), ("RoomName", "По имени помещения"), ("RoomAndWallType", "По помещению и типу стены") });
        grouping.SelectedValue = settings.Grouping.ToString();
        SetOptions(elevation, new[] { ("Level", kind == FinishKind.Ceilings ? "От уровня этажа — низ потолка" : "От уровня этажа — верх пола"),
            ("Slab", kind == FinishKind.Ceilings ? "Прижать к перекрытию" : "Положить на перекрытие"),
            ("RoomBoundary", kind == FinishKind.Ceilings ? "По верхней границе помещения" : "По нижней границе помещения") });
        elevation.SelectedValue = settings.Elevation.ToString();
        roomFilter.Text = settings.RoomFilter; offset.Text = settings.OffsetMm.ToString(CultureInfo.CurrentCulture);
        maxThickness.Text = settings.MaxThicknessMm.ToString(CultureInfo.CurrentCulture);
        limitThickness.IsChecked = settings.LimitThickness; typeValue.Text = settings.TypeParameterValue;
        join.IsChecked = settings.JoinWalls; ceilingAsFloor.IsChecked = settings.CeilingAsFloor;
#if !REVIT2022_OR_GREATER
        ceilingAsFloor.IsEnabled = false;
#endif
        roomParameter.ItemsSource = WithNone(FinishParameters.Options(catalog.Rooms.Cast<Element>(), false));
        roomParameter.SelectedValue = settings.RoomParameterKey;
        if (roomParameter.SelectedIndex < 0) roomParameter.SelectedIndex = 0;
        ReloadTypes();
        ready = true;
        foreach (ComboBox combo in new[] { scope, level, grouping, elevation }) combo.SelectionChanged += (_, _) => RefreshRows();
        ceilingAsFloor.Checked += (_, _) => CategoryChanged(); ceilingAsFloor.Unchecked += (_, _) => CategoryChanged();
        limitThickness.Checked += (_, _) => RefreshRows(); limitThickness.Unchecked += (_, _) => RefreshRows();
        typeParameter.SelectionChanged += (_, _) => RefreshRows();
        foreach (TextBox text in new[] { roomFilter, maxThickness, typeValue }) text.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) RefreshRows(); };
        create.Click += (_, _) => Create();
        close.Click += (_, _) => { if (running) { cancelRequested = true; close.IsEnabled = false; status.Text = "Отменяю изменения этого запуска…"; } else Close(); };
        Closing += (_, e) => { if (running) { cancelRequested = true; e.Cancel = true; } };
        RefreshRows();
        if (loadWarning != null) status.Text = loadWarning;
    }

    private void BuildLayout(FinishKind kind)
    {
        WpfGrid root = new() { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;
        TextBlock header = new() { Text = FinishService.Title(kind), FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        root.Children.Add(header);
        WpfGrid.SetRow(settingsGrid, 1); root.Children.Add(settingsGrid);
        settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        settingsGrid.ColumnDefinitions.Add(new ColumnDefinition());
        StackPanel left = new();
        ScrollViewer scroll = new() { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        settingsGrid.Children.Add(scroll);
        StackPanel selection = Group(left, "Помещения");
        selection.Children.Add(scope); selection.Children.Add(level);
        AddLabel(selection, "Фильтр: имя, номер или назначение", roomFilter);
        Button refresh = Button("Показать помещения", "RefreshRooms"); refresh.Click += (_, _) => RefreshRows();
        selection.Children.Add(refresh); settingsButtons.Add(refresh);
        StackPanel heights = Group(left, kind == FinishKind.Walls ? "Высота отделки" : kind == FinishKind.Ceilings ? "Отметка потолка" : "Отметка пола");
        if (kind == FinishKind.Walls)
        {
            heights.Children.Add(Note("До верха помещения. Начало — от нижней границы помещения."));
            AddLabel(heights, "Смещение снизу, мм", offset); heights.Children.Add(join);
            heights.Children.Add(Note("Проёмы переносятся и из связей. Соединение доступно для основы в текущем файле."));
        }
        else
        {
            heights.Children.Add(elevation); AddLabel(heights, "Смещение от уровня, мм", offset);
            heights.Children.Add(Note("Контур — по границам помещения, включая внутренние отверстия. В дверях применяется та же граница."));
            if (kind == FinishKind.Ceilings)
            {
                heights.Children.Add(ceilingAsFloor);
#if !REVIT2022_OR_GREATER
                heights.Children.Add(Note("Revit 2019–2021: потолки создаются категорией «Перекрытия». Системные потолки доступны с Revit 2022."));
#endif
            }
        }
        StackPanel parameters = Group(left, "Привязка к помещению");
        AddLabel(parameters, "Параметр помещения", roomParameter);
        AddLabel(parameters, "Параметр экземпляра отделки", finishParameter);
        parameters.Children.Add(Note("«Не задано» в обоих полях отключает перенос. Текст можно записать в «Комментарии» или свой параметр."));
        left.Children.Add(Note("Создание выполняется только в текущем файле. Повторный запуск пропускает ранее созданную TrueBIM отделку; обновление не выполняется."));

        WpfGrid right = new(); WpfGrid.SetColumn(right, 2); settingsGrid.Children.Add(right);
        foreach (GridLength height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) right.RowDefinitions.Add(new RowDefinition { Height = height });
        right.RowDefinitions[2].MinHeight = table.MinHeight;
        StackPanel filters = new(); right.Children.Add(filters);
        StackPanel typeGroup = Group(filters, "Типы отделки");
        DockPanel thicknessLine = new(); maxThickness.Width = 75; maxThickness.HorizontalContentAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(maxThickness, Dock.Right); thicknessLine.Children.Add(maxThickness); thicknessLine.Children.Add(limitThickness); typeGroup.Children.Add(thicknessLine);
        WpfGrid filterLine = new(); filterLine.ColumnDefinitions.Add(new ColumnDefinition()); filterLine.ColumnDefinitions.Add(new ColumnDefinition());
        typeParameter.Margin = new Thickness(0, 3, 6, 3); typeValue.ToolTip = "Часть значения параметра типа; регистр не учитывается";
        filterLine.Children.Add(typeParameter); WpfGrid.SetColumn(typeValue, 1); filterLine.Children.Add(typeValue); typeGroup.Children.Add(filterLine);
        if (kind == FinishKind.Walls) AddLabel(typeGroup, "Назначать отделку", grouping);
        DockPanel bulk = new();
        Button applyType = Button("Назначить всем строкам", "AssignBulkType");
        applyType.Click += (_, _) => { if (bulkType.SelectedItem is FinishTypeOption type) foreach (FinishMappingRow row in rows.Where(r => r.Enabled)) row.TypeUniqueId = type.UniqueId; };
        DockPanel.SetDock(applyType, Dock.Right); bulk.Children.Add(applyType); bulk.Children.Add(bulkType); typeGroup.Children.Add(bulk);
        bulkType.DisplayMemberPath = "Name"; bulkType.SelectedValuePath = "UniqueId";
        WpfGrid.SetRow(counts, 1); right.Children.Add(counts);
        table.Columns.Add(new DataGridCheckBoxColumn { Header = "✓", Binding = Bind(nameof(FinishMappingRow.Enabled)), Width = new DataGridLength(35) });
        table.Columns.Add(new DataGridTextColumn { Header = kind == FinishKind.Walls ? "Основа / помещение" : "Помещение", Binding = Bind(nameof(FinishMappingRow.Label)), IsReadOnly = true, Width = new DataGridLength(0.9, DataGridLengthUnitType.Star) });
        table.Columns.Add(new DataGridTextColumn { Header = "Кол-во", Binding = Bind(nameof(FinishMappingRow.Rooms)), IsReadOnly = true, Width = new DataGridLength(52) });
        FrameworkElementFactory combo = new(typeof(ComboBox));
        combo.SetValue(ComboBox.DisplayMemberPathProperty, "Name"); combo.SetValue(ComboBox.SelectedValuePathProperty, "UniqueId");
        combo.SetBinding(ItemsControl.ItemsSourceProperty, Bind(nameof(FinishMappingRow.Types)));
        combo.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedValueProperty, Bind(nameof(FinishMappingRow.TypeUniqueId)));
        combo.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        table.Columns.Add(new DataGridTemplateColumn { Header = "Тип отделки", CellTemplate = new DataTemplate { VisualTree = combo }, Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
        table.Columns.Add(new DataGridCheckBoxColumn { Header = "Граница\nпомещения", Binding = Bind(nameof(FinishMappingRow.RoomBounding)), Width = new DataGridLength(100) });
        WpfGrid.SetRow(table, 2); right.Children.Add(table);
        StackPanel issueContent = new();
        FrameworkElementFactory issueText = new(typeof(TextBlock));
        issueText.SetBinding(TextBlock.TextProperty, new WpfBinding());
        issueText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        issueText.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 2));
        issueList.ItemTemplate = new DataTemplate { VisualTree = issueText };
        ScrollViewer.SetHorizontalScrollBarVisibility(issueList, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(issueList, ScrollBarVisibility.Auto);
        issueContent.Children.Add(issueList);
        Button show = Button("Показать выбранное помещение", "ShowIssueRoom");
        show.Click += (_, _) =>
        {
            if (issueList.SelectedItem is FinishIssue issue && issue.RoomId != ElementId.InvalidElementId)
            { RequestedRoomIds = new[] { issue.RoomId }; Close(); }
        };
        issueContent.Children.Add(show); issuePanel.Content = issueContent;
        WpfGrid.SetRow(issuePanel, 3); right.Children.Add(issuePanel);

        DockPanel footer = new() { Margin = new Thickness(0, 12, 0, 0), LastChildFill = true };
        WpfGrid.SetRow(footer, 2); root.Children.Add(footer);
        StackPanel actions = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        Button check = Button("Проверить", "CheckFinish"), save = Button("Сохранить настройки", "SaveFinishSettings");
        check.Click += (_, _) => { RefreshRows(); issuePanel.IsExpanded = issueList.Items.Count > 0; };
        save.Click += (_, _) => Save();
        settingsButtons.AddRange(new[] { check, save });
        create.Background = new SolidColorBrush(Color.FromRgb(30, 69, 148)); create.Foreground = Brushes.White;
        foreach (Button button in new[] { check, save, create, close }) actions.Children.Add(button);
        DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions); status.Margin = new Thickness(0, 0, 10, 0); footer.Children.Add(status);
    }

    private void CategoryChanged()
    {
        if (!ready || running) return;
        settings.CeilingAsFloor = ceilingAsFloor.IsChecked == true;
        ReloadTypes(); RefreshRows();
    }
    private void ReloadTypes()
    {
        bool wasReady = ready; ready = false;
        allTypes = catalog.Types(settings);
        typeParameter.ItemsSource = WithNone(FinishParameters.Options(allTypes.Select(t => catalog.Document.GetElement(t.Id)), false), "Без фильтра по параметру");
        typeParameter.SelectedValue = settings.TypeParameterKey;
        if (typeParameter.SelectedIndex < 0) typeParameter.SelectedIndex = 0;
        finishParameter.ItemsSource = WithNone(FinishParameters.TargetOptions(catalog.Document, FinishCatalog.CategoryFor(settings)));
        finishParameter.SelectedValue = settings.FinishParameterKey;
        if (finishParameter.SelectedIndex < 0) finishParameter.SelectedIndex = 0;
        ready = wasReady;
    }
    private void ReadSettings()
    {
        settings.Scope = ParseEnum<FinishScope>(scope);
        settings.LevelUniqueId = level.SelectedValue as string ?? "";
        settings.RoomFilter = roomFilter.Text.Trim();
        settings.Grouping = settings.Kind == FinishKind.Walls ? ParseEnum<FinishGrouping>(grouping) : FinishGrouping.RoomName;
        settings.Elevation = settings.Kind == FinishKind.Walls ? FinishElevation.RoomBoundary : ParseEnum<FinishElevation>(elevation);
        settings.OffsetMm = Number(offset.Text, "Смещение");
        settings.MaxThicknessMm = Number(maxThickness.Text, "Максимальная толщина");
        settings.LimitThickness = limitThickness.IsChecked == true;
        settings.JoinWalls = join.IsChecked == true; settings.CeilingAsFloor = ceilingAsFloor.IsChecked == true;
        settings.TypeParameterKey = typeParameter.SelectedValue as string ?? ""; settings.TypeParameterValue = typeValue.Text.Trim();
        settings.RoomParameterKey = roomParameter.SelectedValue as string ?? ""; settings.FinishParameterKey = finishParameter.SelectedValue as string ?? "";
        table.CommitEdit(DataGridEditingUnit.Cell, true); table.CommitEdit(DataGridEditingUnit.Row, true);
        var saved = settings.Assignments.GroupBy(a => a.Key).ToDictionary(g => g.Key, g => g.Last());
        foreach (FinishMappingRow row in rows) saved[row.Key] = new FinishAssignment { Key = row.Key, TypeUniqueId = row.TypeUniqueId, Enabled = row.Enabled, RoomBounding = row.RoomBounding };
        settings.Assignments = saved.Values.ToList();
    }
    private void RefreshRows()
    {
        if (!ready || running) return;
        try
        {
            ReadSettings();
            offset.IsEnabled = settings.Kind == FinishKind.Walls || settings.Elevation == FinishElevation.Level;
            level.IsEnabled = settings.Scope == FinishScope.Level;
            maxThickness.IsEnabled = settings.LimitThickness; typeValue.IsEnabled = !string.IsNullOrEmpty(settings.TypeParameterKey);
            FinishPlan plan = catalog.BuildPlan(settings);
            var filtered = allTypes.Where(t => (!settings.LimitThickness || t.ThicknessMm <= settings.MaxThicknessMm + 1e-6)
                && (string.IsNullOrEmpty(settings.TypeParameterKey) || t.Parameters.TryGetValue(settings.TypeParameterKey, out string? value)
                    && value.IndexOf(settings.TypeParameterValue, StringComparison.CurrentCultureIgnoreCase) >= 0)).ToList();
            bulkType.ItemsSource = filtered;
            if (filtered.Count > 0) bulkType.SelectedIndex = 0;
            var assigned = settings.Assignments.ToDictionary(a => a.Key);
            rows = plan.Targets.GroupBy(t => t.Key).Select(g =>
            {
                assigned.TryGetValue(g.Key, out FinishAssignment? a);
                string type = a?.TypeUniqueId ?? "";
                var choices = new List<FinishTypeOption> { new() { Name = "— выберите тип —" } };
                choices.AddRange(filtered);
                FinishTypeOption? previous = allTypes.FirstOrDefault(t => t.UniqueId == type);
                if (previous != null && !filtered.Any(t => t.UniqueId == type)) choices.Add(previous);
                return new FinishMappingRow { Key = g.Key, Label = g.First().Label, Rooms = g.Select(t => t.Room.Id).Distinct().Count(),
                    TypeUniqueId = previous == null ? "" : type, Enabled = a?.Enabled ?? true, RoomBounding = a?.RoomBounding ?? false, Types = choices };
            }).OrderBy(r => r.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
            table.ItemsSource = rows;
            counts.Text = $"Помещений: {plan.RoomCount} · строк настройки: {rows.Count} · типов по фильтру: {filtered.Count}";
            SetIssues(plan.Issues);
            status.Text = rows.Count == 0 ? "Нет подходящих помещений. Измените область выбора или фильтр." : "Выберите типы отделки и проверьте признак «Граница помещения».";
            create.IsEnabled = rows.Count > 0;
        }
        catch (Exception ex) { status.Text = ex.Message; create.IsEnabled = false; }
    }
    private void Save()
    {
        try
        {
            ReadSettings();
            string? error = settings.Validate(); if (error != null) throw new InvalidOperationException(error);
            FinishSettingsStore.Save(settingsPath, settings); status.Text = "Настройки сохранены для этого проекта и команды.";
        }
        catch (Exception ex) { status.Text = "Не удалось сохранить: " + ex.Message; }
    }
    private void Create()
    {
        RefreshRows();
        if (!create.IsEnabled) return;
        try
        {
            ReadSettings();
            if (rows.Any(r => r.Enabled && string.IsNullOrEmpty(r.TypeUniqueId)))
            { status.Text = "Назначьте тип каждой отмеченной строке или снимите её галочку."; return; }
            if (!rows.Any(r => r.Enabled)) { status.Text = "Отметьте хотя бы одну строку."; return; }
            FinishSettingsStore.Save(settingsPath, settings);
            running = true; cancelRequested = false;
            settingsGrid.IsEnabled = false; create.IsEnabled = false;
            foreach (Button button in settingsButtons) button.IsEnabled = false;
            close.Content = "Остановить";
            LastResult = new FinishService(catalog).Create(settings, (done, total) =>
            {
                status.Text = $"Обработка помещений: {done} из {total}…";
                Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                return !cancelRequested;
            });
            status.Text = LastResult.Summary;
            SetIssues(LastResult.Issues); issuePanel.IsExpanded = LastResult.Issues.Count > 0;
        }
        catch (Exception ex) { status.Text = "Не удалось создать отделку: " + ex.Message; }
        finally
        {
            running = false; settingsGrid.IsEnabled = true; create.IsEnabled = rows.Count > 0;
            foreach (Button button in settingsButtons) button.IsEnabled = true;
            close.Content = "Закрыть"; close.IsEnabled = true;
        }
    }
    private void SetIssues(IEnumerable<FinishIssue> issues)
    {
        FinishIssue[] list = issues.ToArray(); issueList.ItemsSource = list;
        issuePanel.Header = "Замечания: " + list.Length;
        issuePanel.Visibility = list.Length == 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    }
    private static T ParseEnum<T>(ComboBox combo) where T : struct => Enum.TryParse(combo.SelectedValue as string, out T value) ? value : default;
    private static double Number(string text, string label)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value)
            && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) throw new InvalidOperationException(label + ": введите число.");
        return value;
    }
    private static IEnumerable<FinishOption> WithNone(IEnumerable<FinishOption> options, string name = "— не задано —") => new[] { new FinishOption { Name = name } }.Concat(options);
    private static void SetOptions(ComboBox combo, IEnumerable<(string Key, string Name)> options) => combo.ItemsSource = options.Select(o => new FinishOption { Key = o.Key, Name = o.Name }).ToArray();
    private static ComboBox Combo(string name) => new() { Name = name, DisplayMemberPath = "Name", SelectedValuePath = "Key", MinHeight = 29, Margin = new Thickness(0, 3, 0, 3), VerticalContentAlignment = VerticalAlignment.Center };
    private static TextBox Text(string name) => new() { Name = name, MinHeight = 28, Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 3, 0, 3), VerticalContentAlignment = VerticalAlignment.Center };
    private static Button Button(string text, string name) => new() { Content = text, Name = name, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(4, 3, 0, 3), MinHeight = 31 };
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, FontSize = 12, Margin = new Thickness(0, 5, 0, 5) };
    private static void AddLabel(Panel parent, string text, UIElement control) { parent.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 5, 0, 0) }); parent.Children.Add(control); }
    private static StackPanel Group(Panel parent, string title)
    {
        StackPanel content = new();
        GroupBox box = new() { Header = title, Content = content, Padding = new Thickness(9, 5, 9, 9), Margin = new Thickness(0, 0, 0, 9), BorderBrush = new SolidColorBrush(Color.FromRgb(210, 218, 229)) };
        parent.Children.Add(box); return content;
    }
    private static WpfBinding Bind(string property) => new(property) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
}
