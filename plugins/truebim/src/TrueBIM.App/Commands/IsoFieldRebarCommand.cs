using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.IsoFieldRebar.Revit;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using TrueBIM.App.Modules.IsoFieldRebar.UI;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class IsoFieldRebarCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            const string windowKey = "truebim.isofield-rebar";
            if (ModelessWindowService.Activate(windowKey, logger))
            {
                return Result.Succeeded;
            }

            logger.Info("Opening IsoField Rebar window.");
            UIDocument? activeUiDocument = commandData.Application.ActiveUIDocument;
            string? documentTitle = activeUiDocument?.Document?.Title;
            IsoFieldRebarWindow window = new(
                documentTitle,
                activeUiDocument,
                new IsoFieldFilePicker(),
                new IsoFieldJsonReader(),
                IsoFieldRecognitionRunnerFactory.Create(logger),
                new IsoFieldRevitPreviewService(logger),
                new IsoFieldHostSelectionService(),
                new IsoFieldRebarCreationService(logger),
                logger);
            ModelessWindowService.Show(windowKey, window, commandData.Application.MainWindowHandle, logger);
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
