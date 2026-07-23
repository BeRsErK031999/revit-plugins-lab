using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.SharedParameters.Revit;
using TrueBIM.App.Modules.SharedParameters.Services;
using TrueBIM.App.Modules.SharedParameters.UI;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class SharedParameterInspectorCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());
        try
        {
            const string windowKey = "truebim.shared-parameter-inspector";
            if (ModelessWindowService.Activate(windowKey, logger))
            {
                return Result.Succeeded;
            }

            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                logger.Warning("Shared Parameter Inspector requested without an active document.");
                TaskDialog.Show(
                    "Общие параметры",
                    "Откройте проект или семейство Revit перед запуском анализа общих параметров.");
                return Result.Succeeded;
            }

            SharedParameterVersionAdapter adapter = new();
            SharedParameterProjectCatalogService catalogService = new(adapter);
            SharedParameterViewFilterService viewFilterService = new(adapter);
            SharedParameterUsageSummaryBuilder summaryBuilder = new();
            SharedParameterProjectAnalysisService projectAnalysisService = new(
                catalogService,
                viewFilterService,
                summaryBuilder,
                logger);
            SharedParameterFamilyAnalysisService familyAnalysisService = new(
                adapter,
                new FamilyFormulaDependencyParser(),
                logger);
            SharedParameterDeletionWorkflow deletionWorkflow = new(
                catalogService,
                projectAnalysisService,
                familyAnalysisService,
                viewFilterService,
                adapter,
                logger);
            SharedParameterInspectorWindow window = new(
                commandData.Application,
                catalogService,
                projectAnalysisService,
                familyAnalysisService,
                deletionWorkflow,
                new SharedParameterDeletionPlanBuilder(),
                new SharedParameterSearchService(),
                new SharedParameterFamilyFileScanner(),
                new SharedParameterReportExportService(),
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
            logger.Error("Failed to open Shared Parameter Inspector.", exception);
            TaskDialog.Show(
                "Общие параметры",
                "Не удалось открыть инструмент. Используйте логи TrueBIM для диагностики.");
            return Result.Failed;
        }
    }
}
