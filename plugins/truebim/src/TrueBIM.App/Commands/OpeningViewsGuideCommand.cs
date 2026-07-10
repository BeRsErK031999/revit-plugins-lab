using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.OpeningViews.UI;
using TrueBIM.App.Services.Logging;

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
            new WindowInteropHelper(window)
            {
                Owner = commandData.Application.MainWindowHandle
            };
            logger.Info("Opening Views guide requested from the ribbon.");
            window.ShowDialog();
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
