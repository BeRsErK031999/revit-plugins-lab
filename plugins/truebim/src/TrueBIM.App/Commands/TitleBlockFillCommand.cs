using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.TitleBlockFill.Services;
using TrueBIM.App.Modules.BimTools.TitleBlockFill.UI;
using TrueBIM.App.Modules.SheetNumbering.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class TitleBlockFillCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                logger.Warning("Title Block Fill requested without an active document.");
                TaskDialog.Show("Оформить штамп", "Откройте документ Revit перед запуском оформления штампа.");
                return Result.Succeeded;
            }

            Document document = uiDocument.Document;
            TitleBlockFillWindow window = new(
                document,
                new SheetCollectorService().Collect(document),
                new TitleBlockProfileStorage(logger),
                new TitleBlockFinderService(),
                new TitleBlockFillService(new TitleBlockValueResolver(), new TitleBlockParameterWriter()),
                logger);
            System.Windows.Interop.WindowInteropHelper helper = new(window)
            {
                Owner = commandData.Application.MainWindowHandle
            };

            window.ShowDialog();
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Title Block Fill window.", exception);
            TaskDialog.Show("Оформить штамп", "Не удалось открыть оформление штампа. Используйте логи для диагностики.");
            return Result.Failed;
        }
    }
}
