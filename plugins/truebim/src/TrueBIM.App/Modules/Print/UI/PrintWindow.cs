using System.Collections.ObjectModel;
using System.ComponentModel;
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
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;
using RevitDocument = Autodesk.Revit.DB.Document;

namespace TrueBIM.App.Modules.Print.UI;

public sealed class PrintWindow : TrueBimWindow
{
    private const double ExportLabelWidth = 132;

    private readonly ObservableCollection<PrintSheetRow> sheetRows = new();
    private readonly ObservableCollection<PrintSheetSourceFilterOption> sourceFilterOptions = new();
    private readonly IReadOnlyList<PrintSheetSource> sheetSources;
    private readonly Dictionary<string, PrintSheetSource> sheetSourcesById;
    private readonly Dictionary<string, List<PrintSheetInfo>> sourceSheetsById;
    private readonly HashSet<string> loadedSourceIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PrintFileNameContext> fileNameContextsBySourceId;
    private readonly PrintSheetSelectionState sheetSelectionState;
    private readonly RevitDocument document;
    private readonly ITrueBimLogger logger;
    private readonly PrintFileNameTemplateService fileNameTemplateService = new();
    private readonly PrintPdfExportService pdfExportService = new();
    private readonly PrintCadExportService cadExportService = new();
    private readonly PrintCadExportSetupService cadExportSetupService = new();
    private readonly DwgExportOptionsFactory dwgOptionsFactory = new();
    private readonly ObservableCollection<PrintCadExportSetupOption> cadExportSetupOptions = new();
    private readonly PrintSettingsService? printSettingsService;
    private readonly DwgExportProfileStorage? dwgProfileStorage;
    private readonly PrintSettings initialSettings;
    private readonly bool hasSavedPrintSettings;
    private readonly PrintFileNameContext fileNameContext;
    private readonly PrintExportProfile exportProfile;
    private readonly DataGrid sheetGrid = new();
    private readonly TextBlock statusText = new();
    private readonly ComboBox sourceFilterInput = new()
    {
        DisplayMemberPath = nameof(PrintSheetSourceFilterOption.DisplayName),
        Height = 32,
        MinWidth = 220,
        ToolTip = "Фильтр листов по открытому документу Revit."
    };
    private readonly TextBox exportFolderInput = new();
    private readonly TextBox fileNameMaskInput = new()
    {
        Text = PrintFileNameTemplateService.DefaultTemplate,
        ToolTip = "Маска имени файла. Доступны токены: {Номер листа}, {Имя листа}, {Номер проекта}, {Имя проекта}, {Имя документа}, {Дата:yyyy-MM-dd}, {Счетчик}, {Счетчик:000}. Старые английские токены тоже поддерживаются."
    };
    private readonly CheckBox includePlaceholdersInput = new()
    {
        Content = "Листы-заглушки",
        ToolTip = "Показывает листы-заглушки в таблице. Они не выбираются для печати."
    };
    private readonly CheckBox pdfInput = new()
    {
        Content = "PDF",
        IsChecked = true,
        ToolTip = "Добавить PDF в очередь экспорта."
    };
    private readonly CheckBox combinePdfInput = new()
    {
        Content = "Один PDF",
        ToolTip = "Объединить выбранные листы в один PDF файл."
    };
    private readonly CheckBox separatePdfWithCombinedInput = new()
    {
        Content = "И отдельные PDF",
        ToolTip = "В режиме одного PDF дополнительно сохранить отдельный PDF на каждый лист."
    };
    private readonly TextBox combinedPdfNameInput = new()
    {
        ToolTip = "Имя файла для объединенного PDF."
    };
    private readonly ComboBox pdfColorModeInput = CreatePdfSettingInput("Цветовой режим PDF.");
    private readonly ComboBox pdfRasterQualityInput = CreatePdfSettingInput("Качество растровых элементов PDF.");
    private readonly CheckBox forceRasterPdfInput = new()
    {
        Content = "Растр",
        ToolTip = "Принудительно растрировать PDF вместо векторного вывода."
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
    private readonly ComboBox dwgSetupInput = CreateCadSetupInput("Настройка экспорта DWG из сохраненных настроек Revit.");
    private readonly ComboBox dxfSetupInput = CreateCadSetupInput("Настройка экспорта DXF из сохраненных настроек Revit.");
    private readonly TextBlock dwgProfileSourceText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Button exportButton = TrueBimUi.CreatePrimaryButton("Экспорт", TrueBimIcon.Export, isEnabled: false);
    private DwgExportProfileStoreState dwgProfileState = new();
    private DwgExportProfile selectedDwgProfile = DwgExportOptionsFactory.CreateProfileFromOptions(
        DwgExportProfile.DefaultProfileName,
        sourceRevitSetupName: null,
        usePredefinedRevitSetup: false,
        isUserProfile: false,
        new Autodesk.Revit.DB.DWGExportOptions());
    private bool isApplyingDwgProfileToSetupInput;

    private bool IsPdfProfile => exportProfile == PrintExportProfile.Pdf;

    private bool IsDwgProfile => exportProfile == PrintExportProfile.Dwg;

    private string WindowTitle => IsPdfProfile ? "Печать PDF" : "Печать DWG";

    public PrintWindow(RevitDocument document, IReadOnlyList<PrintSheetInfo> sheets, ITrueBimLogger logger)
        : this(document, CreateSingleSource(document, sheets), PrintExportProfile.Pdf, logger)
    {
    }

    public PrintWindow(RevitDocument document, IReadOnlyList<PrintSheetSource> sheetSources, ITrueBimLogger logger)
        : this(document, sheetSources, PrintExportProfile.Pdf, printSettingsService: null, logger)
    {
    }

    public PrintWindow(
        RevitDocument document,
        IReadOnlyList<PrintSheetSource> sheetSources,
        PrintSettingsService? printSettingsService,
        ITrueBimLogger logger)
        : this(document, sheetSources, PrintExportProfile.Pdf, printSettingsService, logger)
    {
    }

    public PrintWindow(
        RevitDocument document,
        IReadOnlyList<PrintSheetSource> sheetSources,
        PrintExportProfile exportProfile,
        ITrueBimLogger logger)
        : this(document, sheetSources, exportProfile, printSettingsService: null, logger)
    {
    }

    public PrintWindow(
        RevitDocument document,
        IReadOnlyList<PrintSheetSource> sheetSources,
        PrintExportProfile exportProfile,
        PrintSettingsService? printSettingsService,
        ITrueBimLogger logger)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        this.sheetSources = sheetSources ?? throw new ArgumentNullException(nameof(sheetSources));
        this.exportProfile = Enum.IsDefined(typeof(PrintExportProfile), exportProfile)
            ? exportProfile
            : PrintExportProfile.Pdf;
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
        sheetSelectionState = new PrintSheetSelectionState(GetAllLoadedSheets());
        sheetSourcesById = this.sheetSources.ToDictionary(source => source.SourceId, StringComparer.Ordinal);
        fileNameContextsBySourceId = this.sheetSources.ToDictionary(
            source => source.SourceId,
            source => CreateFileNameContext(source.Document),
            StringComparer.Ordinal);
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.printSettingsService = printSettingsService;
        hasSavedPrintSettings = printSettingsService?.SettingsFileExists == true;
        initialSettings = printSettingsService?.Load() ?? PrintSettingsService.DefaultSettings;
        if (IsDwgProfile)
        {
            dwgProfileStorage = new DwgExportProfileStorage(
                DwgExportProfileStorage.CreateStoragePath(GetRevitVersion(document)),
                logger);
            dwgProfileState = dwgProfileStorage.Load();
        }

        fileNameContext = CreateFileNameContext(document);
        LoadSourceFilterOptions();
        LoadCadExportSetupOptions();
        LoadInitialDwgProfile();
        ApplyInitialSettings();

        Title = WindowTitle;
        Icon = IconFactory.CreateImage(TrueBimIcon.Print, 32);
        exportButton.Content = IconFactory.CreateButtonContent(
            TrueBimIcon.Export,
            IsPdfProfile ? "Экспорт PDF" : "Экспорт DWG",
            Colors.White);
        Width = 1120;
        Height = 720;
        MinWidth = 980;
        MinHeight = 620;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ApplySharedControlStyles();
        Content = CreateContent();

        LoadSheets();
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
                "Печать / Экспорт",
                "Пакетная печать PDF, DWG, DXF и DWF из выбранных листов.",
                TrueBimIcon.Print),
            commandBar: CreateReadSettings(),
            body: CreateSheetsSection(),
            status: CreateStatus(),
            footer: CreateExportSettings());
    }

    private UIElement CreateSheetsSection()
    {
        return TrueBimUi.CreateSectionCard("Листы для экспорта", CreateSheetGrid());
    }

    private UIElement CreateReadSettings()
    {
        DockPanel controls = new()
        {
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12)
        };

        StackPanel selectionActions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
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
        refreshButton.ToolTip = "Перечитать список листов, уже собранный при открытии окна.";
        refreshButton.Click += (_, _) => LoadSheets();
        selectionActions.Children.Add(refreshButton);

        includePlaceholdersInput.VerticalAlignment = VerticalAlignment.Center;
        includePlaceholdersInput.Checked += (_, _) => LoadSheets();
        includePlaceholdersInput.Unchecked += (_, _) => LoadSheets();
        includePlaceholdersInput.Margin = new Thickness(0, 0, TrueBimTheme.Spacing16, 0);
        selectionActions.Children.Add(includePlaceholdersInput);

        sourceFilterInput.ItemsSource = sourceFilterOptions;
        sourceFilterInput.SelectedItem = FindActiveSourceFilterOption() ?? sourceFilterOptions.FirstOrDefault();
        sourceFilterInput.IsEnabled = sourceFilterOptions.Count > 1;
        sourceFilterInput.SelectionChanged += (_, _) => LoadSheets();
        selectionActions.Children.Add(sourceFilterInput);

        DockPanel.SetDock(selectionActions, Dock.Left);
        controls.Children.Add(selectionActions);

        TextBlock documentText = new()
        {
            Text = sheetSources.Count > 1
                ? $"Источников: {sheetSources.Count}. Активный: {(string.IsNullOrWhiteSpace(document.Title) ? "Активный документ" : document.Title)}"
                : string.IsNullOrWhiteSpace(document.Title) ? "Активный документ" : document.Title,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        controls.Children.Add(documentText);

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
        sheetGrid.ItemsSource = sheetRows;
        ICollectionView groupedView = CollectionViewSource.GetDefaultView(sheetRows);
        groupedView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PrintSheetRow.GroupName)));
        groupedView.SortDescriptions.Add(new SortDescription(nameof(PrintSheetRow.GroupName), ListSortDirection.Ascending));
        groupedView.SortDescriptions.Add(new SortDescription(nameof(PrintSheetRow.SheetNumber), ListSortDirection.Ascending));
        groupedView.SortDescriptions.Add(new SortDescription(nameof(PrintSheetRow.SheetName), ListSortDirection.Ascending));
        sheetGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        sheetGrid.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        sheetGrid.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        sheetGrid.SelectionMode = DataGridSelectionMode.Extended;
        sheetGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
        sheetGrid.CanUserSortColumns = true;
        sheetGrid.KeyDown += OnSheetGridKeyDown;
        sheetGrid.GroupStyle.Add(CreateSheetGroupStyle());
        sheetGrid.RowStyle = CreateSheetRowStyle();
        sheetGrid.ToolTip = "Список листов. Shift выделяет диапазон, Space переключает выбор выделенных строк.";

        sheetGrid.Columns.Add(CreateSelectionColumn());
        sheetGrid.Columns.Add(CreateTextColumn("Источник", nameof(PrintSheetRow.SourceName), 150));
        sheetGrid.Columns.Add(CreateTextColumn("Группа", nameof(PrintSheetRow.GroupName), 120));
        sheetGrid.Columns.Add(CreateTextColumn("Номер", nameof(PrintSheetRow.SheetNumber), 110));
        sheetGrid.Columns.Add(CreateTextColumn("Имя листа", nameof(PrintSheetRow.SheetName), new DataGridLength(1, DataGridLengthUnitType.Star)));
        sheetGrid.Columns.Add(CreateTextColumn("Формат", nameof(PrintSheetRow.SheetFormat), 120));
        sheetGrid.Columns.Add(CreateStatusColumn());
        sheetGrid.Columns.Add(CreateTextColumn("Имя файла", nameof(PrintSheetRow.FileNamePreview), 240));

        return sheetGrid;
    }

    private UIElement CreateExportSettings()
    {
        Grid root = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing16, 0, 0)
        };
        for (int index = 0; index < 5; index++)
        {
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        int rowIndex = 0;

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

        Grid maskRow = new()
        {
            Margin = new Thickness(0, 8, 0, 0)
        };
        maskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ExportLabelWidth) });
        maskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        maskRow.Children.Add(new TextBlock
        {
            Text = "Маска имени",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        fileNameMaskInput.Height = 32;
        fileNameMaskInput.TextChanged += (_, _) => UpdateFileNamePreviews();
        Grid.SetColumn(fileNameMaskInput, 1);
        maskRow.Children.Add(fileNameMaskInput);

        Grid.SetRow(maskRow, rowIndex++);
        root.Children.Add(maskRow);

        if (IsPdfProfile)
        {
            Grid pdfRow = new()
            {
                Margin = new Thickness(0, 8, 0, 0)
            };
            pdfRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pdfRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pdfRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pdfRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            combinePdfInput.VerticalAlignment = VerticalAlignment.Center;
            combinePdfInput.Margin = new Thickness(0, 0, 16, 0);
            combinePdfInput.Checked += (_, _) => UpdatePdfOptionsState();
            combinePdfInput.Unchecked += (_, _) => UpdatePdfOptionsState();
            pdfRow.Children.Add(combinePdfInput);

            separatePdfWithCombinedInput.VerticalAlignment = VerticalAlignment.Center;
            separatePdfWithCombinedInput.Margin = new Thickness(0, 0, 16, 0);
            separatePdfWithCombinedInput.Checked += (_, _) => UpdatePdfOptionsState();
            separatePdfWithCombinedInput.Unchecked += (_, _) => UpdatePdfOptionsState();
            Grid.SetColumn(separatePdfWithCombinedInput, 1);
            pdfRow.Children.Add(separatePdfWithCombinedInput);

            TextBlock combinedPdfNameLabel = new()
            {
                Text = "PDF файл",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(combinedPdfNameLabel, 2);
            pdfRow.Children.Add(combinedPdfNameLabel);

            combinedPdfNameInput.Height = 32;
            combinedPdfNameInput.TextChanged += (_, _) =>
            {
                ResetExportStatuses();
                UpdateExportState();
            };
            Grid.SetColumn(combinedPdfNameInput, 3);
            pdfRow.Children.Add(combinedPdfNameInput);

            Grid.SetRow(pdfRow, rowIndex++);
            root.Children.Add(pdfRow);

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
                Text = "PDF цвет",
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
            forceRasterPdfInput.Checked += (_, _) => UpdatePdfOptionsState();
            forceRasterPdfInput.Unchecked += (_, _) => UpdatePdfOptionsState();
            Grid.SetColumn(forceRasterPdfInput, 4);
            pdfSettingsRow.Children.Add(forceRasterPdfInput);

            Grid.SetRow(pdfSettingsRow, rowIndex++);
            root.Children.Add(pdfSettingsRow);
        }

        if (IsDwgProfile)
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
        }

        DockPanel actionRow = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0)
        };

        if (IsDwgProfile)
        {
            StackPanel formatActions = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            dwgInput.Margin = new Thickness(0, 0, 16, 0);
            dxfInput.Margin = new Thickness(0, 0, 16, 0);
            dwfInput.Margin = new Thickness(0, 0, 16, 0);
            combineDwgInput.Margin = new Thickness(0, 0, 16, 0);
            dwgInput.Checked += (_, _) => UpdateExportState();
            dwgInput.Unchecked += (_, _) => UpdateExportState();
            dxfInput.Checked += (_, _) => UpdateExportState();
            dxfInput.Unchecked += (_, _) => UpdateExportState();
            dwfInput.Checked += (_, _) => UpdateExportState();
            dwfInput.Unchecked += (_, _) => UpdateExportState();
            combineDwgInput.Checked += (_, _) => UpdateExportState();
            combineDwgInput.Unchecked += (_, _) => UpdateExportState();
            formatActions.Children.Add(dwgInput);
            formatActions.Children.Add(combineDwgInput);
            formatActions.Children.Add(dxfInput);
            formatActions.Children.Add(dwfInput);

            DockPanel.SetDock(formatActions, Dock.Left);
            actionRow.Children.Add(formatActions);
        }

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        exportButton.Click += (_, _) => StartExport();
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

        UpdatePdfOptionsState();
        return root;
    }

    private void ApplySharedControlStyles()
    {
        sourceFilterInput.Style = TrueBimStyles.CreateComboBoxStyle();
        exportFolderInput.Style = TrueBimStyles.CreateTextBoxStyle();
        fileNameMaskInput.Style = TrueBimStyles.CreateTextBoxStyle();
        combinedPdfNameInput.Style = TrueBimStyles.CreateTextBoxStyle();
        pdfColorModeInput.Style = TrueBimStyles.CreateComboBoxStyle();
        pdfRasterQualityInput.Style = TrueBimStyles.CreateComboBoxStyle();
        dwgSetupInput.Style = TrueBimStyles.CreateComboBoxStyle();
        dxfSetupInput.Style = TrueBimStyles.CreateComboBoxStyle();
        includePlaceholdersInput.Style = TrueBimStyles.CreateCheckBoxStyle();
        pdfInput.Style = TrueBimStyles.CreateCheckBoxStyle();
        combinePdfInput.Style = TrueBimStyles.CreateCheckBoxStyle();
        separatePdfWithCombinedInput.Style = TrueBimStyles.CreateCheckBoxStyle();
        forceRasterPdfInput.Style = TrueBimStyles.CreateCheckBoxStyle();
        dwgInput.Style = TrueBimStyles.CreateCheckBoxStyle();
        dxfInput.Style = TrueBimStyles.CreateCheckBoxStyle();
        dwfInput.Style = TrueBimStyles.CreateCheckBoxStyle();
        combineDwgInput.Style = TrueBimStyles.CreateCheckBoxStyle();
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
        pdfColorModeInput.SelectionChanged += (_, _) => UpdatePdfOptionsState();
    }

    private void BindPdfRasterQualityInput()
    {
        pdfRasterQualityInput.ItemsSource = GetPdfRasterQualityOptions();
        pdfRasterQualityInput.SelectedValue = initialSettings.PdfRasterQuality;
        pdfRasterQualityInput.SelectionChanged += (_, _) => UpdatePdfOptionsState();
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
        if (!IsDwgProfile)
        {
            return;
        }

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
        if (!IsDwgProfile || isApplyingDwgProfileToSetupInput)
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
        if (!IsDwgProfile)
        {
            return;
        }

        isApplyingDwgProfileToSetupInput = true;
        dwgSetupInput.SelectedItem = FindCadSetupOption(selectedDwgProfile.SourceRevitSetupName)
            ?? cadExportSetupOptions.FirstOrDefault();
        isApplyingDwgProfileToSetupInput = false;
        UpdateDwgProfileIndicator();
    }

    private void OpenDwgSettings()
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
        if (!IsDwgProfile)
        {
            return;
        }

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
        separatePdfWithCombinedInput.IsEnabled = exportPdf && combinePdf;
        if (!exportPdf || !combinePdf)
        {
            separatePdfWithCombinedInput.IsChecked = false;
        }

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
        sheetRows.Clear();
        bool includePlaceholders = includePlaceholdersInput.IsChecked == true;
        PrintSheetSourceFilterOption? selectedSource = GetSelectedSourceFilterOption();
        EnsureSheetsLoaded(selectedSource);
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

        visibleSheets = visibleSheets
            .OrderBy(sheet => sheet.GroupName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(sheet => sheet.SheetNumber, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(sheet => sheet.SheetName, StringComparer.CurrentCultureIgnoreCase);

        List<PrintSheetRow> rows = new();
        foreach (PrintSheetInfo sheet in visibleSheets)
        {
            PrintSheetRow row = new(sheet, sheetSelectionState.Get(sheet));
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(PrintSheetRow.IsSelected))
                {
                    sheetSelectionState.Set(row.Sheet, row.IsSelected);
                    UpdateExportState();
                }
            };
            rows.Add(row);
        }

        foreach (PrintSheetRow row in rows)
        {
            sheetRows.Add(row);
        }

        UpdateFileNamePreviews();
    }

    private void EnsureSheetsLoaded(PrintSheetSourceFilterOption? selectedSource)
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
        foreach (PrintSheetSource source in sourcesToLoad)
        {
            if (loadedSourceIds.Contains(source.SourceId))
            {
                continue;
            }

            IReadOnlyList<PrintSheetInfo> sourceSheets = collector.Collect(
                source.Document,
                source.SourceId,
                source.SourceName,
                source.SourceKind);
            sourceSheetsById[source.SourceId] = sourceSheets.ToList();
            loadedSourceIds.Add(source.SourceId);
            logger.Info($"Loaded {sourceSheets.Count} sheets for print source '{source.SourceName}'.");
        }
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
        foreach (PrintSheetRow row in sheetRows.Where(row => row.CanBePrinted))
        {
            row.IsSelected = isSelected;
        }

        logger.Info($"Print sheet selection changed: selected={isSelected}, rows={sheetRows.Count}.");
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
        bool combineDwg)
    {
        List<string> existingPaths = GetExistingOutputPaths(selectedRows, pdfMode, combineDwg)
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
        bool combineDwg)
    {
        return GetOutputPathsForRow(row, pdfMode, combineDwg).Any(File.Exists);
    }

    private IEnumerable<string> GetExistingOutputPaths(
        IReadOnlyList<PrintSheetRow> selectedRows,
        PrintPdfExportMode pdfMode,
        bool combineDwg)
    {
        foreach (PrintSheetRow row in selectedRows)
        {
            foreach (string outputPath in GetOutputPathsForRow(row, pdfMode, combineDwg))
            {
                yield return outputPath;
            }
        }
    }

    private IEnumerable<string> GetOutputPathsForRow(
        PrintSheetRow row,
        PrintPdfExportMode pdfMode,
        bool combineDwg)
    {
        string exportFolder = exportFolderInput.Text;
        if (pdfInput.IsChecked == true && pdfMode is PrintPdfExportMode.SeparateFiles or PrintPdfExportMode.SeparateAndCombined)
        {
            yield return Path.Combine(exportFolder, PrintPdfExportService.NormalizePdfFileName(row.FileNamePreview));
        }

        if (pdfInput.IsChecked == true && pdfMode is PrintPdfExportMode.CombinedFile or PrintPdfExportMode.SeparateAndCombined)
        {
            yield return Path.Combine(exportFolder, PrintPdfExportService.BuildCombinedPdfFileName(BuildSourceCombinedPdfName(row.Sheet.SourceId)));
        }

        if (dwgInput.IsChecked == true)
        {
            string dwgFileName = combineDwg
                ? BuildMergedCadFileName(row.SourceName, PrintCadExportFormat.Dwg)
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

    private string BuildSourceCombinedPdfName(string sourceId)
    {
        bool exportCombinedPdfPerSource = sheetRows
            .Where(row => row.IsSelected && row.CanBePrinted)
            .Select(row => row.Sheet.SourceId)
            .Distinct(StringComparer.Ordinal)
            .Count() > 1;
        if (!exportCombinedPdfPerSource || !sheetSourcesById.TryGetValue(sourceId, out PrintSheetSource? source))
        {
            return combinedPdfNameInput.Text;
        }

        return $"{source.SourceName}_{combinedPdfNameInput.Text}";
    }

    private static string BuildMergedCadFileName(string sourceName, PrintCadExportFormat format)
    {
        return $"{sourceName}_{PrintCadExportService.GetDisplayName(format)}";
    }

    private void StartExport()
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
        bool combineDwg = combineDwgInput.IsChecked == true;
        PrintPdfExportMode pdfMode = GetSelectedPdfMode();
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

        ExistingFileDecision existingFileDecision = ResolveExistingFileDecision(selectedRows, pdfMode, combineDwg);
        if (existingFileDecision == ExistingFileDecision.Cancel)
        {
            return;
        }

        if (existingFileDecision == ExistingFileDecision.Skip)
        {
            selectedRows = selectedRows
                .Where(row => !HasExistingOutput(row, pdfMode, combineDwg))
                .ToList();
            if (selectedRows.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show(WindowTitle, "Все выбранные листы пропущены: файлы уже существуют.");
                UpdateExportState();
                return;
            }
        }

        logger.Info($"Print export requested for document '{document.Title}' with {selectedRows.Count} sheets. Formats: {formats}. PDF mode: {pdfModeLogText}. PDF settings: {pdfSettingsLogText}. CAD setups: {GetSelectedCadSetupsText()}. DWG profile: {(dwgProfile is null ? "not selected" : DwgExportOptionsFactory.GetProfileSummary(dwgProfile))}. Folder: {exportFolderInput.Text}. Mask: {fileNameMaskInput.Text}.");
        Dictionary<PrintSheetRow, List<string>> rowStatuses = selectedRows.ToDictionary(
            row => row,
            _ => new List<string>());
        int exportedCount = 0;
        int failureCount = 0;
        List<string> failureMessages = new();
        foreach (PrintSheetRow row in selectedRows)
        {
            row.ExportStatus = "Экспорт: в очереди";
        }

        IReadOnlyList<IGrouping<string, PrintSheetRow>> rowGroups = selectedRows
            .GroupBy(row => row.Sheet.SourceId)
            .ToList();
        bool exportCombinedPdfPerSource = exportPdf
            && pdfMode is PrintPdfExportMode.CombinedFile or PrintPdfExportMode.SeparateAndCombined
            && rowGroups.Count > 1;

        foreach (IGrouping<string, PrintSheetRow> rowGroup in rowGroups)
        {
            if (!sheetSourcesById.TryGetValue(rowGroup.Key, out PrintSheetSource? source))
            {
                failureCount += rowGroup.Count();
                failureMessages.Add($"Источник листов не найден: {rowGroup.Key}");
                continue;
            }

            IReadOnlyList<PrintSheetRow> sourceRows = rowGroup.ToList();
            string sourceCombinedPdfName = exportCombinedPdfPerSource
                ? BuildSourceCombinedPdfName(source.SourceId)
                : combinedPdfNameInput.Text;

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
                    BuildMergedCadFileName(source.SourceName, PrintCadExportFormat.Dwg),
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
        UpdateExportState();
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
        int duplicateSelectedCount = sheetRows.Count(row => row.IsSelected && row.IsFileNameDuplicate);
        int truncatedSelectedCount = sheetRows.Count(row => row.IsSelected && row.IsFileNameTruncated);
        int unknownTokenCount = sheetRows.Count(row => row.HasUnknownFileNameTokens);
        bool hasFormat = pdfInput.IsChecked == true || dwgInput.IsChecked == true || dxfInput.IsChecked == true || dwfInput.IsChecked == true;
        bool hasFolder = !string.IsNullOrWhiteSpace(exportFolderInput.Text);
        bool exportsPerSheetFiles = dwgInput.IsChecked == true && combineDwgInput.IsChecked != true
            || dxfInput.IsChecked == true
            || dwfInput.IsChecked == true
            || (pdfInput.IsChecked == true && combinePdfInput.IsChecked != true);
        combineDwgInput.IsEnabled = dwgInput.IsChecked == true;
        foreach (PrintSheetRow row in sheetRows)
        {
            row.ShowFileNameDuplicateWarning = exportsPerSheetFiles;
        }

        exportButton.IsEnabled = selectedCount > 0 && hasFormat && hasFolder;
        exportButton.ToolTip = exportButton.IsEnabled
            ? "Подготовить выбранные листы к экспорту."
            : "Выберите листы, формат и папку назначения.";

        string hiddenText = hiddenPlaceholderCount > 0
            ? $" Скрыто листов-заглушек: {hiddenPlaceholderCount}."
            : string.Empty;
        string duplicateText = exportsPerSheetFiles && duplicateSelectedCount > 0
            ? $" Дубли имен: {duplicateSelectedCount}."
            : string.Empty;
        string truncatedText = truncatedSelectedCount > 0
            ? $" Обрезанных имен: {truncatedSelectedCount}."
            : string.Empty;
        string unknownTokenText = unknownTokenCount > 0
            ? $" Неизвестные токены в маске: {unknownTokenCount}."
            : string.Empty;
        string sourceText = sheetSources.Count > 1
            ? $" Источников: {sheetSources.Count}. Фильтр: {GetSelectedSourceDisplayName()}."
            : string.Empty;
        string selectedTotalText = selectedTotalCount == selectedCount
            ? string.Empty
            : $" Всего выбрано: {selectedTotalCount}.";
        string pdfModeText = GetSelectedPdfModeText();
        string pdfSettingsText = GetSelectedPdfSettingsText();
        string cadSetupText = GetSelectedCadSetupsText();
        statusText.Text = $"Листов в таблице: {sheetRows.Count}. Печатаемых: {printableCount}. Выбрано: {selectedCount}.{selectedTotalText}{sourceText} Форматы: {GetSelectedFormatsText()}.{pdfModeText}{pdfSettingsText}{cadSetupText}{hiddenText}{duplicateText}{truncatedText}{unknownTokenText}";
    }

    private string GetSelectedFormatsText()
    {
        List<string> formats = new();
        if (pdfInput.IsChecked == true)
        {
            formats.Add("PDF");
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
        if (combinePdfInput.IsChecked != true)
        {
            return PrintPdfExportMode.SeparateFiles;
        }

        return separatePdfWithCombinedInput.IsChecked == true
            ? PrintPdfExportMode.SeparateAndCombined
            : PrintPdfExportMode.CombinedFile;
    }

    private string GetSelectedPdfModeText()
    {
        if (pdfInput.IsChecked != true)
        {
            return string.Empty;
        }

        PrintPdfExportMode mode = GetSelectedPdfMode();
        string details = mode is PrintPdfExportMode.CombinedFile or PrintPdfExportMode.SeparateAndCombined
            ? $": {PrintPdfExportService.BuildCombinedPdfFileName(combinedPdfNameInput.Text)}"
            : string.Empty;
        return $" PDF: {PrintPdfExportService.GetModeDisplayName(mode)}{details}.";
    }

    private PrintPdfExportSettings GetSelectedPdfSettings()
    {
        return new PrintPdfExportSettings(
            GetSelectedPdfColorMode(),
            GetSelectedPdfRasterQuality(),
            forceRasterPdfInput.IsChecked == true);
    }

    private string GetSelectedPdfSettingsText()
    {
        return pdfInput.IsChecked == true
            ? $" PDF настройки: {PrintPdfExportService.GetSettingsDisplayName(GetSelectedPdfSettings())}."
            : string.Empty;
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

        if (IsDwgProfile && !hasSavedPrintSettings && !string.IsNullOrWhiteSpace(dwgProfileState.LastFolder))
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
        includePlaceholdersInput.IsChecked = settings.IncludePlaceholders;
        fileNameMaskInput.Text = IsDwgProfile
            && !hasSavedPrintSettings
            && !string.IsNullOrWhiteSpace(dwgProfileState.LastNameMask)
            ? dwgProfileState.LastNameMask!
            : settings.FileNameMask;
        pdfInput.IsChecked = IsPdfProfile;
        combinePdfInput.IsChecked = settings.CombinePdf;
        combinedPdfNameInput.Text = hasSavedPrintSettings
            ? settings.CombinedPdfFileName
            : PrintPdfExportService.BuildCombinedPdfFileName(fileNameContext.DocumentName);
        forceRasterPdfInput.IsChecked = settings.AlwaysUseRasterPdf;
        bool hasSavedCadFormat = settings.ExportDwg || settings.ExportDxf || settings.ExportDwf;
        dwgInput.IsChecked = IsDwgProfile && (hasSavedCadFormat ? settings.ExportDwg : true);
        dxfInput.IsChecked = IsDwgProfile && settings.ExportDxf;
        dwfInput.IsChecked = IsDwgProfile && settings.ExportDwf;
        combineDwgInput.IsChecked = IsDwgProfile && settings.CombineDwg;
        separatePdfWithCombinedInput.IsChecked = settings.ExportSeparatePdfWithCombined;
    }

    private void SavePrintSettings()
    {
        PrintSettings settings = PrintSettingsService.Normalize(initialSettings);
        printSettingsService?.Save(new PrintSettings(
            exportFolderInput.Text,
            fileNameMaskInput.Text,
            includePlaceholdersInput.IsChecked == true,
            IsPdfProfile ? pdfInput.IsChecked == true : settings.ExportPdf,
            IsPdfProfile ? combinePdfInput.IsChecked == true : settings.CombinePdf,
            IsPdfProfile ? combinedPdfNameInput.Text : settings.CombinedPdfFileName,
            IsPdfProfile ? GetSelectedPdfColorMode() : settings.PdfColorMode,
            IsPdfProfile ? GetSelectedPdfRasterQuality() : settings.PdfRasterQuality,
            IsPdfProfile ? forceRasterPdfInput.IsChecked == true : settings.AlwaysUseRasterPdf,
            IsDwgProfile ? dwgInput.IsChecked == true : settings.ExportDwg,
            IsDwgProfile ? dxfInput.IsChecked == true : settings.ExportDxf,
            IsDwgProfile ? dwfInput.IsChecked == true : settings.ExportDwf,
            IsDwgProfile ? combineDwgInput.IsChecked == true : settings.CombineDwg,
            IsPdfProfile ? separatePdfWithCombinedInput.IsChecked == true : settings.ExportSeparatePdfWithCombined,
            IsDwgProfile ? GetSelectedSetupName(dwgSetupInput) : settings.DwgSetupName,
            IsDwgProfile ? GetSelectedSetupName(dxfSetupInput) : settings.DxfSetupName));
        SaveDwgProfileState();
    }

    private void SaveDwgProfileState()
    {
        if (!IsDwgProfile || dwgProfileStorage is null)
        {
            return;
        }

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
            Width = width,
            IsReadOnly = true
        };
    }

    private static DataGridTemplateColumn CreateSelectionColumn()
    {
        FrameworkElementFactory checkBox = new(typeof(CheckBox));
        checkBox.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        checkBox.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        checkBox.SetBinding(
            CheckBox.IsCheckedProperty,
            new Binding(nameof(PrintSheetRow.IsSelected))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });

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
            Width = 150,
            IsReadOnly = true
        };
    }

    private static GroupStyle CreateSheetGroupStyle()
    {
        FrameworkElementFactory expander = new(typeof(Expander));
        expander.SetValue(Expander.IsExpandedProperty, true);
        expander.SetBinding(HeaderedContentControl.HeaderProperty, new Binding("Name") { StringFormat = "Группа: {0}" });
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
        foreach (PrintSheetRow row in selectedRows)
        {
            row.IsSelected = shouldSelect;
        }

        args.Handled = true;
        UpdateExportState();
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

    private static PrintFileNameContext CreateFileNameContext(RevitDocument document)
    {
        string documentName = string.IsNullOrWhiteSpace(document.Title)
            ? "Активный документ"
            : document.Title;

        try
        {
            IReadOnlyDictionary<string, string> projectParameters = CollectProjectParameters(document);
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

    private static IReadOnlyDictionary<string, string> CollectProjectParameters(RevitDocument document)
    {
        Dictionary<string, string> parameters = new(StringComparer.CurrentCultureIgnoreCase);
        if (document.ProjectInformation is null)
        {
            return parameters;
        }

        foreach (Autodesk.Revit.DB.Parameter parameter in document.ProjectInformation.Parameters)
        {
            string name = parameter.Definition?.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || parameters.ContainsKey(name))
            {
                continue;
            }

            string value = parameter.AsValueString() ?? parameter.AsString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(value))
            {
                parameters[name] = value.Trim();
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
        private string fileNamePreview = string.Empty;
        private string exportStatus = string.Empty;
        private bool isFileNameDuplicate;
        private bool showFileNameDuplicateWarning = true;
        private bool isFileNameTruncated;
        private bool hasUnknownFileNameTokens;

        public PrintSheetRow(PrintSheetInfo sheet, bool isSelected)
        {
            Sheet = sheet;
            this.isSelected = sheet.CanBePrinted && isSelected;
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

        public bool CanBePrinted => Sheet.CanBePrinted;

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

        public bool ShowFileNameDuplicateWarning
        {
            get => showFileNameDuplicateWarning;
            set
            {
                if (showFileNameDuplicateWarning == value)
                {
                    return;
                }

                showFileNameDuplicateWarning = value;
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
                    return "Лист-заглушка";
                }

                if (IsFileNameDuplicate && ShowFileNameDuplicateWarning)
                {
                    return "Дубликат имени";
                }

                if (HasUnknownFileNameTokens)
                {
                    return "Неизвестный токен";
                }

                if (IsFileNameTruncated)
                {
                    return "Имя обрезано";
                }

                if (!string.IsNullOrWhiteSpace(ExportStatus))
                {
                    return ExportStatus;
                }

                return Sheet.CanBePrinted
                    ? "Готов"
                    : "Не печатается";
            }
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
