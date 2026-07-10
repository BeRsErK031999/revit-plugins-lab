using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using TrueBIM.App.Modules.BimTools.OpeningViews.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.OpeningViews.Services;

public static class OpeningViewMetadataService
{
    private static readonly Guid SchemaGuid = new("D0A747F5-7DF0-41EE-A63D-79FBB04362F6");
    private const string SchemaName = "TrueBIMOpeningViewMetadata";
    private const string SourceUniqueIdField = "SourceElementUniqueId";
    private const string SourceElementIdField = "SourceElementId";
    private const string CategoryKeyField = "CategoryKey";
    private const string AnnotationUniqueIdsField = "AnnotationUniqueIds";
    private const string VersionField = "MetadataVersion";
    private const int CurrentVersion = 1;

    public static OpeningViewMetadata? Read(View view)
    {
        Guard.NotNull(view, nameof(view));

        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is null)
        {
            return null;
        }

        try
        {
            Entity entity = view.GetEntity(schema);
            if (!entity.IsValid())
            {
                return null;
            }

            string sourceUniqueId = entity.Get<string>(schema.GetField(SourceUniqueIdField)) ?? string.Empty;
            string sourceElementIdText = entity.Get<string>(schema.GetField(SourceElementIdField)) ?? string.Empty;
            string categoryKey = entity.Get<string>(schema.GetField(CategoryKeyField)) ?? OpeningViewCategoryKeys.Door;
            string annotationIdsText = entity.Get<string>(schema.GetField(AnnotationUniqueIdsField)) ?? string.Empty;
            long.TryParse(sourceElementIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long sourceElementId);

            return new OpeningViewMetadata(
                sourceUniqueId,
                sourceElementId,
                categoryKey,
                SplitAnnotationUniqueIds(annotationIdsText));
        }
        catch
        {
            return null;
        }
    }

    public static void WriteSource(View view, Element sourceElement, string categoryKey)
    {
        Guard.NotNull(view, nameof(view));
        Guard.NotNull(sourceElement, nameof(sourceElement));

        OpeningViewMetadata? existing = Read(view);
        Write(
            view,
            new OpeningViewMetadata(
                sourceElement.UniqueId,
                RevitElementIds.GetValue(sourceElement.Id),
                categoryKey,
                existing?.AnnotationUniqueIds));
    }

    public static void WriteAnnotations(
        View view,
        Element sourceElement,
        string categoryKey,
        IReadOnlyList<Element> annotations)
    {
        Guard.NotNull(view, nameof(view));
        Guard.NotNull(sourceElement, nameof(sourceElement));
        Guard.NotNull(annotations, nameof(annotations));

        Write(
            view,
            new OpeningViewMetadata(
                sourceElement.UniqueId,
                RevitElementIds.GetValue(sourceElement.Id),
                categoryKey,
                annotations.Select(annotation => annotation.UniqueId).ToList()));
    }

    private static void Write(View view, OpeningViewMetadata metadata)
    {
        Schema schema = GetOrCreateSchema();
        Entity entity = new(schema);
        entity.Set(schema.GetField(SourceUniqueIdField), metadata.SourceElementUniqueId);
        entity.Set(schema.GetField(SourceElementIdField), metadata.SourceElementId.ToString(CultureInfo.InvariantCulture));
        entity.Set(schema.GetField(CategoryKeyField), metadata.CategoryKey);
        entity.Set(schema.GetField(AnnotationUniqueIdsField), string.Join("\n", metadata.AnnotationUniqueIds));
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
        builder.SetDocumentation("Links a TrueBIM opening elevation view to its door/window and generated annotations.");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(SourceUniqueIdField, typeof(string));
        builder.AddSimpleField(SourceElementIdField, typeof(string));
        builder.AddSimpleField(CategoryKeyField, typeof(string));
        builder.AddSimpleField(AnnotationUniqueIdsField, typeof(string));
        builder.AddSimpleField(VersionField, typeof(int));
        return builder.Finish();
    }

    private static IReadOnlyList<string> SplitAnnotationUniqueIds(string value)
    {
        return value.Split(['\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
