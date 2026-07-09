using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.Modules.BimTools.ColorByParameter.Models;
using TrueBIM.App.Modules.BimTools.ColorByParameter.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;
using WpfColor = System.Windows.Media.Color;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfGrid = System.Windows.Controls.Grid;

namespace TrueBIM.App.Modules.BimTools.ColorByParameter.UI;

public sealed class ColorByParameterWindow : TrueBimWindow
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
    private int colorGenerationOffset;

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
        ApplySharedControlStyles();
        Content = CreateContent();
        RefreshCategoryList();
        LoadParameters();
    }

    private UIElement CreateContent()
    {
        return BuildShell(
            header: TrueBimUi.CreateHeader(
                "Цвета по параметрам",
                $"Активный вид: {activeView.Name}. Фильтры BIM_F_ создаются только для текущего вида.",
                TrueBimIcon.ColorByParameter),
            commandBar: null,
            body: CreateMainPanel(),
            status: CreateStatus(),
            footer: CreateFooter());
    }

    private UIElement CreateMainPanel()
    {
        WpfGrid main = new();
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        UIElement categoriesPanel = CreateCategoriesPanel();
        WpfGrid.SetColumn(categoriesPanel, 0);
        main.Children.Add(categoriesPanel);

        UIElement valuesPanel = CreateValuesPanel();
        WpfGrid.SetColumn(valuesPanel, 1);
        main.Children.Add(valuesPanel);

        return main;
    }

    private UIElement CreateStatus()
    {
        statusText.Foreground = TrueBimBrushes.TextPrimary;
        statusText.TextWrapping = TextWrapping.Wrap;
        return TrueBimUi.CreateInfoBanner(statusText, TrueBimUiSeverity.Info);
    }

    private UIElement CreateCategoriesPanel()
    {
        WpfGrid panel = new()
        {
            Margin = TrueBimTheme.SectionPadding
        };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        panel.Children.Add(new TextBlock
        {
            Text = "Категории",
            FontSize = TrueBimTheme.SectionTitleFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        });

        StackPanel toolbar = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };
        Button selectAllButton = CreateSmallButton("Все", TrueBimIcon.Check, (_, _) => SelectCategories(true), 72);
        toolbar.Children.Add(selectAllButton);
        Button clearButton = CreateSmallButton("Снять", TrueBimIcon.Close, (_, _) => SelectCategories(false), 80);
        clearButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        toolbar.Children.Add(clearButton);
        Button refreshButton = CreateSmallButton("Обновить", TrueBimIcon.Refresh, (_, _) => LoadParameters(), 112);
        refreshButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        refreshButton.ToolTip = "Обновить список параметров проекта для выбранных категорий.";
        toolbar.Children.Add(refreshButton);
        WpfGrid.SetRow(toolbar, 1);
        panel.Children.Add(toolbar);

        categoryList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        WpfGrid.SetRow(categoryList, 2);
        panel.Children.Add(categoryList);

        return new Border
        {
            Background = TrueBimBrushes.Surface,
            BorderBrush = TrueBimBrushes.Border,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing12, 0),
            Child = panel
        };
    }

    private UIElement CreateValuesPanel()
    {
        WpfGrid panel = new()
        {
            Margin = TrueBimTheme.SectionPadding
        };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        panel.Children.Add(new TextBlock
        {
            Text = "Значения параметра",
            FontSize = TrueBimTheme.SectionTitleFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        });

        DockPanel parameterBar = new()
        {
            LastChildFill = true,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };
        Button valueButton = TrueBimUi.CreateSecondaryButton("Обновить значения", TrueBimIcon.Refresh, minWidth: 170);
        valueButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        valueButton.Click += (_, _) => LoadValues();
        DockPanel.SetDock(valueButton, Dock.Right);
        parameterBar.Children.Add(valueButton);

        parameterInput.MinHeight = TrueBimTheme.ControlHeight32;
        parameterInput.VerticalContentAlignment = VerticalAlignment.Center;
        parameterInput.ToolTip = "Параметр проекта, по значениям которого будет создана раскраска.";
        parameterInput.SelectionChanged += (_, _) => LoadValues();
        parameterBar.Children.Add(parameterInput);
        WpfGrid.SetRow(parameterBar, 1);
        panel.Children.Add(parameterBar);

        StackPanel toolbar = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };
        Button selectAllButton = CreateSmallButton("Все значения", TrueBimIcon.Check, (_, _) => SelectValues(true), 130);
        toolbar.Children.Add(selectAllButton);
        Button clearButton = CreateSmallButton("Снять значения", TrueBimIcon.Close, (_, _) => SelectValues(false), 142);
        clearButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        toolbar.Children.Add(clearButton);
        Button colorsButton = CreateSmallButton("Сгенерировать цвета", TrueBimIcon.Refresh, (_, _) => RegenerateColors(), 170);
        colorsButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        toolbar.Children.Add(colorsButton);
        WpfGrid.SetRow(toolbar, 2);
        panel.Children.Add(toolbar);

        valueList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        WpfGrid.SetRow(valueList, 3);
        panel.Children.Add(valueList);

        return new Border
        {
            Background = TrueBimBrushes.Surface,
            BorderBrush = TrueBimBrushes.Border,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Child = panel
        };
    }

    private UIElement CreateFooter()
    {
        Button clearButton = TrueBimUi.CreateDangerButton("Очистить раскраску", TrueBimIcon.Close, minWidth: 170);
        clearButton.Click += (_, _) => ClearFilters();

        Button applyButton = TrueBimUi.CreatePrimaryButton("Применить к активному виду", TrueBimIcon.Apply, minWidth: 230);
        applyButton.Click += (_, _) => ApplyFilters();

        Button closeButton = TrueBimUi.CreateSecondaryButton("Закрыть", TrueBimIcon.Close);
        closeButton.IsCancel = true;
        closeButton.Click += (_, _) => Close();

        return TrueBimUi.CreateFooter(null, clearButton, applyButton, closeButton);
    }

    private void ApplySharedControlStyles()
    {
        categoryList.Style = TrueBimStyles.CreateListBoxStyle();
        valueList.Style = TrueBimStyles.CreateListBoxStyle();
        parameterInput.Style = TrueBimStyles.CreateComboBoxStyle();
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
                Margin = new Thickness(TrueBimTheme.Spacing8, TrueBimTheme.Spacing4, TrueBimTheme.Spacing8, TrueBimTheme.Spacing4),
                Style = TrueBimStyles.CreateCheckBoxStyle(),
                Tag = category
            };
            checkBox.Checked += (_, _) => UpdateCategorySelection(category, true);
            checkBox.Unchecked += (_, _) => UpdateCategorySelection(category, false);
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
            rows = [];
            RefreshValueList();
            parameterInput.ItemsSource = parameters;
            parameterInput.DisplayMemberPath = nameof(BimParameterItem.DisplayName);
            parameterInput.SelectedIndex = parameters.Count > 0 ? 0 : -1;
            statusText.Text = parameters.Count == 0
                ? "Для выбранных категорий не найдено параметров проекта, доступных для фильтра."
                : $"Категорий: {selectedCategories.Count}. Параметров проекта: {parameters.Count}.";
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
            colorGenerationOffset = 0;
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
            Margin = new Thickness(TrueBimTheme.Spacing8, TrueBimTheme.Spacing4, TrueBimTheme.Spacing8, TrueBimTheme.Spacing4)
        };

        System.Windows.Controls.TextBox hexInput = new()
        {
            Text = row.ColorHex,
            Width = 88,
            MinHeight = TrueBimTheme.ControlHeight32,
            Foreground = TrueBimBrushes.TextSecondary,
            Style = TrueBimStyles.CreateTextBoxStyle(),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Введите цвет вручную в формате #RRGGBB."
        };
        DockPanel.SetDock(hexInput, Dock.Right);
        panel.Children.Add(hexInput);

        Border swatch = new()
        {
            Width = 26,
            Height = 18,
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, 0),
            BorderBrush = TrueBimBrushes.Border,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            Background = new SolidColorBrush(WpfColor.FromRgb(row.Red, row.Green, row.Blue)),
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(swatch, Dock.Left);
        panel.Children.Add(swatch);

        hexInput.LostFocus += (_, _) => ApplyManualColor(row, hexInput, swatch);
        hexInput.KeyDown += (_, args) =>
        {
            if (args.Key == System.Windows.Input.Key.Enter)
            {
                ApplyManualColor(row, hexInput, swatch);
                args.Handled = true;
            }
        };

        CheckBox checkBox = new()
        {
            Content = row.DisplayValue,
            IsChecked = row.IsSelected,
            VerticalAlignment = VerticalAlignment.Center,
            Style = TrueBimStyles.CreateCheckBoxStyle(),
            Tag = row
        };
        checkBox.Checked += (_, _) => row.IsSelected = true;
        checkBox.Unchecked += (_, _) => row.IsSelected = false;
        panel.Children.Add(checkBox);

        return panel;
    }

    private void UpdateCategorySelection(BimCategoryItem category, bool isSelected)
    {
        category.IsSelected = isSelected;
        LoadParameters();
    }

    private static void ApplyManualColor(ColorRuleRow row, System.Windows.Controls.TextBox input, Border swatch)
    {
        if (!row.TrySetColorHex(input.Text))
        {
            input.Text = row.ColorHex;
            return;
        }

        input.Text = row.ColorHex;
        swatch.Background = new SolidColorBrush(WpfColor.FromRgb(row.Red, row.Green, row.Blue));
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
        if (rows.Count == 0)
        {
            statusText.Text = "Сначала выберите параметр и обновите значения.";
            return;
        }

        colorGenerationOffset++;
        service.AssignColors(rows, colorGenerationOffset);
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

    private static Button CreateSmallButton(string text, TrueBimIcon icon, RoutedEventHandler clickHandler, double minWidth = 96)
    {
        Button button = TrueBimUi.CreateSecondaryButton(text, icon, minWidth: minWidth);
        button.Click += clickHandler;
        return button;
    }
}
