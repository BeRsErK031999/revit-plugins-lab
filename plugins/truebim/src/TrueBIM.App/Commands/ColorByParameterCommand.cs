using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.ColorByParameter.Services;
using TrueBIM.App.Modules.BimTools.ColorByParameter.UI;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class ColorByParameterCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                logger.Warning("Color By Parameter requested without an active document.");
                TaskDialog.Show("Цвета по параметрам", "Откройте документ Revit перед настройкой раскраски.");
                return Result.Succeeded;
            }

            View activeView = uiDocument.ActiveView;
            if (activeView.IsTemplate || !activeView.AreGraphicsOverridesAllowed())
            {
                TaskDialog.Show("Цвета по параметрам", "Активный вид не поддерживает графические переопределения.");
                return Result.Succeeded;
            }

            ColorByParameterService service = new(new ViewFilterService(new FilterNameBuilder(), logger), new ColorPaletteService(), logger);
            IReadOnlyList<Modules.BimTools.ColorByParameter.Models.BimCategoryItem> categories =
                service.CollectCategories(uiDocument.Document, activeView);
            if (categories.Count == 0)
            {
                TaskDialog.Show("Цвета по параметрам", "На активном виде не найдено элементов с категориями, доступными для фильтров.");
                return Result.Succeeded;
            }

            ColorByParameterWindow window = new(uiDocument.Document, activeView, categories, service, logger);
            window.ShowDialog();
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Color By Parameter window.", exception);
            TaskDialog.Show("Цвета по параметрам", "Не удалось открыть инструмент раскраски. Используйте логи для диагностики.");
            return Result.Failed;
        }
    }
}
