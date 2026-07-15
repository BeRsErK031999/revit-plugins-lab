using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.Lintels.Models;
using TrueBIM.App.Modules.Lintels.Revit;
using TrueBIM.App.Modules.Lintels.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;
using WpfBinding = System.Windows.Data.Binding;

namespace TrueBIM.App.Modules.Lintels.UI;

public sealed class LintelsWindow : TrueBimWindow
{
    private const string DialogTitle = "Перемычки";

    private readonly UIDocument uiDocument;
    private readonly LintelDiagnosticCollectorService collectorService;
    private readonly LintelAssemblyPreflightService preflightService;
    private readonly LintelAssemblyCreationService creationService;
    private readonly ITrueBimLogger logger;
    private readonly RevitActionDispatcher revitActions;
    private readonly ObservableCollection<LintelTypeSelectionItem> typeItems = [];
    private readonly DataGrid typeGrid = new();
    private readonly ContentControl summaryHost = new();
    private readonly StackPanel previewContent = new();
    private readonly TextBlock footerStatusText = new();
    private readonly Button refreshButton;
    private readonly Button preflightButton;
    private readonly Button createButton;
    private LintelDiagnosticResult currentResult;
    private LintelTypeDiagnostic? approvedCreationType;
    private LintelAssemblyPreflightItem? approvedCreationPreflight;
    private bool isBulkSelectionUpdate;

    public LintelsWindow(
        UIDocument uiDocument,
        LintelDiagnosticCollectorService collectorService,
        LintelDiagnosticResult initialResult,
        ITrueBimLogger logger)
    {
        this.uiDocument = uiDocument ?? throw new ArgumentNullException(nameof(uiDocument));
        this.collectorService = collectorService ?? throw new ArgumentNullException(nameof(collectorService));
        currentResult = initialResult ?? throw new ArgumentNullException(nameof(initialResult));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        preflightService = new LintelAssemblyPreflightService(this.logger);
        creationService = new LintelAssemblyCreationService(preflightService, this.logger);
        revitActions = new RevitActionDispatcher("действия окна перемычек", this.logger);

        refreshButton = TrueBimUi.CreateSecondaryButton(
            "Обновить из Revit",
            TrueBimIcon.Refresh,
            (_, _) => RefreshFromRevit(),
            minWidth: 160);
        refreshButton.ToolTip = "Повторно прочитать текущее выделение или активный вид в безопасном Revit-контексте.";

        preflightButton = TrueBimUi.CreateSecondaryButton(
            "Проверить сборки",
            TrueBimIcon.Search,
            (_, _) => RequestAssemblyPreflight(),
            minWidth: 160);
        preflightButton.ToolTip = "Проверить выбранные составы через Revit API без транзакции и изменений модели.";
        ToolTipService.SetShowOnDisabled(preflightButton, true);

        createButton = TrueBimUi.CreatePrimaryButton(
            "Создать одну сборку",
            TrueBimIcon.Apply,
            (_, _) => RequestAssemblyCreation(),
            isEnabled: false,
            minWidth: 185);
        ToolTipService.SetShowOnDisabled(createButton, true);
        UpdateCreateButtonState();

        Title = DialogTitle;
        Icon = IconFactory.CreateImage(TrueBimIcon.Lintels, TrueBimTheme.IconSizeRibbon);
        Width = 1180;
        Height = 720;
        MinWidth = 1080;
        MinHeight = 620;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        ApplyTrueBimShell(
            TrueBimUi.CreateHeader(
                DialogTitle,
                "Проверьте будущие имена и состав. Для первого запуска оставьте один типоразмер и выполните preflight.",
                TrueBimIcon.Lintels),
            CreateCommandBar(),
            CreateBody(),
            footer: CreateFooter());

        ApplyResult(initialResult);
        logger.Info($"Lintels selection window opened for '{uiDocument.Document.Title}'.");
    }

    private UIElement CreateCommandBar()
    {
        Button selectReadyButton = TrueBimUi.CreateSecondaryButton(
            "Выбрать готовые",
            TrueBimIcon.Check,
            (_, _) => SetReadySelection(true),
            minWidth: 145);
        selectReadyButton.ToolTip = "Отметить типоразмеры, у которых найдены вложенные проектные компоненты с геометрией.";

        Button clearSelectionButton = TrueBimUi.CreateSecondaryButton(
            "Снять выбор",
            TrueBimIcon.Close,
            (_, _) => SetReadySelection(false),
            minWidth: 125);
        clearSelectionButton.ToolTip = "Снять отметки со всех типоразмеров.";

        Button diagnosticsButton = TrueBimUi.CreateSecondaryButton(
            "Диагностика",
            TrueBimIcon.Info,
            (_, _) => ShowDiagnostics(),
            minWidth: 130);
        diagnosticsButton.ToolTip = "Показать причины исключения элементов и подробности временного правила поиска.";

        return TrueBimUi.CreateCommandBar(
            selectReadyButton,
            clearSelectionButton,
            refreshButton,
            preflightButton,
            diagnosticsButton);
    }

    private UIElement CreateBody()
    {
        Grid body = new();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        summaryHost.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        body.Children.Add(summaryHost);

        Grid columns = new();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TrueBimTheme.Spacing16) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        UIElement typesPanel = CreateStretchSection("Типоразмеры", CreateTypeGrid());
        columns.Children.Add(typesPanel);

        ScrollViewer previewScroll = new()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = previewContent
        };
        UIElement previewPanel = CreateStretchSection("Будущие артефакты", previewScroll);
        Grid.SetColumn(previewPanel, 2);
        columns.Children.Add(previewPanel);

        Grid.SetRow(columns, 1);
        body.Children.Add(columns);
        return body;
    }

    private UIElement CreateTypeGrid()
    {
        typeGrid.AutoGenerateColumns = false;
        typeGrid.CanUserAddRows = false;
        typeGrid.CanUserDeleteRows = false;
        typeGrid.CanUserReorderColumns = false;
        typeGrid.IsReadOnly = false;
        typeGrid.SelectionMode = DataGridSelectionMode.Single;
        typeGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
        typeGrid.Style = TrueBimStyles.CreateDataGridStyle();
        typeGrid.ItemsSource = typeItems;
        typeGrid.Columns.Add(CreateSelectionColumn());
        typeGrid.Columns.Add(CreateTextColumn("Семейство", nameof(LintelTypeSelectionItem.FamilyName), 135));
        typeGrid.Columns.Add(CreateTextColumn("Тип", nameof(LintelTypeSelectionItem.TypeName), 115));
        typeGrid.Columns.Add(CreateTextColumn("Экз.", nameof(LintelTypeSelectionItem.InstanceCount), 52));
        typeGrid.Columns.Add(CreateTextColumn("Статус", nameof(LintelTypeSelectionItem.ReadyStatus), 100));
        typeGrid.Columns.Add(CreateTextColumn("ID", nameof(LintelTypeSelectionItem.RepresentativeElementId), 72));
        typeGrid.Columns.Add(CreateTextColumn(
            "Диагностика",
            nameof(LintelTypeSelectionItem.DiagnosticText),
            new DataGridLength(1, DataGridLengthUnitType.Star)));
        AutomationProperties.SetName(typeGrid, "Типоразмеры перемычек");
        AutomationProperties.SetHelpText(typeGrid, "Отметьте строки со статусом «Готово», чтобы увидеть будущие имена артефактов.");
        return typeGrid;
    }

    private UIElement CreateFooter()
    {
        footerStatusText.Foreground = TrueBimBrushes.TextSecondary;
        footerStatusText.TextWrapping = TextWrapping.Wrap;
        footerStatusText.VerticalAlignment = VerticalAlignment.Center;

        Button closeButton = TrueBimUi.CreateSecondaryButton(
            "Закрыть",
            TrueBimIcon.Close,
            (_, _) => Close(),
            minWidth: 110);
        closeButton.IsCancel = true;
        closeButton.ToolTip = "Закрыть окно перемычек.";

        return TrueBimUi.CreateFooter(footerStatusText, createButton, closeButton);
    }

    private void ApplyResult(LintelDiagnosticResult result)
    {
        InvalidateAssemblyApproval();
        currentResult = result;
        typeItems.Clear();
        foreach (LintelTypeDiagnostic type in result.Types)
        {
            LintelTypeSelectionItem item = new(type);
            item.PropertyChanged += OnTypeItemPropertyChanged;
            typeItems.Add(item);
        }

        RefreshSummary();
        RefreshPreview();
        UpdateStatus();
    }

    private void RefreshSummary()
    {
        TrueBimUiSeverity severity = !currentResult.HasCandidates || currentResult.ReadyTypeCount == 0
            ? TrueBimUiSeverity.Warning
            : currentResult.ReadyTypeCount == currentResult.Types.Count
                ? TrueBimUiSeverity.Success
                : TrueBimUiSeverity.Info;
        summaryHost.Content = TrueBimUi.CreateInfoBanner(currentResult.BuildSummary(), severity);
    }

    private void RefreshPreview()
    {
        previewContent.Children.Clear();
        LintelTypeSelectionItem[] selectedItems = typeItems
            .Where(item => item.IsSelected && item.CanSelect)
            .ToArray();
        if (selectedItems.Length == 0)
        {
            previewContent.Children.Add(TrueBimUi.CreateInfoBanner(
                typeItems.Count == 0
                    ? "Нет типоразмеров для preview. Измените выделение или активный вид и нажмите «Обновить из Revit»."
                    : "Выберите хотя бы один типоразмер со статусом «Готово».",
                TrueBimUiSeverity.Warning));
            return;
        }

        foreach (LintelTypeSelectionItem item in selectedItems)
        {
            previewContent.Children.Add(CreatePreviewItem(item));
        }
    }

    private static UIElement CreatePreviewItem(LintelTypeSelectionItem item)
    {
        StackPanel content = new();
        Grid title = new();
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.Children.Add(new TextBlock
        {
            Text = $"{item.FamilyName} : {item.TypeName}",
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary,
            TextWrapping = TextWrapping.Wrap
        });
        UIElement badge = TrueBimUi.CreateStatusBadge("Готово", TrueBimUiSeverity.Success);
        Grid.SetColumn(badge, 1);
        title.Children.Add(badge);
        content.Children.Add(title);

        content.Children.Add(CreateArtifactLine("Сборка", item.ArtifactPreview.AssemblyName));
        content.Children.Add(CreateArtifactLine("Вид", item.ArtifactPreview.ViewName));
        content.Children.Add(CreateArtifactLine("Изображение", item.ArtifactPreview.ImageFileName));

        return new Border
        {
            BorderBrush = TrueBimBrushes.Border,
            BorderThickness = new Thickness(0, 0, 0, TrueBimTheme.BorderWidth),
            Padding = new Thickness(0, 0, 0, TrueBimTheme.Spacing12),
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12),
            Child = content
        };
    }

    private static UIElement CreateArtifactLine(string label, string value)
    {
        Grid row = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = TrueBimBrushes.TextMuted
        });
        TextBlock name = new()
        {
            Text = value,
            Foreground = TrueBimBrushes.TextPrimary,
            TextWrapping = TextWrapping.Wrap,
            ToolTip = value
        };
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        return row;
    }

    private static Border CreateStretchSection(string title, UIElement content)
    {
        Grid layout = new()
        {
            Margin = TrueBimTheme.SectionPadding
        };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = TrueBimTheme.SectionTitleFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12)
        });
        Grid.SetRow(content, 1);
        layout.Children.Add(content);

        return new Border
        {
            Background = TrueBimBrushes.Surface,
            BorderBrush = TrueBimBrushes.Border,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Child = layout
        };
    }

    private void SetReadySelection(bool isSelected)
    {
        InvalidateAssemblyApproval();
        isBulkSelectionUpdate = true;
        foreach (LintelTypeSelectionItem item in typeItems.Where(item => item.CanSelect))
        {
            item.IsSelected = isSelected;
        }

        isBulkSelectionUpdate = false;
        RefreshPreview();
        UpdateStatus();
    }

    private void OnTypeItemPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!isBulkSelectionUpdate && args.PropertyName == nameof(LintelTypeSelectionItem.IsSelected))
        {
            InvalidateAssemblyApproval();
            RefreshPreview();
            UpdateStatus();
        }
    }

    private void RefreshFromRevit()
    {
        InvalidateAssemblyApproval();
        footerStatusText.Text = "Обновление диагностики поставлено в очередь Revit…";
        revitActions.Raise(RefreshInRevitContext);
    }

    private void RefreshInRevitContext()
    {
        try
        {
            LintelDiagnosticResult result = collectorService.Collect(uiDocument);
            ApplyResult(result);
            logger.Info("Lintels selection preview refreshed from Revit.");
        }
        catch (Exception exception)
        {
            logger.Error("Failed to refresh Lintels selection preview.", exception);
            TaskDialog.Show(
                DialogTitle,
                "Не удалось обновить диагностику. Проверьте открытый документ и используйте логи для анализа ошибки.");
            footerStatusText.Text = "Обновление не выполнено. Модель Revit не изменялась.";
        }
    }

    private void ShowDiagnostics()
    {
        LintelDiagnosticsWindow window = new(currentResult)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void RequestAssemblyPreflight()
    {
        InvalidateAssemblyApproval();
        LintelTypeDiagnostic[] selectedTypes = GetSelectedTypes();
        if (selectedTypes.Length == 0)
        {
            footerStatusText.Text = "Для проверки выберите хотя бы один типоразмер со статусом «Готово».";
            return;
        }

        footerStatusText.Text = $"Проверка {selectedTypes.Length} типоразмеров поставлена в очередь Revit…";
        revitActions.Raise(() => RunAssemblyPreflight(selectedTypes));
    }

    private void RunAssemblyPreflight(IReadOnlyCollection<LintelTypeDiagnostic> selectedTypes)
    {
        try
        {
            LintelAssemblyPreflightResult result = preflightService.Inspect(
                uiDocument.Document,
                selectedTypes);
            Dispatcher.BeginInvoke(new Action(() => ShowAssemblyPreflightResult(result, selectedTypes)));
        }
        catch (Exception exception)
        {
            logger.Error("Failed to run Lintels assembly preflight.", exception);
            TaskDialog.Show(
                DialogTitle,
                "Не удалось проверить будущие сборки. Модель Revit не изменялась; подробности записаны в лог.");
            footerStatusText.Text = "Проверка сборок не выполнена. Модель Revit не изменялась.";
        }
    }

    private void ShowAssemblyPreflightResult(
        LintelAssemblyPreflightResult result,
        IReadOnlyCollection<LintelTypeDiagnostic> checkedTypes)
    {
        if (!IsVisible)
        {
            return;
        }

        LintelTypeDiagnostic[] currentSelection = GetSelectedTypes();
        bool selectionIsCurrent = LintelAssemblyCreationGate.IsCurrentSelection(
            checkedTypes.Select(type => type.TypeId).ToArray(),
            currentSelection.Select(type => type.TypeId).ToArray());
        LintelAssemblyPreflightItem? singleReadyItem = result.Items.Count == 1
            && result.Items[0].Status == LintelAssemblyPreflightStatus.Ready
                ? result.Items[0]
                : null;
        if (selectionIsCurrent && checkedTypes.Count == 1 && singleReadyItem is not null)
        {
            approvedCreationType = checkedTypes.Single();
            approvedCreationPreflight = singleReadyItem;
        }

        UpdateCreateButtonState();
        footerStatusText.Text = createButton.IsEnabled
            ? $"Preflight успешен для «{singleReadyItem!.AssemblyName}». Можно создать одну сборку; вид и оформление пока не создаются."
            : result.ReadyCount > 0 && checkedTypes.Count > 1
                ? "Preflight завершён. Для атомарного создания оставьте выбранным один готовый типоразмер и повторите проверку."
                : $"Preflight завершён: готово {result.ReadyCount}, существует {result.ExistingCount}, заблокировано {result.BlockedCount}. Модель Revit не изменялась.";
        LintelAssemblyPreflightWindow window = new(result)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void RequestAssemblyCreation()
    {
        if (!createButton.IsEnabled
            || approvedCreationType is null
            || approvedCreationPreflight is null)
        {
            footerStatusText.Text = "Перед созданием выберите один типоразмер и выполните успешный preflight.";
            return;
        }

        TaskDialog confirmation = new(DialogTitle)
        {
            MainInstruction = "Создать одну сборку перемычки?",
            MainContent = $"Сборка: {approvedCreationPreflight.AssemblyName}{Environment.NewLine}Компонентов: {approvedCreationPreflight.MemberCount}{Environment.NewLine}{Environment.NewLine}Будет создана только Assembly. Вид, размеры, марки и изображение на этом этапе не создаются.",
            ExpandedContent = $"ElementId компонентов:{Environment.NewLine}{approvedCreationPreflight.MemberIdsDisplay}",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No
        };
        if (confirmation.Show() != TaskDialogResult.Yes)
        {
            return;
        }

        LintelTypeDiagnostic selectedType = approvedCreationType;
        InvalidateAssemblyApproval();
        footerStatusText.Text = "Создание одной сборки поставлено в очередь Revit…";
        revitActions.Raise(() => RunAssemblyCreation(selectedType));
    }

    private void RunAssemblyCreation(LintelTypeDiagnostic selectedType)
    {
        try
        {
            LintelAssemblyCreationResult result = creationService.CreateOne(
                uiDocument.Document,
                selectedType);
            LintelDiagnosticResult refreshedDiagnostic = collectorService.Collect(uiDocument);
            Dispatcher.BeginInvoke(new Action(() => ShowAssemblyCreationResult(
                result,
                refreshedDiagnostic)));
        }
        catch (Exception exception)
        {
            logger.Error("Failed to execute Lintels assembly creation.", exception);
            TaskDialog.Show(
                DialogTitle,
                "Не удалось выполнить создание сборки. Операция отменена; подробности записаны в лог.");
            footerStatusText.Text = "Создание сборки не выполнено.";
        }
    }

    private void ShowAssemblyCreationResult(
        LintelAssemblyCreationResult result,
        LintelDiagnosticResult refreshedDiagnostic)
    {
        if (IsVisible)
        {
            ApplyResult(refreshedDiagnostic);
            footerStatusText.Text = result.ModelChanged
                ? $"Создана сборка «{result.AssemblyName}». Следующий этап — боковой вид 1:10."
                : result.Message;
        }

        TaskDialog dialog = new(DialogTitle)
        {
            MainInstruction = result.Status switch
            {
                LintelAssemblyCreationStatus.Created => "Сборка создана",
                LintelAssemblyCreationStatus.AlreadyExists => "Сборка уже существует",
                LintelAssemblyCreationStatus.Blocked => "Создание заблокировано",
                _ => "Создание не выполнено"
            },
            MainContent = result.BuildSummary(),
            CommonButtons = TaskDialogCommonButtons.Close
        };
        dialog.Show();
    }

    private LintelTypeDiagnostic[] GetSelectedTypes()
    {
        return typeItems
            .Where(item => item.IsSelected && item.CanSelect)
            .Select(item => item.Diagnostic)
            .ToArray();
    }

    private void InvalidateAssemblyApproval()
    {
        approvedCreationType = null;
        approvedCreationPreflight = null;
        UpdateCreateButtonState();
    }

    private void UpdateCreateButtonState()
    {
        long[] selectedTypeIds = typeItems
            .Where(item => item.IsSelected && item.CanSelect)
            .Select(item => item.Diagnostic.TypeId)
            .ToArray();
        bool canCreate = LintelAssemblyCreationGate.CanCreate(
            approvedCreationType?.TypeId,
            approvedCreationPreflight?.Status,
            selectedTypeIds);
        createButton.IsEnabled = canCreate;
        string explanation = canCreate
            ? $"Создать одну Assembly «{approvedCreationPreflight!.AssemblyName}» из подтверждённого preflight-состава."
            : "Сначала оставьте один готовый типоразмер и выполните успешный preflight.";
        createButton.ToolTip = explanation;
        AutomationProperties.SetHelpText(createButton, explanation);
    }

    private void UpdateStatus()
    {
        int readyCount = typeItems.Count(item => item.CanSelect);
        int selectedCount = typeItems.Count(item => item.IsSelected && item.CanSelect);
        preflightButton.IsEnabled = selectedCount > 0;
        UpdateCreateButtonState();
        AutomationProperties.SetHelpText(
            preflightButton,
            selectedCount > 0
                ? $"Проверить через Revit API выбранные типоразмеры: {selectedCount}."
                : "Сначала выберите хотя бы один готовый типоразмер.");
        string source = currentResult.Source == LintelDiagnosticSource.Selection
            ? "выделение"
            : "активный вид";
        footerStatusText.Text = $"Источник: {source}. Типоразмеров: {typeItems.Count}. Готово: {readyCount}. Выбрано: {selectedCount}. Предпросмотр только для чтения — модель Revit не изменяется.";
    }

    private static DataGridTextColumn CreateTextColumn(
        string header,
        string bindingPath,
        double width)
    {
        return CreateTextColumn(header, bindingPath, new DataGridLength(width));
    }

    private static DataGridTextColumn CreateTextColumn(
        string header,
        string bindingPath,
        DataGridLength width)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new WpfBinding(bindingPath),
            Width = width,
            IsReadOnly = true
        };
    }

    private static DataGridTemplateColumn CreateSelectionColumn()
    {
        FrameworkElementFactory checkBox = new(typeof(CheckBox));
        checkBox.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        checkBox.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        checkBox.SetValue(FrameworkElement.StyleProperty, TrueBimStyles.CreateCheckBoxStyle());
        checkBox.SetBinding(
            CheckBox.IsCheckedProperty,
            new WpfBinding(nameof(LintelTypeSelectionItem.IsSelected))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        checkBox.SetBinding(
            UIElement.IsEnabledProperty,
            new WpfBinding(nameof(LintelTypeSelectionItem.CanSelect)));
        checkBox.SetBinding(
            FrameworkElement.ToolTipProperty,
            new WpfBinding(nameof(LintelTypeSelectionItem.DiagnosticText)));

        return new DataGridTemplateColumn
        {
            Header = "Выбор",
            CellTemplate = new DataTemplate { VisualTree = checkBox },
            Width = 58
        };
    }
}
