using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace TrueBIM.App.Modules.Lintels.Revit;

internal static class LintelAssemblyViewMetadataService
{
    private static readonly Guid SchemaGuid = new("D37CAAF6-18D1-4BBA-B903-FD8D68FC97FE");
    private const string SchemaName = "TrueBIMLintelAssemblyViewMetadata";
    private const string AssemblyUniqueIdField = "AssemblyUniqueId";
    private const string AnnotationUniqueIdsField = "AnnotationUniqueIds";
    private const string VersionField = "MetadataVersion";
    private const int CurrentVersion = 1;

    public static IReadOnlyList<string> ReadAnnotationUniqueIds(View view)
    {
        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is null)
        {
            return [];
        }

        try
        {
            Entity entity = view.GetEntity(schema);
            if (!entity.IsValid())
            {
                return [];
            }

            string value = entity.Get<string>(schema.GetField(AnnotationUniqueIdsField)) ?? string.Empty;
            return value.Split(['\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static void Write(View view, AssemblyInstance assembly, IReadOnlyList<Element> annotations)
    {
        Schema schema = GetOrCreateSchema();
        Entity entity = new(schema);
        entity.Set(schema.GetField(AssemblyUniqueIdField), assembly.UniqueId);
        entity.Set(
            schema.GetField(AnnotationUniqueIdsField),
            string.Join("\n", annotations.Select(annotation => annotation.UniqueId)));
        entity.Set(schema.GetField(VersionField), CurrentVersion);
        view.SetEntity(entity);
    }

    private static Schema GetOrCreateSchema()
    {
        Schema? existing = Schema.Lookup(SchemaGuid);
        if (existing is not null)
        {
            return existing;
        }

        SchemaBuilder builder = new(SchemaGuid);
        builder.SetSchemaName(SchemaName);
        builder.SetDocumentation("Links a TrueBIM lintel assembly side view to its generated annotations.");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(AssemblyUniqueIdField, typeof(string));
        builder.AddSimpleField(AnnotationUniqueIdsField, typeof(string));
        builder.AddSimpleField(VersionField, typeof(int));
        return builder.Finish();
    }
}
