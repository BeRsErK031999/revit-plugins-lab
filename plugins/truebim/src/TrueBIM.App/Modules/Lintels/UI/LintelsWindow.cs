using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using TrueBIM.App.Modules.Lintels.Models;
using TrueBIM.App.Modules.Lintels.Revit;
using TrueBIM.App.Modules.Lintels.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;
using WpfBinding = System.Windows.Data.Binding;
using AssemblyInstance = Autodesk.Revit.DB.AssemblyInstance;
using ElementId = Autodesk.Revit.DB.ElementId;
using View = Autodesk.Revit.DB.View;
using ViewSection = Autodesk.Revit.DB.ViewSection;

namespace TrueBIM.App.Modules.Lintels.UI;

public sealed class LintelsWindow : TrueBimWindow
{
    private const string DialogTitle = "Перемычки";
    private const string DefaultFrameFamilyFilePath =
        @"Y:\01 - REVIT\03 - FAMILIES 2017\Аннотации\•• (Аннотация) Рамка аннотации.rfa";

    private readonly UIDocument uiDocument;
    private readonly LintelDiagnosticCollectorService collectorService;
    private readonly LintelWizardSourceMode sourceMode;
    private readonly LintelAssemblyPreflightService preflightService;
    private readonly LintelAssemblyCreationService creationService;
    private readonly LintelAssemblyViewCreationService viewCreationService;
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
    private readonly Button createViewButton;
    private readonly Button frameFamilyButton;
    private readonly TextBlock frameFamilyStatusText = new();
    private LintelDiagnosticResult currentResult;
    private IReadOnlyList<LintelPreparedViewRequest> preparedViewRequests = [];
    private string? selectedFrameFamilyPath;
    private bool isBulkSelectionUpdate;

    public LintelsWindow(
        UIDocument uiDocument,
        LintelDiagnosticCollectorService collectorService,
        LintelWizardSourceMode sourceMode,
        LintelDiagnosticResult initialResult,
        ITrueBimLogger logger)
    {
        this.uiDocument = uiDocument ?? throw new ArgumentNullException(nameof(uiDocument));
        this.collectorService = collectorService ?? throw new ArgumentNullException(nameof(collectorService));
        this.sourceMode = sourceMode;
        currentResult = initialResult ?? throw new ArgumentNullException(nameof(initialResult));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        preflightService = new LintelAssemblyPreflightService(this.logger);
        creationService = new LintelAssemblyCreationService(preflightService, this.logger);
        viewCreationService = new LintelAssemblyViewCreationService(this.logger);
        revitActions = new RevitActionDispatcher("действия окна перемычек", this.logger);

        refreshButton = TrueBimUi.CreateSecondaryButton(
            "Обновить из Revit",
            TrueBimIcon.Refresh,
            (_, _) => RefreshFromRevit(),
            minWidth: 160);
        refreshButton.ToolTip = "Повторно прочитать текущее выделение или активный вид в безопасном Revit-контексте.";

        preflightButton = TrueBimUi.CreateSecondaryButton(
            "Проверить выбранные",
            TrueBimIcon.Search,
            (_, _) => RequestAssemblyPreflight(),
            minWidth: 190);
        preflightButton.ToolTip = "Проверить состав всех отмеченных типоразмеров без создания элементов и изменения модели.";
        ToolTipService.SetShowOnDisabled(preflightButton, true);

        createButton = TrueBimUi.CreatePrimaryButton(
            "Шаг 3: создать сборки",
            TrueBimIcon.Apply,
            (_, _) => RequestAssemblyCreation(),
            isEnabled: false,
            minWidth: 205);
        ToolTipService.SetShowOnDisabled(createButton, true);
        UpdateCreateButtonState();

        frameFamilyButton = TrueBimUi.CreateSecondaryButton(
            "Шаг 4: выбрать рамку .rfa",
            TrueBimIcon.FamilyManager,
            (_, _) => SelectFrameFamily(),
            minWidth: 210);
        frameFamilyButton.ToolTip = "Выбрать загружаемое семейство Revit, которым TrueBIM разместит рамку на каждом создаваемом виде.";

        selectedFrameFamilyPath = File.Exists(DefaultFrameFamilyFilePath)
            ? DefaultFrameFamilyFilePath
            : null;
        createViewButton = TrueBimUi.CreateSecondaryButton(
            "Шаг 4: создать виды 1:10",
            TrueBimIcon.OpeningViews,
            (_, _) => RequestAssemblyViewCreation(),
            isEnabled: false,
            minWidth: 225);
        createViewButton.ToolTip = "Сначала создайте сборки для отмеченных типоразмеров и выберите файл семейства рамки .rfa.";
        ToolTipService.SetShowOnDisabled(createViewButton, true);

        frameFamilyStatusText.Text = selectedFrameFamilyPath is null
            ? "Рамка не найдена автоматически. На шаге 4 укажите семейство «Типовая аннотация» (.rfa)."
            : $"Рамка найдена в библиотеке: {Path.GetFileName(selectedFrameFamilyPath)}. При необходимости выберите другой файл.";
        frameFamilyStatusText.Foreground = selectedFrameFamilyPath is null
            ? TrueBimBrushes.TextSecondary
            : TrueBimBrushes.TextPrimary;
        frameFamilyStatusText.TextWrapping = TextWrapping.Wrap;

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
                $"Шаги 2–4 из 4. Источник: {LintelWizardSourceCatalog.GetTitle(sourceMode)}. Можно отметить несколько типоразмеров и обработать их одним запуском.",
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
            frameFamilyButton,
            diagnosticsButton);
    }

    private UIElement CreateBody()
    {
        Grid body = new();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Border workflowGuide = TrueBimUi.CreateInfoBanner(
            "Как пройти дальше: 2 — отметьте один или несколько типоразмеров; 3 — нажмите «Создать сборки»; 4 — выберите .rfa рамки и нажмите «Создать виды 1:10». Выбор сохраняется между шагами.");
        workflowGuide.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        body.Children.Add(workflowGuide);

        summaryHost.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        Grid.SetRow(summaryHost, 1);
        body.Children.Add(summaryHost);

        Grid columns = new();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TrueBimTheme.Spacing16) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        UIElement typesPanel = CreateStretchSection("Шаг 2. Выберите типоразмеры", CreateTypeGrid());
        columns.Children.Add(typesPanel);

        ScrollViewer previewScroll = new()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = previewContent
        };
        Grid previewPanelContent = new();
        previewPanelContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewPanelContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        frameFamilyStatusText.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        previewPanelContent.Children.Add(frameFamilyStatusText);
        Grid.SetRow(previewScroll, 1);
        previewPanelContent.Children.Add(previewScroll);
        UIElement previewPanel = CreateStretchSection("Что создаст TrueBIM", previewPanelContent);
        Grid.SetColumn(previewPanel, 2);
        columns.Children.Add(previewPanel);

        Grid.SetRow(columns, 2);
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
        AutomationProperties.SetHelpText(typeGrid, "Отметьте один или несколько типоразмеров со статусом «Готово» или «Сборка уже есть».");
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

        return TrueBimUi.CreateFooter(footerStatusText, createButton, createViewButton, closeButton);
    }

    private void ApplyResult(
        LintelDiagnosticResult result,
        IReadOnlyCollection<long>? selectedTypeIds = null)
    {
        UpdateCreateButtonState();
        InvalidateViewCreationRequest();
        currentResult = result;
        HashSet<long> preservedSelection = selectedTypeIds?.ToHashSet() ?? [];
        isBulkSelectionUpdate = true;
        try
        {
            typeItems.Clear();
            foreach (LintelTypeDiagnostic type in result.Types)
            {
                LintelTypeSelectionItem item = new(type);
                item.PropertyChanged += OnTypeItemPropertyChanged;
                typeItems.Add(item);
                if (preservedSelection.Contains(type.TypeId))
                {
                    item.IsSelected = true;
                }
            }
        }
        finally
        {
            isBulkSelectionUpdate = false;
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
                    ? "Типоразмеры не найдены. Измените источник в Revit и нажмите «Обновить из Revit»."
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
        UIElement badge = TrueBimUi.CreateStatusBadge(
            item.Diagnostic.HasExistingAssembly ? "Сборка уже есть" : "Будет создано",
            TrueBimUiSeverity.Success);
        Grid.SetColumn(badge, 1);
        title.Children.Add(badge);
        content.Children.Add(title);

        content.Children.Add(CreateArtifactLine(
            item.Diagnostic.HasExistingAssembly ? "Сборка TrueBIM" : "Новая сборка",
            item.ArtifactPreview.AssemblyName));
        content.Children.Add(CreateArtifactLine("Боковой вид 1:10", item.ArtifactPreview.ViewName));
        content.Children.Add(CreateArtifactLine(
            "PNG → «Изображение типоразмера»",
            item.ArtifactPreview.ImageFileName));

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
        UpdateCreateButtonState();
        InvalidateViewCreationRequest();
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
            UpdateCreateButtonState();
            InvalidateViewCreationRequest();
            RefreshPreview();
            UpdateStatus();
        }
    }

    private void RefreshFromRevit()
    {
        UpdateCreateButtonState();
        InvalidateViewCreationRequest();
        footerStatusText.Text = "Обновление диагностики поставлено в очередь Revit…";
        revitActions.Raise(RefreshInRevitContext);
    }

    private void RefreshInRevitContext()
    {
        try
        {
            LintelDiagnosticResult result = collectorService.Collect(uiDocument, sourceMode);
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
        UpdateCreateButtonState();
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

        UpdateCreateButtonState();
        footerStatusText.Text =
            $"Проверка завершена: можно создать {result.ReadyCount}, уже существует {result.ExistingCount}, заблокировано {result.BlockedCount}. Модель Revit не изменялась.";
        LintelAssemblyPreflightWindow window = new(result)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void RequestAssemblyCreation()
    {
        LintelTypeDiagnostic[] selectedTypes = GetSelectedTypes();
        if (!createButton.IsEnabled || selectedTypes.Length == 0)
        {
            footerStatusText.Text = "Для шага 3 отметьте хотя бы один типоразмер со статусом «Готово» или «Сборка уже есть».";
            return;
        }

        UpdateCreateButtonState();
        footerStatusText.Text = $"Проверяю состав отмеченных типоразмеров: {selectedTypes.Length}…";
        logger.Info(
            $"Lintels batch creation requested; automatic preflight queued for {selectedTypes.Length} type(s).");
        revitActions.Raise(() => RunAssemblyPreflightForCreation(selectedTypes));
    }

    private void RunAssemblyPreflightForCreation(
        IReadOnlyCollection<LintelTypeDiagnostic> selectedTypes)
    {
        try
        {
            LintelAssemblyPreflightResult result = preflightService.Inspect(
                uiDocument.Document,
                selectedTypes);
            Dispatcher.BeginInvoke(new Action(() => ContinueAssemblyCreationAfterPreflight(
                selectedTypes,
                result)));
        }
        catch (Exception exception)
        {
            logger.Error("Failed to run automatic Lintels assembly preflight.", exception);
            TaskDialog.Show(
                DialogTitle,
                "Не удалось проверить состав сборки. Модель Revit не изменялась; подробности записаны в лог.");
            footerStatusText.Text = "Автоматическая проверка не выполнена. Модель Revit не изменялась.";
        }
    }

    private void ContinueAssemblyCreationAfterPreflight(
        IReadOnlyCollection<LintelTypeDiagnostic> checkedTypes,
        LintelAssemblyPreflightResult result)
    {
        if (!IsVisible)
        {
            return;
        }

        LintelTypeDiagnostic[] currentSelection = GetSelectedTypes();
        bool selectionIsCurrent = LintelAssemblyCreationGate.IsCurrentSelection(
            checkedTypes.Select(type => type.TypeId).ToArray(),
            currentSelection.Select(type => type.TypeId).ToArray());
        if (!selectionIsCurrent)
        {
            footerStatusText.Text = "Выбор изменился во время проверки. Нажмите «Шаг 3: создать сборки» ещё раз.";
            UpdateCreateButtonState();
            return;
        }

        int executableCount = result.ReadyCount + result.ExistingCount;
        if (executableCount == 0)
        {
            footerStatusText.Text =
                $"Все отмеченные типоразмеры заблокированы проверкой ({result.BlockedCount}). Модель Revit не изменялась.";
            LintelAssemblyPreflightWindow window = new(result)
            {
                Owner = this
            };
            window.ShowDialog();
            return;
        }

        footerStatusText.Text =
            $"Проверка завершена: создать {result.ReadyCount}, уже существуют {result.ExistingCount}, пропустить {result.BlockedCount}.";
        ConfirmAndQueueAssemblyCreation(checkedTypes, result);
    }

    private void ConfirmAndQueueAssemblyCreation(
        IReadOnlyCollection<LintelTypeDiagnostic> checkedTypes,
        LintelAssemblyPreflightResult preflight)
    {
        TaskDialog confirmation = new(DialogTitle)
        {
            MainInstruction = $"Выполнить шаг 3 для {checkedTypes.Count} типоразмеров?",
            MainContent =
                $"Новых сборок: {preflight.ReadyCount}{Environment.NewLine}"
                + $"Уже существуют: {preflight.ExistingCount}{Environment.NewLine}"
                + $"Будут пропущены: {preflight.BlockedCount}{Environment.NewLine}{Environment.NewLine}"
                + "На этом шаге создаются только Assembly. Виды и рамки создаются отдельной кнопкой шага 4.",
            ExpandedContent = string.Join(
                Environment.NewLine,
                preflight.Items.Select(item =>
                    $"• {item.AssemblyName}: {ResolvePreflightStatus(item.Status)}")),
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No
        };
        if (confirmation.Show() != TaskDialogResult.Yes)
        {
            return;
        }

        UpdateCreateButtonState();
        footerStatusText.Text = $"Создание сборок поставлено в очередь Revit: {checkedTypes.Count}.";
        revitActions.Raise(() => RunAssemblyCreation(checkedTypes));
    }

    private void RunAssemblyCreation(
        IReadOnlyCollection<LintelTypeDiagnostic> selectedTypes)
    {
        List<LintelAssemblyCreationResult> results = [];
        try
        {
            foreach (LintelTypeDiagnostic selectedType in selectedTypes)
            {
                try
                {
                    results.Add(creationService.CreateOne(
                        uiDocument.Document,
                        selectedType));
                }
                catch (Exception exception)
                {
                    LintelArtifactPreview artifact = LintelArtifactNameBuilder.Build(selectedType);
                    logger.Error(
                        $"Failed to create Lintels assembly '{artifact.AssemblyName}' inside batch.",
                        exception);
                    results.Add(new LintelAssemblyCreationResult(
                        LintelAssemblyCreationStatus.Failed,
                        artifact.AssemblyName,
                        null,
                        "Неожиданная ошибка этой строки; остальные отмеченные типоразмеры продолжили обработку."));
                }
            }

            LintelDiagnosticResult refreshedDiagnostic = collectorService.Collect(
                uiDocument,
                sourceMode);
            Dispatcher.BeginInvoke(new Action(() => ShowAssemblyCreationResults(
                results,
                refreshedDiagnostic,
                selectedTypes.Select(type => type.TypeId).ToArray())));
        }
        catch (Exception exception)
        {
            logger.Error("Failed to execute Lintels assembly creation.", exception);
            TaskDialog.Show(
                DialogTitle,
                "Не удалось завершить пакетное создание сборок. Результаты уже обработанных типоразмеров сохранены; подробности записаны в лог.");
            footerStatusText.Text = "Пакетное создание сборок завершилось с ошибкой.";
        }
    }

    private void ShowAssemblyCreationResults(
        IReadOnlyCollection<LintelAssemblyCreationResult> results,
        LintelDiagnosticResult refreshedDiagnostic,
        IReadOnlyCollection<long> selectedTypeIds)
    {
        if (IsVisible)
        {
            ApplyResult(refreshedDiagnostic, selectedTypeIds);
            int successfulCount = results.Count(result => result.Status is
                LintelAssemblyCreationStatus.Created or LintelAssemblyCreationStatus.AlreadyExists);
            footerStatusText.Text =
                $"Шаг 3 завершён: готовы сборки {successfulCount}/{results.Count}. Выбор сохранён; выберите .rfa рамки и переходите к шагу 4.";
        }

        TaskDialog dialog = new(DialogTitle)
        {
            MainInstruction = "Шаг 3 завершён",
            MainContent = BuildAssemblyCreationBatchOverview(results),
            ExpandedContent = BuildAssemblyCreationBatchDetails(results),
            CommonButtons = TaskDialogCommonButtons.Close
        };
        dialog.Show();
    }

    private void PrepareAssemblyViewRequests(
        IReadOnlyCollection<LintelTypeDiagnostic> selectedTypes)
    {
        preparedViewRequests = selectedTypes
            .Where(type => type.HasExistingAssembly)
            .Select(type =>
            {
                LintelArtifactPreview artifact = LintelArtifactNameBuilder.Build(type);
                return new LintelPreparedViewRequest(
                    type.TypeId,
                    type.ExistingAssemblyName!,
                    artifact.ViewName);
            })
            .ToArray();
        UpdatePreparedViewButtonState();
    }

    private bool TryPrepareActiveAssemblyViewRequest()
    {
        View activeView = uiDocument.ActiveView;
        if (activeView is not ViewSection
            || activeView.AssociatedAssemblyInstanceId == ElementId.InvalidElementId
            || uiDocument.Document.GetElement(activeView.AssociatedAssemblyInstanceId) is not AssemblyInstance assembly
            || !LintelArtifactNameBuilder.IsTrueBimLintelArtifactName(assembly.AssemblyTypeName))
        {
            return false;
        }

        preparedViewRequests =
        [
            new LintelPreparedViewRequest(
                null,
                assembly.AssemblyTypeName,
                activeView.Name)
        ];
        UpdatePreparedViewButtonState();
        return true;
    }

    private void InvalidateViewCreationRequest()
    {
        preparedViewRequests = [];
        createViewButton.IsEnabled = false;
        string explanation = "Сначала создайте сборки для отмеченных типоразмеров или откройте боковой вид Assembly, созданный TrueBIM.";
        createViewButton.ToolTip = explanation;
        AutomationProperties.SetHelpText(createViewButton, explanation);
    }

    private void UpdateViewCreationButtonState()
    {
        LintelTypeDiagnostic[] selectedTypes = GetSelectedTypes();
        if (LintelAssemblyCreationGate.CanCreateOrFormatViews(selectedTypes))
        {
            PrepareAssemblyViewRequests(selectedTypes);
            return;
        }

        if (selectedTypes.Length == 0 && TryPrepareActiveAssemblyViewRequest())
        {
            return;
        }

        preparedViewRequests = [];
        createViewButton.IsEnabled = false;
        string explanation = selectedTypes.Length switch
        {
            0 => "Отметьте типоразмеры с уже созданными сборками. После шага 3 отмеченные строки сохранятся.",
            _ => "Не для всех отмеченных типоразмеров создана сборка. Сначала выполните шаг 3."
        };
        createViewButton.ToolTip = explanation;
        AutomationProperties.SetHelpText(createViewButton, explanation);
    }

    private void UpdatePreparedViewButtonState()
    {
        bool hasRequests = preparedViewRequests.Count > 0;
        bool hasFrameFamily = !string.IsNullOrWhiteSpace(selectedFrameFamilyPath);
        createViewButton.IsEnabled = hasRequests && hasFrameFamily;
        string explanation = !hasRequests
            ? "Сначала создайте сборки для отмеченных типоразмеров."
            : !hasFrameFamily
                ? $"Сборки готовы: {preparedViewRequests.Count}. Теперь нажмите «Шаг 4: выбрать рамку .rfa»."
                : $"Создать или повторно оформить боковые виды 1:10: {preparedViewRequests.Count}. Рамка: {Path.GetFileName(selectedFrameFamilyPath)}.";
        createViewButton.ToolTip = explanation;
        AutomationProperties.SetHelpText(createViewButton, explanation);
    }

    private void SelectFrameFamily()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Выбрать семейство рамки для видов перемычек",
            Filter = "Семейства Revit (*.rfa)|*.rfa",
            CheckFileExists = true,
            Multiselect = false,
            FileName = selectedFrameFamilyPath
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        selectedFrameFamilyPath = dialog.FileName;
        frameFamilyStatusText.Text =
            $"Рамка: {Path.GetFileName(selectedFrameFamilyPath)}. TrueBIM загрузит это семейство в проект и разместит один экземпляр по центру каждого вида.";
        frameFamilyStatusText.Foreground = TrueBimBrushes.TextPrimary;
        UpdatePreparedViewButtonState();
        footerStatusText.Text =
            $"Семейство рамки выбрано: {selectedFrameFamilyPath}. Если сборки готовы, можно выполнить шаг 4.";
    }

    private void RequestAssemblyViewCreation()
    {
        if (!createViewButton.IsEnabled
            || preparedViewRequests.Count == 0
            || string.IsNullOrWhiteSpace(selectedFrameFamilyPath))
        {
            footerStatusText.Text = "Для шага 4 нужны готовые сборки и выбранный файл семейства рамки .rfa.";
            return;
        }

        TaskDialog confirmation = new(DialogTitle)
        {
            MainInstruction = $"Выполнить шаг 4 для {preparedViewRequests.Count} сборок?",
            MainContent =
                $"Семейство рамки: {Path.GetFileName(selectedFrameFamilyPath)}{Environment.NewLine}"
                + $"Видов: {preparedViewRequests.Count}{Environment.NewLine}{Environment.NewLine}"
                + "TrueBIM создаст или переиспользует боковые виды 1:10, нанесёт габаритный размер и отметку «отм.» без выноски по нижней грани, разместит семейство рамки, экспортирует оформленный вид в PNG и назначит PNG параметру типа «Изображение типоразмера».",
            ExpandedContent = string.Join(
                Environment.NewLine,
                preparedViewRequests.Select(request =>
                    $"• {request.AssemblyName} → {request.ViewName}")),
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No
        };
        if (confirmation.Show() != TaskDialogResult.Yes)
        {
            return;
        }

        LintelPreparedViewRequest[] requests = preparedViewRequests.ToArray();
        string frameFamilyPath = selectedFrameFamilyPath!;
        footerStatusText.Text = $"Создание или оформление видов поставлено в очередь Revit: {requests.Length}.";
        revitActions.Raise(() => RunAssemblyViewCreation(requests, frameFamilyPath));
    }

    private void RunAssemblyViewCreation(
        IReadOnlyCollection<LintelPreparedViewRequest> requests,
        string frameFamilyPath)
    {
        List<LintelAssemblyViewCreationResult> results = [];
        try
        {
            foreach (LintelPreparedViewRequest request in requests)
            {
                try
                {
                    results.Add(viewCreationService.CreateOne(
                        uiDocument.Document,
                        request.AssemblyName,
                        request.ViewName,
                        frameFamilyPath,
                        request.TypeId));
                }
                catch (Exception exception)
                {
                    logger.Error(
                        $"Failed to create Lintels view '{request.ViewName}' inside batch.",
                        exception);
                    results.Add(new LintelAssemblyViewCreationResult(
                        LintelAssemblyViewCreationStatus.Failed,
                        request.AssemblyName,
                        request.ViewName,
                        null,
                        "Неожиданная ошибка этой строки; остальные отмеченные сборки продолжили обработку."));
                }
            }

            LintelDiagnosticResult refreshedDiagnostic = collectorService.Collect(
                uiDocument,
                sourceMode);
            Dispatcher.BeginInvoke(new Action(() => ShowAssemblyViewCreationResults(
                results,
                refreshedDiagnostic,
                requests
                    .Where(request => request.TypeId is not null)
                    .Select(request => request.TypeId!.Value)
                    .ToArray())));
        }
        catch (Exception exception)
        {
            logger.Error("Failed to execute Lintels side assembly view creation or formatting.", exception);
            TaskDialog.Show(
                DialogTitle,
                "Не удалось завершить пакетное создание видов. Уже обработанные виды сохранены; подробности записаны в лог.");
            footerStatusText.Text = "Пакетное создание или оформление видов завершилось с ошибкой.";
        }
    }

    private void ShowAssemblyViewCreationResults(
        IReadOnlyCollection<LintelAssemblyViewCreationResult> results,
        LintelDiagnosticResult refreshedDiagnostic,
        IReadOnlyCollection<long> selectedTypeIds)
    {
        if (IsVisible)
        {
            ApplyResult(refreshedDiagnostic, selectedTypeIds);
            int successfulCount = results.Count(result => result.Status is
                LintelAssemblyViewCreationStatus.Created or LintelAssemblyViewCreationStatus.AlreadyExists);
            footerStatusText.Text =
                $"Шаг 4 завершён: обработано видов {successfulCount}/{results.Count}. Для каждого вида выполнена попытка экспорта PNG и назначения «Изображения типоразмера».";
        }

        TaskDialog dialog = new(DialogTitle)
        {
            MainInstruction = "Шаг 4 завершён",
            MainContent = BuildViewCreationBatchOverview(results),
            ExpandedContent = BuildViewCreationBatchDetails(results),
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

    private void UpdateCreateButtonState()
    {
        LintelTypeDiagnostic[] selectedTypes = GetSelectedTypes();
        long[] selectedTypeIds = selectedTypes
            .Select(type => type.TypeId)
            .ToArray();
        bool canStart = LintelAssemblyCreationGate.CanStart(selectedTypeIds);
        createButton.IsEnabled = canStart;
        int existingCount = selectedTypes.Count(type => type.HasExistingAssembly);
        string explanation = selectedTypes.Length == 0
            ? "Отметьте один или несколько типоразмеров со статусом «Готово» или «Сборка уже есть»."
            : $"Проверить и обработать отмеченные типоразмеры: {selectedTypes.Length}. Уже имеют сборку: {existingCount}; для остальных TrueBIM создаст Assembly.";
        createButton.ToolTip = explanation;
        AutomationProperties.SetHelpText(createButton, explanation);
    }

    private void UpdateStatus()
    {
        int readyCount = typeItems.Count(item => item.CanSelect);
        int selectedCount = typeItems.Count(item => item.IsSelected && item.CanSelect);
        int selectedExistingCount = GetSelectedTypes().Count(type => type.HasExistingAssembly);
        preflightButton.IsEnabled = selectedCount > 0;
        UpdateCreateButtonState();
        UpdateViewCreationButtonState();
        string preflightExplanation = selectedCount > 0
            ? $"Проверить через Revit API выбранные типоразмеры: {selectedCount}."
            : "Сначала выберите хотя бы один готовый типоразмер.";
        preflightButton.ToolTip = preflightExplanation;
        AutomationProperties.SetHelpText(preflightButton, preflightExplanation);
        string source = currentResult.Source switch
        {
            LintelDiagnosticSource.Selection => "выделение",
            LintelDiagnosticSource.ActiveView => "активный вид",
            LintelDiagnosticSource.ExistingItems => "результаты TrueBIM",
            _ => "неизвестный источник"
        };
        footerStatusText.Text = selectedCount == 0
            ? $"Источник: {source}. Типоразмеров: {typeItems.Count}. Готово к обработке: {readyCount}. Выполните шаг 2 — отметьте нужные строки."
            : selectedExistingCount == selectedCount
                ? selectedFrameFamilyPath is null
                    ? $"Источник: {source}. Отмечено: {selectedCount}; у всех уже есть сборки TrueBIM. Выберите рамку .rfa и переходите к шагу 4."
                    : $"Источник: {source}. Отмечено: {selectedCount}; у всех уже есть сборки TrueBIM. Рамка выбрана — кнопка шага 4 доступна."
                : $"Источник: {source}. Отмечено: {selectedCount}; со сборкой TrueBIM: {selectedExistingCount}. Следующее действие — шаг 3 «Создать сборки».";
    }

    private static string ResolvePreflightStatus(LintelAssemblyPreflightStatus status)
    {
        return status switch
        {
            LintelAssemblyPreflightStatus.Ready => "будет создана",
            LintelAssemblyPreflightStatus.AlreadyExists => "уже существует",
            LintelAssemblyPreflightStatus.Blocked => "будет пропущена",
            _ => status.ToString()
        };
    }

    private static string BuildAssemblyCreationBatchOverview(
        IReadOnlyCollection<LintelAssemblyCreationResult> results)
    {
        int created = results.Count(result => result.Status == LintelAssemblyCreationStatus.Created);
        int existing = results.Count(result => result.Status == LintelAssemblyCreationStatus.AlreadyExists);
        int skipped = results.Count - created - existing;
        return $"Создано сборок: {created}; уже существовали: {existing}; не создано: {skipped}. Откройте подробности, чтобы увидеть результат каждой строки.";
    }

    private static string BuildAssemblyCreationBatchDetails(
        IReadOnlyCollection<LintelAssemblyCreationResult> results)
    {
        return string.Join(Environment.NewLine, results.Select(result =>
            $"• {result.AssemblyName}: {ResolveAssemblyCreationStatus(result.Status)}"));
    }

    private static string ResolveAssemblyCreationStatus(LintelAssemblyCreationStatus status)
    {
        return status switch
        {
            LintelAssemblyCreationStatus.Created => "создана",
            LintelAssemblyCreationStatus.AlreadyExists => "уже существует",
            LintelAssemblyCreationStatus.Blocked => "заблокирована",
            _ => "ошибка"
        };
    }

    private static string BuildViewCreationBatchOverview(
        IReadOnlyCollection<LintelAssemblyViewCreationResult> results)
    {
        int created = results.Count(result => result.Status == LintelAssemblyViewCreationStatus.Created);
        int updated = results.Count(result =>
            result.Status == LintelAssemblyViewCreationStatus.AlreadyExists
            && result.ModelChanged);
        int unchanged = results.Count(result =>
            result.Status == LintelAssemblyViewCreationStatus.AlreadyExists
            && !result.ModelChanged);
        int failed = results.Count - created - updated - unchanged;
        return $"Создано видов: {created}; повторно оформлено: {updated}; без изменений: {unchanged}; не обработано: {failed}. Откройте подробности, чтобы увидеть результат каждого вида.";
    }

    private static string BuildViewCreationBatchDetails(
        IReadOnlyCollection<LintelAssemblyViewCreationResult> results)
    {
        return string.Join(Environment.NewLine, results.Select(result =>
            $"• {result.ViewName}: {ResolveViewCreationStatus(result)}"));
    }

    private static string ResolveViewCreationStatus(LintelAssemblyViewCreationResult result)
    {
        return result.Status switch
        {
            LintelAssemblyViewCreationStatus.Created when result.TypeImage?.TypeImageAssigned == true =>
                "создан, оформлен, PNG назначен типоразмеру",
            LintelAssemblyViewCreationStatus.Created => "создан и оформлен; PNG не назначен",
            LintelAssemblyViewCreationStatus.AlreadyExists when result.ModelChanged
                && result.TypeImage?.TypeImageAssigned == true =>
                "повторно оформлен, PNG назначен типоразмеру",
            LintelAssemblyViewCreationStatus.AlreadyExists when result.ModelChanged =>
                "повторно оформлен; PNG не назначен",
            LintelAssemblyViewCreationStatus.AlreadyExists => "уже существует, без изменений",
            LintelAssemblyViewCreationStatus.Blocked => "заблокирован",
            _ => "ошибка"
        };
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

    private sealed record LintelPreparedViewRequest(
        long? TypeId,
        string AssemblyName,
        string ViewName);
}
