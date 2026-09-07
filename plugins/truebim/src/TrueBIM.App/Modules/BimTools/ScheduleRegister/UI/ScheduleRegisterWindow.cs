using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
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
    private readonly Button readinessButton;
    private readonly Border readinessCard;
    private readonly Border sheetCard;
    private ScheduleRegisterSettings currentSettings;
    private bool templateCanProceed;
    private TrueBimUiSeverity readinessSeverity = TrueBimUiSeverity.Info;

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

        readinessCard = CreateReadinessCard();
        sheetCard = CreateSheetCard();
        readinessButton = CreateReadinessButton();

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
                "Проверьте листы, выбранные в диспетчере проекта, и нажмите «Создать ведомость».",
                TrueBimIcon.ScheduleRegister),
            CreateCommandBar(),
            CreateBody(),
            null,
            TrueBimUi.CreateFooter(footerStatus, guideButton, settingsButton, runButton));

        Loaded += (_, _) => searchInput.Focus();
        RefreshState();
    }

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

        readinessButton.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        readinessButton.HorizontalAlignment = HorizontalAlignment.Right;
        readinessButton.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(readinessButton, 1);
        bar.Children.Add(readinessButton);
        return bar;
    }

    private UIElement CreateBody()
    {
        Grid body = new();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        body.Children.Add(readinessCard);

        Grid.SetRow(sheetCard, 1);
        body.Children.Add(sheetCard);
        return body;
    }

    private Border CreateReadinessCard()
    {
        StackPanel readiness = new();
        readiness.Children.Add(templateStatusHost);
        filterStatusHost.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        readiness.Children.Add(filterStatusHost);
        Border card = TrueBimUi.CreateSectionCard("Готовность", readiness);
        card.Visibility = Visibility.Collapsed;
        return card;
    }

    private Button CreateReadinessButton()
    {
        Button button = new()
        {
            Width = TrueBimTheme.ControlHeight32,
            Height = TrueBimTheme.ControlHeight32,
            MinWidth = TrueBimTheme.ControlHeight32,
            Padding = new Thickness(0),
            Style = TrueBimStyles.CreateButtonStyle()
        };
        button.Click += ToggleReadiness;
        AutomationProperties.SetName(button, "Показать готовность");
        return button;
    }

    private void ToggleReadiness(object sender, RoutedEventArgs args)
    {
        bool showDetails = readinessCard.Visibility != Visibility.Visible;
        readinessCard.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
        sheetCard.Margin = showDetails
            ? new Thickness(0, TrueBimTheme.Spacing12, 0, 0)
            : new Thickness(0);
        UpdateReadinessButton();
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
            IsReadOnly = true,
            ItemsSource = sheetView,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            Style = TrueBimStyles.CreateDataGridStyle(),
            HeadersVisibility = DataGridHeadersVisibility.Column,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "Список листов, выбранных в диспетчере проекта перед запуском команды."
        };
        ScrollViewer.SetCanContentScroll(grid, true);
        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
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

        readinessSeverity = settingsIssues.Count > 0
            ? TrueBimUiSeverity.Danger
            : severity;
        UpdateReadinessButton();

        int sheetCount = sheets.Count;
        int placementCount = sheets.Sum(sheet => sheet.PlacedScheduleCount);
        int visibleCount = sheetView.Cast<object>().Count();
        if (sheetCount == 0)
        {
            footerStatus.Text = "В диспетчере проекта не выбраны листы для обработки.";
            footerStatus.Foreground = TrueBimBrushes.Danger;
        }
        else
        {
            string searchStatus = visibleCount == sheetCount
                ? string.Empty
                : $" Показано по поиску: {visibleCount} из {sheetCount}.";
            string readinessStatus = templateCanProceed
                ? string.Empty
                : " Готовность требует внимания — откройте её кнопкой рядом с поиском.";
            footerStatus.Text = $"Выбрано в диспетчере листов: {sheetCount}. "
                                + $"Размещено спецификаций: {placementCount}."
                                + searchStatus
                                + readinessStatus;
            footerStatus.Foreground = templateCanProceed
                ? TrueBimBrushes.TextSecondary
                : TrueBimBrushes.Danger;
        }

        runButton.IsEnabled = sheetCount > 0 && templateCanProceed;
    }

    private void UpdateReadinessButton()
    {
        TrueBimIcon icon = readinessSeverity switch
        {
            TrueBimUiSeverity.Success => TrueBimIcon.Check,
            TrueBimUiSeverity.Warning => TrueBimIcon.Warning,
            TrueBimUiSeverity.Danger => TrueBimIcon.Error,
            _ => TrueBimIcon.Info
        };
        bool detailsVisible = readinessCard.Visibility == Visibility.Visible;
        string status = readinessSeverity switch
        {
            TrueBimUiSeverity.Success => "всё готово",
            TrueBimUiSeverity.Warning => "есть предупреждение",
            TrueBimUiSeverity.Danger => "требуется настройка",
            _ => "информация"
        };
        string action = detailsVisible ? "Скрыть подробности" : "Показать подробности";
        readinessButton.Content = IconFactory.Create(
            icon,
            TrueBimBrushes.ForSeverity(readinessSeverity).Color,
            TrueBimTheme.IconSizeSmall);
        readinessButton.ToolTip = $"Готовность: {status}. {action}.";
        AutomationProperties.SetName(readinessButton, $"Готовность: {status}. {action}");
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
