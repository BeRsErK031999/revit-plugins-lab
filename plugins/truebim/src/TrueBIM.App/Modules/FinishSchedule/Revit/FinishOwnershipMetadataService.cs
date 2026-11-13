using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.ExtensibleStorage;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

/// <summary>Keeps automatic physical ownership when several rooms share a report identifier.</summary>
internal sealed class FinishOwnershipMetadataService
{
    private static readonly Guid SchemaGuid = new("F8F7F504-5DB7-4696-8FB0-27CF581DA592");
    private static readonly string[] Fields = ["ElementUniqueId", "RoomUniqueId", "ParameterKey", "IdentifierSourceKey", "IdentifierValue"];

    public long? ReadRoomId(Element element, ParameterReference reference, string sourceKey, string? value)
    {
        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is null)
        {
            return null;
        }

        try
        {
            Entity entity = element.GetEntity(schema);
            if (!entity.IsValid()
                || entity.Get<string>(Fields[0]) != element.UniqueId
                || entity.Get<string>(Fields[2]) != reference.StableKey
                || entity.Get<string>(Fields[3]) != sourceKey
                || !SameIdentifier(entity.Get<string>(Fields[4]), value))
            {
                return null;
            }

            return element.Document.GetElement(entity.Get<string>(Fields[1])) is Room room
                ? RevitElementIds.GetValue(room.Id)
                : null;
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            return null;
        }
    }

    public void Write(Element element, FinishRoomAssignment assignment)
    {
        Schema? schema = Schema.Lookup(SchemaGuid);
        if (!assignment.RoomId.HasValue)
        {
            if (schema is not null && element.GetEntity(schema).IsValid())
            {
                element.DeleteEntity(schema);
            }
            return;
        }

        Room room = element.Document.GetElement(RevitElementIds.Create(assignment.RoomId.Value)) as Room
            ?? throw new InvalidOperationException($"Помещение {assignment.RoomId.Value} не найдено после предпросмотра.");
        if (ReadRoomId(element, assignment.Reference, assignment.IdentifierSourceKey, assignment.Identifier) == assignment.RoomId)
        {
            return;
        }

        schema ??= CreateSchema();
        Entity entity = new(schema);
        string[] values = [element.UniqueId, room.UniqueId, assignment.Reference.StableKey,
            assignment.IdentifierSourceKey, assignment.Identifier];
        for (int index = 0; index < Fields.Length; index++)
        {
            entity.Set(Fields[index], values[index]);
        }
        element.SetEntity(entity);
    }

    private static bool SameIdentifier(string? first, string? second) => string.Equals(
        FinishDescriptionNormalizer.CollapseWhitespace(first),
        FinishDescriptionNormalizer.CollapseWhitespace(second), StringComparison.OrdinalIgnoreCase);

    private static Schema CreateSchema()
    {
        SchemaBuilder builder = new(SchemaGuid);
        builder.SetSchemaName("TrueBIMFinishOwnership");
        builder.SetDocumentation("Physical room association for an automatically assigned finish element.");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        foreach (string field in Fields)
        {
            builder.AddSimpleField(field, typeof(string));
        }
        return builder.Finish();
    }
}
