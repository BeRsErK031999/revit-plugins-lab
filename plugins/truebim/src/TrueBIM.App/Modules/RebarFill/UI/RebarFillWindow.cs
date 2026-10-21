using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TrueBIM.App.Modules.RebarFill.Models;
using TrueBIM.App.Modules.RebarFill.Revit;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.RebarFill.UI;

public sealed class RebarFillWindow : TrueBimWindow
{
    private readonly ComboBox parameter = new() { DisplayMemberPath = nameof(RebarFillParameter.Label), MinHeight = 32, IsTextSearchEnabled = true };
    private readonly TextBox value = new() { MinHeight = 32 };
    private readonly CheckBox onlyEmpty = new() { Content = "Заполнять только пустые значения", Margin = new Thickness(0, 12, 0, 8) };
    private readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12) };
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly DataGrid table = CreateTable(true);
    private readonly ComboBox typeFilter = new() { DisplayMemberPath = nameof(TypeOption.Label), MinHeight = 30, Margin = new Thickness(0, 4, 0, 6) };
    private readonly Dictionary<long, bool> selection = new();
    private bool changingVisibleSelection;
    private readonly Func<RebarFillParameter, IReadOnlyList<RebarFillRow>> inspect;
    private readonly Action<IReadOnlyList<RebarFillRow>, RebarFillParameter, string> validate;
    private IReadOnlyList<RebarFillRow> rows = Array.Empty<RebarFillRow>();

    public RebarFillWindow(int count, IReadOnlyList<RebarFillParameter> parameters,
        Func<RebarFillParameter, IReadOnlyList<RebarFillRow>> inspect,
        Action<IReadOnlyList<RebarFillRow>, RebarFillParameter, string> validate)
    {
        this.inspect = inspect;
        this.validate = validate;
        Title = "Заполнятор арматуры";
        Width = 1000; Height = 740; MinWidth = 780; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = IconFactory.CreateImage(TrueBimIcon.Parameter, 32);
        Grid body = new();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        StackPanel controls = new();
        TextBox search = new() { MinHeight = 30, Margin = new Thickness(0, 4, 0, 8), ToolTip = "Поиск параметра по названию" };
        controls.Children.Add(new TextBlock { Text = "Найти параметр экземпляра" });
        controls.Children.Add(search);
        controls.Children.Add(parameter);
        controls.Children.Add(detail);
        controls.Children.Add(new TextBlock { Text = "Новое значение", Margin = new Thickness(0, 0, 0, 4) });
        controls.Children.Add(value);
        onlyEmpty.Style = TrueBimStyles.CreateCheckBoxStyle();
        controls.Children.Add(onlyEmpty);
        controls.Children.Add(new TextBlock { Text = "Стержни: семейство / тип" });
        controls.Children.Add(typeFilter);
        WrapPanel selectionButtons = new() { Margin = new Thickness(0, 0, 0, 4) };
        Button selectVisible = new() { Content = "Отметить показанные", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        selectVisible.Click += (_, _) => SetVisibleSelection(true);
        Button clearVisible = new() { Content = "Снять показанные", Padding = new Thickness(10, 4, 10, 4) };
        clearVisible.Click += (_, _) => SetVisibleSelection(false);
        selectionButtons.Children.Add(selectVisible);
        selectionButtons.Children.Add(clearVisible);
        controls.Children.Add(selectionButtons);
        typeFilter.SelectionChanged += (_, _) => RefreshVisibleRows();
        onlyEmpty.Checked += (_, _) => RefreshSummary();
        onlyEmpty.Unchecked += (_, _) => RefreshSummary();
        body.Children.Add(controls);
        Grid.SetRow(table, 1); body.Children.Add(table);
        parameter.SelectionChanged += (_, _) => Refresh();
        search.TextChanged += (_, _) =>
        {
            RebarFillParameter? previous = SelectedParameter;
            parameter.ItemsSource = parameters.Where(p => p.Name.IndexOf(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            parameter.SelectedItem = previous;
            if (parameter.SelectedIndex < 0 && parameter.Items.Count > 0) parameter.SelectedIndex = 0;
        };
        parameter.ItemsSource = parameters;
        if (parameters.Count > 0) parameter.SelectedIndex = 0;
        Button run = TrueBimUi.CreatePrimaryButton("Заполнить", TrueBimIcon.Apply, (_, _) => Accept());
        run.IsDefault = true;
        Button cancel = TrueBimUi.CreateSecondaryButton("Отмена", TrueBimIcon.Close);
        cancel.IsCancel = true;
        ApplyTrueBimShell(TrueBimUi.CreateHeader(Title, $"Арматуры в выбранной области: {count}. Вложенность и принадлежность системе сохраняются.", TrueBimIcon.Parameter),
            null, body, summary, TrueBimUi.CreateFooter(null, run, cancel));
    }

    public RebarFillParameter? SelectedParameter => parameter.SelectedItem as RebarFillParameter;
    public string Input => value.Text;
    public bool OnlyEmpty => onlyEmpty.IsChecked == true;
    public IReadOnlyList<long> SelectedElementIds => RebarFillSelection.GetSelectedIds(rows, SelectedTypeId);
    private long? SelectedTypeId => (typeFilter.SelectedItem as TypeOption)?.Id;

    private void Refresh()
    {
        foreach (RebarFillRow row in rows) row.PropertyChanged -= OnSelectionChanged;
        rows = SelectedParameter is { } selected ? inspect(selected) : Array.Empty<RebarFillRow>();
        foreach (RebarFillRow row in rows)
        {
            row.IsSelected = !selection.TryGetValue(row.ElementId, out bool isChecked) || isChecked;
            row.PropertyChanged += OnSelectionChanged;
        }
        if (typeFilter.Items.Count == 0 && rows.Count > 0)
        {
            List<TypeOption> types = new() { new TypeOption(null, $"Все семейства и типы ({rows.Count})") };
            types.AddRange(rows.GroupBy(row => row.TypeId).OrderBy(group => group.First().TypeName, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new TypeOption(group.Key, $"{group.First().TypeName} ({group.Count()})")));
            typeFilter.ItemsSource = types;
            typeFilter.SelectedIndex = 0;
        }
        detail.Text = SelectedParameter?.Identity + "\nДля чисел используются единицы проекта. Для Да/Нет: Да, Нет, 1 или 0.";
        RefreshVisibleRows();
    }

    private void RefreshVisibleRows()
    {
        ListCollectionView view = new(rows.Where(row => RebarFillSelection.IsVisible(row, SelectedTypeId)).ToList());
        table.ItemsSource = view;
        view.SortDescriptions.Add(new SortDescription(nameof(RebarFillRow.TypeName), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(RebarFillRow.ElementId), ListSortDirection.Ascending));
        foreach (DataGridColumn column in table.Columns) column.SortDirection = null;
        table.Columns.OfType<DataGridTextColumn>().First(column => (column.Binding as Binding)?.Path?.Path == nameof(RebarFillRow.TypeName)).SortDirection = ListSortDirection.Ascending;
        RefreshSummary();
    }

    private void SetVisibleSelection(bool selected)
    {
        changingVisibleSelection = true;
        try { foreach (RebarFillRow row in rows.Where(row => RebarFillSelection.IsVisible(row, SelectedTypeId))) row.IsSelected = selected; }
        finally { changingVisibleSelection = false; RefreshSummary(); }
    }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is RebarFillRow row && args.PropertyName == nameof(RebarFillRow.IsSelected))
        {
            selection[row.ElementId] = row.IsSelected;
            if (!changingVisibleSelection) RefreshSummary();
        }
    }

    private void RefreshSummary()
    {
        HashSet<long> selectedIds = new(SelectedElementIds);
        List<RebarFillRow> selected = rows.Where(row => selectedIds.Contains(row.ElementId)).ToList();
        summary.Text = $"Отмечено: {selected.Count}. Показано: {rows.Count(row => RebarFillSelection.IsVisible(row, SelectedTypeId))} из {rows.Count}. "
            + $"К заполнению: {selected.Count(row => row.CanWrite && (!OnlyEmpty || !row.HasValue))}. Недоступно из отмеченных: {selected.Count(row => !row.CanWrite)}."
            + "\nЗаполняются только отмеченные строки текущего фильтра. Скрытые и неотмеченные стержни не изменяются.";
        if (selected.Any(row => row.BarCount > 1)) summary.Text += "\nЗначение относится ко всем стержням выбранного элемента набора (столбец «В наборе»).";
    }

    private void Accept()
    {
        try
        {
            if (SelectedParameter is null) throw new ArgumentException("Выберите параметр из списка.");
            HashSet<long> selectedIds = new(SelectedElementIds);
            if (selectedIds.Count == 0) throw new ArgumentException("Отметьте галочками стержни, которым нужно заполнить параметр.");
            List<RebarFillRow> candidates = rows.Where(r => selectedIds.Contains(r.ElementId) && r.CanWrite && (!OnlyEmpty || !r.HasValue)).ToList();
            if (candidates.Count == 0) throw new ArgumentException("Нет доступных элементов для заполнения. Причины приведены в таблице.");
            if (Input.Length == 0) throw new ArgumentException("Введите значение. Очистка параметров этой командой не выполняется.");
            validate(candidates, SelectedParameter, Input);
            DialogResult = true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private sealed class TypeOption
    {
        public long? Id { get; }
        public string Label { get; }
        public TypeOption(long? id, string label) { Id = id; Label = label; }
    }

    internal static DataGrid CreateTable(bool selectable = false)
    {
        DataGrid grid = new()
        {
            AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
            CanUserDeleteRows = false, EnableRowVirtualization = true, SelectionMode = DataGridSelectionMode.Extended,
            ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader,
            Margin = new Thickness(0, 4, 0, 10)
        };
        if (selectable)
        {
            FrameworkElementFactory checkbox = new(typeof(CheckBox));
            checkbox.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(RebarFillRow.IsSelected)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            checkbox.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            checkbox.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            checkbox.SetValue(FrameworkElement.ToolTipProperty, "Заполнить параметр этого стержня");
            grid.Columns.Add(new DataGridTemplateColumn { Header = "✓", Width = 40, CanUserSort = false, CellTemplate = new DataTemplate { VisualTree = checkbox } });
        }
        grid.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new Binding(nameof(RebarFillRow.ElementId)), Width = 85 });
        grid.Columns.Add(new DataGridTextColumn { Header = "В наборе", Binding = new Binding(nameof(RebarFillRow.BarCount)), Width = 70 });
        Style typeStyle = new(typeof(TextBlock));
        typeStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        typeStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(nameof(RebarFillRow.TypeName))));
        grid.Columns.Add(new DataGridTextColumn { Header = "Семейство / тип", Binding = new Binding(nameof(RebarFillRow.TypeName)), ElementStyle = typeStyle, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Было", Binding = new Binding(nameof(RebarFillRow.CurrentValue)), Width = 150 });
        Style statusStyle = new(typeof(TextBlock));
        statusStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        statusStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(nameof(RebarFillRow.Status))));
        grid.Columns.Add(new DataGridTextColumn { Header = "Состояние", Binding = new Binding(nameof(RebarFillRow.Status)), ElementStyle = statusStyle, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        return grid;
    }
}

public sealed class RebarFillResultWindow : TrueBimWindow
{
    public RebarFillResultWindow(string parameterName, string value, IReadOnlyList<RebarFillRow> rows)
    {
        Title = "Результат заполнения арматуры";
        Width = 920; Height = 560; MinWidth = 700; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        DataGrid grid = RebarFillWindow.CreateTable();
        grid.ItemsSource = rows;
        Button close = TrueBimUi.CreateSecondaryButton("Закрыть", TrueBimIcon.Close, (_, _) => Close());
        close.IsCancel = true;
        ApplyTrueBimShell(TrueBimUi.CreateHeader(Title, $"{parameterName} = {value}", TrueBimIcon.Parameter), null, grid,
            new TextBlock { Text = $"Записано: {rows.Count(r => r.Status == "Записано")}. Уже задано: {rows.Count(r => r.Status == "Уже задано")}. "
                + $"Пропущено: {rows.Count(r => r.Status != "Записано" && r.Status != "Уже задано")}.\nСтроки отчёта можно скопировать сочетанием Ctrl+C.", TextWrapping = TextWrapping.Wrap },
            TrueBimUi.CreateFooter(null, close));
    }
}
