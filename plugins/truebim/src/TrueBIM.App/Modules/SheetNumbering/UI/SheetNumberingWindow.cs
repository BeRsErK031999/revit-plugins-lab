using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using TrueBIM.App.Modules.SheetNumbering.Models;
using TrueBIM.App.Modules.SheetNumbering.Rules;
using TrueBIM.App.Modules.SheetNumbering.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using RevitDocument = Autodesk.Revit.DB.Document;

namespace TrueBIM.App.Modules.SheetNumbering.UI;

public sealed class SheetNumberingWindow : Window
{
    private readonly ObservableCollection<PreviewRow> previewRows = new();
    private readonly RevitDocument document;
    private readonly SheetNumberingPreviewWorkflow workflow;
    private readonly SheetNumberApplyService applyService;
    private readonly ITrueBimLogger logger;
    private readonly SheetPreviewOrderService orderService = new();
    private readonly Button applyButton = CreateActionButton("Применить", TrueBimIcon.Apply, isEnabled: false);
    private readonly Button exportPreviewButton = CreateActionButton("Экспорт", TrueBimIcon.Export, isEnabled: false);
    private readonly Button invisibleDuplicateButton = CreateActionButton("Скрытый символ", TrueBimIcon.Move, isEnabled: false, minWidth: 150);
    private readonly Button moveUpButton = CreateActionButton("Вверх", TrueBimIcon.Up, isEnabled: false);
    private readonly Button moveDownButton = CreateActionButton("Вниз", TrueBimIcon.Down, isEnabled: false);
    private readonly Button moveToPositionButton = CreateActionButton("К позиции", TrueBimIcon.Move, isEnabled: false, minWidth: 128);
    private readonly CheckBox includePlaceholdersInput = new()
    {
        Content = "Включать листы-заглушки",
        IsChecked = false,
        ToolTip = "Включает листы-заглушки в предпросмотр и применение."
    };
    private readonly TextBlock statusText = new();
    private readonly ComboBox orderInput = new();
    private readonly TextBox positionInput = new() { Text = "1" };
    private readonly DataGrid previewGrid = new();
    private readonly DispatcherTimer selectionLogTimer;
    private readonly TextBox prefixInput = new() { Text = "A-", ToolTip = "Текст перед номером листа." };
    private readonly TextBox suffixInput = new() { ToolTip = "Текст после номера листа." };
    private readonly TextBox startNumberInput = new() { Text = "1", ToolTip = "Первое число в новой нумерации." };
    private readonly TextBox incrementInput = new() { Text = "1", ToolTip = "На сколько увеличивать номер для следующего листа." };
    private readonly TextBox paddingInput = new() { Text = "2", ToolTip = "Минимальное количество цифр, например 01 или 001." };
    private bool suppressSelectionLogging;
    private bool isPreviewCurrent;
    private IReadOnlyList<SheetInfo> sheets;
    private int previewRowCount;
    private int duplicateIssueCount;
    private string? lastApplyDisabledReason;

    public SheetNumberingWindow(
        RevitDocument document,
        IReadOnlyList<SheetInfo> sheets,
        SheetNumberingPreviewWorkflow workflow,
        SheetNumberApplyService applyService,
        ITrueBimLogger logger)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        this.sheets = sheets ?? throw new ArgumentNullException(nameof(sheets));
        this.workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        this.applyService = applyService ?? throw new ArgumentNullException(nameof(applyService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Title = "Нумерация листов";
        Icon = IconFactory.CreateImage(TrueBimIcon.App, 32);
        Width = 1060;
        Height = 720;
        MinWidth = 1000;
        MinHeight = 650;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        selectionLogTimer = CreateSelectionLogTimer();
        Content = CreateContent();
        logger.Info($"Sheet Numbering window opened with {sheets.Count} sheets.");
        LoadCurrentSheets();
    }

    private UIElement CreateContent()
    {
        DockPanel root = new()
        {
            Margin = new Thickness(20)
        };

        UIElement ruleInputs = CreateRuleInputs();
        DockPanel.SetDock(ruleInputs, Dock.Top);
        root.Children.Add(ruleInputs);

        UIElement selectionControls = CreateSelectionControls();
        DockPanel.SetDock(selectionControls, Dock.Top);
        root.Children.Add(selectionControls);

        UIElement status = CreateStatus();
        DockPanel.SetDock(status, Dock.Top);
        root.Children.Add(status);

        UIElement actions = CreateActions();
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);

        root.Children.Add(CreatePreviewGrid());
        return root;
    }

    private UIElement CreateRuleInputs()
    {
        Grid rules = new()
        {
            Margin = new Thickness(0, 0, 0, 16)
        };

        for (int index = 0; index < 5; index++)
        {
            rules.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        AddRuleField(rules, "Префикс", prefixInput, 0);
        AddRuleField(rules, "Суффикс", suffixInput, 1);
        AddRuleField(rules, "Стартовый номер", startNumberInput, 2);
        AddRuleField(rules, "Шаг", incrementInput, 3);
        AddRuleField(rules, "Разрядность", paddingInput, 4);

        return rules;
    }

    private UIElement CreateSelectionControls()
    {
        DockPanel controls = new()
        {
            Margin = new Thickness(0, 0, 0, 12)
        };

        StackPanel selectionActions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        Button selectAllButton = CreateActionButton("Выбрать все", TrueBimIcon.Apply, isEnabled: true);
        selectAllButton.Margin = new Thickness(0, 0, 8, 0);
        selectAllButton.ToolTip = "Отметить все листы в таблице.";
        selectAllButton.Click += (_, _) => SetAllSelected(isSelected: true);
        selectionActions.Children.Add(selectAllButton);

        Button clearSelectionButton = CreateActionButton("Снять выбор", TrueBimIcon.Close, isEnabled: true);
        clearSelectionButton.Margin = new Thickness(0, 0, 8, 0);
        clearSelectionButton.ToolTip = "Снять отметки со всех листов.";
        clearSelectionButton.Click += (_, _) => SetAllSelected(isSelected: false);
        selectionActions.Children.Add(clearSelectionButton);

        includePlaceholdersInput.VerticalAlignment = VerticalAlignment.Center;
        includePlaceholdersInput.Margin = new Thickness(8, 0, 0, 0);
        includePlaceholdersInput.Checked += (_, _) => OnIncludePlaceholdersChanged();
        includePlaceholdersInput.Unchecked += (_, _) => OnIncludePlaceholdersChanged();
        selectionActions.Children.Add(includePlaceholdersInput);

        DockPanel.SetDock(selectionActions, Dock.Left);
        controls.Children.Add(selectionActions);

        StackPanel orderControls = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        orderControls.Children.Add(new TextBlock
        {
            Text = "Порядок предпросмотра",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        orderInput.Width = 180;
        orderInput.Height = 32;
        orderInput.ToolTip = "Выберите сортировку для предпросмотра. Ручные перемещения включают ручной порядок.";
        orderInput.Items.Add(new ComboBoxItem { Content = "Исходный порядок", Tag = PreviewOrder.Original });
        orderInput.Items.Add(new ComboBoxItem { Content = "Текущий номер", Tag = PreviewOrder.CurrentNumber });
        orderInput.Items.Add(new ComboBoxItem { Content = "Название", Tag = PreviewOrder.Name });
        orderInput.Items.Add(new ComboBoxItem { Content = "Ручной порядок", Tag = PreviewOrder.Manual });
        orderInput.SelectedIndex = 0;
        orderInput.SelectionChanged += (_, _) => ApplyCurrentOrder();
        orderControls.Children.Add(orderInput);

        controls.Children.Add(orderControls);
        return controls;
    }

    private UIElement CreatePreviewGrid()
    {
        previewGrid.AutoGenerateColumns = false;
        previewGrid.CanUserAddRows = false;
        previewGrid.IsReadOnly = false;
        previewGrid.ItemsSource = previewRows;
        previewGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        previewGrid.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        previewGrid.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        previewGrid.SelectionMode = DataGridSelectionMode.Single;
        previewGrid.ToolTip = "Список листов. Отметьте листы и задайте порядок перед предпросмотром.";
        previewGrid.SelectionChanged += (_, _) => UpdateManualOrderButtons();

        previewGrid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Выбрано",
            Binding = new Binding(nameof(PreviewRow.IsSelected))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = 72
        });
        previewGrid.Columns.Add(CreateTextColumn("Позиция", nameof(PreviewRow.Position), 70));
        previewGrid.Columns.Add(CreateTextColumn("Текущий номер", nameof(PreviewRow.CurrentNumber), 130));
        previewGrid.Columns.Add(CreateTextColumn("Предпросмотр", nameof(PreviewRow.PreviewNumber), 130));
        previewGrid.Columns.Add(CreateTextColumn("Название", nameof(PreviewRow.Name), new DataGridLength(1, DataGridLengthUnitType.Star)));
        previewGrid.Columns.Add(CreateTextColumn("Статус / проблема", nameof(PreviewRow.Status), 200));

        return previewGrid;
    }

    private UIElement CreateActions()
    {
        Grid actionRoot = new()
        {
            Margin = new Thickness(0, 16, 0, 0)
        };
        actionRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        StackPanel orderActions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };

        moveUpButton.Margin = new Thickness(0, 0, 8, 0);
        moveUpButton.ToolTip = "Переместить выбранную строку на одну позицию вверх.";
        moveUpButton.Click += (_, _) => MoveSelectedRowUp();
        orderActions.Children.Add(moveUpButton);

        moveDownButton.Margin = new Thickness(0, 0, 16, 0);
        moveDownButton.ToolTip = "Переместить выбранную строку на одну позицию вниз.";
        moveDownButton.Click += (_, _) => MoveSelectedRowDown();
        orderActions.Children.Add(moveDownButton);

        orderActions.Children.Add(new TextBlock
        {
            Text = "Позиция",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        positionInput.Width = 72;
        positionInput.Height = 32;
        positionInput.ToolTip = "Номер позиции, куда нужно переместить выбранный лист.";
        orderActions.Children.Add(positionInput);

        moveToPositionButton.Margin = new Thickness(8, 0, 0, 0);
        moveToPositionButton.ToolTip = "Перемещает выбранный лист на указанную позицию в списке предпросмотра.";
        moveToPositionButton.Click += (_, _) => MoveSelectedRowToPosition();
        orderActions.Children.Add(moveToPositionButton);

        Grid.SetColumn(orderActions, 0);
        actionRoot.Children.Add(orderActions);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        Button previewButton = CreateActionButton("Предпросмотр", TrueBimIcon.Preview, isEnabled: true);
        previewButton.ToolTip = "Сформировать новые номера для выбранных листов.";
        previewButton.Click += (_, _) => GeneratePreview();
        actions.Children.Add(previewButton);

        exportPreviewButton.ToolTip = "Экспорт доступен после предпросмотра.";
        exportPreviewButton.Click += (_, _) => ExportPreview();
        actions.Children.Add(exportPreviewButton);

        invisibleDuplicateButton.ToolTip = "Добавить невидимые символы к дублирующимся номерам, чтобы Revit считал их уникальными.";
        invisibleDuplicateButton.Click += (_, _) => ApplyInvisibleDuplicateSuffixes();
        actions.Children.Add(invisibleDuplicateButton);

        applyButton.ToolTip = "Сначала выполните предпросмотр.";
        applyButton.Click += (_, _) => ApplyPreview();
        actions.Children.Add(applyButton);

        Button closeButton = CreateActionButton("Закрыть", TrueBimIcon.Close, isEnabled: true);
        closeButton.IsCancel = true;
        closeButton.ToolTip = "Закрыть окно без изменений.";
        closeButton.Click += (_, _) => Close();
        actions.Children.Add(closeButton);

        Grid.SetColumn(actions, 1);
        actionRoot.Children.Add(actions);
        return actionRoot;
    }

    private UIElement CreateStatus()
    {
        statusText.Margin = new Thickness(0, 0, 0, 12);
        statusText.TextWrapping = TextWrapping.Wrap;
        UpdateStatusSummary();
        return statusText;
    }

    private void LoadCurrentSheets()
    {
        suppressSelectionLogging = true;
        previewRows.Clear();

        foreach ((SheetInfo sheet, int index) in sheets.Select((sheet, index) => (sheet, index)))
        {
            previewRows.Add(new PreviewRow(
                sheet,
                index,
                isSelected: true,
                sheet.CurrentNumber,
                string.Empty,
                sheet.Name,
                GetBaseStatus(sheet),
                OnRowSelectionChanged));
        }

        suppressSelectionLogging = false;
        isPreviewCurrent = false;
        previewRowCount = 0;
        duplicateIssueCount = 0;
        ApplyCurrentOrder();
        UpdatePositions();
        UpdateStatusSummary();
    }

    private void GeneratePreview()
    {
        ResetPreviewState();

        if (sheets.Count == 0)
        {
            UpdateStatusSummary("В активном документе не найдены листы.");
            logger.Warning("Sheet Numbering preview requested with no sheets.");
            return;
        }

        IReadOnlyList<PreviewRow> selectedRows = GetSelectedRowsInPreviewOrder();
        if (selectedRows.Count == 0)
        {
            UpdateStatusSummary("Выберите хотя бы один лист перед предпросмотром.");
            logger.Warning("Sheet Numbering preview validation failed: no selected sheets.");
            return;
        }

        IReadOnlyList<SheetInfo> previewSheets = SheetNumberingPreviewSelection.FilterSheetsForPreview(
            selectedRows.Select(row => row.Sheet).ToList(),
            IncludePlaceholders);

        if (previewSheets.Count == 0)
        {
            UpdateStatusSummary("Выбранные листы являются заглушками. Включите листы-заглушки для предпросмотра.");
            logger.Warning("Sheet Numbering preview validation failed: selected sheets were excluded placeholders.");
            return;
        }

        if (!TryCreateRules(out NumberingRules? rules, out string? error))
        {
            string message = error ?? "Invalid numbering rules.";
            UpdateStatusSummary(message);
            logger.Warning("Sheet Numbering preview validation failed: " + message);
            return;
        }

        NumberingRules validRules = rules ?? throw new InvalidOperationException("Numbering rules were not created.");

        try
        {
            logger.Info($"Running Sheet Numbering preview for {previewSheets.Count} sheets. Include placeholders: {IncludePlaceholders}.");
            SheetNumberingPreviewResult result = workflow.GeneratePreview(
                new SheetNumberingPreviewRequest(
                    previewSheets,
                    sheets,
                    validRules));
            IReadOnlyDictionary<long, string> issuesBySheetId = CreateIssueLookup(result.DuplicateIssues);
            IReadOnlyDictionary<long, SheetNumberPreview> previewsBySheetId = result.Previews.ToDictionary(
                preview => preview.Sheet.ElementId);
            previewRowCount = result.Previews.Count;
            duplicateIssueCount = result.DuplicateIssues.Count;
            isPreviewCurrent = true;
            logger.Info(
                $"Sheet Numbering preview generated {previewRowCount} rows for {selectedRows.Count} selected sheets with {duplicateIssueCount} duplicate issues.");

            foreach (PreviewRow row in previewRows)
            {
                if (previewsBySheetId.TryGetValue(row.Sheet.ElementId, out SheetNumberPreview? preview))
                {
                    row.PreviewNumber = preview.PreviewNumber;
                    row.Status = GetStatus(preview, issuesBySheetId);
                }
                else if (issuesBySheetId.TryGetValue(row.Sheet.ElementId, out string? issue))
                {
                    row.PreviewNumber = string.Empty;
                    row.Status = issue;
                }
            }

            string message = result.HasBlockingIssues
                ? "Предпросмотр создан, но есть дубли номеров. Применение отключено."
                : "Предпросмотр создан. Применение доступно, если есть измененные строки.";
            UpdateStatusSummary(message);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            UpdateStatusSummary(exception.Message);
            logger.Error("Sheet Numbering preview failed.", exception);
        }
    }

    private void ResetPreviewState()
    {
        isPreviewCurrent = false;
        previewRowCount = 0;
        duplicateIssueCount = 0;

        foreach (PreviewRow row in previewRows)
        {
            row.PreviewNumber = string.Empty;
            row.Status = GetBaseStatus(row);
        }

        UpdateStatusSummary();
    }

    private void ApplyPreview()
    {
        IReadOnlyList<SheetNumberChange> changes = CreateApplyChanges();

        if (!CanApply(changes))
        {
            UpdateStatusSummary("Применение недоступно, пока нет актуального предпросмотра без дублей.");
            logger.Warning("Sheet Numbering Apply requested while validation state was not ready.");
            return;
        }

        try
        {
            int changedPreviewCount = changes.Count(change => change.IsChanged);
            IReadOnlyList<SheetNumberChange> changedChanges = changes
                .Where(change => change.IsChanged)
                .ToList();
            if (!ConfirmApply(changedChanges))
            {
                logger.Info("Sheet Numbering Apply confirmation cancelled.");
                UpdateStatusSummary("Применение отменено. Номера листов не изменены.");
                return;
            }

            logger.Info("Sheet Numbering Apply confirmation accepted.");
            logger.Info(
                $"Starting Sheet Numbering Apply for {changes.Count} preview rows with {changedPreviewCount} changed rows.");

            SheetNumberApplyResult result = applyService.Apply(document, changes);
            if (!result.Succeeded)
            {
                logger.Warning(
                    $"Sheet Numbering Apply rolled back or did not start. {result.Message} Changed {result.ChangedCount}, unchanged {result.UnchangedCount}, skipped {result.SkippedCount}, failed {result.FailedCount}.");
                UpdateStatusSummary(
                    $"Применение не выполнено, номера листов не изменены. {result.Message}");
                return;
            }

            logger.Info(
                $"Sheet Numbering Apply transaction committed. Changed {result.ChangedCount}, unchanged {result.UnchangedCount}, skipped {result.SkippedCount}, failed {result.FailedCount}.");

            ReloadSheetsFromDocument();
            UpdateStatusSummary(
                $"Готово. Изменено: {result.ChangedCount}, без изменений: {result.UnchangedCount}, пропущено: {result.SkippedCount}, ошибок: {result.FailedCount}.");
            MessageBox.Show(
                this,
                $"Готово. Изменено номеров листов: {result.ChangedCount}.",
                "Нумерация листов",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (
            exception is Autodesk.Revit.Exceptions.ArgumentException
            or InvalidOperationException
            or Autodesk.Revit.Exceptions.ApplicationException)
        {
            logger.Error("Sheet Numbering Apply failed.", exception);
            UpdateStatusSummary("Применение не выполнено, номера листов не изменены. Проверьте логи.");
        }
    }

    private bool ConfirmApply(IReadOnlyList<SheetNumberChange> changedChanges)
    {
        string examples = string.Join(
            Environment.NewLine,
            changedChanges.Take(5).Select(change => $"{change.CurrentNumber} -> {change.NewNumber}"));
        string more = changedChanges.Count > 5
            ? Environment.NewLine + $"...и еще {changedChanges.Count - 5}."
            : string.Empty;
        string message =
            $"Применить изменений номеров листов: {changedChanges.Count}?" + Environment.NewLine + Environment.NewLine +
            examples + more + Environment.NewLine + Environment.NewLine +
            "Операция выполняется одной транзакцией Revit и откатывается через Revit Undo.";

        return MessageBox.Show(
            this,
            message,
            "Подтверждение нумерации листов",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning) == MessageBoxResult.OK;
    }

    private void ExportPreview()
    {
        if (!isPreviewCurrent || previewRowCount == 0)
        {
            UpdateStatusSummary("Перед экспортом выполните предпросмотр.");
            return;
        }

        try
        {
            string exportDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TrueBIM",
                "Exports",
                "SheetNumbering");
            Directory.CreateDirectory(exportDirectory);
            string exportPath = Path.Combine(
                exportDirectory,
                "SheetNumberingPreview-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");

            SheetNumberPreviewExportFormatter formatter = new();
            File.WriteAllText(exportPath, formatter.FormatCsv(CreateExportRows()));
            logger.Info($"Sheet Numbering preview exported to '{exportPath}'.");
            UpdateStatusSummary($"Предпросмотр экспортирован: {exportPath}");
            Process.Start(new ProcessStartInfo(exportPath) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.Error("Failed to export Sheet Numbering preview.", exception);
            UpdateStatusSummary("Экспорт предпросмотра не выполнен. Проверьте логи.");
        }
    }

    private IReadOnlyList<SheetNumberChange> CreateApplyChanges()
    {
        return previewRows
            .Where(row => row.IsSelected && !string.IsNullOrWhiteSpace(row.PreviewNumber) && IsRowApplyEligible(row))
            .Select(row => new SheetNumberChange(row.Sheet.ElementId, row.CurrentNumber, row.PreviewNumber))
            .ToList();
    }

    private IReadOnlyList<SheetNumberPreviewExportRow> CreateExportRows()
    {
        return previewRows
            .Select(row => new SheetNumberPreviewExportRow(
                row.Sheet.ElementId,
                row.CurrentNumber,
                row.PreviewNumber,
                row.Name,
                row.Sheet.IsPlaceholder,
                row.Status))
            .ToList();
    }

    private bool CanApply(IReadOnlyList<SheetNumberChange>? changes = null)
    {
        return GetApplyValidation(changes).CanApply;
    }

    private SheetNumberingApplyValidationResult GetApplyValidation(IReadOnlyList<SheetNumberChange>? changes = null)
    {
        IReadOnlyList<SheetNumberChange> applyChanges = changes ?? CreateApplyChanges();

        return SheetNumberingApplyValidator.Validate(
            GetSelectedPreviewEligibleCount(),
            isPreviewCurrent,
            duplicateIssueCount,
            applyChanges.Count(change => change.IsChanged));
    }

    private void InvalidatePreview(string message)
    {
        isPreviewCurrent = false;
        previewRowCount = 0;
        duplicateIssueCount = 0;

        foreach (PreviewRow row in previewRows)
        {
            row.PreviewNumber = string.Empty;
            row.Status = GetBaseStatus(row);
        }

        UpdateStatusSummary(message);
    }

    private void ReloadSheetsFromDocument()
    {
        sheets = new SheetCollectorService().Collect(document);
        LoadCurrentSheets();
        logger.Info($"Sheet Numbering reloaded {sheets.Count} sheets after Apply.");
    }

    private static IReadOnlyDictionary<long, string> CreateIssueLookup(
        IReadOnlyList<DuplicateSheetNumberIssue> duplicateIssues)
    {
        Dictionary<long, string> issuesBySheetId = new();

        foreach (DuplicateSheetNumberIssue issue in duplicateIssues)
        {
            string message = issue.Kind == DuplicateSheetNumberIssueKind.Preview
                ? $"Дубль в предпросмотре: {issue.SheetNumber}. Можно применить «Скрытый символ»."
                : $"Конфликт с существующим номером: {issue.SheetNumber}. Можно применить «Скрытый символ».";

            foreach (SheetInfo sheet in issue.Sheets)
            {
                issuesBySheetId[sheet.ElementId] = message;
            }
        }

        return issuesBySheetId;
    }

    private void ApplyInvisibleDuplicateSuffixes()
    {
        if (!isPreviewCurrent || previewRowCount == 0 || duplicateIssueCount == 0)
        {
            UpdateStatusSummary("Нет актуальных дублей для скрытого символа.");
            return;
        }

        if (MessageBox.Show(
                this,
                "К дублирующимся номерам будут добавлены невидимые Unicode-символы. В Revit номера станут технически уникальными, но на листах будут выглядеть одинаково. Продолжить?",
                "Нумератор листов",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }

        HashSet<long> previewSheetIds = previewRows
            .Where(row => row.IsSelected && !string.IsNullOrWhiteSpace(row.PreviewNumber))
            .Select(row => row.Sheet.ElementId)
            .ToHashSet();
        HashSet<string> usedNumbers = sheets
            .Where(sheet => !previewSheetIds.Contains(sheet.ElementId))
            .Select(sheet => sheet.CurrentNumber)
            .Where(number => !string.IsNullOrWhiteSpace(number))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int changed = 0;
        foreach (PreviewRow row in previewRows.Where(row => row.IsSelected && !string.IsNullOrWhiteSpace(row.PreviewNumber)))
        {
            string baseNumber = StripInvisibleDuplicateMarkers(row.PreviewNumber);
            string candidate = baseNumber;
            int suffixLength = 0;
            while (usedNumbers.Contains(candidate))
            {
                suffixLength++;
                candidate = baseNumber + CreateInvisibleSuffix(row.Sheet.ElementId, suffixLength);
            }

            usedNumbers.Add(candidate);
            if (!string.Equals(row.PreviewNumber, candidate, StringComparison.Ordinal))
            {
                row.PreviewNumber = candidate;
                row.Status = "Скрытый символ добавлен для уникальности.";
                changed++;
            }
        }

        RevalidatePreviewDuplicates();
        UpdateStatusSummary(changed == 0
            ? "Скрытые символы не понадобились: конфликтов в текущем предпросмотре больше нет."
            : $"Скрытые символы добавлены: {changed}. Проверьте предпросмотр перед применением.");
    }

    private void RevalidatePreviewDuplicates()
    {
        List<SheetNumberPreview> previews = previewRows
            .Where(row => row.IsSelected && !string.IsNullOrWhiteSpace(row.PreviewNumber))
            .Select(row => new SheetNumberPreview(row.Sheet, row.PreviewNumber))
            .ToList();
        IReadOnlyList<DuplicateSheetNumberIssue> issues = new DuplicateSheetNumberDetector().Detect(previews, sheets);
        IReadOnlyDictionary<long, string> issuesBySheetId = CreateIssueLookup(issues);
        duplicateIssueCount = issues.Count;

        foreach (PreviewRow row in previewRows.Where(row => row.IsSelected && !string.IsNullOrWhiteSpace(row.PreviewNumber)))
        {
            if (issuesBySheetId.TryGetValue(row.Sheet.ElementId, out string? issue))
            {
                row.Status = issue;
            }
            else if (ContainsInvisibleDuplicateMarker(row.PreviewNumber))
            {
                row.Status = "Скрытый символ добавлен для уникальности.";
            }
            else
            {
                row.Status = row.CurrentNumber == row.PreviewNumber ? "Без изменений" : "Будет изменено";
            }
        }
    }

    private static string CreateInvisibleSuffix(long elementId, int suffixLength)
    {
        char[] markers = ['\u200B', '\u200C', '\u2060'];
        int markerIndex = (int)((Math.Abs(elementId) + suffixLength) % markers.Length);
        char marker = markers[markerIndex];
        return new string(marker, suffixLength);
    }

    private static string StripInvisibleDuplicateMarkers(string value)
    {
        return value
            .Replace("\u200B", string.Empty)
            .Replace("\u200C", string.Empty)
            .Replace("\u2060", string.Empty);
    }

    private static bool ContainsInvisibleDuplicateMarker(string value)
    {
        return value.IndexOf('\u200B') >= 0
            || value.IndexOf('\u200C') >= 0
            || value.IndexOf('\u2060') >= 0;
    }

    private static string GetStatus(
        SheetNumberPreview preview,
        IReadOnlyDictionary<long, string> issuesBySheetId)
    {
        if (issuesBySheetId.TryGetValue(preview.Sheet.ElementId, out string? issue))
        {
            return issue;
        }

        if (preview.Sheet.IsPlaceholder)
        {
            return "Лист-заглушка";
        }

        return preview.IsChanged ? "Будет изменено" : "Без изменений";
    }

    private void SetAllSelected(bool isSelected)
    {
        suppressSelectionLogging = true;

        foreach (PreviewRow row in previewRows)
        {
            row.IsSelected = isSelected;
            row.PreviewNumber = string.Empty;
            row.Status = GetBaseStatus(row);
        }

        suppressSelectionLogging = false;
        isPreviewCurrent = false;
        previewRowCount = 0;
        duplicateIssueCount = 0;
        UpdateStatusSummary(isSelected ? "Выбраны все листы." : "Выбор снят.");
        logger.Info($"Sheet Numbering selection changed: {GetSelectedCount()} of {sheets.Count} sheets selected.");
    }

    private void OnRowSelectionChanged()
    {
        if (suppressSelectionLogging)
        {
            return;
        }

        previewRowCount = 0;
        duplicateIssueCount = 0;
        isPreviewCurrent = false;

        foreach (PreviewRow row in previewRows)
        {
            row.PreviewNumber = string.Empty;
            row.Status = GetBaseStatus(row);
        }

        UpdateStatusSummary("Выбор изменен. Выполните предпросмотр заново.");
        selectionLogTimer.Stop();
        selectionLogTimer.Start();
    }

    private DispatcherTimer CreateSelectionLogTimer()
    {
        DispatcherTimer timer = new()
        {
            Interval = TimeSpan.FromMilliseconds(600)
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            logger.Info($"Sheet Numbering selection changed: {GetSelectedCount()} of {sheets.Count} sheets selected.");
        };

        return timer;
    }

    private void ApplyCurrentOrder()
    {
        if (previewRows.Count == 0)
        {
            return;
        }

        PreviewOrder selectedOrder = GetSelectedPreviewOrder();
        if (selectedOrder == PreviewOrder.Manual)
        {
            UpdateManualOrderButtons();
            return;
        }

        List<PreviewRow> orderedRows = GetRowsInPreviewOrder().ToList();
        suppressSelectionLogging = true;
        previewRows.Clear();

        foreach (PreviewRow row in orderedRows)
        {
            previewRows.Add(row);
        }

        suppressSelectionLogging = false;
        UpdatePositions();
        InvalidatePreview("Порядок предпросмотра изменен. Выполните предпросмотр заново.");
        UpdateManualOrderButtons();
    }

    private IReadOnlyList<PreviewRow> GetSelectedRowsInPreviewOrder()
    {
        return GetRowsInPreviewOrder()
            .Where(row => row.IsSelected)
            .ToList();
    }

    private IEnumerable<PreviewRow> GetRowsInPreviewOrder()
    {
        return GetSelectedPreviewOrder() switch
        {
            PreviewOrder.CurrentNumber => previewRows
                .OrderBy(row => row.CurrentNumber, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.OriginalOrder),
            PreviewOrder.Name => previewRows
                .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.OriginalOrder),
            PreviewOrder.Manual => previewRows,
            _ => previewRows.OrderBy(row => row.OriginalOrder)
        };
    }

    private PreviewOrder GetSelectedPreviewOrder()
    {
        return orderInput.SelectedItem is ComboBoxItem { Tag: PreviewOrder order }
            ? order
            : PreviewOrder.Original;
    }

    private void UpdateStatusSummary(string? message = null)
    {
        string summary = $"Загружено листов: {sheets.Count}. Выбрано: {GetSelectedCount()}. Строк предпросмотра: {previewRowCount}. Дублей: {duplicateIssueCount}.";
        statusText.Text = string.IsNullOrWhiteSpace(message)
            ? summary
            : $"{summary} {message}";
        UpdateApplyState();
    }

    private void UpdateApplyState()
    {
        SheetNumberingApplyValidationResult validation = GetApplyValidation();
        applyButton.IsEnabled = validation.CanApply;
        applyButton.ToolTip = validation.CanApply
            ? "Применить текущий предпросмотр одной транзакцией Revit."
            : LocalizeApplyDisabledReason(validation.Reason);
        exportPreviewButton.IsEnabled = isPreviewCurrent && previewRowCount > 0;
        exportPreviewButton.ToolTip = exportPreviewButton.IsEnabled
            ? "Экспортировать текущий предпросмотр в CSV."
            : "Перед экспортом выполните предпросмотр.";
        invisibleDuplicateButton.IsEnabled = isPreviewCurrent && duplicateIssueCount > 0;
        invisibleDuplicateButton.ToolTip = invisibleDuplicateButton.IsEnabled
            ? "Добавить невидимые символы к конфликтующим номерам листов."
            : "Доступно после предпросмотра с дублями номеров.";

        if (!validation.CanApply && validation.Reason != lastApplyDisabledReason)
        {
            lastApplyDisabledReason = validation.Reason;
            logger.Info("Sheet Numbering Apply disabled: " + validation.Reason);
        }
        UpdateManualOrderButtons();
    }

    private int GetSelectedCount()
    {
        return previewRows.Count(row => row.IsSelected);
    }

    private int GetSelectedPreviewEligibleCount()
    {
        return previewRows.Count(row => row.IsSelected && (IncludePlaceholders || !row.Sheet.IsPlaceholder));
    }

    private bool IncludePlaceholders => includePlaceholdersInput.IsChecked == true;

    private string GetBaseStatus(PreviewRow row)
    {
        if (!row.IsSelected)
        {
            return "Не выбрано";
        }

        return GetBaseStatus(row.Sheet);
    }

    private string GetBaseStatus(SheetInfo sheet)
    {
        if (!sheet.IsPlaceholder)
        {
            return "Готово";
        }

        return IncludePlaceholders ? "Лист-заглушка" : "Заглушка исключена";
    }

    private static bool IsRowApplyEligible(PreviewRow row)
    {
        return !row.Status.StartsWith("Дубль в предпросмотре:", StringComparison.OrdinalIgnoreCase)
            && !row.Status.StartsWith("Конфликт с существующим номером:", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryCreateRules(out NumberingRules? rules, out string? error)
    {
        rules = null;

        if (!int.TryParse(startNumberInput.Text, out int startNumber))
        {
            error = "Стартовый номер должен быть целым числом.";
            return false;
        }

        if (!int.TryParse(incrementInput.Text, out int increment))
        {
            error = "Шаг должен быть целым числом.";
            return false;
        }

        if (!int.TryParse(paddingInput.Text, out int padding))
        {
            error = "Разрядность должна быть целым числом.";
            return false;
        }

        rules = new NumberingRules(prefixInput.Text, suffixInput.Text, startNumber, increment, padding);
        try
        {
            rules.FormatNumber(0);
        }
        catch (InvalidOperationException exception)
        {
            rules = null;
            error = LocalizeRuleError(exception.Message);
            return false;
        }

        error = null;
        return true;
    }

    private void OnIncludePlaceholdersChanged()
    {
        InvalidatePreview("Настройка листов-заглушек изменена. Выполните предпросмотр заново.");
        logger.Info($"Sheet Numbering Include placeholders changed: {IncludePlaceholders}.");
    }

    private static void AddRuleField(Grid grid, string label, TextBox input, int column)
    {
        StackPanel field = new()
        {
            Margin = new Thickness(column == 0 ? 0 : 8, 0, 0, 0)
        };

        field.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 0, 4)
        });

        input.MinWidth = 120;
        input.Height = 28;
        field.Children.Add(input);

        Grid.SetColumn(field, column);
        grid.Children.Add(field);
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
            IsReadOnly = true,
            Width = width
        };
    }

    private static Button CreateActionButton(string text, TrueBimIcon icon, bool isEnabled, double minWidth = 104)
    {
        return new Button
        {
            Content = IconFactory.CreateButtonContent(icon, text),
            MinWidth = minWidth,
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = isEnabled
        };
    }

    private void MoveSelectedRowUp()
    {
        ApplyManualOrderChange(orderService.MoveUp(previewRows.ToList(), previewGrid.SelectedIndex));
    }

    private void MoveSelectedRowDown()
    {
        ApplyManualOrderChange(orderService.MoveDown(previewRows.ToList(), previewGrid.SelectedIndex));
    }

    private void MoveSelectedRowToPosition()
    {
        if (!int.TryParse(positionInput.Text, out int targetPosition))
        {
            UpdateStatusSummary("Позиция должна быть целым числом.");
            return;
        }

        ApplyManualOrderChange(orderService.MoveToPosition(previewRows.ToList(), previewGrid.SelectedIndex, targetPosition));
    }

    private void ApplyManualOrderChange(SheetPreviewOrderChange<PreviewRow> change)
    {
        if (!change.Changed)
        {
            UpdateManualOrderButtons();
            return;
        }

        SelectPreviewOrder(PreviewOrder.Manual);
        suppressSelectionLogging = true;
        previewRows.Clear();

        foreach (PreviewRow row in change.Items)
        {
            previewRows.Add(row);
        }

        suppressSelectionLogging = false;
        UpdatePositions();
        previewGrid.SelectedIndex = change.SelectedIndex;
        InvalidatePreview("Порядок листов изменен вручную. Выполните предпросмотр заново.");
        UpdateManualOrderButtons();
    }

    private void UpdatePositions()
    {
        for (int index = 0; index < previewRows.Count; index++)
        {
            previewRows[index].Position = index + 1;
        }
    }

    private void UpdateManualOrderButtons()
    {
        int index = previewGrid.SelectedIndex;
        bool hasSelection = index >= 0 && index < previewRows.Count;
        moveUpButton.IsEnabled = hasSelection && index > 0;
        moveDownButton.IsEnabled = hasSelection && index < previewRows.Count - 1;
        moveToPositionButton.IsEnabled = hasSelection && previewRows.Count > 1;
        if (hasSelection)
        {
            positionInput.Text = (index + 1).ToString();
        }
    }

    private void SelectPreviewOrder(PreviewOrder order)
    {
        foreach (ComboBoxItem item in orderInput.Items)
        {
            if (item.Tag is PreviewOrder itemOrder && itemOrder == order)
            {
                orderInput.SelectedItem = item;
                return;
            }
        }
    }

    private static string LocalizeApplyDisabledReason(string reason)
    {
        return reason switch
        {
            "Select at least one sheet." => "Выберите хотя бы один лист.",
            "Run Preview before Apply." => "Сначала выполните предпросмотр.",
            "Resolve duplicate conflicts before Apply." => "Устраните дубли номеров перед применением.",
            "No sheet numbers will change." => "Предпросмотр не меняет выбранные номера листов.",
            "Ready to apply." => "Готово к применению.",
            _ => reason
        };
    }

    private static string LocalizeRuleError(string message)
    {
        return message switch
        {
            "Increment must not be zero." => "Шаг не должен быть равен нулю.",
            "Padding must be zero or greater." => "Разрядность должна быть нулем или больше.",
            _ => message
        };
    }

    private enum PreviewOrder
    {
        Original,
        CurrentNumber,
        Name,
        Manual
    }

    private sealed class PreviewRow : INotifyPropertyChanged
    {
        private readonly Action selectionChanged;
        private bool isSelected;
        private string previewNumber;
        private string status;
        private int position;

        public PreviewRow(
            SheetInfo sheet,
            int originalOrder,
            bool isSelected,
            string currentNumber,
            string previewNumber,
            string name,
            string status,
            Action selectionChanged)
        {
            Sheet = sheet;
            OriginalOrder = originalOrder;
            this.isSelected = isSelected;
            CurrentNumber = currentNumber;
            this.previewNumber = previewNumber;
            Name = name;
            this.status = status;
            this.selectionChanged = selectionChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public SheetInfo Sheet { get; }

        public int OriginalOrder { get; }

        public int Position
        {
            get => position;
            set
            {
                if (position == value)
                {
                    return;
                }

                position = value;
                OnPropertyChanged(nameof(Position));
            }
        }

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
                OnPropertyChanged(nameof(IsSelected));
                selectionChanged();
            }
        }

        public string CurrentNumber { get; }

        public string PreviewNumber
        {
            get => previewNumber;
            set
            {
                if (previewNumber == value)
                {
                    return;
                }

                previewNumber = value;
                OnPropertyChanged(nameof(PreviewNumber));
            }
        }

        public string Name { get; }

        public string Status
        {
            get => status;
            set
            {
                if (status == value)
                {
                    return;
                }

                status = value;
                OnPropertyChanged(nameof(Status));
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
