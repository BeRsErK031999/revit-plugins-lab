using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

public sealed class StoredFinishQuantitySource(
    Document document,
    FinishScheduleSettings settings,
    IReadOnlyDictionary<long, string>? proposedOwnership = null) : IFinishQuantitySource
{
    public FinishQuantityResult Calculate(FinishQuantityRequest request)
    {
        return new StoredFinishQuantityCalculator().Calculate(
            request.Elements.Select(ReadElement), request.Rooms, settings.RoomIdentifier);
    }

    private StoredFinishElement ReadElement(FinishClassifiedElement classified)
    {
        Element? element = document.GetElement(RevitElementIds.Create(classified.Element.ElementId));
        ParameterReference? reference = classified.Category switch
        {
            FinishPreviewCategory.Walls => settings.Walls.OwnershipParameter,
            FinishPreviewCategory.Floors => settings.Floors.OwnershipParameter,
            FinishPreviewCategory.Ceilings => settings.Ceilings.OwnershipParameter,
            _ => null
        };
        Parameter? ownership = element is not null && reference is not null
            ? RevitFinishParameterAccess.FindParameter(element, reference)
            : null;
        string? value = proposedOwnership is not null
            && proposedOwnership.TryGetValue(classified.Element.ElementId, out string? proposed)
                ? proposed
                : ownership?.StorageType == StorageType.String ? ownership.AsString() : null;
        Parameter? area = element?.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
        return new StoredFinishElement(classified.Element.ElementId, classified.Category,
            classified.Element.LevelId, value,
            area?.StorageType == StorageType.Double ? RevitAreaUnits.ToSquareMeters(area.AsDouble()) : 0);
    }
}
