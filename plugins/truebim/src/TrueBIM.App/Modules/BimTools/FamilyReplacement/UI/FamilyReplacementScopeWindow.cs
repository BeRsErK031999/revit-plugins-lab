using System.Windows;
using System.Windows.Controls;
using TrueBIM.App.Modules.BimTools.FamilyManager.Services;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.BimTools.FamilyReplacement.UI;

public sealed class FamilyReplacementScopeWindow : TrueBimWindow
{
    private readonly IReadOnlyList<FamilyReplacementViewOption> views;
    private readonly HashSet<long> selectedViews;
    private readonly ListBox viewList = new();
    private readonly TextBox searchBox = TrueBimUi.CreateSearchBox("Поиск вида по названию");
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Grid viewPanel = new();

    public FamilyReplacementScopeWindow(
        IReadOnlyList<FamilyReplacementViewOption> views,
        string activeViewName,
        int selectedElementCount)
    {
        this.views = views;
        selectedViews = new HashSet<long>(views.Where(view => view.InitiallySelected).Select(view => view.Id));
        Scope = selectedElementCount > 0
            ? FamilyReplacementScope.CurrentSelection
            : selectedViews.Count > 0 ? FamilyReplacementScope.SelectedViews : FamilyReplacementScope.ActiveView;
        Title = "Замена семейств — область поиска";
        Icon = IconFactory.CreateImage(TrueBimIcon.FamilyManager, 32);
        Width = 870;
        Height = 570;
        MinWidth = 730;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Grid body = new();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        StackPanel scopes = new() { Margin = new Thickness(0, 0, 16, 0) };
        AddScope(scopes, FamilyReplacementScope.ActiveView, $"На активном виде\n{activeViewName}");
        AddScope(scopes, FamilyReplacementScope.SelectedViews, "На выбранных видах");
        AddScope(scopes, FamilyReplacementScope.Project, "Во всём проекте");
        AddScope(scopes, FamilyReplacementScope.CurrentSelection,
            $"Выделенные экземпляры ({selectedElementCount})", selectedElementCount > 0);
        AddScope(scopes, FamilyReplacementScope.PickElements, "Выбрать экземпляры в модели…");
        body.Children.Add(scopes);

        viewPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        viewPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        viewPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        searchBox.Margin = new Thickness(0, 0, 0, 8);
        searchBox.TextChanged += (_, _) => RefreshViews();
        viewPanel.Children.Add(searchBox);
        StackPanel buttons = TrueBimUi.CreateCommandBar(
            TrueBimUi.CreateSecondaryButton("Все найденные", TrueBimIcon.Apply, (_, _) => SelectViews(true)),
            TrueBimUi.CreateSecondaryButton("Снять все", TrueBimIcon.Close, (_, _) => SelectViews(false)));
        Grid.SetRow(buttons, 1);
        viewPanel.Children.Add(buttons);
        viewList.Style = TrueBimStyles.CreateListBoxStyle();
        Grid.SetRow(viewList, 2);
        viewPanel.Children.Add(viewList);
        Grid.SetColumn(viewPanel, 1);
        body.Children.Add(viewPanel);
        Button next = TrueBimUi.CreatePrimaryButton("Найти экземпляры", TrueBimIcon.FamilyManager, (_, _) => Accept());
        Button cancel = TrueBimUi.CreateSecondaryButton("Отмена", TrueBimIcon.Close);
        cancel.IsCancel = true;
        ApplyTrueBimShell(
            TrueBimUi.CreateHeader("Область поиска", "Выберите, где искать исходные экземпляры. Затем можно отфильтровать категории, семейства и типы.", TrueBimIcon.FamilyManager),
            null, body, status, TrueBimUi.CreateFooter(null, next, cancel));
        RefreshViews();
        RefreshScope();
    }

    public FamilyReplacementScope Scope { get; private set; }
    public IReadOnlyList<long> SelectedViewIds => selectedViews.ToList();

    private void AddScope(StackPanel panel, FamilyReplacementScope scope, string label, bool enabled = true)
    {
        RadioButton button = new()
        {
            Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap },
            GroupName = "Scope", IsChecked = Scope == scope, IsEnabled = enabled,
            Margin = new Thickness(0, 0, 0, 18)
        };
        button.Checked += (_, _) => { Scope = scope; RefreshScope(); };
        panel.Children.Add(button);
    }

    private IEnumerable<FamilyReplacementViewOption> FilteredViews => views.Where(view =>
        FamilySearchMatchService.MatchesText(searchBox.Text, view.Name, view.Kind));

    private void RefreshViews()
    {
        viewList.Items.Clear();
        foreach (FamilyReplacementViewOption view in FilteredViews)
        {
            CheckBox box = new()
            {
                Content = $"{view.Name} ({view.Kind})", IsChecked = selectedViews.Contains(view.Id),
                Margin = new Thickness(6)
            };
            box.Checked += (_, _) => { selectedViews.Add(view.Id); RefreshScope(); };
            box.Unchecked += (_, _) => { selectedViews.Remove(view.Id); RefreshScope(); };
            viewList.Items.Add(box);
        }
    }

    private void SelectViews(bool select)
    {
        if (select)
        {
            selectedViews.UnionWith(FilteredViews.Select(view => view.Id));
        }
        else
        {
            selectedViews.Clear();
        }

        RefreshViews();
        RefreshScope();
    }

    private void RefreshScope()
    {
        viewPanel.IsEnabled = Scope == FamilyReplacementScope.SelectedViews;
        status.Text = Scope == FamilyReplacementScope.SelectedViews
            ? $"Выбрано видов: {selectedViews.Count}. Экземпляры на нескольких видах учитываются один раз."
            : Scope == FamilyReplacementScope.PickElements
                ? "После нажатия «Найти экземпляры» укажите элементы в Revit и завершите выбор."
                : "Поиск выполняется в текущей модели. RVT-связи не входят в выборку.";
    }

    private void Accept()
    {
        if (Scope == FamilyReplacementScope.SelectedViews && selectedViews.Count == 0)
        {
            status.Text = "Отметьте хотя бы один вид для поиска.";
            return;
        }

        DialogResult = true;
    }
}
