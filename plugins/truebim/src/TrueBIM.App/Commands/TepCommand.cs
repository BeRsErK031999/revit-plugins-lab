using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.Tep;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.ReadOnly)]
public sealed class TepCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Document? document = commandData.Application.ActiveUIDocument?.Document;
        if (document is null || document.IsFamilyDocument)
        {
            TaskDialog.Show("Проверка ТЭП", "Откройте проект с загруженными связями АР и КР.");
            return Result.Cancelled;
        }
        try
        {
            TepWindow window = new(TepCollector.Documents(document), selection => new TepCollector().Collect(document, selection));
            RevitModalWindowService.ShowDialog(window, commandData.Application.MainWindowHandle);
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            new FileTrueBimLogger(new TrueBimLogPaths()).Error("TEP check failed.", exception);
            message = exception.Message;
            return Result.Failed;
        }
    }
}
