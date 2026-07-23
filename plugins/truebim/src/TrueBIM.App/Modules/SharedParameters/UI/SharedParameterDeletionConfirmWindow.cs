using System.Windows;
using System.Windows.Controls;
using TrueBIM.App.Modules.SharedParameters.Models;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.SharedParameters.UI;

public sealed class SharedParameterDeletionConfirmWindow : TrueBimWindow
{
    private readonly SharedParameterDeletionPlan plan;
    private readonly SharedParameterProjectAnalysis analysis;
    private readonly CheckBox acknowledgement = new();
    private readonly TextBox confirmationInput = new();
    private readonly RadioButton safeMode = new();
    private readonly RadioButton advancedMode = new();
    private readonly Button deleteButton;

    public SharedParameterDeletionConfirmWindow(
        SharedParameterDeletionPlan plan,
        SharedParameterProjectAnalysis analysis)
    {
        this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
        this.analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
        Title = "Удаление общего параметра";
        Width = 760;
        Height = 720;
        MinWidth = 640;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = IconFactory.CreateImage(TrueBimIcon.SharedParameters, 32);

        acknowledgement.Content = new TextBlock
        {
            Text = "Я понимаю, что операция изменит проект и может удалить зависимые настройки.",
            TextWrapping = TextWrapping.Wrap
        };
        acknowledgement.Style = TrueBimStyles.CreateCheckBoxStyle();
        acknowledgement.Checked += (_, _) => UpdateState();
        acknowledgement.Unchecked += (_, _) => UpdateState();

        confirmationInput.Style = TrueBimStyles.CreateTextBoxStyle();
        confirmationInput.ToolTip = $"Введите точное имя параметра: {plan.Parameter.Name}";
        confirmationInput.TextChanged += (_, _) => UpdateState();

        safeMode.Content = "Безопасное удаление";
        safeMode.IsChecked = true;
        safeMode.GroupName = "DeletionMode";
        safeMode.Checked += (_, _) => UpdateState();

        advancedMode.Content = "Расширенное удаление";
        advancedMode.GroupName = "DeletionMode";
        advancedMode.ToolTip = "Показывает расширенный режим, но не обходит blockers и ограничения публичного API.";
        advancedMode.Checked += (_, _) =>
        {
            UpdateState();
            MessageBox.Show(
                this,
                "Расширенный режим не обходит blockers и ограничения публичного API. "
                + "Проверьте каждое действие, риск и затрагиваемый объект в плане удаления.",
                "Расширенное удаление",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        };

        deleteButton = TrueBimUi.CreateDangerButton(
            "Удалить из проекта",
            TrueBimIcon.Delete,
            (_, _) =>
            {
                DialogResult = true;
                Close();
            },
            isEnabled: false,
            minWidth: 170);

        Button cancelButton = TrueBimUi.CreateSecondaryButton(
            "Отмена",
            TrueBimIcon.Close,
            (_, _) => Close());
        cancelButton.IsCancel = true;

        ApplyTrueBimShell(
            TrueBimUi.CreateHeader(
                "Удаление общего параметра",
                "Сначала проверьте dry run, план и blockers. Удаление не запускается до явного подтверждения.",
                TrueBimIcon.SharedParameters),
            null,
            CreateBody(),
            status: null,
            footer: TrueBimUi.CreateFooter(null, cancelButton, deleteButton));
        UpdateState();
    }

    public DeletionMode SelectedMode => advancedMode.IsChecked == true
        ? DeletionMode.Advanced
        : DeletionMode.Safe;

    private UIElement CreateBody()
    {
        StackPanel content = new();
        content.Children.Add(TrueBimUi.CreateInfoBanner(
            plan.Blockers.Count == 0
                ? "Dry run завершён. Неизвестные каскадные зависимости не обнаружены."
                : $"Удаление заблокировано: blockers — {plan.Blockers.Count}.",
            plan.Blockers.Count == 0 ? TrueBimUiSeverity.Warning : TrueBimUiSeverity.Danger));

        Grid parameterCard = new();
        parameterCard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        parameterCard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddRow(parameterCard, "Имя", plan.Parameter.Name);
        AddRow(parameterCard, "GUID", plan.Parameter.Guid.ToString("D"));
        AddRow(parameterCard, "ElementId", plan.Parameter.ParameterElementId.ToString());
        AddRow(parameterCard, "Тип данных", plan.Parameter.DataTypeName);
        AddRow(parameterCard, "Привязка", plan.Parameter.BindingDisplay);
        AddRow(parameterCard, "Категории", string.Join(", ", plan.Parameter.Categories.Select(category => category.Name)));

        Border parameterSection = TrueBimUi.CreateSectionCard("Параметр", parameterCard);
        parameterSection.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        content.Children.Add(parameterSection);

        TextBlock planText = new()
        {
            Text = BuildPlanText(),
            TextWrapping = TextWrapping.Wrap,
            Foreground = TrueBimBrushes.TextPrimary
        };
        ScrollViewer planScroll = new()
        {
            Content = planText,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 220
        };
        Border planSection = TrueBimUi.CreateSectionCard("План удаления", planScroll);
        planSection.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        content.Children.Add(planSection);

        StackPanel modePanel = new();
        modePanel.Children.Add(safeMode);
        advancedMode.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        modePanel.Children.Add(advancedMode);
        Border modeSection = TrueBimUi.CreateSectionCard("Режим", modePanel);
        modeSection.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        content.Children.Add(modeSection);

        StackPanel confirmPanel = new();
        confirmPanel.Children.Add(acknowledgement);
        confirmPanel.Children.Add(new TextBlock
        {
            Text = $"Для подтверждения введите: {plan.Parameter.Name}",
            Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, TrueBimTheme.Spacing4),
            Foreground = TrueBimBrushes.TextSecondary
        });
        confirmPanel.Children.Add(confirmationInput);
        Border confirmSection = TrueBimUi.CreateSectionCard("Подтверждение", confirmPanel);
        confirmSection.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        content.Children.Add(confirmSection);

        return new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private string BuildPlanText()
    {
        List<string> lines =
        [
            $"Элементы с параметром: {analysis.Elements.Count(element => element.HasParameter)}",
            $"Заполнено / пусто: {analysis.FilledValueCount} / {analysis.EmptyValueCount}",
            $"Спецификации: {analysis.ScheduleFields.Select(field => field.ScheduleId).Distinct().Count()}",
            $"Фильтры спецификаций: {analysis.ScheduleFields.Count(field => field.UsedInFilter)}",
            $"Сортировка/группировка: {analysis.ScheduleFields.Count(field => field.UsedInSortOrGroup)}",
            $"Фильтры видов: {plan.ViewFilters.Count}",
            $"Виды и шаблоны: {analysis.ViewFilters.SelectMany(filter => filter.AppliedViews).Select(view => view.ViewId).Distinct().Count()}",
            $"Глобальные ассоциации: {plan.GlobalParameters.Count}",
            $"Семейства с параметром: {analysis.FamilyCountWithParameter}",
            $"Dry run ElementId: {plan.DryRunDeletedIds.Count} [{string.Join(", ", plan.DryRunDeletedIds)}]",
            $"Предупреждения: {plan.Warnings.Count}",
            $"Blockers: {plan.Blockers.Count}"
        ];
        lines.AddRange(plan.Schedules.Select(action =>
            $"SCHEDULE {action.ScheduleName} ({action.ScheduleId}) / FieldId {action.FieldId}: "
            + $"{action.Action}; risk={action.Risk}; support={action.Support}; {action.Reason}"));
        lines.AddRange(plan.ViewFilters.Select(action =>
            $"VIEW FILTER {action.FilterName} ({action.FilterId}): "
            + $"{action.Action}; risk={action.Risk}; support={action.Support}; {action.Reason}"));
        lines.AddRange(plan.GlobalParameters.Select(action =>
            $"GLOBAL {action.GlobalParameterName} ({action.GlobalParameterId}) / element {action.ElementId}: "
            + $"{action.Action}; risk={action.Risk}; support={action.Support}; {action.Reason}"));
        lines.AddRange(plan.Families.Select(action =>
            $"FAMILY {action.FamilyName} ({action.FamilyId}): "
            + $"{action.Action}; risk={action.Risk}; support={action.Support}; {action.Reason}"));
        lines.AddRange(plan.Blockers.Select(blocker => $"BLOCKER [{blocker.Code}] {blocker.Message}"));
        lines.AddRange(plan.Warnings.Select(warning => $"WARNING [{warning.Code}] {warning.Message}"));
        return string.Join(Environment.NewLine, lines);
    }

    private void UpdateState()
    {
        deleteButton.IsEnabled = plan.Blockers.Count == 0
            && acknowledgement.IsChecked == true
            && string.Equals(
                confirmationInput.Text.Trim(),
                plan.Parameter.Name,
                StringComparison.Ordinal);
    }

    private static void AddRow(Grid grid, string label, string value)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        TextBlock labelText = new()
        {
            Text = label,
            Foreground = TrueBimBrushes.TextSecondary,
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing12, TrueBimTheme.Spacing4)
        };
        TextBlock valueText = new()
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TrueBimBrushes.TextPrimary,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing4)
        };
        Grid.SetRow(labelText, row);
        Grid.SetColumn(labelText, 0);
        Grid.SetRow(valueText, row);
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(labelText);
        grid.Children.Add(valueText);
    }
}
