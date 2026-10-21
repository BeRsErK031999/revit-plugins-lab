using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Vernadskogo.App.Core;

namespace Vernadskogo.App.Revit;

internal sealed class PanelData
{
    public List<PanelPosition> Panels { get; } = new();
    public Dictionary<string, string> Materials { get; } = new();
}

internal static class PanelReader
{
    public static PanelData Read(Document document)
    {
        var result = new PanelData();
        var typeMaterials = new Dictionary<long, Material?>();
        var floorNames = new Dictionary<long, string>();
        foreach (Element element in new FilteredElementCollector(document)
                     .OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType())
        {
            ElementId typeId = element.GetTypeId();
            if (!typeMaterials.TryGetValue(typeId.Value, out Material? material))
            {
                Element? type = document.GetElement(typeId);
                if (type == null
                    || type.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL)?.AsString() != "Фасад"
                    || type.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION)?.AsString() != "Стеновая панель")
                {
                    typeMaterials[typeId.Value] = null;
                    continue;
                }
                Parameter? parameter = type.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM)
                    ?? NamedParameter(type, "Материал несущих конструкций");
                material = parameter?.StorageType == StorageType.ElementId
                    ? document.GetElement(parameter.AsElementId()) as Material : null;
                if (material == null)
                    throw Error(element, "у типа «" + type.Name + "» не задан материал в параметре «Материал несущих конструкций».");
                typeMaterials[typeId.Value] = material;
            }
            if (material == null) continue;

            // Loaded families use «Уровень». Never substitute a wall base constraint.
            Parameter? levelParameter = element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                ?? NamedParameter(element, "Уровень");
            ElementId levelId = levelParameter?.StorageType == StorageType.ElementId
                ? levelParameter.AsElementId() : element.LevelId;
            Level? level = document.GetElement(levelId) as Level;
            if (level == null) throw Error(element, "не удалось определить «Уровень».");
            if (!floorNames.TryGetValue(levelId.Value, out string? floor))
            {
                try { floor = PanelNumbering.ParseFloor(level.Name); }
                catch (InvalidOperationException exception) { throw Error(element, exception.Message); }
                floorNames[levelId.Value] = floor;
            }

            BoundingBoxXYZ? box = element.get_BoundingBox(null);
            if (box == null) throw Error(element, "не удалось определить границы панели.");
            // Transform all corners; ordering always uses the model/project axes.
            var corners = (from x in new[] { box.Min.X, box.Max.X }
                           from y in new[] { box.Min.Y, box.Max.Y }
                           from z in new[] { box.Min.Z, box.Max.Z }
                           select box.Transform.OfPoint(new XYZ(x, y, z))).ToArray();
            result.Panels.Add(new PanelPosition(element.Id.Value, floor, material.UniqueId,
                corners.Min(p => p.X), corners.Min(p => p.Y), corners.Max(p => p.X), corners.Max(p => p.Y)));
            result.Materials[material.UniqueId] = material.Name;
        }
        return result;
    }

    internal static Parameter? NamedParameter(Element element, string name)
    {
        IList<Parameter> parameters = element.GetParameters(name);
        if (parameters.Count > 1) throw Error(element, "найдено несколько параметров «" + name + "».");
        return parameters.SingleOrDefault();
    }

    internal static InvalidOperationException Error(Element element, string message) =>
        new("Элемент " + element.Id.Value + ": " + message);
}
