using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Modules.Print.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using RevitDocument = Autodesk.Revit.DB.Document;
using WpfGrid = System.Windows.Controls.Grid;

namespace TrueBIM.App.Modules.Print.UI;

public sealed class DwgExportSettingsWindow : TrueBimWindow
{
    private readonly RevitDocument document;
    private readonly IReadOnlyList<PrintCadExportSetupOption> setupOptions;
    private readonly DwgExportProfileStorage? profileStorage;
    private readonly ITrueBimLogger logger;
    private readonly DwgExportOptionsFactory optionsFactory = new();

    private DwgExportProfileStoreState storeState;
    private DwgExportProfile workingProfile;
    private bool isApplyingValues;

    private readonly ComboBox savedProfileInput = CreateProfileInput("Сохраненные профили TrueBIM.");
    private readonly TextBox profileNameInput = CreateTextInput("Имя профиля TrueBIM.");
    private readonly ComboBox sourceSetupInput = CreateSetupInput("Базовая настройка DWG из Revit Export Setup.");
    private readonly CheckBox usePredefinedInput = CreateCheckBox("Использовать Revit Export Setup как базу");
    private readonly TextBlock sourceText = CreateMutedText();

    private readonly ComboBox fileVersionInput = CreateEnumInput<ACADVersion>("Версия AutoCAD для DWG.");
    private readonly ComboBox colorsInput = CreateEnumInput<ExportColorMode>("Index Colors, True Color или режим текущей версии Revit API.");
    private readonly ComboBox propOverridesInput = CreateEnumInput<PropOverrideMode>("Как экспортировать переопределения графики: by entity, by layer или new layer.");
    private readonly ComboBox targetUnitInput = CreateEnumInput<ExportUnit>("Единицы DWG.");
    private readonly ComboBox solidsInput = CreateEnumInput<SolidGeometry>("Polymesh или ACIS, если поддерживается текущей версией Revit API.");
    private readonly ComboBox lineScalingInput = CreateEnumInput<LineScaling>("Масштабирование линий DWG.");
    private readonly ComboBox textTreatmentInput = CreateEnumInput<TextTreatment>("Обработка текста при экспорте DWG.");

    private readonly CheckBox sharedCoordsInput = CreateCheckBox("Shared coordinates");
    private readonly CheckBox exportingAreasInput = CreateCheckBox("Экспорт помещений, пространств и зон");
    private readonly CheckBox mergedViewsInput = CreateCheckBox("Merged views / XRef");
    private readonly CheckBox hideScopeBoxInput = CreateCheckBox("Скрыть scope boxes");
    private readonly CheckBox hideReferencePlaneInput = CreateCheckBox("Скрыть reference planes");
    private readonly CheckBox hideUnreferenceViewTagsInput = CreateCheckBox("Скрыть неподдержанные view tags");
    private readonly CheckBox preserveCoincidentLinesInput = CreateCheckBox("Сохранять совпадающие линии");
    private readonly CheckBox markNonplotLayersInput = CreateCheckBox("Помечать non-plot слои");
    private readonly CheckBox useHatchBackgroundColorInput = CreateCheckBox("Использовать фон штриховок");

    private readonly TextBox nonplotSuffixInput = CreateTextInput("Суффикс non-plot слоев.");
    private readonly TextBox layerMappingInput = CreateTextInput("AIA, BS1192, ISO13567, CP83 или путь к txt layer mapping.");
    private readonly TextBox linetypesFileInput = CreateTextInput("Путь к .lin файлу типов линий.");
    private readonly TextBox hatchPatternsFileInput = CreateTextInput("Путь к .pat файлу штриховок.");
    private readonly TextBox hatchBackgroundColorInput = CreateTextInput("Цвет фона штриховок в формате #RRGGBB.");
    private readonly TextBlock statusText = CreateMutedText();

    public DwgExportSettingsWindow(
        RevitDocument document,
        DwgExportProfile profile,
        IReadOnlyList<PrintCadExportSetupOption> setupOptions,
        DwgExportProfileStoreState storeState,
        DwgExportProfileStorage? profileStorage,
        ITrueBimLogger logger)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        this.workingProfile = DwgExportProfileStorage.NormalizeProfile(profile ?? throw new ArgumentNullException(nameof(profile)));
        this.setupOptions = setupOptions ?? throw new ArgumentNullException(nameof(setupOptions));
        this.storeState = DwgExportProfileStorage.Normalize(storeState ?? new DwgExportProfileStoreState());
        this.profileStorage = profileStorage;
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        SelectedProfile = this.workingProfile.Clone();
        StoreState = this.storeState;

        Title = "Настройки DWG";
        Icon = IconFactory.CreateImage(TrueBimIcon.Print, 32);
        Width = 820;
        Height = 720;
        MinWidth = 720;
        MinHeight = 600;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();
        ApplyProfileToInputs(this.workingProfile);
    }

    public DwgExportProfile SelectedProfile { get; private set; }

    public DwgExportProfileStoreState StoreState { get; private set; }

    private UIElement CreateContent()
    {
        DockPanel root = new()
        {
            Margin = new Thickness(18)
        };

        UIElement footer = CreateFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        TabControl tabs = new()
        {
            Margin = new Thickness(0, 0, 0, 14)
        };
        tabs.Items.Add(CreateTab("Профиль", CreateProfileTab()));
        tabs.Items.Add(CreateTab("Общие", CreateGeneralTab()));
        tabs.Items.Add(CreateTab("Единицы и координаты", CreateUnitsTab()));
        tabs.Items.Add(CreateTab("Цвета", CreateColorsTab()));
        tabs.Items.Add(CreateTab("Слои", CreateLayersTab()));
        tabs.Items.Add(CreateTab("Линии", CreateLinesTab()));
        tabs.Items.Add(CreateTab("Штриховки", CreatePatternsTab()));
        tabs.Items.Add(CreateTab("Текст и шрифты", CreateTextTab()));
        tabs.Items.Add(CreateTab("3D Solids", CreateSolidsTab()));

        root.Children.Add(tabs);
        return root;
    }

    private UIElement CreateProfileTab()
    {
        WpfGrid grid = CreateFormGrid();

        savedProfileInput.ItemsSource = storeState.Profiles;
        savedProfileInput.DisplayMemberPath = nameof(DwgExportProfile.ProfileName);
        savedProfileInput.SelectionChanged += (_, _) => LoadSavedProfileSelection();
        AddRow(grid, "Профиль TrueBIM", savedProfileInput);
        AddRow(grid, "Имя профиля", profileNameInput);

        sourceSetupInput.ItemsSource = setupOptions;
        sourceSetupInput.DisplayMemberPath = nameof(PrintCadExportSetupOption.DisplayName);
        AddRow(grid, "Revit Export Setup", sourceSetupInput);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal
        };
        Button loadButton = CreateActionButton("Загрузить из Revit", TrueBimIcon.Open);
        loadButton.Click += (_, _) => LoadFromRevitSetup();
        buttons.Children.Add(loadButton);

        Button saveButton = CreateActionButton("Сохранить профиль", TrueBimIcon.Apply);
        saveButton.Click += (_, _) => SavePluginProfile();
        buttons.Children.Add(saveButton);

        Button resetButton = CreateActionButton("Сбросить к Revit", TrueBimIcon.Close);
        resetButton.Click += (_, _) => ResetToDefaultOptions();
        buttons.Children.Add(resetButton);
        AddRow(grid, "Действия", buttons);

        AddRow(grid, "Источник", sourceText);
        AddRow(grid, "База", usePredefinedInput);
        return WrapScrollable(grid);
    }

    private UIElement CreateGeneralTab()
    {
        WpfGrid grid = CreateFormGrid();
        AddRow(grid, "Версия AutoCAD", fileVersionInput);
        AddRow(grid, "Merged views / XRef", mergedViewsInput);
        AddRow(grid, "Помещения и зоны", exportingAreasInput);
        AddRow(grid, "Non-plot layers", markNonplotLayersInput);
        AddRow(grid, "Non-plot suffix", nonplotSuffixInput);
        AddRow(grid, "Scope boxes", hideScopeBoxInput);
        AddRow(grid, "Reference planes", hideReferencePlaneInput);
        AddRow(grid, "View tags", hideUnreferenceViewTagsInput);
        AddRow(grid, "Совпадающие линии", preserveCoincidentLinesInput);
        return WrapScrollable(grid);
    }

    private UIElement CreateUnitsTab()
    {
        WpfGrid grid = CreateFormGrid();
        AddRow(grid, "Единицы", targetUnitInput);
        AddRow(grid, "Координаты", sharedCoordsInput);
        AddRow(grid, "Project/Internal", CreateParagraph("При выключенном Shared coordinates Revit использует проектные/внутренние координаты текущего вида или листа."));
        AddRow(grid, "Shared coordinates", CreateParagraph("При включенном Shared coordinates Revit экспортирует по общим координатам, если они заданы в модели."));
        return WrapScrollable(grid);
    }

    private UIElement CreateColorsTab()
    {
        WpfGrid grid = CreateFormGrid();
        AddRow(grid, "Цвета", colorsInput);
        AddRow(grid, "Переопределения", propOverridesInput);
        AddRow(grid, "Фон штриховок", useHatchBackgroundColorInput);
        AddRow(grid, "Цвет фона", hatchBackgroundColorInput);
        AddRow(grid, "Подсказка", CreateParagraph("Index Colors ближе к классическим CAD-палитрам; True Color сохраняет RGB-цвета. ByLayer / ByEntity / NewLayer управляет тем, где окажутся графические переопределения."));
        return WrapScrollable(grid);
    }

    private UIElement CreateLayersTab()
    {
        WpfGrid grid = CreateFormGrid();
        AddRow(grid, "Layer mapping", CreateFileInput(layerMappingInput, "Выбрать txt", "Layer mapping (*.txt)|*.txt|All files (*.*)|*.*"));
        Button tableButton = CreateActionButton("Открыть таблицу слоев", TrueBimIcon.Preview);
        tableButton.Click += (_, _) => ShowTablePlaceholder("слоев", "ExportLayerTable");
        AddRow(grid, "Таблица", tableButton);
        AddRow(grid, "Поддержка", CreateParagraph("На этом этапе профиль безопасно управляет LayerMapping и не мутирует таблицы Revit без отдельного действия."));
        return WrapScrollable(grid);
    }

    private UIElement CreateLinesTab()
    {
        WpfGrid grid = CreateFormGrid();
        AddRow(grid, "Line scaling", lineScalingInput);
        AddRow(grid, "Linetypes", CreateFileInput(linetypesFileInput, "Выбрать lin", "AutoCAD linetypes (*.lin)|*.lin|All files (*.*)|*.*"));
        Button tableButton = CreateActionButton("Открыть таблицу линий", TrueBimIcon.Preview);
        tableButton.Click += (_, _) => ShowTablePlaceholder("типов линий", "ExportLinetypeTable");
        AddRow(grid, "Таблица", tableButton);
        return WrapScrollable(grid);
    }

    private UIElement CreatePatternsTab()
    {
        WpfGrid grid = CreateFormGrid();
        AddRow(grid, "Hatch patterns", CreateFileInput(hatchPatternsFileInput, "Выбрать pat", "AutoCAD hatch patterns (*.pat)|*.pat|All files (*.*)|*.*"));
        Button tableButton = CreateActionButton("Открыть таблицу штриховок", TrueBimIcon.Preview);
        tableButton.Click += (_, _) => ShowTablePlaceholder("штриховок", "ExportPatternTable");
        AddRow(grid, "Таблица", tableButton);
        return WrapScrollable(grid);
    }

    private UIElement CreateTextTab()
    {
        WpfGrid grid = CreateFormGrid();
        AddRow(grid, "Text treatment", textTreatmentInput);
        Button tableButton = CreateActionButton("Открыть таблицу шрифтов", TrueBimIcon.Preview);
        tableButton.Click += (_, _) => ShowTablePlaceholder("шрифтов", "ExportFontTable");
        AddRow(grid, "Таблица", tableButton);
        AddRow(grid, "Подсказка", CreateParagraph("TextTreatment выбирает баланс между визуальным соответствием и редактируемостью текста в CAD, насколько это поддерживает текущая версия Revit API."));
        return WrapScrollable(grid);
    }

    private UIElement CreateSolidsTab()
    {
        WpfGrid grid = CreateFormGrid();
        AddRow(grid, "3D solids", solidsInput);
        AddRow(grid, "Подсказка", CreateParagraph("Параметр важен прежде всего для 3D видов. Для листов он применяется только там, где Revit реально экспортирует 3D-геометрию."));
        return WrapScrollable(grid);
    }

    private UIElement CreateFooter()
    {
        DockPanel footer = new();

        statusText.TextWrapping = TextWrapping.Wrap;
        statusText.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(statusText, Dock.Left);
        footer.Children.Add(statusText);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Button createSetupButton = CreateActionButton("Создать Revit setup", TrueBimIcon.Apply);
        createSetupButton.MinWidth = 160;
        createSetupButton.ToolTip = "Создать Revit DWG Export Setup из текущих настроек. Если имя занято, будет создана копия с суффиксом.";
        createSetupButton.Click += (_, _) => CreateRevitSetup();
        actions.Children.Add(createSetupButton);

        Button okButton = CreateActionButton("Применить", TrueBimIcon.Apply);
        okButton.Click += (_, _) => ApplyAndClose();
        actions.Children.Add(okButton);

        Button cancelButton = CreateActionButton("Отмена", TrueBimIcon.Close);
        cancelButton.IsCancel = true;
        cancelButton.Click += (_, _) => Close();
        actions.Children.Add(cancelButton);

        footer.Children.Add(actions);
        return footer;
    }

    private void LoadSavedProfileSelection()
    {
        if (isApplyingValues || savedProfileInput.SelectedItem is not DwgExportProfile profile)
        {
            return;
        }

        ApplyProfileToInputs(profile.Clone());
        statusText.Text = $"Загружен профиль TrueBIM: {profile.ProfileName}.";
    }

    private void LoadFromRevitSetup()
    {
        string? setupName = GetSelectedSetupName();
        if (string.IsNullOrWhiteSpace(setupName))
        {
            MessageBox.Show(this, "Выберите сохраненную DWG настройку Revit.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DwgExportProfile profile = optionsFactory.CreateProfile(document, setupName, setupName!, isUserProfile: false, logger);
        ApplyProfileToInputs(profile);
        statusText.Text = $"Загружена настройка Revit: {setupName}.";
    }

    private void SavePluginProfile()
    {
        DwgExportProfile profile = ReadProfileFromInputs(isUserProfile: true);
        storeState.UpsertProfile(profile);
        storeState = DwgExportProfileStorage.Normalize(storeState);
        StoreState = storeState;
        profileStorage?.Save(StoreState);
        RefreshSavedProfiles(profile.ProfileName);
        ApplyProfileToInputs(profile);
        statusText.Text = $"Профиль TrueBIM сохранен: {profile.ProfileName}.";
    }

    private void ResetToDefaultOptions()
    {
        DwgExportProfile profile = DwgExportOptionsFactory.CreateProfileFromOptions(
            DwgExportProfile.DefaultProfileName,
            sourceRevitSetupName: null,
            usePredefinedRevitSetup: false,
            isUserProfile: false,
            new DWGExportOptions());
        ApplyProfileToInputs(profile);
        statusText.Text = "Настройки сброшены к стандартным DWGExportOptions Revit.";
    }

    private void CreateRevitSetup()
    {
        try
        {
            DwgExportProfile profile = ReadProfileFromInputs(isUserProfile: true);
            string setupName = optionsFactory.CreateRevitExportSetup(document, profile, logger);
            statusText.Text = $"Создан Revit DWG Export Setup: {setupName}.";
            MessageBox.Show(this, $"Создан Revit DWG Export Setup:\n{setupName}", Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            logger.Error("Failed to create Revit DWG export setup from TrueBIM profile.", exception);
            MessageBox.Show(this, $"Не удалось создать Revit DWG Export Setup:\n{exception.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyAndClose()
    {
        SelectedProfile = ReadProfileFromInputs(isUserProfile: true);
        storeState = DwgExportProfileStorage.Normalize(storeState);
        StoreState = storeState;
        DialogResult = true;
        Close();
    }

    private void ApplyProfileToInputs(DwgExportProfile profile)
    {
        isApplyingValues = true;
        workingProfile = DwgExportProfileStorage.NormalizeProfile(profile);

        profileNameInput.Text = workingProfile.ProfileName;
        usePredefinedInput.IsChecked = workingProfile.UsePredefinedRevitSetup;
        SelectSetup(workingProfile.SourceRevitSetupName);
        SelectSavedProfile(workingProfile.ProfileName);

        fileVersionInput.SelectedValue = workingProfile.FileVersion;
        colorsInput.SelectedValue = workingProfile.Colors;
        propOverridesInput.SelectedValue = workingProfile.PropOverrides;
        targetUnitInput.SelectedValue = workingProfile.TargetUnit;
        solidsInput.SelectedValue = workingProfile.ExportOfSolids;
        lineScalingInput.SelectedValue = workingProfile.LineScaling;
        textTreatmentInput.SelectedValue = workingProfile.TextTreatment;

        sharedCoordsInput.IsChecked = workingProfile.SharedCoords;
        exportingAreasInput.IsChecked = workingProfile.ExportingAreas;
        mergedViewsInput.IsChecked = workingProfile.MergedViews;
        hideScopeBoxInput.IsChecked = workingProfile.HideScopeBox;
        hideReferencePlaneInput.IsChecked = workingProfile.HideReferencePlane;
        hideUnreferenceViewTagsInput.IsChecked = workingProfile.HideUnreferenceViewTags;
        preserveCoincidentLinesInput.IsChecked = workingProfile.PreserveCoincidentLines;
        markNonplotLayersInput.IsChecked = workingProfile.MarkNonplotLayers;
        useHatchBackgroundColorInput.IsChecked = workingProfile.UseHatchBackgroundColor;

        nonplotSuffixInput.Text = workingProfile.NonplotSuffix ?? string.Empty;
        layerMappingInput.Text = workingProfile.LayerMapping ?? string.Empty;
        linetypesFileInput.Text = workingProfile.LinetypesFileName ?? string.Empty;
        hatchPatternsFileInput.Text = workingProfile.HatchPatternsFileName ?? string.Empty;
        hatchBackgroundColorInput.Text = workingProfile.HatchBackgroundColor.ToHex();
        sourceText.Text = workingProfile.IsUserProfile ? "Пользовательский профиль TrueBIM" : "Из Revit Export Setup";

        isApplyingValues = false;
    }

    private DwgExportProfile ReadProfileFromInputs(bool isUserProfile)
    {
        DwgExportColor hatchColor = DwgExportColor.TryParse(hatchBackgroundColorInput.Text, out DwgExportColor parsedColor)
            ? parsedColor
            : workingProfile.HatchBackgroundColor;

        DwgExportProfile profile = new()
        {
            ProfileName = DwgExportProfileStorage.NormalizeProfileName(profileNameInput.Text),
            SourceRevitSetupName = GetSelectedSetupName(),
            UsePredefinedRevitSetup = usePredefinedInput.IsChecked == true,
            IsUserProfile = isUserProfile,
            FileVersion = GetSelectedEnum(fileVersionInput, workingProfile.FileVersion),
            Colors = GetSelectedEnum(colorsInput, workingProfile.Colors),
            PropOverrides = GetSelectedEnum(propOverridesInput, workingProfile.PropOverrides),
            TargetUnit = GetSelectedEnum(targetUnitInput, workingProfile.TargetUnit),
            SharedCoords = sharedCoordsInput.IsChecked == true,
            ExportingAreas = exportingAreasInput.IsChecked == true,
            MergedViews = mergedViewsInput.IsChecked == true,
            HideScopeBox = hideScopeBoxInput.IsChecked == true,
            HideReferencePlane = hideReferencePlaneInput.IsChecked == true,
            HideUnreferenceViewTags = hideUnreferenceViewTagsInput.IsChecked == true,
            PreserveCoincidentLines = preserveCoincidentLinesInput.IsChecked == true,
            ExportOfSolids = GetSelectedEnum(solidsInput, workingProfile.ExportOfSolids),
            LineScaling = GetSelectedEnum(lineScalingInput, workingProfile.LineScaling),
            TextTreatment = GetSelectedEnum(textTreatmentInput, workingProfile.TextTreatment),
            LayerMapping = layerMappingInput.Text,
            LinetypesFileName = linetypesFileInput.Text,
            HatchPatternsFileName = hatchPatternsFileInput.Text,
            MarkNonplotLayers = markNonplotLayersInput.IsChecked == true,
            NonplotSuffix = nonplotSuffixInput.Text,
            UseHatchBackgroundColor = useHatchBackgroundColorInput.IsChecked == true,
            HatchBackgroundColor = hatchColor
        };

        return DwgExportProfileStorage.NormalizeProfile(profile);
    }

    private void RefreshSavedProfiles(string selectedProfileName)
    {
        savedProfileInput.ItemsSource = null;
        savedProfileInput.ItemsSource = StoreState.Profiles;
        savedProfileInput.DisplayMemberPath = nameof(DwgExportProfile.ProfileName);
        SelectSavedProfile(selectedProfileName);
    }

    private string? GetSelectedSetupName()
    {
        return sourceSetupInput.SelectedItem is PrintCadExportSetupOption option
            ? option.SetupName
            : null;
    }

    private void SelectSetup(string? setupName)
    {
        sourceSetupInput.SelectedItem = setupOptions.FirstOrDefault(option =>
            string.Equals(option.SetupName, setupName, StringComparison.CurrentCultureIgnoreCase))
            ?? setupOptions.FirstOrDefault();
    }

    private void SelectSavedProfile(string profileName)
    {
        savedProfileInput.SelectedItem = storeState.FindProfile(profileName);
    }

    private static T GetSelectedEnum<T>(ComboBox input, T fallback)
        where T : struct, Enum
    {
        return input.SelectedValue is T value ? value : fallback;
    }

    private static UIElement CreateFileInput(TextBox input, string browseText, string filter)
    {
        DockPanel panel = new();
        Button browseButton = CreateActionButton(browseText, TrueBimIcon.Open);
        browseButton.MinWidth = 120;
        browseButton.Margin = new Thickness(8, 0, 0, 0);
        browseButton.Click += (_, _) =>
        {
            OpenFileDialog dialog = new()
            {
                Filter = filter,
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                input.Text = dialog.FileName;
            }
        };

        DockPanel.SetDock(browseButton, Dock.Right);
        panel.Children.Add(browseButton);
        panel.Children.Add(input);
        return panel;
    }

    private void ShowTablePlaceholder(string tableName, string apiName)
    {
        MessageBox.Show(
            this,
            $"Просмотр таблицы {tableName} через {apiName} оставлен безопасным read-only этапом для следующего шага.\n\nТекущий профиль уже управляет базовыми DWGExportOptions и путями файлов сопоставлений.",
            Title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static TabItem CreateTab(string header, UIElement content)
    {
        return new TabItem
        {
            Header = header,
            Content = content
        };
    }

    private static ScrollViewer WrapScrollable(UIElement content)
    {
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content
        };
    }

    private static WpfGrid CreateFormGrid()
    {
        WpfGrid grid = new()
        {
            Margin = new Thickness(12)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private static void AddRow(WpfGrid grid, string label, UIElement input)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock labelBlock = new()
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 10)
        };
        WpfGrid.SetRow(labelBlock, row);
        grid.Children.Add(labelBlock);

        if (input is FrameworkElement frameworkElement)
        {
            frameworkElement.Margin = new Thickness(0, 0, 0, 10);
        }

        WpfGrid.SetColumn(input, 1);
        WpfGrid.SetRow(input, row);
        grid.Children.Add(input);
    }

    private static ComboBox CreateProfileInput(string tooltip)
    {
        return new ComboBox
        {
            Height = 32,
            MinWidth = 260,
            ToolTip = tooltip
        };
    }

    private static ComboBox CreateSetupInput(string tooltip)
    {
        return new ComboBox
        {
            Height = 32,
            MinWidth = 260,
            ToolTip = tooltip
        };
    }

    private static ComboBox CreateEnumInput<T>(string tooltip)
        where T : struct, Enum
    {
        return new ComboBox
        {
            ItemsSource = Enum.GetValues(typeof(T))
                .Cast<T>()
                .Select(value => new KeyValuePair<T, string>(value, value.ToString()))
                .ToList(),
            DisplayMemberPath = "Value",
            SelectedValuePath = "Key",
            Height = 32,
            MinWidth = 220,
            ToolTip = tooltip
        };
    }

    private static TextBox CreateTextInput(string tooltip)
    {
        return new TextBox
        {
            Height = 32,
            MinWidth = 260,
            ToolTip = tooltip
        };
    }

    private static CheckBox CreateCheckBox(string text)
    {
        return new CheckBox
        {
            Content = text,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static TextBlock CreateParagraph(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19
        };
    }

    private static TextBlock CreateMutedText()
    {
        return new TextBlock
        {
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static Button CreateActionButton(string text, TrueBimIcon icon)
    {
        return new Button
        {
            Content = IconFactory.CreateButtonContent(icon, text),
            MinWidth = 120,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0)
        };
    }
}
