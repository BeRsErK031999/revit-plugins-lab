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

        List<FinishRoomCandidateSnapshot> rooms = CollectRooms(document, settings);
        List<FinishElementCandidateSnapshot> walls = CollectPhysicalElements(
            document,
            BuiltInCategory.OST_Walls,
            FinishPhysicalCategory.Wall);
        List<FinishElementCandidateSnapshot> floors = CollectPhysicalElements(
            document,
            BuiltInCategory.OST_Floors,
            FinishPhysicalCategory.Floor);
        List<FinishElementCandidateSnapshot> ceilings = CollectPhysicalElements(
            document,
            BuiltInCategory.OST_Ceilings,
            FinishPhysicalCategory.Ceiling);
        Dictionary<long, FinishTypeSnapshot> types = CollectTypes(
            document,
            walls.Concat(floors).Concat(ceilings),
            settings.DescriptionParameter);

        logger.Info(
            $"Finish Schedule candidates collected. Rooms={rooms.Count}; Walls={walls.Count}; Floors={floors.Count}; Ceilings={ceilings.Count}; Types={types.Count}.");
        return new FinishElementCollection(rooms, walls, floors, ceilings, types.Values);
    }

    private List<FinishRoomCandidateSnapshot> CollectRooms(
        Document document,
        FinishScheduleSettings settings)
    {
        ParameterReference?[] requestedParameters =
        [
            settings.Scope.Kind == ReportScopeKind.Section
                ? settings.Scope.SectionParameter
                : null,
            settings.RoomIdentifier.Mode == RoomIdentifierMode.CustomParameter
                ? settings.RoomIdentifier.CustomParameter
                : null
        ];
        List<FinishRoomCandidateSnapshot> result = [];
        foreach (Room room in new FilteredElementCollector(document)
                     .OfCategory(BuiltInCategory.OST_Rooms)
                     .WhereElementIsNotElementType()
                     .Cast<Room>())
        {
            try
            {
                IReadOnlyDictionary<string, FinishParameterValueSnapshot> values = ReadParameterValues(
                    room,
                    requestedParameters.OfType<ParameterReference>(),
                    ParameterBindingKind.Instance);
                result.Add(new FinishRoomCandidateSnapshot(
                    RevitElementIds.GetValue(room.Id),
                    RevitElementIds.GetValue(room.LevelId),
                    room.Area,
                    room.Location is not null,
                    ReadBounds(room),
                    values,
                    room.Number,
                    room.Name));
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
        IEnumerable<FinishElementCandidateSnapshot> elements,
        ParameterReference? descriptionReference)
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
                FinishParameterValueSnapshot? description = descriptionReference is null
                    ? null
                    : FindParameterValue(
                        type,
                        descriptionReference,
                        ParameterBindingKind.Type);
                result[typeId] = new FinishTypeSnapshot(
                    typeId,
                    classification is null ? null : ReadParameterValue(classification).DisplayValue,
                    classification is not null,
                    description,
                    description is not null);
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
        IEnumerable<ParameterReference> requestedReferences,
        ParameterBindingKind bindingKind)
    {
        Dictionary<string, ParameterReference> requested = (requestedReferences
                ?? throw new ArgumentNullException(nameof(requestedReferences)))
            .GroupBy(reference => reference.StableKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        Dictionary<string, FinishParameterValueSnapshot> result = new(StringComparer.Ordinal);
        foreach (Parameter parameter in element.Parameters)
        {
            ParameterReference? reference = RevitParameterReferenceFactory.Create(
                parameter,
                bindingKind);
            if (reference is not null && requested.ContainsKey(reference.StableKey))
            {
                result[reference.StableKey] = ReadParameterValue(parameter);
                if (result.Count == requested.Count)
                {
                    break;
                }
            }
        }

        return result;
    }

    private static FinishParameterValueSnapshot? FindParameterValue(
        Element element,
        ParameterReference requestedReference,
        ParameterBindingKind bindingKind)
    {
        IReadOnlyDictionary<string, FinishParameterValueSnapshot> values = ReadParameterValues(
            element,
            [requestedReference],
            bindingKind);
        return values.TryGetValue(requestedReference.StableKey, out FinishParameterValueSnapshot? value)
            ? value
            : null;
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
