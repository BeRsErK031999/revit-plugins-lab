using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.Updates;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class CheckForUpdatesCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());
        try
        {
            UpdateWindow window = new(commandData.Application.Application.VersionNumber, logger);
            RevitModalWindowService.ShowDialog(window, commandData.Application.MainWindowHandle);
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open TrueBIM updates.", exception);
            TaskDialog.Show("Обновления TrueBIM", "Не удалось открыть проверку обновлений. Подробности записаны в логи TrueBIM.");
            return Result.Failed;
        }
    }
}

public sealed class CheckForUpdatesAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => true;
}
