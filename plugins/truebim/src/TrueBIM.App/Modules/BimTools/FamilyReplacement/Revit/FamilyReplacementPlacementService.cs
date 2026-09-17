using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using TrueBIM.App.Modules.BimTools.FamilyReplacement.Models;

namespace TrueBIM.App.Modules.BimTools.FamilyReplacement.Revit;

internal sealed class FamilyReplacementPlacementService
{
    internal const double PositionTolerance = 1.0 / 3048.0; // 0.1 mm, in internal feet.
    private const double DirectionTolerance = 1e-7;

    public FamilyInstance Create(Document document, FamilyInstance source, FamilySymbol symbol)
    {
        if (source.Location is not LocationPoint location)
            throw new InvalidOperationException("Поддерживаются семейства с точкой размещения.");
        if (source.Symbol.Family.FamilyPlacementType != symbol.Family.FamilyPlacementType)
            throw new InvalidOperationException("Способы размещения семейств различаются. Выберите тип с такой же основой размещения.");

        Level? level = ResolveLevel(document, source);
        XYZ point = location.Point;
        switch (symbol.Family.FamilyPlacementType)
        {
            case FamilyPlacementType.OneLevelBased:
                if (level is null)
                    throw new InvalidOperationException("Не удалось определить уровень исходного экземпляра.");
                return document.Create.NewFamilyInstance(point, symbol, level, StructuralType.NonStructural);
            case FamilyPlacementType.OneLevelBasedHosted:
                if (source.Host is null || level is null)
                    throw new InvalidOperationException("Не удалось определить основу или уровень исходного экземпляра.");
                return document.Create.NewFamilyInstance(point, symbol, source.Host, level, StructuralType.NonStructural);
            case FamilyPlacementType.WorkPlaneBased:
                if (source.HostFace is not null)
                    return document.Create.NewFamilyInstance(source.HostFace, point, source.GetTransform().BasisX, symbol);
                throw new InvalidOperationException("Для семейства на рабочей плоскости требуется доступная ссылка на исходную грань.");
            default:
                throw new InvalidOperationException("Этот способ размещения пока не поддерживается. Исходный элемент сохранён.");
        }
    }

    public FamilyReplacementPlacementSnapshot Capture(Document document, FamilyInstance source, FamilyReplacementAlignment alignment)
    {
        return new(source.GetTransform(), Anchor(source, alignment), alignment,
            ResolveLevel(document, source)?.Id ?? ElementId.InvalidElementId,
            source.Host?.Id ?? ElementId.InvalidElementId, source.HandFlipped, source.FacingFlipped);
    }

    public void Align(Document document, FamilyReplacementPlacementSnapshot snapshot, FamilyInstance target)
    {
        // Flip states can affect family geometry independently of its placement frame.
        if (target.CanFlipHand && target.HandFlipped != snapshot.HandFlipped)
            target.flipHand();
        if (target.CanFlipFacing && target.FacingFlipped != snapshot.FacingFlipped)
            target.flipFacing();
        document.Regenerate();
        Transform expected = snapshot.Transform;
        Transform actual = target.GetTransform();
        if (actual.HasReflection != expected.HasReflection)
        {
            Plane plane = Plane.CreateByNormalAndOrigin(actual.BasisX, actual.Origin);
            ElementTransformUtils.MirrorElements(document, new[] { target.Id }, plane, false);
            document.Regenerate();
        }

        // Align the full 3D frame, including reflection, rather than only the plan angle.
        actual = target.GetTransform();
        RotateDirection(document, target, actual.BasisZ, expected.BasisZ, actual.BasisX);
        actual = target.GetTransform();
        XYZ normal = expected.BasisZ.Normalize();
        double angle = Math.Atan2(normal.DotProduct(actual.BasisX.CrossProduct(expected.BasisX)),
            actual.BasisX.DotProduct(expected.BasisX));
        Rotate(document, target, normal, angle);

        XYZ delta = snapshot.Anchor - Anchor(target, snapshot.Alignment);
        if (delta.GetLength() > 1e-9)
        {
            ElementTransformUtils.MoveElement(document, target.Id, delta);
            document.Regenerate();
        }
        Validate(document, snapshot, target);
    }

    public void Validate(Document document, FamilyReplacementPlacementSnapshot snapshot, FamilyInstance target)
    {
        if (!target.IsValidObject)
            throw new InvalidOperationException("Новый экземпляр удалён при пересчёте Revit.");
        Transform expected = snapshot.Transform;
        Transform actual = target.GetTransform();
        if ((actual.BasisX - expected.BasisX).GetLength() > DirectionTolerance ||
            (actual.BasisY - expected.BasisY).GetLength() > DirectionTolerance ||
            (actual.BasisZ - expected.BasisZ).GetLength() > DirectionTolerance)
            throw new InvalidOperationException("Не удалось сохранить пространственную ориентацию семейства.");
        if (snapshot.Anchor.DistanceTo(Anchor(target, snapshot.Alignment)) > PositionTolerance)
            throw new InvalidOperationException("Новое семейство смещено относительно выбранной точки совмещения.");

        if ((ResolveLevel(document, target)?.Id ?? ElementId.InvalidElementId) != snapshot.LevelId)
            throw new InvalidOperationException("Не удалось сохранить привязку к исходному уровню.");
        if ((target.Host?.Id ?? ElementId.InvalidElementId) != snapshot.HostId)
            throw new InvalidOperationException("Основа размещения нового семейства отличается от исходной.");
    }

    internal static Level? ResolveLevel(Document document, FamilyInstance instance)
    {
        if (document.GetElement(instance.LevelId) is Level level)
            return level;
        foreach (BuiltInParameter id in new[] { BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM })
        {
            Parameter? parameter = instance.get_Parameter(id);
            if (parameter?.StorageType == StorageType.ElementId && document.GetElement(parameter.AsElementId()) is Level parameterLevel)
                return parameterLevel;
        }
        return instance.Host as Level;
    }

    internal static XYZ Anchor(FamilyInstance instance, FamilyReplacementAlignment alignment)
    {
        if (alignment == FamilyReplacementAlignment.InsertionPoint)
            return ((LocationPoint)instance.Location).Point;
        GeometryElement? geometry = instance.get_Geometry(new Options
        {
            DetailLevel = ViewDetailLevel.Fine,
            IncludeNonVisibleObjects = false
        });
        List<XYZ> points = new();
        if (geometry is not null)
            CollectGeometryPoints(geometry, points);
        if (points.Count == 0)
            throw new InvalidOperationException("Не найдено объёмной геометрии для определения центра. Выберите совмещение по точке вставки.");
        return new XYZ((points.Min(p => p.X) + points.Max(p => p.X)) / 2,
            (points.Min(p => p.Y) + points.Max(p => p.Y)) / 2,
            (points.Min(p => p.Z) + points.Max(p => p.Z)) / 2);
    }

    private static void CollectGeometryPoints(GeometryElement geometry, List<XYZ> points)
    {
        foreach (GeometryObject item in geometry)
        {
            if (item is GeometryInstance nested)
                CollectGeometryPoints(nested.GetInstanceGeometry(), points);
            else if (item is Solid solid && solid.Faces.Size > 0)
                foreach (Face face in solid.Faces)
                    points.AddRange(face.Triangulate().Vertices);
            else if (item is Mesh mesh)
                points.AddRange(mesh.Vertices);
        }
    }

    private static void RotateDirection(Document document, FamilyInstance instance, XYZ from, XYZ to, XYZ oppositeAxis)
    {
        XYZ axis = from.CrossProduct(to);
        double angle = from.AngleTo(to);
        if (angle < DirectionTolerance)
            return;
        Rotate(document, instance, axis.GetLength() > DirectionTolerance ? axis.Normalize() : oppositeAxis, angle);
    }

    private static void Rotate(Document document, FamilyInstance instance, XYZ direction, double angle)
    {
        if (Math.Abs(angle) < DirectionTolerance)
            return;
        XYZ origin = ((LocationPoint)instance.Location).Point;
        ElementTransformUtils.RotateElement(document, instance.Id, Line.CreateUnbound(origin, direction), angle);
        document.Regenerate();
    }
}

internal sealed record FamilyReplacementPlacementSnapshot(
    Transform Transform, XYZ Anchor, FamilyReplacementAlignment Alignment,
    ElementId LevelId, ElementId HostId, bool HandFlipped, bool FacingFlipped);
