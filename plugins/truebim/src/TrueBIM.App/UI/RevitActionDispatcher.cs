using Autodesk.Revit.UI;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.UI;

public sealed class RevitActionDispatcher
{
    private readonly RevitActionExternalEventHandler handler;
    private readonly ExternalEvent externalEvent;

    public RevitActionDispatcher(string actionName, ITrueBimLogger logger)
    {
        Guard.NotNullOrWhiteSpace(actionName, nameof(actionName));
        handler = new RevitActionExternalEventHandler(actionName, logger ?? throw new ArgumentNullException(nameof(logger)));
        externalEvent = ExternalEvent.Create(handler);
    }

    public void Raise(Action action)
    {
        handler.Enqueue(action ?? throw new ArgumentNullException(nameof(action)));
        ExternalEventRequest request = externalEvent.Raise();
        if (request is ExternalEventRequest.Denied or ExternalEventRequest.TimedOut)
        {
            handler.Cancel(action);
            handler.ReportRejected(request);
        }
    }

    private sealed class RevitActionExternalEventHandler : IExternalEventHandler
    {
        private readonly string actionName;
        private readonly ITrueBimLogger logger;
        private readonly object syncRoot = new();
        private readonly Queue<Action> pendingActions = new();

        public RevitActionExternalEventHandler(string actionName, ITrueBimLogger logger)
        {
            this.actionName = actionName;
            this.logger = logger;
        }

        public void Enqueue(Action action)
        {
            lock (syncRoot)
            {
                pendingActions.Enqueue(action);
            }
        }

        public void Cancel(Action action)
        {
            lock (syncRoot)
            {
                if (pendingActions.Count == 0)
                {
                    return;
                }

                Queue<Action> retainedActions = new();
                bool removed = false;
                while (pendingActions.Count > 0)
                {
                    Action pendingAction = pendingActions.Dequeue();
                    if (!removed && ReferenceEquals(pendingAction, action))
                    {
                        removed = true;
                        continue;
                    }

                    retainedActions.Enqueue(pendingAction);
                }

                while (retainedActions.Count > 0)
                {
                    pendingActions.Enqueue(retainedActions.Dequeue());
                }
            }
        }

        public void Execute(UIApplication app)
        {
            while (TryDequeue(out Action? action))
            {
                try
                {
                    action!.Invoke();
                }
                catch (Exception exception)
                {
                    logger.Error($"Failed to execute Revit action '{actionName}'.", exception);
                    TaskDialog.Show(
                        "TrueBIM",
                        $"Не удалось выполнить действие «{actionName}». Используйте логи для диагностики.");
                }
            }
        }

        public string GetName()
        {
            return $"TrueBIM: {actionName}";
        }

        public void ReportRejected(ExternalEventRequest request)
        {
            logger.Warning($"Revit rejected external event '{actionName}': {request}.");
            TaskDialog.Show(
                "TrueBIM",
                $"Revit не принял действие «{actionName}». Повторите команду после завершения текущей операции.");
        }

        private bool TryDequeue(out Action? action)
        {
            lock (syncRoot)
            {
                if (pendingActions.Count == 0)
                {
                    action = null;
                    return false;
                }

                action = pendingActions.Dequeue();
                return true;
            }
        }
    }
}
