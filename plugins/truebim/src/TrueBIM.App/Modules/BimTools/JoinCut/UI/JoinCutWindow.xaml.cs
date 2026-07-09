using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TrueBIM.App.Modules.BimTools.JoinCut.Models;
using TrueBIM.App.Modules.BimTools.JoinCut.Services;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using WpfBinding = System.Windows.Data.Binding;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace TrueBIM.App.Modules.BimTools.JoinCut.UI;

public sealed partial class JoinCutWindow : TrueBimWindow
{
    private readonly UIDocument uiDocument;
    private readonly Document document;
    private readonly JoinCutConfigurationStorage storage;
    private readonly ITrueBimLogger logger;
    private readonly JoinCutConfigurationState state;
    private readonly JoinCutProcessingService processingService = new();
    private readonly JoinCutExternalEventHandler operationHandler;
    private readonly ExternalEvent operationEvent;
    private IReadOnlyList<CategoryCatalogItem>? categoryCatalog;
    private IReadOnlyDictionary<BuiltInCategory, string> categoryNames = new Dictionary<BuiltInCategory, string>();
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
        operationHandler = new JoinCutExternalEventHandler(this);
        operationEvent = ExternalEvent.Create(operationHandler);

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

    private void EditJoinLeftCategories_Click(object sender, RoutedEventArgs e)
    {
        EditFilterCategories(SelectedJoinRule?.LeftFilter, "Категории: что присоединять", UpdateJoinRuleEditor);
    }

    private void EditJoinRightCategories_Click(object sender, RoutedEventArgs e)
    {
        EditFilterCategories(SelectedJoinRule?.RightFilter, "Категории: к чему присоединять", UpdateJoinRuleEditor);
    }

    private void EditCuttingCategories_Click(object sender, RoutedEventArgs e)
    {
        EditFilterCategories(SelectedCutRule?.CuttingElementsFilter, "Категории: что вырезает", UpdateCutRuleEditor);
    }

    private void EditCutElementCategories_Click(object sender, RoutedEventArgs e)
    {
        EditFilterCategories(SelectedCutRule?.CutElementsFilter, "Категории: из чего вырезает", UpdateCutRuleEditor);
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        JoinCutConfiguration? configuration = SelectedConfiguration;
        if (configuration is null)
        {
            return;
        }

        QueuePreviewOperation(configuration);
    }

    private void Execute_Click(object sender, RoutedEventArgs e)
    {
        JoinCutConfiguration? configuration = SelectedConfiguration;
        if (configuration is null)
        {
            return;
        }

        QueueExecuteOperation(configuration);
    }

    private void QueuePreviewOperation(JoinCutConfiguration configuration)
    {
        JoinCutOperationRequest request = MainTabs.SelectedIndex == 1
            ? JoinCutOperationRequest.ForCut(
                isPreview: true,
                configuration,
                GetSelectedScope(),
                GetSelectedCutAction())
            : JoinCutOperationRequest.ForJoin(
                isPreview: true,
                configuration,
                GetSelectedScope(),
                GetSelectedJoinAction());
        RaiseOperation(request);
    }

    private void QueueExecuteOperation(JoinCutConfiguration configuration)
    {
        JoinCutOperationRequest request = MainTabs.SelectedIndex == 1
            ? JoinCutOperationRequest.ForCut(
                isPreview: false,
                configuration,
                GetSelectedScope(),
                GetSelectedCutAction())
            : JoinCutOperationRequest.ForJoin(
                isPreview: false,
                configuration,
                GetSelectedScope(),
                GetSelectedJoinAction());
        RaiseOperation(request);
    }

    private void RaiseOperation(JoinCutOperationRequest request)
    {
        operationHandler.SetRequest(request);
        SetOperationButtonsEnabled(false);
        string mode = request.IsPreview ? "Предпросмотр" : "Выполнение";
        StatusText.Text = $"{mode} запущен. Revit обработает запрос через ExternalEvent.";
        logger.Info($"Join/Cut {request.OperationName} {mode.ToLowerInvariant()} requested.");
        operationEvent.Raise();
    }

    private void RunOperation(JoinCutOperationRequest request)
    {
        try
        {
            string action = request.IsPreview ? "Предпросмотр" : "Выполнение";
            JoinCutProcessingResult result;
            if (request.JoinAction.HasValue)
            {
                result = request.IsPreview
                    ? processingService.PreviewJoin(uiDocument, request.Configuration, request.Scope, request.JoinAction.Value)
                    : processingService.ExecuteJoin(uiDocument, request.Configuration, request.Scope, request.JoinAction.Value);
                ReportText.Text = BuildJoinProcessingReport(action, request.Configuration, result);
                StatusText.Text = BuildJoinStatus(action, result);
            }
            else
            {
                result = request.IsPreview
                    ? processingService.PreviewCut(uiDocument, request.Configuration, request.Scope, request.CutAction!.Value)
                    : processingService.ExecuteCut(uiDocument, request.Configuration, request.Scope, request.CutAction!.Value);
                ReportText.Text = BuildCutProcessingReport(action, request.Configuration, result);
                StatusText.Text = BuildCutStatus(action, result);
            }

            logger.Info($"Join/Cut {request.OperationName} {action.ToLowerInvariant()} completed with {result.Rows.Count} row(s), changedModel={result.ChangedModel}.");
        }
        catch (Exception exception)
        {
            logger.Error($"Join/Cut {request.OperationName} failed.", exception);
            StatusText.Text = "Не удалось выполнить операцию Join/Cut. Используйте логи для диагностики.";
        }
        finally
        {
            SetOperationButtonsEnabled(true);
        }
    }

    private void SetOperationButtonsEnabled(bool isEnabled)
    {
        PreviewButton.IsEnabled = isEnabled;
        ExecuteButton.IsEnabled = isEnabled;
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
        EditJoinLeftCategoriesButton.IsEnabled = rule is not null;
        EditJoinRightCategoriesButton.IsEnabled = rule is not null;
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
        EditCuttingCategoriesButton.IsEnabled = rule is not null;
        EditCutElementCategoriesButton.IsEnabled = rule is not null;
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

    private string BuildJoinProcessingReport(
        string action,
        JoinCutConfiguration configuration,
        JoinCutProcessingResult result)
    {
        List<string> lines =
        [
            $"{action}: Соединить элементы",
            $"Документ: {document.Title}",
            $"Конфигурация: {configuration.Name}",
            $"Область обработки: {GetScopeText(GetSelectedScope())}",
            $"Действие: {GetJoinActionText(GetSelectedJoinAction())}",
            string.Empty,
            "Сводка:"
        ];

        lines.AddRange(result.Messages.Select(message => $"- {message}"));
        lines.Add(string.Empty);
        lines.Add($"Пары элементов: {result.Rows.Count}");

        foreach (JoinCutOperationRow row in result.Rows.Take(300))
        {
            lines.Add(
                $"{row.Status}: {row.RuleName}: {row.LeftCategoryName} #{row.LeftElementId} \"{row.LeftElementName}\" -> {row.RightCategoryName} #{row.RightElementId} \"{row.RightElementName}\". {row.Message}");
        }

        if (result.Rows.Count > 300)
        {
            lines.Add($"Показаны первые 300 строк из {result.Rows.Count}.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildJoinStatus(string action, JoinCutProcessingResult result)
    {
        int ready = result.Rows.Count(row => row.Status == JoinCutOperationStatuses.Ready);
        int done = result.Rows.Count(row => row.Status == JoinCutOperationStatuses.Done);
        int skipped = result.Rows.Count(row => row.Status == JoinCutOperationStatuses.Skipped);
        int failed = result.Rows.Count(row => row.Status == JoinCutOperationStatuses.Failed);
        string truncated = result.Truncated ? " Обработка остановлена по лимиту пар." : string.Empty;
        string modelState = result.ChangedModel ? " Модель Revit изменена." : " Модель Revit не изменялась.";

        return $"{action}: пар {result.Rows.Count}, готово {ready}, выполнено {done}, пропущено {skipped}, ошибок {failed}.{truncated}{modelState}";
    }

    private string BuildCutProcessingReport(
        string action,
        JoinCutConfiguration configuration,
        JoinCutProcessingResult result)
    {
        List<string> lines =
        [
            $"{action}: Вырезать элементы",
            $"Документ: {document.Title}",
            $"Конфигурация: {configuration.Name}",
            $"Область обработки: {GetScopeText(GetSelectedScope())}",
            $"Действие: {GetCutActionText(GetSelectedCutAction())}",
            string.Empty,
            "Сводка:"
        ];

        lines.AddRange(result.Messages.Select(message => $"- {message}"));
        lines.Add(string.Empty);
        lines.Add($"Пары элементов: {result.Rows.Count}");

        foreach (JoinCutOperationRow row in result.Rows.Take(300))
        {
            lines.Add(
                $"{row.Status}: {row.RuleName}: {row.LeftCategoryName} #{row.LeftElementId} \"{row.LeftElementName}\" cuts {row.RightCategoryName} #{row.RightElementId} \"{row.RightElementName}\". {row.Message}");
        }

        if (result.Rows.Count > 300)
        {
            lines.Add($"Показаны первые 300 строк из {result.Rows.Count}.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildCutStatus(string action, JoinCutProcessingResult result)
    {
        int ready = result.Rows.Count(row => row.Status == JoinCutOperationStatuses.Ready);
        int done = result.Rows.Count(row => row.Status == JoinCutOperationStatuses.Done);
        int skipped = result.Rows.Count(row => row.Status == JoinCutOperationStatuses.Skipped);
        int failed = result.Rows.Count(row => row.Status == JoinCutOperationStatuses.Failed);
        string truncated = result.Truncated ? " Обработка остановлена по лимиту пар." : string.Empty;
        string modelState = result.ChangedModel ? " Модель Revit изменена." : " Модель Revit не изменялась.";

        return $"{action}: пар {result.Rows.Count}, готово {ready}, выполнено {done}, пропущено {skipped}, ошибок {failed}.{truncated}{modelState}";
    }

    private ProcessingScope GetSelectedScope()
    {
        return ScopeCombo.SelectedItem is ScopeOption scopeOption
            ? scopeOption.Value
            : SelectedConfiguration?.LastSelectedScope ?? ProcessingScope.SelectedElements;
    }

    private JoinAction GetSelectedJoinAction()
    {
        return ActionCombo.SelectedItem is ActionOption actionOption && actionOption.JoinAction.HasValue
            ? actionOption.JoinAction.Value
            : SelectedConfiguration?.LastSelectedJoinAction ?? JoinAction.Join;
    }

    private CutAction GetSelectedCutAction()
    {
        return ActionCombo.SelectedItem is ActionOption actionOption && actionOption.CutAction.HasValue
            ? actionOption.CutAction.Value
            : SelectedConfiguration?.LastSelectedCutAction ?? CutAction.Cut;
    }

    private string GetScopeText(ProcessingScope scope)
    {
        return scopeOptions.FirstOrDefault(option => option.Value == scope)?.Text ?? scope.ToString();
    }

    private string GetJoinActionText(JoinAction action)
    {
        return joinActionOptions.FirstOrDefault(option => option.JoinAction == action)?.Text ?? action.ToString();
    }

    private string GetCutActionText(CutAction action)
    {
        return cutActionOptions.FirstOrDefault(option => option.CutAction == action)?.Text ?? action.ToString();
    }

    private void EditFilterCategories(
        ElementFilterDefinition? filter,
        string title,
        Action refreshEditor)
    {
        if (filter is null)
        {
            return;
        }

        IReadOnlyList<BuiltInCategory>? selectedCategories = ShowCategorySelectionDialog(title, filter.Categories);
        if (selectedCategories is null)
        {
            return;
        }

        filter.Categories = selectedCategories
            .Distinct()
            .OrderBy(GetCategoryDisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        SaveState("Категории фильтра сохранены.");
        refreshEditor();
    }

    private IReadOnlyList<BuiltInCategory>? ShowCategorySelectionDialog(
        string title,
        IReadOnlyCollection<BuiltInCategory> selectedCategories)
    {
        IReadOnlyList<CategoryCatalogItem> categories = EnsureCategoryCatalog();
        if (categories.Count == 0)
        {
            MessageBox.Show(this, "В проекте не найдены модельные категории с элементами.", "Категории", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        bool allCategoriesSelected = selectedCategories.Count == 0;
        ObservableCollection<CategorySelectionOption> options = new(categories
            .Select(category => new CategorySelectionOption(
                category.BuiltInCategory,
                category.Name,
                category.ElementCount,
                allCategoriesSelected || selectedCategories.Contains(category.BuiltInCategory))));
        ICollectionView categoryView = CollectionViewSource.GetDefaultView(options);
        IReadOnlyList<BuiltInCategory>? result = null;

        TrueBimWindow dialog = new()
        {
            Title = title,
            Owner = this,
            Width = 560,
            Height = 620,
            MinWidth = 460,
            MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        WpfGrid root = new()
        {
            Margin = new Thickness(14)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        StackPanel headerPanel = new()
        {
            Margin = new Thickness(0, 0, 0, 10)
        };

        StackPanel toolbar = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Button selectAllButton = new()
        {
            Content = "Выбрать все",
            MinWidth = 104,
            Height = 30
        };
        selectAllButton.Click += (_, _) => SetCategoryOptionsSelected(GetVisibleCategoryOptions(categoryView), true);
        toolbar.Children.Add(selectAllButton);

        Button clearButton = new()
        {
            Content = "Снять все",
            MinWidth = 96,
            Height = 30,
            Margin = new Thickness(8, 0, 0, 0)
        };
        clearButton.Click += (_, _) => SetCategoryOptionsSelected(GetVisibleCategoryOptions(categoryView), false);
        toolbar.Children.Add(clearButton);
        headerPanel.Children.Add(toolbar);

        WpfTextBox searchInput = new()
        {
            Height = 28,
            ToolTip = "Поиск категории по имени."
        };
        searchInput.TextChanged += (_, _) =>
        {
            string query = searchInput.Text.Trim();
            categoryView.Filter = item => CategoryMatchesSearch(item as CategorySelectionOption, query);
            categoryView.Refresh();
        };
        headerPanel.Children.Add(searchInput);
        root.Children.Add(headerPanel);

        DataGrid categoryGrid = new()
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            IsReadOnly = false,
            ItemsSource = categoryView,
            SelectionMode = DataGridSelectionMode.Extended
        };
        categoryGrid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Вкл.",
            Binding = new WpfBinding(nameof(CategorySelectionOption.IsSelected))
            {
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            },
            Width = 54
        });
        categoryGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Категория",
            Binding = new WpfBinding(nameof(CategorySelectionOption.Name)),
            IsReadOnly = true,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        categoryGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Элементов",
            Binding = new WpfBinding(nameof(CategorySelectionOption.ElementCount)),
            IsReadOnly = true,
            Width = 100
        });
        WpfGrid.SetRow(categoryGrid, 1);
        root.Children.Add(categoryGrid);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Button okButton = new()
        {
            Content = "OK",
            MinWidth = 82,
            Height = 30,
            IsDefault = true
        };
        okButton.Click += (_, _) =>
        {
            List<BuiltInCategory> selected = options
                .Where(option => option.IsSelected)
                .Select(option => option.BuiltInCategory)
                .ToList();
            result = selected.Count == options.Count ? [] : selected;
            dialog.DialogResult = true;
        };
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
        dialog.ShowDialog();
        return result;
    }

    private static void SetCategoryOptionsSelected(IEnumerable<CategorySelectionOption> options, bool isSelected)
    {
        foreach (CategorySelectionOption option in options)
        {
            option.IsSelected = isSelected;
        }
    }

    private static IEnumerable<CategorySelectionOption> GetVisibleCategoryOptions(ICollectionView categoryView)
    {
        foreach (object? item in categoryView)
        {
            if (item is CategorySelectionOption option)
            {
                yield return option;
            }
        }
    }

    private static bool CategoryMatchesSearch(CategorySelectionOption? option, string query)
    {
        if (option is null || string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return option.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    private string DescribeFilter(ElementFilterDefinition filter)
    {
        string categories = DescribeCategories(filter.Categories);
        string parameters = filter.ParameterConditions.Count == 0
            ? "без условий по параметрам"
            : $"{filter.ParameterConditions.Count} условий по параметрам";
        string logicalOperator = filter.CategoryAndParameterOperator == FilterLogicalOperator.And ? "AND" : "OR";

        return $"{categories}; {parameters}; связка {logicalOperator}.";
    }

    private string DescribeCategories(IReadOnlyCollection<BuiltInCategory> categories)
    {
        if (categories.Count == 0)
        {
            return "любые категории";
        }

        IReadOnlyList<string> names = categories
            .Select(GetCategoryDisplayName)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (names.Count <= 3)
        {
            return string.Join(", ", names);
        }

        return $"{names.Count} категорий: {string.Join(", ", names.Take(3))}...";
    }

    private string GetCategoryDisplayName(BuiltInCategory builtInCategory)
    {
        return categoryNames.TryGetValue(builtInCategory, out string? name)
            ? name
            : builtInCategory.ToString();
    }

    private IReadOnlyList<CategoryCatalogItem> EnsureCategoryCatalog()
    {
        if (categoryCatalog is not null)
        {
            return categoryCatalog;
        }

        categoryCatalog = CollectCategoryCatalog(document);
        categoryNames = categoryCatalog
            .GroupBy(category => category.BuiltInCategory)
            .ToDictionary(group => group.Key, group => group.First().Name);
        logger.Info($"Join/Cut category catalog loaded with {categoryCatalog.Count} categor(ies).");
        return categoryCatalog;
    }

    private static IReadOnlyList<CategoryCatalogItem> CollectCategoryCatalog(Document document)
    {
        Dictionary<BuiltInCategory, CategoryBucket> buckets = [];
        foreach (Element element in new FilteredElementCollector(document).WhereElementIsNotElementType())
        {
            if (!CanCatalogElement(element) || !TryGetBuiltInCategory(element.Category, out BuiltInCategory builtInCategory))
            {
                continue;
            }

            if (!buckets.TryGetValue(builtInCategory, out CategoryBucket? bucket))
            {
                bucket = new CategoryBucket(builtInCategory, element.Category?.Name ?? builtInCategory.ToString());
                buckets.Add(builtInCategory, bucket);
            }

            bucket.ElementCount++;
        }

        return buckets.Values
            .OrderBy(bucket => bucket.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(bucket => new CategoryCatalogItem(bucket.BuiltInCategory, bucket.Name, bucket.ElementCount))
            .ToList();
    }

    private static bool CanCatalogElement(Element element)
    {
        Category? category = element.Category;
        return category is not null
            && category.Id != ElementId.InvalidElementId
            && category.CategoryType == CategoryType.Model
            && element is not RevitLinkInstance
            && !element.ViewSpecific;
    }

    private static bool TryGetBuiltInCategory(Category? category, out BuiltInCategory builtInCategory)
    {
        builtInCategory = default;
        if (category is null || category.Id == ElementId.InvalidElementId)
        {
            return false;
        }

        long categoryId = RevitElementIds.GetValue(category.Id);
        if (categoryId is < int.MinValue or > int.MaxValue)
        {
            return false;
        }

        builtInCategory = (BuiltInCategory)(int)categoryId;
        return true;
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
        TrueBimWindow dialog = new()
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

    private sealed class JoinCutExternalEventHandler : IExternalEventHandler
    {
        private readonly JoinCutWindow window;
        private JoinCutOperationRequest? request;

        public JoinCutExternalEventHandler(JoinCutWindow window)
        {
            this.window = window ?? throw new ArgumentNullException(nameof(window));
        }

        public void SetRequest(JoinCutOperationRequest request)
        {
            this.request = request;
        }

        public void Execute(UIApplication app)
        {
            JoinCutOperationRequest? currentRequest = request;
            request = null;
            if (currentRequest is null)
            {
                return;
            }

            window.RunOperation(currentRequest);
        }

        public string GetName()
        {
            return "TrueBIM Join/Cut";
        }
    }

    private sealed record JoinCutOperationRequest(
        bool IsPreview,
        JoinCutConfiguration Configuration,
        ProcessingScope Scope,
        JoinAction? JoinAction,
        CutAction? CutAction)
    {
        public string OperationName => JoinAction.HasValue ? "join" : "cut";

        public static JoinCutOperationRequest ForJoin(
            bool isPreview,
            JoinCutConfiguration configuration,
            ProcessingScope scope,
            JoinAction action)
        {
            return new JoinCutOperationRequest(isPreview, configuration, scope, action, null);
        }

        public static JoinCutOperationRequest ForCut(
            bool isPreview,
            JoinCutConfiguration configuration,
            ProcessingScope scope,
            CutAction action)
        {
            return new JoinCutOperationRequest(isPreview, configuration, scope, null, action);
        }
    }

    private sealed record CategoryCatalogItem(
        BuiltInCategory BuiltInCategory,
        string Name,
        int ElementCount);

    private sealed record CategoryBucket(BuiltInCategory BuiltInCategory, string Name)
    {
        public int ElementCount { get; set; }
    }

    private sealed class CategorySelectionOption : INotifyPropertyChanged
    {
        private bool isSelected;

        public CategorySelectionOption(
            BuiltInCategory builtInCategory,
            string name,
            int elementCount,
            bool isSelected)
        {
            BuiltInCategory = builtInCategory;
            Name = name;
            ElementCount = elementCount;
            this.isSelected = isSelected;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public BuiltInCategory BuiltInCategory { get; }

        public string Name { get; }

        public int ElementCount { get; }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value)
                {
                    return;
                }

                isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
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
