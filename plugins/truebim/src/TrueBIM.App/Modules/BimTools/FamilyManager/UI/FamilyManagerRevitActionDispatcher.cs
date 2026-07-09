using Autodesk.Revit.UI;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.UI;

public sealed class FamilyManagerRevitActionDispatcher
{
    private readonly FamilyManagerExternalEventHandler handler;
    private readonly ExternalEvent externalEvent;

    public FamilyManagerRevitActionDispatcher(ITrueBimLogger logger)
    {
        handler = new FamilyManagerExternalEventHandler(logger);
        externalEvent = ExternalEvent.Create(handler);
    }

    public void Raise(Action action)
    {
        handler.Raise(externalEvent, action);
    }

    private sealed class FamilyManagerExternalEventHandler : IExternalEventHandler
    {
        private readonly ITrueBimLogger logger;
        private readonly Queue<Action> pendingActions = new();

        public FamilyManagerExternalEventHandler(ITrueBimLogger logger)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Raise(ExternalEvent externalEvent, Action action)
        {
            pendingActions.Enqueue(action ?? throw new ArgumentNullException(nameof(action)));
            externalEvent.Raise();
        }

        public void Execute(UIApplication app)
        {
            while (pendingActions.Count > 0)
            {
                Action action = pendingActions.Dequeue();
                try
                {
                    action.Invoke();
                }
                catch (Exception exception)
                {
                    logger.Error("Failed to execute Family Manager Revit action.", exception);
                    TaskDialog.Show("Family Manager", "Не удалось выполнить действие Family Manager. Используйте логи для диагностики.");
                }
            }
        }

        public string GetName()
        {
            return "TrueBIM Family Manager Action";
        }
    }
}
