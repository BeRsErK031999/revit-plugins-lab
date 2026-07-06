using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class CreateWorksetsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        TrueBimCommandActions.OpenBimToolPlaceholder(
            commandData,
            BimToolPlaceholders.CreateWorksets,
            owner: null,
            new FileTrueBimLogger(new TrueBimLogPaths()));

        return Result.Succeeded;
    }
}
