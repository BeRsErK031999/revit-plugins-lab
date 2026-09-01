using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Models;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Services;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.BimTools.ScheduleRegister.UI;

public sealed class ScheduleRegisterWindow : TrueBimWindow
{
    private readonly ScheduleRegisterSettingsStorage settingsStorage;
    private readonly IReadOnlyList<string> parameterNames;
    private readonly ScheduleRegisterTemplateInspection templateInspection;
    private readonly ObservableCollection<ScheduleRegisterSheetOption> sheets;
    private readonly ICollectionView sheetView;
    private readonly TextBox searchInput = TrueBimUi.CreateSearchBox("Поиск по номеру или имени листа");
    private readonly ContentControl templateStatusHost = new();
    private readonly ContentControl filterStatusHost = new();
    private readonly TextBlock footerStatus = new()
    {
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Button runButton;
    private ScheduleRegisterSettings currentSettings;
    private bool templateCanProceed;
    private bool updatingSelection;

    public ScheduleRegisterWindow(
        ScheduleRegisterSettingsStorage settingsStorage,
        IReadOnlyList<string> parameterNames,
        ScheduleRegisterTemplateInspection templateInspection,
        IReadOnlyList<ScheduleRegisterSheetOption> sheetOptions)
    {
        this.settingsStorage = settingsStorage ?? throw new ArgumentNullException(nameof(settingsStorage));
        this.parameterNames = parameterNames ?? throw new ArgumentNullException(nameof(parameterNames));
        this.templateInspection = templateInspection ?? throw new ArgumentNullException(nameof(templateInspection));
        Guard.NotNull(sheetOptions, nameof(sheetOptions));
        currentSettings = settingsStorage.Load();

        Title = "Ведомость спецификаций";
        Width = 980;
        Height = 740;
        MinWidth = 780;
        MinHeight = 600;
        ResizeMode = ResizeMode.CanResize;
        Icon = IconFactory.CreateImage(TrueBimIcon.ScheduleRegister, 32);

        sheets = new ObservableCollection<ScheduleRegisterSheetOption>(sheetOptions);
        foreach (ScheduleRegisterSheetOption sheet in sheets)
        {
            sheet.PropertyChanged += OnSheetPropertyChanged;
        }

        sheetView = CollectionViewSource.GetDefaultView(sheets);
        sheetView.Filter = MatchesSearch;
        searchInput.MinWidth = 300;
        AutomationProperties.SetName(searchInput, "Поиск листов");
        searchInput.TextChanged += (_, _) =>
        {
            sheetView.Refresh();
            RefreshState();
        };

        runButton = TrueBimUi.CreatePrimaryButton(
            "Создать ведомость",
            TrueBimIcon.Apply,
            Confirm,
            minWidth: 175);
        AutomationProperties.SetName(runButton, "Создать ведомость по выбранным листам");

        Button settingsButton = TrueBimUi.CreateSecondaryButton(
            "Настройки",
            TrueBimIcon.Settings,
            OpenSettings,
            minWidth: 125);
        Button guideButton = TrueBimUi.CreateSecondaryButton(
            "Методичка",
            TrueBimIcon.Help,
            OpenGuide,
            minWidth: 125);

        ApplyTrueBimShell(
            TrueBimUi.CreateHeader(
                "Создание ведомости спецификаций",
                "1. Отметьте листы.  2. Проверьте шаблон и фильтр.  3. Нажмите «Создать ведомость».",
                TrueBimIcon.ScheduleRegister),
            CreateCommandBar(),
            CreateBody(),
            null,
            TrueBimUi.CreateFooter(footerStatus, guideButton, settingsButton, runButton));

        Loaded += (_, _) => searchInput.Focus();
        RefreshState();
    }

    public IReadOnlyList<long> SelectedSheetIds => sheets
        .Where(sheet => sheet.IsSelected)
        .Select(sheet => sheet.SheetId)
        .ToArray();

    private UIElement CreateCommandBar()
    {
        Grid bar = new()
        {
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12)
        };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        StackPanel search = new();
        search.Children.Add(TrueBimUi.CreateFieldLabel("Поиск листов"));
        search.Children.Add(searchInput);
        bar.Children.Add(search);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        Button selectVisible = TrueBimUi.CreateSecondaryButton(
            "Выбрать найденные",
            TrueBimIcon.Apply,
            (_, _) => SetVisibleSelection(true),
            minWidth: 160);
        selectVisible.ToolTip = "Отметить все листы, оставшиеся после поиска.";
        actions.Children.Add(selectVisible);

        Button onlyWithSchedules = TrueBimUi.CreateSecondaryButton(
            "Только со спецификациями",
            TrueBimIcon.ScheduleRegister,
            (_, _) => SelectOnlyVisibleWithSchedules(),
            minWidth: 195);
        onlyWithSchedules.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        onlyWithSchedules.ToolTip = "Снять текущий выбор и отметить найденные листы, на которых есть спецификации.";
        actions.Children.Add(onlyWithSchedules);

        Button clear = TrueBimUi.CreateSecondaryButton(
            "Снять выбор",
            TrueBimIcon.Close,
            (_, _) => SetAllSelection(false),
            minWidth: 125);
        clear.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        actions.Children.Add(clear);
        Grid.SetColumn(actions, 1);
        bar.Children.Add(actions);
        return bar;
    }

    private UIElement CreateBody()
    {
        Grid body = new();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        StackPanel readiness = new();
        readiness.Children.Add(templateStatusHost);
        filterStatusHost.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        readiness.Children.Add(filterStatusHost);
        body.Children.Add(TrueBimUi.CreateSectionCard("Готовность", readiness));

        Border sheetCard = CreateSheetCard();
        sheetCard.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        Grid.SetRow(sheetCard, 1);
        body.Children.Add(sheetCard);
        return body;
    }

    private Border CreateSheetCard()
    {
        Grid content = new()
        {
            Margin = TrueBimTheme.SectionPadding
        };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(new TextBlock
        {
            Text = "Листы для обработки",
            FontSize = TrueBimTheme.SectionTitleFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12)
        });

        UIElement grid = CreateSheetGrid();
        Grid.SetRow(grid, 1);
        content.Children.Add(grid);
        return new Border
        {
            Background = TrueBimBrushes.Surface,
            BorderBrush = TrueBimBrushes.Border,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Child = content
        };
    }

    private UIElement CreateSheetGrid()
    {
        DataGrid grid = new()
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserReorderColumns = false,
            IsReadOnly = false,
            ItemsSource = sheetView,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            Style = TrueBimStyles.CreateDataGridStyle(),
            HeadersVisibility = DataGridHeadersVisibility.Column,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "Отмечайте листы флажками. Можно выделить несколько строк и нажать пробел."
        };
        ScrollViewer.SetCanContentScroll(grid, true);
        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        grid.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Space)
            {
                return;
            }

            ScheduleRegisterSheetOption[] selectedRows = grid.SelectedItems
                .Cast<ScheduleRegisterSheetOption>()
                .ToArray();
            bool nextValue = selectedRows.Any(row => !row.IsSelected);
            updatingSelection = true;
            try
            {
                foreach (ScheduleRegisterSheetOption row in selectedRows)
                {
                    row.IsSelected = nextValue;
                }
            }
            finally
            {
                updatingSelection = false;
            }

            RefreshState();
            args.Handled = true;
        };

        grid.Columns.Add(CreateSelectionColumn());
        grid.Columns.Add(CreateTextColumn("Номер", nameof(ScheduleRegisterSheetOption.SheetNumber), 130));
        grid.Columns.Add(CreateTextColumn(
            "Наименование листа",
            nameof(ScheduleRegisterSheetOption.SheetName),
            new DataGridLength(1, DataGridLengthUnitType.Star)));
        grid.Columns.Add(CreateTextColumn(
            "Размещено спецификаций",
            nameof(ScheduleRegisterSheetOption.PlacedSchedulesText),
            170));
        return grid;
    }

    private static DataGridTemplateColumn CreateSelectionColumn()
    {
        FrameworkElementFactory checkBox = new(typeof(CheckBox));
        checkBox.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        checkBox.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        checkBox.SetBinding(
            CheckBox.IsCheckedProperty,
            new Binding(nameof(ScheduleRegisterSheetOption.IsSelected))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        return new DataGridTemplateColumn
        {
            Header = "Выбран",
            Width = 75,
            CellTemplate = new DataTemplate { VisualTree = checkBox }
        };
    }

    private static DataGridTextColumn CreateTextColumn(string header, string path, double width)
    {
        return CreateTextColumn(header, path, new DataGridLength(width));
    }

    private static DataGridTextColumn CreateTextColumn(string header, string path, DataGridLength width)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path),
            Width = width,
            IsReadOnly = true
        };
    }

    private bool MatchesSearch(object item)
    {
        if (item is not ScheduleRegisterSheetOption sheet)
        {
            return false;
        }

        string search = searchInput.Text?.Trim() ?? string.Empty;
        return search.Length == 0
               || sheet.SheetNumber.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0
               || sheet.SheetName.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    private void SetVisibleSelection(bool isSelected)
    {
        updatingSelection = true;
        try
        {
            foreach (ScheduleRegisterSheetOption sheet in sheetView.Cast<ScheduleRegisterSheetOption>())
            {
                sheet.IsSelected = isSelected;
            }
        }
        finally
        {
            updatingSelection = false;
        }

        RefreshState();
    }

    private void SelectOnlyVisibleWithSchedules()
    {
        updatingSelection = true;
        try
        {
            foreach (ScheduleRegisterSheetOption sheet in sheets)
            {
                sheet.IsSelected = false;
            }

            foreach (ScheduleRegisterSheetOption sheet in sheetView
                         .Cast<ScheduleRegisterSheetOption>()
                         .Where(sheet => sheet.PlacedScheduleCount > 0))
            {
                sheet.IsSelected = true;
            }
        }
        finally
        {
            updatingSelection = false;
        }

        RefreshState();
    }

    private void SetAllSelection(bool isSelected)
    {
        updatingSelection = true;
        try
        {
            foreach (ScheduleRegisterSheetOption sheet in sheets)
            {
                sheet.IsSelected = isSelected;
            }
        }
        finally
        {
            updatingSelection = false;
        }

        RefreshState();
    }

    private void OnSheetPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ScheduleRegisterSheetOption.IsSelected))
        {
            if (!updatingSelection)
            {
                RefreshState();
            }
        }
    }

    private void OpenSettings(object sender, RoutedEventArgs args)
    {
        ScheduleRegisterSettingsWindow window = new(
            settingsStorage,
            parameterNames,
            templateInspection)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        window.ShowDialog();
        currentSettings = settingsStorage.Load();
        RefreshState();
    }

    private void OpenGuide(object sender, RoutedEventArgs args)
    {
        ScheduleRegisterGuideWindow guide = new()
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        guide.ShowDialog();
    }

    private void RefreshState()
    {
        IReadOnlyList<string> settingsIssues = ScheduleRegisterSettingsStorage.Validate(currentSettings);
        (string templateText, TrueBimUiSeverity severity, bool canProceed) = BuildTemplateStatus(currentSettings);
        templateCanProceed = canProceed && settingsIssues.Count == 0;
        templateStatusHost.Content = TrueBimUi.CreateInfoBanner(templateText, severity);

        string filterText = currentSettings.FilterEnabled
            ? $"Фильтр включён: исключать спецификации, если параметр «{currentSettings.FilterParameterName}» содержит «{currentSettings.ExcludedValue}»."
            : "Фильтр выключен: в ведомость попадут все подходящие спецификации с выбранных листов.";
        if (settingsIssues.Count > 0)
        {
            filterText = string.Join(" ", settingsIssues);
        }

        filterStatusHost.Content = TrueBimUi.CreateInfoBanner(
            filterText,
            settingsIssues.Count == 0 ? TrueBimUiSeverity.Info : TrueBimUiSeverity.Danger);

        int selectedCount = sheets.Count(sheet => sheet.IsSelected);
        int selectedPlacementCount = sheets
            .Where(sheet => sheet.IsSelected)
            .Sum(sheet => sheet.PlacedScheduleCount);
        int visibleCount = sheetView.Cast<object>().Count();
        if (sheets.Count == 0)
        {
            footerStatus.Text = "В проекте нет обычных листов для обработки.";
            footerStatus.Foreground = TrueBimBrushes.Danger;
        }
        else if (selectedCount == 0)
        {
            footerStatus.Text = $"Найдено листов: {visibleCount} из {sheets.Count}. Отметьте хотя бы один лист.";
            footerStatus.Foreground = TrueBimBrushes.Warning;
        }
        else
        {
            footerStatus.Text = $"Выбрано листов: {selectedCount}. На них размещено спецификаций: {selectedPlacementCount}.";
            footerStatus.Foreground = templateCanProceed
                ? TrueBimBrushes.TextSecondary
                : TrueBimBrushes.Danger;
        }

        runButton.IsEnabled = selectedCount > 0 && templateCanProceed;
    }

    private (string Text, TrueBimUiSeverity Severity, bool CanProceed) BuildTemplateStatus(
        ScheduleRegisterSettings settings)
    {
        if (templateInspection.IsValid)
        {
            return (
                $"Шаблон «{ScheduleRegisterConstants.TemplateScheduleName}» проверен и готов. Исходная таблица не изменится.",
                TrueBimUiSeverity.Success,
                true);
        }

        if (templateInspection.CanRepairLocally)
        {
            return (
                "В шаблоне заполнены рабочие строки или он размещён на листе. "
                + "При запуске TrueBIM автоматически создаст чистый шаблон с тем же оформлением и продолжит работу.",
                TrueBimUiSeverity.Warning,
                true);
        }

        if (ScheduleRegisterSettingsStorage.IsUsableTemplateProjectPath(settings.TemplateProjectPath))
        {
            return (
                "Структура шаблона повреждена или шаблон отсутствует. "
                + $"При запуске он будет восстановлен из «{settings.TemplateProjectPath}».",
                TrueBimUiSeverity.Warning,
                true);
        }

        return (
            "Структура шаблона повреждена или шаблон отсутствует. "
            + "Откройте «Настройки» и выберите эталонный проект .rvt или шаблон .rte.",
            TrueBimUiSeverity.Danger,
            false);
    }

    private void Confirm(object sender, RoutedEventArgs args)
    {
        RefreshState();
        if (!runButton.IsEnabled)
        {
            return;
        }

        DialogResult = true;
        Close();
    }
}
