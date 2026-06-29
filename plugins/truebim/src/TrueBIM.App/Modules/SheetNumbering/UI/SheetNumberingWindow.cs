using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TrueBIM.App.Modules.SheetNumbering.Models;
using TrueBIM.App.Modules.SheetNumbering.Rules;
using TrueBIM.App.Modules.SheetNumbering.Services;

namespace TrueBIM.App.Modules.SheetNumbering.UI;

public sealed class SheetNumberingWindow : Window
{
    private readonly ObservableCollection<PreviewRow> previewRows = new();
    private readonly IReadOnlyList<SheetInfo> sheets;
    private readonly SheetNumberingPreviewWorkflow workflow;
    private readonly TextBlock statusText = new();
    private readonly TextBox prefixInput = new() { Text = "A-" };
    private readonly TextBox suffixInput = new();
    private readonly TextBox startNumberInput = new() { Text = "1" };
    private readonly TextBox incrementInput = new() { Text = "1" };
    private readonly TextBox paddingInput = new() { Text = "2" };

    public SheetNumberingWindow(
        IReadOnlyList<SheetInfo> sheets,
        SheetNumberingPreviewWorkflow workflow)
    {
        this.sheets = sheets ?? throw new ArgumentNullException(nameof(sheets));
        this.workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        Title = "Sheet Numbering";
        Width = 820;
        Height = 520;
        MinWidth = 680;
        MinHeight = 420;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();
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

    private UIElement CreatePreviewGrid()
    {
        DataGrid grid = new()
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = true,
            ItemsSource = previewRows,
            HeadersVisibility = DataGridHeadersVisibility.Column
        };

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

        actions.Children.Add(CreateActionButton("Apply", isEnabled: false));

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
        statusText.Text = sheets.Count == 0
            ? "No sheets found in the active document."
            : $"Loaded {sheets.Count} sheets from the active document.";

        return statusText;
    }

    private void LoadCurrentSheets()
    {
        previewRows.Clear();

        foreach (SheetInfo sheet in sheets)
        {
            previewRows.Add(new PreviewRow(
                sheet.CurrentNumber,
                string.Empty,
                sheet.Name,
                sheet.IsPlaceholder ? "Placeholder" : "Ready"));
        }
    }

    private void GeneratePreview()
    {
        previewRows.Clear();

        if (sheets.Count == 0)
        {
            statusText.Text = "No sheets found in the active document.";
            return;
        }

        if (!TryCreateRules(out NumberingRules? rules, out string? error))
        {
            statusText.Text = error ?? "Invalid numbering rules.";
            previewRows.Add(new PreviewRow(string.Empty, string.Empty, "Rule validation", error ?? "Invalid numbering rules."));
            return;
        }

        NumberingRules validRules = rules ?? throw new InvalidOperationException("Numbering rules were not created.");

        try
        {
            SheetNumberingPreviewResult result = workflow.GeneratePreview(
                new SheetNumberingPreviewRequest(sheets, sheets, validRules));
            IReadOnlyDictionary<long, string> issuesBySheetId = CreateIssueLookup(result.DuplicateIssues);

            foreach (SheetNumberPreview preview in result.Previews)
            {
                previewRows.Add(new PreviewRow(
                    preview.Sheet.CurrentNumber,
                    preview.PreviewNumber,
                    preview.Sheet.Name,
                    GetStatus(preview, issuesBySheetId)));
            }

            statusText.Text = result.HasBlockingIssues
                ? "Preview generated with duplicate sheet number issues. Apply is disabled."
                : "Preview generated. Apply is disabled until the write operation is implemented.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            statusText.Text = exception.Message;
            previewRows.Add(new PreviewRow(string.Empty, string.Empty, "Preview error", exception.Message));
        }
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
            return preview.IsChanged ? "Placeholder preview" : "Placeholder unchanged";
        }

        return preview.IsChanged ? "Preview" : "Unchanged";
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

    private sealed record PreviewRow(
        string CurrentNumber,
        string PreviewNumber,
        string Name,
        string Status);
}
