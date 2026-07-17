using System.IO;
using System.Windows;
using System.Windows.Controls;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.FinishSchedule.UI;

public sealed class FinishScheduleWindow : TrueBimWindow
{
    private const bool WorkflowAvailable = false;

    private readonly FinishScheduleModuleStatus status;
    private readonly ParameterCatalog catalog;
    private readonly FinishScheduleParameterCategories categories;
    private readonly IReadOnlyList<FinishScheduleLevelOption> levels;
    private readonly FinishScheduleProfileStorage profileStorage;
    private readonly ITrueBimLogger logger;
    private readonly FinishScheduleSettingsValidator validator;
    private readonly FinishScheduleParameterOptionService optionService;

    private readonly CategoryControls walls = new(
        "Стены",
        "Внутренняя отделка",
        "Значение «Группа модели» для типов отделочных стен.");
    private readonly CategoryControls floors = new(
        "Полы",
        "Пол",
        "Значение «Группа модели» для типов отделочных перекрытий пола.");
    private readonly CategoryControls ceilings = new(
        "Потолки",
        "Потолки",
        "Значение «Группа модели» для перекрытий, используемых как потолок.");

    private readonly ComboBox descriptionInput = CreateParameterInput(
        "Текстовый параметр типа, доступный у всех включённых физических категорий.");
    private readonly ComboBox roomIdentifierModeInput = CreateChoiceInput();
    private readonly ComboBox roomIdentifierParameterInput = CreateParameterInput(
        "Текстовый параметр экземпляра помещения для пользовательского идентификатора.");
    private readonly CheckBox writeOwnershipInput = CreateCheckBox(
        "Записывать фактическую принадлежность в элементы отделки",
        "Параметры физических элементов будут только выбраны и проверены. Запись появится в FS-007.");
    private readonly ComboBox roomListOutputInput = CreateParameterInput(
        "Записываемый текстовый параметр экземпляра помещения для списка помещений группы.");
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
        VerticalAlignment = VerticalAlignment.Center,
        MaxWidth = 520
    };
    private readonly Button saveButton;
    private readonly Button generateButton;
    private readonly Border validationBanner;

    private bool isUpdating;

    public FinishScheduleWindow(
        FinishScheduleModuleStatus status,
        ParameterCatalog catalog,
        FinishScheduleParameterCategories categories,
        IEnumerable<FinishScheduleLevelOption> levels,
        FinishScheduleProfileStorage profileStorage,
        ITrueBimLogger logger)
    {
        this.status = status ?? throw new ArgumentNullException(nameof(status));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.categories = categories ?? throw new ArgumentNullException(nameof(categories));
        this.levels = (levels ?? throw new ArgumentNullException(nameof(levels))).ToArray();
        this.profileStorage = profileStorage ?? throw new ArgumentNullException(nameof(profileStorage));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        ParameterCatalogMatcher matcher = new();
        validator = new FinishScheduleSettingsValidator(matcher);
        optionService = new FinishScheduleParameterOptionService(matcher);

        saveButton = TrueBimUi.CreateSecondaryButton(
            "Сохранить профиль",
            TrueBimIcon.Settings,
            (_, _) => SaveProfile(showFeedback: true),
            minWidth: 150);
        saveButton.ToolTip = "Сохранить текущие настройки локально, даже если конфигурация ещё не завершена.";

        generateButton = TrueBimUi.CreatePrimaryButton(
            "Сформировать",
            TrueBimIcon.Apply,
            isEnabled: false,
            minWidth: 140);
        ToolTipService.SetShowOnDisabled(generateButton, true);

        validationBanner = CreateValidationBanner();
        validationBanner.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        ApplyBannerSeverity(validationBanner, TrueBimUiSeverity.Warning);

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
                "Настройка источников, выходных параметров и области ведомости помещений",
                TrueBimIcon.FinishSchedule),
            commandBar: null,
            body: CreateBody(),
            status: null,
            footer: CreateFooter());

        AttachEvents();
        ApplySettings(profileStorage.Load());
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
                ? $"Документ: {status.DocumentName}. Каталог содержит вариантов параметров: {catalog.Items.Count}. Настройки сохраняются локально; модель Revit на этом этапе не изменяется."
                : "Документ Revit не открыт. Профиль можно просмотреть, но выбор параметров и формирование недоступны.",
            status.HasActiveDocument ? TrueBimUiSeverity.Info : TrueBimUiSeverity.Warning);
        contextBanner.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        content.Children.Add(contextBanner);

        AddCard(content, "1. Категории отделки", CreateCategorySelectionContent());
        AddCard(content, "2. Классификация элементов", CreateClassificationContent());
        AddCard(content, "3. Описание отделки", CreateDescriptionContent());
        AddCard(content, "4. Идентификатор помещения", CreateRoomIdentifierContent());
        AddCard(content, "5. Принадлежность элементов помещениям", CreateOwnershipContent());
        AddCard(content, "6. Выходные параметры помещений", CreateOutputContent());
        AddCard(content, "7. Область расчёта", CreateScopeContent());
        AddCard(content, "8. Спецификация", CreateScheduleContent(), isLast: true);
        content.Children.Add(validationBanner);

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content
        };
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

        return TrueBimUi.CreateFooter(footerStatus, saveButton, generateButton, closeButton);
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
                optionService.GetOwnershipOptions(catalog, categories.Floors));
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
            WorkflowAvailable);

        generateButton.IsEnabled = launchState.CanGenerate;
        generateButton.ToolTip = launchState.GenerateToolTip;
        footerStatus.Text = launchState.StatusText;
        footerStatus.Foreground = launchState.IsConfigurationValid
            ? TrueBimBrushes.Success
            : TrueBimBrushes.Warning;

        if (validation.IsValid)
        {
            validationText.Text = "Все обязательные поля заполнены, выбранные параметры совместимы. Профиль можно безопасно сохранить.";
            ApplyBannerSeverity(validationBanner, TrueBimUiSeverity.Success);
            return;
        }

        IEnumerable<string> visibleIssues = validation.Issues
            .Take(5)
            .Select(issue => $"• {issue.Message}");
        string remaining = validation.Issues.Count > 5
            ? $"\n• Ещё ошибок: {validation.Issues.Count - 5}."
            : string.Empty;
        validationText.Text = $"Конфигурация пока не готова:\n{string.Join("\n", visibleIssues)}{remaining}";
        ApplyBannerSeverity(validationBanner, TrueBimUiSeverity.Warning);
    }

    private void SaveProfile(bool showFeedback)
    {
        try
        {
            FinishScheduleSettings settings = ReadSettings();
            profileStorage.Save(settings);
            if (showFeedback)
            {
                FinishScheduleValidationResult validation = validator.Validate(
                    settings,
                    catalog,
                    categories);
                footerStatus.Text = validation.IsValid
                    ? "Профиль сохранён. Настройки совместимы."
                    : $"Профиль сохранён как незавершённый: ошибок — {validation.Issues.Count}.";
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

    private Border CreateValidationBanner()
    {
        DockPanel content = new()
        {
            LastChildFill = true
        };
        content.Children.Add(validationIcon);
        content.Children.Add(validationText);

        return new Border
        {
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Padding = new Thickness(TrueBimTheme.Spacing12),
            Child = content
        };
    }

    private void ApplyBannerSeverity(Border banner, TrueBimUiSeverity severity)
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
        validationIcon.Source = IconFactory.CreateImage(
            icon,
            TrueBimBrushes.ForSeverity(severity).Color);
    }

    private sealed class CategoryControls
    {
        public CategoryControls(string displayName, string defaultClassification, string classificationToolTip)
        {
            EnabledInput = CreateCheckBox(
                displayName,
                $"Включить категорию «{displayName}» в ведомость и группировку помещений.");
            EnabledInput.IsChecked = true;
            ClassificationInput = CreateClassificationInput(defaultClassification, classificationToolTip);
            OwnershipInput = CreateParameterInput(
                $"Записываемый текстовый параметр элементов категории «{displayName}».");
            OutputDescriptionInput = CreateParameterInput(
                $"Параметр помещения для агрегированного описания категории «{displayName}».");
            OutputAreaInput = CreateParameterInput(
                $"Параметр помещения для агрегированной площади категории «{displayName}».");
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
