using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.SystemDesignation;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class SystemDesignationCommand : IExternalCommand
{
    private const string Title = "Обозначение системы";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument? ui = commandData.Application.ActiveUIDocument;
        if (ui == null || ui.Document.IsFamilyDocument)
        {
            TaskDialog.Show(Title, "Откройте проект Revit.");
            return Result.Cancelled;
        }
        try
        {
            var selection = ui.Selection.GetElementIds();
            using var choose = new TaskDialog(Title)
            {
                Id = "TrueBIM_SystemDesignation_Scope",
                MainInstruction = "Какие элементы обработать?",
                MainContent = "Пустое сокращение очистит прежнее обозначение системы.",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            choose.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"Выделенные элементы ({selection.Count})",
                "Включая вложенные экземпляры и изоляцию.");
            choose.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Элементы на активном виде",
                "С учётом временного скрытия и изоляции.");
            choose.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Все элементы проекта");
            DesignationScope? scope = choose.Show() switch
            {
                TaskDialogResult.CommandLink1 => DesignationScope.Selection,
                TaskDialogResult.CommandLink2 => DesignationScope.View,
                TaskDialogResult.CommandLink3 => DesignationScope.Project,
                _ => null
            };
            if (scope == null) return Result.Cancelled;

            var sources = SystemDesignationScope.Collect(ui.Document, scope.Value, selection, ui.ActiveView);
            if (sources.Count == 0)
            {
                TaskDialog.Show(Title, "В выбранной области нет элементов вентиляции и трубопроводных систем.");
                return Result.Cancelled;
            }
            var plan = DesignationPlan.Prepare(ui.Document, sources);
            if (plan.Skipped.Count > 0)
            {
                using var warning = new TaskDialog(Title)
                {
                    Id = "TrueBIM_SystemDesignation_UnavailableParameters",
                    MainIcon = TaskDialogIcon.TaskDialogIconWarning,
                    MainInstruction = $"Элементов с недоступным параметром «{DesignationPlan.ParameterName}» — {plan.Skipped.Count}.",
                    MainContent = SummaryOfSkips(plan),
                    AllowCancellation = true
                };
                warning.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Отменить выполнение");
                warning.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Продолжить",
                    "Пропустить эти элементы и обновить остальные.");
                warning.DefaultButton = TaskDialogResult.CommandLink1;
                if (warning.Show() != TaskDialogResult.CommandLink2) return Result.Cancelled;
            }

            plan.Apply(ui.Document);
            using var report = new TaskDialog(Title)
            {
                Id = "TrueBIM_SystemDesignation_Report",
                MainInstruction = $"Обработано элементов — {sources.Count}",
                MainContent = $"Заполнено или обновлено — {plan.Updated}\nОчищено — {plan.Cleared}\n" +
                    $"Совпадало — {plan.Unchanged}\nПропущено — {plan.Skipped.Count}",
                CommonButtons = TaskDialogCommonButtons.Close
            };
            if (plan.Skipped.Count > 0)
                report.ExpandedContent = string.Join("\n", plan.Skipped.Select(row =>
                    $"ID {RevitElementIds.GetValue(row.Id)} — {row.Reason}"));
            report.Show();
            return Result.Succeeded;
        }
        catch (Exception error)
        {
            new FileTrueBimLogger(new TrueBimLogPaths()).Error("System designation command failed.", error);
            message = error.Message;
            return Result.Failed;
        }
    }

    private static string SummaryOfSkips(DesignationPlan plan) => string.Join("\n", plan.Skipped
        .GroupBy(row => row.Reason).Select(group => $"{group.Key} — {group.Count()}"));
}
