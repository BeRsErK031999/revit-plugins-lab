using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using TrueBIM.App.Modules.SheetNumbering.Models;
using TrueBIM.App.Modules.SheetNumbering.Rules;
using TrueBIM.App.Modules.SheetNumbering.Services;
using TrueBIM.App.Services.Logging;
using RevitDocument = Autodesk.Revit.DB.Document;

namespace TrueBIM.App.Modules.SheetNumbering.UI;

public sealed class SheetNumberingWindow : Window
{
    private readonly ObservableCollection<PreviewRow> previewRows = new();
    private readonly RevitDocument document;
    private readonly SheetNumberingPreviewWorkflow workflow;
    private readonly SheetNumberApplyService applyService;
    private readonly ITrueBimLogger logger;
    private readonly Button applyButton = CreateActionButton("Apply", isEnabled: false);
    private readonly TextBlock statusText = new();
    private readonly ComboBox orderInput = new();
    private readonly DispatcherTimer selectionLogTimer;
    private readonly TextBox prefixInput = new() { Text = "A-" };
    private readonly TextBox suffixInput = new();
    private readonly TextBox startNumberInput = new() { Text = "1" };
    private readonly TextBox incrementInput = new() { Text = "1" };
    private readonly TextBox paddingInput = new() { Text = "2" };
    private bool suppressSelectionLogging;
    private bool isPreviewCurrent;
    private IReadOnlyList<SheetInfo> sheets;
    private int previewRowCount;
    private int duplicateIssueCount;

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
        Title = "Sheet Numbering";
        Width = 820;
        Height = 520;
        MinWidth = 680;
        MinHeight = 420;
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

        AddRuleField(rules, "Prefix", prefixInput, 0);
        AddRuleField(rules, "Suffix", suffixInput, 1);
        AddRuleField(rules, "Start Number", startNumberInput, 2);
        AddRuleField(rules, "Increment", incrementInput, 3);
        AddRuleField(rules, "Padding", paddingInput, 4);

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

        Button selectAllButton = CreateActionButton("Select All", isEnabled: true);
        selectAllButton.Margin = new Thickness(0, 0, 8, 0);
        selectAllButton.Click += (_, _) => SetAllSelected(isSelected: true);
        selectionActions.Children.Add(selectAllButton);

        Button clearSelectionButton = CreateActionButton("Clear Selection", isEnabled: true);
        clearSelectionButton.Margin = new Thickness(0, 0, 8, 0);
        clearSelectionButton.Click += (_, _) => SetAllSelected(isSelected: false);
        selectionActions.Children.Add(clearSelectionButton);

        DockPanel.SetDock(selectionActions, Dock.Left);
        controls.Children.Add(selectionActions);

        StackPanel orderControls = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        orderControls.Children.Add(new TextBlock
        {
            Text = "Preview order",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        orderInput.Width = 180;
        orderInput.Height = 32;
        orderInput.Items.Add(new ComboBoxItem { Content = "Original order", Tag = PreviewOrder.Original });
        orderInput.Items.Add(new ComboBoxItem { Content = "Current number", Tag = PreviewOrder.CurrentNumber });
        orderInput.Items.Add(new ComboBoxItem { Content = "Name", Tag = PreviewOrder.Name });
        orderInput.SelectedIndex = 0;
        orderInput.SelectionChanged += (_, _) => ApplyCurrentOrder();
        orderControls.Children.Add(orderInput);

        controls.Children.Add(orderControls);
        return controls;
    }

    private UIElement CreatePreviewGrid()
    {
        DataGrid grid = new()
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = false,
            ItemsSource = previewRows,
            HeadersVisibility = DataGridHeadersVisibility.Column
        };

        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Selected",
            Binding = new Binding(nameof(PreviewRow.IsSelected))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = 80
        });
        grid.Columns.Add(CreateTextColumn("Current Number", nameof(PreviewRow.CurrentNumber), 130));
        grid.Columns.Add(CreateTextColumn("Preview Number", nameof(PreviewRow.PreviewNumber), 130));
        grid.Columns.Add(CreateTextColumn("Name", nameof(PreviewRow.Name), 220));
        grid.Columns.Add(CreateTextColumn("Status / Issue", nameof(PreviewRow.Status), 220));

        return grid;
    }

    private UIElement CreateActions()
    {
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        Button previewButton = CreateActionButton("Preview", isEnabled: true);
        previewButton.Click += (_, _) => GeneratePreview();
        actions.Children.Add(previewButton);

        applyButton.ToolTip = "Apply is disabled until the Revit transaction write step is implemented.";
        applyButton.Click += (_, _) => ApplyPreview();
        actions.Children.Add(applyButton);

        Button closeButton = CreateActionButton("Close", isEnabled: true);
        closeButton.IsCancel = true;
        closeButton.Click += (_, _) => Close();
        actions.Children.Add(closeButton);

        return actions;
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
                sheet.IsPlaceholder ? "Placeholder" : "Ready",
                OnRowSelectionChanged));
        }

        suppressSelectionLogging = false;
        isPreviewCurrent = false;
        previewRowCount = 0;
        duplicateIssueCount = 0;
        ApplyCurrentOrder();
        UpdateStatusSummary();
    }

    private void GeneratePreview()
    {
        ResetPreviewState();

        if (sheets.Count == 0)
        {
            UpdateStatusSummary("No sheets found in the active document.");
            logger.Warning("Sheet Numbering preview requested with no sheets.");
            return;
        }

        IReadOnlyList<PreviewRow> selectedRows = GetSelectedRowsInPreviewOrder();
        if (selectedRows.Count == 0)
        {
            UpdateStatusSummary("Select at least one sheet before running preview.");
            logger.Warning("Sheet Numbering preview validation failed: no selected sheets.");
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
            logger.Info($"Running Sheet Numbering preview for {selectedRows.Count} selected sheets.");
            SheetNumberingPreviewResult result = workflow.GeneratePreview(
                new SheetNumberingPreviewRequest(
                    selectedRows.Select(row => row.Sheet).ToList(),
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
                ? "Preview generated with duplicate sheet number issues. Apply is disabled."
                : "Preview generated. Apply is disabled until the write operation is implemented.";
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
            UpdateStatusSummary("Apply is unavailable until a changed preview without duplicate issues is generated.");
            logger.Warning("Sheet Numbering Apply requested while validation state was not ready.");
            return;
        }

        try
        {
            int changedPreviewCount = changes.Count(change => change.IsChanged);
            logger.Info(
                $"Starting Sheet Numbering Apply for {changes.Count} preview rows with {changedPreviewCount} changed rows.");

            SheetNumberApplyResult result = applyService.Apply(document, changes);
            if (!result.Succeeded)
            {
                logger.Warning(
                    $"Sheet Numbering Apply rolled back or did not start. {result.Message} Changed {result.ChangedCount}, unchanged {result.UnchangedCount}, skipped {result.SkippedCount}, failed {result.FailedCount}.");
                UpdateStatusSummary(
                    $"Apply failed, no sheet numbers were changed. {result.Message}");
                return;
            }

            logger.Info(
                $"Sheet Numbering Apply transaction committed. Changed {result.ChangedCount}, unchanged {result.UnchangedCount}, skipped {result.SkippedCount}, failed {result.FailedCount}.");

            ReloadSheetsFromDocument();
            UpdateStatusSummary(
                $"Apply complete. Changed {result.ChangedCount}, unchanged {result.UnchangedCount}, skipped {result.SkippedCount}, failed {result.FailedCount}.");
        }
        catch (Exception exception) when (
            exception is Autodesk.Revit.Exceptions.ArgumentException
            or InvalidOperationException
            or Autodesk.Revit.Exceptions.ApplicationException)
        {
            logger.Error("Sheet Numbering Apply failed.", exception);
            UpdateStatusSummary("Apply failed, no sheet numbers were changed. Review Logs for diagnostics.");
        }
    }

    private IReadOnlyList<SheetNumberChange> CreateApplyChanges()
    {
        return previewRows
            .Where(row => row.IsSelected && !string.IsNullOrWhiteSpace(row.PreviewNumber) && IsRowApplyEligible(row))
            .Select(row => new SheetNumberChange(row.Sheet.ElementId, row.CurrentNumber, row.PreviewNumber))
            .ToList();
    }

    private bool CanApply(IReadOnlyList<SheetNumberChange>? changes = null)
    {
        IReadOnlyList<SheetNumberChange> applyChanges = changes ?? CreateApplyChanges();

        return GetSelectedCount() > 0
            && isPreviewCurrent
            && duplicateIssueCount == 0
            && applyChanges.Any(change => change.IsChanged);
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
                ? $"Duplicate preview number {issue.SheetNumber}"
                : $"Conflicts with existing sheet number {issue.SheetNumber}";

            foreach (SheetInfo sheet in issue.Sheets)
            {
                issuesBySheetId[sheet.ElementId] = message;
            }
        }

        return issuesBySheetId;
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
            return "Placeholder";
        }

        return preview.IsChanged ? "Preview" : "Unchanged";
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
        UpdateStatusSummary(isSelected ? "All sheets selected." : "Selection cleared.");
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

        UpdateStatusSummary("Selection changed. Run Preview to refresh preview numbers.");
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

        List<PreviewRow> orderedRows = GetRowsInPreviewOrder().ToList();
        suppressSelectionLogging = true;
        previewRows.Clear();

        foreach (PreviewRow row in orderedRows)
        {
            previewRows.Add(row);
        }

        suppressSelectionLogging = false;
        InvalidatePreview("Preview order changed. Run Preview to refresh preview numbers.");
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
        string summary = $"Loaded {sheets.Count} sheets. Selected {GetSelectedCount()}. Preview rows {previewRowCount}. Duplicate issues {duplicateIssueCount}.";
        statusText.Text = string.IsNullOrWhiteSpace(message)
            ? summary
            : $"{summary} {message}";
        UpdateApplyState();
    }

    private void UpdateApplyState()
    {
        bool canApply = CanApply();
        applyButton.IsEnabled = canApply;
        applyButton.ToolTip = canApply
            ? "Apply the current preview in one Revit transaction."
            : "Apply is available after a changed preview is generated without duplicate issues.";
    }

    private int GetSelectedCount()
    {
        return previewRows.Count(row => row.IsSelected);
    }

    private static string GetBaseStatus(PreviewRow row)
    {
        if (!row.IsSelected)
        {
            return "Not selected";
        }

        return row.Sheet.IsPlaceholder ? "Placeholder" : "Ready";
    }

    private static bool IsRowApplyEligible(PreviewRow row)
    {
        return !row.Status.StartsWith("Duplicate preview number", StringComparison.OrdinalIgnoreCase)
            && !row.Status.StartsWith("Conflicts with existing sheet number", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryCreateRules(out NumberingRules? rules, out string? error)
    {
        rules = null;

        if (!int.TryParse(startNumberInput.Text, out int startNumber))
        {
            error = "Start Number must be an integer.";
            return false;
        }

        if (!int.TryParse(incrementInput.Text, out int increment))
        {
            error = "Increment must be an integer.";
            return false;
        }

        if (!int.TryParse(paddingInput.Text, out int padding))
        {
            error = "Padding must be an integer.";
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
            error = exception.Message;
            return false;
        }

        error = null;
        return true;
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

        input.MinWidth = 90;
        input.Height = 28;
        field.Children.Add(input);

        Grid.SetColumn(field, column);
        grid.Children.Add(field);
    }

    private static DataGridTextColumn CreateTextColumn(string header, string bindingPath, double width)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(bindingPath),
            IsReadOnly = true,
            Width = width
        };
    }

    private static Button CreateActionButton(string text, bool isEnabled)
    {
        return new Button
        {
            Content = text,
            MinWidth = 96,
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = isEnabled
        };
    }

    private enum PreviewOrder
    {
        Original,
        CurrentNumber,
        Name
    }

    private sealed class PreviewRow : INotifyPropertyChanged
    {
        private readonly Action selectionChanged;
        private bool isSelected;
        private string previewNumber;
        private string status;

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
