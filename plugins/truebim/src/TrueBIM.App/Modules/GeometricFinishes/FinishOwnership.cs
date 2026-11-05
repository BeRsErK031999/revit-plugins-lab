using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace TrueBIM.App.Modules.GeometricFinishes;

public static class FinishOwnership
{
    public static readonly Guid SchemaId = new("13868CE9-F8D1-4F8A-A377-C1D0A35E83B7");
    private static Schema GetSchema()
    {
        Schema? schema = Schema.Lookup(SchemaId);
        if (schema != null) return schema;
        SchemaBuilder builder = new(SchemaId);
        builder.SetSchemaName("TrueBIMGeometricFinishV1");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField("Identity", typeof(string));
        builder.AddSimpleField("RoomUniqueId", typeof(string));
        builder.AddSimpleField("Kind", typeof(string));
        return builder.Finish();
    }
    public static string Identity(FinishTarget target, FinishKind kind) => target.Room.UniqueId + ":" + kind
        + (kind == FinishKind.Walls ? ":" + target.Source!.InstanceKey + ":" + target.Source.Wall.UniqueId : "");
    public static bool IsManaged(Element element)
    {
        Schema? schema = Schema.Lookup(SchemaId);
        return schema != null && element.GetEntity(schema).IsValid();
    }
    public static HashSet<string> Existing(Document doc)
    {
        Schema? schema = Schema.Lookup(SchemaId);
        if (schema == null) return new HashSet<string>(StringComparer.Ordinal);
        return new HashSet<string>(new FilteredElementCollector(doc).WherePasses(new ExtensibleStorageFilter(SchemaId))
            .Select(e => e.GetEntity(schema)).Where(e => e.IsValid()).Select(e => e.Get<string>("Identity")), StringComparer.Ordinal);
    }
    public static void Stamp(Element element, FinishTarget target, FinishKind kind)
    {
        Entity entity = new(GetSchema());
        entity.Set("Identity", Identity(target, kind));
        entity.Set("RoomUniqueId", target.Room.UniqueId);
        entity.Set("Kind", kind.ToString());
        element.SetEntity(entity);
    }
}
