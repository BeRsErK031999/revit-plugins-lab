using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.IsoFieldRebar.Revit;

public sealed class IsoFieldRebarCreationService
{
    private const string WallHostKind = "Wall";
    private const string SlabHostKind = "Slab";
    private const double MillimetersPerFoot = 304.8;
    private const double MinimumTestLengthFeet = 0.5;
    private const double MinimumDirectionLengthFeet = 1e-9;
    private const double GeometryComparisonToleranceFeet = 1e-5;
    private readonly SlabRebarPlacementService slabPlacementService = new();
    private readonly WallRebarPlacementService wallPlacementService = new();
    private readonly IsoFieldRebarChangePlanService changePlanService = new();
    private readonly ITrueBimLogger logger;

    public IsoFieldRebarCreationService(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IsoFieldRebarCreationResult CreateTestRebar(
        UIDocument uiDocument,
        IsoFieldHostElement hostElement,
        RebarRulePreviewResult rulePreview,
        IsoFieldSlabBindingAnalysis? slabBinding = null)
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

        RebarCreationRequest[] requests = BuildCreationRequests(
                document,
                host,
                hostElement,
                rulePreview,
                previewItems,
                slabBinding)
            .ToArray();
        if (rulePreview.IsEngineeringPreview)
        {
            IsoFieldRebarChangePlan changePlan = BuildEngineeringChangePlan(document, host, requests);
            return ApplyEngineeringChangePlan(document, host, hostElement, requests, changePlan);
        }

        List<long> createdIds = new();
        logger.Info($"IsoField test rebar transaction starting. HostId={hostElement.ElementId}; HostKind={hostElement.HostKind}; ValidRules={previewItems.Count}.");

        string transactionName = rulePreview.IsEngineeringPreview
            ? "TrueBIM: армирование плиты по изополям"
            : "TrueBIM: пробное армирование по изополям";
        using Transaction transaction = new(document, transactionName);
        transaction.Start();

        try
        {
            foreach (RebarCreationRequest request in requests)
            {
                Rebar rebar = CreateRebar(document, host, request);
                MarkCreatedRebar(
                    rebar,
                    request.PreviewItem,
                    request.Placement,
                    request.Signature,
                    hostElement.ElementId);
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
        string resultKind = rulePreview.IsEngineeringPreview
            ? "армирование по отсечённым зонам"
            : "пробное армирование";
        return new IsoFieldRebarCreationResult(
            createdIds.Count,
            0,
            0,
            0,
            createdIds,
            Array.Empty<long>(),
            $"Создано {resultKind}: {createdIds.Count}. Host: {hostElement.DisplayName}.");
    }

    public IsoFieldRebarChangePlan PreviewEngineeringChanges(
        UIDocument uiDocument,
        IsoFieldHostElement hostElement,
        RebarRulePreviewResult rulePreview,
        IsoFieldSlabBindingAnalysis? slabBinding = null)
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

        if (!rulePreview.IsEngineeringPreview)
        {
            throw new InvalidOperationException("Diff повторного запуска доступен только для инженерной раскладки плиты.");
        }

        IReadOnlyList<RebarRulePreviewItem> previewItems = ResolvePreviewItems(rulePreview, hostElement.HostKind);
        Document document = uiDocument.Document;
        Element host = document.GetElement(RevitElementIds.Create(hostElement.ElementId))
            ?? throw new InvalidOperationException("Выбранный host-элемент не найден в текущем документе Revit.");
        EnsureHostMatchesSelection(host, hostElement);
        RebarCreationRequest[] requests = BuildCreationRequests(
                document,
                host,
                hostElement,
                rulePreview,
                previewItems,
                slabBinding)
            .ToArray();
        return BuildEngineeringChangePlan(document, host, requests);
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

    private static RebarBarType ResolveBarType(
        Document document,
        string preferredName,
        IsoFieldRebarComponent? component = null)
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

        if (component is not null)
        {
            RebarBarType? diameterMatch = barTypes.FirstOrDefault(type =>
            {
                Parameter? diameterParameter = type.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
                return diameterParameter?.StorageType == StorageType.Double
                    && Math.Abs(
                        (diameterParameter.AsDouble() * MillimetersPerFoot)
                        - component.DiameterMillimeters) <= 0.2;
            });
            if (diameterMatch is null)
            {
                throw new InvalidOperationException(
                    $"В документе Revit не найден тип арматуры диаметром {component.DiameterMillimeters:0.###} мм для {component.DisplayName}.");
            }

            return diameterMatch;
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
        RebarRulePreviewResult rulePreview,
        IReadOnlyList<RebarRulePreviewItem> previewItems,
        IsoFieldSlabBindingAnalysis? slabBinding)
    {
        if (string.Equals(hostElement.HostKind, WallHostKind, StringComparison.Ordinal) && host is Wall wall)
        {
            foreach (IsoFieldRebarPlacement placement in wallPlacementService.BuildPlacements(
                BuildWallPlacementFrame(wall),
                previewItems))
            {
                logger.Info($"IsoField wall rebar placement prepared. ZoneId={placement.ZoneId}; Direction={placement.Rule.PlacementDirection}; LengthFeet={placement.LengthFeet:0.###}.");
                yield return CreateRequest(
                    previewItems.First(item => string.Equals(item.ZoneId, placement.ZoneId, StringComparison.Ordinal)),
                    ResolveBarType(document, placement.Rule.BarTypeName, placement.Component),
                    new TestRebarGeometry(
                        [Line.CreateBound(ToXyz(placement.Start), ToXyz(placement.End))],
                        ToXyz(placement.Normal)),
                    placement);
            }

            yield break;
        }

        if (string.Equals(hostElement.HostKind, SlabHostKind, StringComparison.Ordinal))
        {
            IsoFieldRebarPlacementBounds bounds = BuildPlacementBounds(host);
            IReadOnlyList<IsoFieldRebarPlacement> placements;
            if (rulePreview.IsEngineeringPreview)
            {
                if (slabBinding?.CanProceed != true || hostElement.Geometry is null)
                {
                    throw new InvalidOperationException(
                        "Инженерная раскладка плиты требует актуальную проверенную привязку и геометрию верхней грани.");
                }

                placements = slabPlacementService.BuildEngineeringPlacements(
                    hostElement.Geometry,
                    bounds.WidthZFeet,
                    rulePreview);
            }
            else
            {
                placements = slabPlacementService.BuildPlacements(bounds, previewItems);
            }

            foreach (IsoFieldRebarPlacement placement in placements)
            {
                logger.Info($"IsoField slab rebar placement prepared. ZoneId={placement.ZoneId}; Direction={placement.Rule.PlacementDirection}; LengthFeet={placement.LengthFeet:0.###}.");
                yield return CreateRequest(
                    previewItems.First(item => string.Equals(item.ZoneId, placement.ZoneId, StringComparison.Ordinal)),
                    ResolveBarType(document, placement.Rule.BarTypeName, placement.Component),
                    new TestRebarGeometry(
                        [Line.CreateBound(ToXyz(placement.Start), ToXyz(placement.End))],
                        ToXyz(placement.Normal)),
                    placement);
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

    private static void MarkCreatedRebar(
        Rebar rebar,
        RebarRulePreviewItem previewItem,
        IsoFieldRebarPlacement placement,
        string? signature,
        long hostElementId)
    {
        Parameter? parameter = rebar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (placement.Component is not null
            && (parameter is null
                || parameter.IsReadOnly
                || string.IsNullOrWhiteSpace(placement.StableId)
                || string.IsNullOrWhiteSpace(signature)))
        {
            throw new InvalidOperationException(
                "Созданный Rebar нельзя безопасно пометить stable id и сигнатурой TrueBIM. Транзакция отменена.");
        }

        if (parameter is not null && !parameter.IsReadOnly)
        {
            string comment = placement.Component is null
                ? $"{IsoFieldRebarChangePlanService.OwnedCommentPrefix} Test: {previewItem.ZoneName}; {previewItem.Rule.BarTypeName}; spacing {previewItem.Rule.SpacingMillimeters.ToString("0", CultureInfo.InvariantCulture)} mm"
                : $"{IsoFieldRebarChangePlanService.OwnedCommentPrefix}; id={placement.StableId}; sig={signature}; host={hostElementId}; zone={previewItem.ZoneId}; "
                    + $"layer={previewItem.Rule.LayerRole}; face={previewItem.Rule.Face}; "
                    + $"{placement.Component.DisplayName}";
            bool marked = parameter.Set(comment);
            if (!marked && placement.Component is not null)
            {
                throw new InvalidOperationException(
                    "Rebar создан, но Revit отклонил запись stable id TrueBIM. Транзакция отменена.");
            }
        }
    }

    private RebarCreationRequest CreateRequest(
        RebarRulePreviewItem previewItem,
        RebarBarType barType,
        TestRebarGeometry geometry,
        IsoFieldRebarPlacement placement)
    {
        string? signature = placement.Component is null
            ? null
            : changePlanService.BuildSignature(placement);
        return new RebarCreationRequest(previewItem, barType, geometry, placement, signature);
    }

    private IsoFieldRebarChangePlan BuildEngineeringChangePlan(
        Document document,
        Element host,
        IReadOnlyList<RebarCreationRequest> requests)
    {
        IsoFieldRebarPlanItem[] plannedItems = requests
            .Select(request => new IsoFieldRebarPlanItem(
                request.Placement.StableId
                    ?? throw new InvalidOperationException("Инженерная линия не содержит стабильный id."),
                request.Signature
                    ?? throw new InvalidOperationException("Инженерная линия не содержит сигнатуру.")))
            .ToArray();
        Dictionary<string, RebarCreationRequest> requestsByStableId = requests
            .Where(request => !string.IsNullOrWhiteSpace(request.Placement.StableId))
            .GroupBy(request => request.Placement.StableId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
        long hostId = RevitElementIds.GetValue(host.Id);
        List<IsoFieldOwnedRebarSnapshot> existingElements = new();
        foreach (Rebar rebar in new FilteredElementCollector(document)
            .OfClass(typeof(Rebar))
            .Cast<Rebar>())
        {
            if (RevitElementIds.GetValue(rebar.GetHostId()) != hostId)
            {
                continue;
            }

            Parameter? comments = rebar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (changePlanService.TryParseOwnedComment(
                RevitElementIds.GetValue(rebar.Id),
                comments?.AsString(),
                out IsoFieldOwnedRebarSnapshot? snapshot))
            {
                IsoFieldOwnedRebarSnapshot activeSnapshot = snapshot!;
                activeSnapshot = activeSnapshot with
                {
                    StateSignature = BuildOwnedRebarStateSignature(document, rebar)
                };
                if (requestsByStableId.TryGetValue(activeSnapshot.StableId, out RebarCreationRequest? request)
                    && !RebarMatchesRequest(document, rebar, request))
                {
                    activeSnapshot = activeSnapshot with { Signature = null };
                }

                existingElements.Add(activeSnapshot);
            }
        }

        return changePlanService.Build(plannedItems, existingElements);
    }

    private static string BuildOwnedRebarStateSignature(Document document, Rebar rebar)
    {
        IList<Curve> centerlineCurves = rebar.GetCenterlineCurves(
            false,
            false,
            false,
            MultiplanarOption.IncludeOnlyPlanarCurves,
            0);
        RebarBarType? barType = document.GetElement(rebar.GetTypeId()) as RebarBarType;
        Parameter? diameterParameter = barType?.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
        string diameter = diameterParameter?.StorageType == StorageType.Double
            ? diameterParameter.AsDouble().ToString("0.#########", CultureInfo.InvariantCulture)
            : "unknown";
        return string.Join(
            "|",
            RevitElementIds.GetValue(rebar.GetTypeId()),
            diameter,
            string.Join(
                ";",
                centerlineCurves.Select(curve => string.Join(
                    ",",
                    curve.GetType().Name,
                    FormatStatePoint(curve.GetEndPoint(0)),
                    FormatStatePoint(curve.GetEndPoint(1))))));
    }

    private static string FormatStatePoint(XYZ point)
    {
        return string.Join(
            ":",
            point.X.ToString("0.#########", CultureInfo.InvariantCulture),
            point.Y.ToString("0.#########", CultureInfo.InvariantCulture),
            point.Z.ToString("0.#########", CultureInfo.InvariantCulture));
    }

    private static bool RebarMatchesRequest(
        Document document,
        Rebar rebar,
        RebarCreationRequest request)
    {
        IList<Curve> centerlineCurves = rebar.GetCenterlineCurves(
            false,
            false,
            false,
            MultiplanarOption.IncludeOnlyPlanarCurves,
            0);
        if (centerlineCurves.Count != 1 || centerlineCurves[0] is not Line actualLine)
        {
            return false;
        }

        Line plannedLine = (Line)request.Geometry.Curves[0];
        XYZ actualStart = actualLine.GetEndPoint(0);
        XYZ actualEnd = actualLine.GetEndPoint(1);
        XYZ plannedStart = plannedLine.GetEndPoint(0);
        XYZ plannedEnd = plannedLine.GetEndPoint(1);
        bool geometryMatches = PointsMatch(actualStart, plannedStart)
            && PointsMatch(actualEnd, plannedEnd)
            || PointsMatch(actualStart, plannedEnd)
            && PointsMatch(actualEnd, plannedStart);
        if (!geometryMatches)
        {
            return false;
        }

        RebarBarType? existingBarType = document.GetElement(rebar.GetTypeId()) as RebarBarType;
        Parameter? diameterParameter = existingBarType?.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
        return diameterParameter?.StorageType == StorageType.Double
            && request.Placement.Component is not null
            && Math.Abs(
                (diameterParameter.AsDouble() * MillimetersPerFoot)
                - request.Placement.Component.DiameterMillimeters) <= 0.2;
    }

    private static bool PointsMatch(XYZ first, XYZ second)
    {
        return first.DistanceTo(second) <= GeometryComparisonToleranceFeet;
    }

    private IsoFieldRebarCreationResult ApplyEngineeringChangePlan(
        Document document,
        Element host,
        IsoFieldHostElement hostElement,
        IReadOnlyList<RebarCreationRequest> requests,
        IsoFieldRebarChangePlan changePlan)
    {
        if (!changePlan.CanApply)
        {
            throw new InvalidOperationException(string.Join(" ", changePlan.Diagnostics));
        }

        if (!changePlan.HasChanges)
        {
            string unchangedMessage =
                $"Армирование уже соответствует расчётной раскладке. Без изменений: {changePlan.UnchangedCount}. Host: {hostElement.DisplayName}.";
            logger.Info($"IsoField engineering rebar is current. HostId={hostElement.ElementId}; Unchanged={changePlan.UnchangedCount}.");
            return new IsoFieldRebarCreationResult(
                0,
                0,
                0,
                changePlan.UnchangedCount,
                Array.Empty<long>(),
                Array.Empty<long>(),
                unchangedMessage);
        }

        Dictionary<string, RebarCreationRequest> requestsByStableId = requests.ToDictionary(
            request => request.Placement.StableId!,
            StringComparer.Ordinal);
        List<long> createdIds = new();
        List<long> deletedIds = new();
        logger.Info(
            $"IsoField engineering rebar change transaction starting. HostId={hostElement.ElementId}; {changePlan.Summary}");
        using Transaction transaction = new(document, "TrueBIM: обновить армирование плиты по изополям");
        transaction.Start();
        try
        {
            foreach (IsoFieldRebarChange change in changePlan.Changes.Where(change =>
                change.Kind is IsoFieldRebarChangeKind.Update or IsoFieldRebarChangeKind.Delete))
            {
                foreach (long elementId in change.ExistingElementIds)
                {
                    document.Delete(RevitElementIds.Create(elementId));
                    deletedIds.Add(elementId);
                }
            }

            foreach (IsoFieldRebarChange change in changePlan.Changes.Where(change =>
                change.Kind is IsoFieldRebarChangeKind.Add or IsoFieldRebarChangeKind.Update))
            {
                RebarCreationRequest request = requestsByStableId[change.StableId];
                Rebar rebar = CreateRebar(document, host, request);
                MarkCreatedRebar(
                    rebar,
                    request.PreviewItem,
                    request.Placement,
                    request.Signature,
                    hostElement.ElementId);
                createdIds.Add(RevitElementIds.GetValue(rebar.Id));
            }

            transaction.Commit();
        }
        catch (Exception exception)
        {
            transaction.RollBack();
            logger.Error(
                $"IsoField engineering rebar change transaction rolled back. HostId={hostElement.ElementId}.",
                exception);
            throw;
        }

        string message = $"Армирование обновлено. {changePlan.Summary} Host: {hostElement.DisplayName}.";
        logger.Info(
            $"IsoField engineering rebar changes applied. HostId={hostElement.ElementId}; {changePlan.Summary}");
        return new IsoFieldRebarCreationResult(
            changePlan.AddCount,
            changePlan.UpdateCount,
            changePlan.DeleteCount,
            changePlan.UnchangedCount,
            createdIds,
            deletedIds,
            message);
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
        TestRebarGeometry Geometry,
        IsoFieldRebarPlacement Placement,
        string? Signature);

    private sealed record TestRebarGeometry(IList<Curve> Curves, XYZ Normal);
}
