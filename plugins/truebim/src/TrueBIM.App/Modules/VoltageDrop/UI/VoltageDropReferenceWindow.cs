using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.Modules.VoltageDrop.Models;
using TrueBIM.App.Modules.VoltageDrop.Services;
using TrueBIM.App.UI;

namespace TrueBIM.App.Modules.VoltageDrop.UI;

public sealed class VoltageDropReferenceWindow : Window
{
    private readonly VoltageDropReferenceCatalog catalog;

    public VoltageDropReferenceWindow(VoltageDropReferenceCatalog catalog)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        Title = "Справочники расчета потери напряжения";
        Icon = IconFactory.CreateImage(TrueBimIcon.Preview, 32);
        Width = 960;
        Height = 700;
        MinWidth = 820;
        MinHeight = 560;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();
    }

    private UIElement CreateContent()
    {
        DockPanel root = new()
        {
            Margin = new Thickness(18)
        };

        UIElement footer = CreateFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        TabControl tabs = new();
        tabs.Items.Add(CreateTab("Коэффициенты C", CreateCoefficientContent()));
        tabs.Items.Add(CreateTab("Квартиры", CreateApartmentContent()));
        tabs.Items.Add(CreateTab("Лифты", CreateLiftContent()));
        tabs.Items.Add(CreateTab("Формулы", CreateFormulaContent()));
        tabs.Items.Add(CreateTab("Изображения", CreateImageMapContent()));
        root.Children.Add(tabs);

        return root;
    }

    private static UIElement CreateFooter()
    {
        StackPanel footer = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };

        Button closeButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Закрыть"),
            MinWidth = 120,
            Height = 32,
            IsCancel = true,
            ToolTip = "Закрыть окно справочников."
        };
        closeButton.Click += (_, _) => Window.GetWindow(closeButton)?.Close();
        footer.Children.Add(closeButton);

        return footer;
    }

    private static TabItem CreateTab(string header, UIElement content)
    {
        return new TabItem
        {
            Header = header,
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content
            }
        };
    }

    private UIElement CreateCoefficientContent()
    {
        StackPanel panel = new()
        {
            Margin = new Thickness(0, 12, 0, 0)
        };

        panel.Children.Add(CreateSection(
            "Коэффициенты C из справочной таблицы",
            CreateReferenceTable(
                ["Материал", "Напряжение", "C", "Описание"],
                catalog.VoltageDropCoefficients.Select(entry => (IReadOnlyList<string>)
                [
                    FormatMaterial(entry.Material),
                    $"{Format(entry.Voltage)} В",
                    Format(entry.Coefficient),
                    entry.Description
                ]))));

        return panel;
    }

    private UIElement CreateApartmentContent()
    {
        StackPanel panel = new()
        {
            Margin = new Thickness(0, 12, 0, 0)
        };

        panel.Children.Add(CreateSection(
            "Ко/Руд для квартир с электроплитами",
            CreateReferenceTable(
                ["Количество квартир", "Значение", "Примечание"],
                CreateReferencePointRows(catalog.StandardApartmentSpecificDemandPoints, "10"))));

        panel.Children.Add(CreateSection(
            "Коэффициент по количеству квартир повышенной комфортности",
            CreateReferenceTable(
                ["Количество квартир", "Значение", "Примечание"],
                CreateReferencePointRows(catalog.HighComfortApartmentCountDemandPoints, "1"))));

        panel.Children.Add(CreateSection(
            "Коэффициент по Ру квартир повышенной комфортности",
            CreateReferenceTable(
                ["Ру, кВт", "Значение", "Правило"],
                CreateHighComfortUnitPowerRows())));

        return panel;
    }

    private static UIElement CreateLiftContent()
    {
        StackPanel panel = new()
        {
            Margin = new Thickness(0, 12, 0, 0)
        };

        panel.Children.Add(CreateSection(
            "Коэффициент спроса лифтов: до 11 этажей",
            CreateReferenceTable(
                ["Количество лифтов", "Значение", "Правило"],
                CreateLowRiseLiftRows())));

        panel.Children.Add(CreateSection(
            "Коэффициент спроса лифтов: выше 11 этажей",
            CreateReferenceTable(
                ["Количество лифтов", "Значение", "Правило"],
                CreateTallLiftRows())));

        return panel;
    }

    private static UIElement CreateFormulaContent()
    {
        StackPanel panel = new()
        {
            Margin = new Thickness(0, 12, 0, 0)
        };

        panel.Children.Add(CreateSection(
            "Формулы первого листа",
            CreateReferenceTable(
                ["Блок", "Формула", "Ячейки Excel"],
                CreateFormulaRows())));

        return panel;
    }

    private static UIElement CreateImageMapContent()
    {
        StackPanel panel = new()
        {
            Margin = new Thickness(0, 12, 0, 0)
        };

        panel.Children.Add(CreateSection(
            "Карта встроенных изображений первого листа",
            CreateReferenceTable(
                ["Изображения", "Что перенесено", "Использование"],
                CreateImageMapRows())));

        return panel;
    }

    private static UIElement CreateSection(string header, UIElement content)
    {
        GroupBox groupBox = new()
        {
            Header = header,
            Content = content,
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(10)
        };
        return groupBox;
    }

    private static Grid CreateReferenceTable(
        IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<string>> rows)
    {
        List<IReadOnlyList<string>> rowList = rows.ToList();
        Grid grid = new()
        {
            MinWidth = 760
        };

        for (int column = 0; column < columns.Count; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = column == columns.Count - 1
                    ? new GridLength(1, GridUnitType.Star)
                    : new GridLength(170)
            });
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int row = 0; row < rowList.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (int column = 0; column < columns.Count; column++)
        {
            AddCell(grid, 0, column, columns[column], isHeader: true);
        }

        for (int row = 0; row < rowList.Count; row++)
        {
            IReadOnlyList<string> values = rowList[row];
            for (int column = 0; column < columns.Count; column++)
            {
                string value = column < values.Count ? values[column] : string.Empty;
                AddCell(grid, row + 1, column, value, isHeader: false);
            }
        }

        return grid;
    }

    private static void AddCell(Grid grid, int row, int column, string text, bool isHeader)
    {
        Border border = new()
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(208, 214, 219)),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Background = isHeader
                ? new SolidColorBrush(Color.FromRgb(236, 240, 244))
                : Brushes.White
        };
        TextBlock textBlock = new()
        {
            Text = text,
            FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal,
            Padding = new Thickness(8, 5, 8, 5),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        border.Child = textBlock;
        Grid.SetRow(border, row);
        Grid.SetColumn(border, column);
        grid.Children.Add(border);
    }

    private static IEnumerable<IReadOnlyList<string>> CreateReferencePointRows(
        IReadOnlyList<VoltageDropReferencePoint> points,
        string belowRangeValue)
    {
        yield return ["1-5", belowRangeValue, "Без интерполяции"];
        foreach (VoltageDropReferencePoint point in points)
        {
            yield return [Format(point.Input), Format(point.Value), "Узел интерполяции"];
        }
    }

    private static IEnumerable<IReadOnlyList<string>> CreateHighComfortUnitPowerRows()
    {
        yield return ["1-14", "0.8", "Без интерполяции"];
        yield return ["14-19", "0.8..0.65", "Линейная интерполяция к 20 кВт"];
        yield return ["20-29", "0.65..0.6", "Линейная интерполяция к 30 кВт"];
        yield return ["30-39", "0.6..0.55", "Линейная интерполяция к 40 кВт"];
        yield return ["40-49", "0.55..0.5", "Формула сохранена как в Excel"];
        yield return ["50-59", "0.5..0.48", "Линейная интерполяция к 60 кВт"];
        yield return ["60-70", "0.48..0.45", "Линейная интерполяция"];
    }

    private static IEnumerable<IReadOnlyList<string>> CreateLowRiseLiftRows()
    {
        yield return ["1", "1", "Без интерполяции"];
        yield return ["2-3", "0.8", "Без интерполяции"];
        yield return ["4-5", "0.7", "Без интерполяции"];
        yield return ["6-10", "0.65..0.5", "Линейная интерполяция"];
        yield return ["11-20", "0.5..0.4", "Линейная интерполяция"];
        yield return ["21-25", "0.4..0.35", "Линейная интерполяция"];
        yield return [">25", "0.35", "Постоянное значение"];
    }

    private static IEnumerable<IReadOnlyList<string>> CreateTallLiftRows()
    {
        yield return ["1", "1", "Без интерполяции"];
        yield return ["2-3", "0.9", "Без интерполяции"];
        yield return ["4-5", "0.8", "Без интерполяции"];
        yield return ["6-10", "0.75..0.6", "Линейная интерполяция"];
        yield return ["11-20", "0.6..0.5", "Линейная интерполяция"];
        yield return ["21-25", "0.5..0.4", "Линейная интерполяция"];
        yield return [">25", "0.4", "Постоянное значение"];
    }

    private static IEnumerable<IReadOnlyList<string>> CreateFormulaRows()
    {
        yield return ["Потеря напряжения", "M = L*P", "B9"];
        yield return ["Потеря напряжения", "ΔU% = M/(C*S)", "B11:B14"];
        yield return ["Ток 3Ф", "I = P/(0.38*1.73*cosφ)", "B16:B22"];
        yield return ["Ток 1Ф", "I = P/(0.22*cosφ)", "B24:B30"];
        yield return ["Квартиры", "Pр = n*Ко/Руд*Кс*Кс.об", "K67"];
        yield return ["Лифты", "Pр = Pуст*Кс*Кс.об", "K68, K75"];
        yield return ["Полная мощность", "S = P/cosφ", "N67, N68, N73, N75, N77"];
        yield return ["Реактивная мощность", "Q = SQRT(S*S-P*P)", "M67, M68, M73, M75, M77"];
        yield return ["cosφ 3Ф", "cosφ = P/(I*0.38*1.73)", "U78"];
        yield return ["cosφ 1Ф", "cosφ = P/(I*0.22)", "Y78"];
        yield return ["Новый ток 3Ф", "I = P/(0.38*1.73*cosφ)", "U81"];
        yield return ["sinφ", "sinφ = SQRT(1-cosφ^2)", "F90"];
        yield return ["Потеря U", "U = (ρ1*(l/s)*cosφ + λ*l*sinφ)*Ip", "F85"];
        yield return ["Потеря U%", "U% = 100*(U/380)", "J85"];
        yield return ["Линейная ΔU", "ΔU = 1.73*Ip*L*(ro*cosφ+xo*sinφ)", "K99"];
        yield return ["Линейная ΔU%", "ΔU% = ΔU*100/380", "M99"];
    }

    private static IEnumerable<IReadOnlyList<string>> CreateImageMapRows()
    {
        yield return ["image1:image4", "Таблицы допустимых токовых нагрузок", "Справочный источник первого листа; в текущих формулах окна не участвует"];
        yield return ["image5:image11", "Коэффициенты спроса квартир и лифтов", "Перенесены в табличные правила и интерполяцию"];
        yield return ["image12:image14", "Формулы ΔU, M и коэффициенты C", "Перенесены в расчеты и справочник коэффициентов"];
    }

    private static string FormatMaterial(VoltageDropConductorMaterial material)
    {
        return material == VoltageDropConductorMaterial.Aluminum ? "Al" : "Cu";
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.CurrentCulture);
    }
}
