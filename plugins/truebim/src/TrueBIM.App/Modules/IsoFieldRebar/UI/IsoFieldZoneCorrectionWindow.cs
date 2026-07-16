using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;
using WpfBinding = System.Windows.Data.Binding;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfGrid = System.Windows.Controls.Grid;

namespace TrueBIM.App.Modules.IsoFieldRebar.UI;

public sealed class IsoFieldZoneCorrectionWindow : TrueBimWindow
{
    private readonly IsoFieldRecognitionResult source;
    private readonly IsoFieldZoneCorrectionService correctionService = new();
    private readonly ObservableCollection<IsoFieldZoneCorrectionRow> rows = new();
    private readonly DataGrid zoneGrid;
    private readonly TextBlock summaryText;
    private readonly ContentControl statusHost;
    private int nextMergeGroupNumber = 1;

    public IsoFieldZoneCorrectionWindow(IsoFieldRecognitionResult source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        summaryText = new TextBlock
        {
            Foreground = TrueBimBrushes.TextSecondary,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusHost = new ContentControl();
        zoneGrid = CreateZoneGrid();

        Title = "Коррекция зон изополей";
        Icon = IconFactory.CreateImage(TrueBimIcon.IsoFieldRebar, 32);
        Width = 1080;
        Height = 720;
        MinWidth = 860;
        MinHeight = 600;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        ResetRows();
        ApplyTrueBimShell(
            header: TrueBimUi.CreateHeader(
                Title,
                "Проверьте результат до расчёта правил: исключите шум, назначьте правильный диапазон и объедините зоны одного слоя и класса.",
                TrueBimIcon.IsoFieldRebar),
            commandBar: CreateCommandBar(),
            body: CreateBody(),
            status: statusHost,
            footer: CreateFooter());
        SetStatus(
            "Изменения применяются только к текущему результату распознавания. Модель Revit не изменяется.",
            TrueBimUiSeverity.Info);
    }

    public IsoFieldRecognitionResult? Result { get; private set; }

    private UIElement CreateCommandBar()
    {
        Button mergeButton = TrueBimUi.CreateSecondaryButton(
            "Объединить выбранные",
            TrueBimIcon.Apply,
            (_, _) => MergeSelected(),
            minWidth: 188);
        mergeButton.ToolTip = "Выберите Ctrl/Shift минимум две включённые зоны одного слоя и класса.";

        Button unmergeButton = TrueBimUi.CreateSecondaryButton(
            "Снять объединение",
            TrueBimIcon.Close,
            (_, _) => UnmergeSelected(),
            minWidth: 164);
        unmergeButton.ToolTip = "Убрать группу объединения у выбранных строк. Остальные правки сохранятся.";

        Button resetButton = TrueBimUi.CreateSecondaryButton(
            "Сбросить правки",
            TrueBimIcon.Refresh,
            (_, _) => ResetRows(),
            minWidth: 154);
        resetButton.ToolTip = "Вернуть включение, класс и объединения к исходному результату распознавания.";

        return TrueBimUi.CreateCommandBar(mergeButton, unmergeButton, resetButton);
    }

    private UIElement CreateBody()
    {
        WpfGrid body = new();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Border hint = TrueBimUi.CreateInfoBanner(
            "Снимите «Исп.» для исключения зоны. Класс выбирается из шкалы соответствующего слоя. Для объединения выделяйте строки через Ctrl или Shift.",
            TrueBimUiSeverity.Neutral);
        hint.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        body.Children.Add(hint);

        Border gridCard = TrueBimUi.CreateSectionCard("Распознанные зоны", zoneGrid);
        WpfGrid.SetRow(gridCard, 1);
        body.Children.Add(gridCard);
        return body;
    }

    private UIElement CreateFooter()
    {
        Button cancelButton = TrueBimUi.CreateSecondaryButton(
            "Отмена",
            TrueBimIcon.Close,
            (_, _) => Close(),
            minWidth: 110);
        cancelButton.IsCancel = true;
        cancelButton.ToolTip = "Закрыть окно без применения правок.";

        Button applyButton = TrueBimUi.CreatePrimaryButton(
            "Применить правки",
            TrueBimIcon.Apply,
            (_, _) => ApplyCorrections(),
            minWidth: 166);
        applyButton.IsDefault = true;
        applyButton.ToolTip = "Обновить зоны в preview и сбросить ранее рассчитанные правила.";

        return TrueBimUi.CreateFooter(summaryText, cancelButton, applyButton);
    }

    private DataGrid CreateZoneGrid()
    {
        DataGrid grid = new()
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserReorderColumns = false,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            IsReadOnly = false,
            MinHeight = 360,
            Style = TrueBimStyles.CreateDataGridStyle(),
            ItemsSource = rows
        };
        grid.Columns.Add(CreateIncludedColumn());
        grid.Columns.Add(CreateTextColumn("Слой", nameof(IsoFieldZoneCorrectionRow.LayerName), 78));
        grid.Columns.Add(CreateTextColumn("Зона", nameof(IsoFieldZoneCorrectionRow.DisplayId), 150));
        grid.Columns.Add(CreateClassColumn());
        grid.Columns.Add(CreateTextColumn("Confidence", nameof(IsoFieldZoneCorrectionRow.ConfidenceText), 92));
        grid.Columns.Add(CreateTextColumn(
            "Действие",
            nameof(IsoFieldZoneCorrectionRow.ActionText),
            new DataGridLength(170)));
        grid.SelectionChanged += (_, _) => UpdateSummary();
        return grid;
    }

    private void ResetRows()
    {
        foreach (IsoFieldZoneCorrectionRow row in rows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        rows.Clear();
        foreach (IsoFieldPolyline polyline in source.Polylines)
        {
            IsoFieldZoneCorrectionRow row = CreateRow(polyline);
            row.PropertyChanged += OnRowPropertyChanged;
            rows.Add(row);
        }

        nextMergeGroupNumber = 1;
        zoneGrid?.UnselectAll();
        UpdateSummary();
        if (IsLoaded)
        {
            SetStatus("Все ручные правки сброшены.", TrueBimUiSeverity.Info);
        }
    }

    private IsoFieldZoneCorrectionRow CreateRow(IsoFieldPolyline polyline)
    {
        IsoFieldLegend? legend = FindLegend(polyline.LayerRole);
        List<IsoFieldZoneClassOption> options = legend?.Bands
            .Select(band => new IsoFieldZoneClassOption(
                band.Index,
                IsoFieldZoneCorrectionService.BuildZoneName(band)))
            .ToList() ?? new List<IsoFieldZoneClassOption>();
        IsoFieldZoneClassOption? selected = options.FirstOrDefault(
            option => option.LegendBandIndex == polyline.LegendBandIndex);
        if (selected is null)
        {
            selected = new IsoFieldZoneClassOption(
                polyline.LegendBandIndex,
                polyline.ZoneName ?? "Класс не задан");
            options.Insert(0, selected);
        }

        return new IsoFieldZoneCorrectionRow(polyline, options, selected);
    }

    private IsoFieldLegend? FindLegend(IsoFieldLayerRole? layerRole)
    {
        IsoFieldLegend? exact = source.EffectiveLegends.FirstOrDefault(
            legend => legend.LayerRole == layerRole);
        if (exact is not null)
        {
            return exact;
        }

        return source.EffectiveLegends.Count == 1
            ? source.EffectiveLegends[0]
            : null;
    }

    private void MergeSelected()
    {
        IsoFieldZoneCorrectionRow[] selected = zoneGrid.SelectedItems
            .Cast<IsoFieldZoneCorrectionRow>()
            .OrderBy(row => rows.IndexOf(row))
            .ToArray();
        if (selected.Length < 2)
        {
            SetStatus(
                "Для объединения выделите минимум две строки через Ctrl или Shift.",
                TrueBimUiSeverity.Warning);
            return;
        }

        if (selected.Any(row => !row.IsIncluded))
        {
            SetStatus(
                "Исключённую зону нельзя объединить. Сначала включите все выбранные строки.",
                TrueBimUiSeverity.Warning);
            return;
        }

        if (selected.Any(row => row.MergeGroupId is not null))
        {
            SetStatus(
                "Одна из зон уже входит в объединение. Сначала снимите существующую группу.",
                TrueBimUiSeverity.Warning);
            return;
        }

        IsoFieldZoneCorrectionRow first = selected[0];
        if (selected.Any(row => row.Source.LayerRole != first.Source.LayerRole))
        {
            SetStatus(
                "Объединять можно только зоны одного расчётного слоя.",
                TrueBimUiSeverity.Warning);
            return;
        }

        if (selected.Any(row => row.SelectedClassOption.ClassKey != first.SelectedClassOption.ClassKey))
        {
            SetStatus(
                "Перед объединением назначьте выбранным зонам одинаковый класс.",
                TrueBimUiSeverity.Warning);
            return;
        }

        string groupId = $"merge-{nextMergeGroupNumber:000}";
        int displayedNumber = nextMergeGroupNumber;
        nextMergeGroupNumber++;
        foreach (IsoFieldZoneCorrectionRow row in selected)
        {
            row.MergeGroupId = groupId;
            row.MergeGroupName = $"Объединение {displayedNumber}";
        }

        UpdateSummary();
        SetStatus(
            $"В группу {displayedNumber} добавлено зон: {selected.Length}. Геометрия будет построена как общий convex hull.",
            TrueBimUiSeverity.Success);
    }

    private void UnmergeSelected()
    {
        IsoFieldZoneCorrectionRow[] selected = zoneGrid.SelectedItems
            .Cast<IsoFieldZoneCorrectionRow>()
            .ToArray();
        string[] groupIds = selected
            .Select(row => row.MergeGroupId)
            .Where(groupId => groupId is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (groupIds.Length == 0)
        {
            SetStatus("У выбранных строк нет объединения.", TrueBimUiSeverity.Info);
            return;
        }

        foreach (IsoFieldZoneCorrectionRow row in rows.Where(
            row => row.MergeGroupId is not null
                && groupIds.Contains(row.MergeGroupId, StringComparer.Ordinal)))
        {
            row.ClearMergeGroup();
        }

        UpdateSummary();
        SetStatus("Выбранные группы объединения сняты.", TrueBimUiSeverity.Success);
    }

    private void ApplyCorrections()
    {
        zoneGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        zoneGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (rows.All(row => !row.IsIncluded))
        {
            SetStatus(
                "Нельзя исключить все зоны: оставьте хотя бы одну зону для продолжения.",
                TrueBimUiSeverity.Danger);
            return;
        }

        IReadOnlyList<IsoFieldZoneCorrection> corrections = rows
            .Select(row => new IsoFieldZoneCorrection(
                row.Source.Id,
                row.IsIncluded,
                row.SelectedClassOption.LegendBandIndex))
            .ToArray();
        IReadOnlyList<IsoFieldZoneMerge> merges = rows
            .Where(row => row.MergeGroupId is not null)
            .GroupBy(row => row.MergeGroupId!, StringComparer.Ordinal)
            .Select(group => new IsoFieldZoneMerge(
                group.Select(row => row.Source.Id).ToArray()))
            .ToArray();

        try
        {
            Result = correctionService.Apply(source, corrections, merges);
            DialogResult = true;
        }
        catch (InvalidOperationException exception)
        {
            SetStatus(exception.Message, TrueBimUiSeverity.Danger);
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (sender is IsoFieldZoneCorrectionRow changed
            && changed.MergeGroupId is not null
            && (eventArgs.PropertyName == nameof(IsoFieldZoneCorrectionRow.IsIncluded)
                || eventArgs.PropertyName == nameof(IsoFieldZoneCorrectionRow.SelectedClassOption)))
        {
            string groupId = changed.MergeGroupId;
            foreach (IsoFieldZoneCorrectionRow row in rows.Where(
                row => string.Equals(row.MergeGroupId, groupId, StringComparison.Ordinal)))
            {
                row.ClearMergeGroup();
            }

            SetStatus(
                "Объединение снято: состав или класс одной из его зон изменён.",
                TrueBimUiSeverity.Info);
        }

        UpdateSummary();
    }

    private void UpdateSummary()
    {
        int included = rows.Count(row => row.IsIncluded);
        int excluded = rows.Count - included;
        int reclassified = rows.Count(row => row.IsClassChanged);
        int mergeGroups = rows
            .Where(row => row.MergeGroupId is not null)
            .Select(row => row.MergeGroupId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        int selected = zoneGrid?.SelectedItems.Count ?? 0;
        summaryText.Text = $"Зон: {rows.Count} · включено: {included} · исключено: {excluded} · "
            + $"смена класса: {reclassified} · объединений: {mergeGroups} · выделено: {selected}";
    }

    private void SetStatus(string message, TrueBimUiSeverity severity)
    {
        statusHost.Content = TrueBimUi.CreateInfoBanner(message, severity);
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

    private static DataGridTemplateColumn CreateIncludedColumn()
    {
        FrameworkElementFactory checkBox = new(typeof(CheckBox));
        checkBox.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        checkBox.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        checkBox.SetValue(FrameworkElement.StyleProperty, TrueBimStyles.CreateCheckBoxStyle());
        checkBox.SetBinding(
            CheckBox.IsCheckedProperty,
            new WpfBinding(nameof(IsoFieldZoneCorrectionRow.IsIncluded))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        checkBox.SetValue(FrameworkElement.ToolTipProperty, "Снимите флажок, чтобы исключить зону из preview и расчёта правил.");

        return new DataGridTemplateColumn
        {
            Header = "Исп.",
            CellTemplate = new DataTemplate { VisualTree = checkBox },
            Width = 54
        };
    }

    private static DataGridTemplateColumn CreateClassColumn()
    {
        DataTemplate template = new(typeof(IsoFieldZoneCorrectionRow));
        FrameworkElementFactory combo = new(typeof(WpfComboBox));
        combo.SetValue(FrameworkElement.StyleProperty, TrueBimStyles.CreateComboBoxStyle());
        combo.SetBinding(
            ItemsControl.ItemsSourceProperty,
            new WpfBinding(nameof(IsoFieldZoneCorrectionRow.ClassOptions)));
        combo.SetBinding(
            WpfComboBox.SelectedItemProperty,
            new WpfBinding(nameof(IsoFieldZoneCorrectionRow.SelectedClassOption))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        combo.SetBinding(
            UIElement.IsEnabledProperty,
            new WpfBinding(nameof(IsoFieldZoneCorrectionRow.CanEditClass)));
        combo.SetValue(ItemsControl.DisplayMemberPathProperty, nameof(IsoFieldZoneClassOption.DisplayName));
        combo.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 0, 4, 0));
        combo.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        combo.SetValue(FrameworkElement.ToolTipProperty, "Назначить диапазон из легенды этого расчётного слоя.");
        template.VisualTree = combo;

        return new DataGridTemplateColumn
        {
            Header = "Класс зоны",
            CellTemplate = template,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 280,
            IsReadOnly = false
        };
    }
}

internal sealed class IsoFieldZoneCorrectionRow : INotifyPropertyChanged
{
    private readonly string originalClassKey;
    private bool isIncluded = true;
    private IsoFieldZoneClassOption selectedClassOption;
    private string? mergeGroupId;
    private string? mergeGroupName;

    public IsoFieldZoneCorrectionRow(
        IsoFieldPolyline source,
        IReadOnlyList<IsoFieldZoneClassOption> classOptions,
        IsoFieldZoneClassOption selectedClassOption)
    {
        Source = source;
        ClassOptions = classOptions;
        this.selectedClassOption = selectedClassOption;
        originalClassKey = selectedClassOption.ClassKey;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IsoFieldPolyline Source { get; }

    public IReadOnlyList<IsoFieldZoneClassOption> ClassOptions { get; }

    public string LayerName => Source.LayerRole?.ToString() ?? "Источник";

    public string DisplayId => Source.Id;

    public string ConfidenceText => Source.Confidence.HasValue
        ? Source.Confidence.Value.ToString("P0", CultureInfo.GetCultureInfo("ru-RU"))
        : "—";

    public bool CanChangeClass => ClassOptions.Count > 1;

    public bool CanEditClass => IsIncluded && CanChangeClass;

    public bool IsIncluded
    {
        get => isIncluded;
        set
        {
            if (isIncluded == value)
            {
                return;
            }

            isIncluded = value;
            RaisePropertyChanged(nameof(IsIncluded));
            RaisePropertyChanged(nameof(CanEditClass));
            RaisePropertyChanged(nameof(ActionText));
        }
    }

    public IsoFieldZoneClassOption SelectedClassOption
    {
        get => selectedClassOption;
        set
        {
            if (value is null || selectedClassOption.ClassKey == value.ClassKey)
            {
                return;
            }

            selectedClassOption = value;
            RaisePropertyChanged(nameof(SelectedClassOption));
            RaisePropertyChanged(nameof(IsClassChanged));
            RaisePropertyChanged(nameof(ActionText));
        }
    }

    public bool IsClassChanged => selectedClassOption.ClassKey != originalClassKey;

    public string? MergeGroupId
    {
        get => mergeGroupId;
        set
        {
            if (string.Equals(mergeGroupId, value, StringComparison.Ordinal))
            {
                return;
            }

            mergeGroupId = value;
            RaisePropertyChanged(nameof(MergeGroupId));
            RaisePropertyChanged(nameof(ActionText));
        }
    }

    public string? MergeGroupName
    {
        get => mergeGroupName;
        set
        {
            if (string.Equals(mergeGroupName, value, StringComparison.Ordinal))
            {
                return;
            }

            mergeGroupName = value;
            RaisePropertyChanged(nameof(MergeGroupName));
            RaisePropertyChanged(nameof(ActionText));
        }
    }

    public string ActionText
    {
        get
        {
            if (!IsIncluded)
            {
                return "Исключить";
            }

            if (MergeGroupName is not null && IsClassChanged)
            {
                return $"{MergeGroupName} + новый класс";
            }

            if (MergeGroupName is not null)
            {
                return MergeGroupName;
            }

            return IsClassChanged ? "Изменить класс" : "Без изменений";
        }
    }

    public void ClearMergeGroup()
    {
        MergeGroupId = null;
        MergeGroupName = null;
    }

    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal sealed record IsoFieldZoneClassOption(
    int? LegendBandIndex,
    string DisplayName)
{
    public string ClassKey => LegendBandIndex.HasValue
        ? $"band:{LegendBandIndex.Value}"
        : $"name:{DisplayName}";
}
