using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TrueBIM.App.Modules.Print.Services;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;
using RevitDocument = Autodesk.Revit.DB.Document;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using TaskDialogCommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons;
using TaskDialogResult = Autodesk.Revit.UI.TaskDialogResult;

namespace TrueBIM.App.Modules.Print.UI;

public sealed class PrintWindow : TrueBimWindow
{
    private const double ExportLabelWidth = 132;
    private const string PlaceholderSheetExplanation =
        "Предварительный лист из ведомости, для которого обычный лист Revit еще не создан. Он отображается только для контроля и не включается в печать.";

    private IReadOnlyList<PrintSheetRow> sheetRows = Array.Empty<PrintSheetRow>();
    private readonly ObservableCollection<PrintSheetSourceFilterOption> sourceFilterOptions = new();
    private readonly IReadOnlyList<PrintSheetSource> sheetSources;
    private readonly Dictionary<string, PrintSheetSource> sheetSourcesById;
    private readonly IReadOnlyDictionary<string, int> sheetSourceOrderById;
    private readonly Dictionary<string, List<PrintSheetInfo>> sourceSheetsById;
    private readonly HashSet<string> loadedSourceIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> loadedSheetParameterNamesBySourceId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> loadedTitleBlockParameterNamesBySourceId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> loadedProjectParameterNamesBySourceId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PrintParameterCatalog> parameterCatalogsBySourceId;
    private readonly Dictionary<string, PrintFileNameContext> fileNameContextsBySourceId;
    private readonly PrintSheetSelectionState sheetSelectionState;
    private readonly RevitDocument document;
    private readonly ITrueBimLogger logger;
    private readonly RevitActionDispatcher revitActions;
    private readonly PrintFileNameTemplateService fileNameTemplateService = new();
    private readonly PrintFileNameTokenCatalogService fileNameTokenCatalogService = new();
    private readonly PrintPdfExportService pdfExportService = new();
    private readonly PrintCadExportService cadExportService = new();
    private readonly PrintCadExportSetupService cadExportSetupService = new();
    private readonly DwgExportOptionsFactory dwgOptionsFactory = new();
    private readonly PrintDriverCatalogService printDriverCatalogService = new();
    private readonly PrintManagerService printManagerService = new();
    private readonly ObservableCollection<PrintCadExportSetupOption> cadExportSetupOptions = new();
    private readonly ObservableCollection<PrintPrinterOption> printerOptions = new();
    private readonly ObservableCollection<PrintSetupOption> printSetupOptions = new();
    private readonly PrintSettingsService? printSettingsService;
    private readonly DwgExportProfileStorage dwgProfileStorage;
    private readonly PrintPresetStorage printPresetStorage;
    private readonly PrintSettings initialSettings;
    private readonly string collectedFileNameMask;
    private readonly string collectedCombinedPdfFileNameMask;
    private readonly string collectedCombinedDwgFileNameMask;
    private readonly bool hasSavedPrintSettings;
    private readonly PrintFileNameContext fileNameContext;
    private readonly DataGrid sheetGrid = new();
    private readonly TextBlock statusText = new();
    private readonly ComboBox presetInput = new()
    {
        DisplayMemberPath = nameof(PrintPreset.Name),
        IsEditable = true,
        IsTextSearchEnabled = true,
        MinWidth = 260,
        Height = 32,
        ToolTip = "Выберите готовый пресет или введите имя нового пресета заказчика."
    };
    private readonly ComboBox sourceFilterInput = new()
    {
        DisplayMemberPath = nameof(PrintSheetSourceFilterOption.DisplayName),
        Height = 32,
        MinWidth = 220,
        ToolTip = "Фильтр листов по открытому документу Revit."
    };
    private readonly ComboBox operationModeInput = new()
    {
        DisplayMemberPath = nameof(PrintOperationModeOption.DisplayName),
        SelectedValuePath = nameof(PrintOperationModeOption.Mode),
        Height = 32,
        MinWidth = 180,
        ToolTip = "Экспорт создает файлы, печать отправляет выбранные листы в установленный принтер."
    };
    private readonly ComboBox printerInput = new()
    {
        DisplayMemberPath = nameof(PrintPrinterOption.DisplayName),
        Height = 32,
        MinWidth = 240,
        ToolTip = "Установленный в Windows принтер для фактической печати листов."
    };
    private readonly ComboBox printSetupInput = new()
    {
        DisplayMemberPath = nameof(PrintSetupOption.DisplayName),
        Height = 32,
        MinWidth = 220,
        ToolTip = "Сохраненная настройка печати Revit. По умолчанию используется текущая настройка каждого документа."
    };
    private readonly TextBlock printerWarningText = new()
    {
        Text = "PDF-драйвер может открыть собственный диалог или запросить имя файла. Для управляемого PDF используйте режим «Экспорт».",
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = TrueBimBrushes.Warning
    };
    private readonly TextBox exportFolderInput = new();
    private readonly TextBox fileNameMaskInput = new()
    {
        Text = PrintFileNameTemplateService.DefaultTemplate,
        ToolTip = "Маска имени файла. Кнопка «Токены» вставляет системные токены и параметры листа, основной надписи или проекта в позицию курсора. Старые английские токены тоже поддерживаются."
    };
    private readonly CheckBox includePlaceholdersInput = new()
    {
        Content = "Неразмещенные листы (заглушки)",
        ToolTip = PlaceholderSheetExplanation
    };
    private readonly TextBox combinedPdfNameInput = new()
    {
        Text = PrintFileNameTemplateService.DefaultCombinedTemplate,
        ToolTip = "Маска общего PDF. Поддерживает те же токены, что маска листов; параметры листа и основной надписи берутся из первого выбранного листа каждого документа."
    };
    private readonly TextBlock combinedPdfNamePreviewText = new()
    {
        Text = "Выберите листы",
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBox combinedDwgNameMaskInput = new()
    {
        Text = PrintFileNameTemplateService.DefaultCombinedTemplate,
        ToolTip = "Маска общего DWG. Поддерживает те же токены, что маска листов; параметры листа и основной надписи берутся из первого выбранного листа каждого документа."
    };
    private readonly TextBlock combinedDwgNamePreviewText = new()
    {
        Text = "Выберите листы",
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly ComboBox pdfColorModeInput = CreatePdfSettingInput("Цветовой режим PDF.");
    private readonly ComboBox pdfRasterQualityInput = CreatePdfSettingInput("Качество растровых элементов PDF.");
    private readonly CheckBox forceRasterPdfInput = new()
    {
        Content = "Всегда растр",
        ToolTip = "Принудительно растрировать PDF вместо векторного вывода."
    };
    private readonly CheckBox pdfInput = new()
    {
        Content = "PDF",
        ToolTip = "Добавить PDF в очередь экспорта."
    };
    private readonly CheckBox combinePdfInput = new()
    {
        Content = "Один PDF",
        ToolTip = "Объединить выбранные листы каждого документа-источника в один PDF."
    };
    private readonly CheckBox dwgInput = new()
    {
        Content = "DWG",
        ToolTip = "Добавить DWG в очередь экспорта."
    };
    private readonly CheckBox dxfInput = new()
    {
        Content = "DXF",
        ToolTip = "Добавить DXF в очередь экспорта."
    };
    private readonly CheckBox dwfInput = new()
    {
        Content = "DWF",
        ToolTip = "Добавить DWF в очередь экспорта."
    };
    private readonly CheckBox combineDwgInput = new()
    {
        Content = "Один DWG",
        ToolTip = "Экспортировать выбранные DWG в один файл через MergedViews."
    };
    private readonly CheckBox openExportFolderInput = new()
    {
        Content = "Открыть папку",
        ToolTip = "Открыть папку результата после завершения, если создан хотя бы один файл."
    };
    private readonly ComboBox dwgSetupInput = CreateCadSetupInput("Настройка экспорта DWG из сохраненных настроек Revit.");
    private readonly ComboBox dxfSetupInput = CreateCadSetupInput("Настройка экспорта DXF из сохраненных настроек Revit.");
    private readonly TextBlock dwgProfileSourceText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Button exportButton = TrueBimUi.CreatePrimaryButton("Печать", TrueBimIcon.Print, isEnabled: false);
    private readonly List<UIElement> detailedSettingsRows = [];
    private readonly List<UIElement> exportModeRows = [];
    private readonly List<UIElement> printerModeRows = [];
    private UIElement? printerWarningRow;
    private DataGridColumn? fileNameColumn;
    private DwgExportProfileStoreState dwgProfileState = new();
    private PrintPresetStoreState printPresetState = new();
    private DwgExportProfile selectedDwgProfile = DwgExportOptionsFactory.CreateProfileFromOptions(
        DwgExportProfile.DefaultProfileName,
        sourceRevitSetupName: null,
        usePredefinedRevitSetup: false,
        isUserProfile: false,
        new Autodesk.Revit.DB.DWGExportOptions());
    private bool isApplyingDwgProfileToSetupInput;
    private bool isApplyingPreset;
    private bool isBatchUpdatingSelection;
    private bool isSheetNumberSortDescending;
    private bool reloadInitialSourcesForPreset;
    private bool showDetailedExportSettings;
    private PrintSheetRow? sheetSelectionAnchor;

    private const string WindowTitle = "Печать и экспорт";

    public PrintWindow(RevitDocument document, IReadOnlyList<PrintSheetInfo> sheets, ITrueBimLogger logger)
        : this(document, CreateSingleSource(document, sheets), printSettingsService: null, logger)
    {
    }

    public PrintWindow(RevitDocument document, IReadOnlyList<PrintSheetSource> sheetSources, ITrueBimLogger logger)
        : this(document, sheetSources, printSettingsService: null, logger)
    {
    }

    public PrintWindow(
        RevitDocument document,
        IReadOnlyList<PrintSheetSource> sheetSources,
        PrintSettingsService? printSettingsService,
        ITrueBimLogger logger,
        string? collectedFileNameMask = null,
        string? collectedCombinedPdfFileNameMask = null,
        string? collectedCombinedDwgFileNameMask = null)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        this.sheetSources = sheetSources ?? throw new ArgumentNullException(nameof(sheetSources));
        sheetSourceOrderById = this.sheetSources
            .Select((source, index) => new { source.SourceId, Index = index })
            .ToDictionary(item => item.SourceId, item => item.Index, StringComparer.Ordinal);
        sourceSheetsById = this.sheetSources.ToDictionary(
            source => source.SourceId,
            source =>
            {
                List<PrintSheetInfo> sourceSheets = source.Sheets.ToList();
                if (sourceSheets.Count > 0)
                {
                    loadedSourceIds.Add(source.SourceId);
                }

                return sourceSheets;
            },
            StringComparer.Ordinal);
        parameterCatalogsBySourceId = this.sheetSources.ToDictionary(
            source => source.SourceId,
            source => source.AvailableParameters,
            StringComparer.Ordinal);
        sheetSelectionState = new PrintSheetSelectionState(GetAllLoadedSheets());
        sheetSourcesById = this.sheetSources.ToDictionary(source => source.SourceId, StringComparer.Ordinal);
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        revitActions = new RevitActionDispatcher("печать и экспорт", this.logger);
        this.printSettingsService = printSettingsService;
        hasSavedPrintSettings = printSettingsService?.SettingsFileExists == true;
        initialSettings = printSettingsService?.Load() ?? PrintSettingsService.DefaultSettings;
        this.collectedFileNameMask = string.IsNullOrWhiteSpace(collectedFileNameMask)
            ? initialSettings.FileNameMask
            : collectedFileNameMask!;
        this.collectedCombinedPdfFileNameMask = string.IsNullOrWhiteSpace(collectedCombinedPdfFileNameMask)
            ? initialSettings.CombinedPdfFileName
            : collectedCombinedPdfFileNameMask!;
        this.collectedCombinedDwgFileNameMask = string.IsNullOrWhiteSpace(collectedCombinedDwgFileNameMask)
            ? initialSettings.CombinedDwgFileNameMask
            : collectedCombinedDwgFileNameMask!;
        IReadOnlyCollection<string> collectedSheetParameterNames = fileNameTemplateService.GetSheetParameterNames(
            this.collectedFileNameMask,
            this.collectedCombinedPdfFileNameMask,
            this.collectedCombinedDwgFileNameMask);
        IReadOnlyCollection<string> collectedTitleBlockParameterNames = fileNameTemplateService.GetTitleBlockParameterNames(
            this.collectedFileNameMask,
            this.collectedCombinedPdfFileNameMask,
            this.collectedCombinedDwgFileNameMask);
        IReadOnlyCollection<string> collectedProjectParameterNames = fileNameTemplateService.GetProjectParameterNames(
            this.collectedFileNameMask,
            this.collectedCombinedPdfFileNameMask,
            this.collectedCombinedDwgFileNameMask);
        foreach (string sourceId in loadedSourceIds)
        {
            loadedSheetParameterNamesBySourceId[sourceId] = collectedSheetParameterNames.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            loadedTitleBlockParameterNamesBySourceId[sourceId] = collectedTitleBlockParameterNames.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            loadedProjectParameterNamesBySourceId[sourceId] = collectedProjectParameterNames.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        }

        fileNameContext = CreateFileNameContext(document, collectedProjectParameterNames);
        fileNameContextsBySourceId = new Dictionary<string, PrintFileNameContext>(StringComparer.Ordinal);
        foreach (PrintSheetSource source in this.sheetSources.Where(source => loadedSourceIds.Contains(source.SourceId)))
        {
            fileNameContextsBySourceId[source.SourceId] = ReferenceEquals(source.Document, document)
                ? fileNameContext
                : CreateFileNameContext(source.Document, collectedProjectParameterNames);
        }

        string revitVersion = GetRevitVersion(document);
        dwgProfileStorage = new DwgExportProfileStorage(
            DwgExportProfileStorage.CreateStoragePath(revitVersion),
            logger);
        dwgProfileState = dwgProfileStorage.Load();
        printPresetStorage = new PrintPresetStorage(
            PrintPresetStorage.CreateStoragePath(revitVersion),
            logger);
        printPresetState = printPresetStorage.Load();

        LoadSourceFilterOptions();
        LoadCadExportSetupOptions();
        LoadPrintOptions();

        LoadInitialDwgProfile();
        ApplyInitialSettings();

        Title = WindowTitle;
        Icon = IconFactory.CreateImage(TrueBimIcon.Print, 32);
        exportButton.Content = IconFactory.CreateButtonContent(
            TrueBimIcon.Export,
            "Экспортировать",
            Colors.White);
        Width = 1120;
        Height = 720;
        MinWidth = 980;
        MinHeight = 620;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ApplySharedControlStyles();
        Content = CreateContent();

        InitializePrintPresets();
        LoadSheets(reloadSources: reloadInitialSourcesForPreset);
        logger.Info($"{WindowTitle} window opened for '{document.Title}' with {GetAllLoadedSheets().Count} loaded sheets from {this.sheetSources.Count} sources.");
    }

    protected override void OnClosed(EventArgs e)
    {
        SavePrintSettings();
        base.OnClosed(e);
    }

    private UIElement CreateContent()
    {
        return BuildShell(
            header: TrueBimUi.CreateHeader(
                WindowTitle,
                "Выберите листы, затем явно укажите: создать файлы или отправить листы в принтер.",
                TrueBimIcon.Print),
            commandBar: CreateReadSettings(),
            body: CreateSheetsSection(),
            status: CreateStatus(),
            footer: CreateExportSettings());
    }

    private UIElement CreateSheetsSection()
    {
        Grid section = new()
        {
            Margin = TrueBimTheme.SectionPadding
        };
        section.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        section.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        section.Children.Add(new TextBlock
        {
            Text = "Листы для печати и экспорта",
            FontSize = TrueBimTheme.SectionTitleFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12)
        });

        UIElement grid = CreateSheetGrid();
        Grid.SetRow(grid, 1);
        section.Children.Add(grid);

        return new Border
        {
            Background = TrueBimBrushes.Surface,
            BorderBrush = TrueBimBrushes.Border,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Child = section
        };
    }

    private UIElement CreateReadSettings()
    {
        Grid controls = new()
        {
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12)
        };
        controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        StackPanel selectionActions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0)
        };

        Button selectAllButton = CreateActionButton("Выбрать все", TrueBimIcon.Apply, isEnabled: true);
        selectAllButton.Margin = new Thickness(0, 0, 8, 0);
        selectAllButton.ToolTip = "Отметить все печатаемые листы.";
        selectAllButton.Click += (_, _) => SetAllSelected(isSelected: true);
        selectionActions.Children.Add(selectAllButton);

        Button clearSelectionButton = CreateActionButton("Снять выбор", TrueBimIcon.Close, isEnabled: true);
        clearSelectionButton.Margin = new Thickness(0, 0, 8, 0);
        clearSelectionButton.ToolTip = "Снять отметки со всех листов.";
        clearSelectionButton.Click += (_, _) => SetAllSelected(isSelected: false);
        selectionActions.Children.Add(clearSelectionButton);

        Button refreshButton = CreateActionButton("Обновить", TrueBimIcon.Refresh, isEnabled: true);
        refreshButton.Margin = new Thickness(0, 0, TrueBimTheme.Spacing16, 0);
        refreshButton.ToolTip = "Перечитать листы и параметры, используемые текущей маской имени.";
        refreshButton.Click += (_, _) => RequestReloadSheets();
        selectionActions.Children.Add(refreshButton);

        includePlaceholdersInput.VerticalAlignment = VerticalAlignment.Center;
        includePlaceholdersInput.Checked += (_, _) => RequestLoadSheetsUnlessApplyingPreset();
        includePlaceholdersInput.Unchecked += (_, _) => RequestLoadSheetsUnlessApplyingPreset();
        includePlaceholdersInput.Margin = new Thickness(0, 0, TrueBimTheme.Spacing16, 0);
        selectionActions.Children.Add(includePlaceholdersInput);

        sourceFilterInput.ItemsSource = sourceFilterOptions;
        sourceFilterInput.SelectedItem = FindActiveSourceFilterOption() ?? sourceFilterOptions.FirstOrDefault();
        sourceFilterInput.IsEnabled = sourceFilterOptions.Count > 1;
        sourceFilterInput.SelectionChanged += (_, _) => RequestLoadSheets();
        if (sheetSources.Count > 1)
        {
            selectionActions.Children.Add(sourceFilterInput);
        }

        StackPanel presetActions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        presetActions.Children.Add(new TextBlock
        {
            Text = "Пресет",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        presetInput.ItemsSource = printPresetState.Presets;
        presetInput.SelectionChanged += (_, _) => ApplySelectedPrintPreset();
        presetActions.Children.Add(presetInput);

        Button savePresetButton = CreateActionButton("Сохранить", TrueBimIcon.Apply, isEnabled: true);
        savePresetButton.Margin = new Thickness(8, 0, 0, 0);
        savePresetButton.ToolTip = "Сохранить текущие форматы и настройки под именем из поля пресета.";
        savePresetButton.Click += (_, _) => SaveCurrentPrintPreset();
        presetActions.Children.Add(savePresetButton);

        Button deletePresetButton = CreateActionButton("Удалить", TrueBimIcon.Close, isEnabled: true);
        deletePresetButton.Margin = new Thickness(8, 0, 0, 0);
        deletePresetButton.ToolTip = "Удалить выбранный локальный пресет.";
        deletePresetButton.Click += (_, _) => DeleteSelectedPrintPreset();
        presetActions.Children.Add(deletePresetButton);

        Grid.SetRow(presetActions, 0);
        controls.Children.Add(presetActions);

        Grid.SetRow(selectionActions, 1);
        controls.Children.Add(selectionActions);

        return controls;
    }

    private UIElement CreateStatus()
    {
        statusText.Foreground = TrueBimBrushes.TextPrimary;
        statusText.TextWrapping = TextWrapping.Wrap;
        return TrueBimUi.CreateInfoBanner(statusText, TrueBimUiSeverity.Info);
    }

    private UIElement CreateSheetGrid()
    {
        sheetGrid.AutoGenerateColumns = false;
        sheetGrid.CanUserAddRows = false;
        sheetGrid.IsReadOnly = false;
        sheetGrid.Style = TrueBimStyles.CreateDataGridStyle();
        sheetGrid.EnableRowVirtualization = true;
        sheetGrid.EnableColumnVirtualization = true;
        VirtualizingPanel.SetIsVirtualizing(sheetGrid, true);
        VirtualizingPanel.SetIsVirtualizingWhenGrouping(sheetGrid, true);
        VirtualizingPanel.SetVirtualizationMode(sheetGrid, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(sheetGrid, true);
        sheetGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        sheetGrid.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        sheetGrid.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
        sheetGrid.SelectionMode = DataGridSelectionMode.Extended;
        sheetGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
        sheetGrid.CanUserSortColumns = true;
        sheetGrid.Sorting += OnSheetGridSorting;
        sheetGrid.KeyDown += OnSheetGridKeyDown;
        sheetGrid.RowStyle = CreateSheetRowStyle();
        sheetGrid.ToolTip = "Щёлкните чекбокс опорного листа, затем используйте Shift+Click для выбора диапазона. Space переключает отмеченные строки.";

        sheetGrid.Columns.Add(CreateSelectionColumn());
        if (sheetSources.Count > 1)
        {
            sheetGrid.Columns.Add(CreateTextColumn("Источник", nameof(PrintSheetRow.SourceName), 150));
        }

        DataGridTextColumn sheetNumberColumn = CreateTextColumn("Номер", nameof(PrintSheetRow.SheetNumber), 110);
        sheetNumberColumn.SortDirection = ListSortDirection.Ascending;
        sheetGrid.Columns.Add(sheetNumberColumn);
        sheetGrid.Columns.Add(CreateTextColumn("Имя листа", nameof(PrintSheetRow.SheetName), new DataGridLength(1, DataGridLengthUnitType.Star)));
        sheetGrid.Columns.Add(CreateTextColumn("Формат", nameof(PrintSheetRow.SheetFormat), 120));
        sheetGrid.Columns.Add(CreateStatusColumn());
        fileNameColumn = CreateTextColumn("Имя файла", nameof(PrintSheetRow.FileNamePreview), 240);
        sheetGrid.Columns.Add(fileNameColumn);

        return sheetGrid;
    }

    private UIElement CreateExportSettings()
    {
        Grid root = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing16, 0, 0)
        };
        for (int index = 0; index < 14; index++)
        {
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        int rowIndex = 0;

        Grid modeRow = new();
        modeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ExportLabelWidth) });
        modeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        modeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        modeRow.Children.Add(new TextBlock
        {
            Text = "Режим",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        operationModeInput.SelectionChanged += (_, _) => UpdateOperationModeState();
        Grid.SetColumn(operationModeInput, 1);
        modeRow.Children.Add(operationModeInput);
        TextBlock modeExplanation = new()
        {
            Text = "Экспорт — файлы PDF/DWG/DXF/DWF; печать — задание установленному принтеру.",
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 0, 0, 0),
            Foreground = TrueBimBrushes.TextSecondary
        };
        Grid.SetColumn(modeExplanation, 2);
        modeRow.Children.Add(modeExplanation);
        Grid.SetRow(modeRow, rowIndex++);
        root.Children.Add(modeRow);

        Grid printerRow = new()
        {
            Margin = new Thickness(0, 8, 0, 0)
        };
        printerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ExportLabelWidth) });
        printerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        printerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        printerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        printerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        printerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        printerRow.Children.Add(new TextBlock
        {
            Text = "Принтер",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        printerInput.SelectionChanged += (_, _) =>
        {
            UpdatePrinterWarning();
            UpdateExportState();
        };
        Grid.SetColumn(printerInput, 1);
        printerRow.Children.Add(printerInput);
        TextBlock printSetupLabel = new()
        {
            Text = "Настройка Revit",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 8, 0)
        };
        Grid.SetColumn(printSetupLabel, 2);
        printerRow.Children.Add(printSetupLabel);
        printSetupInput.SelectionChanged += (_, _) => UpdateExportState();
        Grid.SetColumn(printSetupInput, 3);
        printerRow.Children.Add(printSetupInput);
        TextBlock rangeLabel = new()
        {
            Text = "Диапазон",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 8, 0)
        };
        Grid.SetColumn(rangeLabel, 4);
        printerRow.Children.Add(rangeLabel);
        TextBlock rangeValue = new()
        {
            Text = "Выбранные листы",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            ToolTip = "Печатаются только листы, отмеченные флажками в таблице."
        };
        Grid.SetColumn(rangeValue, 5);
        printerRow.Children.Add(rangeValue);
        Grid.SetRow(printerRow, rowIndex++);
        root.Children.Add(printerRow);
        printerModeRows.Add(printerRow);

        Grid warningRow = new()
        {
            Margin = new Thickness(0, 8, 0, 0)
        };
        warningRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ExportLabelWidth) });
        warningRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(printerWarningText, 1);
        warningRow.Children.Add(printerWarningText);
        Grid.SetRow(warningRow, rowIndex++);
        root.Children.Add(warningRow);
        printerWarningRow = warningRow;

        Grid folderRow = new();
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ExportLabelWidth) });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        folderRow.Children.Add(new TextBlock
        {
            Text = "Папка",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        exportFolderInput.Text = GetInitialExportFolder();
        exportFolderInput.Height = 32;
        exportFolderInput.ToolTip = "Папка назначения для будущего экспорта.";
        exportFolderInput.TextChanged += (_, _) =>
        {
            if (isApplyingPreset)
            {
                return;
            }

            ResetExportStatuses();
            UpdateExportState();
        };
        Grid.SetColumn(exportFolderInput, 1);
        folderRow.Children.Add(exportFolderInput);

        Button browseButton = CreateActionButton("Обзор", TrueBimIcon.Open, isEnabled: true);
        browseButton.Margin = new Thickness(8, 0, 0, 0);
        browseButton.ToolTip = "Выбрать папку назначения.";
        browseButton.Click += (_, _) => BrowseExportFolder();
        Grid.SetColumn(browseButton, 2);
        folderRow.Children.Add(browseButton);

        Grid.SetRow(folderRow, rowIndex++);
        root.Children.Add(folderRow);
        exportModeRows.Add(folderRow);

        Grid maskRow = new()
        {
            Margin = new Thickness(0, 8, 0, 0)
        };
        maskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ExportLabelWidth) });
        maskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        maskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        maskRow.Children.Add(new TextBlock
        {
            Text = "Маска имени",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        fileNameMaskInput.Height = 32;
        fileNameMaskInput.TextChanged += (_, _) => UpdateFileNamePreviewsUnlessApplyingPreset();
        Grid.SetColumn(fileNameMaskInput, 1);
        maskRow.Children.Add(fileNameMaskInput);

        Button fileNameTokenButton = CreateFileNameTokenButton(fileNameMaskInput);
        Grid.SetColumn(fileNameTokenButton, 2);
        maskRow.Children.Add(fileNameTokenButton);

        Grid.SetRow(maskRow, rowIndex++);
        root.Children.Add(maskRow);
        RegisterDetailedSettingsRow(maskRow);

        Grid combinedDwgMaskRow = new()
        {
            Margin = new Thickness(0, 8, 0, 0)
        };
        combinedDwgMaskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ExportLabelWidth) });
        combinedDwgMaskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        combinedDwgMaskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        combinedDwgMaskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        combinedDwgMaskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        combinedDwgMaskRow.Children.Add(new TextBlock
        {
            Text = "Маска общего DWG",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        combinedDwgNameMaskInput.Height = 32;
        combinedDwgNameMaskInput.TextChanged += (_, _) =>
        {
            if (isApplyingPreset)
            {
                return;
            }

            ResetExportStatuses();
            UpdateExportState();
        };
        Grid.SetColumn(combinedDwgNameMaskInput, 1);
        combinedDwgMaskRow.Children.Add(combinedDwgNameMaskInput);

        Button combinedDwgTokenButton = CreateFileNameTokenButton(combinedDwgNameMaskInput);
        Grid.SetColumn(combinedDwgTokenButton, 2);
        combinedDwgMaskRow.Children.Add(combinedDwgTokenButton);

        TextBlock combinedDwgPreviewLabel = new()
        {
            Text = "Итог",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 8, 0)
        };
        Grid.SetColumn(combinedDwgPreviewLabel, 3);
        combinedDwgMaskRow.Children.Add(combinedDwgPreviewLabel);

        Grid.SetColumn(combinedDwgNamePreviewText, 4);
        combinedDwgMaskRow.Children.Add(combinedDwgNamePreviewText);

        Grid.SetRow(combinedDwgMaskRow, rowIndex++);
        root.Children.Add(combinedDwgMaskRow);
        RegisterDetailedSettingsRow(combinedDwgMaskRow);

        {
            Grid pdfRow = new()
            {
                Margin = new Thickness(0, 8, 0, 0)
            };
            pdfRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ExportLabelWidth) });
            pdfRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pdfRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            pdfRow.Children.Add(new TextBlock
            {
                Text = "Маска общего PDF",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            combinedPdfNameInput.Height = 32;
            combinedPdfNameInput.TextChanged += (_, _) =>
            {
                if (isApplyingPreset)
                {
                    return;
                }

                ResetExportStatuses();
                UpdateExportState();
            };
            Grid.SetColumn(combinedPdfNameInput, 1);
            pdfRow.Children.Add(combinedPdfNameInput);

            Button combinedPdfTokenButton = CreateFileNameTokenButton(combinedPdfNameInput);
            Grid.SetColumn(combinedPdfTokenButton, 2);
            pdfRow.Children.Add(combinedPdfTokenButton);

            Grid.SetRow(pdfRow, rowIndex++);
            root.Children.Add(pdfRow);
            RegisterDetailedSettingsRow(pdfRow);

            Grid combinedPdfPreviewRow = new()
            {
                Margin = new Thickness(0, 8, 0, 0)
            };
            combinedPdfPreviewRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ExportLabelWidth) });
            combinedPdfPreviewRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            combinedPdfPreviewRow.Children.Add(new TextBlock
            {
                Text = "Итог общего PDF",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            Grid.SetColumn(combinedPdfNamePreviewText, 1);
            combinedPdfPreviewRow.Children.Add(combinedPdfNamePreviewText);

            Grid.SetRow(combinedPdfPreviewRow, rowIndex++);
            root.Children.Add(combinedPdfPreviewRow);
            RegisterDetailedSettingsRow(combinedPdfPreviewRow);

            Grid pdfSettingsRow = new()
            {
                Margin = new Thickness(0, 8, 0, 0)
            };
            pdfSettingsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pdfSettingsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pdfSettingsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pdfSettingsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pdfSettingsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            pdfSettingsRow.Children.Add(new TextBlock
            {
                Text = "Цвет",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            BindPdfColorModeInput();
            Grid.SetColumn(pdfColorModeInput, 1);
            pdfSettingsRow.Children.Add(pdfColorModeInput);

            TextBlock pdfRasterQualityLabel = new()
            {
                Text = "Качество",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 8, 0)
            };
            Grid.SetColumn(pdfRasterQualityLabel, 2);
            pdfSettingsRow.Children.Add(pdfRasterQualityLabel);

            BindPdfRasterQualityInput();
            Grid.SetColumn(pdfRasterQualityInput, 3);
            pdfSettingsRow.Children.Add(pdfRasterQualityInput);

            forceRasterPdfInput.VerticalAlignment = VerticalAlignment.Center;
            forceRasterPdfInput.Margin = new Thickness(16, 0, 0, 0);
            forceRasterPdfInput.Checked += (_, _) => UpdatePdfOptionsStateUnlessApplyingPreset();
            forceRasterPdfInput.Unchecked += (_, _) => UpdatePdfOptionsStateUnlessApplyingPreset();
            Grid.SetColumn(forceRasterPdfInput, 4);
            pdfSettingsRow.Children.Add(forceRasterPdfInput);

            Grid.SetRow(pdfSettingsRow, rowIndex++);
            root.Children.Add(pdfSettingsRow);
            RegisterDetailedSettingsRow(pdfSettingsRow);
        }

        {
            Grid cadSetupRow = new()
            {
                Margin = new Thickness(0, 8, 0, 0)
            };
            cadSetupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cadSetupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cadSetupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cadSetupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            cadSetupRow.Children.Add(new TextBlock
            {
                Text = "DWG настройка",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            BindCadSetupInput(dwgSetupInput, selectedDwgProfile.SourceRevitSetupName ?? initialSettings.DwgSetupName);
            Grid.SetColumn(dwgSetupInput, 1);
            cadSetupRow.Children.Add(dwgSetupInput);

            TextBlock dxfSetupLabel = new()
            {
                Text = "DXF настройка",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 8, 0)
            };
            Grid.SetColumn(dxfSetupLabel, 2);
            cadSetupRow.Children.Add(dxfSetupLabel);

            BindCadSetupInput(dxfSetupInput, initialSettings.DxfSetupName);
            Grid.SetColumn(dxfSetupInput, 3);
            cadSetupRow.Children.Add(dxfSetupInput);

            Grid.SetRow(cadSetupRow, rowIndex++);
            root.Children.Add(cadSetupRow);
            RegisterDetailedSettingsRow(cadSetupRow);

            Grid dwgProfileRow = new()
            {
                Margin = new Thickness(0, 8, 0, 0)
            };
            dwgProfileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ExportLabelWidth) });
            dwgProfileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dwgProfileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dwgProfileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            dwgProfileRow.Children.Add(new TextBlock
            {
                Text = "DWG профиль",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            UpdateDwgProfileIndicator();
            Grid.SetColumn(dwgProfileSourceText, 1);
            dwgProfileRow.Children.Add(dwgProfileSourceText);

            Button dwgSettingsButton = CreateActionButton(
                "Настройки DWG...",
                TrueBimIcon.Settings,
                isEnabled: true);
            dwgSettingsButton.Margin = new Thickness(8, 0, 0, 0);
            dwgSettingsButton.ToolTip = "Открыть расширенные настройки DWGExportOptions.";
            dwgSettingsButton.Click += (_, _) => OpenDwgSettings();
            Grid.SetColumn(dwgSettingsButton, 2);
            dwgProfileRow.Children.Add(dwgSettingsButton);

            Button validateDwgButton = CreateActionButton(
                "Проверить настройки",
                TrueBimIcon.Check,
                isEnabled: true);
            validateDwgButton.Margin = new Thickness(8, 0, 0, 0);
            validateDwgButton.ToolTip = "Показать сводку выбранных листов, папки и DWG-профиля.";
            validateDwgButton.Click += (_, _) => ShowDwgSettingsSummary();
            Grid.SetColumn(validateDwgButton, 3);
            dwgProfileRow.Children.Add(validateDwgButton);

            Grid.SetRow(dwgProfileRow, rowIndex++);
            root.Children.Add(dwgProfileRow);
            RegisterDetailedSettingsRow(dwgProfileRow);
        }

        DockPanel actionRow = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0)
        };

        {
            StackPanel formatActions = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            pdfInput.Margin = new Thickness(0, 0, 16, 0);
            combinePdfInput.Margin = new Thickness(0, 0, 16, 0);
            dwgInput.Margin = new Thickness(0, 0, 16, 0);
            dxfInput.Margin = new Thickness(0, 0, 16, 0);
            dwfInput.Margin = new Thickness(0, 0, 16, 0);
            combineDwgInput.Margin = new Thickness(0, 0, 16, 0);
            openExportFolderInput.Margin = new Thickness(0, 0, 16, 0);
            pdfInput.Checked += (_, _) => UpdatePdfOptionsStateUnlessApplyingPreset();
            pdfInput.Unchecked += (_, _) => UpdatePdfOptionsStateUnlessApplyingPreset();
            combinePdfInput.Checked += (_, _) => UpdatePdfOptionsStateUnlessApplyingPreset();
            combinePdfInput.Unchecked += (_, _) => UpdatePdfOptionsStateUnlessApplyingPreset();
            dwgInput.Checked += (_, _) => UpdateExportStateUnlessApplyingPreset();
            dwgInput.Unchecked += (_, _) => UpdateExportStateUnlessApplyingPreset();
            dxfInput.Checked += (_, _) => UpdateExportStateUnlessApplyingPreset();
            dxfInput.Unchecked += (_, _) => UpdateExportStateUnlessApplyingPreset();
            dwfInput.Checked += (_, _) => UpdateExportStateUnlessApplyingPreset();
            dwfInput.Unchecked += (_, _) => UpdateExportStateUnlessApplyingPreset();
            combineDwgInput.Checked += (_, _) => UpdateExportStateUnlessApplyingPreset();
            combineDwgInput.Unchecked += (_, _) => UpdateExportStateUnlessApplyingPreset();
            formatActions.Children.Add(pdfInput);
            formatActions.Children.Add(combinePdfInput);
            formatActions.Children.Add(dwgInput);
            formatActions.Children.Add(combineDwgInput);
            formatActions.Children.Add(dxfInput);
            formatActions.Children.Add(dwfInput);
            formatActions.Children.Add(openExportFolderInput);

            Button settingsButton = CreateActionButton("Настройки...", TrueBimIcon.Settings, isEnabled: true);
            settingsButton.Margin = new Thickness(0, 0, TrueBimTheme.Spacing16, 0);
            settingsButton.ToolTip = "Показать или скрыть общие параметры PDF и DWG.";
            settingsButton.Click += (_, _) => ToggleDetailedSettings(settingsButton);
            formatActions.Children.Add(settingsButton);

            DockPanel.SetDock(formatActions, Dock.Left);
            actionRow.Children.Add(formatActions);
            exportModeRows.Add(formatActions);
        }

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        exportButton.Click += (_, _) => StartOperation();
        actions.Children.Add(exportButton);

        Button closeButton = TrueBimUi.CreateSecondaryButton("Закрыть", TrueBimIcon.Close);
        closeButton.Margin = new Thickness(8, 0, 0, 0);
        closeButton.IsCancel = true;
        closeButton.ToolTip = "Закрыть окно печати.";
        closeButton.Click += (_, _) => Close();
        actions.Children.Add(closeButton);

        actionRow.Children.Add(actions);
        Grid.SetRow(actionRow, rowIndex);
        root.Children.Add(actionRow);

        ApplyOperationModeVisibility();
        UpdatePdfOptionsState();
        return root;
    }

    private void RegisterDetailedSettingsRow(UIElement row)
    {
        row.Visibility = Visibility.Collapsed;
        detailedSettingsRows.Add(row);
    }

    private void ToggleDetailedSettings(Button settingsButton)
    {
        showDetailedExportSettings = !showDetailedExportSettings;
        ApplyOperationModeVisibility();

        settingsButton.ToolTip = showDetailedExportSettings
            ? "Скрыть подробные параметры PDF и DWG."
            : "Показать подробные параметры PDF и DWG.";
    }

    private void RequestLoadSheetsUnlessApplyingPreset()
    {
        if (!isApplyingPreset)
        {
            RequestLoadSheets();
        }
    }

    private void UpdateFileNamePreviewsUnlessApplyingPreset()
    {
        if (!isApplyingPreset)
        {
            UpdateFileNamePreviews();
        }
    }

    private void UpdatePdfOptionsStateUnlessApplyingPreset()
    {
        if (!isApplyingPreset)
        {
            UpdatePdfOptionsState();
        }
    }

    private void UpdateExportStateUnlessApplyingPreset()
    {
        if (!isApplyingPreset)
        {
            UpdateExportState();
        }
    }

    private void UpdateOperationModeState()
    {
        ApplyOperationModeVisibility();
        ResetExportStatuses();
        UpdateExportState();
    }

    private void ApplyOperationModeVisibility()
    {
        bool printToPrinter = GetSelectedOperationMode() == PrintOperationMode.Printer;
        foreach (UIElement row in printerModeRows)
        {
            row.Visibility = printToPrinter ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (UIElement row in exportModeRows)
        {
            row.Visibility = printToPrinter ? Visibility.Collapsed : Visibility.Visible;
        }

        foreach (UIElement row in detailedSettingsRows)
        {
            row.Visibility = !printToPrinter && showDetailedExportSettings
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (fileNameColumn is not null)
        {
            fileNameColumn.Visibility = printToPrinter ? Visibility.Collapsed : Visibility.Visible;
        }

        UpdatePrinterWarning();
    }

    private void UpdatePrinterWarning()
    {
        if (printerWarningRow is null)
        {
            return;
        }

        printerWarningRow.Visibility = GetSelectedOperationMode() == PrintOperationMode.Printer
            && GetSelectedPrinter()?.IsPdfDriver == true
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void ApplySharedControlStyles()
    {
        presetInput.Style = TrueBimStyles.CreateComboBoxStyle();
        sourceFilterInput.Style = TrueBimStyles.CreateComboBoxStyle();
        operationModeInput.Style = TrueBimStyles.CreateComboBoxStyle();
        printerInput.Style = TrueBimStyles.CreateComboBoxStyle();
        printSetupInput.Style = TrueBimStyles.CreateComboBoxStyle();
        exportFolderInput.Style = TrueBimStyles.CreateTextBoxStyle();
        fileNameMaskInput.Style = TrueBimStyles.CreateTextBoxStyle();
        combinedPdfNameInput.Style = TrueBimStyles.CreateTextBoxStyle();
        combinedDwgNameMaskInput.Style = TrueBimStyles.CreateTextBoxStyle();
        pdfColorModeInput.Style = TrueBimStyles.CreateComboBoxStyle();
        pdfRasterQualityInput.Style = TrueBimStyles.CreateComboBoxStyle();
        dwgSetupInput.Style = TrueBimStyles.CreateComboBoxStyle();
        dxfSetupInput.Style = TrueBimStyles.CreateComboBoxStyle();
        includePlaceholdersInput.Style = TrueBimStyles.CreateCheckBoxStyle();
        forceRasterPdfInput.Style = TrueBimStyles.CreateCheckBoxStyle();
        pdfInput.Style = TrueBimStyles.CreateCheckBoxStyle();
        combinePdfInput.Style = TrueBimStyles.CreateCheckBoxStyle();
        dwgInput.Style = TrueBimStyles.CreateCheckBoxStyle();
        dxfInput.Style = TrueBimStyles.CreateCheckBoxStyle();
        dwfInput.Style = TrueBimStyles.CreateCheckBoxStyle();
        combineDwgInput.Style = TrueBimStyles.CreateCheckBoxStyle();
        openExportFolderInput.Style = TrueBimStyles.CreateCheckBoxStyle();
        dwgProfileSourceText.Foreground = TrueBimBrushes.TextSecondary;
    }

    private void BindCadSetupInput(ComboBox setupInput, string? setupName)
    {
        setupInput.ItemsSource = cadExportSetupOptions;
        setupInput.SelectionChanged += (_, _) =>
        {
            if (ReferenceEquals(setupInput, dwgSetupInput))
            {
                ApplyDwgSetupSelectionToProfile();
            }

            if (isApplyingPreset)
            {
                return;
            }

            ResetExportStatuses();
            UpdateExportState();
        };
        setupInput.SelectedItem = FindCadSetupOption(setupName) ?? cadExportSetupOptions.FirstOrDefault();
        setupInput.IsEnabled = cadExportSetupOptions.Count > 1;
    }

    private void BindPdfColorModeInput()
    {
        pdfColorModeInput.ItemsSource = GetPdfColorModeOptions();
        pdfColorModeInput.SelectedValue = initialSettings.PdfColorMode;
        pdfColorModeInput.SelectionChanged += (_, _) => UpdatePdfOptionsStateUnlessApplyingPreset();
    }

    private void BindPdfRasterQualityInput()
    {
        pdfRasterQualityInput.ItemsSource = GetPdfRasterQualityOptions();
        pdfRasterQualityInput.SelectedValue = initialSettings.PdfRasterQuality;
        pdfRasterQualityInput.SelectionChanged += (_, _) => UpdatePdfOptionsStateUnlessApplyingPreset();
    }

    private PrintCadExportSetupOption? FindCadSetupOption(string? setupName)
    {
        string? normalizedSetupName = PrintCadExportSetupService.NormalizeSetupName(setupName);
        if (normalizedSetupName is null)
        {
            return null;
        }

        return cadExportSetupOptions.FirstOrDefault(option =>
            string.Equals(option.SetupName, normalizedSetupName, StringComparison.OrdinalIgnoreCase));
    }

    private void LoadInitialDwgProfile()
    {
        DwgExportProfile? savedProfile = dwgProfileState.FindProfile(dwgProfileState.LastSelectedProfileName);
        if (savedProfile is not null)
        {
            selectedDwgProfile = savedProfile.Clone();
            return;
        }

        string? setupName = PrintCadExportSetupService.NormalizeSetupName(initialSettings.DwgSetupName);
        selectedDwgProfile = dwgOptionsFactory.CreateProfile(
            document,
            setupName,
            setupName ?? DwgExportProfile.DefaultProfileName,
            isUserProfile: false,
            logger);
    }

    private void ApplyDwgSetupSelectionToProfile()
    {
        if (isApplyingDwgProfileToSetupInput || isApplyingPreset)
        {
            return;
        }

        string? setupName = GetSelectedSetupName(dwgSetupInput);
        if (selectedDwgProfile.IsUserProfile)
        {
            selectedDwgProfile.SourceRevitSetupName = setupName;
            selectedDwgProfile.UsePredefinedRevitSetup = setupName is not null && selectedDwgProfile.UsePredefinedRevitSetup;
        }
        else
        {
            selectedDwgProfile = dwgOptionsFactory.CreateProfile(
                document,
                setupName,
                setupName ?? DwgExportProfile.DefaultProfileName,
                isUserProfile: false,
                logger);
        }

        UpdateDwgProfileIndicator();
    }

    private void ApplyDwgProfileToSetupInput()
    {
        isApplyingDwgProfileToSetupInput = true;
        dwgSetupInput.SelectedItem = FindCadSetupOption(selectedDwgProfile.SourceRevitSetupName)
            ?? cadExportSetupOptions.FirstOrDefault();
        isApplyingDwgProfileToSetupInput = false;
        UpdateDwgProfileIndicator();
    }

    private void OpenDwgSettings()
    {
        statusText.Text = "Открытие настроек DWG поставлено в очередь Revit.";
        revitActions.Raise(OpenDwgSettingsInRevitContext);
    }

    private void OpenDwgSettingsInRevitContext()
    {
        DwgExportSettingsWindow settingsWindow = new(
            document,
            GetCurrentDwgProfileForExport(),
            cadExportSetupOptions.ToList(),
            dwgProfileState,
            dwgProfileStorage,
            logger)
        {
            Owner = this
        };

        if (settingsWindow.ShowDialog() != true)
        {
            return;
        }

        selectedDwgProfile = settingsWindow.SelectedProfile.Clone();
        dwgProfileState = settingsWindow.StoreState;
        LoadCadExportSetupOptions();
        ApplyDwgProfileToSetupInput();
        ResetExportStatuses();
        UpdateExportState();
    }

    private void ShowDwgSettingsSummary()
    {
        IReadOnlyList<PrintSheetRow> selectedRows = sheetRows
            .Where(row => row.IsSelected && row.CanBePrinted)
            .ToList();
        DwgExportProfile profile = GetCurrentDwgProfileForExport();
        string folder = string.IsNullOrWhiteSpace(exportFolderInput.Text) ? "не выбрана" : exportFolderInput.Text;
        string setup = profile.UsePredefinedRevitSetup && !string.IsNullOrWhiteSpace(profile.SourceRevitSetupName)
            ? profile.SourceRevitSetupName!
            : "по умолчанию";

        Autodesk.Revit.UI.TaskDialog.Show(
            WindowTitle,
            $"Документ: {document.Title}\nЛистов: {selectedRows.Count}\nФорматы: {GetSelectedFormatsText()}\nПапка: {folder}\nDWG setup: {setup}\nПрофиль: {profile.ProfileName}\nВерсия DWG: {profile.FileVersion}\nЦвета: {profile.Colors}\nКоординаты: {(profile.SharedCoords ? "Shared" : "Project/Internal")}\n3D solids: {profile.ExportOfSolids}");
    }

    private DwgExportProfile GetCurrentDwgProfileForExport()
    {
        DwgExportProfile profile = selectedDwgProfile.Clone();
        profile.SourceRevitSetupName = GetSelectedSetupName(dwgSetupInput);
        if (profile.SourceRevitSetupName is null && profile.UsePredefinedRevitSetup)
        {
            profile.UsePredefinedRevitSetup = false;
        }

        return DwgExportProfileStorage.NormalizeProfile(profile);
    }

    private void UpdateDwgProfileIndicator()
    {
        string source = selectedDwgProfile.IsUserProfile
            ? "Используется пользовательский профиль"
            : "Используется настройка Revit";
        string setup = selectedDwgProfile.UsePredefinedRevitSetup && !string.IsNullOrWhiteSpace(selectedDwgProfile.SourceRevitSetupName)
            ? selectedDwgProfile.SourceRevitSetupName!
            : "по умолчанию";
        dwgProfileSourceText.Text = $"{source}: {selectedDwgProfile.ProfileName}. База: {setup}.";
    }

    private void UpdatePdfOptionsState()
    {
        bool exportPdf = pdfInput.IsChecked == true;
        bool combinePdf = combinePdfInput.IsChecked == true;
        combinePdfInput.IsEnabled = exportPdf;
        combinedPdfNameInput.IsEnabled = exportPdf && combinePdf;
        pdfColorModeInput.IsEnabled = exportPdf;
        pdfRasterQualityInput.IsEnabled = exportPdf;
        forceRasterPdfInput.IsEnabled = exportPdf;
        ResetExportStatuses();
        UpdateExportState();
    }

    private void LoadCadExportSetupOptions()
    {
        cadExportSetupOptions.Clear();
        foreach (PrintCadExportSetupOption option in cadExportSetupService.GetAvailableOptions(document, logger))
        {
            cadExportSetupOptions.Add(option);
        }
    }

    private void LoadPrintOptions()
    {
        operationModeInput.ItemsSource = new[]
        {
            new PrintOperationModeOption(PrintOperationMode.Export, "Экспорт в файлы"),
            new PrintOperationModeOption(PrintOperationMode.Printer, "Печать на принтер")
        };
        operationModeInput.SelectedValue = PrintOperationMode.Export;

        printerOptions.Clear();
        try
        {
            foreach (PrintPrinterOption option in printDriverCatalogService.GetInstalledPrinters())
            {
                printerOptions.Add(option);
            }
        }
        catch (Exception exception)
        {
            logger.Error("Failed to enumerate installed Windows printers.", exception);
        }

        printerInput.ItemsSource = printerOptions;
        string? currentPrinterName = null;
        try
        {
            currentPrinterName = document.PrintManager.PrinterName;
        }
        catch (Exception exception)
        {
            logger.Warning($"Could not read the current Revit printer: {exception.Message}");
        }

        printerInput.SelectedItem = printerOptions.FirstOrDefault(option => string.Equals(
                option.Name,
                currentPrinterName,
                StringComparison.CurrentCultureIgnoreCase))
            ?? printerOptions.FirstOrDefault();

        printSetupOptions.Clear();
        try
        {
            foreach (PrintSetupOption option in printManagerService.GetPrintSetups(document))
            {
                printSetupOptions.Add(option);
            }
        }
        catch (Exception exception)
        {
            logger.Error("Failed to read Revit print setups.", exception);
            printSetupOptions.Add(new PrintSetupOption(null, "Текущая настройка каждого документа"));
        }

        printSetupInput.ItemsSource = printSetupOptions;
        printSetupInput.SelectedItem = printSetupOptions.FirstOrDefault();
    }

    private void LoadSourceFilterOptions()
    {
        sourceFilterOptions.Clear();
        sourceFilterOptions.Add(new PrintSheetSourceFilterOption(null, "Все открытые документы", IncludeLinked: false));
        if (sheetSources.Any(source => source.IsLinked))
        {
            sourceFilterOptions.Add(new PrintSheetSourceFilterOption(null, "Все открытые и связи", IncludeLinked: true));
        }

        foreach (PrintSheetSource source in sheetSources)
        {
            string displayName = source.IsLinked
                ? $"Связь: {source.SourceName}"
                : source.SourceName;
            sourceFilterOptions.Add(new PrintSheetSourceFilterOption(source.SourceId, displayName, IncludeLinked: true));
        }
    }

    private PrintSheetSourceFilterOption? FindActiveSourceFilterOption()
    {
        string? activeSourceId = sheetSources.FirstOrDefault(source => ReferenceEquals(source.Document, document))?.SourceId;
        return string.IsNullOrWhiteSpace(activeSourceId)
            ? null
            : sourceFilterOptions.FirstOrDefault(option => string.Equals(option.SourceId, activeSourceId, StringComparison.Ordinal));
    }

    private void LoadSheets()
    {
        LoadSheets(reloadSources: false);
    }

    private void LoadSheets(bool reloadSources)
    {
        Stopwatch loadTimer = Stopwatch.StartNew();
        bool includePlaceholders = includePlaceholdersInput.IsChecked == true;
        PrintSheetSourceFilterOption? selectedSource = GetSelectedSourceFilterOption();
        EnsureSheetsLoaded(selectedSource, reloadSources);
        IEnumerable<PrintSheetInfo> visibleSheets = includePlaceholders
            ? GetAllLoadedSheets()
            : GetAllLoadedSheets().Where(sheet => !sheet.IsPlaceholder);
        string? selectedSourceId = selectedSource?.SourceId;
        if (!string.IsNullOrWhiteSpace(selectedSourceId))
        {
            visibleSheets = visibleSheets.Where(sheet => string.Equals(sheet.SourceId, selectedSourceId, StringComparison.Ordinal));
        }
        else if (selectedSource?.IncludeLinked != true)
        {
            visibleSheets = visibleSheets.Where(sheet => !sheet.SourceIsLinked);
        }

        visibleSheets = visibleSheets.OrderBy(sheet => sheet, PrintSheetComparer.Ascending);

        List<PrintSheetRow> rows = new();
        foreach (PrintSheetInfo sheet in visibleSheets)
        {
            PrintSheetRow row = new(sheet, sheetSelectionState.Get(sheet));
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(PrintSheetRow.IsSelected))
                {
                    sheetSelectionState.Set(row.Sheet, row.IsSelected);
                    if (!isBatchUpdatingSelection)
                    {
                        UpdateExportState();
                    }
                }
            };
            rows.Add(row);
        }

        sheetSelectionAnchor = null;
        sheetRows = rows;
        sheetGrid.ItemsSource = sheetRows;
        ConfigureSheetView();
        UpdateFileNamePreviews();
        loadTimer.Stop();
        logger.Info($"{WindowTitle} prepared {sheetRows.Count} visible sheet rows in {loadTimer.ElapsedMilliseconds} ms. Reloaded sources: {reloadSources}.");
    }

    private void RequestLoadSheets()
    {
        statusText.Text = "Обновление листов поставлено в очередь Revit.";
        revitActions.Raise(LoadSheets);
    }

    private void RequestReloadSheets()
    {
        statusText.Text = "Перечитываю листы и параметры текущей маски.";
        revitActions.Raise(() => LoadSheets(reloadSources: true));
    }

    private void EnsureSheetsLoaded(
        PrintSheetSourceFilterOption? selectedSource,
        bool reloadSources)
    {
        IEnumerable<PrintSheetSource> sourcesToLoad;
        string? selectedSourceId = selectedSource?.SourceId;
        if (!string.IsNullOrWhiteSpace(selectedSourceId))
        {
            sourcesToLoad = sheetSources.Where(source => string.Equals(source.SourceId, selectedSourceId, StringComparison.Ordinal));
        }
        else if (selectedSource?.IncludeLinked == true)
        {
            sourcesToLoad = sheetSources;
        }
        else
        {
            sourcesToLoad = sheetSources.Where(source => !source.IsLinked);
        }

        PrintSheetCollectorService collector = new();
        IReadOnlyCollection<string> sheetParameterNames = fileNameTemplateService.GetSheetParameterNames(
            fileNameMaskInput.Text,
            combinedPdfNameInput.Text,
            combinedDwgNameMaskInput.Text);
        IReadOnlyCollection<string> titleBlockParameterNames = fileNameTemplateService.GetTitleBlockParameterNames(
            fileNameMaskInput.Text,
            combinedPdfNameInput.Text,
            combinedDwgNameMaskInput.Text);
        IReadOnlyCollection<string> projectParameterNames = fileNameTemplateService.GetProjectParameterNames(
            fileNameMaskInput.Text,
            combinedPdfNameInput.Text,
            combinedDwgNameMaskInput.Text);
        foreach (PrintSheetSource source in sourcesToLoad)
        {
            bool sourceIsLoaded = loadedSourceIds.Contains(source.SourceId);
            bool requiredParametersAreLoaded = loadedSheetParameterNamesBySourceId.TryGetValue(
                    source.SourceId,
                    out HashSet<string>? loadedParameterNames)
                && sheetParameterNames.All(loadedParameterNames.Contains)
                && loadedTitleBlockParameterNamesBySourceId.TryGetValue(
                    source.SourceId,
                    out HashSet<string>? loadedTitleBlockParameterNames)
                && titleBlockParameterNames.All(loadedTitleBlockParameterNames.Contains)
                && loadedProjectParameterNamesBySourceId.TryGetValue(
                    source.SourceId,
                    out HashSet<string>? loadedProjectParameterNames)
                && projectParameterNames.All(loadedProjectParameterNames.Contains);
            if (!reloadSources && sourceIsLoaded && requiredParametersAreLoaded)
            {
                continue;
            }

            PrintSheetCollection collection = collector.CollectWithCatalog(
                source.Document,
                source.SourceId,
                source.SourceName,
                source.SourceKind,
                sheetParameterNames,
                titleBlockParameterNames);
            sourceSheetsById[source.SourceId] = collection.Sheets.ToList();
            parameterCatalogsBySourceId[source.SourceId] = collection.ParameterCatalog;
            loadedSheetParameterNamesBySourceId[source.SourceId] = sheetParameterNames.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            loadedTitleBlockParameterNamesBySourceId[source.SourceId] = titleBlockParameterNames.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            loadedProjectParameterNamesBySourceId[source.SourceId] = projectParameterNames.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            fileNameContextsBySourceId[source.SourceId] = ReferenceEquals(source.Document, document)
                ? CreateFileNameContext(document, projectParameterNames)
                : CreateFileNameContext(source.Document, projectParameterNames);
            loadedSourceIds.Add(source.SourceId);
            logger.Info($"Loaded {collection.Sheets.Count} sheets for print source '{source.SourceName}' with {sheetParameterNames.Count} custom sheet parameters and {titleBlockParameterNames.Count} title block parameters.");
        }
    }

    private void ConfigureSheetView()
    {
        bool includeSourceLevel = sheetRows
            .Select(row => row.Sheet.SourceId)
            .Distinct(StringComparer.Ordinal)
            .Skip(1)
            .Any();
        ConfigureSheetGroupStyles(includeSourceLevel);

        ICollectionView groupedView = CollectionViewSource.GetDefaultView(sheetRows);
        using (groupedView.DeferRefresh())
        {
            groupedView.GroupDescriptions.Clear();
            groupedView.SortDescriptions.Clear();
            if (includeSourceLevel)
            {
                groupedView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PrintSheetRow.SourceName)));
            }

            groupedView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PrintSheetRow.GroupName)));
            if (groupedView is ListCollectionView listView)
            {
                listView.CustomSort = new PrintSheetRowComparer(
                    sheetSourceOrderById,
                    isSheetNumberSortDescending);
            }
        }

        UpdateSheetNumberSortIndicator();
    }

    private void UpdateFileNamePreviews()
    {
        int counter = 1;
        foreach (PrintSheetRow row in sheetRows)
        {
            PrintFileNameContext context = fileNameContextsBySourceId.TryGetValue(row.Sheet.SourceId, out PrintFileNameContext? sourceContext)
                ? sourceContext
                : fileNameContext;
            PrintFileNamePreview preview = fileNameTemplateService.Build(
                fileNameMaskInput.Text,
                row.Sheet,
                context,
                counter);
            row.UpdateFileNamePreview(preview);
            row.ExportStatus = string.Empty;
            counter++;
        }

        HashSet<string> duplicatedNames = sheetRows
            .Where(row => row.CanBePrinted)
            .GroupBy(row => row.FileNamePreview, StringComparer.CurrentCultureIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        foreach (PrintSheetRow row in sheetRows)
        {
            row.IsFileNameDuplicate = row.CanBePrinted && duplicatedNames.Contains(row.FileNamePreview);
        }

        UpdateExportState();
    }

    private void SetAllSelected(bool isSelected)
    {
        SetRowsSelected(sheetRows.Where(row => row.CanBePrinted), isSelected);
        logger.Info($"Print sheet selection changed: selected={isSelected}, rows={sheetRows.Count}.");
    }

    private void SetRowsSelected(IEnumerable<PrintSheetRow> rows, bool isSelected)
    {
        isBatchUpdatingSelection = true;
        try
        {
            foreach (PrintSheetRow row in rows)
            {
                row.IsSelected = isSelected;
            }
        }
        finally
        {
            isBatchUpdatingSelection = false;
        }

        UpdateExportState();
    }

    private void BrowseExportFolder()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Выберите папку экспорта",
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "Выберите папку",
            InitialDirectory = Directory.Exists(exportFolderInput.Text)
                ? exportFolderInput.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string? folder = Path.GetDirectoryName(dialog.FileName);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            exportFolderInput.Text = folder;
        }
    }

    private ExistingFileDecision ResolveExistingFileDecision(
        IReadOnlyList<PrintSheetRow> selectedRows,
        PrintPdfExportMode pdfMode,
        bool combineDwg,
        IReadOnlyDictionary<string, string> combinedPdfFileNamesBySourceId,
        IReadOnlyDictionary<string, string> mergedDwgFileNamesBySourceId)
    {
        List<string> existingPaths = GetExistingOutputPaths(
                selectedRows,
                pdfMode,
                combineDwg,
                combinedPdfFileNamesBySourceId,
                mergedDwgFileNamesBySourceId)
            .Where(File.Exists)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (existingPaths.Count == 0)
        {
            return ExistingFileDecision.Replace;
        }

        MessageBoxResult result = MessageBox.Show(
            this,
            $"В папке экспорта уже есть файлов с такими именами: {existingPaths.Count}.\n\nДа - заменить существующие файлы.\nНет - пропустить листы с совпадающими файлами.\nОтмена - не запускать экспорт.",
            WindowTitle,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        return result switch
        {
            MessageBoxResult.Yes => ExistingFileDecision.Replace,
            MessageBoxResult.No => ExistingFileDecision.Skip,
            _ => ExistingFileDecision.Cancel
        };
    }

    private bool HasExistingOutput(
        PrintSheetRow row,
        PrintPdfExportMode pdfMode,
        bool combineDwg,
        IReadOnlyDictionary<string, string> combinedPdfFileNamesBySourceId,
        IReadOnlyDictionary<string, string> mergedDwgFileNamesBySourceId)
    {
        return GetOutputPathsForRow(
                row,
                pdfMode,
                combineDwg,
                combinedPdfFileNamesBySourceId,
                mergedDwgFileNamesBySourceId)
            .Any(File.Exists);
    }

    private IEnumerable<string> GetExistingOutputPaths(
        IReadOnlyList<PrintSheetRow> selectedRows,
        PrintPdfExportMode pdfMode,
        bool combineDwg,
        IReadOnlyDictionary<string, string> combinedPdfFileNamesBySourceId,
        IReadOnlyDictionary<string, string> mergedDwgFileNamesBySourceId)
    {
        foreach (PrintSheetRow row in selectedRows)
        {
            foreach (string outputPath in GetOutputPathsForRow(
                         row,
                         pdfMode,
                         combineDwg,
                         combinedPdfFileNamesBySourceId,
                         mergedDwgFileNamesBySourceId))
            {
                yield return outputPath;
            }
        }
    }

    private IEnumerable<string> GetOutputPathsForRow(
        PrintSheetRow row,
        PrintPdfExportMode pdfMode,
        bool combineDwg,
        IReadOnlyDictionary<string, string> combinedPdfFileNamesBySourceId,
        IReadOnlyDictionary<string, string> mergedDwgFileNamesBySourceId)
    {
        string exportFolder = exportFolderInput.Text;
        if (pdfInput.IsChecked == true
            && pdfMode is PrintPdfExportMode.SeparateFiles or PrintPdfExportMode.SeparateAndCombined)
        {
            yield return Path.Combine(exportFolder, PrintPdfExportService.NormalizePdfFileName(row.FileNamePreview));
        }

        if (pdfInput.IsChecked == true
            && pdfMode is PrintPdfExportMode.CombinedFile or PrintPdfExportMode.SeparateAndCombined)
        {
            yield return Path.Combine(
                exportFolder,
                PrintPdfExportService.BuildCombinedPdfFileName(combinedPdfFileNamesBySourceId[row.Sheet.SourceId]));
        }

        if (dwgInput.IsChecked == true)
        {
            string dwgFileName = combineDwg
                ? mergedDwgFileNamesBySourceId[row.Sheet.SourceId]
                : row.FileNamePreview;
            yield return Path.Combine(exportFolder, PrintCadExportService.NormalizeCadFileName(dwgFileName, PrintCadExportFormat.Dwg));
        }

        if (dxfInput.IsChecked == true)
        {
            yield return Path.Combine(exportFolder, PrintCadExportService.NormalizeCadFileName(row.FileNamePreview, PrintCadExportFormat.Dxf));
        }

        if (dwfInput.IsChecked == true)
        {
            yield return Path.Combine(exportFolder, PrintCadExportService.NormalizeCadFileName(row.FileNamePreview, PrintCadExportFormat.Dwf));
        }
    }

    private IReadOnlyDictionary<string, PrintFileNamePreview> BuildCombinedFileNamePreviews(
        string template,
        IReadOnlyList<PrintSheetRow> selectedRows)
    {
        Dictionary<string, PrintFileNamePreview> previews = new(StringComparer.Ordinal);
        foreach (IGrouping<string, PrintSheetRow> sourceRows in selectedRows.GroupBy(row => row.Sheet.SourceId))
        {
            PrintFileNameContext context = fileNameContextsBySourceId.TryGetValue(
                    sourceRows.Key,
                    out PrintFileNameContext? sourceContext)
                ? sourceContext
                : fileNameContext;
            previews[sourceRows.Key] = fileNameTemplateService.BuildCombined(
                template,
                sourceRows.Select(row => row.Sheet).ToList(),
                context);
        }

        return previews;
    }

    private static string? FindDuplicateCombinedPdfFileName(
        IReadOnlyDictionary<string, PrintFileNamePreview> previews)
    {
        return previews.Values
            .Select(preview => PrintPdfExportService.BuildCombinedPdfFileName(preview.FileName))
            .GroupBy(fileName => fileName, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
    }

    private static string? FindDuplicateMergedDwgFileName(
        IReadOnlyDictionary<string, PrintFileNamePreview> previews)
    {
        return previews.Values
            .Select(preview => PrintCadExportService.NormalizeCadFileName(
                preview.FileName,
                PrintCadExportFormat.Dwg))
            .GroupBy(fileName => fileName, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
    }

    private void UpdateCombinedDwgNamePreview(
        IReadOnlyDictionary<string, PrintFileNamePreview> previews)
    {
        if (dwgInput.IsChecked != true || combineDwgInput.IsChecked != true)
        {
            combinedDwgNamePreviewText.Text = "Включите «Один DWG»";
            combinedDwgNamePreviewText.ToolTip = null;
            return;
        }

        if (previews.Count == 0)
        {
            combinedDwgNamePreviewText.Text = "Выберите листы";
            combinedDwgNamePreviewText.ToolTip = null;
            return;
        }

        List<string> fileNames = previews.Values
            .Select(preview => PrintCadExportService.NormalizeCadFileName(
                preview.FileName,
                PrintCadExportFormat.Dwg))
            .ToList();
        combinedDwgNamePreviewText.Text = fileNames.Count == 1
            ? fileNames[0]
            : $"{fileNames.Count} файла: {string.Join("; ", fileNames.Take(2))}";
        combinedDwgNamePreviewText.ToolTip = string.Join(Environment.NewLine, fileNames);
    }

    private void UpdateCombinedPdfNamePreview(
        IReadOnlyDictionary<string, PrintFileNamePreview> previews)
    {
        bool useCombinedPdf = pdfInput.IsChecked == true
            && combinePdfInput.IsChecked == true;
        if (!useCombinedPdf)
        {
            combinedPdfNamePreviewText.Text = "Включите «Один PDF»";
            combinedPdfNamePreviewText.ToolTip = null;
            return;
        }

        if (previews.Count == 0)
        {
            combinedPdfNamePreviewText.Text = "Выберите листы";
            combinedPdfNamePreviewText.ToolTip = null;
            return;
        }

        List<string> fileNames = previews.Values
            .Select(preview => PrintPdfExportService.BuildCombinedPdfFileName(preview.FileName))
            .ToList();
        combinedPdfNamePreviewText.Text = fileNames.Count == 1
            ? fileNames[0]
            : $"{fileNames.Count} файла: {string.Join("; ", fileNames.Take(2))}";
        combinedPdfNamePreviewText.ToolTip = string.Join(Environment.NewLine, fileNames);
    }

    private void StartOperation()
    {
        bool printToPrinter = GetSelectedOperationMode() == PrintOperationMode.Printer;
        statusText.Text = printToPrinter
            ? "Печать поставлена в очередь Revit."
            : "Экспорт поставлен в очередь Revit.";
        revitActions.Raise(printToPrinter ? StartPrintInRevitContext : StartExportInRevitContext);
    }

    private PrintabilityValidationResult ValidateSelectedSheetPrintability(
        IReadOnlyList<PrintSheetRow> selectedRows)
    {
        Stopwatch timer = Stopwatch.StartNew();
        List<PrintSheetRow> printableRows = new();
        List<PrintSheetRow> rejectedRows = new();
        foreach (PrintSheetRow row in selectedRows)
        {
            try
            {
                if (!sheetSourcesById.TryGetValue(row.Sheet.SourceId, out PrintSheetSource? source))
                {
                    row.SetPrintability(canBePrinted: false, "Источник недоступен");
                    rejectedRows.Add(row);
                    continue;
                }

                Autodesk.Revit.DB.ViewSheet? sheet = source.Document.GetElement(
                    RevitElementIds.Create(row.Sheet.ElementId)) as Autodesk.Revit.DB.ViewSheet;
                bool canBePrinted = sheet is not null && !sheet.IsPlaceholder && sheet.CanBePrinted;
                row.SetPrintability(canBePrinted, canBePrinted ? string.Empty : "Не печатается");
                if (canBePrinted)
                {
                    printableRows.Add(row);
                }
                else
                {
                    rejectedRows.Add(row);
                }
            }
            catch (Exception exception)
            {
                row.SetPrintability(canBePrinted: false, "Ошибка проверки");
                rejectedRows.Add(row);
                logger.Error($"Failed to validate printability for sheet element id {row.Sheet.ElementId}.", exception);
            }
        }

        timer.Stop();
        logger.Info($"Validated printability for {selectedRows.Count} selected sheets in {timer.ElapsedMilliseconds} ms. Printable: {printableRows.Count}; rejected: {rejectedRows.Count}.");
        return new PrintabilityValidationResult(printableRows, rejectedRows);
    }

    private void StartPrintInRevitContext()
    {
        PrintPrinterOption? printer = GetSelectedPrinter();
        if (printer is null)
        {
            TaskDialog.Show(WindowTitle, "Выберите установленный принтер.");
            UpdateExportState();
            return;
        }

        if (printer.IsPdfDriver && !ConfirmPdfDriverPrint(printer.Name))
        {
            statusText.Text = "Печать через PDF-драйвер отменена.";
            return;
        }

        List<PrintSheetRow> selectedRows = sheetRows
            .Where(row => row.IsSelected && row.CanBePrinted)
            .ToList();
        PrintabilityValidationResult printability = ValidateSelectedSheetPrintability(selectedRows);
        selectedRows = printability.PrintableRows.ToList();
        if (selectedRows.Count == 0)
        {
            TaskDialog.Show(
                WindowTitle,
                "Ни один из выбранных листов не прошел проверку печати. Проверьте статус листов в таблице.");
            UpdateExportState();
            return;
        }

        string? printSetupName = GetSelectedPrintSetupName();
        logger.Info($"Print operation requested with {selectedRows.Count} sheets. Printer: {printer.Name}. PDF driver: {printer.IsPdfDriver}. Print setup: {printSetupName ?? "current document setting"}. Range: selected sheets.");
        foreach (PrintSheetRow row in selectedRows)
        {
            row.ExportStatus = "Печать: в очереди";
        }

        int printedSheetCount = 0;
        int failureCount = printability.RejectedRows.Count;
        List<string> failureMessages = printability.RejectedRows
            .Select(row => $"Лист {row.SheetNumber}: не прошел проверку печати")
            .ToList();
        foreach (IGrouping<string, PrintSheetRow> rowGroup in selectedRows.GroupBy(row => row.Sheet.SourceId))
        {
            if (!sheetSourcesById.TryGetValue(rowGroup.Key, out PrintSheetSource? source))
            {
                foreach (PrintSheetRow row in rowGroup)
                {
                    row.ExportStatus = "Ошибка печати";
                    failureCount++;
                }

                failureMessages.Add($"Источник листов не найден: {rowGroup.Key}");
                continue;
            }

            IReadOnlyList<PrintSheetRow> sourceRows = rowGroup.ToList();
            PrintDriverResult result = printManagerService.Print(
                source.Document,
                sourceRows
                    .Select(row => new PrintDriverJobItem(
                        row.Sheet.ElementId,
                        row.SheetNumber,
                        row.SheetName))
                    .ToList(),
                printer.Name,
                printSetupName,
                logger);
            printedSheetCount += result.PrintedSheetCount;
            failureCount += result.Failures.Count;
            HashSet<long> failedIds = result.Failures
                .Select(failure => failure.Item.ElementId)
                .ToHashSet();
            foreach (PrintSheetRow row in sourceRows)
            {
                row.ExportStatus = failedIds.Contains(row.Sheet.ElementId)
                    ? "Ошибка печати"
                    : "Напечатан";
            }

            failureMessages.AddRange(result.Failures.Select(failure =>
                $"{source.SourceName}, лист {failure.Item.SheetNumber}: {failure.Message}"));
        }

        string failureMessage = failureMessages.Count > 0
            ? "\n\nОшибки:\n" + string.Join("\n", failureMessages.Take(3))
            : string.Empty;
        TaskDialog.Show(
            WindowTitle,
            $"Отправлено листов в принтер: {printedSheetCount}\nОшибок: {failureCount}{failureMessage}");
        UpdateExportState();
    }

    private static bool ConfirmPdfDriverPrint(string printerName)
    {
        TaskDialog confirmation = new(WindowTitle)
        {
            MainInstruction = $"Отправить листы в PDF-драйвер «{printerName}»?",
            MainContent = "Драйвер может открыть собственный диалог или запросить путь и имя файла. Для предсказуемого пакетного PDF используйте режим «Экспорт».",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No
        };
        return confirmation.Show() == TaskDialogResult.Yes;
    }

    private void StartExportInRevitContext()
    {
        SavePrintSettings();
        List<PrintSheetRow> selectedRows = sheetRows
            .Where(row => row.IsSelected && row.CanBePrinted)
            .ToList();
        string formats = GetSelectedFormatsText();
        bool exportPdf = pdfInput.IsChecked == true;
        bool exportDwg = dwgInput.IsChecked == true;
        bool exportDxf = dxfInput.IsChecked == true;
        bool exportDwf = dwfInput.IsChecked == true;
        bool combineDwg = exportDwg && combineDwgInput.IsChecked == true;
        PrintPdfExportMode pdfMode = GetSelectedPdfMode();
        bool combinePdf = exportPdf && combinePdfInput.IsChecked == true;
        PrintPdfExportSettings pdfSettings = GetSelectedPdfSettings();
        string pdfModeLogText = exportPdf
            ? PrintPdfExportService.GetModeDisplayName(pdfMode)
            : "не выбран";
        string pdfSettingsLogText = exportPdf
            ? PrintPdfExportService.GetSettingsDisplayName(pdfSettings)
            : "не выбраны";
        string? dwgSetupName = GetSelectedSetupName(dwgSetupInput);
        string? dxfSetupName = GetSelectedSetupName(dxfSetupInput);
        DwgExportProfile? dwgProfile = exportDwg ? GetCurrentDwgProfileForExport() : null;

        IReadOnlyDictionary<string, PrintFileNamePreview> combinedPdfPreviews = combinePdf
            ? BuildCombinedFileNamePreviews(combinedPdfNameInput.Text, selectedRows)
            : new Dictionary<string, PrintFileNamePreview>(StringComparer.Ordinal);
        if (combinedPdfPreviews.Values.Any(preview => preview.HasUnknownTokens))
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                WindowTitle,
                "Маска общего PDF содержит неизвестный токен или незагруженный параметр. Исправьте маску либо нажмите «Обновить».");
            UpdateExportState();
            return;
        }

        string? duplicateCombinedPdfFileName = FindDuplicateCombinedPdfFileName(combinedPdfPreviews);
        if (duplicateCombinedPdfFileName is not null)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                WindowTitle,
                $"Маска общего PDF создает одинаковое имя для нескольких документов: {duplicateCombinedPdfFileName}\n\nДобавьте в маску токен {{Имя документа}} или параметр проекта.");
            UpdateExportState();
            return;
        }

        IReadOnlyDictionary<string, string> combinedPdfFileNamesBySourceId = combinedPdfPreviews
            .ToDictionary(pair => pair.Key, pair => pair.Value.FileName, StringComparer.Ordinal);

        IReadOnlyDictionary<string, PrintFileNamePreview> mergedDwgPreviews = combineDwg
            ? BuildCombinedFileNamePreviews(combinedDwgNameMaskInput.Text, selectedRows)
            : new Dictionary<string, PrintFileNamePreview>(StringComparer.Ordinal);
        if (mergedDwgPreviews.Values.Any(preview => preview.HasUnknownTokens))
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                WindowTitle,
                "Маска общего DWG содержит неизвестный токен или незагруженный параметр. Исправьте маску либо нажмите «Обновить».");
            UpdateExportState();
            return;
        }

        string? duplicateMergedDwgFileName = FindDuplicateMergedDwgFileName(mergedDwgPreviews);
        if (duplicateMergedDwgFileName is not null)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                WindowTitle,
                $"Маска общего DWG создает одинаковое имя для нескольких документов: {duplicateMergedDwgFileName}\n\nДобавьте в маску токен {{Имя документа}} или параметр проекта.");
            UpdateExportState();
            return;
        }

        IReadOnlyDictionary<string, string> mergedDwgFileNamesBySourceId = mergedDwgPreviews
            .ToDictionary(pair => pair.Key, pair => pair.Value.FileName, StringComparer.Ordinal);

        ExistingFileDecision existingFileDecision = ResolveExistingFileDecision(
            selectedRows,
            pdfMode,
            combineDwg,
            combinedPdfFileNamesBySourceId,
            mergedDwgFileNamesBySourceId);
        if (existingFileDecision == ExistingFileDecision.Cancel)
        {
            return;
        }

        if (existingFileDecision == ExistingFileDecision.Skip)
        {
            selectedRows = selectedRows
                .Where(row => !HasExistingOutput(
                    row,
                    pdfMode,
                    combineDwg,
                    combinedPdfFileNamesBySourceId,
                    mergedDwgFileNamesBySourceId))
                .ToList();
            if (selectedRows.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show(WindowTitle, "Все выбранные листы пропущены: файлы уже существуют.");
                UpdateExportState();
                return;
            }
        }

        PrintabilityValidationResult printability = ValidateSelectedSheetPrintability(selectedRows);
        selectedRows = printability.PrintableRows.ToList();
        if (selectedRows.Count == 0)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                WindowTitle,
                "Ни один из выбранных листов не прошел проверку печати. Проверьте статус листов в таблице.");
            UpdateExportState();
            return;
        }

        string mergedDwgLogText = combineDwg
            ? string.Join(", ", mergedDwgFileNamesBySourceId.Select(pair => $"{pair.Key}={pair.Value}"))
            : "not combined";
        string combinedPdfLogText = combinePdf
            ? string.Join(", ", combinedPdfFileNamesBySourceId.Select(pair => $"{pair.Key}={pair.Value}"))
            : "not combined";
        logger.Info($"Print export requested for document '{document.Title}' with {selectedRows.Count} sheets. Formats: {formats}. PDF mode: {pdfModeLogText}. PDF settings: {pdfSettingsLogText}. CAD setups: {GetSelectedCadSetupsText()}. DWG profile: {(dwgProfile is null ? "not selected" : DwgExportOptionsFactory.GetProfileSummary(dwgProfile))}. Folder: {exportFolderInput.Text}. Open folder after completion: {openExportFolderInput.IsChecked == true}. Mask: {fileNameMaskInput.Text}. Combined PDF names: {combinedPdfLogText}. Merged DWG names: {mergedDwgLogText}.");
        Dictionary<PrintSheetRow, List<string>> rowStatuses = selectedRows.ToDictionary(
            row => row,
            _ => new List<string>());
        int exportedCount = 0;
        int failureCount = printability.RejectedRows.Count;
        List<string> failureMessages = printability.RejectedRows
            .Select(row => $"Лист {row.SheetNumber}: не прошел проверку печати")
            .ToList();
        foreach (PrintSheetRow row in selectedRows)
        {
            row.ExportStatus = "Экспорт: в очереди";
        }

        IReadOnlyList<IGrouping<string, PrintSheetRow>> rowGroups = selectedRows
            .GroupBy(row => row.Sheet.SourceId)
            .ToList();

        foreach (IGrouping<string, PrintSheetRow> rowGroup in rowGroups)
        {
            if (!sheetSourcesById.TryGetValue(rowGroup.Key, out PrintSheetSource? source))
            {
                failureCount += rowGroup.Count();
                failureMessages.Add($"Источник листов не найден: {rowGroup.Key}");
                continue;
            }

            IReadOnlyList<PrintSheetRow> sourceRows = rowGroup.ToList();
            string? sourceCombinedPdfName = combinePdf
                ? combinedPdfFileNamesBySourceId[source.SourceId]
                : null;

            if (exportPdf)
            {
                PrintPdfExportResult result = pdfExportService.Export(
                    source.Document,
                    exportFolderInput.Text,
                    sourceRows
                        .Select(row => new PrintPdfExportItem(row.Sheet.ElementId, row.FileNamePreview))
                        .ToList(),
                    pdfMode,
                    sourceCombinedPdfName,
                    pdfSettings,
                    logger);
                exportedCount += result.ExportedFiles.Count;
                failureCount += result.Failures.Count;
                ApplyPdfStatus(rowStatuses, sourceRows, result);
                failureMessages.AddRange(result.Failures.Select(failure => $"PDF {source.SourceName} {failure.Item.FileName}: {failure.Message}"));
            }

            if (exportDwg)
            {
                PrintCadExportResult result = cadExportService.Export(
                    source.Document,
                    exportFolderInput.Text,
                    sourceRows
                        .Select(row => new PrintCadExportItem(row.Sheet.ElementId, row.FileNamePreview))
                        .ToList(),
                    PrintCadExportFormat.Dwg,
                    dwgSetupName,
                    dwgProfile,
                    combineDwg,
                    combineDwg ? mergedDwgFileNamesBySourceId[source.SourceId] : null,
                    logger);
                exportedCount += result.ExportedFiles.Count;
                failureCount += result.Failures.Count;
                ApplyCadStatus(rowStatuses, sourceRows, result);
                failureMessages.AddRange(result.Failures.Select(failure => $"{PrintCadExportService.GetDisplayName(failure.Format)} {source.SourceName} {failure.Item.FileName}: {failure.Message}"));
            }

            if (exportDxf)
            {
                PrintCadExportResult result = cadExportService.Export(
                    source.Document,
                    exportFolderInput.Text,
                    sourceRows
                        .Select(row => new PrintCadExportItem(row.Sheet.ElementId, row.FileNamePreview))
                        .ToList(),
                    PrintCadExportFormat.Dxf,
                    dxfSetupName,
                    logger);
                exportedCount += result.ExportedFiles.Count;
                failureCount += result.Failures.Count;
                ApplyCadStatus(rowStatuses, sourceRows, result);
                failureMessages.AddRange(result.Failures.Select(failure => $"{PrintCadExportService.GetDisplayName(failure.Format)} {source.SourceName} {failure.Item.FileName}: {failure.Message}"));
            }

            if (exportDwf)
            {
                PrintCadExportResult result = cadExportService.Export(
                    source.Document,
                    exportFolderInput.Text,
                    sourceRows
                        .Select(row => new PrintCadExportItem(row.Sheet.ElementId, row.FileNamePreview))
                        .ToList(),
                    PrintCadExportFormat.Dwf,
                    setupName: null,
                    logger);
                exportedCount += result.ExportedFiles.Count;
                failureCount += result.Failures.Count;
                ApplyCadStatus(rowStatuses, sourceRows, result);
                failureMessages.AddRange(result.Failures.Select(failure => $"{PrintCadExportService.GetDisplayName(failure.Format)} {source.SourceName} {failure.Item.FileName}: {failure.Message}"));
            }
        }

        foreach (PrintSheetRow row in selectedRows)
        {
            row.ExportStatus = rowStatuses.TryGetValue(row, out List<string>? statuses)
                ? string.Join(", ", statuses)
                : string.Empty;
        }

        string failureMessage = failureMessages.Count > 0
            ? "\n\nОшибки:\n" + string.Join("\n", failureMessages.Take(3))
            : string.Empty;
        Autodesk.Revit.UI.TaskDialog.Show(
            WindowTitle,
            $"Экспортировано файлов: {exportedCount}\nОшибок: {failureCount}{failureMessage}");
        OpenExportFolderAfterCompletion(exportedCount);
        UpdateExportState();
    }

    private void OpenExportFolderAfterCompletion(int exportedFileCount)
    {
        if (!PrintExportCompletionPolicy.ShouldOpenExportFolder(
                openExportFolderInput.IsChecked == true,
                exportedFileCount))
        {
            return;
        }

        string exportFolder = exportFolderInput.Text;
        if (!Directory.Exists(exportFolder))
        {
            logger.Warning($"Print export folder was not opened because it does not exist: '{exportFolder}'.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exportFolder,
                UseShellExecute = true
            });
            logger.Info($"Opened print export folder after completion: '{exportFolder}'. Exported files: {exportedFileCount}.");
        }
        catch (Exception exception)
        {
            logger.Warning($"Failed to open print export folder '{exportFolder}': {exception.Message}");
        }
    }

    private static void ApplyPdfStatus(
        Dictionary<PrintSheetRow, List<string>> rowStatuses,
        IReadOnlyList<PrintSheetRow> rows,
        PrintPdfExportResult result)
    {
        HashSet<long> failedIds = result.Failures
            .Select(failure => failure.Item.ElementId)
            .ToHashSet();
        foreach (PrintSheetRow row in rows)
        {
            rowStatuses[row].Add(failedIds.Contains(row.Sheet.ElementId) ? "PDF ошибка" : "PDF готов");
        }
    }

    private static void ApplyCadStatus(
        Dictionary<PrintSheetRow, List<string>> rowStatuses,
        IReadOnlyList<PrintSheetRow> rows,
        PrintCadExportResult result)
    {
        string formatName = PrintCadExportService.GetDisplayName(result.Format);
        HashSet<long> failedIds = result.Failures
            .Select(failure => failure.Item.ElementId)
            .ToHashSet();
        foreach (PrintSheetRow row in rows)
        {
            rowStatuses[row].Add(failedIds.Contains(row.Sheet.ElementId) ? $"{formatName} ошибка" : $"{formatName} готов");
        }
    }

    private void ResetExportStatuses()
    {
        foreach (PrintSheetRow row in sheetRows)
        {
            row.ExportStatus = string.Empty;
        }
    }

    private void UpdateExportState()
    {
        int selectedCount = sheetRows.Count(row => row.IsSelected && row.CanBePrinted);
        IReadOnlyList<PrintSheetInfo> loadedSheets = GetAllLoadedSheets();
        int selectedTotalCount = sheetSelectionState.CountSelected(loadedSheets);
        int printableCount = sheetRows.Count(row => row.CanBePrinted);
        int hiddenPlaceholderCount = includePlaceholdersInput.IsChecked == true
            ? 0
            : loadedSheets.Count(sheet => sheet.IsPlaceholder);
        if (GetSelectedOperationMode() == PrintOperationMode.Printer)
        {
            PrintPrinterOption? printer = GetSelectedPrinter();
            printerInput.IsEnabled = printerOptions.Count > 0;
            printSetupInput.IsEnabled = printSetupOptions.Count > 0;
            foreach (PrintSheetRow row in sheetRows)
            {
                row.ShowFileNameWarnings = false;
            }

            exportButton.Content = IconFactory.CreateButtonContent(
                TrueBimIcon.Print,
                "Печатать",
                Colors.White);
            exportButton.IsEnabled = selectedCount > 0 && printer is not null;
            exportButton.ToolTip = printer is null
                ? "В Windows не найден доступный принтер."
                : selectedCount == 0
                    ? "Выберите хотя бы один лист."
                    : $"Отправить выбранные листы в принтер «{printer.Name}».";

            string printHiddenText = hiddenPlaceholderCount > 0
                ? $" Скрыто неразмещенных листов (заглушек): {hiddenPlaceholderCount}."
                : string.Empty;
            string printSourceText = sheetSources.Count > 1
                ? $" Источников: {sheetSources.Count}. Фильтр: {GetSelectedSourceDisplayName()}."
                : string.Empty;
            string printSelectedTotalText = selectedTotalCount == selectedCount
                ? string.Empty
                : $" Всего выбрано: {selectedTotalCount}.";
            string setupText = printSetupInput.SelectedItem is PrintSetupOption setup
                ? setup.DisplayName
                : "текущая настройка каждого документа";
            string printerText = printer?.Name ?? "не найден";
            statusText.Text = $"Режим: печать. Листов: {sheetRows.Count}. Печатаемых: {printableCount}. Выбрано: {selectedCount}.{printSelectedTotalText}{printSourceText} Принтер: {printerText}. Диапазон: выбранные листы. Настройка Revit: {setupText}.{printHiddenText}";
            return;
        }

        exportButton.Content = IconFactory.CreateButtonContent(
            TrueBimIcon.Export,
            "Экспортировать",
            Colors.White);
        int duplicateSelectedCount = sheetRows.Count(row => row.IsSelected && row.IsFileNameDuplicate);
        int truncatedSelectedCount = sheetRows.Count(row => row.IsSelected && row.IsFileNameTruncated);
        int unknownTokenCount = sheetRows.Count(row => row.HasUnknownFileNameTokens);
        bool hasFormat = pdfInput.IsChecked == true
            || dwgInput.IsChecked == true
            || dxfInput.IsChecked == true
            || dwfInput.IsChecked == true;
        bool hasFolder = !string.IsNullOrWhiteSpace(exportFolderInput.Text);
        List<PrintSheetRow> selectedRows = sheetRows
            .Where(row => row.IsSelected && row.CanBePrinted)
            .ToList();
        bool useCombinedPdf = pdfInput.IsChecked == true
            && combinePdfInput.IsChecked == true;
        IReadOnlyDictionary<string, PrintFileNamePreview> combinedPdfPreviews = useCombinedPdf
            ? BuildCombinedFileNamePreviews(combinedPdfNameInput.Text, selectedRows)
            : new Dictionary<string, PrintFileNamePreview>(StringComparer.Ordinal);
        bool combinedPdfMaskHasUnknownTokens = combinedPdfPreviews.Values.Any(preview => preview.HasUnknownTokens);
        string? duplicateCombinedPdfFileName = FindDuplicateCombinedPdfFileName(combinedPdfPreviews);
        bool useCombinedDwg = dwgInput.IsChecked == true && combineDwgInput.IsChecked == true;
        IReadOnlyDictionary<string, PrintFileNamePreview> mergedDwgPreviews = useCombinedDwg
            ? BuildCombinedFileNamePreviews(combinedDwgNameMaskInput.Text, selectedRows)
            : new Dictionary<string, PrintFileNamePreview>(StringComparer.Ordinal);
        bool mergedDwgMaskHasUnknownTokens = mergedDwgPreviews.Values.Any(preview => preview.HasUnknownTokens);
        string? duplicateMergedDwgFileName = FindDuplicateMergedDwgFileName(mergedDwgPreviews);
        bool exportsPerSheetFiles = dwgInput.IsChecked == true && combineDwgInput.IsChecked != true
            || dxfInput.IsChecked == true
            || dwfInput.IsChecked == true
            || pdfInput.IsChecked == true && combinePdfInput.IsChecked != true;
        combinePdfInput.IsEnabled = pdfInput.IsChecked == true;
        combineDwgInput.IsEnabled = dwgInput.IsChecked == true;
        combinedDwgNameMaskInput.IsEnabled = useCombinedDwg;
        dwgSetupInput.IsEnabled = dwgInput.IsChecked == true && cadExportSetupOptions.Count > 1;
        dxfSetupInput.IsEnabled = dxfInput.IsChecked == true && cadExportSetupOptions.Count > 1;
        UpdateCombinedPdfNamePreview(combinedPdfPreviews);
        UpdateCombinedDwgNamePreview(mergedDwgPreviews);
        foreach (PrintSheetRow row in sheetRows)
        {
            row.ShowFileNameWarnings = exportsPerSheetFiles;
        }

        exportButton.IsEnabled = selectedCount > 0
            && hasFormat
            && hasFolder
            && !combinedPdfMaskHasUnknownTokens
            && duplicateCombinedPdfFileName is null
            && !mergedDwgMaskHasUnknownTokens
            && duplicateMergedDwgFileName is null;
        exportButton.ToolTip = combinedPdfMaskHasUnknownTokens
            ? "Исправьте неизвестные токены маски общего PDF или нажмите «Обновить»."
            : duplicateCombinedPdfFileName is not null
                ? "Добавьте в маску общего PDF токен, различающий документы."
                : mergedDwgMaskHasUnknownTokens
                    ? "Исправьте неизвестные токены маски общего DWG или нажмите «Обновить»."
                    : duplicateMergedDwgFileName is not null
                        ? "Добавьте в маску общего DWG токен, различающий документы."
                        : exportButton.IsEnabled
                            ? "Экспортировать выбранные листы в отмеченные форматы."
                            : "Выберите листы, хотя бы один формат и папку назначения.";

        string hiddenText = hiddenPlaceholderCount > 0
            ? $" Скрыто неразмещенных листов (заглушек): {hiddenPlaceholderCount}."
            : string.Empty;
        string duplicateText = exportsPerSheetFiles && duplicateSelectedCount > 0
            ? $" Дубли имен: {duplicateSelectedCount}."
            : string.Empty;
        string truncatedText = truncatedSelectedCount > 0
            ? $" Обрезанных имен: {truncatedSelectedCount}."
            : string.Empty;
        string unknownTokenText = unknownTokenCount > 0
            ? $" Неизвестные токены или параметры в маске: {unknownTokenCount}."
            : string.Empty;
        string combinedPdfMaskText = combinedPdfMaskHasUnknownTokens
            ? " Маска общего PDF содержит неизвестный токен."
            : duplicateCombinedPdfFileName is not null
                ? $" Имя общего PDF повторяется для нескольких документов: {duplicateCombinedPdfFileName}."
                : string.Empty;
        string mergedDwgMaskText = mergedDwgMaskHasUnknownTokens
            ? " Маска общего DWG содержит неизвестный токен."
            : duplicateMergedDwgFileName is not null
                ? $" Имя общего DWG повторяется для нескольких документов: {duplicateMergedDwgFileName}."
                : string.Empty;
        string sourceText = sheetSources.Count > 1
            ? $" Источников: {sheetSources.Count}. Фильтр: {GetSelectedSourceDisplayName()}."
            : string.Empty;
        string selectedTotalText = selectedTotalCount == selectedCount
            ? string.Empty
            : $" Всего выбрано: {selectedTotalCount}.";
        string formatText = $" Форматы: {GetSelectedFormatsText()}.";
        statusText.Text = $"Режим: экспорт. Листов: {sheetRows.Count}. Печатаемых: {printableCount}. Выбрано: {selectedCount}.{selectedTotalText}{sourceText}{formatText}{hiddenText}{duplicateText}{truncatedText}{unknownTokenText}{combinedPdfMaskText}{mergedDwgMaskText}";
    }

    private PrintOperationMode GetSelectedOperationMode()
    {
        return operationModeInput.SelectedValue is PrintOperationMode mode
            ? mode
            : PrintOperationMode.Export;
    }

    private PrintPrinterOption? GetSelectedPrinter()
    {
        return printerInput.SelectedItem as PrintPrinterOption;
    }

    private string? GetSelectedPrintSetupName()
    {
        return printSetupInput.SelectedItem is PrintSetupOption option
            ? option.SetupName
            : null;
    }

    private string GetSelectedFormatsText()
    {
        List<string> formats = new();
        if (pdfInput.IsChecked == true)
        {
            formats.Add(combinePdfInput.IsChecked == true ? "PDF (один файл)" : "PDF");
        }

        if (dwgInput.IsChecked == true)
        {
            formats.Add(combineDwgInput.IsChecked == true ? "DWG (один файл)" : "DWG");
        }

        if (dxfInput.IsChecked == true)
        {
            formats.Add("DXF");
        }

        if (dwfInput.IsChecked == true)
        {
            formats.Add("DWF");
        }

        return formats.Count == 0
            ? "не выбраны"
            : string.Join(", ", formats);
    }

    private PrintPdfExportMode GetSelectedPdfMode()
    {
        return combinePdfInput.IsChecked == true
            ? PrintPdfExportMode.CombinedFile
            : PrintPdfExportMode.SeparateFiles;
    }

    private PrintPdfExportSettings GetSelectedPdfSettings()
    {
        return new PrintPdfExportSettings(
            GetSelectedPdfColorMode(),
            GetSelectedPdfRasterQuality(),
            forceRasterPdfInput.IsChecked == true);
    }

    private PrintPdfColorMode GetSelectedPdfColorMode()
    {
        return pdfColorModeInput.SelectedValue is PrintPdfColorMode mode
            ? mode
            : PrintPdfExportService.DefaultSettings.ColorMode;
    }

    private PrintPdfRasterQuality GetSelectedPdfRasterQuality()
    {
        return pdfRasterQualityInput.SelectedValue is PrintPdfRasterQuality quality
            ? quality
            : PrintPdfExportService.DefaultSettings.RasterQuality;
    }

    private string GetSelectedCadSetupsText()
    {
        List<string> setupDisplays = new();
        if (dwgInput.IsChecked == true)
        {
            string dwgDisplay = PrintCadExportSetupService.GetSelectionDisplayName(
                PrintCadExportFormat.Dwg,
                dwgSetupInput.SelectedItem as PrintCadExportSetupOption);
            string profileDisplay = selectedDwgProfile.IsUserProfile
                ? $"профиль: {selectedDwgProfile.ProfileName}"
                : "профиль: Revit setup";
            setupDisplays.Add(combineDwgInput.IsChecked == true
                ? $"{dwgDisplay}, один файл, {profileDisplay}"
                : $"{dwgDisplay}, {profileDisplay}");
        }

        if (dxfInput.IsChecked == true)
        {
            setupDisplays.Add(PrintCadExportSetupService.GetSelectionDisplayName(
                PrintCadExportFormat.Dxf,
                dxfSetupInput.SelectedItem as PrintCadExportSetupOption));
        }

        if (dwfInput.IsChecked == true)
        {
            setupDisplays.Add("DWF: без CAD setup");
        }

        return setupDisplays.Count == 0
            ? string.Empty
            : $" CAD настройки: {string.Join("; ", setupDisplays)}.";
    }

    private string? GetSelectedSourceId()
    {
        return GetSelectedSourceFilterOption()?.SourceId;
    }

    private PrintSheetSourceFilterOption? GetSelectedSourceFilterOption()
    {
        return sourceFilterInput.SelectedItem as PrintSheetSourceFilterOption;
    }

    private string GetSelectedSourceDisplayName()
    {
        return sourceFilterInput.SelectedItem is PrintSheetSourceFilterOption option
            ? option.DisplayName
            : "Все открытые документы";
    }

    private IReadOnlyList<PrintSheetInfo> GetAllLoadedSheets()
    {
        return sourceSheetsById.Values.SelectMany(sourceSheets => sourceSheets).ToList();
    }

    private static string? GetSelectedSetupName(ComboBox setupInput)
    {
        return setupInput.SelectedItem is PrintCadExportSetupOption option
            ? option.SetupName
            : null;
    }

    private string GetInitialExportFolder()
    {
        string? savedExportFolder = initialSettings.ExportFolder;
        if (!string.IsNullOrWhiteSpace(savedExportFolder))
        {
            return savedExportFolder!;
        }

        if (!hasSavedPrintSettings && !string.IsNullOrWhiteSpace(dwgProfileState.LastFolder))
        {
            return dwgProfileState.LastFolder!;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(document.PathName))
            {
                string? documentFolder = Path.GetDirectoryName(document.PathName);
                if (!string.IsNullOrWhiteSpace(documentFolder))
                {
                    return documentFolder;
                }
            }
        }
        catch (ArgumentException)
        {
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void ApplyInitialSettings()
    {
        PrintSettings settings = PrintSettingsService.Normalize(initialSettings);
        if (!hasSavedPrintSettings)
        {
            settings = settings with
            {
                FileNameMask = !string.IsNullOrWhiteSpace(dwgProfileState.LastNameMask)
                    ? dwgProfileState.LastNameMask!
                    : settings.FileNameMask,
                CombinedPdfFileName = PrintFileNameTemplateService.DefaultCombinedTemplate
            };
        }

        ApplySettingsToInputs(settings);
    }

    private void SavePrintSettings()
    {
        printSettingsService?.Save(ReadCurrentPrintSettings());
        SaveDwgProfileState();
        SavePrintPresetSelection();
    }

    private PrintSettings ReadCurrentPrintSettings()
    {
        return PrintSettingsService.Normalize(new PrintSettings(
            exportFolderInput.Text,
            fileNameMaskInput.Text,
            includePlaceholdersInput.IsChecked == true,
            pdfInput.IsChecked == true,
            combinePdfInput.IsChecked == true,
            combinedPdfNameInput.Text,
            GetSelectedPdfColorMode(),
            GetSelectedPdfRasterQuality(),
            forceRasterPdfInput.IsChecked == true,
            dwgInput.IsChecked == true,
            dxfInput.IsChecked == true,
            dwfInput.IsChecked == true,
            combineDwgInput.IsChecked == true,
            openExportFolderInput.IsChecked == true,
            GetSelectedSetupName(dwgSetupInput),
            GetSelectedSetupName(dxfSetupInput),
            combinedDwgNameMaskInput.Text));
    }

    private void ApplySettingsToInputs(PrintSettings sourceSettings)
    {
        PrintSettings settings = PrintSettingsService.Normalize(sourceSettings);
        exportFolderInput.Text = settings.ExportFolder ?? GetInitialExportFolder();
        fileNameMaskInput.Text = settings.FileNameMask;
        includePlaceholdersInput.IsChecked = settings.IncludePlaceholders;
        pdfInput.IsChecked = settings.ExportPdf;
        combinePdfInput.IsChecked = settings.CombinePdf;
        combinedPdfNameInput.Text = settings.CombinedPdfFileName;
        pdfColorModeInput.SelectedValue = settings.PdfColorMode;
        pdfRasterQualityInput.SelectedValue = settings.PdfRasterQuality;
        forceRasterPdfInput.IsChecked = settings.AlwaysUseRasterPdf;
        dwgInput.IsChecked = settings.ExportDwg;
        dxfInput.IsChecked = settings.ExportDxf;
        dwfInput.IsChecked = settings.ExportDwf;
        combineDwgInput.IsChecked = settings.CombineDwg;
        openExportFolderInput.IsChecked = settings.OpenExportFolderAfterCompletion;
        combinedDwgNameMaskInput.Text = settings.CombinedDwgFileNameMask;
        dwgSetupInput.SelectedItem = FindCadSetupOption(settings.DwgSetupName) ?? cadExportSetupOptions.FirstOrDefault();
        dxfSetupInput.SelectedItem = FindCadSetupOption(settings.DxfSetupName) ?? cadExportSetupOptions.FirstOrDefault();
    }

    private void SaveDwgProfileState()
    {
        DwgExportProfile currentProfile = GetCurrentDwgProfileForExport();
        if (currentProfile.IsUserProfile)
        {
            dwgProfileState.UpsertProfile(currentProfile);
        }

        dwgProfileState.LastSelectedProfileName = currentProfile.ProfileName;
        dwgProfileState.LastFolder = exportFolderInput.Text;
        dwgProfileState.LastNameMask = fileNameMaskInput.Text;
        dwgProfileState.LastFormatSelection = GetSelectedFormatsText();
        dwgProfileStorage.Save(dwgProfileState);
    }

    private void InitializePrintPresets()
    {
        if (printPresetState.Presets.Count == 0)
        {
            printPresetState.UpsertPreset(CaptureCurrentPrintPreset(PrintPreset.DefaultPresetName));
            printPresetStorage.Save(printPresetState);
        }

        PrintPreset selectedPreset = printPresetState.FindPreset(printPresetState.LastSelectedPresetName)
            ?? printPresetState.Presets[0];
        PrintSettings selectedSettings = PrintSettingsService.Normalize(
            selectedPreset.Settings ?? PrintSettingsService.DefaultSettings);
        reloadInitialSourcesForPreset = !string.Equals(
                selectedSettings.FileNameMask,
                collectedFileNameMask,
                StringComparison.Ordinal)
            || !string.Equals(
                selectedSettings.CombinedPdfFileName,
                collectedCombinedPdfFileNameMask,
                StringComparison.Ordinal)
            || !string.Equals(
                selectedSettings.CombinedDwgFileNameMask,
                collectedCombinedDwgFileNameMask,
                StringComparison.Ordinal);
        RefreshPresetInput(selectedPreset.Name);
        ApplyPrintPreset(selectedPreset, requestSheetReload: false);
    }

    private void ApplySelectedPrintPreset()
    {
        if (isApplyingPreset || presetInput.SelectedItem is not PrintPreset preset)
        {
            return;
        }

        ApplyPrintPreset(preset, requestSheetReload: true);
    }

    private void ApplyPrintPreset(PrintPreset preset, bool requestSheetReload)
    {
        PrintPreset normalizedPreset = PrintPresetStorage.NormalizePreset(preset);
        isApplyingPreset = true;
        try
        {
            selectedDwgProfile = normalizedPreset.DwgProfile!.Clone();
            ApplySettingsToInputs(normalizedPreset.Settings!);
            ApplyDwgProfileToSetupInput();
            printPresetState.LastSelectedPresetName = normalizedPreset.Name;
        }
        finally
        {
            isApplyingPreset = false;
        }

        UpdateDwgProfileIndicator();
        UpdatePdfOptionsState();
        UpdateFileNamePreviews();
        statusText.Text = $"Пресет применен: {normalizedPreset.Name}.";
        if (requestSheetReload)
        {
            RequestLoadSheets();
        }
    }

    private void SaveCurrentPrintPreset()
    {
        string presetName = PrintPresetStorage.NormalizePresetName(presetInput.Text);
        PrintPreset preset = CaptureCurrentPrintPreset(presetName);
        printPresetState.UpsertPreset(preset);
        printPresetState = PrintPresetStorage.Normalize(printPresetState);
        printPresetStorage.Save(printPresetState);
        RefreshPresetInput(presetName);
        statusText.Text = $"Пресет сохранен локально: {presetName}.";
    }

    private void DeleteSelectedPrintPreset()
    {
        PrintPreset? preset = printPresetState.FindPreset(presetInput.Text)
            ?? presetInput.SelectedItem as PrintPreset;
        if (preset is null)
        {
            MessageBox.Show(this, "Выберите сохраненный пресет для удаления.", WindowTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            $"Удалить локальный пресет «{preset.Name}»?",
            WindowTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        printPresetState.RemovePreset(preset.Name);
        if (printPresetState.Presets.Count == 0)
        {
            printPresetState.UpsertPreset(CaptureCurrentPrintPreset(PrintPreset.DefaultPresetName));
        }

        PrintPreset nextPreset = printPresetState.Presets[0];
        printPresetState.LastSelectedPresetName = nextPreset.Name;
        printPresetStorage.Save(printPresetState);
        RefreshPresetInput(nextPreset.Name);
        ApplyPrintPreset(nextPreset, requestSheetReload: true);
    }

    private PrintPreset CaptureCurrentPrintPreset(string presetName)
    {
        return new PrintPreset
        {
            Name = presetName,
            Settings = ReadCurrentPrintSettings(),
            DwgProfile = GetCurrentDwgProfileForExport()
        };
    }

    private void RefreshPresetInput(string selectedPresetName)
    {
        isApplyingPreset = true;
        try
        {
            presetInput.ItemsSource = null;
            presetInput.ItemsSource = printPresetState.Presets;
            presetInput.SelectedItem = printPresetState.FindPreset(selectedPresetName);
            presetInput.Text = selectedPresetName;
        }
        finally
        {
            isApplyingPreset = false;
        }
    }

    private void SavePrintPresetSelection()
    {
        PrintPreset? selectedPreset = printPresetState.FindPreset(presetInput.Text)
            ?? presetInput.SelectedItem as PrintPreset;
        if (selectedPreset is not null)
        {
            printPresetState.LastSelectedPresetName = selectedPreset.Name;
        }

        printPresetStorage.Save(printPresetState);
    }

    private static DataGridTextColumn CreateTextColumn(string header, string bindingPath, double width)
    {
        return CreateTextColumn(header, bindingPath, new DataGridLength(width));
    }

    private static DataGridTextColumn CreateTextColumn(string header, string bindingPath, DataGridLength width)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(bindingPath),
            SortMemberPath = bindingPath,
            Width = width,
            IsReadOnly = true
        };
    }

    private DataGridTemplateColumn CreateSelectionColumn()
    {
        FrameworkElementFactory checkBox = new(typeof(CheckBox));
        checkBox.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        checkBox.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        checkBox.SetBinding(
            UIElement.IsEnabledProperty,
            new Binding(nameof(PrintSheetRow.CanBePrinted)));
        checkBox.SetBinding(
            CheckBox.IsCheckedProperty,
            new Binding(nameof(PrintSheetRow.IsSelected))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        checkBox.AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnSheetSelectionCheckBoxPreviewMouseLeftButtonDown));
        checkBox.AddHandler(
            CheckBox.ClickEvent,
            new RoutedEventHandler(OnSheetSelectionCheckBoxClick));

        return new DataGridTemplateColumn
        {
            Header = "Выбран",
            CellTemplate = new DataTemplate
            {
                VisualTree = checkBox
            },
            Width = 72
        };
    }

    private static DataGridTemplateColumn CreateStatusColumn()
    {
        FrameworkElementFactory badge = new(typeof(Border));
        badge.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        badge.SetValue(Border.BorderThicknessProperty, new Thickness(TrueBimTheme.BorderWidth));
        badge.SetValue(Border.PaddingProperty, TrueBimTheme.BadgePadding);
        badge.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        badge.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        badge.SetBinding(Border.BackgroundProperty, new Binding(nameof(PrintSheetRow.Status)) { Converter = new PrintStatusBackgroundConverter() });
        badge.SetBinding(Border.BorderBrushProperty, new Binding(nameof(PrintSheetRow.Status)) { Converter = new PrintStatusBrushConverter() });
        badge.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(PrintSheetRow.StatusToolTip)));

        FrameworkElementFactory text = new(typeof(TextBlock));
        text.SetValue(TextBlock.FontSizeProperty, TrueBimTheme.CaptionFontSize);
        text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(FrameworkElement.MinWidthProperty, 84.0);
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(PrintSheetRow.Status)));
        text.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(PrintSheetRow.Status)) { Converter = new PrintStatusBrushConverter() });
        badge.AppendChild(text);

        return new DataGridTemplateColumn
        {
            Header = "Статус",
            CellTemplate = new DataTemplate
            {
                VisualTree = badge
            },
            Width = 190,
            IsReadOnly = true
        };
    }

    private void ConfigureSheetGroupStyles(bool includeSourceLevel)
    {
        sheetGrid.GroupStyle.Clear();
        if (includeSourceLevel)
        {
            sheetGrid.GroupStyle.Add(CreateSheetGroupStyle("Источник: {0}"));
        }

        sheetGrid.GroupStyle.Add(CreateSheetGroupStyle("Том / группа: {0}"));
    }

    private static GroupStyle CreateSheetGroupStyle(string headerFormat)
    {
        FrameworkElementFactory expander = new(typeof(Expander));
        expander.SetValue(Expander.IsExpandedProperty, true);
        expander.SetBinding(HeaderedContentControl.HeaderProperty, new Binding("Name") { StringFormat = headerFormat });
        FrameworkElementFactory presenter = new(typeof(ItemsPresenter));
        expander.AppendChild(presenter);

        ControlTemplate template = new(typeof(GroupItem))
        {
            VisualTree = expander
        };
        Style containerStyle = new(typeof(GroupItem));
        containerStyle.Setters.Add(new Setter(Control.TemplateProperty, template));

        return new GroupStyle
        {
            ContainerStyle = containerStyle
        };
    }

    private static Style CreateSheetRowStyle()
    {
        Style style = TrueBimStyles.CreateDataGridRowStyle();
        style.Triggers.Add(new DataTrigger
        {
            Binding = new Binding(nameof(PrintSheetRow.SourceIsLinked)),
            Value = true,
            Setters =
            {
                new Setter(Control.BackgroundProperty, TrueBimBrushes.InfoBackground)
            }
        });
        return style;
    }

    private void OnSheetGridSorting(object sender, DataGridSortingEventArgs args)
    {
        if (!string.Equals(
                args.Column.SortMemberPath,
                nameof(PrintSheetRow.SheetNumber),
                StringComparison.Ordinal))
        {
            return;
        }

        ICollectionView view = CollectionViewSource.GetDefaultView(sheetRows);
        if (view is not ListCollectionView listView)
        {
            return;
        }

        isSheetNumberSortDescending = args.Column.SortDirection == ListSortDirection.Ascending;
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            listView.CustomSort = new PrintSheetRowComparer(
                sheetSourceOrderById,
                isSheetNumberSortDescending);
        }

        UpdateSheetNumberSortIndicator();
        args.Handled = true;
    }

    private void UpdateSheetNumberSortIndicator()
    {
        foreach (DataGridColumn column in sheetGrid.Columns)
        {
            column.SortDirection = string.Equals(
                column.SortMemberPath,
                nameof(PrintSheetRow.SheetNumber),
                StringComparison.Ordinal)
                    ? isSheetNumberSortDescending
                        ? ListSortDirection.Descending
                        : ListSortDirection.Ascending
                    : null;
        }
    }

    private void OnSheetGridKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Space || sheetGrid.SelectedItems.Count == 0)
        {
            return;
        }

        List<PrintSheetRow> selectedRows = sheetGrid.SelectedItems
            .OfType<PrintSheetRow>()
            .Where(row => row.CanBePrinted)
            .ToList();
        if (selectedRows.Count == 0)
        {
            return;
        }

        bool shouldSelect = selectedRows.Any(row => !row.IsSelected);
        SetRowsSelected(selectedRows, shouldSelect);

        args.Handled = true;
    }

    private void OnSheetSelectionCheckBoxClick(object sender, RoutedEventArgs args)
    {
        if (sender is CheckBox { DataContext: PrintSheetRow row } && row.CanBePrinted)
        {
            sheetSelectionAnchor = row;
        }
    }

    private void OnSheetSelectionCheckBoxPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0
            || sheetSelectionAnchor is null
            || sender is not CheckBox { DataContext: PrintSheetRow targetRow }
            || !targetRow.CanBePrinted)
        {
            return;
        }

        List<PrintSheetRow> orderedRows = CollectionViewSource
            .GetDefaultView(sheetRows)
            .Cast<object>()
            .OfType<PrintSheetRow>()
            .ToList();
        int anchorIndex = orderedRows.IndexOf(sheetSelectionAnchor);
        int targetIndex = orderedRows.IndexOf(targetRow);
        PrintSheetSelectionRange? selectionRange = PrintSheetSelectionRange.Resolve(
            orderedRows.Count,
            anchorIndex,
            targetIndex,
            sheetSelectionAnchor.IsSelected);
        if (selectionRange is null)
        {
            sheetSelectionAnchor = null;
            return;
        }

        List<PrintSheetRow> rangeRows = orderedRows.GetRange(
            selectionRange.StartIndex,
            selectionRange.Count);
        SetRowsSelected(
            rangeRows.Where(row => row.CanBePrinted),
            selectionRange.IsSelected);
        SelectSheetGridRows(rangeRows, targetRow);
        logger.Info(
            $"Print sheet range selection changed: selected={selectionRange.IsSelected}, rows={rangeRows.Count}.");

        args.Handled = true;
    }

    private void SelectSheetGridRows(IEnumerable<PrintSheetRow> rows, PrintSheetRow currentRow)
    {
        sheetGrid.SelectedItems.Clear();
        foreach (PrintSheetRow row in rows)
        {
            sheetGrid.SelectedItems.Add(row);
        }

        sheetGrid.CurrentItem = currentRow;
        sheetGrid.ScrollIntoView(currentRow);
    }

    private static ComboBox CreateCadSetupInput(string tooltip)
    {
        return new ComboBox
        {
            DisplayMemberPath = nameof(PrintCadExportSetupOption.DisplayName),
            MinHeight = TrueBimTheme.ControlHeight32,
            MinWidth = 220,
            ToolTip = tooltip
        };
    }

    private static ComboBox CreatePdfSettingInput(string tooltip)
    {
        return new ComboBox
        {
            DisplayMemberPath = "Value",
            SelectedValuePath = "Key",
            MinHeight = TrueBimTheme.ControlHeight32,
            MinWidth = 160,
            ToolTip = tooltip
        };
    }

    private Button CreateFileNameTokenButton(TextBox targetInput)
    {
        Button button = CreateActionButton("Токены", TrueBimIcon.Parameter, isEnabled: true);
        button.Margin = new Thickness(8, 0, 0, 0);
        button.ToolTip = "Выбрать токен и вставить его в позицию курсора этой маски.";
        button.Click += (_, _) => OpenFileNameTokenMenu(button, targetInput);
        return button;
    }

    private void OpenFileNameTokenMenu(Button owner, TextBox targetInput)
    {
        IReadOnlyList<PrintFileNameTokenOption> options = fileNameTokenCatalogService.BuildOptions(
            parameterCatalogsBySourceId.Values);
        ContextMenu menu = new()
        {
            PlacementTarget = owner,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };

        foreach (IGrouping<string, PrintFileNameTokenOption> category in options.GroupBy(
            option => option.Category,
            StringComparer.CurrentCultureIgnoreCase))
        {
            MenuItem categoryItem = new() { Header = category.Key };
            foreach (PrintFileNameTokenOption option in category)
            {
                MenuItem tokenItem = new()
                {
                    Header = option.DisplayName,
                    ToolTip = option.Token
                };
                tokenItem.Click += (_, _) => InsertFileNameToken(targetInput, option);
                categoryItem.Items.Add(tokenItem);
            }

            menu.Items.Add(categoryItem);
        }

        menu.IsOpen = true;
    }

    private void InsertFileNameToken(TextBox targetInput, PrintFileNameTokenOption option)
    {
        PrintFileNameTokenInsertion insertion = fileNameTokenCatalogService.InsertAtCaret(
            targetInput.Text,
            targetInput.CaretIndex,
            option.Token);
        targetInput.Text = insertion.Text;
        targetInput.CaretIndex = insertion.CaretIndex;
        targetInput.Focus();

        if (!string.Equals(option.Category, "Системные", StringComparison.CurrentCultureIgnoreCase))
        {
            RequestLoadSheets();
        }
    }

    private static Button CreateActionButton(string text, TrueBimIcon icon, bool isEnabled)
    {
        Button button = TrueBimUi.CreateSecondaryButton(text, icon, isEnabled: isEnabled);
        button.Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, 0);
        return button;
    }

    private static string GetRevitVersion(RevitDocument document)
    {
        try
        {
            string? version = document.Application?.VersionNumber;
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version!;
            }
        }
        catch (Exception)
        {
        }

        return "default";
    }

    private static PrintFileNameContext CreateFileNameContext(
        RevitDocument document,
        IReadOnlyCollection<string> projectParameterNames)
    {
        string documentName = string.IsNullOrWhiteSpace(document.Title)
            ? "Активный документ"
            : document.Title;

        try
        {
            IReadOnlyDictionary<string, string> projectParameters = CollectProjectParameters(document, projectParameterNames);
            return new PrintFileNameContext(
                documentName,
                document.ProjectInformation?.Name ?? string.Empty,
                document.ProjectInformation?.Number ?? string.Empty,
                DateTime.Now,
                projectParameters);
        }
        catch (Exception)
        {
            return new PrintFileNameContext(documentName, string.Empty, string.Empty, DateTime.Now, new Dictionary<string, string>());
        }
    }

    private static IReadOnlyDictionary<string, string> CollectProjectParameters(
        RevitDocument document,
        IReadOnlyCollection<string> requestedParameterNames)
    {
        Dictionary<string, string> parameters = new(StringComparer.CurrentCultureIgnoreCase);
        if (document.ProjectInformation is null)
        {
            return parameters;
        }

        foreach (string requestedParameterName in requestedParameterNames)
        {
            string parameterName = requestedParameterName.Trim();
            if (string.IsNullOrWhiteSpace(parameterName) || parameters.ContainsKey(parameterName))
            {
                continue;
            }

            Autodesk.Revit.DB.Parameter? parameter = document.ProjectInformation.LookupParameter(parameterName);
            if (parameter is null)
            {
                continue;
            }

            string value = parameter.AsValueString() ?? parameter.AsString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(value))
            {
                parameters[parameterName] = value.Trim();
            }
        }

        return parameters;
    }

    private static string CreateFallbackSourceId(RevitDocument document)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(document.PathName))
            {
                return document.PathName;
            }
        }
        catch (ArgumentException)
        {
        }

        return string.IsNullOrWhiteSpace(document.Title)
            ? "active-document"
            : document.Title;
    }

    private static IReadOnlyList<PrintSheetSource> CreateSingleSource(
        RevitDocument document,
        IReadOnlyList<PrintSheetInfo> sheets)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (sheets is null)
        {
            throw new ArgumentNullException(nameof(sheets));
        }

        return
        [
            new PrintSheetSource(
                sheets.FirstOrDefault()?.SourceId ?? CreateFallbackSourceId(document),
                string.IsNullOrWhiteSpace(document.Title) ? "Активный документ" : document.Title,
                PrintSheetSourceKind.OpenDocument,
                document,
                sheets)
        ];
    }

    private sealed class PrintSheetRow : INotifyPropertyChanged
    {
        private bool isSelected;
        private bool canBePrinted;
        private bool isPrintabilityVerified;
        private string fileNamePreview = string.Empty;
        private string exportStatus = string.Empty;
        private bool isFileNameDuplicate;
        private bool showFileNameWarnings = true;
        private bool isFileNameTruncated;
        private bool hasUnknownFileNameTokens;

        public PrintSheetRow(PrintSheetInfo sheet, bool isSelected)
        {
            Sheet = sheet;
            canBePrinted = sheet.CanBePrinted;
            isPrintabilityVerified = sheet.IsPlaceholder || !sheet.CanBePrinted;
            this.isSelected = canBePrinted && isSelected;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public PrintSheetInfo Sheet { get; }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (!CanBePrinted)
                {
                    value = false;
                }

                if (isSelected == value)
                {
                    return;
                }

                isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public string SourceName => Sheet.SourceIsLinked ? $"{Sheet.SourceName} (связь)" : Sheet.SourceName;

        public bool SourceIsLinked => Sheet.SourceIsLinked;

        public string GroupName => Sheet.GroupName;

        public string SheetNumber => Sheet.SheetNumber;

        public string SheetName => Sheet.SheetName;

        public string SheetFormat => Sheet.SheetFormat;

        public bool CanBePrinted => canBePrinted;

        public string FileNamePreview
        {
            get => fileNamePreview;
            private set
            {
                if (fileNamePreview == value)
                {
                    return;
                }

                fileNamePreview = value;
                NotifyChanged(nameof(FileNamePreview));
            }
        }

        public bool IsFileNameDuplicate
        {
            get => isFileNameDuplicate;
            set
            {
                if (isFileNameDuplicate == value)
                {
                    return;
                }

                isFileNameDuplicate = value;
                NotifyChanged(nameof(IsFileNameDuplicate));
                NotifyChanged(nameof(Status));
            }
        }

        public bool ShowFileNameWarnings
        {
            get => showFileNameWarnings;
            set
            {
                if (showFileNameWarnings == value)
                {
                    return;
                }

                showFileNameWarnings = value;
                NotifyChanged(nameof(Status));
            }
        }

        public bool IsFileNameTruncated
        {
            get => isFileNameTruncated;
            private set
            {
                if (isFileNameTruncated == value)
                {
                    return;
                }

                isFileNameTruncated = value;
                NotifyChanged(nameof(IsFileNameTruncated));
                NotifyChanged(nameof(Status));
            }
        }

        public bool HasUnknownFileNameTokens
        {
            get => hasUnknownFileNameTokens;
            private set
            {
                if (hasUnknownFileNameTokens == value)
                {
                    return;
                }

                hasUnknownFileNameTokens = value;
                NotifyChanged(nameof(HasUnknownFileNameTokens));
                NotifyChanged(nameof(Status));
            }
        }

        public string ExportStatus
        {
            get => exportStatus;
            set
            {
                if (exportStatus == value)
                {
                    return;
                }

                exportStatus = value;
                NotifyChanged(nameof(ExportStatus));
                NotifyChanged(nameof(Status));
            }
        }

        public string Status
        {
            get
            {
                if (Sheet.IsPlaceholder)
                {
                    return "Заглушка — не печатается";
                }

                if (IsFileNameDuplicate && ShowFileNameWarnings)
                {
                    return "Дубликат имени";
                }

                if (HasUnknownFileNameTokens && ShowFileNameWarnings)
                {
                    return "Неизвестный токен";
                }

                if (IsFileNameTruncated && ShowFileNameWarnings)
                {
                    return "Имя обрезано";
                }

                if (!string.IsNullOrWhiteSpace(ExportStatus))
                {
                    return ExportStatus;
                }

                if (!isPrintabilityVerified)
                {
                    return "Проверка при запуске";
                }

                return CanBePrinted ? "Готов" : "Не печатается";
            }
        }

        public string? StatusToolTip => Sheet.IsPlaceholder
            ? PlaceholderSheetExplanation
            : null;

        public void SetPrintability(bool canBePrinted, string status)
        {
            bool selectionChanged = !canBePrinted && isSelected;
            this.canBePrinted = canBePrinted;
            isPrintabilityVerified = true;
            if (selectionChanged)
            {
                isSelected = false;
                NotifyChanged(nameof(IsSelected));
            }

            ExportStatus = status;
            NotifyChanged(nameof(CanBePrinted));
            NotifyChanged(nameof(Status));
        }

        public void UpdateFileNamePreview(PrintFileNamePreview preview)
        {
            FileNamePreview = preview.FileName;
            IsFileNameTruncated = preview.WasTruncated;
            HasUnknownFileNameTokens = preview.HasUnknownTokens;
        }

        private void NotifyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private sealed record PrintabilityValidationResult(
        IReadOnlyList<PrintSheetRow> PrintableRows,
        IReadOnlyList<PrintSheetRow> RejectedRows);

    private sealed class PrintSheetRowComparer : System.Collections.IComparer
    {
        private readonly PrintSheetHierarchyComparer sheetComparer;

        public PrintSheetRowComparer(
            IReadOnlyDictionary<string, int> sourceOrderById,
            bool descendingSheetNumbers)
        {
            sheetComparer = new PrintSheetHierarchyComparer(
                sourceOrderById,
                descendingSheetNumbers);
        }

        public int Compare(object? x, object? y)
        {
            return sheetComparer.Compare(
                (x as PrintSheetRow)?.Sheet,
                (y as PrintSheetRow)?.Sheet);
        }
    }

    private sealed record PrintSheetSourceFilterOption(string? SourceId, string DisplayName, bool IncludeLinked);

    private enum ExistingFileDecision
    {
        Replace,
        Skip,
        Cancel
    }

    private sealed class PrintStatusBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return TrueBimBrushes.ForSeverity(GetStatusSeverity(value as string));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class PrintStatusBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return TrueBimBrushes.BackgroundForSeverity(GetStatusSeverity(value as string));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    private static TrueBimUiSeverity GetStatusSeverity(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return TrueBimUiSeverity.Neutral;
        }

        string normalizedStatus = status!;

        if (StatusContains(normalizedStatus, "ошибка"))
        {
            return TrueBimUiSeverity.Danger;
        }

        if (StatusContains(normalizedStatus, "дубликат")
            || StatusContains(normalizedStatus, "неизвест")
            || StatusContains(normalizedStatus, "обрез")
            || StatusContains(normalizedStatus, "заглуш")
            || StatusContains(normalizedStatus, "не печатается"))
        {
            return TrueBimUiSeverity.Warning;
        }

        if (StatusContains(normalizedStatus, "готов")
            || StatusContains(normalizedStatus, "экспорт"))
        {
            return TrueBimUiSeverity.Success;
        }

        return TrueBimUiSeverity.Info;
    }

    private static bool StatusContains(string status, string value)
    {
        return status.IndexOf(value, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    private static IReadOnlyList<KeyValuePair<PrintPdfColorMode, string>> GetPdfColorModeOptions()
    {
        return
        [
            new KeyValuePair<PrintPdfColorMode, string>(
                PrintPdfColorMode.Color,
                PrintPdfExportService.GetColorModeDisplayName(PrintPdfColorMode.Color)),
            new KeyValuePair<PrintPdfColorMode, string>(
                PrintPdfColorMode.GrayScale,
                PrintPdfExportService.GetColorModeDisplayName(PrintPdfColorMode.GrayScale)),
            new KeyValuePair<PrintPdfColorMode, string>(
                PrintPdfColorMode.BlackLine,
                PrintPdfExportService.GetColorModeDisplayName(PrintPdfColorMode.BlackLine))
        ];
    }

    private static IReadOnlyList<KeyValuePair<PrintPdfRasterQuality, string>> GetPdfRasterQualityOptions()
    {
        return
        [
            new KeyValuePair<PrintPdfRasterQuality, string>(
                PrintPdfRasterQuality.Low,
                PrintPdfExportService.GetRasterQualityDisplayName(PrintPdfRasterQuality.Low)),
            new KeyValuePair<PrintPdfRasterQuality, string>(
                PrintPdfRasterQuality.Medium,
                PrintPdfExportService.GetRasterQualityDisplayName(PrintPdfRasterQuality.Medium)),
            new KeyValuePair<PrintPdfRasterQuality, string>(
                PrintPdfRasterQuality.High,
                PrintPdfExportService.GetRasterQualityDisplayName(PrintPdfRasterQuality.High)),
            new KeyValuePair<PrintPdfRasterQuality, string>(
                PrintPdfRasterQuality.Presentation,
                PrintPdfExportService.GetRasterQualityDisplayName(PrintPdfRasterQuality.Presentation))
        ];
    }
}
