using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.SelectionBox;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class SelectionBoxCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
                throw new InvalidOperationException("Откройте проект и выберите элементы модели.");
            SelectionBoxRequest.Start(commandData.Application);
            return Result.Succeeded;
        }
        catch (InvalidOperationException exception)
        {
            TaskDialog.Show("Рамка выбора", exception.Message);
            return Result.Cancelled;
        }
        catch (Exception exception)
        {
            new FileTrueBimLogger(new TrueBimLogPaths()).Error("Selection box failed.", exception);
            TaskDialog.Show("Рамка выбора", "Не удалось ограничить 3D-вид. Проверьте шаблон и права на редактирование вида.\n\n" + exception.Message);
            return Result.Cancelled;
        }
    }
}
