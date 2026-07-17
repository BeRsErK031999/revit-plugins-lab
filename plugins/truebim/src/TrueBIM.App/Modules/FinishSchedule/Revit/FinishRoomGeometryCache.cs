using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

internal sealed class FinishRoomGeometryCache : IDisposable
{
    private readonly Document document;
    private readonly SpatialElementGeometryCalculator calculator;
    private readonly Dictionary<long, FinishRoomGeometryData> cache = [];

    public FinishRoomGeometryCache(Document document)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        SpatialElementBoundaryOptions options = new()
        {
            SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
        };
        calculator = new SpatialElementGeometryCalculator(document, options);
    }

    public bool TryGet(
        long roomId,
        out FinishRoomGeometryData? geometry,
        out FinishGeometryWarning? warning)
    {
        if (cache.TryGetValue(roomId, out geometry))
        {
            warning = null;
            return true;
        }

        Room? room = document.GetElement(RevitElementIds.Create(roomId)) as Room;
        if (room is null)
        {
            warning = new FinishGeometryWarning(
                FinishGeometryWarningCode.RoomNotFound,
                $"Помещение {roomId} не найдено в документе во время расчёта геометрии.",
                RoomId: roomId);
            return false;
        }

        try
        {
            SpatialElementGeometryResults results = calculator.CalculateSpatialElementGeometry(room);
            Solid solid = results.GetGeometry();
            if (solid is null || solid.Faces.Size == 0 || solid.Volume <= 1e-12)
            {
                warning = CreateUnavailableWarning(roomId, "room solid пуст");
                return false;
            }

            geometry = new FinishRoomGeometryData(room, solid, results);
            cache.Add(roomId, geometry);
            warning = null;
            return true;
        }
        catch (Exception exception)
        {
            warning = CreateUnavailableWarning(roomId, exception.Message);
            return false;
        }
    }

    public void Dispose()
    {
        calculator.Dispose();
    }

    private static FinishGeometryWarning CreateUnavailableWarning(long roomId, string details)
    {
        return new FinishGeometryWarning(
            FinishGeometryWarningCode.RoomGeometryUnavailable,
            $"Не удалось получить геометрию помещения {roomId}: {details}.",
            RoomId: roomId);
    }
}

internal sealed record FinishRoomGeometryData(
    Room Room,
    Solid Solid,
    SpatialElementGeometryResults Results);
