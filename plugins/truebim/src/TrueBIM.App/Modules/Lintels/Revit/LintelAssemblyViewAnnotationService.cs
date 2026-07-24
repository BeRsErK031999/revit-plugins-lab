using System.IO;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Lintels.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.Lintels.Revit;

public sealed class LintelAssemblyViewAnnotationService
{
    private const double ReferencePositionTolerance = 1.0 / 304800.0;
    private readonly ITrueBimLogger logger;
    private readonly LintelFrameFamilyPlacementService frameFamilyPlacementService = new();

    public LintelAssemblyViewAnnotationService(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public LintelAssemblyViewFormattingResult Apply(
        Document document,
        ViewSection view,
        AssemblyInstance assembly,
        string frameFamilyFilePath)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (view is null)
        {
            throw new ArgumentNullException(nameof(view));
        }

        if (assembly is null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        if (document.IsReadOnly)
        {
            return LintelAssemblyViewFormattingResult.Failed(
                "Документ Revit доступен только для чтения; оформление вида не изменялось.");
        }

        IReadOnlyList<Element> members = ResolveAssemblyMembers(document, assembly);
        LintelViewProjectedBounds? bounds = ResolveProjectedBounds(view, members);
        if (bounds is null)
        {
            return LintelAssemblyViewFormattingResult.Failed(
                "Не удалось определить границы геометрии элементов Assembly; оформление не применено.");
        }

        LintelAssemblyViewAnnotationLayout layout = LintelAssemblyViewAnnotationLayout.Create(bounds, view.Scale);
        AnnotationReferences references = ResolveReferences(view, members);
        List<string> messages = [];
        if (!references.HasWidthPair)
        {
            messages.Add("Не найдены две крайние вертикальные грани геометрии; линейный размер пропущен.");
        }

        if (references.Bottom is null)
        {
            messages.Add("Не найдена нижняя горизонтальная грань геометрии; отметка «отм.» пропущена.");
        }

        List<Element> createdAnnotations = [];
        int removedCount = 0;
        bool cropAdjusted = false;
        bool dimensionCreated = false;
        bool elevationCreated = false;
        bool frameCreated = false;

        using Transaction transaction = new(document, "TrueBIM: оформление вида перемычки");
        try
        {
            EnsureStatus(
                transaction.Start(),
                TransactionStatus.Started,
                "Revit не начал транзакцию оформления бокового вида.");

            removedCount = DeleteOwnedAnnotations(document, view);
            cropAdjusted = TryAdjustCrop(document, view, layout, messages);

            if (references.HasWidthPair)
            {
                dimensionCreated = TryCreateAnnotation(
                    document,
                    () => CreateWidthDimension(document, view, references.Left!, references.Right!, layout),
                    "Линейный габаритный размер создан.",
                    "Не удалось создать линейный размер",
                    createdAnnotations,
                    messages);
            }

            if (references.Bottom is not null)
            {
                elevationCreated = TryCreateAnnotation(
                    document,
                    () => CreateElevationMark(document, view, references.Bottom),
                    "Отметка «отм.» создана без выноски по нижней грани перемычек.",
                    "Не удалось создать отметку «отм.»",
                    createdAnnotations,
                    messages);
            }

            frameCreated = TryCreateAnnotation(
                document,
                () => CreateFrameFamilyInstance(
                    document,
                    view,
                    layout,
                    frameFamilyFilePath),
                $"Рамка размещена семейством «{Path.GetFileName(frameFamilyFilePath)}».",
                "Не удалось создать ограничивающую рамку",
                createdAnnotations,
                messages);

            LintelAssemblyViewMetadataService.Write(view, assembly, createdAnnotations);
            EnsureStatus(
                transaction.Commit(),
                TransactionStatus.Committed,
                "Revit откатил транзакцию оформления бокового вида.");
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw;
        }

        LintelAssemblyViewFormattingResult result = new(
            dimensionCreated,
            elevationCreated,
            frameCreated,
            cropAdjusted,
            removedCount,
            messages);
        logger.Info(
            $"Lintels side assembly view formatted. Assembly='{assembly.AssemblyTypeName}'; View='{view.Name}'; "
            + $"Created={result.CreatedAnnotationCount}; Removed={removedCount}; CropAdjusted={cropAdjusted}; "
            + $"Dimension={dimensionCreated}; Elevation={elevationCreated}; Frame={frameCreated}.");
        return result;
    }

    private static IReadOnlyList<Element> ResolveAssemblyMembers(Document document, AssemblyInstance assembly)
    {
        List<Element> members = assembly.GetMemberIds()
            .Select(document.GetElement)
            .Where(element => element is not null)
            .Cast<Element>()
            .ToList();
        if (members.Count == 0)
        {
            members.Add(assembly);
        }

        return members;
    }

    private static LintelViewProjectedBounds? ResolveProjectedBounds(
        ViewSection view,
        IReadOnlyList<Element> elements)
    {
        XYZ origin = view.Origin;
        XYZ right = view.RightDirection.Normalize();
        XYZ up = view.UpDirection.Normalize();
        List<double> horizontal = [];
        List<double> vertical = [];

        foreach (Element element in elements)
        {
            BoundingBoxXYZ? bounds = element.get_BoundingBox(view) ?? element.get_BoundingBox(null);
            if (bounds is null)
            {
                continue;
            }

            foreach (XYZ point in GetWorldCorners(bounds))
            {
                horizontal.Add((point - origin).DotProduct(right));
                vertical.Add((point - origin).DotProduct(up));
            }
        }

        return horizontal.Count == 0 || vertical.Count == 0
            ? null
            : new LintelViewProjectedBounds(
                horizontal.Min(),
                horizontal.Max(),
                vertical.Min(),
                vertical.Max());
    }

    private static IReadOnlyList<XYZ> GetWorldCorners(BoundingBoxXYZ bounds)
    {
        Transform transform = bounds.Transform;
        return
        [
            transform.OfPoint(new XYZ(bounds.Min.X, bounds.Min.Y, bounds.Min.Z)),
            transform.OfPoint(new XYZ(bounds.Max.X, bounds.Min.Y, bounds.Min.Z)),
            transform.OfPoint(new XYZ(bounds.Min.X, bounds.Max.Y, bounds.Min.Z)),
            transform.OfPoint(new XYZ(bounds.Max.X, bounds.Max.Y, bounds.Min.Z)),
            transform.OfPoint(new XYZ(bounds.Min.X, bounds.Min.Y, bounds.Max.Z)),
            transform.OfPoint(new XYZ(bounds.Max.X, bounds.Min.Y, bounds.Max.Z)),
            transform.OfPoint(new XYZ(bounds.Min.X, bounds.Max.Y, bounds.Max.Z)),
            transform.OfPoint(new XYZ(bounds.Max.X, bounds.Max.Y, bounds.Max.Z))
        ];
    }

    private static AnnotationReferences ResolveReferences(
        ViewSection view,
        IReadOnlyList<Element> elements)
    {
        Options options = new()
        {
            ComputeReferences = true,
            IncludeNonVisibleObjects = false,
            View = view
        };
        XYZ right = view.RightDirection.Normalize();
        XYZ up = view.UpDirection.Normalize();
        List<ReferenceCandidate> horizontal = [];
        List<ReferenceCandidate> vertical = [];

        foreach (Element element in elements)
        {
            try
            {
                GeometryElement? geometry = element.get_Geometry(options);
                if (geometry is not null)
                {
                    CollectReferenceCandidates(
                        geometry,
                        Transform.Identity,
                        view.Origin,
                        right,
                        up,
                        horizontal,
                        vertical);
                }
            }
            catch
            {
            }
        }

        ReferenceCandidate? left = SelectExtreme(horizontal, minimum: true);
        ReferenceCandidate? rightReference = SelectExtreme(horizontal, minimum: false);
        if (left is not null
            && rightReference is not null
            && Math.Abs(rightReference.Position - left.Position) <= ReferencePositionTolerance)
        {
            left = null;
            rightReference = null;
        }

        return new AnnotationReferences(
            left?.Reference,
            rightReference?.Reference,
            SelectExtreme(vertical, minimum: true));
    }

    private static void CollectReferenceCandidates(
        GeometryElement geometry,
        Transform transform,
        XYZ origin,
        XYZ right,
        XYZ up,
        ICollection<ReferenceCandidate> horizontal,
        ICollection<ReferenceCandidate> vertical)
    {
        foreach (GeometryObject geometryObject in geometry)
        {
            switch (geometryObject)
            {
                case Solid solid:
                    CollectSolidReferenceCandidates(solid, transform, origin, right, up, horizontal, vertical);
                    break;
                case GeometryInstance instance:
                    CollectReferenceCandidates(
                        instance.GetSymbolGeometry(),
                        transform.Multiply(instance.Transform),
                        origin,
                        right,
                        up,
                        horizontal,
                        vertical);
                    break;
            }
        }
    }

    private static void CollectSolidReferenceCandidates(
        Solid solid,
        Transform transform,
        XYZ origin,
        XYZ right,
        XYZ up,
        ICollection<ReferenceCandidate> horizontal,
        ICollection<ReferenceCandidate> vertical)
    {
        if (solid.Faces.IsEmpty)
        {
            return;
        }

        foreach (Face face in solid.Faces)
        {
            if (face is not PlanarFace planarFace || planarFace.Reference is null)
            {
                continue;
            }

            XYZ normal = transform.OfVector(planarFace.FaceNormal).Normalize();
            XYZ point = transform.OfPoint(planarFace.Origin);
            if (Math.Abs(normal.DotProduct(right)) >= 0.98)
            {
                horizontal.Add(new ReferenceCandidate(
                    planarFace.Reference,
                    (point - origin).DotProduct(right),
                    planarFace.Area,
                    point));
            }

            if (Math.Abs(normal.DotProduct(up)) >= 0.98)
            {
                vertical.Add(new ReferenceCandidate(
                    planarFace.Reference,
                    (point - origin).DotProduct(up),
                    planarFace.Area,
                    point));
            }
        }
    }

    private static ReferenceCandidate? SelectExtreme(
        IReadOnlyList<ReferenceCandidate> candidates,
        bool minimum)
    {
        IOrderedEnumerable<ReferenceCandidate> ordered = minimum
            ? candidates.OrderBy(candidate => candidate.Position)
                .ThenByDescending(candidate => candidate.Area)
            : candidates.OrderByDescending(candidate => candidate.Position)
                .ThenByDescending(candidate => candidate.Area);
        return ordered.FirstOrDefault();
    }

    private static Dimension CreateWidthDimension(
        Document document,
        ViewSection view,
        Reference left,
        Reference right,
        LintelAssemblyViewAnnotationLayout layout)
    {
        ReferenceArray references = new();
        references.Append(left);
        references.Append(right);
        Line line = Line.CreateBound(
            ToWorldPoint(view, layout.DimensionStart, layout.DimensionVertical),
            ToWorldPoint(view, layout.DimensionEnd, layout.DimensionVertical));
        return document.Create.NewDimension(view, line, references);
    }

    private static SpotDimension CreateElevationMark(
        Document document,
        ViewSection view,
        ReferenceCandidate bottom)
    {
        XYZ origin = bottom.WorldPoint;
        SpotDimension elevation = document.Create.NewSpotElevation(
            view,
            bottom.Reference,
            origin,
            origin,
            origin,
            bottom.WorldPoint,
            false);
#if REVIT2021_OR_GREATER
        elevation.HasLeader = false;
#endif
        elevation.Prefix = "отм. ";
        return elevation;
    }

    private FamilyInstance CreateFrameFamilyInstance(
        Document document,
        ViewSection view,
        LintelAssemblyViewAnnotationLayout layout,
        string frameFamilyFilePath)
    {
        XYZ insertionPoint = ToWorldPoint(
            view,
            layout.FrameCenterHorizontal,
            layout.FrameCenterVertical);
        return frameFamilyPlacementService.Place(
            document,
            view,
            frameFamilyFilePath,
            insertionPoint);
    }

    private bool TryCreateAnnotation(
        Document document,
        Func<Element> factory,
        string successMessage,
        string failureMessage,
        ICollection<Element> created,
        ICollection<string> messages)
    {
        using SubTransaction subTransaction = new(document);
        try
        {
            EnsureStatus(
                subTransaction.Start(),
                TransactionStatus.Started,
                "Revit не начал вложенную транзакцию аннотации.");
            Element annotation = factory();
            EnsureStatus(
                subTransaction.Commit(),
                TransactionStatus.Committed,
                "Revit откатил вложенную транзакцию аннотации.");
            created.Add(annotation);
            messages.Add(successMessage);
            return true;
        }
        catch (Exception exception)
        {
            if (subTransaction.GetStatus() == TransactionStatus.Started)
            {
                subTransaction.RollBack();
            }

            string message = $"{failureMessage}: {exception.Message}";
            messages.Add(message);
            logger.Warning(message);
            return false;
        }
    }

    private int DeleteOwnedAnnotations(Document document, View view)
    {
        int removed = 0;
        foreach (string uniqueId in LintelAssemblyViewMetadataService.ReadAnnotationUniqueIds(view))
        {
            try
            {
                Element? annotation = document.GetElement(uniqueId);
                if (annotation is null
                    || annotation.OwnerViewId != view.Id
                    || annotation is not (Dimension or FamilyInstance or CurveElement))
                {
                    continue;
                }

                document.Delete(annotation.Id);
                removed++;
            }
            catch (Exception exception)
            {
                logger.Warning($"Failed to remove owned Lintels view annotation '{uniqueId}': {exception.Message}");
            }
        }

        return removed;
    }

    private bool TryAdjustCrop(
        Document document,
        ViewSection view,
        LintelAssemblyViewAnnotationLayout layout,
        ICollection<string> messages)
    {
        using SubTransaction subTransaction = new(document);
        try
        {
            EnsureStatus(
                subTransaction.Start(),
                TransactionStatus.Started,
                "Revit не начал вложенную транзакцию настройки crop.");
            BoundingBoxXYZ cropBox = view.CropBox;
            Transform inverse = cropBox.Transform.Inverse;
            IReadOnlyList<XYZ> localCorners = new[]
            {
                ToWorldPoint(view, layout.FrameMinHorizontal, layout.FrameMinVertical),
                ToWorldPoint(view, layout.FrameMaxHorizontal, layout.FrameMinVertical),
                ToWorldPoint(view, layout.FrameMinHorizontal, layout.FrameMaxVertical),
                ToWorldPoint(view, layout.FrameMaxHorizontal, layout.FrameMaxVertical)
            }.Select(inverse.OfPoint).ToList();
            XYZ newMin = new(
                localCorners.Min(point => point.X),
                localCorners.Min(point => point.Y),
                cropBox.Min.Z);
            XYZ newMax = new(
                localCorners.Max(point => point.X),
                localCorners.Max(point => point.Y),
                cropBox.Max.Z);
            bool changed = !AlmostEqual(cropBox.Min.X, newMin.X)
                || !AlmostEqual(cropBox.Min.Y, newMin.Y)
                || !AlmostEqual(cropBox.Max.X, newMax.X)
                || !AlmostEqual(cropBox.Max.Y, newMax.Y)
                || !view.CropBoxActive
                || view.CropBoxVisible;

            if (changed)
            {
                cropBox.Min = newMin;
                cropBox.Max = newMax;
                view.CropBox = cropBox;
                view.CropBoxActive = true;
                view.CropBoxVisible = false;
            }

            EnsureStatus(
                subTransaction.Commit(),
                TransactionStatus.Committed,
                "Revit откатил вложенную транзакцию настройки crop.");
            if (changed)
            {
                messages.Add("Область обрезки центрирована по геометрии и нормализована под временный формат не менее 1050 × 385 мм в модели.");
            }

            return changed;
        }
        catch (Exception exception)
        {
            if (subTransaction.GetStatus() == TransactionStatus.Started)
            {
                subTransaction.RollBack();
            }

            string message = $"Не удалось настроить область обрезки: {exception.Message}";
            messages.Add(message);
            logger.Warning(message);
            return false;
        }
    }

    private static XYZ ToWorldPoint(ViewSection view, double horizontal, double vertical)
    {
        return view.Origin
            + view.RightDirection.Normalize().Multiply(horizontal)
            + view.UpDirection.Normalize().Multiply(vertical);
    }

    private static bool AlmostEqual(double first, double second)
    {
        return Math.Abs(first - second) <= ReferencePositionTolerance;
    }

    private static void EnsureStatus(
        TransactionStatus actual,
        TransactionStatus expected,
        string message)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException($"{message} Status={actual}.");
        }
    }

    private sealed record AnnotationReferences(
        Reference? Left,
        Reference? Right,
        ReferenceCandidate? Bottom)
    {
        public bool HasWidthPair => Left is not null && Right is not null;
    }

    private sealed record ReferenceCandidate(
        Reference Reference,
        double Position,
        double Area,
        XYZ WorldPoint);
}
