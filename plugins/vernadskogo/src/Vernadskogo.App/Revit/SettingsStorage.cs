using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Vernadskogo.App.Core;

namespace Vernadskogo.App.Revit;

internal sealed class PanelSettings
{
    public Dictionary<string, string> Materials { get; } = new();
    public StartDirection Direction { get; set; } = StartDirection.South;
}

internal static class SettingsStorage
{
    private static readonly Guid SchemaId = new("FE1154B9-F63A-4076-B8ED-E4A8776EA1F6");

    public static PanelSettings Read(Document document)
    {
        var settings = new PanelSettings();
        Schema? schema = Schema.Lookup(SchemaId);
        if (schema == null) return settings;
        DataStorage? storage = FindStorage(document);
        if (storage == null) return settings;
        Entity entity = storage.GetEntity(schema);
        foreach (var pair in entity.Get<IDictionary<string, string>>(schema.GetField("MaterialNumbers")))
            settings.Materials[pair.Key] = pair.Value;
        int direction = entity.Get<int>(schema.GetField("StartDirection"));
        if (Enum.IsDefined(typeof(StartDirection), direction)) settings.Direction = (StartDirection)direction;
        return settings;
    }

    // Called inside the same transaction as panel writes, so Undo restores both.
    public static void Write(Document document, IReadOnlyDictionary<string, string> numbers, StartDirection direction)
    {
        PanelSettings previous = Read(document);
        foreach (var pair in numbers) previous.Materials[pair.Key] = pair.Value;
        Schema? schema = Schema.Lookup(SchemaId);
        if (schema == null)
        {
            var builder = new SchemaBuilder(SchemaId);
            builder.SetSchemaName("VernadskogoPanelSettingsV1");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddMapField("MaterialNumbers", typeof(string), typeof(string));
            builder.AddSimpleField("StartDirection", typeof(int));
            schema = builder.Finish();
        }
        DataStorage storage = FindStorage(document) ?? DataStorage.Create(document);
        storage.Name = "Vernadskogo.PanelSettings";
        var entity = new Entity(schema);
        entity.Set<IDictionary<string, string>>(schema.GetField("MaterialNumbers"), previous.Materials);
        entity.Set(schema.GetField("StartDirection"), (int)direction);
        storage.SetEntity(entity);
    }

    private static DataStorage? FindStorage(Document document) =>
        new FilteredElementCollector(document).OfClass(typeof(DataStorage))
            .WherePasses(new ExtensibleStorageFilter(SchemaId)).Cast<DataStorage>().FirstOrDefault();
}
