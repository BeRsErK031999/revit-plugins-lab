using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.Modules.BimTools.ColorByParameter.Models;
using TrueBIM.App.Modules.BimTools.ColorByParameter.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using WpfColor = System.Windows.Media.Color;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfGrid = System.Windows.Controls.Grid;

namespace TrueBIM.App.Modules.BimTools.ColorByParameter.UI;

public sealed class ColorByParameterWindow : Window
{
    private const int MaxValueCount = 40;
    private readonly Document document;
    private readonly View activeView;
    private readonly IReadOnlyList<BimCategoryItem> categories;
    private readonly ColorByParameterService service;
    private readonly ITrueBimLogger logger;
    private readonly ListBox categoryList = new();
    private readonly WpfComboBox parameterInput = new();
    private readonly ListBox valueList = new();
    private readonly TextBlock statusText = new();
    private List<BimParameterItem> parameters = [];
    private List<ColorRuleRow> rows = [];

    public ColorByParameterWindow(
        Document document,
        View activeView,
        IReadOnlyList<BimCategoryItem> categories,
        ColorByParameterService service,
        ITrueBimLogger logger)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        this.activeView = activeView ?? throw new ArgumentNullException(nameof(activeView));
        this.categories = categories ?? throw new ArgumentNullException(nameof(categories));
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Title = "Цвета по параметрам";
        Icon = IconFactory.CreateImage(TrueBimIcon.ColorByParameter, 32);
        Width = 980;
        Height = 700;
        MinWidth = 900;
        MinHeight = 620;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();
        RefreshCategoryList();
        LoadParameters();
    }

    private UIElement CreateContent()
    {
        WpfGrid root = new()
        {
            Margin = new Thickness(16)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(CreateHeader());

        WpfGrid main = new();
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        WpfGrid.SetRow(main, 1);
        root.Children.Add(main);

        UIElement categoriesPanel = CreateCategoriesPanel();
        WpfGrid.SetColumn(categoriesPanel, 0);
        main.Children.Add(categoriesPanel);

        UIElement valuesPanel = CreateValuesPanel();
        WpfGrid.SetColumn(valuesPanel, 1);
        main.Children.Add(valuesPanel);

        statusText.Foreground = Brushes.DimGray;
        statusText.Margin = new Thickness(0, 10, 0, 10);
        statusText.TextWrapping = TextWrapping.Wrap;
        WpfGrid.SetRow(statusText, 2);
        root.Children.Add(statusText);

        StackPanel footer = CreateFooter();
        WpfGrid.SetRow(footer, 3);
        root.Children.Add(footer);

        return root;
    }

    private StackPanel CreateHeader()
    {
        StackPanel header = new();
        header.Children.Add(new TextBlock
        {
            Text = "Цвета по параметрам",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = $"Активный вид: {activeView.Name}. Инструмент создаёт фильтры вида с префиксом BIM_F_ и применяет их только к текущему виду.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12)
        });
        return header;
    }

    private UIElement CreateCategoriesPanel()
    {
        WpfGrid panel = new()
        {
            Margin = new Thickness(0, 0, 12, 0)
        };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        panel.Children.Add(new TextBlock
        {
            Text = "Категории",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        StackPanel toolbar = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Button selectAllButton = CreateSmallButton("Все", (_, _) => SelectCategories(true));
        toolbar.Children.Add(selectAllButton);
        Button clearButton = CreateSmallButton("Снять", (_, _) => SelectCategories(false));
        clearButton.Margin = new Thickness(8, 0, 0, 0);
        toolbar.Children.Add(clearButton);
        Button refreshButton = CreateSmallButton("Параметры", (_, _) => LoadParameters());
        refreshButton.Margin = new Thickness(8, 0, 0, 0);
        toolbar.Children.Add(refreshButton);
        WpfGrid.SetRow(toolbar, 1);
        panel.Children.Add(toolbar);

        categoryList.BorderBrush = Brushes.LightGray;
        categoryList.BorderThickness = new Thickness(1);
        WpfGrid.SetRow(categoryList, 2);
        panel.Children.Add(categoryList);

        return panel;
    }

    private UIElement CreateValuesPanel()
    {
        WpfGrid panel = new();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        DockPanel parameterBar = new()
        {
            LastChildFill = true,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Button valueButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Preview, "Обновить значения"),
            Height = 30,
            MinWidth = 170,
            Margin = new Thickness(8, 0, 0, 0)
        };
        valueButton.Click += (_, _) => LoadValues();
        DockPanel.SetDock(valueButton, Dock.Right);
        parameterBar.Children.Add(valueButton);

        parameterInput.Height = 30;
        parameterInput.VerticalContentAlignment = VerticalAlignment.Center;
        parameterInput.ToolTip = "Параметр для раскраски выбранных категорий.";
        parameterInput.SelectionChanged += (_, _) => LoadValues();
        parameterBar.Children.Add(parameterInput);
        panel.Children.Add(parameterBar);

        StackPanel toolbar = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Button selectAllButton = CreateSmallButton("Все значения", (_, _) => SelectValues(true));
        toolbar.Children.Add(selectAllButton);
        Button clearButton = CreateSmallButton("Снять значения", (_, _) => SelectValues(false));
        clearButton.Margin = new Thickness(8, 0, 0, 0);
        toolbar.Children.Add(clearButton);
        Button colorsButton = CreateSmallButton("Сгенерировать цвета", (_, _) => RegenerateColors());
        colorsButton.Margin = new Thickness(8, 0, 0, 0);
        toolbar.Children.Add(colorsButton);
        WpfGrid.SetRow(toolbar, 1);
        panel.Children.Add(toolbar);

        valueList.BorderBrush = Brushes.LightGray;
        valueList.BorderThickness = new Thickness(1);
        valueList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        WpfGrid.SetRow(valueList, 2);
        panel.Children.Add(valueList);

        return panel;
    }

    private StackPanel CreateFooter()
    {
        StackPanel footer = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Button clearButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Очистить раскраску"),
            MinWidth = 170,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0)
        };
        clearButton.Click += (_, _) => ClearFilters();
        footer.Children.Add(clearButton);

        Button applyButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Apply, "Применить к активному виду"),
            MinWidth = 230,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0)
        };
        applyButton.Click += (_, _) => ApplyFilters();
        footer.Children.Add(applyButton);

        Button closeButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Закрыть"),
            MinWidth = 110,
            Height = 32,
            IsCancel = true
        };
        closeButton.Click += (_, _) => Close();
        footer.Children.Add(closeButton);

        return footer;
    }

    private void RefreshCategoryList()
    {
        categoryList.Items.Clear();
        foreach (BimCategoryItem category in categories)
        {
            CheckBox checkBox = new()
            {
                Content = category.DisplayName,
                IsChecked = category.IsSelected,
                Margin = new Thickness(8, 5, 8, 5),
                Tag = category
            };
            checkBox.Checked += (_, _) => category.IsSelected = true;
            checkBox.Unchecked += (_, _) => category.IsSelected = false;
            categoryList.Items.Add(checkBox);
        }

        UpdateStatus();
    }

    private void LoadParameters()
    {
        try
        {
            IReadOnlyList<BimCategoryItem> selectedCategories = GetSelectedCategories();
            if (selectedCategories.Count == 0)
            {
                parameters = [];
                parameterInput.ItemsSource = null;
                rows = [];
                RefreshValueList();
                statusText.Text = "Выберите хотя бы одну категорию.";
                return;
            }

            parameters = service.CollectParameters(document, activeView, selectedCategories).ToList();
            parameterInput.ItemsSource = parameters;
            parameterInput.DisplayMemberPath = nameof(BimParameterItem.DisplayName);
            parameterInput.SelectedIndex = parameters.Count > 0 ? 0 : -1;
            statusText.Text = parameters.Count == 0
                ? "Для выбранных категорий не найдено параметров, доступных для фильтра."
                : $"Категорий: {selectedCategories.Count}. Параметров: {parameters.Count}.";
        }
        catch (Exception exception)
        {
            logger.Error("Failed to collect Color By Parameter parameters.", exception);
            TaskDialog.Show("Цвета по параметрам", "Не удалось собрать параметры активного вида. Используйте логи для диагностики.");
        }
    }

    private void LoadValues()
    {
        try
        {
            if (parameterInput.SelectedItem is not BimParameterItem parameter)
            {
                rows = [];
                RefreshValueList();
                return;
            }

            ColorValueCollection collection = service.CollectValues(
                document,
                activeView,
                GetSelectedCategories(),
                parameter,
                MaxValueCount);
            rows = collection.Rows.ToList();
            RefreshValueList();
            statusText.Text = collection.WasTruncated
                ? $"Найдено уникальных значений: {collection.TotalValueCount}. Показаны первые {rows.Count}; сузьте категории перед применением."
                : $"Найдено уникальных значений: {rows.Count}.";
        }
        catch (Exception exception)
        {
            logger.Error("Failed to collect Color By Parameter values.", exception);
            TaskDialog.Show("Цвета по параметрам", "Не удалось собрать значения параметра. Используйте логи для диагностики.");
        }
    }

    private void RefreshValueList()
    {
        valueList.Items.Clear();
        foreach (ColorRuleRow row in rows)
        {
            valueList.Items.Add(CreateValueRow(row));
        }
    }

    private static UIElement CreateValueRow(ColorRuleRow row)
    {
        DockPanel panel = new()
        {
            LastChildFill = true,
            Margin = new Thickness(8, 6, 8, 6)
        };

        TextBlock hexText = new()
        {
            Text = row.ColorHex,
            Width = 80,
            Foreground = Brushes.DimGray,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(hexText, Dock.Right);
        panel.Children.Add(hexText);

        Border swatch = new()
        {
            Width = 26,
            Height = 18,
            Margin = new Thickness(0, 0, 8, 0),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(WpfColor.FromRgb(row.Red, row.Green, row.Blue)),
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(swatch, Dock.Left);
        panel.Children.Add(swatch);

        CheckBox checkBox = new()
        {
            Content = row.DisplayValue,
            IsChecked = row.IsSelected,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = row
        };
        checkBox.Checked += (_, _) => row.IsSelected = true;
        checkBox.Unchecked += (_, _) => row.IsSelected = false;
        panel.Children.Add(checkBox);

        return panel;
    }

    private void SelectCategories(bool isSelected)
    {
        foreach (BimCategoryItem category in categories)
        {
            category.IsSelected = isSelected;
        }

        RefreshCategoryList();
        LoadParameters();
    }

    private void SelectValues(bool isSelected)
    {
        foreach (ColorRuleRow row in rows)
        {
            row.IsSelected = isSelected;
        }

        RefreshValueList();
        UpdateStatus();
    }

    private void RegenerateColors()
    {
        service.AssignColors(rows);
        RefreshValueList();
        statusText.Text = $"Цвета пересчитаны для значений: {rows.Count}.";
    }

    private void ApplyFilters()
    {
        try
        {
            if (parameterInput.SelectedItem is not BimParameterItem parameter)
            {
                statusText.Text = "Выберите параметр.";
                return;
            }

            int selectedValueCount = rows.Count(row => row.IsSelected);
            if (selectedValueCount == 0)
            {
                statusText.Text = "Выберите хотя бы одно значение.";
                return;
            }

            if (selectedValueCount > 25 && !ConfirmLargeFilterSet(selectedValueCount))
            {
                return;
            }

            ColorApplyResult result = service.Apply(document, activeView, GetSelectedCategories(), parameter, rows);
            statusText.Text = $"Применено: {result.AppliedFilterCount}. Создано: {result.CreatedFilterCount}. Обновлено: {result.UpdatedFilterCount}. Пропущено: {result.SkippedValueCount}.";
            TaskDialog.Show("Цвета по параметрам", result.ToDialogText());
        }
        catch (Exception exception)
        {
            logger.Error("Failed to apply Color By Parameter filters.", exception);
            TaskDialog.Show("Цвета по параметрам", "Не удалось применить фильтры. Используйте логи для диагностики.");
        }
    }

    private void ClearFilters()
    {
        try
        {
            if (!ConfirmClear())
            {
                return;
            }

            ColorApplyResult result = service.Clear(document, activeView);
            statusText.Text = $"Очищено фильтров с активного вида: {result.ClearedFilterCount}.";
            TaskDialog.Show("Цвета по параметрам", result.ToDialogText());
        }
        catch (Exception exception)
        {
            logger.Error("Failed to clear Color By Parameter filters.", exception);
            TaskDialog.Show("Цвета по параметрам", "Не удалось очистить фильтры активного вида. Используйте логи для диагностики.");
        }
    }

    private IReadOnlyList<BimCategoryItem> GetSelectedCategories()
    {
        return categories.Where(category => category.IsSelected).ToList();
    }

    private void UpdateStatus()
    {
        statusText.Text = $"Категорий на активном виде: {categories.Count}. Выбрано: {categories.Count(category => category.IsSelected)}.";
    }

    private static bool ConfirmLargeFilterSet(int count)
    {
        TaskDialog dialog = new("Цвета по параметрам")
        {
            MainInstruction = $"Будет создано или обновлено фильтров: {count}.",
            MainContent = "Большое количество фильтров может замедлить активный вид. Продолжить?",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No
        };

        return dialog.Show() == TaskDialogResult.Yes;
    }

    private static bool ConfirmClear()
    {
        TaskDialog dialog = new("Цвета по параметрам")
        {
            MainInstruction = "Очистить раскраску TrueBIM с активного вида?",
            MainContent = "Будут сняты только фильтры, имя которых начинается с BIM_F_. Сами элементы фильтров в проекте не удаляются.",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No
        };

        return dialog.Show() == TaskDialogResult.Yes;
    }

    private static Button CreateSmallButton(string text, RoutedEventHandler clickHandler)
    {
        Button button = new()
        {
            Content = text,
            Height = 28,
            MinWidth = 90
        };
        button.Click += clickHandler;
        return button;
    }
}
