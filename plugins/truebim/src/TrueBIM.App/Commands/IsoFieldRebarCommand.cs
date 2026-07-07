using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class IsoFieldRebarCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            logger.Info("Opening IsoField Rebar module scaffold.");

            IIsoFieldRecognitionRunner recognitionRunner = new StubIsoFieldRecognitionRunner();
            IsoFieldRecognitionResult recognitionResult = recognitionRunner.Run(sourcePath: null);

            TaskDialog.Show(
                "Армирование по изополям",
                "Модуль \"Армирование по изополям\" подключен. Следующий этап - выбор файла изополей и предпросмотр распознавания.");

            logger.Info($"IsoField Rebar scaffold opened. Recognized polylines: {recognitionResult.Polylines.Count}.");
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open IsoField Rebar module scaffold.", exception);
            TaskDialog.Show(
                "Армирование по изополям",
                "Не удалось открыть модуль армирования по изополям. Используйте логи для диагностики.");
            return Result.Failed;
        }
    }
}
