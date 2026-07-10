using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.OpeningViews.Models;
using TrueBIM.App.Modules.BimTools.OpeningViews.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class OpeningViewAnnotationCommand : IExternalCommand
{
    private const string DialogTitle = "Оформление фасада";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                TaskDialog.Show(DialogTitle, "Откройте документ Revit перед запуском оформления фасада.");
                return Result.Succeeded;
            }

            Document document = uiDocument.Document;
            View activeView = uiDocument.ActiveView;
            if (!OpeningViewSourceResolver.CanUseActiveView(activeView, out string viewMessage)
                || activeView is not ViewSection viewSection)
            {
                logger.Warning($"Opening view annotation requested for unsupported view '{activeView?.Name}': {viewMessage}");
                TaskDialog.Show(DialogTitle, viewMessage);
                return Result.Succeeded;
            }

            if (!OpeningViewSourceResolver.TryResolve(document, viewSection, out FamilyInstance? source, out string sourceMessage)
                || source is null)
            {
                logger.Warning($"Opening view source was not resolved for '{viewSection.Name}': {sourceMessage}");
                TaskDialog.Show(DialogTitle, sourceMessage);
                return Result.Succeeded;
            }

            OpeningViewAnnotationService service = new();
            OpeningViewAnnotationPreview preview = service.Preview(document, viewSection, source);
            if (!preview.CanApply)
            {
                TaskDialog.Show(DialogTitle, preview.ToDialogText());
                return Result.Succeeded;
            }

            TaskDialog confirmation = new(DialogTitle)
            {
                MainInstruction = $"Оформить активный фасад «{viewSection.Name}»?",
                MainContent = preview.ToDialogText(),
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
                FooterText = "Эталон MVP: марка над фасадом и габариты проёма по стабильным reference planes семейства."
            };
            if (confirmation.Show() != TaskDialogResult.Yes)
            {
                return Result.Cancelled;
            }

            OpeningViewAnnotationResult result = service.Apply(document, viewSection, source, logger);
            logger.Info(
                $"Opening view '{viewSection.Name}' annotated for ElementId {source.Id}: "
                + $"created={result.CreatedAnnotationCount}, removed={result.RemovedAnnotationCount}.");
            TaskDialog.Show(DialogTitle, result.ToDialogText());
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to annotate opening view.", exception);
            TaskDialog.Show(DialogTitle, $"Не удалось оформить фасад: {exception.Message}");
            return Result.Failed;
        }
    }
}

public sealed class OpeningViewAnnotationCommandAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
    {
        try
        {
            UIDocument? uiDocument = applicationData.ActiveUIDocument;
            return uiDocument is not null
                && OpeningViewSourceResolver.CanUseActiveView(uiDocument.ActiveView, out _);
        }
        catch
        {
            return false;
        }
    }
}
