using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Xml;
using Autodesk.Revit.DB;
using Microsoft.Win32;
using TrueBIM.App.Modules.BimTools.ClashReport.Models;
using TrueBIM.App.Modules.BimTools.ClashReport.Services;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using RevitDocument = Autodesk.Revit.DB.Document;
using RevitUIDocument = Autodesk.Revit.UI.UIDocument;
using WpfBinding = System.Windows.Data.Binding;
using WpfGrid = System.Windows.Controls.Grid;

namespace TrueBIM.App.Modules.BimTools.ClashReport.UI;

public sealed class ClashReportWindow : TrueBimWindow
{
    private static readonly IReadOnlyList<ClashStatus> StatusOptions =
        Enum.GetValues(typeof(ClashStatus)).Cast<ClashStatus>().ToList();

    private readonly RevitUIDocument uiDocument;
    private readonly RevitDocument document;
    private readonly ClashReportFileImportService importService;
    private readonly ClashElementResolver elementResolver;
    private readonly ClashViewNavigator viewNavigator;
    private readonly ClashReportStorage storage;
    private readonly ITrueBimLogger logger;
    private readonly RevitActionDispatcher revitActions;
    private readonly ObservableCollection<ClashItem> clashRows = new();
    private readonly ObservableCollection<ClashReportRow> reportRows = new();
    private readonly TextBox profileNameInput = new();
    private readonly TextBox filterInput = new();
    private readonly CheckBox highlightInput = new()
    {
        Content = "Подсветка в 3D",
        IsChecked = true,
        ToolTip = "При переходе в 3D подсвечивать найденные элементы коллизии служебными overrides."
    };
    private readonly TextBlock fileText = new();
    private readonly DataGrid clashGrid = new();
    private readonly DataGrid reportGrid = new();
    private readonly TextBlock statusText = new();
    private readonly Button selectButton = CreateButton("Выбрать", TrueBimIcon.Apply, 105);
    private readonly Button navigateButton = CreateButton("Показать в 3D", TrueBimIcon.ClashReport, 140);
    private readonly Button saveStateButton = CreateButton("Сохранить", TrueBimIcon.Apply, 110);
    private readonly string modelKey;
    private string currentImportPath = string.Empty;

    public ClashReportWindow(
        RevitUIDocument uiDocument,
        ClashReportFileImportService importService,
        ClashElementResolver elementResolver,
        ClashViewNavigator viewNavigator,
        ClashReportStorage storage,
        ITrueBimLogger logger)
    {
        this.uiDocument = uiDocument ?? throw new ArgumentNullException(nameof(uiDocument));
        document = uiDocument.Document;
        this.importService = importService ?? throw new ArgumentNullException(nameof(importService));
        this.elementResolver = elementResolver ?? throw new ArgumentNullException(nameof(elementResolver));
        this.viewNavigator = viewNavigator ?? throw new ArgumentNullException(nameof(viewNavigator));
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        revitActions = new RevitActionDispatcher("отчёт коллизий", this.logger);
        modelKey = ClashReportStorage.BuildModelKey(document);

        ApplyProfile(storage.LoadProfile());

        Title = "Отчёт коллизий";
        Icon = IconFactory.CreateImage(TrueBimIcon.ClashReport, 32);
        Width = 1180;
        Height = 720;
        MinWidth = 980;
        MinHeight = 600;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();

        UpdateStatus("Добавьте CSV или XML файл коллизий.");
        logger.Info($"Clash Report import window opened for '{document.Title}'.");
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveState(showDialog: false);
        base.OnClosed(e);
    }

    private void ApplyProfile(ClashReportProfile profile)
    {
        profileNameInput.Text = profile.Name;
        currentImportPath = profile.LastImportPath;
        highlightInput.IsChecked = profile.HighlightOnNavigate;
        UpdateFileText();
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
            Header = "Коллизии",
            Content = CreateClashPanel()
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Журнал",
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
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddLabel(root, $"Модель: {document.Title}", 0, 0);
        profileNameInput.Height = 32;
        profileNameInput.Margin = new Thickness(8, 0, 12, 8);
        profileNameInput.ToolTip = "Имя локального набора импортированных коллизий.";
        WpfGrid.SetColumn(profileNameInput, 1);
        root.Children.Add(profileNameInput);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Button importButton = CreateButton("Добавить файл", TrueBimIcon.Open, 135);
        importButton.Click += (_, _) => ImportFile();
        actions.Children.Add(importButton);

        Button refreshButton = CreateButton("Проверить", TrueBimIcon.Preview, 115);
        refreshButton.Click += (_, _) => ResolveRows();
        actions.Children.Add(refreshButton);

        selectButton.Click += (_, _) => SelectSelectedClashElements(showDialogWhenMissing: true);
        actions.Children.Add(selectButton);

        navigateButton.Click += (_, _) => NavigateToSelectedClash();
        actions.Children.Add(navigateButton);

        saveStateButton.Click += (_, _) => SaveState(showDialog: true);
        actions.Children.Add(saveStateButton);

        Button closeButton = CreateButton("Закрыть", TrueBimIcon.Close, 110);
        closeButton.IsCancel = true;
        closeButton.Click += (_, _) => Close();
        actions.Children.Add(closeButton);

        WpfGrid.SetColumn(actions, 2);
        root.Children.Add(actions);

        AddLabel(root, "Файл", 0, 1);
        fileText.VerticalAlignment = VerticalAlignment.Center;
        fileText.Margin = new Thickness(8, 0, 12, 8);
        fileText.Foreground = System.Windows.Media.Brushes.DimGray;
        fileText.TextWrapping = TextWrapping.Wrap;
        WpfGrid.SetRow(fileText, 1);
        WpfGrid.SetColumn(fileText, 1);
        root.Children.Add(fileText);

        AddLabel(root, "Навигация", 0, 2);
        StackPanel options = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        highlightInput.Margin = new Thickness(8, 0, 18, 8);
        highlightInput.VerticalAlignment = VerticalAlignment.Center;
        options.Children.Add(highlightInput);

        WpfGrid.SetRow(options, 2);
        WpfGrid.SetColumn(options, 1);
        root.Children.Add(options);

        return root;
    }

    private UIElement CreateClashPanel()
    {
        DockPanel panel = new();

        StackPanel filterPanel = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        filterPanel.Children.Add(CreateOptionLabel("Фильтр"));
        filterInput.Width = 360;
        filterInput.Height = 30;
        filterInput.Margin = new Thickness(8, 0, 12, 0);
        filterInput.ToolTip = "Фильтр по имени, ElementId, статусу, сообщению или комментарию.";
        filterInput.TextChanged += (_, _) => RefreshFilter();
        filterPanel.Children.Add(filterInput);

        DockPanel.SetDock(filterPanel, Dock.Top);
        panel.Children.Add(filterPanel);

        panel.Children.Add(CreateClashGrid());
        return panel;
    }

    private DataGrid CreateClashGrid()
    {
        clashGrid.AutoGenerateColumns = false;
        clashGrid.CanUserAddRows = false;
        clashGrid.CanUserDeleteRows = false;
        clashGrid.IsReadOnly = false;
        clashGrid.SelectionMode = DataGridSelectionMode.Single;
        clashGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
        clashGrid.ItemsSource = clashRows;
        clashGrid.Columns.Add(CreateTextColumn("Источник", nameof(ClashItem.Source), 145));
        clashGrid.Columns.Add(CreateTextColumn("ID", nameof(ClashItem.ClashId), 110));
        clashGrid.Columns.Add(CreateTextColumn("Имя", nameof(ClashItem.Name), new DataGridLength(1, DataGridLengthUnitType.Star)));
        clashGrid.Columns.Add(CreateTextColumn("ElementId 1", nameof(ClashItem.ElementId1Text), 105));
        clashGrid.Columns.Add(CreateTextColumn("Element 1", nameof(ClashItem.Element1Name), 190));
        clashGrid.Columns.Add(CreateTextColumn("ElementId 2", nameof(ClashItem.ElementId2Text), 105));
        clashGrid.Columns.Add(CreateTextColumn("Element 2", nameof(ClashItem.Element2Name), 190));
        clashGrid.Columns.Add(CreateTextColumn("Точка", nameof(ClashItem.PointText), 150));
        clashGrid.Columns.Add(CreateStatusColumn());
        clashGrid.Columns.Add(CreateEditableTextColumn("Комментарий", nameof(ClashItem.Comment), 220));
        clashGrid.Columns.Add(CreateTextColumn("Resolve", nameof(ClashItem.ResolvedDisplay), 90));
        clashGrid.Columns.Add(CreateTextColumn("Сообщение", nameof(ClashItem.Message), 260));
        clashGrid.MouseDoubleClick += (_, _) => NavigateToSelectedClash();

        ICollectionView view = CollectionViewSource.GetDefaultView(clashGrid.ItemsSource);
        view.Filter = MatchesFilter;

        return clashGrid;
    }

    private DataGrid CreateReportGrid()
    {
        reportGrid.AutoGenerateColumns = false;
        reportGrid.CanUserAddRows = false;
        reportGrid.CanUserDeleteRows = false;
        reportGrid.IsReadOnly = true;
        reportGrid.ItemsSource = reportRows;
        reportGrid.Columns.Add(CreateTextColumn("Операция", nameof(ClashReportRow.Operation), 120));
        reportGrid.Columns.Add(CreateTextColumn("ID", nameof(ClashReportRow.ClashId), 110));
        reportGrid.Columns.Add(CreateTextColumn("Имя", nameof(ClashReportRow.ClashName), new DataGridLength(1, DataGridLengthUnitType.Star)));
        reportGrid.Columns.Add(CreateTextColumn("Статус", nameof(ClashReportRow.Status), 120));
        reportGrid.Columns.Add(CreateTextColumn("Сообщение", nameof(ClashReportRow.Message), new DataGridLength(2, DataGridLengthUnitType.Star)));
        return reportGrid;
    }

    private void ImportFile()
    {
        CommitGridEdits();
        SaveState(showDialog: false);
        OpenFileDialog dialog = new()
        {
            Title = "Добавить файл коллизий",
            Filter = "CSV/XML files (*.csv;*.xml)|*.csv;*.xml|CSV files (*.csv)|*.csv|XML files (*.xml)|*.xml|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (!string.IsNullOrWhiteSpace(currentImportPath))
        {
            string? directory = Path.GetDirectoryName(currentImportPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
            }
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string importPath = dialog.FileName;
        UpdateStatus("Импорт и сопоставление элементов поставлены в очередь Revit.");
        revitActions.Raise(() => ImportFileInRevitContext(importPath));
    }

    private void ImportFileInRevitContext(string importPath)
    {
        try
        {
            ClashImportResult result = importService.Import(importPath);
            clashRows.Clear();
            reportRows.Clear();
            currentImportPath = importPath;
            UpdateFileText();

            foreach (ClashItem item in result.Items)
            {
                storage.ApplyState(modelKey, item);
                elementResolver.Resolve(document, item);
                clashRows.Add(item);
            }

            foreach (string importMessage in result.Messages)
            {
                AddReport("Импорт", string.Empty, string.Empty, "Info", importMessage);
            }

            storage.SaveProfile(ReadProfile());
            RefreshFilter();
            UpdateStatus($"Загружен файл: {Path.GetFileName(importPath)}.");
            logger.Info($"Clash Report imported {result.Items.Count} rows from '{importPath}'.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or XmlException or InvalidOperationException)
        {
            string message = $"Не удалось импортировать файл '{Path.GetFileName(importPath)}': {exception.Message}";
            AddReport("Импорт", string.Empty, string.Empty, "Error", message);
            UpdateStatus();
            logger.Warning(message);
            TaskDialog("Отчёт коллизий", message);
        }
    }

    private void ResolveRows()
    {
        UpdateStatus("Проверка элементов поставлена в очередь Revit.");
        revitActions.Raise(ResolveRowsInRevitContext);
    }

    private void ResolveRowsInRevitContext()
    {
        CommitGridEdits();
        foreach (ClashItem item in clashRows)
        {
            elementResolver.Resolve(document, item);
        }

        AddReport("Проверка", string.Empty, string.Empty, "OK", $"Обновлено строк: {clashRows.Count}.");
        UpdateStatus();
    }

    private void SelectSelectedClashElements(bool showDialogWhenMissing)
    {
        UpdateStatus("Выбор элементов поставлен в очередь Revit.");
        revitActions.Raise(() => SelectSelectedClashElementsInRevitContext(showDialogWhenMissing));
    }

    private bool SelectSelectedClashElementsInRevitContext(bool showDialogWhenMissing)
    {
        CommitGridEdits();
        if (clashGrid.SelectedItem is not ClashItem item)
        {
            if (showDialogWhenMissing)
            {
                TaskDialog("Отчёт коллизий", "Выберите строку коллизии для выбора элементов.");
            }

            return false;
        }

        elementResolver.Resolve(document, item);
        List<ElementId> elementIds = item
            .GetResolvedElementIds()
            .Distinct()
            .Select(RevitElementIds.Create)
            .ToList();
        if (elementIds.Count == 0)
        {
            string message = "Нет найденных ElementId для выбора. Нажмите «Проверить» или используйте строку с координатами для перехода в 3D.";
            AddReport("Выбор", item.ClashId, item.Name, "Error", message);
            UpdateStatus();
            if (showDialogWhenMissing)
            {
                TaskDialog("Отчёт коллизий", message);
            }

            return false;
        }

        try
        {
            uiDocument.Selection.SetElementIds(elementIds);
            string linkedNote = item.IsLinkDriven
                ? " Для RVT-связей выбирается экземпляр связи; вложенный элемент доступен в отчёте по ElementId."
                : string.Empty;
            string message = $"Выбрано элементов: {elementIds.Count}.{linkedNote}";
            AddReport("Выбор", item.ClashId, item.Name, "OK", message);
            UpdateStatus(message);
            logger.Info($"Clash '{item.ClashId}' selected {elementIds.Count} Revit elements.");
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Autodesk.Revit.Exceptions.InvalidOperationException or ArgumentException)
        {
            string message = $"Не удалось выбрать элементы: {exception.Message}";
            AddReport("Выбор", item.ClashId, item.Name, "Error", message);
            UpdateStatus();
            logger.Warning($"Failed to select clash '{item.ClashId}': {exception.Message}");
            if (showDialogWhenMissing)
            {
                TaskDialog("Отчёт коллизий", message);
            }

            return false;
        }
    }

    private void NavigateToSelectedClash()
    {
        UpdateStatus("Переход к коллизии поставлен в очередь Revit.");
        revitActions.Raise(NavigateToSelectedClashInRevitContext);
    }

    private void NavigateToSelectedClashInRevitContext()
    {
        CommitGridEdits();
        if (clashGrid.SelectedItem is not ClashItem item)
        {
            TaskDialog("Отчёт коллизий", "Выберите строку коллизии для перехода в 3D.");
            return;
        }

        elementResolver.Resolve(document, item);
        ClashReportProfile profile = ReadProfile();
        storage.SaveStates(modelKey, clashRows, profile);
        ClashNavigationResult result = viewNavigator.Focus(uiDocument, document, item, profile);
        AddReport(
            "3D",
            item.ClashId,
            item.Name,
            result.Succeeded ? "OK" : "Error",
            string.IsNullOrWhiteSpace(result.ViewName)
                ? result.Message
                : $"{result.Message} Вид: {result.ViewName}, элементов: {result.SelectedElementCount}.");

        UpdateStatus();
        if (!result.Succeeded)
        {
            TaskDialog("Отчёт коллизий", result.Message);
        }
    }

    private void SaveState(bool showDialog)
    {
        CommitGridEdits();
        storage.SaveStates(modelKey, clashRows, ReadProfile());
        if (showDialog)
        {
            TaskDialog("Отчёт коллизий", "Состояние импортированных коллизий сохранено в локальный JSON.");
        }
    }

    private ClashReportProfile ReadProfile()
    {
        return ClashReportStorage.NormalizeProfile(new ClashReportProfile
        {
            Name = profileNameInput.Text,
            LastImportPath = currentImportPath,
            HighlightOnNavigate = highlightInput.IsChecked == true
        });
    }

    private void RefreshFilter()
    {
        CollectionViewSource.GetDefaultView(clashGrid.ItemsSource)?.Refresh();
        UpdateStatus();
    }

    private bool MatchesFilter(object value)
    {
        if (value is not ClashItem item)
        {
            return false;
        }

        string filter = filterInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return Contains(item.ClashId, filter)
            || Contains(item.Source, filter)
            || Contains(item.Name, filter)
            || Contains(item.ElementId1Text, filter)
            || Contains(item.ElementId2Text, filter)
            || Contains(item.Element1Name, filter)
            || Contains(item.Element2Name, filter)
            || Contains(item.PointText, filter)
            || Contains(item.StatusDisplay, filter)
            || Contains(item.Comment, filter)
            || Contains(item.Message, filter);
    }

    private void UpdateStatus(string? prefix = null)
    {
        int resolved = clashRows.Count(item => item.GetResolvedElementIds().Count > 0);
        int open = clashRows.Count(item => item.Status == ClashStatus.Open);
        int filtered = CollectionViewSource.GetDefaultView(clashGrid.ItemsSource)?.Cast<object>().Count() ?? clashRows.Count;
        string text = $"Коллизий: {clashRows.Count}; показано: {filtered}; с найденными элементами: {resolved}; New: {open}; событий журнала: {reportRows.Count}.";
        statusText.Text = string.IsNullOrWhiteSpace(prefix) ? text : $"{prefix} {text}";
        bool hasRows = clashRows.Count > 0;
        selectButton.IsEnabled = hasRows;
        navigateButton.IsEnabled = hasRows;
        saveStateButton.IsEnabled = hasRows;
    }

    private void UpdateFileText()
    {
        fileText.Text = string.IsNullOrWhiteSpace(currentImportPath)
            ? "Файл не выбран. Поддерживаются CSV и XML."
            : currentImportPath;
    }

    private void AddReport(string operation, string clashId, string clashName, string status, string message)
    {
        reportRows.Add(new ClashReportRow(operation, clashId, clashName, status, message));
    }

    private void CommitGridEdits()
    {
        clashGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        clashGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
    }

    private static void AddLabel(WpfGrid grid, string text, int column, int row)
    {
        TextBlock label = new()
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold
        };
        WpfGrid.SetColumn(label, column);
        WpfGrid.SetRow(label, row);
        grid.Children.Add(label);
    }

    private static TextBlock CreateOptionLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
    }

    private static Button CreateButton(string text, TrueBimIcon icon, double minWidth)
    {
        Button button = new()
        {
            MinWidth = minWidth,
            Height = 32,
            Margin = new Thickness(6, 0, 0, 8),
            Padding = new Thickness(10, 0, 10, 0)
        };

        StackPanel content = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        content.Children.Add(IconFactory.Create(icon, 16));
        content.Children.Add(new TextBlock
        {
            Text = text,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        button.Content = content;
        return button;
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

    private static DataGridTextColumn CreateEditableTextColumn(string header, string bindingPath, double width)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new WpfBinding(bindingPath)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
            },
            Width = new DataGridLength(width)
        };
    }

    private static DataGridComboBoxColumn CreateStatusColumn()
    {
        return new DataGridComboBoxColumn
        {
            Header = "Статус",
            ItemsSource = StatusOptions,
            SelectedItemBinding = new WpfBinding(nameof(ClashItem.Status))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = new DataGridLength(120)
        };
    }

    private static bool Contains(string value, string filter)
    {
        return value?.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    private static void TaskDialog(string title, string message)
    {
        Autodesk.Revit.UI.TaskDialog.Show(title, message);
    }
}
