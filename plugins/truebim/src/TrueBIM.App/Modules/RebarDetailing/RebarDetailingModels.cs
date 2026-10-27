using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.RebarDetailing;

public sealed class DetailingOptions
{
    public bool Tags { get; set; } = true;
    public bool ConcreteDimensions { get; set; } = true;
    public bool BarDimensions { get; set; } = true;
    public bool Elevations { get; set; } = true;
    public bool ReplaceExisting { get; set; } = true;
    public ElementId TagTypeId { get; set; } = ElementId.InvalidElementId;
    public ElementId DimensionTypeId { get; set; } = ElementId.InvalidElementId;
    public ElementId SpotTypeId { get; set; } = ElementId.InvalidElementId;
    public ElementId MultiTypeId { get; set; } = ElementId.InvalidElementId;
}

public sealed class DetailingType
{
    public ElementId Id { get; }
    public string Name { get; }
    public DetailingType(ElementId id, string name) { Id = id; Name = name; }
}

public sealed class DetailingViewRow
{
    public View View { get; }
    public bool Selected { get; set; } = true;
    public string Name => View.Name;
    public int Scale => View.Scale;
    public string Kind => Math.Abs(View.ViewDirection.Z) > 0.9 ? "Горизонтальный узел" : "Вертикальное сечение";
    public int Bars { get; internal set; }
    public int Hosts { get; internal set; }
    public int Existing { get; internal set; }
    public string Status { get; internal set; } = "Готов к оформлению";
    public DetailingViewRow(View view) { View = view; }
}

public sealed class DetailingResult
{
    public string ViewName { get; internal set; } = "";
    public bool Succeeded { get; internal set; }
    public int Tags { get; internal set; }
    public int Dimensions { get; internal set; }
    public int Elevations { get; internal set; }
    public int Removed { get; internal set; }
    public List<string> Notes { get; } = new();
    public override string ToString() => Succeeded
        ? $"{ViewName}: марки {Tags}, размеры {Dimensions}, отметки {Elevations}; удалено {Removed}." + (Notes.Count == 0 ? "" : "\n" + string.Join("\n", Notes.Distinct()))
        : $"{ViewName}: изменения отменены. " + string.Join("; ", Notes.Distinct());
}
