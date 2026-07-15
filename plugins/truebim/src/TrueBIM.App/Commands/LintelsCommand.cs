using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.Lintels;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class LintelsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            string? documentTitle = commandData.Application.ActiveUIDocument?.Document?.Title;
            LintelsModuleStatus status = LintelsModuleStatus.Create(documentTitle);

            logger.Info($"Opening Lintels module scaffold. Document='{status.DocumentName}'; CanModifyModel={status.CanModifyModel}.");
            TaskDialog dialog = new("Перемычки")
            {
                MainInstruction = "Модуль «Перемычки» подключён",
                MainContent = status.ToDialogText(),
                ExpandedContent = "Первый этап является безопасным каркасом. Создание сборок, видов, аннотаций и изображений будет добавлено после проверки рабочего RVT-файла и семейств оформления.",
                CommonButtons = TaskDialogCommonButtons.Close
            };
            dialog.Show();
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Lintels module scaffold.", exception);
            TaskDialog.Show(
                "Перемычки",
                "Не удалось открыть модуль перемычек. Используйте логи для диагностики.");
            return Result.Failed;
        }
    }
}
