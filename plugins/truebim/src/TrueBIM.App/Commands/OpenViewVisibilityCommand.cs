using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.ViewVisibility.Models;
using TrueBIM.App.Modules.ViewVisibility.Services;
using TrueBIM.App.Modules.ViewVisibility.UI;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class OpenViewVisibilityCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                logger.Warning("View Visibility requested without an active document.");
                TaskDialog.Show("Видимость", "Откройте документ Revit перед настройкой видимости.");
                return Result.Succeeded;
            }

            View activeView = uiDocument.Document.ActiveView;
            if (activeView.IsTemplate)
            {
                logger.Warning("View Visibility requested for a view template.");
                TaskDialog.Show("Видимость", "Откройте рабочий вид Revit. Шаблон вида нельзя настроить этим инструментом.");
                return Result.Succeeded;
            }

            ViewCategoryVisibilityService service = new(logger);
            IReadOnlyList<ViewCategoryVisibilityItem> categories = service.Collect(uiDocument.Document, activeView);
            if (categories.Count == 0)
            {
                logger.Warning($"View Visibility found no controllable categories for view '{activeView.Name}'.");
                TaskDialog.Show("Видимость", "На активном виде не найдены категории, доступные для управления видимостью.");
                return Result.Succeeded;
            }

            ViewVisibilityWindow window = new(uiDocument.Document, activeView, categories, service, logger);
            window.ShowDialog();
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open View Visibility window.", exception);
            TaskDialog.Show("Видимость", "Не удалось открыть управление видимостью. Используйте логи для диагностики.");
            return Result.Failed;
        }
    }
}
