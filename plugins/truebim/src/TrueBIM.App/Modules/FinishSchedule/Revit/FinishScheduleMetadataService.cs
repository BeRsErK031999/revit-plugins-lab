using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

public sealed class FinishScheduleMetadataService
{
    public const string FeatureId = "TrueBIM.FinishSchedule";
    public const int CurrentSchemaVersion = 1;

    private static readonly Guid SchemaGuid = new("C72569A6-4C99-4CD0-92D0-0198730E4551");
    private const string SchemaName = "TrueBIMFinishSchedule";
    private const string SchemaVersionField = "SchemaVersion";
    private const string FeatureIdField = "FeatureId";
    private const string SettingsHashField = "SettingsHash";
    private const string ScopeField = "Scope";
    private const string ParameterIdentitiesField = "ParameterIdentities";
    private const string LastUpdatedUtcField = "LastUpdatedUtc";

    public FinishScheduleMetadata? Read(ViewSchedule schedule)
    {
        if (schedule is null)
        {
            throw new ArgumentNullException(nameof(schedule));
        }

        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is null)
        {
            return null;
        }

        try
        {
            Entity entity = schedule.GetEntity(schema);
            if (!entity.IsValid())
            {
                return null;
            }

            return new FinishScheduleMetadata(
                entity.Get<int>(schema.GetField(SchemaVersionField)),
                entity.Get<string>(schema.GetField(FeatureIdField)) ?? string.Empty,
                entity.Get<string>(schema.GetField(SettingsHashField)) ?? string.Empty,
                entity.Get<string>(schema.GetField(ScopeField)) ?? string.Empty,
                Split(entity.Get<string>(schema.GetField(ParameterIdentitiesField)) ?? string.Empty),
                entity.Get<string>(schema.GetField(LastUpdatedUtcField)) ?? string.Empty);
        }
        catch
        {
            return null;
        }
    }

    public bool IsManaged(ViewSchedule schedule)
    {
        FinishScheduleMetadata? metadata = Read(schedule);
        return metadata is not null
            && metadata.SchemaVersion == CurrentSchemaVersion
            && string.Equals(metadata.FeatureId, FeatureId, StringComparison.Ordinal);
    }

    public void Write(ViewSchedule schedule, FinishRoomSchedulePlan plan)
    {
        if (schedule is null)
        {
            throw new ArgumentNullException(nameof(schedule));
        }

        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        Schema schema = GetOrCreateSchema();
        Entity entity = new(schema);
        entity.Set(schema.GetField(SchemaVersionField), CurrentSchemaVersion);
        entity.Set(schema.GetField(FeatureIdField), FeatureId);
        entity.Set(schema.GetField(SettingsHashField), plan.SettingsHash);
        entity.Set(schema.GetField(ScopeField), plan.ScopeFilter.Kind.ToString());
        entity.Set(schema.GetField(ParameterIdentitiesField), string.Join("\n", plan.ParameterIdentities));
        entity.Set(schema.GetField(LastUpdatedUtcField), DateTime.UtcNow.ToString("O"));
        schedule.SetEntity(entity);
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
        builder.SetDocumentation("Marks and versions the room finish schedule managed by TrueBIM.");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(SchemaVersionField, typeof(int));
        builder.AddSimpleField(FeatureIdField, typeof(string));
        builder.AddSimpleField(SettingsHashField, typeof(string));
        builder.AddSimpleField(ScopeField, typeof(string));
        builder.AddSimpleField(ParameterIdentitiesField, typeof(string));
        builder.AddSimpleField(LastUpdatedUtcField, typeof(string));
        return builder.Finish();
    }

    private static IReadOnlyList<string> Split(string value)
    {
        return value.Split(['\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
    }
}
