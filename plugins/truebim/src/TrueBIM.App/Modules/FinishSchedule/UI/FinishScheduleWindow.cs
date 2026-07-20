using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Revit;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.FinishSchedule.UI;

public sealed class FinishScheduleWindow : TrueBimWindow
{
    private readonly FinishScheduleModuleStatus status;
    private ParameterCatalog catalog;
    private readonly FinishScheduleParameterCategories categories;
    private readonly IReadOnlyList<FinishScheduleLevelOption> levels;
    private readonly FinishScheduleProfileStorage profileStorage;
    private readonly Func<FinishScheduleSettings, FinishSchedulePreviewResult>? previewFactory;
    private readonly Func<FinishScheduleSettings, FinishScheduleWritePreview>? writePreviewFactory;
    private readonly Func<FinishScheduleWritePreview, FinishScheduleWriteResult>? writeApplyFactory;
    private readonly Func<FinishScheduleDefaultParameterResult>? defaultParameterFactory;
    private readonly ITrueBimLogger logger;
    private readonly FinishScheduleSettingsValidator validator;
    private readonly FinishSchedulePreviewValidator previewValidator;
    private readonly FinishScheduleParameterOptionService optionService;
    private readonly FinishSchedulePreferredParameterResolver preferredParameterResolver;
    private readonly FinishScheduleConfigurationStorage configurationStorage = new();
    private readonly FinishScheduleReportBuilder reportBuilder = new();

    private readonly CategoryControls walls = new(
        "Стены",
        "Внутренняя отделка",
        "Значение «Группа модели» для типов отделочных стен.",
        FinishSchedulePreferredParameterNames.WallsOwnership,
        FinishSchedulePreferredParameterNames.WallsDescription,
        FinishSchedulePreferredParameterNames.WallsArea);
    private readonly CategoryControls floors = new(
        "Полы",
        "Пол",
        "Значение «Группа модели» для типов отделочных перекрытий пола.",
        FinishSchedulePreferredParameterNames.FloorsOwnership,
        FinishSchedulePreferredParameterNames.FloorsDescription,
        FinishSchedulePreferredParameterNames.FloorsArea);
    private readonly CategoryControls ceilings = new(
        "Потолки",
        "Потолки",
        "Значение «Группа модели» для перекрытий, используемых как потолок.",
        FinishSchedulePreferredParameterNames.CeilingsOwnership,
        FinishSchedulePreferredParameterNames.CeilingsDescription,
        FinishSchedulePreferredParameterNames.CeilingsArea);

    private readonly ComboBox descriptionInput = CreateParameterInput(
        "Текстовый параметр типа, доступный у всех включённых физических категорий.");
    private readonly ComboBox roomIdentifierModeInput = CreateChoiceInput();
    private readonly ComboBox roomIdentifierParameterInput = CreateParameterInput(
        "Текстовый параметр экземпляра помещения для пользовательского идентификатора.");
    private readonly CheckBox writeOwnershipInput = CreateCheckBox(
        "Записывать фактическую принадлежность в элементы отделки",
        "В выбранные параметры физических элементов будут записаны реальные идентификаторы связанных помещений.");
    private readonly ComboBox roomListOutputInput = CreateParameterInput(
        $"Записываемый текстовый параметр экземпляра помещения. Рекомендуемое имя: «{FinishSchedulePreferredParameterNames.RoomListOutput}».");
    private readonly ComboBox scopeModeInput = CreateChoiceInput();
    private readonly ComboBox levelInput = CreateChoiceInput();
    private readonly ComboBox sectionParameterInput = CreateParameterInput(
        "Параметр экземпляра помещения, по которому будет ограничена область расчёта.");
    private readonly TextBox sectionValueInput = CreateTextInput(
        "Точное значение секции или корпуса. Сбор доступных значений будет добавлен вместе с preview FS-004.");
    private readonly TextBox scheduleNameInput = CreateTextInput(
        "Имя спецификации помещений, которой будет управлять TrueBIM.");
    private readonly TextBlock validationText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = TrueBimBrushes.TextPrimary
    };
    private readonly Image validationIcon = new()
    {
        Width = TrueBimTheme.IconSizeSmall,
        Height = TrueBimTheme.IconSizeSmall,
        Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, 0),
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock footerStatus = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = TrueBimBrushes.TextSecondary,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock previewText = new()
    {
        Text = "Предпросмотр ещё не выполнялся. Настройте категории и область, затем нажмите «Предпросмотр».",
        TextWrapping = TextWrapping.Wrap,
        Foreground = TrueBimBrushes.TextPrimary
    };
    private readonly Image previewIcon = new()
    {
        Width = TrueBimTheme.IconSizeSmall,
        Height = TrueBimTheme.IconSizeSmall,
        Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, 0),
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Button saveButton;
    private readonly Button configurationButton;
    private readonly Button defaultParametersButton;
    private readonly Button previewButton;
    private readonly Button generateButton;
    private readonly Button copyReportButton;
    private readonly Button openScheduleButton;
    private readonly Border validationBanner;
    private readonly Border previewBanner;

    private bool isUpdating;
    private string currentReportText = string.Empty;
    private long? lastScheduleId;

    public long? RequestedScheduleId { get; private set; }

    public FinishScheduleWindow(
        FinishScheduleModuleStatus status,
        ParameterCatalog catalog,
        FinishScheduleParameterCategories categories,
        IEnumerable<FinishScheduleLevelOption> levels,
        FinishScheduleProfileStorage profileStorage,
        Func<FinishScheduleSettings, FinishSchedulePreviewResult>? previewFactory,
        Func<FinishScheduleSettings, FinishScheduleWritePreview>? writePreviewFactory,
        Func<FinishScheduleWritePreview, FinishScheduleWriteResult>? writeApplyFactory,
        Func<FinishScheduleDefaultParameterResult>? defaultParameterFactory,
        ITrueBimLogger logger)
    {
        this.status = status ?? throw new ArgumentNullException(nameof(status));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.categories = categories ?? throw new ArgumentNullException(nameof(categories));
        this.levels = (levels ?? throw new ArgumentNullException(nameof(levels))).ToArray();
        this.profileStorage = profileStorage ?? throw new ArgumentNullException(nameof(profileStorage));
        this.previewFactory = previewFactory;
        this.writePreviewFactory = writePreviewFactory;
        this.writeApplyFactory = writeApplyFactory;
        this.defaultParameterFactory = defaultParameterFactory;
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        ParameterCatalogMatcher matcher = new();
        validator = new FinishScheduleSettingsValidator(matcher);
        previewValidator = new FinishSchedulePreviewValidator();
        optionService = new FinishScheduleParameterOptionService(matcher);
        preferredParameterResolver = new FinishSchedulePreferredParameterResolver(optionService);

        saveButton = TrueBimUi.CreateSecondaryButton(
            "Сохранить настройки",
            TrueBimIcon.Settings,
            (_, _) => SaveProfile(showFeedback: true),
            minWidth: 165);
        saveButton.ToolTip = "Сохранить текущие настройки локально, даже если конфигурация ещё не завершена.";

        configurationButton = TrueBimUi.CreateSecondaryButton(
            "Конфигурация проекта",
            TrueBimIcon.Settings,
            (_, _) => OpenConfiguration(),
            minWidth: 175);
        configurationButton.ToolTip = "Открыть проектные настройки классификации, источников и выходных параметров.";

        defaultParametersButton = TrueBimUi.CreateSecondaryButton(
            "Добавить параметры по умолчанию",
            TrueBimIcon.Parameter,
            (_, _) => AddDefaultParameters(),
            isEnabled: defaultParameterFactory is not null,
            minWidth: 235);
        defaultParametersButton.ToolTip = defaultParameterFactory is null
            ? "Откройте документ Revit, чтобы добавить общие параметры."
            : "Создать недостающие общие параметры TrueBIM и привязать их к помещениям, стенам, перекрытиям и потолкам.";
        ToolTipService.SetShowOnDisabled(defaultParametersButton, true);

        previewButton = TrueBimUi.CreateSecondaryButton(
            "Предпросмотр",
            TrueBimIcon.Preview,
            (_, _) => RunPreview(),
            isEnabled: false,
            minWidth: 140);
        ToolTipService.SetShowOnDisabled(previewButton, true);

        generateButton = TrueBimUi.CreatePrimaryButton(
            "Сформировать",
            TrueBimIcon.Apply,
            (_, _) => RunGeneration(),
            isEnabled: false,
            minWidth: 140);
        ToolTipService.SetShowOnDisabled(generateButton, true);

        copyReportButton = TrueBimUi.CreateSecondaryButton(
            "Копировать отчёт",
            TrueBimIcon.CopyParameters,
            (_, _) => CopyCurrentReport(),
            isEnabled: false,
            minWidth: 145);
        copyReportButton.ToolTip = "Скопировать полный отчёт со всеми предупреждениями и таймингами стадий.";
        ToolTipService.SetShowOnDisabled(copyReportButton, true);

        openScheduleButton = TrueBimUi.CreateSecondaryButton(
            "Открыть спецификацию",
            TrueBimIcon.Open,
            (_, _) => RequestScheduleOpen(),
            isEnabled: false,
            minWidth: 165);
        openScheduleButton.ToolTip = "Закрыть окно и открыть созданную или обновлённую спецификацию в Revit.";
        ToolTipService.SetShowOnDisabled(openScheduleButton, true);

        previewBanner = CreateStatusBanner(previewIcon, previewText);
        ApplyBannerSeverity(previewBanner, previewIcon, TrueBimUiSeverity.Info);
        validationBanner = CreateStatusBanner(validationIcon, validationText);
        validationBanner.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        ApplyBannerSeverity(validationBanner, validationIcon, TrueBimUiSeverity.Warning);

        ConfigureChoices();

        Title = "Ведомость отделки";
        Icon = IconFactory.CreateImage(TrueBimIcon.FinishSchedule, TrueBimTheme.IconSizeRibbon);
        Width = 1040;
        Height = 820;
        MinWidth = 880;
        MinHeight = 660;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        ApplyTrueBimShell(
            TrueBimUi.CreateHeader(
                "Ведомость отделки",
                "Настройки текущего расчёта и формирование ведомости помещений",
                TrueBimIcon.FinishSchedule),
            commandBar: CreateCommandBar(),
            body: CreateBody(),
            status: footerStatus,
            footer: CreateFooter());

        AttachEvents();
        FinishScheduleSettings loadedSettings = profileStorage.Load();
        ApplySettings(preferredParameterResolver.Resolve(loadedSettings, catalog, categories));
        logger.Info(
            $"Finish Schedule settings window opened. Document='{status.DocumentName}'; Parameters={catalog.Items.Count}; Levels={this.levels.Count}.");
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveProfile(showFeedback: false);
        base.OnClosed(e);
    }

    private void ConfigureChoices()
    {
        roomIdentifierModeInput.ItemsSource = new Choice<RoomIdentifierMode>[]
        {
            new(RoomIdentifierMode.Number, "Номер помещения"),
            new(RoomIdentifierMode.Name, "Имя помещения"),
            new(RoomIdentifierMode.CustomParameter, "Пользовательский параметр")
        };
        roomIdentifierModeInput.DisplayMemberPath = nameof(Choice<RoomIdentifierMode>.DisplayName);

        scopeModeInput.ItemsSource = new Choice<ReportScopeKind>[]
        {
            new(ReportScopeKind.Level, "По этажу"),
            new(ReportScopeKind.Section, "По секции или корпусу"),
            new(ReportScopeKind.EntireProject, "По всему объекту")
        };
        scopeModeInput.DisplayMemberPath = nameof(Choice<ReportScopeKind>.DisplayName);

        levelInput.ItemsSource = levels;
        levelInput.DisplayMemberPath = nameof(FinishScheduleLevelOption.DisplayName);
        levelInput.ToolTip = levels.Count == 0
            ? "В текущем документе уровни не найдены."
            : "Уровень помещений для режима «По этажу».";
    }

    private UIElement CreateBody()
    {
        StackPanel content = new()
        {
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing4, 0)
        };

        Border contextBanner = TrueBimUi.CreateInfoBanner(
            status.HasActiveDocument
                ? $"Документ: {status.DocumentName}. Каталог содержит вариантов параметров: {catalog.Items.Count}. Расчёт и предпросмотр не меняют модель; запись выполняют только подтверждённые действия."
                : "Документ Revit не открыт. Профиль можно просмотреть, но выбор параметров и формирование недоступны.",
            status.HasActiveDocument ? TrueBimUiSeverity.Info : TrueBimUiSeverity.Warning);
        contextBanner.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        content.Children.Add(contextBanner);

        previewBanner.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        content.Children.Add(previewBanner);

        AddCard(content, FinishScheduleSectionTitles.Categories, CreateCategorySelectionContent());
        AddCard(content, FinishScheduleSectionTitles.Ownership, CreateOwnershipContent());
        AddCard(content, FinishScheduleSectionTitles.Scope, CreateScopeContent());
        AddCard(content, FinishScheduleSectionTitles.Schedule, CreateScheduleContent(), isLast: true);
        content.Children.Add(validationBanner);

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content
        };
    }

    private UIElement CreateCommandBar()
    {
        return TrueBimUi.CreateCommandBar(configurationButton, defaultParametersButton);
    }

    private UIElement CreateCategorySelectionContent()
    {
        StackPanel panel = new();
        panel.Children.Add(CreateDescription(
            "Отключённая категория не участвует в ключе группировки и не требует выходных параметров."));

        StackPanel choices = new()
        {
            Orientation = Orientation.Horizontal
        };
        choices.Children.Add(walls.EnabledInput);
        floors.EnabledInput.Margin = new Thickness(TrueBimTheme.Spacing16, 0, 0, 0);
        choices.Children.Add(floors.EnabledInput);
        ceilings.EnabledInput.Margin = new Thickness(TrueBimTheme.Spacing16, 0, 0, 0);
        choices.Children.Add(ceilings.EnabledInput);
        panel.Children.Add(choices);
        return panel;
    }

    private UIElement CreateClassificationContent()
    {
        StackPanel panel = new();
        panel.Children.Add(CreateDescription(
            $"Фиксированный параметр типа: «{FinishScheduleSettings.ClassificationParameterName}». Значения можно выбрать или ввести вручную."));
        panel.Children.Add(CreateFieldRow("Стены", walls.ClassificationInput));
        panel.Children.Add(CreateFieldRow("Полы", floors.ClassificationInput));
        panel.Children.Add(CreateFieldRow("Потолки", ceilings.ClassificationInput, isLast: true));
        return panel;
    }

    private UIElement CreateDescriptionContent()
    {
        StackPanel panel = new();
        panel.Children.Add(CreateDescription(
            "Показываются только текстовые параметры типа, доступные у всех включённых физических категорий."));
        panel.Children.Add(CreateFieldRow("Источник описания", descriptionInput, isLast: true));
        return panel;
    }

    private UIElement CreateRoomIdentifierContent()
    {
        StackPanel panel = new();
        panel.Children.Add(CreateFieldRow("Режим идентификатора", roomIdentifierModeInput));
        panel.Children.Add(CreateFieldRow(
            "Пользовательский параметр",
            roomIdentifierParameterInput,
            isLast: true));
        return panel;
    }

    private UIElement CreateOwnershipContent()
    {
        StackPanel panel = new();
        writeOwnershipInput.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        panel.Children.Add(writeOwnershipInput);
        panel.Children.Add(CreateFieldRow("Стены", walls.OwnershipInput));
        panel.Children.Add(CreateFieldRow("Полы", floors.OwnershipInput));
        panel.Children.Add(CreateFieldRow("Потолки", ceilings.OwnershipInput, isLast: true));
        return panel;
    }

    private UIElement CreateOutputContent()
    {
        StackPanel panel = new();
        panel.Children.Add(CreateDescription(
            "Показываются только записываемые текстовые параметры экземпляра помещения. Один параметр нельзя использовать для разных выходных значений."));
        panel.Children.Add(CreateFieldRow("Список помещений", roomListOutputInput));
        panel.Children.Add(CreateOutputHeader());
        panel.Children.Add(CreateOutputRow("Стены", walls.OutputDescriptionInput, walls.OutputAreaInput));
        panel.Children.Add(CreateOutputRow("Полы", floors.OutputDescriptionInput, floors.OutputAreaInput));
        panel.Children.Add(CreateOutputRow(
            "Потолки",
            ceilings.OutputDescriptionInput,
            ceilings.OutputAreaInput,
            isLast: true));
        return panel;
    }

    private UIElement CreateScopeContent()
    {
        StackPanel panel = new();
        panel.Children.Add(CreateFieldRow("Режим", scopeModeInput));
        panel.Children.Add(CreateFieldRow("Уровень", levelInput));
        panel.Children.Add(CreateFieldRow("Параметр секции", sectionParameterInput));
        panel.Children.Add(CreateFieldRow("Значение секции", sectionValueInput, isLast: true));
        return panel;
    }

    private UIElement CreateScheduleContent()
    {
        StackPanel panel = new();
        panel.Children.Add(CreateDescription(
            "TrueBIM будет обновлять только спецификацию со своим служебным маркером. Чужой вид с тем же именем останется без изменений."));
        panel.Children.Add(CreateFieldRow("Название", scheduleNameInput, isLast: true));
        return panel;
    }

    private UIElement CreateFooter()
    {
        Button closeButton = TrueBimUi.CreateSecondaryButton(
            "Закрыть",
            TrueBimIcon.Close,
            (_, _) => Close(),
            minWidth: 110);
        closeButton.IsCancel = true;
        closeButton.ToolTip = "Сохранить текущий профиль и закрыть окно без изменений модели.";

        return TrueBimUi.CreateFooter(
            null,
            saveButton,
            previewButton,
            copyReportButton,
            openScheduleButton,
            generateButton,
            closeButton);
    }

    private void AttachEvents()
    {
        AttachCategoryEvents(walls);
        AttachCategoryEvents(floors);
        AttachCategoryEvents(ceilings);

        descriptionInput.SelectionChanged += (_, _) => OnInputChanged();
        roomIdentifierModeInput.SelectionChanged += (_, _) => OnInputChanged(refreshDependencies: true);
        roomIdentifierParameterInput.SelectionChanged += (_, _) => OnInputChanged();
        writeOwnershipInput.Checked += (_, _) => OnInputChanged(refreshDependencies: true);
        writeOwnershipInput.Unchecked += (_, _) => OnInputChanged(refreshDependencies: true);
        roomListOutputInput.SelectionChanged += (_, _) => OnInputChanged();
        scopeModeInput.SelectionChanged += (_, _) => OnInputChanged(refreshDependencies: true);
        levelInput.SelectionChanged += (_, _) => OnInputChanged();
        sectionParameterInput.SelectionChanged += (_, _) => OnInputChanged();
        sectionValueInput.TextChanged += (_, _) => OnInputChanged();
        scheduleNameInput.TextChanged += (_, _) => OnInputChanged();
    }

    private void AttachCategoryEvents(CategoryControls controls)
    {
        controls.EnabledInput.Checked += (_, _) => OnCategoryChanged();
        controls.EnabledInput.Unchecked += (_, _) => OnCategoryChanged();
        controls.ClassificationInput.SelectionChanged += (_, _) => OnInputChanged();
        controls.ClassificationInput.AddHandler(
            TextBox.TextChangedEvent,
            new TextChangedEventHandler((_, _) => OnInputChanged()));
        controls.OwnershipInput.SelectionChanged += (_, _) => OnInputChanged();
        controls.OutputDescriptionInput.SelectionChanged += (_, _) => OnInputChanged();
        controls.OutputAreaInput.SelectionChanged += (_, _) => OnInputChanged();
    }

    private void OnCategoryChanged()
    {
        if (isUpdating)
        {
            return;
        }

        RefreshParameterOptions();
        RefreshDependentState();
        InvalidatePreview();
        UpdateValidation();
    }

    private void OnInputChanged(bool refreshDependencies = false)
    {
        if (isUpdating)
        {
            return;
        }

        if (refreshDependencies)
        {
            RefreshDependentState();
        }

        InvalidatePreview();
        UpdateValidation();
    }

    private void ApplySettings(FinishScheduleSettings settings)
    {
        isUpdating = true;
        try
        {
            walls.EnabledInput.IsChecked = settings.Walls.IsEnabled;
            floors.EnabledInput.IsChecked = settings.Floors.IsEnabled;
            ceilings.EnabledInput.IsChecked = settings.Ceilings.IsEnabled;

            walls.ClassificationInput.Text = settings.Walls.ClassificationValue;
            floors.ClassificationInput.Text = settings.Floors.ClassificationValue;
            ceilings.ClassificationInput.Text = settings.Ceilings.ClassificationValue;
            writeOwnershipInput.IsChecked = settings.WriteOwnership;

            RefreshParameterOptions();
            SetSelectedParameter(descriptionInput, settings.DescriptionParameter);
            SelectChoice(roomIdentifierModeInput, settings.RoomIdentifier.Mode);
            SetSelectedParameter(roomIdentifierParameterInput, settings.RoomIdentifier.CustomParameter);
            SetSelectedParameter(walls.OwnershipInput, settings.Walls.OwnershipParameter);
            SetSelectedParameter(floors.OwnershipInput, settings.Floors.OwnershipParameter);
            SetSelectedParameter(ceilings.OwnershipInput, settings.Ceilings.OwnershipParameter);
            SetSelectedParameter(roomListOutputInput, settings.RoomListOutputParameter);
            SetSelectedParameter(walls.OutputDescriptionInput, settings.Walls.OutputDescriptionParameter);
            SetSelectedParameter(walls.OutputAreaInput, settings.Walls.OutputAreaParameter);
            SetSelectedParameter(floors.OutputDescriptionInput, settings.Floors.OutputDescriptionParameter);
            SetSelectedParameter(floors.OutputAreaInput, settings.Floors.OutputAreaParameter);
            SetSelectedParameter(ceilings.OutputDescriptionInput, settings.Ceilings.OutputDescriptionParameter);
            SetSelectedParameter(ceilings.OutputAreaInput, settings.Ceilings.OutputAreaParameter);

            SelectChoice(scopeModeInput, settings.Scope.Kind);
            levelInput.SelectedItem = levels.FirstOrDefault(level => level.ElementId == settings.Scope.LevelId);
            SetSelectedParameter(sectionParameterInput, settings.Scope.SectionParameter);
            sectionValueInput.Text = settings.Scope.SectionValue;
            scheduleNameInput.Text = settings.ScheduleName;
        }
        finally
        {
            isUpdating = false;
        }

        RefreshDependentState();
        UpdateValidation();
    }

    private FinishScheduleSettings ReadSettings()
    {
        RoomIdentifierMode roomMode = ReadChoice(
            roomIdentifierModeInput,
            RoomIdentifierMode.Number);
        ReportScopeKind scopeKind = ReadChoice(
            scopeModeInput,
            ReportScopeKind.EntireProject);
        FinishScheduleLevelOption? selectedLevel = levelInput.SelectedItem as FinishScheduleLevelOption;

        return new FinishScheduleSettings(
            GetSelectedParameter(descriptionInput),
            new RoomIdentifierSettings(
                roomMode,
                roomMode == RoomIdentifierMode.CustomParameter
                    ? GetSelectedParameter(roomIdentifierParameterInput)
                    : null),
            writeOwnershipInput.IsChecked == true,
            ReadCategory(walls),
            ReadCategory(floors),
            ReadCategory(ceilings),
            GetSelectedParameter(roomListOutputInput),
            new ReportScopeSettings(
                scopeKind,
                scopeKind == ReportScopeKind.Level ? selectedLevel?.ElementId : null,
                scopeKind == ReportScopeKind.Section
                    ? GetSelectedParameter(sectionParameterInput)
                    : null,
                scopeKind == ReportScopeKind.Section ? sectionValueInput.Text.Trim() : string.Empty),
            scheduleNameInput.Text.Trim());
    }

    private static FinishCategorySettings ReadCategory(CategoryControls controls)
    {
        return new FinishCategorySettings(
            controls.EnabledInput.IsChecked == true,
            controls.ClassificationInput.Text.Trim(),
            GetSelectedParameter(controls.OwnershipInput),
            GetSelectedParameter(controls.OutputDescriptionInput),
            GetSelectedParameter(controls.OutputAreaInput));
    }

    private void RefreshParameterOptions()
    {
        bool previousUpdating = isUpdating;
        isUpdating = true;
        try
        {
            SetOptions(
                descriptionInput,
                optionService.GetDescriptionOptions(
                    catalog,
                    categories,
                    walls.EnabledInput.IsChecked == true,
                    floors.EnabledInput.IsChecked == true,
                    ceilings.EnabledInput.IsChecked == true));
            SetOptions(
                roomIdentifierParameterInput,
                optionService.GetRoomIdentifierOptions(catalog, categories));

            IReadOnlyList<FinishScheduleParameterOption> roomOutputs = optionService.GetRoomOutputOptions(
                catalog,
                categories);
            SetOptions(roomListOutputInput, roomOutputs);
            SetOptions(walls.OutputDescriptionInput, roomOutputs);
            SetOptions(walls.OutputAreaInput, roomOutputs);
            SetOptions(floors.OutputDescriptionInput, roomOutputs);
            SetOptions(floors.OutputAreaInput, roomOutputs);
            SetOptions(ceilings.OutputDescriptionInput, roomOutputs);
            SetOptions(ceilings.OutputAreaInput, roomOutputs);

            SetOptions(
                walls.OwnershipInput,
                optionService.GetOwnershipOptions(catalog, categories.Walls));
            SetOptions(
                floors.OwnershipInput,
                optionService.GetOwnershipOptions(catalog, categories.Floors));
            SetOptions(
                ceilings.OwnershipInput,
                optionService.GetOwnershipOptions(catalog, categories.Ceilings));
            SetOptions(
                sectionParameterInput,
                optionService.GetSectionOptions(catalog, categories));
        }
        finally
        {
            isUpdating = previousUpdating;
        }
    }

    private void RefreshDependentState()
    {
        bool wallsEnabled = walls.EnabledInput.IsChecked == true;
        bool floorsEnabled = floors.EnabledInput.IsChecked == true;
        bool ceilingsEnabled = ceilings.EnabledInput.IsChecked == true;
        bool writeOwnership = writeOwnershipInput.IsChecked == true;
        bool customRoomIdentifier = ReadChoice(
            roomIdentifierModeInput,
            RoomIdentifierMode.Number) == RoomIdentifierMode.CustomParameter;
        ReportScopeKind scope = ReadChoice(scopeModeInput, ReportScopeKind.EntireProject);

        SetCategoryState(walls, wallsEnabled, writeOwnership);
        SetCategoryState(floors, floorsEnabled, writeOwnership);
        SetCategoryState(ceilings, ceilingsEnabled, writeOwnership);
        descriptionInput.IsEnabled = wallsEnabled || floorsEnabled || ceilingsEnabled;
        roomIdentifierParameterInput.IsEnabled = customRoomIdentifier;
        levelInput.IsEnabled = scope == ReportScopeKind.Level;
        sectionParameterInput.IsEnabled = scope == ReportScopeKind.Section;
        sectionValueInput.IsEnabled = scope == ReportScopeKind.Section;
    }

    private static void SetCategoryState(
        CategoryControls controls,
        bool categoryEnabled,
        bool writeOwnership)
    {
        controls.ClassificationInput.IsEnabled = categoryEnabled;
        controls.OwnershipInput.IsEnabled = categoryEnabled && writeOwnership;
        controls.OutputDescriptionInput.IsEnabled = categoryEnabled;
        controls.OutputAreaInput.IsEnabled = categoryEnabled;
    }

    private void UpdateValidation()
    {
        FinishScheduleValidationResult validation = validator.Validate(
            ReadSettings(),
            catalog,
            categories);
        FinishScheduleLaunchState launchState = FinishScheduleLaunchState.Create(
            validation,
            writePreviewFactory is not null && writeApplyFactory is not null);
        FinishScheduleValidationResult previewValidation = previewValidator.Validate(ReadSettings());

        generateButton.IsEnabled = launchState.CanGenerate;
        generateButton.ToolTip = launchState.GenerateToolTip;
        previewButton.IsEnabled = status.HasActiveDocument
            && previewFactory is not null
            && previewValidation.IsValid;
        previewButton.ToolTip = CreatePreviewToolTip(previewValidation);
        footerStatus.Text = launchState.StatusText;
        footerStatus.Foreground = launchState.IsConfigurationValid
            ? TrueBimBrushes.Success
            : TrueBimBrushes.Warning;

        if (validation.IsValid)
        {
            validationText.Text = "Все обязательные поля заполнены, выбранные параметры совместимы. Профиль можно безопасно сохранить.";
            ApplyBannerSeverity(validationBanner, validationIcon, TrueBimUiSeverity.Success);
            return;
        }

        IEnumerable<string> visibleIssues = validation.Issues
            .Select(issue => $"• {issue.Message}");
        string outputGuidance = validation.Issues.Any(IsMissingOutputParameter)
            ? "Добавьте рекомендуемые общие параметры кнопкой «Добавить параметры по умолчанию» или выберите совместимые существующие.\n"
            : string.Empty;
        validationText.Text = $"Настройка пока не завершена:\n{outputGuidance}{string.Join("\n", visibleIssues)}";
        ApplyBannerSeverity(validationBanner, validationIcon, TrueBimUiSeverity.Warning);
    }

    private static bool IsMissingOutputParameter(FinishScheduleValidationIssue issue)
    {
        return issue.Code.EndsWith(".missing", StringComparison.Ordinal)
            && (issue.Field == "room_list_output"
                || issue.Field.EndsWith(".output_description", StringComparison.Ordinal)
                || issue.Field.EndsWith(".output_area", StringComparison.Ordinal));
    }

    private string CreatePreviewToolTip(FinishScheduleValidationResult validation)
    {
        if (!status.HasActiveDocument || previewFactory is null)
        {
            return "Откройте документ Revit, чтобы собрать read-only предпросмотр.";
        }

        return validation.IsValid
            ? "Один раз собрать помещения, стены, перекрытия и типы без изменения модели."
            : validation.Issues[0].Message;
    }

    private void InvalidatePreview()
    {
        previewText.Text = "Настройки изменены. Обновите предпросмотр, чтобы увидеть актуальный состав области.";
        ApplyBannerSeverity(previewBanner, previewIcon, TrueBimUiSeverity.Info);
        SetCurrentReport(string.Empty);
        SetLastSchedule(null);
    }

    private void RunPreview()
    {
        FinishScheduleSettings settings = ReadSettings();
        FinishScheduleValidationResult validation = previewValidator.Validate(settings);
        if (!validation.IsValid || previewFactory is null)
        {
            previewText.Text = validation.IsValid
                ? "Документ Revit недоступен для предпросмотра."
                : string.Join("\n", validation.Issues.Select(issue => $"• {issue.Message}"));
            ApplyBannerSeverity(previewBanner, previewIcon, TrueBimUiSeverity.Warning);
            return;
        }

        try
        {
            FinishSchedulePreviewResult result = previewFactory(settings);
            previewText.Text = FormatPreview(result, settings);
            SetCurrentReport(reportBuilder.BuildPreview(result, settings));
            ApplyBannerSeverity(
                previewBanner,
                previewIcon,
                result.Warnings.Count == 0 ? TrueBimUiSeverity.Success : TrueBimUiSeverity.Warning);
            footerStatus.Text = "Предпросмотр обновлён. Модель Revit не изменялась.";
            footerStatus.Foreground = TrueBimBrushes.Success;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to build Finish Schedule preview.", exception);
            previewText.Text = "Не удалось собрать предпросмотр. Подробности записаны в лог TrueBIM.";
            ApplyBannerSeverity(previewBanner, previewIcon, TrueBimUiSeverity.Danger);
        }
    }

    private void RunGeneration()
    {
        FinishScheduleSettings settings = ReadSettings();
        FinishScheduleValidationResult validation = validator.Validate(
            settings,
            catalog,
            categories);
        if (!validation.IsValid || writePreviewFactory is null || writeApplyFactory is null)
        {
            previewText.Text = validation.IsValid
                ? "Workflow записи недоступен для текущего документа."
                : string.Join("\n", validation.Issues.Select(issue => $"• {issue.Message}"));
            ApplyBannerSeverity(previewBanner, previewIcon, TrueBimUiSeverity.Warning);
            return;
        }

        try
        {
            SaveProfile(showFeedback: false);
            FinishScheduleWritePreview writePreview = writePreviewFactory(settings);
            previewText.Text = FormatWritePreview(writePreview);
            SetCurrentReport(reportBuilder.BuildWritePreview(writePreview));
            SetLastSchedule(null);
            ApplyBannerSeverity(
                previewBanner,
                previewIcon,
                writePreview.CanApply ? TrueBimUiSeverity.Info : TrueBimUiSeverity.Danger);
            if (!writePreview.CanApply)
            {
                footerStatus.Text = "Запись не начата: исправьте критические ошибки preflight.";
                footerStatus.Foreground = TrueBimBrushes.Danger;
                return;
            }

            if (writePreview.RequiresTransaction)
            {
                MessageBoxResult confirmation = MessageBox.Show(
                    this,
                    CreateWriteConfirmation(writePreview),
                    "Ведомость отделки — подтверждение записи",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (confirmation != MessageBoxResult.Yes)
                {
                    footerStatus.Text = "Применение отменено. Модель Revit не изменялась.";
                    footerStatus.Foreground = TrueBimBrushes.TextSecondary;
                    return;
                }
            }

            FinishScheduleWriteResult result = writeApplyFactory(writePreview);
            bool incompleteCalculation = writePreview.Calculation is not null
                && FinishGeometryWarningClassifier.HasIncompleteScheduleValues(writePreview.Calculation);
            previewText.Text = FormatWriteResult(writePreview, result);
            SetCurrentReport(reportBuilder.BuildResult(writePreview, result));
            SetLastSchedule(result.Succeeded ? result.Schedule?.ScheduleId : null);
            ApplyBannerSeverity(
                previewBanner,
                previewIcon,
                result.Status switch
                {
                    FinishScheduleWriteStatus.Applied => result.Warnings.Count == 0 && !incompleteCalculation
                        ? TrueBimUiSeverity.Success
                        : TrueBimUiSeverity.Warning,
                    FinishScheduleWriteStatus.NoChanges => incompleteCalculation
                        ? TrueBimUiSeverity.Warning
                        : TrueBimUiSeverity.Info,
                    _ => TrueBimUiSeverity.Danger
                });
            footerStatus.Text = incompleteCalculation
                ? $"{result.Message} Расчёт выполнен частично; проверьте предупреждения геометрии."
                : result.Message;
            footerStatus.Foreground = result.Succeeded && !incompleteCalculation
                ? TrueBimBrushes.Success
                : result.Succeeded
                    ? TrueBimBrushes.Warning
                : TrueBimBrushes.Danger;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to prepare or apply Finish Schedule write plan.", exception);
            previewText.Text = "Не удалось подготовить или применить план записи. Подробности записаны в лог TrueBIM.";
            ApplyBannerSeverity(previewBanner, previewIcon, TrueBimUiSeverity.Danger);
            footerStatus.Text = "Модель не оставлена в частично обновлённом состоянии.";
            footerStatus.Foreground = TrueBimBrushes.Danger;
        }
    }

    private void CopyCurrentReport()
    {
        if (string.IsNullOrWhiteSpace(currentReportText))
        {
            footerStatus.Text = "Сначала выполните предпросмотр или формирование ведомости.";
            footerStatus.Foreground = TrueBimBrushes.Warning;
            return;
        }

        try
        {
            Clipboard.SetText(currentReportText);
            footerStatus.Text = "Полный отчёт скопирован в буфер обмена.";
            footerStatus.Foreground = TrueBimBrushes.Success;
        }
        catch (ExternalException exception)
        {
            logger.Error("Failed to copy Finish Schedule report to clipboard.", exception);
            footerStatus.Text = "Буфер обмена занят другим приложением. Повторите копирование.";
            footerStatus.Foreground = TrueBimBrushes.Warning;
        }
    }

    private void RequestScheduleOpen()
    {
        if (!lastScheduleId.HasValue)
        {
            footerStatus.Text = "Сначала сформируйте ведомость отделки.";
            footerStatus.Foreground = TrueBimBrushes.Warning;
            return;
        }

        RequestedScheduleId = lastScheduleId.Value;
        Close();
    }

    private void OpenConfiguration()
    {
        FinishScheduleConfigurationWindow window = new(
            ReadSettings(),
            catalog,
            categories,
            configurationStorage,
            logger)
        {
            Owner = this
        };
        if (window.ShowDialog() != true || window.UpdatedSettings is null)
        {
            return;
        }

        ApplySettings(preferredParameterResolver.Resolve(
            window.UpdatedSettings,
            catalog,
            categories));
        InvalidatePreview();
        footerStatus.Text = "Конфигурация проекта применена к текущему расчёту.";
        footerStatus.Foreground = TrueBimBrushes.Success;
    }

    private void AddDefaultParameters()
    {
        if (defaultParameterFactory is null)
        {
            footerStatus.Text = "Документ Revit недоступен для создания параметров.";
            footerStatus.Foreground = TrueBimBrushes.Warning;
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            "TrueBIM создаст только недостающие общие параметры ведомости отделки и добавит требуемые привязки категорий. Существующие совместимые параметры и их значения сохранятся.\n\nПродолжить?",
            "Ведомость отделки — параметры по умолчанию",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            FinishScheduleSettings current = ReadSettings();
            FinishScheduleDefaultParameterResult result = defaultParameterFactory();
            catalog = result.Catalog;
            ApplySettings(preferredParameterResolver.Resolve(current, catalog, categories));
            InvalidatePreview();
            string warningSuffix = result.Warnings.Count == 0
                ? string.Empty
                : $" Предупреждений: {result.Warnings.Count}; подробности в логе.";
            footerStatus.Text = $"Параметры обработаны: создано — {result.CreatedCount}, обновлено — {result.UpdatedCount}, уже настроено — {result.ExistingCount}.{warningSuffix}";
            footerStatus.Foreground = result.Warnings.Count == 0
                ? TrueBimBrushes.Success
                : TrueBimBrushes.Warning;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to add default Finish Schedule parameters.", exception);
            footerStatus.Text = "Не удалось добавить параметры по умолчанию. Изменения транзакции отменены; подробности в логе TrueBIM.";
            footerStatus.Foreground = TrueBimBrushes.Danger;
        }
    }

    private void SetCurrentReport(string report)
    {
        currentReportText = report ?? string.Empty;
        copyReportButton.IsEnabled = !string.IsNullOrWhiteSpace(currentReportText);
    }

    private void SetLastSchedule(long? scheduleId)
    {
        lastScheduleId = scheduleId;
        openScheduleButton.IsEnabled = scheduleId.HasValue;
    }

    private static string FormatWritePreview(FinishScheduleWritePreview preview)
    {
        int skippedOwnership = Math.Max(
            0,
            preview.OwnershipPlan.TargetElementCount
                - preview.OwnershipPlan.Changes.Count
                - preview.OwnershipPlan.UnchangedCount);
        List<string> lines =
        [
            $"План записи: помещений — {preview.RoomCount}; групп — {preview.GroupCount}.",
            $"Параметры помещений: изменений — {preview.RoomPlan.Changes.Count}; "
                + $"без изменений — {preview.RoomPlan.UnchangedCount}; заблокировано — {preview.RoomPlan.BlockedCount}.",
            $"Ownership: элементов — {preview.OwnershipPlan.TargetElementCount}; "
                + $"изменений — {preview.OwnershipPlan.Changes.Count}; пропущено preflight — {skippedOwnership}.",
            $"Спецификация «{preview.Schedule.Plan?.ScheduleName ?? "—"}»: "
                + FormatScheduleAction(preview.Schedule.Action) + "."
        ];
        if (preview.Calculation is not null)
        {
            lines.AddRange(FinishScheduleDiagnosticGuidanceBuilder.Build(preview.Calculation)
                .Select(item => $"• {item}"));
        }

        lines.AddRange(preview.Issues.Take(4).Select(issue => $"• {issue.Message}"));
        lines.AddRange(preview.RoomPlan.Changes
            .Concat(preview.OwnershipPlan.Changes)
            .Take(5)
            .Select(change =>
                $"• {change.Role}, id {change.ElementId}: «{CompactValue(change.PreviousValue)}» → «{CompactValue(change.NewValue)}»"));
        if (preview.TotalChangeCount > 5)
        {
            lines.Add($"• Ещё изменений: {preview.TotalChangeCount - 5}.");
        }

        return string.Join("\n", lines);
    }

    private static string CreateWriteConfirmation(FinishScheduleWritePreview preview)
    {
        string[] samples = preview.RoomPlan.Changes
            .Concat(preview.OwnershipPlan.Changes)
            .Take(3)
            .Select(change =>
                $"• {change.Role}, id {change.ElementId}: «{CompactValue(change.PreviousValue)}» → «{CompactValue(change.NewValue)}»")
            .ToArray();
        string sampleText = samples.Length == 0
            ? string.Empty
            : $"\n\nПримеры изменений:\n{string.Join("\n", samples)}";
        return "Выбранные параметры помещений будут обновлены. Один комплект параметров поддерживает "
            + "один активный вариант агрегации.\n\n"
            + $"Параметры помещений: {preview.RoomPlan.Changes.Count} изменений.\n"
            + $"Ownership физических элементов: {preview.OwnershipPlan.Changes.Count} изменений.\n\n"
            + $"Спецификация: {FormatScheduleAction(preview.Schedule.Action)}.\n\n"
            + "Параметры и управляемая спецификация создаются или обновляются атомарно."
            + sampleText
            + "\n\n"
            + "Продолжить?";
    }

    private static string FormatWriteResult(
        FinishScheduleWritePreview preview,
        FinishScheduleWriteResult result)
    {
        List<string> lines =
        [
            result.Message,
            $"Помещений: {preview.RoomCount}; групп: {preview.GroupCount}; "
                + $"записано Room-значений: {result.AppliedRoomValues}.",
            $"Ownership: записано — {result.AppliedOwnershipValues}; пропущено — {result.SkippedOwnershipValues}."
        ];
        if (result.Schedule is not null)
        {
            lines.Add(
                $"Спецификация «{result.Schedule.ScheduleName}» (id {result.Schedule.ScheduleId}): "
                    + FormatAppliedScheduleAction(result.Schedule.Action) + ".");
        }

        if (preview.Calculation is not null)
        {
            lines.AddRange(FinishScheduleDiagnosticGuidanceBuilder.Build(preview.Calculation)
                .Select(item => $"• {item}"));
        }

        FinishScheduleStageTiming? totalApply = result.Performance?.Stages
            .FirstOrDefault(timing => timing.Stage == FinishScheduleStageNames.TotalApply);
        if (totalApply is not null)
        {
            lines.Add($"Время применения: {totalApply.ElapsedMilliseconds} мс.");
        }

        lines.AddRange(result.Warnings.Take(4).Select(warning => $"• {warning}"));
        if (result.Warnings.Count > 4)
        {
            lines.Add($"• Ещё предупреждений: {result.Warnings.Count - 4}.");
        }

        return string.Join("\n", lines);
    }

    private static string FormatScheduleAction(FinishRoomScheduleAction action)
    {
        return action switch
        {
            FinishRoomScheduleAction.Create => "будет создана",
            FinishRoomScheduleAction.Update => "будет обновлена",
            FinishRoomScheduleAction.NoChanges => "уже актуальна",
            FinishRoomScheduleAction.Blocked => "заблокирована",
            _ => action.ToString()
        };
    }

    private static string FormatAppliedScheduleAction(FinishRoomScheduleAction action)
    {
        return action switch
        {
            FinishRoomScheduleAction.Create => "создана",
            FinishRoomScheduleAction.Update => "обновлена",
            FinishRoomScheduleAction.NoChanges => "оставлена без изменений",
            FinishRoomScheduleAction.Blocked => "заблокирована",
            _ => action.ToString()
        };
    }

    private static string CompactValue(string value)
    {
        string compact = string.IsNullOrEmpty(value)
            ? "пусто"
            : value.Replace("\r\n", " / ").Replace("\n", " / ").Trim();
        return compact.Length <= 80
            ? compact
            : $"{compact.Substring(0, 77)}…";
    }

    private static string FormatPreview(
        FinishSchedulePreviewResult result,
        FinishScheduleSettings settings)
    {
        List<string> lines =
        [
            $"Помещения в области: {result.RoomScope.SelectedRooms.Count} из {result.CollectedRooms}; "
                + $"невалидных — {result.RoomScope.InvalidRooms.Count}, вне области — {result.RoomScope.OutsideScopeCount}.",
            FormatCategory("Стены", settings.Walls.IsEnabled, result.Walls, result.Quantities?.Walls),
            FormatCategory("Полы", settings.Floors.IsEnabled, result.Floors, result.Quantities?.Floors),
            FormatCategory("Потолки", settings.Ceilings.IsEnabled, result.Ceilings, result.Quantities?.Ceilings),
            $"Spatial index: {result.Index.IndexedElements} элементов; "
                + $"потенциальных пар помещение–элемент — {result.Index.PotentialRoomElementPairs}."
        ];
        if (result.Aggregation is not null)
        {
            lines.Insert(
                4,
                $"Группировка: {result.Aggregation.GroupCount}; "
                    + $"помещений с подготовленным output — {result.Aggregation.RoomCount}.");
        }

        FinishScheduleStageTiming? totalCalculation = result.Performance?.Stages
            .FirstOrDefault(timing => timing.Stage == FinishScheduleStageNames.TotalCalculation);
        if (totalCalculation is not null)
        {
            lines.Add(
                $"Время расчёта: {totalCalculation.ElapsedMilliseconds} мс; "
                    + $"element geometry cache hits — {result.Performance!.Cache.Geometry.ElementHits}.");
        }

        lines.AddRange(result.Warnings.Take(3).Select(warning => $"• {warning}"));
        lines.AddRange(FinishScheduleDiagnosticGuidanceBuilder.Build(result)
            .Select(item => $"• {item}"));
        return string.Join("\n", lines);
    }

    private static string FormatCategory(
        string name,
        bool enabled,
        FinishPreviewCategoryCounts counts,
        FinishQuantityCategorySummary? quantities)
    {
        if (!enabled)
        {
            return $"{name}: категория отключена.";
        }

        string geometry = quantities is null
            ? string.Empty
            : $"; геометрия — {quantities.OccurrenceCount} связей / "
                + $"{quantities.AreaSquareMeters.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))} м²";
        return $"{name}: в области — {counts.InScope}; классифицировано — {counts.Classified}; "
            + $"собрано источников — {counts.SourceCollected}{geometry}.";
    }

    private void SaveProfile(bool showFeedback)
    {
        try
        {
            FinishScheduleSettings settings = ReadSettings();
            profileStorage.Save(settings);
            FinishScheduleValidationResult validation = validator.Validate(
                settings,
                catalog,
                categories);
            if (!validation.IsValid)
            {
                logger.Warning(
                    $"Finish Schedule profile saved incomplete. Issues={validation.Issues.Count}; "
                        + string.Join(
                            " | ",
                            validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
            }

            if (showFeedback)
            {
                footerStatus.Text = validation.IsValid
                    ? "Профиль сохранён. Настройки совместимы."
                    : $"Профиль сохранён как незавершённый: обязательных или несовместимых настроек — {validation.Issues.Count}.";
            }

            logger.Info($"Finish Schedule profile saved to '{profileStorage.SettingsPath}'.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Error("Failed to save Finish Schedule profile.", exception);
            if (showFeedback)
            {
                footerStatus.Text = "Не удалось сохранить профиль. Подробности записаны в лог TrueBIM.";
                footerStatus.Foreground = TrueBimBrushes.Danger;
            }
        }
    }

    private static void SetOptions(
        ComboBox input,
        IReadOnlyList<FinishScheduleParameterOption> options)
    {
        string? selectedKey = GetSelectedParameter(input)?.StableKey;
        input.ItemsSource = options;
        input.DisplayMemberPath = nameof(FinishScheduleParameterOption.DisplayName);
        input.SelectedItem = selectedKey is null
            ? null
            : options.FirstOrDefault(option => option.Reference.StableKey == selectedKey);
    }

    private static void SetSelectedParameter(ComboBox input, ParameterReference? reference)
    {
        input.SelectedItem = reference is null
            ? null
            : input.Items
                .OfType<FinishScheduleParameterOption>()
                .FirstOrDefault(option => option.Reference.StableKey == reference.StableKey);
    }

    private static ParameterReference? GetSelectedParameter(ComboBox input)
    {
        return (input.SelectedItem as FinishScheduleParameterOption)?.Reference;
    }

    private static void SelectChoice<TEnum>(ComboBox input, TEnum value)
        where TEnum : struct, Enum
    {
        input.SelectedItem = input.Items
            .OfType<Choice<TEnum>>()
            .FirstOrDefault(choice => EqualityComparer<TEnum>.Default.Equals(choice.Value, value));
    }

    private static TEnum ReadChoice<TEnum>(ComboBox input, TEnum fallback)
        where TEnum : struct, Enum
    {
        return input.SelectedItem is Choice<TEnum> choice ? choice.Value : fallback;
    }

    private static void AddCard(
        Panel content,
        string title,
        UIElement body,
        bool isLast = false)
    {
        Border card = TrueBimUi.CreateSectionCard(title, body);
        if (!isLast)
        {
            card.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        }

        content.Children.Add(card);
    }

    private static UIElement CreateDescription(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TrueBimBrushes.TextSecondary,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12)
        };
    }

    private static UIElement CreateFieldRow(
        string label,
        Control input,
        bool isLast = false)
    {
        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (!isLast)
        {
            row.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        }

        TextBlock labelText = TrueBimUi.CreateFieldLabel(label);
        labelText.VerticalAlignment = VerticalAlignment.Center;
        labelText.Margin = new Thickness(0, 0, TrueBimTheme.Spacing16, 0);
        row.Children.Add(labelText);

        input.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(input, 1);
        row.Children.Add(input);
        return row;
    }

    private static UIElement CreateOutputHeader()
    {
        Grid header = CreateOutputGrid();
        header.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing4);
        AddOutputCell(header, "Категория", 0, isHeader: true);
        AddOutputCell(header, "Описание", 1, isHeader: true);
        AddOutputCell(header, "Площадь", 2, isHeader: true);
        return header;
    }

    private static UIElement CreateOutputRow(
        string categoryName,
        ComboBox description,
        ComboBox area,
        bool isLast = false)
    {
        Grid row = CreateOutputGrid();
        if (!isLast)
        {
            row.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8);
        }

        AddOutputCell(row, categoryName, 0, isHeader: false);
        description.Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, 0);
        Grid.SetColumn(description, 1);
        row.Children.Add(description);
        Grid.SetColumn(area, 2);
        row.Children.Add(area);
        return row;
    }

    private static Grid CreateOutputGrid()
    {
        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private static void AddOutputCell(Grid grid, string text, int column, bool isHeader)
    {
        TextBlock block = new()
        {
            Text = text,
            Foreground = isHeader ? TrueBimBrushes.TextSecondary : TrueBimBrushes.TextPrimary,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, 0)
        };
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private static ComboBox CreateParameterInput(string toolTip)
    {
        return new ComboBox
        {
            MinWidth = 280,
            Height = 34,
            Style = TrueBimStyles.CreateComboBoxStyle(),
            ToolTip = toolTip,
            IsTextSearchEnabled = true
        };
    }

    private static ComboBox CreateChoiceInput()
    {
        return new ComboBox
        {
            MinWidth = 280,
            Height = 34,
            Style = TrueBimStyles.CreateComboBoxStyle()
        };
    }

    private static ComboBox CreateClassificationInput(
        string defaultValue,
        string toolTip)
    {
        return new ComboBox
        {
            ItemsSource = new[] { defaultValue },
            Text = defaultValue,
            IsEditable = true,
            IsTextSearchEnabled = true,
            MinWidth = 280,
            Height = 34,
            Style = TrueBimStyles.CreateComboBoxStyle(),
            ToolTip = toolTip
        };
    }

    private static TextBox CreateTextInput(string toolTip)
    {
        return new TextBox
        {
            MinWidth = 280,
            Height = 34,
            Style = TrueBimStyles.CreateTextBoxStyle(),
            ToolTip = toolTip,
            VerticalContentAlignment = VerticalAlignment.Center
        };
    }

    private static CheckBox CreateCheckBox(string text, string toolTip)
    {
        return new CheckBox
        {
            Content = text,
            Style = TrueBimStyles.CreateCheckBoxStyle(),
            ToolTip = toolTip,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static Border CreateStatusBanner(Image icon, TextBlock text)
    {
        DockPanel content = new()
        {
            LastChildFill = true
        };
        content.Children.Add(icon);
        content.Children.Add(text);

        return new Border
        {
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Padding = new Thickness(TrueBimTheme.Spacing12),
            Child = content
        };
    }

    private static void ApplyBannerSeverity(
        Border banner,
        Image iconControl,
        TrueBimUiSeverity severity)
    {
        banner.Background = TrueBimBrushes.BackgroundForSeverity(severity);
        banner.BorderBrush = TrueBimBrushes.ForSeverity(severity);
        TrueBimIcon icon = severity switch
        {
            TrueBimUiSeverity.Success => TrueBimIcon.Check,
            TrueBimUiSeverity.Warning => TrueBimIcon.Warning,
            TrueBimUiSeverity.Danger => TrueBimIcon.Error,
            _ => TrueBimIcon.Info
        };
        iconControl.Source = IconFactory.CreateImage(
            icon,
            TrueBimBrushes.ForSeverity(severity).Color);
    }

    private sealed class CategoryControls
    {
        public CategoryControls(
            string displayName,
            string defaultClassification,
            string classificationToolTip,
            string preferredOwnershipName,
            string preferredDescriptionName,
            string preferredAreaName)
        {
            EnabledInput = CreateCheckBox(
                displayName,
                $"Включить категорию «{displayName}» в ведомость и группировку помещений.");
            EnabledInput.IsChecked = true;
            ClassificationInput = CreateClassificationInput(defaultClassification, classificationToolTip);
            OwnershipInput = CreateParameterInput(
                $"Записываемый текстовый параметр элементов категории «{displayName}». Рекомендуемое имя: «{preferredOwnershipName}».");
            OutputDescriptionInput = CreateParameterInput(
                $"Параметр помещения для агрегированного описания категории «{displayName}». Рекомендуемое имя: «{preferredDescriptionName}».");
            OutputAreaInput = CreateParameterInput(
                $"Параметр помещения для агрегированной площади категории «{displayName}». Рекомендуемое имя: «{preferredAreaName}».");
        }

        public CheckBox EnabledInput { get; }

        public ComboBox ClassificationInput { get; }

        public ComboBox OwnershipInput { get; }

        public ComboBox OutputDescriptionInput { get; }

        public ComboBox OutputAreaInput { get; }
    }

    private sealed record Choice<TEnum>(TEnum Value, string DisplayName)
        where TEnum : struct, Enum;
}
