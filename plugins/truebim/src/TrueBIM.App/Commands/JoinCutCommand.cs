using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.JoinCut.Services;
using TrueBIM.App.Modules.BimTools.JoinCut.UI;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class JoinCutCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                logger.Warning("Join/Cut requested without an active document.");
                TaskDialog.Show("Соединить / Вырезать", "Откройте проект Revit перед запуском инструмента.");
                return Result.Succeeded;
            }

            Document document = uiDocument.Document;
            if (document.IsFamilyDocument)
            {
                logger.Warning("Join/Cut requested for a family document.");
                TaskDialog.Show("Соединить / Вырезать", "Инструмент доступен только для проектных документов Revit.");
                return Result.Succeeded;
            }

            JoinCutConfigurationStorage storage = new(
                JoinCutConfigurationStorage.CreateDefaultSettingsPath(),
                logger);
            JoinCutConfigurationLoadResult loadResult = storage.Load();
            if (!string.IsNullOrWhiteSpace(loadResult.WarningMessage))
            {
                TaskDialog.Show("Соединить / Вырезать", loadResult.WarningMessage);
            }

            JoinCutWindow window = new(uiDocument, loadResult, storage, logger);
            System.Windows.Interop.WindowInteropHelper helper = new(window)
            {
                Owner = commandData.Application.MainWindowHandle
            };

            window.ShowDialog();
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Join/Cut window.", exception);
            TaskDialog.Show("Соединить / Вырезать", "Не удалось открыть инструмент. Используйте логи для диагностики.");
            return Result.Failed;
        }
    }
}
