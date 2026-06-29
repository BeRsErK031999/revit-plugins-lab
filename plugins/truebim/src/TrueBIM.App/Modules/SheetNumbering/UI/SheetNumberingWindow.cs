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
    private readonly TextBox prefixInput = new() { Text = "A-" };
    private readonly TextBox suffixInput = new();
    private readonly TextBox startNumberInput = new() { Text = "1" };
    private readonly TextBox incrementInput = new() { Text = "1" };
    private readonly TextBox paddingInput = new() { Text = "2" };

    public SheetNumberingWindow()
    {
        Title = "Sheet Numbering";
        Width = 820;
        Height = 520;
        MinWidth = 680;
        MinHeight = 420;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();
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
        previewButton.Click += (_, _) => GenerateDemoPreview();
        actions.Children.Add(previewButton);

        actions.Children.Add(CreateActionButton("Apply", isEnabled: false));

        Button closeButton = CreateActionButton("Close", isEnabled: true);
        closeButton.IsCancel = true;
        closeButton.Click += (_, _) => Close();
        actions.Children.Add(closeButton);

        return actions;
    }

    private void GenerateDemoPreview()
    {
        previewRows.Clear();

        if (!TryCreateRules(out NumberingRules? rules, out string? error))
        {
            previewRows.Add(new PreviewRow(string.Empty, string.Empty, "Rule validation", error ?? "Invalid numbering rules."));
            return;
        }

        SheetInfo[] selectedSheets =
        [
            new(1001, "G000", "Cover", false),
            new(1002, "A101", "Level 1 Plan", false),
            new(1003, "A102", "Level 2 Plan", false)
        ];
        NumberingRules validRules = rules ?? throw new InvalidOperationException("Numbering rules were not created.");

        SheetNumberingPreviewWorkflow workflow = new(
            new SheetNumberPreviewService(),
            new DuplicateSheetNumberDetector());
        SheetNumberingPreviewResult result = workflow.GeneratePreview(
            new SheetNumberingPreviewRequest(selectedSheets, selectedSheets, validRules));

        foreach (SheetNumberPreview preview in result.Previews)
        {
            previewRows.Add(new PreviewRow(
                preview.Sheet.CurrentNumber,
                preview.PreviewNumber,
                preview.Sheet.Name,
                preview.IsChanged ? "Preview" : "Unchanged"));
        }
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
