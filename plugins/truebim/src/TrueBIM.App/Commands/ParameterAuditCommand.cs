using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.ParameterAudit.Revit;
using TrueBIM.App.Modules.BimTools.ParameterAudit.Services;
using TrueBIM.App.Modules.BimTools.ParameterAudit.UI;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class ParameterAuditCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());
        try
        {
            const string windowKey = "truebim.parameter-audit";
            if (ModelessWindowService.Activate(windowKey, logger))
            {
                return Result.Succeeded;
            }

            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                logger.Warning("Parameter Audit requested without an active document.");
                TaskDialog.Show("Проверка параметров", "Откройте проект Revit перед проверкой параметров.");
                return Result.Succeeded;
            }

            if (uiDocument.Document.IsFamilyDocument)
            {
                TaskDialog.Show(
                    "Проверка параметров",
                    "В первом релизе проверка выполняется только для проектных RVT-документов.");
                return Result.Succeeded;
            }

            ParameterAuditWindow window = new(
                uiDocument.Document,
                new ParameterAuditProfileReader(),
                new ParameterAuditService(new ParameterAuditRuleEvaluator(), logger),
                new ParameterAuditReportExportService(),
                logger);
            ModelessWindowService.Show(
                windowKey,
                window,
                commandData.Application.MainWindowHandle,
                logger);
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Parameter Audit window.", exception);
            TaskDialog.Show(
                "Проверка параметров",
                "Не удалось открыть инструмент. Используйте логи TrueBIM для диагностики.");
            return Result.Failed;
        }
    }
}
