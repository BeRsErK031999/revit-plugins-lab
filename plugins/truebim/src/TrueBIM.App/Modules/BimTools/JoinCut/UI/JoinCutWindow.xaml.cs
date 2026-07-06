using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using TrueBIM.App.Modules.BimTools.JoinCut.Models;
using TrueBIM.App.Modules.BimTools.JoinCut.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace TrueBIM.App.Modules.BimTools.JoinCut.UI;

public sealed partial class JoinCutWindow : Window
{
    private readonly UIDocument uiDocument;
    private readonly Document document;
    private readonly JoinCutConfigurationStorage storage;
    private readonly ITrueBimLogger logger;
    private readonly JoinCutConfigurationState state;
    private readonly IReadOnlyList<ScopeOption> scopeOptions =
    [
        new ScopeOption(ProcessingScope.SelectedElements, "Выбранные элементы"),
        new ScopeOption(ProcessingScope.ActiveView, "Активный вид"),
        new ScopeOption(ProcessingScope.EntireProject, "Весь проект")
    ];
    private readonly IReadOnlyList<ActionOption> joinActionOptions =
    [
        ActionOption.ForJoin(JoinAction.Join, "Соединить"),
        ActionOption.ForJoin(JoinAction.Unjoin, "Отсоединить"),
        ActionOption.ForJoin(JoinAction.SwitchJoinOrder, "Инвертировать порядок")
    ];
    private readonly IReadOnlyList<ActionOption> cutActionOptions =
    [
        ActionOption.ForCut(CutAction.Cut, "Вырезать"),
        ActionOption.ForCut(CutAction.Uncut, "Отменить вырезание")
    ];
    private bool refreshing;

    public JoinCutWindow(
        UIDocument uiDocument,
        JoinCutConfigurationLoadResult loadResult,
        JoinCutConfigurationStorage storage,
        ITrueBimLogger logger)
    {
        this.uiDocument = uiDocument ?? throw new ArgumentNullException(nameof(uiDocument));
        document = uiDocument.Document ?? throw new ArgumentException("UIDocument must contain a Revit document.", nameof(uiDocument));
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        state = loadResult?.State ?? throw new ArgumentNullException(nameof(loadResult));

        InitializeComponent();

        Icon = IconFactory.CreateImage(TrueBimIcon.JoinCut, 32);
        DocumentText.Text = $"Активный документ: {document.Title}.";
        ScopeCombo.ItemsSource = scopeOptions;
        SetButtonContents();
        RefreshConfigurations();

        StatusText.Text = string.IsNullOrWhiteSpace(loadResult.WarningMessage)
            ? "Готово. Настройки сохраняются автоматически."
            : loadResult.WarningMessage;
        logger.Info("Join/Cut window opened.");
    }

    private JoinCutConfiguration? SelectedConfiguration => ConfigurationCombo.SelectedItem as JoinCutConfiguration;

    private JoinRule? SelectedJoinRule => JoinRulesList.SelectedItem as JoinRule;

    private CutRule? SelectedCutRule => CutRulesList.SelectedItem as CutRule;

    private void SetButtonContents()
    {
        MoveJoinUpButton.Content = IconFactory.CreateButtonContent(TrueBimIcon.Up, "Вверх");
        MoveJoinDownButton.Content = IconFactory.CreateButtonContent(TrueBimIcon.Down, "Вниз");
        MoveCutUpButton.Content = IconFactory.CreateButtonContent(TrueBimIcon.Up, "Вверх");
        MoveCutDownButton.Content = IconFactory.CreateButtonContent(TrueBimIcon.Down, "Вниз");
        PreviewButton.Content = IconFactory.CreateButtonContent(TrueBimIcon.Preview, "Предпросмотр");
        ExecuteButton.Content = IconFactory.CreateButtonContent(TrueBimIcon.Apply, "Выполнить");
        CloseButton.Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Закрыть");
    }

    private void RefreshConfigurations()
    {
        refreshing = true;
        ConfigurationCombo.ItemsSource = null;
        ConfigurationCombo.ItemsSource = state.Configurations;

        JoinCutConfiguration? selected = state.Configurations.FirstOrDefault(configuration =>
            string.Equals(configuration.Id, state.SelectedConfigurationId, StringComparison.Ordinal));
        ConfigurationCombo.SelectedItem = selected ?? state.Configurations.FirstOrDefault();

        refreshing = false;
        RefreshRuleLists();
        RefreshFooterSelections();
    }

    private void RefreshRuleLists()
    {
        refreshing = true;

        JoinCutConfiguration? configuration = SelectedConfiguration;
        JoinRulesList.ItemsSource = null;
        CutRulesList.ItemsSource = null;

        if (configuration is not null)
        {
            JoinRulesList.ItemsSource = configuration.JoinRules;
            CutRulesList.ItemsSource = configuration.CutRules;
            if (JoinRulesList.SelectedItem is null && configuration.JoinRules.Count > 0)
            {
                JoinRulesList.SelectedIndex = 0;
            }

            if (CutRulesList.SelectedItem is null && configuration.CutRules.Count > 0)
            {
                CutRulesList.SelectedIndex = 0;
            }
        }

        refreshing = false;
        UpdateJoinRuleEditor();
        UpdateCutRuleEditor();
    }

    private void RefreshFooterSelections()
    {
        refreshing = true;

        JoinCutConfiguration? configuration = SelectedConfiguration;
        if (configuration is not null)
        {
            ScopeCombo.SelectedItem = scopeOptions.First(option => option.Value == configuration.LastSelectedScope);
            MainTabs.SelectedIndex = configuration.LastSelectedTab == JoinCutTab.Cut ? 1 : 0;
        }

        refreshing = false;
        RefreshActionCombo();
    }

    private void RefreshActionCombo()
    {
        refreshing = true;
        JoinCutConfiguration? configuration = SelectedConfiguration;
        bool cutTabSelected = MainTabs.SelectedIndex == 1;

        ActionCombo.IsEnabled = MainTabs.SelectedIndex != 2;
        ActionCombo.ItemsSource = cutTabSelected ? cutActionOptions : joinActionOptions;
        if (configuration is not null)
        {
            ActionCombo.SelectedItem = cutTabSelected
                ? cutActionOptions.First(option => option.CutAction == configuration.LastSelectedCutAction)
                : joinActionOptions.First(option => option.JoinAction == configuration.LastSelectedJoinAction);
        }

        refreshing = false;
    }

    private void ConfigurationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing)
        {
            return;
        }

        if (SelectedConfiguration is not null)
        {
            state.SelectedConfigurationId = SelectedConfiguration.Id;
            SaveState("Выбрана конфигурация.");
        }

        RefreshRuleLists();
        RefreshFooterSelections();
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || e.Source != MainTabs)
        {
            return;
        }

        JoinCutConfiguration? configuration = SelectedConfiguration;
        if (configuration is not null)
        {
            if (MainTabs.SelectedIndex == 0)
            {
                configuration.LastSelectedTab = JoinCutTab.Join;
            }
            else if (MainTabs.SelectedIndex == 1)
            {
                configuration.LastSelectedTab = JoinCutTab.Cut;
            }

            SaveState("Выбрана вкладка.");
        }

        RefreshActionCombo();
    }

    private void ScopeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || SelectedConfiguration is null || ScopeCombo.SelectedItem is not ScopeOption option)
        {
            return;
        }

        SelectedConfiguration.LastSelectedScope = option.Value;
        SaveState("Область обработки сохранена.");
    }

    private void ActionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || SelectedConfiguration is null || ActionCombo.SelectedItem is not ActionOption option)
        {
            return;
        }

        if (option.JoinAction.HasValue)
        {
            SelectedConfiguration.LastSelectedJoinAction = option.JoinAction.Value;
        }

        if (option.CutAction.HasValue)
        {
            SelectedConfiguration.LastSelectedCutAction = option.CutAction.Value;
        }

        SaveState("Действие сохранено.");
    }

    private void AddConfiguration_Click(object sender, RoutedEventArgs e)
    {
        JoinCutConfiguration configuration = storage.CreateConfiguration(CreateUniqueConfigurationName("Конфигурация"));
        state.Configurations.Add(configuration);
        state.SelectedConfigurationId = configuration.Id;
        SaveState("Конфигурация добавлена.");
        RefreshConfigurations();
    }

    private void DuplicateConfiguration_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedConfiguration is null)
        {
            return;
        }

        JoinCutConfiguration duplicate = CloneConfiguration(
            SelectedConfiguration,
            CreateUniqueConfigurationName($"{SelectedConfiguration.Name} копия"));
        state.Configurations.Add(duplicate);
        state.SelectedConfigurationId = duplicate.Id;
        SaveState("Конфигурация продублирована.");
        RefreshConfigurations();
    }

    private void RenameConfiguration_Click(object sender, RoutedEventArgs e)
    {
        JoinCutConfiguration? configuration = SelectedConfiguration;
        if (configuration is null)
        {
            return;
        }

        string? name = PromptForText("Переименовать конфигурацию", "Новое название", configuration.Name);
        string trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return;
        }

        configuration.Name = trimmedName;
        SaveState("Конфигурация переименована.");
        RefreshConfigurations();
    }

    private void DeleteConfiguration_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedConfiguration is null)
        {
            return;
        }

        if (SelectedConfiguration.IsDefault)
        {
            MessageBox.Show(this, "Стандартную конфигурацию нельзя удалить.", "Соединить / Вырезать", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            this,
            $"Удалить конфигурацию \"{SelectedConfiguration.Name}\"?",
            "Соединить / Вырезать",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        state.Configurations.Remove(SelectedConfiguration);
        state.SelectedConfigurationId = state.Configurations.First(configuration => configuration.IsDefault).Id;
        SaveState("Конфигурация удалена.");
        RefreshConfigurations();
    }

    private void JoinRulesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshing)
        {
            UpdateJoinRuleEditor();
        }
    }

    private void CutRulesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshing)
        {
            UpdateCutRuleEditor();
        }
    }

    private void AddJoinRule_Click(object sender, RoutedEventArgs e)
    {
        JoinCutConfiguration? configuration = SelectedConfiguration;
        if (configuration is null)
        {
            return;
        }

        JoinRule rule = storage.CreateJoinRule(CreateUniqueRuleName(configuration.JoinRules.Select(item => item.Name), "Правило соединения"));
        configuration.JoinRules.Add(rule);
        SaveState("Правило соединения добавлено.");
        RefreshRuleLists();
        JoinRulesList.SelectedItem = rule;
    }

    private void DuplicateJoinRule_Click(object sender, RoutedEventArgs e)
    {
        JoinCutConfiguration? configuration = SelectedConfiguration;
        if (configuration is null || SelectedJoinRule is null)
        {
            return;
        }

        JoinRule duplicate = CloneJoinRule(SelectedJoinRule, CreateUniqueRuleName(configuration.JoinRules.Select(item => item.Name), $"{SelectedJoinRule.Name} копия"));
        configuration.JoinRules.Add(duplicate);
        SaveState("Правило соединения продублировано.");
        RefreshRuleLists();
        JoinRulesList.SelectedItem = duplicate;
    }

    private void MoveJoinRuleUp_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedRule(SelectedConfiguration?.JoinRules, JoinRulesList.SelectedIndex, -1);
    }

    private void MoveJoinRuleDown_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedRule(SelectedConfiguration?.JoinRules, JoinRulesList.SelectedIndex, 1);
    }

    private void DeleteJoinRule_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedRule(SelectedConfiguration?.JoinRules, SelectedJoinRule, "правило соединения");
    }

    private void SwapJoinFilters_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedJoinRule is null)
        {
            return;
        }

        (SelectedJoinRule.LeftFilter, SelectedJoinRule.RightFilter) = (SelectedJoinRule.RightFilter, SelectedJoinRule.LeftFilter);
        SaveState("Фильтры правила соединения поменяны местами.");
        UpdateJoinRuleEditor();
    }

    private void AddCutRule_Click(object sender, RoutedEventArgs e)
    {
        JoinCutConfiguration? configuration = SelectedConfiguration;
        if (configuration is null)
        {
            return;
        }

        CutRule rule = storage.CreateCutRule(CreateUniqueRuleName(configuration.CutRules.Select(item => item.Name), "Правило вырезания"));
        configuration.CutRules.Add(rule);
        SaveState("Правило вырезания добавлено.");
        RefreshRuleLists();
        CutRulesList.SelectedItem = rule;
    }

    private void DuplicateCutRule_Click(object sender, RoutedEventArgs e)
    {
        JoinCutConfiguration? configuration = SelectedConfiguration;
        if (configuration is null || SelectedCutRule is null)
        {
            return;
        }

        CutRule duplicate = CloneCutRule(SelectedCutRule, CreateUniqueRuleName(configuration.CutRules.Select(item => item.Name), $"{SelectedCutRule.Name} копия"));
        configuration.CutRules.Add(duplicate);
        SaveState("Правило вырезания продублировано.");
        RefreshRuleLists();
        CutRulesList.SelectedItem = duplicate;
    }

    private void MoveCutRuleUp_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedRule(SelectedConfiguration?.CutRules, CutRulesList.SelectedIndex, -1);
    }

    private void MoveCutRuleDown_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedRule(SelectedConfiguration?.CutRules, CutRulesList.SelectedIndex, 1);
    }

    private void DeleteCutRule_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedRule(SelectedConfiguration?.CutRules, SelectedCutRule, "правило вырезания");
    }

    private void SwapCutFilters_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCutRule is null)
        {
            return;
        }

        (SelectedCutRule.CuttingElementsFilter, SelectedCutRule.CutElementsFilter) = (SelectedCutRule.CutElementsFilter, SelectedCutRule.CuttingElementsFilter);
        SaveState("Фильтры правила вырезания поменяны местами.");
        UpdateCutRuleEditor();
    }

    private void JoinRuleNameInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (SelectedJoinRule is null)
        {
            return;
        }

        string name = JoinRuleNameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.Equals(SelectedJoinRule.Name, name, StringComparison.Ordinal))
        {
            return;
        }

        SelectedJoinRule.Name = name;
        SaveState("Название правила соединения сохранено.");
        RefreshRuleLists();
    }

    private void CutRuleNameInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (SelectedCutRule is null)
        {
            return;
        }

        string name = CutRuleNameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.Equals(SelectedCutRule.Name, name, StringComparison.Ordinal))
        {
            return;
        }

        SelectedCutRule.Name = name;
        SaveState("Название правила вырезания сохранено.");
        RefreshRuleLists();
    }

    private void OnlyParallelWallsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (refreshing || SelectedJoinRule is null)
        {
            return;
        }

        SelectedJoinRule.OnlyParallelWalls = OnlyParallelWallsCheck.IsChecked == true;
        SaveState("Настройка параллельных стен сохранена.");
    }

    private void SplitFacesCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (refreshing || SelectedCutRule is null)
        {
            return;
        }

        SelectedCutRule.SplitFacesOfCuttingSolid = SplitFacesCheck.IsChecked == true;
        SaveState("Настройка разделения граней сохранена.");
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        string report = BuildStubReport("Предпросмотр");
        ReportText.Text = report;
        StatusText.Text = "Предпросмотр записан в отчёт. Модель Revit не изменялась.";
        logger.Info("Join/Cut preview placeholder invoked.");
    }

    private void Execute_Click(object sender, RoutedEventArgs e)
    {
        string report = BuildStubReport("Выполнение");
        ReportText.Text = report;
        StatusText.Text = "Отчёт обновлён. Модель Revit не изменялась.";
        logger.Info("Join/Cut execute placeholder invoked.");
    }

    private void CopyReport_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ReportText.Text))
        {
            return;
        }

        Clipboard.SetText(ReportText.Text);
        StatusText.Text = "Отчёт скопирован в буфер обмена.";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MoveSelectedRule<T>(List<T>? rules, int selectedIndex, int direction)
    {
        if (rules is null || selectedIndex < 0)
        {
            return;
        }

        int targetIndex = selectedIndex + direction;
        if (targetIndex < 0 || targetIndex >= rules.Count)
        {
            return;
        }

        T item = rules[selectedIndex];
        rules.RemoveAt(selectedIndex);
        rules.Insert(targetIndex, item);
        SaveState("Порядок правил сохранён.");
        RefreshRuleLists();

        if (typeof(T) == typeof(JoinRule))
        {
            JoinRulesList.SelectedIndex = targetIndex;
        }
        else
        {
            CutRulesList.SelectedIndex = targetIndex;
        }
    }

    private void DeleteSelectedRule<T>(List<T>? rules, T? selectedRule, string ruleKind)
        where T : class
    {
        if (rules is null || selectedRule is null)
        {
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            this,
            $"Удалить {ruleKind}?",
            "Соединить / Вырезать",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        rules.Remove(selectedRule);
        SaveState("Правило удалено.");
        RefreshRuleLists();
    }

    private void UpdateJoinRuleEditor()
    {
        refreshing = true;
        JoinRule? rule = SelectedJoinRule;
        JoinRuleNameInput.Text = rule?.Name ?? string.Empty;
        JoinRuleNameInput.IsEnabled = rule is not null;
        OnlyParallelWallsCheck.IsChecked = rule?.OnlyParallelWalls == true;
        OnlyParallelWallsCheck.IsEnabled = rule is not null;
        JoinLeftFilterText.Text = rule is null ? "Правило не выбрано." : DescribeFilter(rule.LeftFilter);
        JoinRightFilterText.Text = rule is null ? "Правило не выбрано." : DescribeFilter(rule.RightFilter);
        refreshing = false;
    }

    private void UpdateCutRuleEditor()
    {
        refreshing = true;
        CutRule? rule = SelectedCutRule;
        CutRuleNameInput.Text = rule?.Name ?? string.Empty;
        CutRuleNameInput.IsEnabled = rule is not null;
        SplitFacesCheck.IsChecked = rule?.SplitFacesOfCuttingSolid == true;
        SplitFacesCheck.IsEnabled = rule is not null;
        CuttingFilterText.Text = rule is null ? "Правило не выбрано." : DescribeFilter(rule.CuttingElementsFilter);
        CutElementFilterText.Text = rule is null ? "Правило не выбрано." : DescribeFilter(rule.CutElementsFilter);
        refreshing = false;
    }

    private void SaveState(string statusMessage)
    {
        try
        {
            storage.Save(state);
            StatusText.Text = statusMessage;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Error("Failed to save Join/Cut configuration.", exception);
            StatusText.Text = "Не удалось сохранить конфигурации. Используйте логи для диагностики.";
        }
    }

    private string BuildStubReport(string action)
    {
        JoinCutConfiguration? configuration = SelectedConfiguration;
        string configurationName = configuration?.Name ?? "не выбрана";
        string scope = ScopeCombo.SelectedItem is ScopeOption scopeOption ? scopeOption.Text : "не выбрана";
        string selectedAction = ActionCombo.SelectedItem is ActionOption actionOption ? actionOption.Text : "не выбрано";

        return string.Join(
            Environment.NewLine,
            $"{action}: Соединить / Вырезать",
            $"Документ: {document.Title}",
            $"Конфигурация: {configurationName}",
            $"Область обработки: {scope}",
            $"Действие: {selectedAction}",
            $"Правил соединения: {configuration?.JoinRules.Count ?? 0}",
            $"Правил вырезания: {configuration?.CutRules.Count ?? 0}",
            string.Empty,
            "Модель Revit не изменялась.");
    }

    private static string DescribeFilter(ElementFilterDefinition filter)
    {
        string categories = filter.Categories.Count == 0
            ? "любые категории"
            : $"{filter.Categories.Count} категорий";
        string parameters = filter.ParameterConditions.Count == 0
            ? "без условий по параметрам"
            : $"{filter.ParameterConditions.Count} условий по параметрам";
        string logicalOperator = filter.CategoryAndParameterOperator == FilterLogicalOperator.And ? "AND" : "OR";

        return $"{categories}; {parameters}; связка {logicalOperator}.";
    }

    private string CreateUniqueConfigurationName(string baseName)
    {
        return CreateUniqueRuleName(state.Configurations.Select(configuration => configuration.Name), baseName);
    }

    private static string CreateUniqueRuleName(IEnumerable<string> existingNames, string baseName)
    {
        HashSet<string> existing = new(existingNames, StringComparer.CurrentCultureIgnoreCase);
        if (!existing.Contains(baseName))
        {
            return baseName;
        }

        for (int index = 2; index < 1000; index++)
        {
            string candidate = $"{baseName} {index}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{baseName} {Guid.NewGuid():N}";
    }

    private static JoinCutConfiguration CloneConfiguration(JoinCutConfiguration source, string name)
    {
        return new JoinCutConfiguration
        {
            Name = name,
            IsDefault = false,
            JoinRules = source.JoinRules.Select(rule => CloneJoinRule(rule, rule.Name)).ToList(),
            CutRules = source.CutRules.Select(rule => CloneCutRule(rule, rule.Name)).ToList(),
            LastSelectedTab = source.LastSelectedTab,
            LastSelectedScope = source.LastSelectedScope,
            LastSelectedJoinAction = source.LastSelectedJoinAction,
            LastSelectedCutAction = source.LastSelectedCutAction
        };
    }

    private static JoinRule CloneJoinRule(JoinRule source, string name)
    {
        return new JoinRule
        {
            Name = name,
            LeftFilter = CloneFilter(source.LeftFilter),
            RightFilter = CloneFilter(source.RightFilter),
            OnlyParallelWalls = source.OnlyParallelWalls,
            Enabled = source.Enabled
        };
    }

    private static CutRule CloneCutRule(CutRule source, string name)
    {
        return new CutRule
        {
            Name = name,
            CuttingElementsFilter = CloneFilter(source.CuttingElementsFilter),
            CutElementsFilter = CloneFilter(source.CutElementsFilter),
            SplitFacesOfCuttingSolid = source.SplitFacesOfCuttingSolid,
            Enabled = source.Enabled
        };
    }

    private static ElementFilterDefinition CloneFilter(ElementFilterDefinition source)
    {
        return new ElementFilterDefinition
        {
            Categories = source.Categories.ToList(),
            ParameterConditions = source.ParameterConditions
                .Select(condition => new ParameterFilterCondition
                {
                    ParameterName = condition.ParameterName,
                    ParameterGuid = condition.ParameterGuid,
                    BuiltInParameter = condition.BuiltInParameter,
                    Operator = condition.Operator,
                    Value = condition.Value
                })
                .ToList(),
            CategoryAndParameterOperator = source.CategoryAndParameterOperator
        };
    }

    private string? PromptForText(string title, string label, string value)
    {
        Window dialog = new()
        {
            Title = title,
            Owner = this,
            Width = 420,
            Height = 160,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        WpfGrid root = new()
        {
            Margin = new Thickness(14)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        WpfTextBox input = new()
        {
            Text = value,
            Height = 28
        };
        WpfGrid.SetRow(input, 1);
        root.Children.Add(input);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        Button okButton = new()
        {
            Content = "OK",
            MinWidth = 80,
            Height = 30,
            IsDefault = true
        };
        okButton.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(okButton);

        Button cancelButton = new()
        {
            Content = "Отмена",
            MinWidth = 90,
            Height = 30,
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true
        };
        buttons.Children.Add(cancelButton);
        WpfGrid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        dialog.Content = root;
        input.SelectAll();
        input.Focus();

        return dialog.ShowDialog() == true ? input.Text : null;
    }

    private sealed record ScopeOption(ProcessingScope Value, string Text);

    private sealed record ActionOption(string Text, JoinAction? JoinAction, CutAction? CutAction)
    {
        public static ActionOption ForJoin(JoinAction action, string text)
        {
            return new ActionOption(text, action, null);
        }

        public static ActionOption ForCut(CutAction action, string text)
        {
            return new ActionOption(text, null, action);
        }
    }
}
