using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.Modules.BimTools.CopyParameters.Models;
using TrueBIM.App.UI;
using WpfGrid = System.Windows.Controls.Grid;

namespace TrueBIM.App.Modules.BimTools.CopyParameters.UI;

public sealed class CopyParametersWindow : TrueBimWindow
{
    private readonly List<CopyParameterRow> rows;
    private readonly TextBlock statusText = new();
    private readonly ListBox instanceParameterList = new();
    private readonly ListBox typeParameterList = new();

    public CopyParametersWindow(string sourceElementLabel, IReadOnlyList<CopyParameterRow> parameters)
    {
        rows = parameters?.ToList() ?? throw new ArgumentNullException(nameof(parameters));

        Title = "Копирование параметров";
        Icon = IconFactory.CreateImage(TrueBimIcon.CopyParameters, 32);
        Width = 900;
        Height = 650;
        MinWidth = 820;
        MinHeight = 560;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent(sourceElementLabel);

        RefreshList();
    }

    public IReadOnlyList<CopyParameterRow> SelectedParameters => rows
        .Where(row => row.IsSelected)
        .ToList();

    private UIElement CreateContent(string sourceElementLabel)
    {
        WpfGrid root = new()
        {
            Margin = new Thickness(16)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        StackPanel header = new();
        header.Children.Add(new TextBlock
        {
            Text = "Копирование параметров",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = "Выберите параметры исходного элемента. После подтверждения Revit попросит выбрать элементы-получатели.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        root.Children.Add(header);

        TextBlock sourceText = new()
        {
            Text = $"Исходный элемент: {sourceElementLabel}",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 12)
        };
        WpfGrid.SetRow(sourceText, 1);
        root.Children.Add(sourceText);

        StackPanel toolbar = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        toolbar.Children.Add(CreateSmallButton("Выбрать всё", (_, _) => SetSelection(true)));
        Button clearButton = CreateSmallButton("Очистить", (_, _) => SetSelection(false));
        clearButton.Margin = new Thickness(8, 0, 0, 0);
        toolbar.Children.Add(clearButton);
        WpfGrid.SetRow(toolbar, 2);
        root.Children.Add(toolbar);

        UIElement parameterColumns = CreateParameterColumns();
        WpfGrid.SetRow(parameterColumns, 3);
        root.Children.Add(parameterColumns);

        statusText.Foreground = Brushes.DimGray;
        statusText.Margin = new Thickness(0, 10, 0, 10);
        statusText.TextWrapping = TextWrapping.Wrap;
        WpfGrid.SetRow(statusText, 4);
        root.Children.Add(statusText);

        StackPanel footer = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        WpfGrid.SetRow(footer, 5);
        root.Children.Add(footer);

        Button applyButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Apply, "Выбрать получателей"),
            MinWidth = 190,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Закрыть окно и перейти к выбору элементов-получателей в Revit."
        };
        applyButton.Click += (_, _) => ConfirmSelection();
        footer.Children.Add(applyButton);

        Button cancelButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Отмена"),
            MinWidth = 110,
            Height = 32,
            IsCancel = true
        };
        cancelButton.Click += (_, _) => DialogResult = false;
        footer.Children.Add(cancelButton);

        return root;
    }

    private void RefreshList()
    {
        instanceParameterList.Items.Clear();
        typeParameterList.Items.Clear();
        foreach (CopyParameterRow row in rows)
        {
            ListBox targetList = row.SourceKind == ParameterSourceKind.Instance
                ? instanceParameterList
                : typeParameterList;
            targetList.Items.Add(CreateParameterRow(row));
        }

        UpdateStatus();
    }

    private UIElement CreateParameterColumns()
    {
        WpfGrid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        AddColumnHeader(grid, "Параметры экземпляра", 0);
        AddColumnHeader(grid, "Параметры типа", 1);

        ConfigureParameterList(instanceParameterList);
        ConfigureParameterList(typeParameterList);
        instanceParameterList.Margin = new Thickness(0, 0, 6, 0);
        typeParameterList.Margin = new Thickness(6, 0, 0, 0);

        WpfGrid.SetRow(instanceParameterList, 1);
        WpfGrid.SetColumn(instanceParameterList, 0);
        grid.Children.Add(instanceParameterList);

        WpfGrid.SetRow(typeParameterList, 1);
        WpfGrid.SetColumn(typeParameterList, 1);
        grid.Children.Add(typeParameterList);

        return grid;
    }

    private static void AddColumnHeader(WpfGrid grid, string text, int column)
    {
        TextBlock header = new()
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = column == 0 ? new Thickness(0, 0, 6, 6) : new Thickness(6, 0, 0, 6)
        };
        WpfGrid.SetColumn(header, column);
        grid.Children.Add(header);
    }

    private static void ConfigureParameterList(ListBox list)
    {
        list.BorderBrush = Brushes.LightGray;
        list.BorderThickness = new Thickness(1);
        list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
    }

    private UIElement CreateParameterRow(CopyParameterRow row)
    {
        CheckBox checkBox = new()
        {
            IsChecked = row.IsSelected,
            Margin = new Thickness(8, 6, 8, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Content = CreateParameterContent(row)
        };
        checkBox.Checked += (_, _) => UpdateRowSelection(row, true);
        checkBox.Unchecked += (_, _) => UpdateRowSelection(row, false);
        return checkBox;
    }

    private static UIElement CreateParameterContent(CopyParameterRow row)
    {
        StackPanel panel = new();
        panel.Children.Add(new TextBlock
        {
            Text = row.ParameterName,
            FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{row.ValueDisplay} | {row.StorageTypeDisplay} | {row.SourceDisplay}",
            Foreground = Brushes.DimGray,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });

        if (row.HasWarning)
        {
            panel.Children.Add(new TextBlock
            {
                Text = row.Warning,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 90, 20)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        return panel;
    }

    private void UpdateRowSelection(CopyParameterRow row, bool isSelected)
    {
        row.IsSelected = isSelected;
        UpdateStatus();
    }

    private void SetSelection(bool isSelected)
    {
        foreach (CopyParameterRow row in rows)
        {
            row.IsSelected = isSelected;
        }

        RefreshList();
    }

    private void ConfirmSelection()
    {
        if (SelectedParameters.Count == 0)
        {
            statusText.Text = "Выберите хотя бы один параметр для копирования.";
            return;
        }

        DialogResult = true;
    }

    private void UpdateStatus()
    {
        int selectedCount = rows.Count(row => row.IsSelected);
        int warningCount = rows.Count(row => row.HasWarning);
        int instanceCount = rows.Count(row => row.SourceKind == ParameterSourceKind.Instance);
        int typeCount = rows.Count(row => row.SourceKind == ParameterSourceKind.Type);
        statusText.Text = $"Доступно параметров: {rows.Count} (экземпляра: {instanceCount}, типа: {typeCount}). Выбрано: {selectedCount}. С предупреждениями: {warningCount}.";
    }

    private static Button CreateSmallButton(string text, RoutedEventHandler clickHandler)
    {
        Button button = new()
        {
            Content = text,
            Height = 28,
            MinWidth = 92
        };
        button.Click += clickHandler;
        return button;
    }
}
