using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Vernadskogo.App.Core;
using Vernadskogo.App.Revit;

namespace Vernadskogo.App.UI;

internal sealed class MaterialRow
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Number { get; set; } = "";
}

internal sealed class MarkingWindow : Window
{
    private readonly List<MaterialRow> rows;
    private readonly DataGrid grid;
    private readonly ComboBox direction;
    private readonly TextBlock error;

    public IReadOnlyDictionary<string, string> Numbers { get; private set; } = new Dictionary<string, string>();
    public StartDirection Direction => (StartDirection)direction.SelectedValue;

    public MarkingWindow(PanelData data, string building, PanelSettings settings)
    {
        Title = "Вернадского · Маркировка панелей";
        Width = 780;
        Height = 650;
        MinWidth = 620;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;

        var root = new DockPanel { Margin = new Thickness(24) };
        Content = root;
        var heading = new StackPanel();
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = "Маркировка панелей", FontSize = 25, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock
        {
            Text = "Корпус К" + building + "  ·  Панелей: " + data.Panels.Count
                + "  ·  Этажей: " + data.Panels.Select(p => p.Floor).Distinct().Count(),
            Foreground = Brushes.DimGray, Margin = new Thickness(0, 8, 0, 22)
        });
        heading.Children.Add(new TextBlock { Text = "Начать нумерацию с направления", FontWeight = FontWeights.SemiBold });
        var directions = new[]
        {
            new { Value = StartDirection.South, Name = "Юг" },
            new { Value = StartDirection.SouthEast, Name = "Юго-восток" },
            new { Value = StartDirection.East, Name = "Восток" },
            new { Value = StartDirection.NorthEast, Name = "Северо-восток" },
            new { Value = StartDirection.North, Name = "Север" },
            new { Value = StartDirection.NorthWest, Name = "Северо-запад" },
            new { Value = StartDirection.West, Name = "Запад" },
            new { Value = StartDirection.SouthWest, Name = "Юго-запад" }
        };
        direction = new ComboBox
        {
            ItemsSource = directions, DisplayMemberPath = "Name", SelectedValuePath = "Value",
            SelectedValue = settings.Direction, Width = 220, HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 8), Padding = new Thickness(8, 5, 8, 5)
        };
        heading.Children.Add(direction);
        heading.Children.Add(new TextBlock
        {
            Text = "Обход против часовой стрелки. Север — условный север проекта.\n"
                + "Отсчёт от луча из центра панелей каждого этажа; на каждом этаже — с 1.",
            Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20)
        });

        var footer = new StackPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        footer.Children.Add(error);
        footer.Children.Add(new TextBlock
        {
            Text = "Марка: материал_корпус_этаж_номер, например 1_1_13_7.\n"
                + "«• Секция», «GP_Этаж» и «Марка» будут перезаписаны.\n"
                + "Соответствия материалов сохраняются в модели без добавления параметров.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 14, 0, 16)
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        footer.Children.Add(buttons);
        var cancel = new Button { Content = "Отмена", IsCancel = true, Padding = new Thickness(20, 8, 20, 8), Margin = new Thickness(0, 0, 12, 0) };
        buttons.Children.Add(cancel);
        var run = new Button { Content = "Маркировать", IsDefault = true, Padding = new Thickness(20, 8, 20, 8) };
        run.Click += Run;
        buttons.Children.Add(run);

        rows = data.Materials.OrderBy(p => p.Value, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new MaterialRow { Key = p.Key, Name = p.Value,
                Number = settings.Materials.TryGetValue(p.Key, out string? number) ? number : "" }).ToList();
        grid = new DataGrid
        {
            ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            CanUserReorderColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeaderWidth = 0, RowHeight = 36, ColumnHeaderHeight = 36, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            SelectionMode = DataGridSelectionMode.Single, Background = Brushes.White
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Материал несущих конструкций", Binding = new Binding(nameof(MaterialRow.Name)),
            IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Номер материала", Width = 180,
            Binding = new Binding(nameof(MaterialRow.Number)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
        });
        root.Children.Add(grid);
    }

    private void Run(object sender, RoutedEventArgs args)
    {
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
        var numbers = new Dictionary<string, string>();
        var used = new Dictionary<string, string>();
        foreach (MaterialRow row in rows)
        {
            string number;
            try { number = PanelNumbering.NormalizeMaterialNumber(row.Number); }
            catch (InvalidOperationException)
            {
                ShowError(row, "Материал «" + row.Name + "»: введите номер, содержащий только цифры.");
                return;
            }
            if (used.TryGetValue(number, out string? other))
            {
                ShowError(row, "Номер " + number + " уже задан материалу «" + other + "». Номера должны различаться.");
                return;
            }
            used[number] = row.Name;
            numbers[row.Key] = number;
        }
        Numbers = numbers;
        DialogResult = true;
    }

    private void ShowError(MaterialRow row, string message)
    {
        error.Text = message;
        grid.SelectedItem = row;
        grid.ScrollIntoView(row);
    }
}
