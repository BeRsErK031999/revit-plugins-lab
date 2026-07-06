using Autodesk.Revit.DB;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.Worksets.Services;

public sealed class WorksharingService
{
    private readonly ITrueBimLogger logger;

    public WorksharingService(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool EnsureWorkshared(Document document, out string message)
    {
        if (document.IsWorkshared)
        {
            message = string.Empty;
            return true;
        }

        if (!document.CanEnableWorksharing())
        {
            message = "Revit не позволяет включить совместную работу для текущего документа.";
            return false;
        }

        logger.Info("Enabling worksharing before creating worksets.");
        document.EnableWorksharing("Shared Levels and Grids", "Workset1");
        message = "Совместная работа включена. Рекомендуется сохранить модель после создания рабочих наборов.";
        return true;
    }
}
