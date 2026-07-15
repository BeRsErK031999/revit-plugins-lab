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
    private const double MinimumDirectionLengthFeet = 1e-9;
    private readonly SlabRebarPlacementService slabPlacementService = new();
    private readonly WallRebarPlacementService wallPlacementService = new();
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
        logger.Info($"IsoField test rebar transaction starting. HostId={hostElement.ElementId}; HostKind={hostElement.HostKind}; ValidRules={previewItems.Count}.");

        using Transaction transaction = new(document, "TrueBIM: пробное армирование по изополям");
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
        catch (Exception exception)
        {
            transaction.RollBack();
            logger.Error($"IsoField test rebar transaction rolled back. HostId={hostElement.ElementId}; HostKind={hostElement.HostKind}.", exception);
            throw;
        }

        logger.Info($"IsoField test rebar created. Count={createdIds.Count}; HostId={hostElement.ElementId}; HostKind={hostElement.HostKind}.");
        return new IsoFieldRebarCreationResult(
            createdIds.Count,
            createdIds,
            $"Создано пробное армирование: {createdIds.Count}. Host: {hostElement.DisplayName}.");
    }

    private static IReadOnlyList<RebarRulePreviewItem> ResolvePreviewItems(
        RebarRulePreviewResult rulePreview,
        string hostKind)
    {
        if (!rulePreview.CanCreateRebar)
        {
            throw new InvalidOperationException("Перед созданием пробного армирования рассчитайте валидные правила армирования.");
        }

        RebarRulePreviewItem[] validItems = rulePreview.Items
            .Where(item => item.IsValid && string.Equals(item.Rule.HostKind, hostKind, StringComparison.Ordinal))
            .ToArray();
        if (validItems.Length == 0)
        {
            throw new InvalidOperationException("Нет валидной зоны для создания пробного армирования.");
        }

        return validItems;
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
            foreach (IsoFieldRebarPlacement placement in wallPlacementService.BuildPlacements(
                BuildWallPlacementFrame(wall),
                previewItems))
            {
                logger.Info($"IsoField wall rebar placement prepared. ZoneId={placement.ZoneId}; Direction={placement.Rule.PlacementDirection}; LengthFeet={placement.LengthFeet:0.###}.");
                yield return new RebarCreationRequest(
                    previewItems.First(item => string.Equals(item.ZoneId, placement.ZoneId, StringComparison.Ordinal)),
                    ResolveBarType(document, placement.Rule.BarTypeName),
                    new TestRebarGeometry(
                        [Line.CreateBound(ToXyz(placement.Start), ToXyz(placement.End))],
                        ToXyz(placement.Normal)));
            }

            yield break;
        }

        if (string.Equals(hostElement.HostKind, SlabHostKind, StringComparison.Ordinal))
        {
            foreach (IsoFieldRebarPlacement placement in slabPlacementService.BuildPlacements(
                BuildPlacementBounds(host),
                previewItems))
            {
                logger.Info($"IsoField slab rebar placement prepared. ZoneId={placement.ZoneId}; Direction={placement.Rule.PlacementDirection}; LengthFeet={placement.LengthFeet:0.###}.");
                yield return new RebarCreationRequest(
                    previewItems.First(item => string.Equals(item.ZoneId, placement.ZoneId, StringComparison.Ordinal)),
                    ResolveBarType(document, placement.Rule.BarTypeName),
                    new TestRebarGeometry(
                        [Line.CreateBound(ToXyz(placement.Start), ToXyz(placement.End))],
                        ToXyz(placement.Normal)));
            }

            yield break;
        }

        throw new InvalidOperationException("MVP пробного армирования поддерживает только простые стены и плиты.");
    }

    private static IsoFieldWallPlacementFrame BuildWallPlacementFrame(Wall wall)
    {
        if (wall.Location is not LocationCurve locationCurve)
        {
            throw new InvalidOperationException("Для пробного армирования стены нужна LocationCurve.");
        }

        if (locationCurve.Curve is not Line location)
        {
            throw new InvalidOperationException("MVP пробного армирования поддерживает только прямые стены.");
        }

        XYZ start = location.GetEndPoint(0);
        XYZ end = location.GetEndPoint(1);
        XYZ direction = end - start;
        double lengthFeet = new XYZ(direction.X, direction.Y, 0).GetLength();
        if (lengthFeet < MinimumTestLengthFeet)
        {
            throw new InvalidOperationException("LocationCurve стены слишком короткая для пробного армирования.");
        }

        XYZ axis = NormalizeHorizontalDirection(direction);
        BoundingBoxXYZ boundingBox = wall.get_BoundingBox(null)
            ?? throw new InvalidOperationException("У выбранной стены нет bounding box.");
        double heightFeet = boundingBox.Max.Z - boundingBox.Min.Z;
        if (heightFeet < MinimumTestLengthFeet)
        {
            throw new InvalidOperationException("Bounding box стены слишком мал для пробного армирования.");
        }

        XYZ centerOnCurve = location.Evaluate(0.5, true);
        XYZ center = new(centerOnCurve.X, centerOnCurve.Y, (boundingBox.Min.Z + boundingBox.Max.Z) / 2);
        XYZ normal = ResolveWallNormal(wall, axis);

        return new IsoFieldWallPlacementFrame(
            ToPoint3D(center),
            ToPoint3D(axis),
            ToPoint3D(normal),
            lengthFeet,
            heightFeet);
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
        if (horizontal.GetLength() < MinimumDirectionLengthFeet)
        {
            throw new InvalidOperationException("LocationCurve стены должна иметь ненулевое горизонтальное направление.");
        }

        return horizontal.Normalize();
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
#if REVIT2026_OR_GREATER
        using BarTerminationsData barTerminations = new(document)
        {
            TerminationOrientationAtStart = RebarTerminationOrientation.Left,
            TerminationOrientationAtEnd = RebarTerminationOrientation.Right
        };
        Rebar rebar = Rebar.CreateFromCurves(
            document,
            RebarStyle.Standard,
            request.BarType,
            host,
            request.Geometry.Normal,
            request.Geometry.Curves,
            barTerminations,
            true,
            true);
#else
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
#endif

        rebar.GetShapeDrivenAccessor().SetLayoutAsSingle();
        return rebar;
    }

    private static XYZ ToXyz(IsoFieldRebarPoint3D point)
    {
        return new XYZ(point.XFeet, point.YFeet, point.ZFeet);
    }

    private static IsoFieldRebarPoint3D ToPoint3D(XYZ point)
    {
        return new IsoFieldRebarPoint3D(point.X, point.Y, point.Z);
    }

    private sealed record RebarCreationRequest(
        RebarRulePreviewItem PreviewItem,
        RebarBarType BarType,
        TestRebarGeometry Geometry);

    private sealed record TestRebarGeometry(IList<Curve> Curves, XYZ Normal);
}
