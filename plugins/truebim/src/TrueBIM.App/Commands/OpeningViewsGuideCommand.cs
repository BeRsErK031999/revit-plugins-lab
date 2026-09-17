using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.OpeningViews.UI;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class OpeningViewsGuideCommand : IExternalCommand
{
    private const string DialogTitle = "Методичка: фасады проёмов";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());
        try
        {
            OpeningViewsGuideWindow window = new();
            logger.Info("Opening Views guide requested from the ribbon.");
            RevitModalWindowService.ShowDialog(
                window,
                commandData.Application.MainWindowHandle);
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open the Opening Views guide.", exception);
            TaskDialog.Show(DialogTitle, $"Не удалось открыть методичку: {exception.Message}");
            return Result.Failed;
        }
    }
}
