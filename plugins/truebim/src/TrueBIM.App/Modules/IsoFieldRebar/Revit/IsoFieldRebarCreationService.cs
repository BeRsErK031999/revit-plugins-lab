using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.IsoFieldRebar.Revit;

public sealed class IsoFieldRebarCreationService
{
    private const string WallHostKind = "Wall";
    private const string SlabHostKind = "Slab";
    private const string OwnedCommentPrefix = "TrueBIM IsoFieldRebar Test";
    private const double MinimumTestLengthFeet = 0.5;
    private const double MaximumTestLengthFeet = 4.0;
    private readonly SlabRebarPlacementService slabPlacementService = new();
    private readonly ITrueBimLogger logger;

    public IsoFieldRebarCreationService(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IsoFieldRebarCreationResult CreateTestRebar(
        UIDocument uiDocument,
        IsoFieldHostElement hostElement,
        RebarRulePreviewResult rulePreview)
    {
        if (uiDocument is null)
        {
            throw new ArgumentNullException(nameof(uiDocument));
        }

        if (hostElement is null)
        {
            throw new ArgumentNullException(nameof(hostElement));
        }

        if (rulePreview is null)
        {
            throw new ArgumentNullException(nameof(rulePreview));
        }

        IReadOnlyList<RebarRulePreviewItem> previewItems = ResolvePreviewItems(rulePreview, hostElement.HostKind);
        Document document = uiDocument.Document;
        Element host = document.GetElement(RevitElementIds.Create(hostElement.ElementId))
            ?? throw new InvalidOperationException("Выбранный host-элемент не найден в текущем документе Revit.");
        EnsureHostMatchesSelection(host, hostElement);

        List<long> createdIds = new();

        using Transaction transaction = new(document, "TrueBIM: тестовая арматура по изополям");
        transaction.Start();

        try
        {
            foreach (RebarCreationRequest request in BuildCreationRequests(document, host, hostElement, previewItems))
            {
                Rebar rebar = CreateRebar(document, host, request);
                MarkCreatedRebar(rebar, request.PreviewItem);
                createdIds.Add(RevitElementIds.GetValue(rebar.Id));
            }

            transaction.Commit();
        }
        catch
        {
            transaction.RollBack();
            throw;
        }

        logger.Info($"IsoField test rebar created. Count={createdIds.Count}; HostId={hostElement.ElementId}; HostKind={hostElement.HostKind}.");
        return new IsoFieldRebarCreationResult(
            createdIds.Count,
            createdIds,
            $"Создана тестовая арматура: {createdIds.Count}. Host: {hostElement.DisplayName}.");
    }

    private static IReadOnlyList<RebarRulePreviewItem> ResolvePreviewItems(
        RebarRulePreviewResult rulePreview,
        string hostKind)
    {
        if (!rulePreview.CanCreateRebar)
        {
            throw new InvalidOperationException("Перед созданием тестовой арматуры рассчитайте валидные правила армирования.");
        }

        RebarRulePreviewItem[] validItems = rulePreview.Items
            .Where(item => item.IsValid && string.Equals(item.Rule.HostKind, hostKind, StringComparison.Ordinal))
            .ToArray();
        if (validItems.Length == 0)
        {
            throw new InvalidOperationException("Нет валидной зоны для создания тестовой арматуры.");
        }

        return string.Equals(hostKind, SlabHostKind, StringComparison.Ordinal)
            ? validItems
            : [validItems[0]];
    }

    private static void EnsureHostMatchesSelection(Element host, IsoFieldHostElement selectedHost)
    {
        Category category = host.Category
            ?? throw new InvalidOperationException("У выбранного host-элемента нет категории.");

        long categoryId = RevitElementIds.GetValue(category.Id);
        string actualHostKind = categoryId switch
        {
            (long)BuiltInCategory.OST_Walls => WallHostKind,
            (long)BuiltInCategory.OST_Floors => SlabHostKind,
            _ => string.Empty
        };

        if (!string.Equals(actualHostKind, selectedHost.HostKind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Выбранный host-элемент больше не соответствует сохраненному типу стены или плиты.");
        }
    }

    private static RebarBarType ResolveBarType(Document document, string preferredName)
    {
        List<RebarBarType> barTypes = new FilteredElementCollector(document)
            .OfClass(typeof(RebarBarType))
            .Cast<RebarBarType>()
            .OrderBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (barTypes.Count == 0)
        {
            throw new InvalidOperationException("В документе Revit не найден ни один тип арматуры RebarBarType.");
        }

        string normalizedPreferredName = NormalizeBarTypeName(preferredName);
        RebarBarType? exactMatch = barTypes.FirstOrDefault(type =>
            string.Equals(NormalizeBarTypeName(type.Name), normalizedPreferredName, StringComparison.Ordinal)
            || string.Equals(NormalizeBarTypeName(type.FamilyName), normalizedPreferredName, StringComparison.Ordinal));
        if (exactMatch is not null)
        {
            return exactMatch;
        }

        string diameterToken = new(normalizedPreferredName.Where(char.IsDigit).ToArray());
        if (!string.IsNullOrWhiteSpace(diameterToken))
        {
            RebarBarType? diameterMatch = barTypes.FirstOrDefault(type =>
                ContainsOrdinal(NormalizeBarTypeName(type.Name), diameterToken)
                || ContainsOrdinal(NormalizeBarTypeName(type.FamilyName), diameterToken));
            if (diameterMatch is not null)
            {
                return diameterMatch;
            }
        }

        return barTypes[0];
    }

    private IEnumerable<RebarCreationRequest> BuildCreationRequests(
        Document document,
        Element host,
        IsoFieldHostElement hostElement,
        IReadOnlyList<RebarRulePreviewItem> previewItems)
    {
        if (string.Equals(hostElement.HostKind, WallHostKind, StringComparison.Ordinal) && host is Wall wall)
        {
            RebarRulePreviewItem item = previewItems[0];
            yield return new RebarCreationRequest(
                item,
                ResolveBarType(document, item.Rule.BarTypeName),
                BuildWallTestGeometry(wall));
            yield break;
        }

        if (string.Equals(hostElement.HostKind, SlabHostKind, StringComparison.Ordinal))
        {
            foreach (IsoFieldRebarPlacement placement in slabPlacementService.BuildPlacements(
                BuildPlacementBounds(host),
                previewItems))
            {
                yield return new RebarCreationRequest(
                    previewItems.First(item => string.Equals(item.ZoneId, placement.ZoneId, StringComparison.Ordinal)),
                    ResolveBarType(document, placement.Rule.BarTypeName),
                    new TestRebarGeometry(
                        [Line.CreateBound(ToXyz(placement.Start), ToXyz(placement.End))],
                        ToXyz(placement.Normal)));
            }

            yield break;
        }

        throw new InvalidOperationException("MVP создания тестовой арматуры поддерживает только простые стены и плиты.");
    }

    private static TestRebarGeometry BuildWallTestGeometry(Wall wall)
    {
        if (wall.Location is not LocationCurve locationCurve)
        {
            throw new InvalidOperationException("Для тестовой арматуры стены нужна LocationCurve.");
        }

        Curve location = locationCurve.Curve;
        XYZ start = location.GetEndPoint(0);
        XYZ end = location.GetEndPoint(1);
        XYZ direction = end - start;
        if (direction.GetLength() < MinimumTestLengthFeet)
        {
            throw new InvalidOperationException("LocationCurve стены слишком короткая для тестовой арматуры.");
        }

        direction = NormalizeHorizontalDirection(direction);
        BoundingBoxXYZ boundingBox = wall.get_BoundingBox(null)
            ?? throw new InvalidOperationException("У выбранной стены нет bounding box.");

        XYZ centerOnCurve = location.Evaluate(0.5, true);
        XYZ center = new(centerOnCurve.X, centerOnCurve.Y, (boundingBox.Min.Z + boundingBox.Max.Z) / 2);
        double halfLength = ResolveHalfLengthFeet(start.DistanceTo(end));
        XYZ normal = ResolveWallNormal(wall, direction);

        return new TestRebarGeometry(
            [Line.CreateBound(center - (direction * halfLength), center + (direction * halfLength))],
            normal);
    }

    private static IsoFieldRebarPlacementBounds BuildPlacementBounds(Element slab)
    {
        BoundingBoxXYZ boundingBox = slab.get_BoundingBox(null)
            ?? throw new InvalidOperationException("У выбранной плиты нет bounding box.");

        return new IsoFieldRebarPlacementBounds(
            boundingBox.Min.X,
            boundingBox.Min.Y,
            boundingBox.Min.Z,
            boundingBox.Max.X,
            boundingBox.Max.Y,
            boundingBox.Max.Z);
    }

    private static XYZ NormalizeHorizontalDirection(XYZ direction)
    {
        XYZ horizontal = new(direction.X, direction.Y, 0);
        return horizontal.GetLength() >= MinimumTestLengthFeet
            ? horizontal.Normalize()
            : direction.Normalize();
    }

    private static XYZ ResolveWallNormal(Wall wall, XYZ direction)
    {
        XYZ normal = wall.Orientation;
        if (normal.GetLength() < 1e-9)
        {
            normal = direction.CrossProduct(XYZ.BasisZ);
        }

        return normal.Normalize();
    }

    private static double ResolveHalfLengthFeet(double availableLengthFeet)
    {
        double targetLength = Math.Min(MaximumTestLengthFeet, availableLengthFeet * 0.5);
        targetLength = Math.Max(MinimumTestLengthFeet, targetLength);
        return targetLength / 2;
    }

    private static void MarkCreatedRebar(Rebar rebar, RebarRulePreviewItem previewItem)
    {
        Parameter? parameter = rebar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (parameter is not null && !parameter.IsReadOnly)
        {
            parameter.Set($"{OwnedCommentPrefix}: {previewItem.ZoneName}; {previewItem.Rule.BarTypeName}; spacing {previewItem.Rule.SpacingMillimeters.ToString("0", CultureInfo.InvariantCulture)} mm");
        }
    }

    private static string NormalizeBarTypeName(string value)
    {
        return new string((value ?? string.Empty)
            .Where(character => !char.IsWhiteSpace(character))
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static bool ContainsOrdinal(string value, string search)
    {
        return value.IndexOf(search, StringComparison.Ordinal) >= 0;
    }

    private static Rebar CreateRebar(
        Document document,
        Element host,
        RebarCreationRequest request)
    {
        Rebar rebar = Rebar.CreateFromCurves(
            document,
            RebarStyle.Standard,
            request.BarType,
            null,
            null,
            host,
            request.Geometry.Normal,
            request.Geometry.Curves,
            RebarHookOrientation.Left,
            RebarHookOrientation.Right,
            true,
            true);

        rebar.GetShapeDrivenAccessor().SetLayoutAsSingle();
        return rebar;
    }

    private static XYZ ToXyz(IsoFieldRebarPoint3D point)
    {
        return new XYZ(point.XFeet, point.YFeet, point.ZFeet);
    }

    private sealed record RebarCreationRequest(
        RebarRulePreviewItem PreviewItem,
        RebarBarType BarType,
        TestRebarGeometry Geometry);

    private sealed record TestRebarGeometry(IList<Curve> Curves, XYZ Normal);
}
