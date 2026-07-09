using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class OpenPrintPdfCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        TrueBimCommandActions.OpenPrintPdf(
            commandData,
            owner: null,
            new FileTrueBimLogger(new TrueBimLogPaths()));

        return Result.Succeeded;
    }
}
