using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TrueBIM.App.Modules.BimTools.FamilyManager.Services;
using TrueBIM.App.Modules.BimTools.FamilyManager.UI;
using TrueBIM.App.Modules.BimTools.FamilyReplacement.Models;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.BimTools.FamilyReplacement.UI;

public sealed class FamilyReplacementSelectionWindow : TrueBimWindow
{
    private readonly IReadOnlyList<FamilyReplacementCandidateRow> rows;
    private readonly IReadOnlyList<FamilyReplacementTypeOption> targetTypes;
    private readonly HashSet<long> includedTypes;
    private readonly TreeView sourceTree = new();
    private readonly DataGrid candidateGrid = new();
    private readonly TextBox sourceSearch = TrueBimUi.CreateSearchBox("Поиск категории, семейства или типа");
    private readonly TextBox instanceSearch = TrueBimUi.CreateSearchBox("Поиск экземпляра по ID, марке, уровню или типу");
    private readonly TextBox targetSearch = TrueBimUi.CreateSearchBox("Поиск загруженного семейства или типа замены");
    private readonly ComboBox targetCategory = new();
    private readonly ComboBox targetType = new();
    private readonly ComboBox tagType = new();
    private readonly ComboBox alignmentBox = new();
    private readonly CheckBox ignoreIntersections = new()
    {
        Content = "Игнорировать предупреждения о пересечениях",
        ToolTip = "Пропускать предупреждения Revit о наложениях и пересечениях. Ошибки, требующие удаления элементов или потери привязок, по-прежнему отменяют замену.",
        Margin = new Thickness(0, 6, 0, 6)
    };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button apply;
    private bool updatingSelection;

    public FamilyReplacementSelectionWindow(
        IReadOnlyList<FamilyReplacementCandidate> candidates,
        IReadOnlyList<FamilyReplacementTypeOption> targetTypes,
        IReadOnlyList<FamilyReplacementTypeOption> tagTypes,
        string scopeLabel,
        long? preferredTargetTypeId = null)
    {
        rows = candidates.Select(item => new FamilyReplacementCandidateRow(item)).ToList();
        this.targetTypes = targetTypes;
        includedTypes = new HashSet<long>(candidates.Where(item => item.PreferredSource).Select(item => item.TypeId));
        foreach (FamilyReplacementCandidateRow row in rows)
        {
            row.PropertyChanged += (_, _) => { if (!updatingSelection) UpdateStatus(); };
        }

        Title = "Замена семейств";
        Icon = IconFactory.CreateImage(TrueBimIcon.FamilyManager, 32);
        Width = 1190;
        Height = 850;
        MinWidth = 960;
        MinHeight = 690;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        apply = TrueBimUi.CreatePrimaryButton("Заменить выбранные", TrueBimIcon.Apply, (_, _) => Accept(), minWidth: 180);
        Button cancel = TrueBimUi.CreateSecondaryButton("Отмена", TrueBimIcon.Close);
        cancel.IsCancel = true;
        Grid body = new();
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.Children.Add(CreateSourcePanel());
        UIElement replacement = CreateReplacementPanel(tagTypes);
        Grid.SetRow(replacement, 1);
        body.Children.Add(replacement);
        ApplyTrueBimShell(
            TrueBimUi.CreateHeader("Замена семейств", $"{scopeLabel}. Отметьте категории, семейства или типы в дереве, затем уточните экземпляры справа.", TrueBimIcon.FamilyManager),
            null, body, status, TrueBimUi.CreateFooter(null, apply, cancel));

        FamilyReplacementTypeOption? preferred = targetTypes.FirstOrDefault(type => type.Id == preferredTargetTypeId);
        targetCategory.ItemsSource = targetTypes.Select(type => type.CategoryName).Distinct().ToList();
        targetCategory.SelectedItem = preferred?.CategoryName
            ?? targetTypes.FirstOrDefault(type => type.PreferredTarget)?.CategoryName;
        if (preferred is not null)
        {
            targetType.SelectedItem = preferred;
        }

        RefreshSourceTree();
        RefreshCandidates();
    }

    public IReadOnlyList<long> SourceIds => VisibleRows
        .Where(row => row.IsSelected)
        .Select(row => row.Item.Id).ToList();

    public long? TargetTypeId => (targetType.SelectedItem as FamilyReplacementTypeOption)?.Id;
    public bool IgnoreIntersectionWarnings => ignoreIntersections.IsChecked == true;
    public long? TargetTagTypeId => (tagType.SelectedItem as ComboBoxItem)?.Tag as long?;
    public FamilyReplacementAlignment Alignment => alignmentBox.SelectedIndex == 1
        ? FamilyReplacementAlignment.InsertionPoint : FamilyReplacementAlignment.GeometryCenter;

    private UIElement CreateSourcePanel()
    {
        Grid panel = new();
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.45, GridUnitType.Star) });
        Grid treePanel = CreateVerticalPanel();
        sourceSearch.TextChanged += (_, _) => { RefreshSourceTree(); RefreshCandidates(); };
        StackPanel sourceHeader = new();
        sourceHeader.Children.Add(TrueBimUi.CreateFieldLabel("Исходные категории / семейства / типы"));
        sourceHeader.Children.Add(sourceSearch);
        sourceHeader.Children.Add(TrueBimUi.CreateCommandBar(
            TrueBimUi.CreateSecondaryButton("Все найденные", TrueBimIcon.Apply, (_, _) => SetFilteredTypes(true)),
            TrueBimUi.CreateSecondaryButton("Снять фильтры", TrueBimIcon.Close, (_, _) => SetFilteredTypes(false))));
        treePanel.Children.Add(sourceHeader);
        sourceTree.BorderBrush = TrueBimBrushes.Border;
        sourceTree.BorderThickness = new Thickness(1);
        Grid.SetRow(sourceTree, 1);
        treePanel.Children.Add(sourceTree);
        panel.Children.Add(treePanel);
        GridSplitter splitter = new() { Width = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch };
        Grid.SetColumn(splitter, 1);
        panel.Children.Add(splitter);

        Grid candidates = CreateVerticalPanel();
        StackPanel candidateHeader = new();
        candidateHeader.Children.Add(TrueBimUi.CreateFieldLabel("Экземпляры выбранных фильтров"));
        instanceSearch.TextChanged += (_, _) => RefreshCandidates();
        candidateHeader.Children.Add(instanceSearch);
        candidateHeader.Children.Add(TrueBimUi.CreateCommandBar(
            TrueBimUi.CreateSecondaryButton("Выбрать найденные", TrueBimIcon.Apply, (_, _) => SetVisibleRows(true)),
            TrueBimUi.CreateSecondaryButton("Снять найденные", TrueBimIcon.Close, (_, _) => SetVisibleRows(false))));
        candidates.Children.Add(candidateHeader);
        candidateGrid.AutoGenerateColumns = false;
        candidateGrid.CanUserAddRows = false;
        candidateGrid.CanUserDeleteRows = false;
        candidateGrid.EnableRowVirtualization = true;
        candidateGrid.Style = TrueBimStyles.CreateDataGridStyle();
        DataGridTemplateColumn selectionColumn = new() { Header = "✓", Width = 42 };
        FrameworkElementFactory checkBox = new(typeof(CheckBox));
        checkBox.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(FamilyReplacementCandidateRow.IsSelected))
        {
            Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        checkBox.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        checkBox.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        selectionColumn.CellTemplate = new DataTemplate { VisualTree = checkBox };
        candidateGrid.Columns.Add(selectionColumn);
        AddTextColumn(candidateGrid, "ID", "Item.Id", 70);
        AddTextColumn(candidateGrid, "Семейство", "Item.FamilyName", 160);
        AddTextColumn(candidateGrid, "Тип", "Item.TypeName", 140);
        AddTextColumn(candidateGrid, "Уровень", "Item.LevelName", 100);
        AddTextColumn(candidateGrid, "Марка", "Item.Mark", 90);
        Grid.SetRow(candidateGrid, 1);
        candidates.Children.Add(candidateGrid);
        Grid.SetColumn(candidates, 2);
        panel.Children.Add(candidates);
        return panel;
    }

    private UIElement CreateReplacementPanel(IReadOnlyList<FamilyReplacementTypeOption> tagTypes)
    {
        Grid panel = new() { Margin = new Thickness(0, 14, 0, 0) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) });
        for (int index = 0; index < 5; index++)
        {
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        ConfigureComboBox(targetCategory);
        ConfigureComboBox(targetType);
        ConfigureComboBox(tagType);
        ConfigureComboBox(alignmentBox);
        targetType.DisplayMemberPath = nameof(FamilyReplacementTypeOption.Label);
        targetCategory.SelectionChanged += (_, _) => RefreshTargets();
        targetSearch.TextChanged += (_, _) => RefreshTargets();
        targetType.SelectionChanged += (_, _) => UpdateStatus();
        AddField(panel, "Категория замены", targetCategory, 0, 0);
        AddField(panel, "Найти загруженное семейство / тип", targetSearch, 0, 1);
        AddField(panel, "Совмещение", alignmentBox, 1, 0);
        AddField(panel, "Заменить на тип (выберите вручную)", targetType, 1, 1);
        alignmentBox.Items.Add("По центру габаритов геометрии (XYZ)");
        alignmentBox.Items.Add("По точке вставки (XYZ)");
        alignmentBox.ToolTip = "Центр габаритов объёмной геометрии (без символических линий). Если геометрия недоступна, экземпляр будет пропущен; можно повторить замену по точке вставки.";
        alignmentBox.SelectedIndex = 0;
        tagType.Items.Add(new ComboBoxItem { Content = "Автоматически, если подходящий тип единственный" });
        foreach (FamilyReplacementTypeOption type in tagTypes)
        {
            tagType.Items.Add(new ComboBoxItem { Content = $"{type.CategoryName} — {type.Label}", Tag = type.Id });
        }

        tagType.SelectedIndex = 0;
        StackPanel tags = CreateField("Тип аннотационной марки для новых элементов", tagType);
        Grid.SetRow(tags, 2);
        Grid.SetColumnSpan(tags, 2);
        panel.Children.Add(tags);
        Grid.SetRow(ignoreIntersections, 3);
        Grid.SetColumnSpan(ignoreIntersections, 2);
        panel.Children.Add(ignoreIntersections);
        TextBlock note = new()
        {
            Text = "Переносятся положение, ориентация и совместимые параметры экземпляра. Если размещение, марки или размеры нельзя сохранить, экземпляр останется исходным; причина будет в отчёте.",
            TextWrapping = TextWrapping.Wrap, Foreground = TrueBimBrushes.TextSecondary,
            Margin = new Thickness(0, 6, 0, 0)
        };
        Grid.SetRow(note, 4);
        Grid.SetColumnSpan(note, 2);
        panel.Children.Add(note);
        return panel;
    }

    private IEnumerable<FamilyReplacementCandidateRow> FilteredSourceRows => rows.Where(row =>
        FamilyReplacementSelectionFilter.MatchesSource(row.Item, sourceSearch.Text));

    private IEnumerable<FamilyReplacementCandidateRow> VisibleRows => FamilyReplacementSelectionFilter.VisibleRows(
        rows, includedTypes, sourceSearch.Text, instanceSearch.Text);

    private void RefreshSourceTree()
    {
        sourceTree.Items.Clear();
        foreach (IGrouping<long, FamilyReplacementCandidateRow> category in FilteredSourceRows.GroupBy(row => row.Item.CategoryId))
        {
            List<FamilyReplacementCandidateRow> categoryRows = category.ToList();
            TreeViewItem categoryNode = CreateFilterNode(categoryRows[0].Item.CategoryName, categoryRows);
            foreach (IGrouping<string, FamilyReplacementCandidateRow> family in categoryRows.GroupBy(row => row.Item.FamilyName))
            {
                List<FamilyReplacementCandidateRow> familyRows = family.ToList();
                TreeViewItem familyNode = CreateFilterNode(family.Key, familyRows);
                foreach (IGrouping<long, FamilyReplacementCandidateRow> type in familyRows.GroupBy(row => row.Item.TypeId))
                {
                    List<FamilyReplacementCandidateRow> typeRows = type.ToList();
                    familyNode.Items.Add(CreateFilterNode(typeRows[0].Item.TypeName, typeRows));
                }

                categoryNode.Items.Add(familyNode);
            }

            sourceTree.Items.Add(categoryNode);
        }
    }

    private TreeViewItem CreateFilterNode(string label, IReadOnlyList<FamilyReplacementCandidateRow> group)
    {
        long[] typeIds = group.Select(row => row.Item.TypeId).Distinct().ToArray();
        int selected = typeIds.Count(includedTypes.Contains);
        CheckBox box = new()
        {
            Content = new SearchHighlightTextBlock
            {
                HighlightText = $"{label} ({group.Count})", SearchText = sourceSearch.Text
            },
            IsThreeState = false,
            IsChecked = selected == 0 ? false : selected == typeIds.Length ? true : (bool?)null,
            Margin = new Thickness(2, 4, 2, 4)
        };
        box.Click += (_, _) =>
        {
            SetTypes(typeIds, box.IsChecked == true);
            RefreshSourceTree();
            RefreshCandidates();
        };
        return new TreeViewItem
        {
            Header = box,
            IsExpanded = selected > 0 || !string.IsNullOrWhiteSpace(sourceSearch.Text)
        };
    }

    private void SetTypes(IEnumerable<long> typeIds, bool include)
    {
        HashSet<long> changed = new(typeIds);
        if (include)
        {
            includedTypes.UnionWith(changed);
        }
        else
        {
            includedTypes.ExceptWith(changed);
        }

        updatingSelection = true;
        foreach (FamilyReplacementCandidateRow row in rows.Where(row => changed.Contains(row.Item.TypeId)))
        {
            row.IsSelected = include;
        }
        updatingSelection = false;
    }

    private void SetFilteredTypes(bool include)
    {
        SetTypes(include ? FilteredSourceRows.Select(row => row.Item.TypeId) : includedTypes.ToArray(), include);
        RefreshSourceTree();
        RefreshCandidates();
    }

    private void SetVisibleRows(bool selected)
    {
        updatingSelection = true;
        foreach (FamilyReplacementCandidateRow row in VisibleRows)
        {
            row.IsSelected = selected;
        }
        updatingSelection = false;

        UpdateStatus();
    }

    private void RefreshCandidates()
    {
        candidateGrid.ItemsSource = VisibleRows.ToList();
        UpdateStatus();
    }

    private void RefreshTargets()
    {
        long? previouslySelected = TargetTypeId;
        List<FamilyReplacementTypeOption> filtered = targetTypes.Where(type =>
            type.CategoryName == (targetCategory.SelectedItem as string)
            && FamilySearchMatchService.MatchesText(targetSearch.Text, type.FamilyName, type.TypeName)).ToList();
        targetType.ItemsSource = filtered;
        targetType.SelectedItem = filtered.FirstOrDefault(type => type.Id == previouslySelected);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        int selectedCount = SourceIds.Count;
        status.Text = $"Найдено в области: {rows.Count}. По текущим фильтрам: {VisibleRows.Count()}. К замене: {selectedCount}. "
            + "Заменяются только отмеченные строки, показанные справа; поиск сужает выборку.";
        apply.IsEnabled = selectedCount > 0 && TargetTypeId.HasValue;
        apply.Content = IconFactory.CreateButtonContent(TrueBimIcon.Apply, $"Заменить ({selectedCount})");
    }

    private void Accept()
    {
        candidateGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        candidateGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (SourceIds.Count == 0 || !TargetTypeId.HasValue)
        {
            status.Text = "Отметьте исходные экземпляры и выберите загруженный тип замены.";
            return;
        }

        DialogResult = true;
    }

    private static Grid CreateVerticalPanel()
    {
        Grid panel = new();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        return panel;
    }

    private static void AddTextColumn(DataGrid grid, string header, string property, double width)
    {
        grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(property), Width = width, IsReadOnly = true });
    }

    private static void ConfigureComboBox(ComboBox box)
    {
        box.Style = TrueBimStyles.CreateComboBoxStyle();
        box.MinHeight = 30;
        box.IsTextSearchEnabled = true;
    }

    private static StackPanel CreateField(string label, UIElement input)
    {
        StackPanel field = new() { Margin = new Thickness(0, 0, 8, 6) };
        field.Children.Add(TrueBimUi.CreateFieldLabel(label));
        field.Children.Add(input);
        return field;
    }

    private static void AddField(Grid panel, string label, UIElement input, int row, int column)
    {
        StackPanel field = CreateField(label, input);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, column);
        panel.Children.Add(field);
    }
}
