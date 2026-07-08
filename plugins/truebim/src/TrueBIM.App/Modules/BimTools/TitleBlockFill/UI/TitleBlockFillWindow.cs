using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.Common.Services.Export;
using TrueBIM.App.Modules.BimTools.TitleBlockFill.Models;
using TrueBIM.App.Modules.BimTools.TitleBlockFill.Services;
using TrueBIM.App.Modules.SheetNumbering.Models;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using WpfBinding = System.Windows.Data.Binding;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace TrueBIM.App.Modules.BimTools.TitleBlockFill.UI;

public sealed class TitleBlockFillWindow : Window
{
    private readonly Document document;
    private readonly TitleBlockProfileStorage profileStorage;
    private readonly TitleBlockFinderService titleBlockFinder;
    private readonly TitleBlockFillService fillService;
    private readonly ITrueBimLogger logger;
    private readonly CsvExportService csvExportService = new();
    private readonly TitleBlockFillReportCsvService reportCsvService = new();
    private readonly ObservableCollection<TitleBlockSheetRow> sheets = new();
    private readonly ObservableCollection<TitleBlockParameterRule> rules = new();
    private readonly ObservableCollection<TitleBlockPreviewRow> reportRows = new();
    private readonly WpfTextBox profileNameInput = new();
    private readonly WpfTextBox sheetFilterInput = new();
    private readonly ObservableCollection<TitleBlockSheetRow> visibleSheets = new();
    private readonly DataGrid sheetGrid = new();
    private readonly DataGrid ruleGrid = new();
    private readonly DataGrid reportGrid = new();
    private readonly TextBlock statusText = new();
    private readonly Button applyButton = CreateButton("Применить", TrueBimIcon.Apply, 130);
    private readonly Button exportReportButton = CreateButton("Отчёт CSV", TrueBimIcon.Export, 130);

    public TitleBlockFillWindow(
        Document document,
        IReadOnlyList<SheetInfo> sheetInfos,
        TitleBlockProfileStorage profileStorage,
        TitleBlockFinderService titleBlockFinder,
        TitleBlockFillService fillService,
        ITrueBimLogger logger)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        this.profileStorage = profileStorage ?? throw new ArgumentNullException(nameof(profileStorage));
        this.titleBlockFinder = titleBlockFinder ?? throw new ArgumentNullException(nameof(titleBlockFinder));
        this.fillService = fillService ?? throw new ArgumentNullException(nameof(fillService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        foreach (SheetInfo sheetInfo in sheetInfos ?? throw new ArgumentNullException(nameof(sheetInfos)))
        {
            TitleBlockSheetRow row = new(sheetInfo);
            row.PropertyChanged += OnSheetRowPropertyChanged;
            sheets.Add(row);
        }

        TitleBlockProfile profile = this.profileStorage.Load();
        profileNameInput.Text = profile.Name;
        foreach (TitleBlockParameterRule rule in profile.Rules)
        {
            rules.Add(rule);
        }

        Title = "Оформить штамп";
        Icon = IconFactory.CreateImage(TrueBimIcon.TitleBlock, 32);
        Width = 1180;
        Height = 760;
        MinWidth = 1020;
        MinHeight = 640;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();

        RefreshTitleBlockStatuses();
        RefreshVisibleSheets();
        UpdateStatus();
        logger.Info($"Title Block Fill window opened for '{document.Title}' with {sheets.Count} sheets.");
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveProfile();
        base.OnClosed(e);
    }

    private UIElement CreateContent()
    {
        DockPanel root = new()
        {
            Margin = new Thickness(18)
        };

        UIElement top = CreateTopPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        statusText.Margin = new Thickness(0, 10, 0, 0);
        statusText.TextWrapping = TextWrapping.Wrap;
        DockPanel.SetDock(statusText, Dock.Bottom);
        root.Children.Add(statusText);

        TabControl tabs = new();
        tabs.Items.Add(new TabItem
        {
            Header = "Листы",
            Content = CreateSheetsPanel()
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Правила",
            Content = CreateRulesPanel()
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Отчёт",
            Content = CreateReportGrid()
        });
        root.Children.Add(tabs);
        return root;
    }

    private UIElement CreateTopPanel()
    {
        WpfGrid root = new()
        {
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBlock label = new()
        {
            Text = "Профиль",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        root.Children.Add(label);

        profileNameInput.Height = 32;
        profileNameInput.Margin = new Thickness(0, 0, 12, 0);
        WpfGrid.SetColumn(profileNameInput, 1);
        root.Children.Add(profileNameInput);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Button previewButton = CreateButton("Предпросмотр", TrueBimIcon.Preview, 150);
        previewButton.Click += (_, _) => Preview();
        actions.Children.Add(previewButton);

        applyButton.Click += (_, _) => Apply();
        actions.Children.Add(applyButton);

        exportReportButton.IsEnabled = false;
        exportReportButton.Click += (_, _) => ExportReport();
        actions.Children.Add(exportReportButton);

        Button closeButton = CreateButton("Закрыть", TrueBimIcon.Close, 110);
        closeButton.IsCancel = true;
        closeButton.Click += (_, _) => Close();
        actions.Children.Add(closeButton);

        WpfGrid.SetColumn(actions, 2);
        root.Children.Add(actions);
        return root;
    }

    private UIElement CreateSheetsPanel()
    {
        DockPanel panel = new();

        DockPanel filterBar = new()
        {
            LastChildFill = true,
            Margin = new Thickness(0, 0, 0, 8)
        };
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal
        };
        Button selectAllButton = CreateButton("Выбрать все", TrueBimIcon.Apply, 130);
        selectAllButton.Click += (_, _) => SetVisibleSheetsSelected(true);
        actions.Children.Add(selectAllButton);

        Button clearButton = CreateButton("Снять выбор", TrueBimIcon.Close, 130);
        clearButton.Click += (_, _) => SetVisibleSheetsSelected(false);
        actions.Children.Add(clearButton);

        Button refreshButton = CreateButton("Обновить", TrueBimIcon.Preview, 120);
        refreshButton.Click += (_, _) =>
        {
            RefreshTitleBlockStatuses();
            RefreshVisibleSheets();
        };
        actions.Children.Add(refreshButton);
        DockPanel.SetDock(actions, Dock.Right);
        filterBar.Children.Add(actions);

        sheetFilterInput.Height = 32;
        sheetFilterInput.Margin = new Thickness(0, 0, 8, 0);
        sheetFilterInput.ToolTip = "Фильтр по номеру или имени листа.";
        sheetFilterInput.TextChanged += (_, _) => RefreshVisibleSheets();
        filterBar.Children.Add(sheetFilterInput);
        DockPanel.SetDock(filterBar, Dock.Top);
        panel.Children.Add(filterBar);

        sheetGrid.AutoGenerateColumns = false;
        sheetGrid.CanUserAddRows = false;
        sheetGrid.CanUserDeleteRows = false;
        sheetGrid.IsReadOnly = false;
        sheetGrid.ItemsSource = visibleSheets;
        sheetGrid.Columns.Add(CreateSelectionColumn(nameof(TitleBlockSheetRow.IsSelected)));
        sheetGrid.Columns.Add(CreateTextColumn("Номер", nameof(TitleBlockSheetRow.SheetNumber), 120));
        sheetGrid.Columns.Add(CreateTextColumn("Имя листа", nameof(TitleBlockSheetRow.SheetName), new DataGridLength(1, DataGridLengthUnitType.Star)));
        sheetGrid.Columns.Add(CreateTextColumn("Тип", nameof(TitleBlockSheetRow.PlaceholderStatus), 100));
        sheetGrid.Columns.Add(CreateTextColumn("Штамп", nameof(TitleBlockSheetRow.TitleBlockStatus), 180));
        sheetGrid.Columns.Add(CreateTextColumn("Предпросмотр", nameof(TitleBlockSheetRow.PreviewStatus), 170));
        sheetGrid.Columns.Add(CreateTextColumn("Применение", nameof(TitleBlockSheetRow.ApplyStatus), 170));
        panel.Children.Add(sheetGrid);
        return panel;
    }

    private UIElement CreateRulesPanel()
    {
        DockPanel panel = new();

        StackPanel toolbar = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Button addButton = CreateButton("Добавить", TrueBimIcon.Apply, 120);
        addButton.Click += (_, _) => AddRule();
        toolbar.Children.Add(addButton);

        Button removeButton = CreateButton("Удалить", TrueBimIcon.Close, 120);
        removeButton.Click += (_, _) => RemoveSelectedRule();
        toolbar.Children.Add(removeButton);

        Button saveButton = CreateButton("Сохранить", TrueBimIcon.Export, 120);
        saveButton.Click += (_, _) =>
        {
            SaveProfile();
            UpdateStatus("Профиль сохранён.");
        };
        toolbar.Children.Add(saveButton);
        DockPanel.SetDock(toolbar, Dock.Top);
        panel.Children.Add(toolbar);

        ruleGrid.AutoGenerateColumns = false;
        ruleGrid.CanUserAddRows = false;
        ruleGrid.CanUserDeleteRows = false;
        ruleGrid.IsReadOnly = false;
        ruleGrid.ItemsSource = rules;
        ruleGrid.Columns.Add(CreateSelectionColumn(nameof(TitleBlockParameterRule.IsEnabled), "Вкл."));
        ruleGrid.Columns.Add(CreateComboColumn("Куда", nameof(TitleBlockParameterRule.Target), TitleBlockRuleTargets.All, 120));
        ruleGrid.Columns.Add(CreateEditableTextColumn("Параметр", nameof(TitleBlockParameterRule.ParameterName), new DataGridLength(1, DataGridLengthUnitType.Star)));
        ruleGrid.Columns.Add(CreateComboColumn("Источник", nameof(TitleBlockParameterRule.Source), TitleBlockValueSources.All, 170));
        ruleGrid.Columns.Add(CreateEditableTextColumn("Значение / имя параметра / формат даты", nameof(TitleBlockParameterRule.Value), new DataGridLength(260)));
        panel.Children.Add(ruleGrid);
        return panel;
    }

    private DataGrid CreateReportGrid()
    {
        reportGrid.AutoGenerateColumns = false;
        reportGrid.CanUserAddRows = false;
        reportGrid.CanUserDeleteRows = false;
        reportGrid.IsReadOnly = true;
        reportGrid.ItemsSource = reportRows;
        reportGrid.Columns.Add(CreateTextColumn("Лист", nameof(TitleBlockPreviewRow.SheetNumber), 100));
        reportGrid.Columns.Add(CreateTextColumn("Имя", nameof(TitleBlockPreviewRow.SheetName), new DataGridLength(1, DataGridLengthUnitType.Star)));
        reportGrid.Columns.Add(CreateTextColumn("Куда", nameof(TitleBlockPreviewRow.Target), 90));
        reportGrid.Columns.Add(CreateTextColumn("Параметр", nameof(TitleBlockPreviewRow.ParameterName), 160));
        reportGrid.Columns.Add(CreateTextColumn("Сейчас", nameof(TitleBlockPreviewRow.CurrentValue), 160));
        reportGrid.Columns.Add(CreateTextColumn("Будет", nameof(TitleBlockPreviewRow.NewValue), 160));
        reportGrid.Columns.Add(CreateTextColumn("Статус", nameof(TitleBlockPreviewRow.Status), 120));
        reportGrid.Columns.Add(CreateTextColumn("Сообщение", nameof(TitleBlockPreviewRow.Message), 220));
        return reportGrid;
    }

    private void RefreshTitleBlockStatuses()
    {
        foreach (TitleBlockSheetRow row in sheets)
        {
            if (row.IsPlaceholder)
            {
                row.TitleBlockStatus = "Заглушка";
                continue;
            }

            try
            {
                if (document.GetElement(TrueBIM.App.Services.RevitElementIds.Create(row.ElementId)) is ViewSheet sheet)
                {
                    row.TitleBlockStatus = titleBlockFinder.GetDisplayStatus(document, sheet);
                }
            }
            catch (Exception exception)
            {
                logger.Warning($"Failed to read title block status for sheet '{row.SheetNumber}': {exception.Message}");
                row.TitleBlockStatus = "Ошибка";
            }
        }
    }

    private void RefreshVisibleSheets()
    {
        string filter = sheetFilterInput.Text.Trim();
        IEnumerable<TitleBlockSheetRow> rows = sheets;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            rows = rows.Where(row =>
                row.SheetNumber.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
                || row.SheetName.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        visibleSheets.Clear();
        foreach (TitleBlockSheetRow row in rows)
        {
            visibleSheets.Add(row);
        }

        UpdateStatus();
    }

    private void SetVisibleSheetsSelected(bool isSelected)
    {
        foreach (TitleBlockSheetRow row in visibleSheets)
        {
            row.IsSelected = isSelected;
        }

        UpdateStatus();
    }

    private void AddRule()
    {
        rules.Add(new TitleBlockParameterRule());
        SaveProfile();
        UpdateStatus();
    }

    private void RemoveSelectedRule()
    {
        if (ruleGrid.SelectedItem is not TitleBlockParameterRule rule)
        {
            UpdateStatus("Выберите правило для удаления.");
            return;
        }

        rules.Remove(rule);
        if (rules.Count == 0)
        {
            rules.Add(new TitleBlockParameterRule());
        }

        SaveProfile();
        UpdateStatus();
    }

    private void Preview()
    {
        SaveProfile();
        reportRows.Clear();
        foreach (TitleBlockSheetRow sheet in sheets)
        {
            sheet.PreviewStatus = string.Empty;
            sheet.ApplyStatus = string.Empty;
        }

        IReadOnlyList<TitleBlockPreviewRow> rows = fillService.Preview(
            document,
            sheets.ToList(),
            rules.ToList(),
            titleBlockFinder);
        foreach (TitleBlockPreviewRow row in rows)
        {
            reportRows.Add(row);
        }

        ApplyPreviewStatus(rows);
        exportReportButton.IsEnabled = reportRows.Count > 0;
        UpdateStatus($"Предпросмотр: {rows.Count} строк.");
    }

    private void Apply()
    {
        SaveProfile();
        if (sheets.Count(row => row.IsSelected && !row.IsPlaceholder) == 0)
        {
            Autodesk.Revit.UI.TaskDialog.Show("Оформить штамп", "Выберите хотя бы один лист.");
            return;
        }

        if (rules.Count(rule => rule.IsEnabled && !string.IsNullOrWhiteSpace(rule.ParameterName)) == 0)
        {
            Autodesk.Revit.UI.TaskDialog.Show("Оформить штамп", "Добавьте хотя бы одно включённое правило с именем параметра.");
            return;
        }

        MessageBoxResult decision = MessageBox.Show(
            this,
            "Записать значения по включённым правилам в выбранные листы и штампы?",
            "Оформить штамп",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (TitleBlockSheetRow sheet in sheets)
        {
            sheet.ApplyStatus = string.Empty;
        }

        TitleBlockApplyResult result = fillService.Apply(
            document,
            sheets.ToList(),
            rules.ToList(),
            titleBlockFinder,
            logger);
        reportRows.Clear();
        foreach (TitleBlockPreviewRow row in result.Rows)
        {
            reportRows.Add(row);
        }

        ApplyResultStatus(result.Rows);
        exportReportButton.IsEnabled = reportRows.Count > 0;
        Autodesk.Revit.UI.TaskDialog.Show(
            "Оформить штамп",
            $"Записано: {result.AppliedCount}\nПропущено: {result.SkippedCount}\nОшибок: {result.FailedCount}");
        UpdateStatus($"Записано: {result.AppliedCount}. Пропущено: {result.SkippedCount}. Ошибок: {result.FailedCount}.");
    }

    private void ExportReport()
    {
        if (reportRows.Count == 0)
        {
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "Сохранить отчёт оформления штампа",
            Filter = "CSV UTF-8 (*.csv)|*.csv",
            FileName = "title-block-fill-report.csv"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        csvExportService.WriteUtf8WithBom(dialog.FileName, reportCsvService.Format(reportRows.ToList()));
        UpdateStatus($"Отчёт сохранён: {dialog.FileName}");
    }

    private void ApplyPreviewStatus(IReadOnlyList<TitleBlockPreviewRow> rows)
    {
        foreach (IGrouping<long, TitleBlockPreviewRow> group in rows.GroupBy(row => row.SheetElementId))
        {
            TitleBlockSheetRow? sheet = sheets.FirstOrDefault(row => row.ElementId == group.Key);
            if (sheet is null)
            {
                continue;
            }

            int writable = group.Count(row => row.CanApply);
            int skipped = group.Count(row => !row.CanApply);
            sheet.PreviewStatus = $"К записи: {writable}. Пропуск: {skipped}.";
        }
    }

    private void ApplyResultStatus(IReadOnlyList<TitleBlockPreviewRow> rows)
    {
        foreach (IGrouping<long, TitleBlockPreviewRow> group in rows.GroupBy(row => row.SheetElementId))
        {
            TitleBlockSheetRow? sheet = sheets.FirstOrDefault(row => row.ElementId == group.Key);
            if (sheet is null)
            {
                continue;
            }

            int applied = group.Count(row => row.Status == "Готово");
            int failed = group.Count(row => row.Status == "Ошибка");
            int skipped = group.Count(row => row.Status == "Пропущено");
            sheet.ApplyStatus = $"Готово: {applied}. Пропуск: {skipped}. Ошибок: {failed}.";
        }
    }

    private void SaveProfile()
    {
        profileStorage.Save(new TitleBlockProfile
        {
            Name = profileNameInput.Text,
            Rules = rules.ToList()
        });
    }

    private void OnSheetRowPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(TitleBlockSheetRow.IsSelected))
        {
            UpdateStatus();
        }
    }

    private void UpdateStatus(string? prefix = null)
    {
        int selected = sheets.Count(row => row.IsSelected && !row.IsPlaceholder);
        int sheetCount = sheets.Count(row => !row.IsPlaceholder);
        int ruleCount = rules.Count(rule => rule.IsEnabled);
        string text = $"Листов: {sheetCount}. Показано: {visibleSheets.Count}. Выбрано: {selected}. Включённых правил: {ruleCount}. Отчётных строк: {reportRows.Count}.";
        statusText.Text = string.IsNullOrWhiteSpace(prefix) ? text : $"{prefix} {text}";
        applyButton.IsEnabled = selected > 0 && ruleCount > 0;
    }

    private static Button CreateButton(string text, TrueBimIcon icon, double minWidth)
    {
        return new Button
        {
            Content = IconFactory.CreateButtonContent(icon, text),
            MinWidth = minWidth,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0)
        };
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
            Binding = new WpfBinding(bindingPath),
            Width = width,
            IsReadOnly = true
        };
    }

    private static DataGridTextColumn CreateEditableTextColumn(string header, string bindingPath, DataGridLength width)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new WpfBinding(bindingPath)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = width
        };
    }

    private static DataGridComboBoxColumn CreateComboColumn(
        string header,
        string bindingPath,
        IEnumerable<string> values,
        double width)
    {
        return new DataGridComboBoxColumn
        {
            Header = header,
            ItemsSource = values,
            SelectedItemBinding = new WpfBinding(bindingPath)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = width
        };
    }

    private static DataGridTemplateColumn CreateSelectionColumn(string bindingPath, string header = "Выбран")
    {
        FrameworkElementFactory checkBox = new(typeof(CheckBox));
        checkBox.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        checkBox.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        checkBox.SetBinding(
            CheckBox.IsCheckedProperty,
            new WpfBinding(bindingPath)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });

        return new DataGridTemplateColumn
        {
            Header = header,
            CellTemplate = new DataTemplate
            {
                VisualTree = checkBox
            },
            Width = 78
        };
    }
}
