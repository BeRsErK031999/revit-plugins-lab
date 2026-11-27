using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.ExtensibleStorage;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

internal static class FinishFloorAreaStorage
{
    private static readonly Guid Id = new("290045DC-AE1A-44B0-BDBB-965846FF88CE");

    internal static IReadOnlyList<FinishOccurrence>? Read(Element element, ParameterReference parameter, string source, string? text)
    {
        var schema = Schema.Lookup(Id);
        if (schema == null) return null;
        using var entity = element.GetEntity(schema);
        if (!entity.IsValid() || entity.Get<string>("Element") != element.UniqueId
            || entity.Get<string>("Parameter") != parameter.StableKey || entity.Get<string>("Source") != source
            || !string.Equals(entity.Get<string>("Label"), text ?? string.Empty, StringComparison.OrdinalIgnoreCase)) return null;
        double currentArea = element.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0;
        if (!double.TryParse(entity.Get<string>("NativeArea"), NumberStyles.Float, CultureInfo.InvariantCulture, out var stored)
            || Math.Abs(currentArea - stored) > 1e-6) return [];
        var result = new List<FinishOccurrence>();
        if (entity.Get<string>("Areas").Length == 0 && string.IsNullOrEmpty(text)) return result;
        foreach (var line in entity.Get<string>("Areas").Split('\n'))
        {
            var fields = line.Split('\t');
            if (fields.Length != 2 || element.Document.GetElement(fields[0]) is not Room room
                || !double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var area)
                || !(area > 0) || double.IsInfinity(area)) return [];
            result.Add(new(RevitElementIds.GetValue(room.Id), RevitElementIds.GetValue(element.Id),
                FinishPreviewCategory.Floors, area, FinishQuantityMethod.FloorRoomSurface));
        }
        return result;
    }

    internal static void Write(Element element, FinishRoomAssignment assignment)
    {
        var schema = Schema.Lookup(Id);
        if (assignment.FloorAreas.Count == 0 && assignment.Identifier.Length > 0)
        {
            if (schema != null && element.GetEntity(schema).IsValid()) element.DeleteEntity(schema);
            return;
        }
        var records = assignment.FloorAreas.OrderBy(a => a.RoomId).Select(a =>
        {
            var room = element.Document.GetElement(RevitElementIds.Create(a.RoomId)) as Room
                ?? throw new InvalidOperationException($"Помещение {a.RoomId} удалено после расчёта.");
            return room.UniqueId + "\t" + a.AreaSquareMeters.ToString("R", CultureInfo.InvariantCulture);
        });
        string payload = string.Join("\n", records);
        if (schema == null)
        {
            var builder = new SchemaBuilder(Id);
            builder.SetSchemaName("TrueBIMFloorRoomAreas");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            foreach (var key in new[] { "Element", "Parameter", "Source", "Label", "NativeArea", "Areas" })
                builder.AddSimpleField(key, typeof(string));
            schema = builder.Finish();
        }
        var nativeArea = element.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED).AsDouble().ToString("R", CultureInfo.InvariantCulture);
        var values = new Dictionary<string, string>
        {
            ["Element"] = element.UniqueId,
            ["Parameter"] = assignment.Reference.StableKey,
            ["Source"] = assignment.IdentifierSourceKey,
            ["Label"] = assignment.Identifier,
            ["NativeArea"] = nativeArea,
            ["Areas"] = payload
        };
        using var old = element.GetEntity(schema);
        if (old.IsValid() && values.All(pair => old.Get<string>(pair.Key) == pair.Value)) return;
        using var entity = new Entity(schema);
        foreach (var pair in values) entity.Set(pair.Key, pair.Value);
        element.SetEntity(entity);
    }
}
