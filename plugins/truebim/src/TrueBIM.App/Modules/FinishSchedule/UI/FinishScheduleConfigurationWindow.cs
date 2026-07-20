using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.FinishSchedule.UI;

public sealed class FinishScheduleConfigurationWindow : TrueBimWindow
{
    private readonly ParameterCatalog catalog;
    private readonly FinishScheduleParameterCategories categories;
    private readonly FinishScheduleConfigurationStorage configurationStorage;
    private readonly FinishScheduleParameterOptionService optionService;
    private readonly FinishSchedulePreferredParameterResolver preferredParameterResolver;
    private readonly ITrueBimLogger logger;

    private readonly CategoryControls walls = new("Стены", "Внутренняя отделка");
    private readonly CategoryControls floors = new("Полы", "Пол");
    private readonly CategoryControls ceilings = new("Потолки", "Потолки");
    private readonly ComboBox descriptionInput = CreateParameterInput();
    private readonly ComboBox roomIdentifierModeInput = CreateChoiceInput();
    private readonly ComboBox roomIdentifierParameterInput = CreateParameterInput();
    private readonly ComboBox roomListOutputInput = CreateParameterInput();
    private readonly TextBlock footerStatus = new()
    {
        Text = "Изменения будут применены к текущему профилю после нажатия «Применить».",
        TextWrapping = TextWrapping.Wrap,
        Foreground = TrueBimBrushes.TextSecondary,
        VerticalAlignment = VerticalAlignment.Center
    };

    private FinishScheduleSettings baseSettings;
    private bool isUpdating;

    public FinishScheduleConfigurationWindow(
        FinishScheduleSettings settings,
        ParameterCatalog catalog,
        FinishScheduleParameterCategories categories,
        FinishScheduleConfigurationStorage configurationStorage,
        ITrueBimLogger logger)
    {
        baseSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.categories = categories ?? throw new ArgumentNullException(nameof(categories));
        this.configurationStorage = configurationStorage
            ?? throw new ArgumentNullException(nameof(configurationStorage));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ParameterCatalogMatcher matcher = new();
        optionService = new FinishScheduleParameterOptionService(matcher);
        preferredParameterResolver = new FinishSchedulePreferredParameterResolver(optionService);

        ConfigureChoices();
        AttachEvents();

        Title = "Конфигурация ведомости отделки";
        Icon = IconFactory.CreateImage(TrueBimIcon.Settings, TrueBimTheme.IconSizeRibbon);
        Width = 980;
        Height = 760;
        MinWidth = 820;
        MinHeight = 620;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        ApplyTrueBimShell(
            TrueBimUi.CreateHeader(
                "Конфигурация ведомости отделки",
                "Проектные соответствия параметров, которые обычно настраивает BIM-координатор",
                TrueBimIcon.Settings),
            commandBar: CreateCommandBar(),
            body: CreateBody(),
            status: footerStatus,
            footer: CreateFooter());

        ApplySettings(preferredParameterResolver.Resolve(baseSettings, catalog, categories));
    }

    public FinishScheduleSettings? UpdatedSettings { get; private set; }

    private void ConfigureChoices()
    {
        roomIdentifierModeInput.ItemsSource = new Choice<RoomIdentifierMode>[]
        {
            new(RoomIdentifierMode.Number, "Номер помещения"),
            new(RoomIdentifierMode.Name, "Имя помещения"),
            new(RoomIdentifierMode.CustomParameter, "Пользовательский параметр")
        };
        roomIdentifierModeInput.DisplayMemberPath = nameof(Choice<RoomIdentifierMode>.DisplayName);
    }

    private UIElement CreateCommandBar()
    {
        Button importButton = TrueBimUi.CreateSecondaryButton(
            "Импортировать",
            TrueBimIcon.Import,
            (_, _) => ImportConfiguration(),
            minWidth: 145);
        importButton.ToolTip = "Загрузить JSON-конфигурацию координатора и сопоставить параметры текущей модели.";
        Button exportButton = TrueBimUi.CreateSecondaryButton(
            "Экспортировать",
            TrueBimIcon.Export,
            (_, _) => ExportConfiguration(),
            minWidth: 145);
        exportButton.ToolTip = "Сохранить проектные соответствия в JSON для передачи другому пользователю или модели.";
        return TrueBimUi.CreateCommandBar(importButton, exportButton);
    }

    private UIElement CreateBody()
    {
        StackPanel content = new()
        {
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing4, 0)
        };
        Border info = TrueBimUi.CreateInfoBanner(
            "Эти настройки сохраняются в локальном профиле. Импорт переносит классификацию, источник описания, идентификатор помещения и выходные параметры; категории, область расчёта и имя спецификации не меняются.",
            TrueBimUiSeverity.Info);
        info.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        content.Children.Add(info);
        AddCard(content, FinishScheduleSectionTitles.Classification, CreateClassificationContent());
        AddCard(content, FinishScheduleSectionTitles.Description, CreateDescriptionContent());
        AddCard(content, FinishScheduleSectionTitles.RoomIdentifier, CreateRoomIdentifierContent());
        AddCard(content, FinishScheduleSectionTitles.RoomOutput, CreateOutputContent(), isLast: true);
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content
        };
    }

    private UIElement CreateClassificationContent()
    {
        StackPanel panel = new();
        panel.Children.Add(CreateDescription(
            $"Значения текстового параметра типа «{FinishScheduleSettings.ClassificationParameterName}»."));
        panel.Children.Add(CreateFieldRow("Стены", walls.ClassificationInput));
        panel.Children.Add(CreateFieldRow("Полы", floors.ClassificationInput));
        panel.Children.Add(CreateFieldRow("Потолки", ceilings.ClassificationInput, isLast: true));
        return panel;
    }

    private UIElement CreateDescriptionContent()
    {
        StackPanel panel = new();
        panel.Children.Add(CreateDescription(
            "Текстовый параметр типа должен быть доступен у всех включённых физических категорий."));
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

    private UIElement CreateOutputContent()
    {
        StackPanel panel = new();
        panel.Children.Add(CreateDescription(
            "Выберите записываемые текстовые параметры экземпляра помещения. Один параметр нельзя использовать для разных значений."));
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

    private UIElement CreateFooter()
    {
        Button applyButton = TrueBimUi.CreatePrimaryButton(
            "Применить",
            TrueBimIcon.Apply,
            (_, _) => ApplyAndClose(),
            minWidth: 120);
        Button cancelButton = TrueBimUi.CreateSecondaryButton(
            "Отмена",
            TrueBimIcon.Close,
            (_, _) => Close(),
            minWidth: 110);
        cancelButton.IsCancel = true;
        return TrueBimUi.CreateFooter(null, applyButton, cancelButton);
    }

    private void AttachEvents()
    {
        roomIdentifierModeInput.SelectionChanged += (_, _) =>
        {
            RefreshDependentState();
            MarkChanged();
        };
        roomIdentifierParameterInput.SelectionChanged += (_, _) => MarkChanged();
        descriptionInput.SelectionChanged += (_, _) => MarkChanged();
        roomListOutputInput.SelectionChanged += (_, _) => MarkChanged();
        foreach (CategoryControls controls in new[] { walls, floors, ceilings })
        {
            controls.ClassificationInput.SelectionChanged += (_, _) => MarkChanged();
            controls.ClassificationInput.AddHandler(
                TextBox.TextChangedEvent,
                new TextChangedEventHandler((_, _) => MarkChanged()));
            controls.OutputDescriptionInput.SelectionChanged += (_, _) => MarkChanged();
            controls.OutputAreaInput.SelectionChanged += (_, _) => MarkChanged();
        }
    }

    private void ApplySettings(FinishScheduleSettings settings)
    {
        baseSettings = settings;
        isUpdating = true;
        try
        {
            RefreshOptions();
            walls.ClassificationInput.Text = settings.Walls.ClassificationValue;
            floors.ClassificationInput.Text = settings.Floors.ClassificationValue;
            ceilings.ClassificationInput.Text = settings.Ceilings.ClassificationValue;
            SetSelectedParameter(descriptionInput, settings.DescriptionParameter);
            SelectChoice(roomIdentifierModeInput, settings.RoomIdentifier.Mode);
            SetSelectedParameter(roomIdentifierParameterInput, settings.RoomIdentifier.CustomParameter);
            SetSelectedParameter(roomListOutputInput, settings.RoomListOutputParameter);
            SetSelectedParameter(walls.OutputDescriptionInput, settings.Walls.OutputDescriptionParameter);
            SetSelectedParameter(walls.OutputAreaInput, settings.Walls.OutputAreaParameter);
            SetSelectedParameter(floors.OutputDescriptionInput, settings.Floors.OutputDescriptionParameter);
            SetSelectedParameter(floors.OutputAreaInput, settings.Floors.OutputAreaParameter);
            SetSelectedParameter(ceilings.OutputDescriptionInput, settings.Ceilings.OutputDescriptionParameter);
            SetSelectedParameter(ceilings.OutputAreaInput, settings.Ceilings.OutputAreaParameter);
        }
        finally
        {
            isUpdating = false;
        }

        RefreshDependentState();
    }

    private FinishScheduleSettings ReadSettings()
    {
        RoomIdentifierMode roomMode = ReadChoice(roomIdentifierModeInput, RoomIdentifierMode.Number);
        return baseSettings with
        {
            DescriptionParameter = GetSelectedParameter(descriptionInput),
            RoomIdentifier = new RoomIdentifierSettings(
                roomMode,
                roomMode == RoomIdentifierMode.CustomParameter
                    ? GetSelectedParameter(roomIdentifierParameterInput)
                    : null),
            RoomListOutputParameter = GetSelectedParameter(roomListOutputInput),
            Walls = baseSettings.Walls with
            {
                ClassificationValue = walls.ClassificationInput.Text.Trim(),
                OutputDescriptionParameter = GetSelectedParameter(walls.OutputDescriptionInput),
                OutputAreaParameter = GetSelectedParameter(walls.OutputAreaInput)
            },
            Floors = baseSettings.Floors with
            {
                ClassificationValue = floors.ClassificationInput.Text.Trim(),
                OutputDescriptionParameter = GetSelectedParameter(floors.OutputDescriptionInput),
                OutputAreaParameter = GetSelectedParameter(floors.OutputAreaInput)
            },
            Ceilings = baseSettings.Ceilings with
            {
                ClassificationValue = ceilings.ClassificationInput.Text.Trim(),
                OutputDescriptionParameter = GetSelectedParameter(ceilings.OutputDescriptionInput),
                OutputAreaParameter = GetSelectedParameter(ceilings.OutputAreaInput)
            }
        };
    }

    private void RefreshOptions()
    {
        SetOptions(
            descriptionInput,
            optionService.GetDescriptionOptions(
                catalog,
                categories,
                baseSettings.Walls.IsEnabled,
                baseSettings.Floors.IsEnabled,
                baseSettings.Ceilings.IsEnabled));
        SetOptions(roomIdentifierParameterInput, optionService.GetRoomIdentifierOptions(catalog, categories));
        IReadOnlyList<FinishScheduleParameterOption> outputs = optionService.GetRoomOutputOptions(catalog, categories);
        SetOptions(roomListOutputInput, outputs);
        SetOptions(walls.OutputDescriptionInput, outputs);
        SetOptions(walls.OutputAreaInput, outputs);
        SetOptions(floors.OutputDescriptionInput, outputs);
        SetOptions(floors.OutputAreaInput, outputs);
        SetOptions(ceilings.OutputDescriptionInput, outputs);
        SetOptions(ceilings.OutputAreaInput, outputs);
    }

    private void RefreshDependentState()
    {
        roomIdentifierParameterInput.IsEnabled = ReadChoice(
            roomIdentifierModeInput,
            RoomIdentifierMode.Number) == RoomIdentifierMode.CustomParameter;
    }

    private void MarkChanged()
    {
        if (isUpdating)
        {
            return;
        }

        footerStatus.Text = "Конфигурация изменена. Нажмите «Применить», чтобы вернуть её в настройки расчёта.";
        footerStatus.Foreground = TrueBimBrushes.Warning;
    }

    private void ApplyAndClose()
    {
        UpdatedSettings = ReadSettings();
        DialogResult = true;
    }

    private void ImportConfiguration()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Импорт конфигурации ведомости отделки",
            Filter = "TrueBIM Finish Schedule (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            FinishScheduleSettings imported = configurationStorage.Import(dialog.FileName, ReadSettings());
            ApplySettings(preferredParameterResolver.Resolve(imported, catalog, categories));
            footerStatus.Text = $"Конфигурация импортирована: {dialog.FileName}";
            footerStatus.Foreground = TrueBimBrushes.Success;
            logger.Info($"Finish Schedule configuration imported from '{dialog.FileName}'.");
        }
        catch (Exception exception)
        {
            logger.Error($"Failed to import Finish Schedule configuration from '{dialog.FileName}'.", exception);
            footerStatus.Text = "Не удалось импортировать конфигурацию. Проверьте формат файла и лог TrueBIM.";
            footerStatus.Foreground = TrueBimBrushes.Danger;
        }
    }

    private void ExportConfiguration()
    {
        SaveFileDialog dialog = new()
        {
            Title = "Экспорт конфигурации ведомости отделки",
            Filter = "TrueBIM Finish Schedule (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = "finish-schedule-config.json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            configurationStorage.Export(dialog.FileName, ReadSettings());
            footerStatus.Text = $"Конфигурация экспортирована: {dialog.FileName}";
            footerStatus.Foreground = TrueBimBrushes.Success;
            logger.Info($"Finish Schedule configuration exported to '{dialog.FileName}'.");
        }
        catch (Exception exception)
        {
            logger.Error($"Failed to export Finish Schedule configuration to '{dialog.FileName}'.", exception);
            footerStatus.Text = "Не удалось экспортировать конфигурацию. Проверьте доступ к папке и лог TrueBIM.";
            footerStatus.Foreground = TrueBimBrushes.Danger;
        }
    }

    private static void AddCard(Panel panel, string title, UIElement body, bool isLast = false)
    {
        Border card = TrueBimUi.CreateSectionCard(title, body);
        if (!isLast)
        {
            card.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        }

        panel.Children.Add(card);
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

    private static UIElement CreateFieldRow(string label, Control input, bool isLast = false)
    {
        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Margin = isLast ? new Thickness(0) : new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
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
        Grid grid = CreateOutputGrid();
        grid.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing4);
        AddOutputLabel(grid, "Категория", 0);
        AddOutputLabel(grid, "Описание", 1);
        AddOutputLabel(grid, "Площадь", 2);
        return grid;
    }

    private static UIElement CreateOutputRow(
        string category,
        ComboBox description,
        ComboBox area,
        bool isLast = false)
    {
        Grid grid = CreateOutputGrid();
        grid.Margin = isLast ? new Thickness(0) : new Thickness(0, 0, 0, TrueBimTheme.Spacing8);
        AddOutputLabel(grid, category, 0);
        description.Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, 0);
        Grid.SetColumn(description, 1);
        grid.Children.Add(description);
        Grid.SetColumn(area, 2);
        grid.Children.Add(area);
        return grid;
    }

    private static Grid CreateOutputGrid()
    {
        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private static void AddOutputLabel(Grid grid, string text, int column)
    {
        TextBlock block = new()
        {
            Text = text,
            Foreground = TrueBimBrushes.TextSecondary,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, 0)
        };
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private static ComboBox CreateParameterInput()
    {
        return new ComboBox
        {
            MinWidth = 280,
            Height = 34,
            Style = TrueBimStyles.CreateComboBoxStyle(),
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

    private static ComboBox CreateClassificationInput(string defaultValue)
    {
        return new ComboBox
        {
            ItemsSource = new[] { defaultValue },
            Text = defaultValue,
            IsEditable = true,
            IsTextSearchEnabled = true,
            MinWidth = 280,
            Height = 34,
            Style = TrueBimStyles.CreateComboBoxStyle()
        };
    }

    private static void SetOptions(ComboBox input, IReadOnlyList<FinishScheduleParameterOption> options)
    {
        input.ItemsSource = options;
        input.DisplayMemberPath = nameof(FinishScheduleParameterOption.DisplayName);
    }

    private static void SetSelectedParameter(ComboBox input, ParameterReference? reference)
    {
        input.SelectedItem = reference is null
            ? null
            : input.Items.OfType<FinishScheduleParameterOption>()
                .FirstOrDefault(option => option.Reference.StableKey == reference.StableKey);
    }

    private static ParameterReference? GetSelectedParameter(ComboBox input)
    {
        return (input.SelectedItem as FinishScheduleParameterOption)?.Reference;
    }

    private static void SelectChoice<TEnum>(ComboBox input, TEnum value)
        where TEnum : struct, Enum
    {
        input.SelectedItem = input.Items.OfType<Choice<TEnum>>()
            .FirstOrDefault(choice => EqualityComparer<TEnum>.Default.Equals(choice.Value, value));
    }

    private static TEnum ReadChoice<TEnum>(ComboBox input, TEnum fallback)
        where TEnum : struct, Enum
    {
        return input.SelectedItem is Choice<TEnum> choice ? choice.Value : fallback;
    }

    private sealed class CategoryControls
    {
        public CategoryControls(string displayName, string defaultClassification)
        {
            ClassificationInput = CreateClassificationInput(defaultClassification);
            OutputDescriptionInput = CreateParameterInput();
            OutputDescriptionInput.ToolTip = $"Агрегированное описание для категории «{displayName}».";
            OutputAreaInput = CreateParameterInput();
            OutputAreaInput.ToolTip = $"Агрегированная площадь для категории «{displayName}».";
        }

        public ComboBox ClassificationInput { get; }

        public ComboBox OutputDescriptionInput { get; }

        public ComboBox OutputAreaInput { get; }
    }

    private sealed record Choice<TEnum>(TEnum Value, string DisplayName)
        where TEnum : struct, Enum;
}
