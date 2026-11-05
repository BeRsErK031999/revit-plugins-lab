using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace TrueBIM.App.Modules.GeometricFinishes;

public sealed class FinishOption
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public override string ToString() => Name;
}

public sealed class FinishTypeOption
{
    public string UniqueId { get; set; } = "";
    public string Name { get; set; } = "";
    public double ThicknessMm { get; set; }
    public ElementId Id { get; set; } = ElementId.InvalidElementId;
    public Dictionary<string, string> Parameters { get; set; } = new();
    public override string ToString() => Name;
}

public sealed class FinishSourceWall
{
    public Wall Wall { get; set; } = null!;
    public Transform Transform { get; set; } = Transform.Identity;
    public bool Linked { get; set; }
    public string InstanceKey { get; set; } = "host";
    public string TypeKey { get; set; } = "";
    public string TypeName { get; set; } = "";
}

public sealed class FinishTarget
{
    public Room Room { get; set; } = null!;
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public Curve? Boundary { get; set; }
    public FinishSourceWall? Source { get; set; }
    public IList<CurveLoop> Loops { get; set; } = new List<CurveLoop>();
    public IReadOnlyList<FinishOpeningRectangle> Openings { get; set; } = Array.Empty<FinishOpeningRectangle>();
    public string? OpeningError { get; set; }
}

public sealed class FinishOpeningRectangle
{
    public double Left { get; set; }
    public double Right { get; set; }
    public double Bottom { get; set; }
    public double Top { get; set; }
}

public sealed class FinishIssue
{
    public ElementId RoomId { get; set; } = ElementId.InvalidElementId;
    public string Room { get; set; } = "";
    public string Message { get; set; } = "";
    public override string ToString() => string.IsNullOrEmpty(Room) ? Message : Room + ": " + Message;
}

public sealed class FinishPlan
{
    public List<FinishTarget> Targets { get; } = new();
    public List<FinishIssue> Issues { get; } = new();
    public int RoomCount { get; set; }
}

public sealed class FinishResult
{
    public List<ElementId> CreatedIds { get; } = new();
    public List<FinishIssue> Issues { get; } = new();
    public int CreatedRooms { get; set; }
    public int SkippedRooms { get; set; }
    public bool Cancelled { get; set; }
    public string Summary => Cancelled ? "Создание отменено. Изменения этого запуска отменены."
        : $"Создано элементов: {CreatedIds.Count}. Помещений: {CreatedRooms}. Пропущено помещений: {SkippedRooms}.";
}
