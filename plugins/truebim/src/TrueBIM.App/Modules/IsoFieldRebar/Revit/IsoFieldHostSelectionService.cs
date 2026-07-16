using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.IsoFieldRebar.Revit;

public sealed class IsoFieldHostSelectionService
{
    private const string WallHostKind = "Wall";
    private const string SlabHostKind = "Slab";
    private const double HorizontalNormalTolerance = 0.995;
    private const double PointPlaneToleranceFeet = 0.02;
    private const double GeometryToleranceFeet = 1e-7;

    public IsoFieldHostElement PickHost(UIDocument uiDocument)
    {
        if (uiDocument is null)
        {
            throw new ArgumentNullException(nameof(uiDocument));
        }

        Document document = uiDocument.Document;
        Element? preselectedHost = ResolveSinglePreselectedHost(uiDocument, document);
        if (preselectedHost is not null)
        {
            return CreateHostElement(preselectedHost);
        }

        Reference reference = uiDocument.Selection.PickObject(
            ObjectType.Element,
            new HostSelectionFilter(),
            "Выберите стену или плиту для армирования по изополям.");
        Element selectedElement = document.GetElement(reference.ElementId)
            ?? throw new InvalidOperationException("Не удалось получить выбранный host-элемент.");
        return CreateHostElement(selectedElement);
    }

    public IsoFieldPoint PickSlabControlPoint(
        UIDocument uiDocument,
        IsoFieldHostElement hostElement,
        int pointNumber)
    {
        if (uiDocument is null)
        {
            throw new ArgumentNullException(nameof(uiDocument));
        }

        if (hostElement is null)
        {
            throw new ArgumentNullException(nameof(hostElement));
        }

        if (!hostElement.IsSlab || hostElement.Geometry is null)
        {
            throw new InvalidOperationException(
                "Контрольные точки доступны только для плиты с распознанной горизонтальной верхней гранью.");
        }

        Reference reference = uiDocument.Selection.PickObject(
            ObjectType.Face,
            new HostFaceSelectionFilter(hostElement.ElementId),
            $"Укажите контрольную точку {pointNumber} на верхней грани выбранной плиты.");
        XYZ worldPoint = reference.GlobalPoint
            ?? throw new InvalidOperationException("Не удалось определить координаты выбранной точки плиты.");
        IsoFieldHostGeometry geometry = hostElement.Geometry;
        XYZ origin = ToXyz(geometry.OriginFeet);
        XYZ axisX = ToXyz(geometry.AxisX);
        XYZ axisY = ToXyz(geometry.AxisY);
        XYZ normal = ToXyz(geometry.Normal);
        XYZ delta = worldPoint - origin;
        double planeDistance = Math.Abs(delta.DotProduct(normal));
        if (planeDistance > PointPlaneToleranceFeet)
        {
            throw new InvalidOperationException(
                "Выбранная точка не лежит на верхней плоскости плиты. Укажите точку на верхней грани.");
        }

        return new IsoFieldPoint(
            delta.DotProduct(axisX),
            delta.DotProduct(axisY));
    }

    public static bool IsSupportedHostCategory(long categoryId)
    {
        return categoryId == (long)BuiltInCategory.OST_Walls
            || categoryId == (long)BuiltInCategory.OST_Floors;
    }

    private static Element? ResolveSinglePreselectedHost(UIDocument uiDocument, Document document)
    {
        ICollection<ElementId> selectedIds = uiDocument.Selection.GetElementIds();
        if (selectedIds.Count != 1)
        {
            return null;
        }

        Element? selectedElement = document.GetElement(selectedIds.Single());
        return selectedElement is not null && IsSupportedHost(selectedElement)
            ? selectedElement
            : null;
    }

    private static IsoFieldHostElement CreateHostElement(Element element)
    {
        Category category = element.Category
            ?? throw new InvalidOperationException("У выбранного элемента нет категории.");
        long categoryId = RevitElementIds.GetValue(category.Id);
        if (!TryResolveHostKind(categoryId, out string hostKind, out string hostKindDisplayName))
        {
            throw new InvalidOperationException("Выбранный элемент не является стеной или плитой.");
        }

        long elementId = RevitElementIds.GetValue(element.Id);
        IsoFieldHostGeometry? geometry = string.Equals(hostKind, SlabHostKind, StringComparison.Ordinal)
            ? TryCreateSlabGeometry(element)
            : null;
        return new IsoFieldHostElement(
            elementId,
            hostKind,
            hostKindDisplayName,
            ResolveElementName(element, elementId),
            geometry);
    }

    private static IsoFieldHostGeometry? TryCreateSlabGeometry(Element element)
    {
        if (element is not HostObject hostObject)
        {
            return null;
        }

        PlanarFace? topFace = HostObjectUtils.GetTopFaces(hostObject)
            .Select(reference => element.GetGeometryObjectFromReference(reference))
            .OfType<PlanarFace>()
            .Where(face => Math.Abs(face.FaceNormal.Normalize().DotProduct(XYZ.BasisZ)) >= HorizontalNormalTolerance)
            .OrderByDescending(face => face.Area)
            .FirstOrDefault();
        if (topFace is null)
        {
            return null;
        }

        XYZ normal = topFace.FaceNormal.Normalize();
        if (normal.DotProduct(XYZ.BasisZ) < 0)
        {
            normal = -normal;
        }

        List<IReadOnlyList<XYZ>> worldLoops = topFace.GetEdgesAsCurveLoops()
            .Select(BuildWorldLoop)
            .Where(loop => loop.Count >= 4)
            .ToList();
        if (worldLoops.Count == 0)
        {
            return null;
        }

        XYZ? axisX = FindLongestHorizontalDirection(worldLoops);
        if (axisX is null)
        {
            return null;
        }

        XYZ normalizedAxisX = axisX.Normalize();
        if (normalizedAxisX.X < -GeometryToleranceFeet
            || Math.Abs(normalizedAxisX.X) <= GeometryToleranceFeet
                && normalizedAxisX.Y < 0)
        {
            normalizedAxisX = -normalizedAxisX;
        }

        XYZ axisY = normal.CrossProduct(normalizedAxisX).Normalize();
        XYZ average = Average(worldLoops.SelectMany(loop => loop));
        XYZ origin = average - (normal * (average - topFace.Origin).DotProduct(normal));
        IReadOnlyList<IReadOnlyList<IsoFieldPoint>> localLoops = worldLoops
            .Select(loop => (IReadOnlyList<IsoFieldPoint>)loop
                .Select(point =>
                {
                    XYZ delta = point - origin;
                    return new IsoFieldPoint(
                        delta.DotProduct(normalizedAxisX),
                        delta.DotProduct(axisY));
                })
                .ToArray())
            .ToArray();

        return new IsoFieldHostGeometry(
            ToPoint3D(origin),
            ToPoint3D(normalizedAxisX),
            ToPoint3D(axisY),
            ToPoint3D(normal),
            localLoops);
    }

    private static IReadOnlyList<XYZ> BuildWorldLoop(CurveLoop curveLoop)
    {
        List<XYZ> points = new();
        foreach (Curve curve in curveLoop)
        {
            foreach (XYZ point in curve.Tessellate())
            {
                if (points.Count == 0 || points[points.Count - 1].DistanceTo(point) > GeometryToleranceFeet)
                {
                    points.Add(point);
                }
            }
        }

        if (points.Count >= 3 && points[0].DistanceTo(points[points.Count - 1]) > GeometryToleranceFeet)
        {
            points.Add(points[0]);
        }

        return points;
    }

    private static XYZ? FindLongestHorizontalDirection(
        IReadOnlyList<IReadOnlyList<XYZ>> loops)
    {
        XYZ? bestDirection = null;
        double bestLength = 0;
        foreach (IReadOnlyList<XYZ> loop in loops)
        {
            for (int index = 0; index < loop.Count - 1; index++)
            {
                XYZ direction = loop[index + 1] - loop[index];
                XYZ horizontal = new(direction.X, direction.Y, 0);
                double length = horizontal.GetLength();
                if (length > bestLength)
                {
                    bestLength = length;
                    bestDirection = horizontal;
                }
            }
        }

        return bestLength > GeometryToleranceFeet ? bestDirection : null;
    }

    private static XYZ Average(IEnumerable<XYZ> points)
    {
        XYZ[] values = points.ToArray();
        if (values.Length == 0)
        {
            return XYZ.Zero;
        }

        return new XYZ(
            values.Average(point => point.X),
            values.Average(point => point.Y),
            values.Average(point => point.Z));
    }

    private static IsoFieldRebarPoint3D ToPoint3D(XYZ point)
    {
        return new IsoFieldRebarPoint3D(point.X, point.Y, point.Z);
    }

    private static XYZ ToXyz(IsoFieldRebarPoint3D point)
    {
        return new XYZ(point.XFeet, point.YFeet, point.ZFeet);
    }

    private static bool IsSupportedHost(Element element)
    {
        if (element is ElementType || element.Category is null)
        {
            return false;
        }

        return IsSupportedHostCategory(RevitElementIds.GetValue(element.Category.Id));
    }

    private static bool TryResolveHostKind(
        long categoryId,
        out string hostKind,
        out string hostKindDisplayName)
    {
        if (categoryId == (long)BuiltInCategory.OST_Walls)
        {
            hostKind = WallHostKind;
            hostKindDisplayName = "Стена";
            return true;
        }

        if (categoryId == (long)BuiltInCategory.OST_Floors)
        {
            hostKind = SlabHostKind;
            hostKindDisplayName = "Плита";
            return true;
        }

        hostKind = string.Empty;
        hostKindDisplayName = string.Empty;
        return false;
    }

    private static string ResolveElementName(Element element, long elementId)
    {
        string? instanceName = element.Name;
        if (!string.IsNullOrWhiteSpace(instanceName))
        {
            return instanceName!;
        }

        ElementId typeId = element.GetTypeId();
        if (typeId != ElementId.InvalidElementId)
        {
            string? typeName = element.Document.GetElement(typeId)?.Name;
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                return typeName!;
            }
        }

        return $"Element {elementId}";
    }

    private sealed class HostSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element element)
        {
            return IsSupportedHost(element);
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return true;
        }
    }

    private sealed class HostFaceSelectionFilter : ISelectionFilter
    {
        private readonly long hostElementId;

        public HostFaceSelectionFilter(long hostElementId)
        {
            this.hostElementId = hostElementId;
        }

        public bool AllowElement(Element element)
        {
            return RevitElementIds.GetValue(element.Id) == hostElementId;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return RevitElementIds.GetValue(reference.ElementId) == hostElementId;
        }
    }
}
