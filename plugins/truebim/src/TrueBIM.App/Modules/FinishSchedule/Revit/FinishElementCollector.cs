using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

public sealed class FinishElementCollector
{
    private readonly ITrueBimLogger logger;

    public FinishElementCollector(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public FinishElementCollection Collect(
        Document document,
        FinishScheduleSettings settings)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        ParameterReference? sectionParameter = settings.Scope.Kind == ReportScopeKind.Section
            ? settings.Scope.SectionParameter
            : null;
        List<FinishRoomCandidateSnapshot> rooms = CollectRooms(document, sectionParameter);
        List<FinishElementCandidateSnapshot> walls = CollectPhysicalElements(
            document,
            BuiltInCategory.OST_Walls,
            FinishPhysicalCategory.Wall);
        List<FinishElementCandidateSnapshot> floors = CollectPhysicalElements(
            document,
            BuiltInCategory.OST_Floors,
            FinishPhysicalCategory.Floor);
        Dictionary<long, FinishTypeSnapshot> types = CollectTypes(
            document,
            walls.Concat(floors));

        logger.Info(
            $"Finish Schedule candidates collected. Rooms={rooms.Count}; Walls={walls.Count}; Floors={floors.Count}; Types={types.Count}.");
        return new FinishElementCollection(rooms, walls, floors, types.Values);
    }

    private List<FinishRoomCandidateSnapshot> CollectRooms(
        Document document,
        ParameterReference? sectionParameter)
    {
        List<FinishRoomCandidateSnapshot> result = [];
        foreach (Room room in new FilteredElementCollector(document)
                     .OfCategory(BuiltInCategory.OST_Rooms)
                     .WhereElementIsNotElementType()
                     .Cast<Room>())
        {
            try
            {
                IReadOnlyDictionary<string, FinishParameterValueSnapshot>? values = sectionParameter is null
                    ? null
                    : ReadParameterValues(room, sectionParameter);
                result.Add(new FinishRoomCandidateSnapshot(
                    RevitElementIds.GetValue(room.Id),
                    RevitElementIds.GetValue(room.LevelId),
                    room.Area,
                    room.Location is not null,
                    ReadBounds(room),
                    values));
            }
            catch (Exception exception)
            {
                logger.Warning(
                    $"Finish Schedule skipped room {RevitElementIds.GetValue(room.Id)} while collecting preview: {exception.Message}");
            }
        }

        return result;
    }

    private List<FinishElementCandidateSnapshot> CollectPhysicalElements(
        Document document,
        BuiltInCategory category,
        FinishPhysicalCategory physicalCategory)
    {
        List<FinishElementCandidateSnapshot> result = [];
        foreach (Element element in new FilteredElementCollector(document)
                     .OfCategory(category)
                     .WhereElementIsNotElementType())
        {
            try
            {
                result.Add(new FinishElementCandidateSnapshot(
                    RevitElementIds.GetValue(element.Id),
                    RevitElementIds.GetValue(element.GetTypeId()),
                    physicalCategory,
                    ReadBounds(element)));
            }
            catch (Exception exception)
            {
                logger.Warning(
                    $"Finish Schedule skipped element {RevitElementIds.GetValue(element.Id)} while collecting preview: {exception.Message}");
            }
        }

        return result;
    }

    private Dictionary<long, FinishTypeSnapshot> CollectTypes(
        Document document,
        IEnumerable<FinishElementCandidateSnapshot> elements)
    {
        Dictionary<long, FinishTypeSnapshot> result = [];
        foreach (long typeId in elements
                     .Select(element => element.TypeId)
                     .Where(typeId => typeId > 0)
                     .Distinct()
                     .OrderBy(typeId => typeId))
        {
            try
            {
                Element? type = document.GetElement(RevitElementIds.Create(typeId));
                if (type is null)
                {
                    continue;
                }

                Parameter? classification = type
                    .GetParameters(FinishScheduleSettings.ClassificationParameterName)
                    .FirstOrDefault();
                result[typeId] = new FinishTypeSnapshot(
                    typeId,
                    classification is null ? null : ReadParameterValue(classification).DisplayValue,
                    classification is not null);
            }
            catch (Exception exception)
            {
                logger.Warning(
                    $"Finish Schedule skipped type {typeId} while collecting classification: {exception.Message}");
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, FinishParameterValueSnapshot> ReadParameterValues(
        Element element,
        ParameterReference requestedReference)
    {
        Dictionary<string, FinishParameterValueSnapshot> result = new(StringComparer.Ordinal);
        foreach (Parameter parameter in element.Parameters)
        {
            ParameterReference? reference = RevitParameterReferenceFactory.Create(
                parameter,
                ParameterBindingKind.Instance);
            if (reference?.StableKey == requestedReference.StableKey)
            {
                result[reference.StableKey] = ReadParameterValue(parameter);
                break;
            }
        }

        return result;
    }

    private static FinishParameterValueSnapshot ReadParameterValue(Parameter parameter)
    {
        string rawValue = parameter.StorageType switch
        {
            StorageType.String => parameter.AsString() ?? string.Empty,
            StorageType.Integer => parameter.AsInteger().ToString(CultureInfo.InvariantCulture),
            StorageType.Double => parameter.AsDouble().ToString("R", CultureInfo.InvariantCulture),
            StorageType.ElementId => RevitElementIds.GetValue(parameter.AsElementId())
                .ToString(CultureInfo.InvariantCulture),
            _ => string.Empty
        };
        string displayValue;
        try
        {
            displayValue = parameter.AsValueString() ?? rawValue;
        }
        catch (Exception)
        {
            displayValue = rawValue;
        }

        return new FinishParameterValueSnapshot(rawValue, displayValue);
    }

    private static AxisAlignedBox3D? ReadBounds(Element element)
    {
        BoundingBoxXYZ? bounds = element.get_BoundingBox(null);
        if (bounds is null)
        {
            return null;
        }

        XYZ min = bounds.Min;
        XYZ max = bounds.Max;
        Transform transform = bounds.Transform;
        XYZ[] corners =
        [
            new XYZ(min.X, min.Y, min.Z),
            new XYZ(min.X, min.Y, max.Z),
            new XYZ(min.X, max.Y, min.Z),
            new XYZ(min.X, max.Y, max.Z),
            new XYZ(max.X, min.Y, min.Z),
            new XYZ(max.X, min.Y, max.Z),
            new XYZ(max.X, max.Y, min.Z),
            new XYZ(max.X, max.Y, max.Z)
        ];
        XYZ[] worldCorners = corners.Select(transform.OfPoint).ToArray();
        return new AxisAlignedBox3D(
            worldCorners.Min(point => point.X),
            worldCorners.Min(point => point.Y),
            worldCorners.Min(point => point.Z),
            worldCorners.Max(point => point.X),
            worldCorners.Max(point => point.Y),
            worldCorners.Max(point => point.Z));
    }
}
