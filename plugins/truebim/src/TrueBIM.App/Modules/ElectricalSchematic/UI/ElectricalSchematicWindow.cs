using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TrueBIM.App.Modules.ElectricalSchematic.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.ElectricalSchematic.UI;

public sealed class ElectricalSchematicWindow : TrueBimWindow
{
    private readonly Func<ElectricalSchematicPreview> preview;
    private readonly Func<IReadOnlyList<ElectricalSchematicRow>, IReadOnlyDictionary<long, string>> apply;
    private readonly ITrueBimLogger logger;
    private readonly DataGrid grid = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox diagnostics = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Button applyButton;
    private IReadOnlyList<ElectricalSchematicRow> rows = [];

    public ElectricalSchematicWindow(Func<ElectricalSchematicPreview> preview,
        Func<IReadOnlyList<ElectricalSchematicRow>, IReadOnlyDictionary<long, string>> apply,
        IEnumerable<long> selectedIds, ITrueBimLogger logger)
    {
        this.preview = preview;
        this.apply = apply;
        this.logger = logger;
        Title = "Электрическая схема";
        Icon = IconFactory.CreateImage(TrueBimIcon.ElectricalSchematic, 32);
        Width = 1220;
        Height = 760;
        MinWidth = 920;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Button refresh = Button("Обновить предпросмотр", TrueBimIcon.Refresh, (_, _) => Refresh(rows.Where(r => r.IsSelected).Select(r => r.Id).ToArray()));
        Button guide = Button("Методичка", TrueBimIcon.Help, (_, _) => new ElectricalSchematicGuideWindow { Owner = this }.ShowDialog());
        Button clear = Button("Снять отметки", TrueBimIcon.Close, (_, _) =>
        {
            foreach (ElectricalSchematicRow row in rows) row.IsSelected = false;
            grid.Items.Refresh();
        });
        WrapPanel commands = new();
        commands.Children.Add(refresh);
        commands.Children.Add(clear);
        commands.Children.Add(guide);
        ConfigureGrid();
        TabControl tabs = new() { Style = TrueBimStyles.CreateTabControlStyle() };
        tabs.Items.Add(new TabItem { Header = "Группы схемы", Content = grid });
        tabs.Items.Add(new TabItem { Header = "Диагностика", Content = diagnostics });
        applyButton = Button("Обновить отмеченные", TrueBimIcon.Apply, (_, _) => Apply(), true);
        Button close = Button("Закрыть", TrueBimIcon.Close, (_, _) => Close());
        close.IsCancel = true;
        Content = BuildShell(
            TrueBimUi.CreateHeader(Title, "Отметьте группы, проверьте новые значения и обновите схему. Источники — весь активный проект.", TrueBimIcon.ElectricalSchematic),
            commands, tabs, TrueBimUi.CreateInfoBanner(status, TrueBimUiSeverity.Info),
            TrueBimUi.CreateFooter(null, applyButton, close));
        Refresh(selectedIds.ToArray());
    }

    private void ConfigureGrid()
    {
        grid.Style = TrueBimStyles.CreateDataGridStyle();
        grid.RowHeight = double.NaN;
        grid.MinRowHeight = 34;
        grid.AutoGenerateColumns = false;
        grid.IsReadOnly = false;
        grid.CanUserAddRows = false;
        grid.CanUserDeleteRows = false;
        FrameworkElementFactory check = new(typeof(CheckBox));
        check.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(ElectricalSchematicRow.IsSelected)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        check.SetBinding(IsEnabledProperty, new Binding(nameof(ElectricalSchematicRow.CanApply)));
        check.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        check.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        grid.Columns.Add(new DataGridTemplateColumn { Header = "✓", Width = 40, CellTemplate = new DataTemplate { VisualTree = check } });
        AddColumn("Группа", nameof(ElectricalSchematicRow.Group), 95);
        AddColumn("Вид", nameof(ElectricalSchematicRow.View), 110);
        AddColumn("ID схемы", nameof(ElectricalSchematicRow.Id), 85);
        AddColumn("Кол-во", nameof(ElectricalSchematicRow.Count), 60);
        AddColumn("Было, ВА", nameof(ElectricalSchematicRow.PreviousPower), 85);
        AddColumn("Будет, ВА", nameof(ElectricalSchematicRow.NewPower), 85);
        AddColumn("Потребители: было", nameof(ElectricalSchematicRow.PreviousConsumers), 140);
        AddColumn("Потребители: будет", nameof(ElectricalSchematicRow.Consumers), 150);
        AddColumn("Результат / причина пропуска", nameof(ElectricalSchematicRow.Message), 310);
    }

    private void AddColumn(string title, string property, double width)
    {
        Style text = new(typeof(TextBlock));
        text.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        text.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4)));
        grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = width, IsReadOnly = true, ElementStyle = text });
    }

    private void Refresh(IEnumerable<long> selectedIds)
    {
        try
        {
            HashSet<long> selected = new(selectedIds);
            ElectricalSchematicPreview result = preview();
            rows = result.Rows;
            foreach (ElectricalSchematicRow row in rows) row.IsSelected = row.CanApply && selected.Contains(row.Id);
            grid.ItemsSource = rows;
            int ignored = selected.Count(id => rows.All(r => r.Id != id));
            List<string> messages = result.Diagnostics.ToList();
            if (ignored > 0) messages.Add($"Выделенных элементов вне поддерживаемых схем: {ignored}.");
            messages.AddRange(rows.Where(r => !r.CanApply).Select(r => $"Схема ID {r.Id}, группа «{r.Group}»: {r.Message}"));
            diagnostics.Text = messages.Count == 0 ? "Ошибок параметров и групп не обнаружено." : string.Join(Environment.NewLine + Environment.NewLine, messages);
            status.Text = rows.Count == 0
                ? "Схемы не найдены. Нужны основные элементы узлов с параметром «• Установленная мощность». Откройте методичку."
                : $"Схем: {rows.Count}. Готово: {rows.Count(r => r.CanApply)}. Сообщений диагностики: {messages.Count}. Обновляются только строки с галочкой в первом столбце.";
            applyButton.IsEnabled = rows.Any(r => r.CanApply);
        }
        catch (Exception exception)
        {
            applyButton.IsEnabled = false;
            status.Text = "Не удалось прочитать проект: " + exception.Message;
            logger.Error("Electrical schematic preview failed.", exception);
        }
    }

    private void Apply()
    {
        grid.CommitEdit();
        ElectricalSchematicRow[] selected = rows.Where(r => r.IsSelected && r.CanApply).ToArray();
        if (selected.Length == 0)
        {
            status.Text = "Отметьте хотя бы одну готовую группу в первом столбце таблицы.";
            return;
        }
        try
        {
            IReadOnlyDictionary<long, string> results = apply(selected);
            foreach (ElectricalSchematicRow row in selected)
            {
                row.Message = results.TryGetValue(row.Id, out string? message) ? message : "Пропуск: результат не получен.";
                row.IsSelected = false;
            }
            grid.Items.Refresh();
            int updated = results.Values.Count(value => value.StartsWith("Обновлено:", StringComparison.Ordinal));
            status.Text = $"Обновлено: {updated}. Пропущено: {selected.Length - updated}. Для повторного запуска обновите предпросмотр. Отмена в Revit — Ctrl+Z после закрытия окна.";
            diagnostics.Text += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, selected.Select(r => $"ID {r.Id}, группа «{r.Group}»: {r.Message}"));
            applyButton.IsEnabled = false;
            logger.Info($"Electrical schematic synchronization: selected={selected.Length}, updated={updated}.");
        }
        catch (Exception exception)
        {
            status.Text = "Обновление отменено: " + exception.Message;
            logger.Error("Electrical schematic synchronization failed.", exception);
        }
    }

    private static Button Button(string text, TrueBimIcon icon, RoutedEventHandler action, bool primary = false)
    {
        Button button = new()
        {
            Content = IconFactory.CreateButtonContent(icon, text), MinHeight = 32, Margin = new Thickness(0, 0, 8, 0),
            Style = TrueBimStyles.CreateButtonStyle(primary ? TrueBimButtonStyleKind.Primary : TrueBimButtonStyleKind.Secondary)
        };
        button.Click += action;
        return button;
    }
}
