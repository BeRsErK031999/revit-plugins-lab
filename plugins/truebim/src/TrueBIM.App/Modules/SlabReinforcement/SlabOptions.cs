#if REVIT2024_OR_GREATER && NET48
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace TrueBIM.App.Modules.SlabReinforcement;

internal sealed class SlabOptions
{
    internal RebarBarType[] Bars { get; }
    internal double[] Depths { get; }
    internal double Step { get; }
    internal double EndCover { get; }
    internal double? ManualLeg { get; }
    internal double Rbt { get; }
    internal double Rs { get; }
    internal bool ReplaceManual { get; set; }
    internal bool PlaceFrames { get; set; }
    internal bool ReplaceFrames { get; set; }
    internal FamilySymbol? FrameType { get; set; }
    internal string Mark { get; set; } = "";
    internal double FrameX { get; set; }
    internal double FrameY { get; set; }

    internal SlabOptions(RebarBarType[] bars, double[] depths, double step, double cover, double? leg, double rbt, double rs)
    {
        Bars = bars;
        Depths = depths;
        Step = step;
        EndCover = cover;
        ManualLeg = leg;
        Rbt = rbt;
        Rs = rs;
    }

    internal void Validate(SlabGeometry g)
    {
        if (Depths.Length != 4 || Bars.Length != 4 || Step < 50 || EndCover <= 0 || ManualLeg <= 0)
            throw new InvalidOperationException("Проверьте шаг, защитный слой и длину ветви.");
        for (int i = 0; i < 4; i++)
            if (Depths[i] * SlabGeometry.Mm < Bars[i].BarModelDiameter / 2)
                throw new InvalidOperationException("Ось стержня должна находиться внутри плиты с учётом радиуса.");
        foreach (int start in new[] { 0, 2 })
            if (Math.Abs(Depths[start] - Depths[start + 1]) * SlabGeometry.Mm + SlabGeometry.Tolerance < (Bars[start].BarModelDiameter + Bars[start + 1].BarModelDiameter) / 2)
                throw new InvalidOperationException("Стержни пересекающихся направлений накладываются. Уточните расстояния до осей.");
        double occupied = Enumerable.Range(0, 2).Max(i => Depths[i] * SlabGeometry.Mm + Bars[i].BarModelDiameter / 2) + Enumerable.Range(2, 2).Max(i => Depths[i] * SlabGeometry.Mm + Bars[i].BarModelDiameter / 2);
        if (occupied >= g.Thickness)
            throw new InvalidOperationException("Верхняя и нижняя сетки не помещаются в плиту.");
        if (PlaceFrames && (FrameType == null || FrameX <= 0 || FrameY <= 0 || string.IsNullOrWhiteSpace(Mark)))
            throw new InvalidOperationException("Для каркасов укажите тип, марку и оба шага.");
    }

    internal RebarBarType EndType(int direction) => Bars[direction].BarNominalDiameter >= Bars[direction + 2].BarNominalDiameter ? Bars[direction] : Bars[direction + 2];

    internal double Leg(RebarBarType type)
    {
        if (ManualLeg is double mm)
            return mm;
        return SlabLayoutRules.LegLength(type.BarNominalDiameter / SlabGeometry.Mm, Rs, Rbt);
    }

    internal static bool IsBackground(Element? type)
    {
        string value = type?.LookupParameter("• Фильтр")?.AsString() ?? "";
        return value.IndexOf("Перекрытия", StringComparison.OrdinalIgnoreCase) >= 0 && value.IndexOf("Фоновая", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static RebarShape Shape(Document d, string number) => new FilteredElementCollector(d).OfClass(typeof(RebarShape)).Cast<RebarShape>().FirstOrDefault(s => s.Name == "(Форма) " + number || s.Name == number) ?? throw new InvalidOperationException("Загрузите в проект форму " + number + ".");
}
#endif
