using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.OpeningViews.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.OpeningViews.Services;

public sealed class OpeningViewAnnotationService
{
    private const string OpeningSuffix = " (проём)";
    private const string ProductSuffix = " (габарит изделия)";
    private const string CurtainWallSuffix = " (габарит витража)";

    public OpeningViewAnnotationPreview Preview(Document document, ViewSection view, Element source)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(view, nameof(view));
        Guard.NotNull(source, nameof(source));

        List<string> warnings = [];
        bool hasBounds = OpeningViewBoundsResolver.Resolve(source, view) is not null;
        OpeningAnnotationReferences references = ResolveReferences(document, view, source);
        ElementId textTypeId = ResolveTextNoteTypeId(document);

        bool hasTextType = textTypeId != ElementId.InvalidElementId;
        bool canCreateTitle = hasTextType && hasBounds;
        bool canCreateWidth = references.Left is not null && references.Right is not null && hasBounds;
        bool canCreateHeight = references.Bottom is not null && references.Top is not null && hasBounds;
        if (!hasBounds)
        {
            warnings.Add("Не найден полный bounding box проёма.");
        }

        if (!hasTextType)
        {
            warnings.Add("В проекте нет доступного TextNoteType для марки над видом.");
        }

        if (!canCreateWidth)
        {
            warnings.Add(OpeningViewElementClassifier.IsCurtainWall(source)
                ? "Не найдены крайние вертикальные грани панелей, импостов или стены витража; ширина не будет нанесена."
                : "Семейство не содержит стабильные reference planes Left/Right; ширина не будет нанесена.");
        }

        if (!canCreateHeight)
        {
            warnings.Add(OpeningViewElementClassifier.IsCurtainWall(source)
                ? "Не найдены крайние горизонтальные грани панелей, импостов или стены витража; высота не будет нанесена."
                : "Семейство не содержит стабильные reference planes Bottom/Top; высота не будет нанесена.");
        }

        if (references.WidthSource == DimensionReferenceSource.VisibleFamilyGeometry)
        {
            warnings.Add("Reference planes проёма Left/Right не найдены: ширина будет построена по крайним видимым граням и помечена как габарит изделия.");
        }

        if (references.HeightSource == DimensionReferenceSource.VisibleFamilyGeometry)
        {
            warnings.Add("Reference planes проёма Bottom/Top не найдены: высота будет построена по крайним видимым граням и помечена как габарит изделия.");
        }

        bool isCurtainWall = OpeningViewElementClassifier.IsCurtainWall(source);
        return new OpeningViewAnnotationPreview(
            ResolveTitle(document, source),
            canCreateTitle,
            canCreateWidth,
            canCreateHeight,
            warnings,
            isCurtainWall,
            references.WidthSource == DimensionReferenceSource.VisibleFamilyGeometry,
            references.HeightSource == DimensionReferenceSource.VisibleFamilyGeometry,
            ResolveReferenceSourceDisplay(references.WidthSource),
            ResolveReferenceSourceDisplay(references.HeightSource));
    }

    public OpeningViewAnnotationResult Apply(
        Document document,
        ViewSection view,
        Element source,
        ITrueBimLogger logger)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(view, nameof(view));
        Guard.NotNull(source, nameof(source));
        Guard.NotNull(logger, nameof(logger));

        OpeningViewAnnotationPreview preview = Preview(document, view, source);
        List<string> messages = [.. preview.Warnings];
        if (!preview.CanApply)
        {
            return new OpeningViewAnnotationResult(view.Name, preview.Title, 0, 0, messages);
        }

        OpeningViewBoundsResult? boundsResult = OpeningViewBoundsResolver.Resolve(source, view);
        if (boundsResult is null)
        {
            messages.Add("Полная геометрия проёма недоступна.");
            return new OpeningViewAnnotationResult(view.Name, preview.Title, 0, 0, messages);
        }

        OpeningViewProjectedBounds projectedBounds = ProjectBounds(boundsResult.Bounds, view);
        OpeningViewAnnotationLayout layout = OpeningViewAnnotationLayout.Create(projectedBounds, view.Scale);
        OpeningAnnotationReferences references = ResolveReferences(document, view, source);
        string categoryKey = OpeningViewSourceResolver.GetCategoryKey(source);
        string widthDimensionSuffix = ResolveDimensionSuffix(
            categoryKey,
            references.WidthSource == DimensionReferenceSource.VisibleFamilyGeometry);
        string heightDimensionSuffix = ResolveDimensionSuffix(
            categoryKey,
            references.HeightSource == DimensionReferenceSource.VisibleFamilyGeometry);
        string widthDimensionTarget = ResolveDimensionTarget(categoryKey, references.WidthSource);
        string heightDimensionTarget = ResolveDimensionTarget(categoryKey, references.HeightSource);
        ElementId textTypeId = ResolveTextNoteTypeId(document);
        OpeningViewMetadata? previousMetadata = OpeningViewMetadataService.Read(view);
        List<Element> createdAnnotations = [];
        int removedCount = 0;

        using Transaction transaction = new(document, "TrueBIM Opening View Annotation");
        transaction.Start();
        try
        {
            removedCount = DeleteOwnedAnnotations(document, view, previousMetadata, logger);
            TryEnsureCropContainsLayout(view, layout, document, messages, logger);

            if (preview.CanCreateTitle)
            {
                TryCreateAnnotation(
                    document,
                    () => CreateTitle(document, view, preview.Title, textTypeId, layout),
                    "Марка над видом создана.",
                    "Не удалось создать марку над видом",
                    createdAnnotations,
                    messages,
                    logger);
            }

            if (preview.CanCreateWidthDimension && references.Left is not null && references.Right is not null)
            {
                TryCreateAnnotation(
                    document,
                    () => CreateWidthDimension(document, view, references.Left!, references.Right!, layout, widthDimensionSuffix),
                    $"Размер ширины {widthDimensionTarget} создан.",
                    "Не удалось создать размер ширины",
                    createdAnnotations,
                    messages,
                    logger);
            }

            if (preview.CanCreateHeightDimension && references.Bottom is not null && references.Top is not null)
            {
                TryCreateAnnotation(
                    document,
                    () => CreateHeightDimension(document, view, references.Bottom!, references.Top!, layout, heightDimensionSuffix),
                    $"Размер высоты {heightDimensionTarget} создан.",
                    "Не удалось создать размер высоты",
                    createdAnnotations,
                    messages,
                    logger);
            }

            OpeningViewMetadataService.WriteAnnotations(view, source, categoryKey, createdAnnotations);
            transaction.Commit();
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw;
        }

        return new OpeningViewAnnotationResult(
            view.Name,
            preview.Title,
            removedCount,
            createdAnnotations.Count,
            messages);
    }

    public static string ResolveTitle(
        string? typeMark,
        string? instanceMark,
        string? typeName,
        string categoryKey,
        long elementId)
    {
        foreach (string? value in new[] { typeMark, instanceMark, typeName })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value!.Trim();
            }
        }

        string categoryName = OpeningViewCategoryKeys.Normalize(categoryKey) switch
        {
            OpeningViewCategoryKeys.Window => "Окно",
            OpeningViewCategoryKeys.CurtainWall => "Витраж",
            _ => "Дверь"
        };
        return $"{categoryName} {elementId}";
    }

    public static string ResolveDimensionSuffix(string? categoryKey, bool usesFamilyGeometry = false)
    {
        if (OpeningViewCategoryKeys.Normalize(categoryKey) == OpeningViewCategoryKeys.CurtainWall)
        {
            return CurtainWallSuffix;
        }

        return usesFamilyGeometry ? ProductSuffix : OpeningSuffix;
    }

    private static string ResolveTitle(Document document, Element source)
    {
        ElementType? type = document.GetElement(source.GetTypeId()) as ElementType;
        return ResolveTitle(
            GetParameterText(type?.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)),
            GetParameterText(source.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)),
            type?.Name,
            OpeningViewSourceResolver.GetCategoryKey(source),
            RevitElementIds.GetValue(source.Id));
    }

    private static string? GetParameterText(Parameter? parameter)
    {
        if (parameter is null)
        {
            return null;
        }

        string? value = parameter.AsString();
        return string.IsNullOrWhiteSpace(value) ? parameter.AsValueString() : value;
    }

    private static Element CreateTitle(
        Document document,
        ViewSection view,
        string title,
        ElementId textTypeId,
        OpeningViewAnnotationLayout layout)
    {
        TextNoteOptions options = new(textTypeId)
        {
            HorizontalAlignment = HorizontalTextAlignment.Center,
            VerticalAlignment = VerticalTextAlignment.Bottom
        };
        return TextNote.Create(
            document,
            view.Id,
            ToWorldPoint(view, layout.TitleHorizontal, layout.TitleVertical),
            title,
            options);
    }

    private static Element CreateWidthDimension(
        Document document,
        ViewSection view,
        Reference left,
        Reference right,
        OpeningViewAnnotationLayout layout,
        string suffix)
    {
        ReferenceArray references = new();
        references.Append(left);
        references.Append(right);
        Line line = Line.CreateBound(
            ToWorldPoint(view, layout.HorizontalStart, layout.HorizontalPosition),
            ToWorldPoint(view, layout.HorizontalEnd, layout.HorizontalPosition));
        Dimension dimension = document.Create.NewDimension(view, line, references);
        dimension.Suffix = suffix;
        return dimension;
    }

    private static Element CreateHeightDimension(
        Document document,
        ViewSection view,
        Reference bottom,
        Reference top,
        OpeningViewAnnotationLayout layout,
        string suffix)
    {
        ReferenceArray references = new();
        references.Append(bottom);
        references.Append(top);
        Line line = Line.CreateBound(
            ToWorldPoint(view, layout.VerticalPosition, layout.VerticalStart),
            ToWorldPoint(view, layout.VerticalPosition, layout.VerticalEnd));
        Dimension dimension = document.Create.NewDimension(view, line, references);
        dimension.Suffix = suffix;
        return dimension;
    }

    private static void TryCreateAnnotation(
        Document document,
        Func<Element> factory,
        string successMessage,
        string failureMessage,
        ICollection<Element> created,
        ICollection<string> messages,
        ITrueBimLogger logger)
    {
        using SubTransaction subTransaction = new(document);
        try
        {
            subTransaction.Start();
            Element annotation = factory();
            subTransaction.Commit();
            created.Add(annotation);
            messages.Add(successMessage);
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
        }
    }

    private static int DeleteOwnedAnnotations(
        Document document,
        View view,
        OpeningViewMetadata? metadata,
        ITrueBimLogger logger)
    {
        if (metadata is null || metadata.AnnotationUniqueIds.Count == 0)
        {
            return 0;
        }

        int removed = 0;
        foreach (string uniqueId in metadata.AnnotationUniqueIds)
        {
            try
            {
                Element? annotation = document.GetElement(uniqueId);
                if (annotation is null
                    || annotation.OwnerViewId != view.Id
                    || annotation is not (Dimension or TextNote))
                {
                    continue;
                }

                document.Delete(annotation.Id);
                removed++;
            }
            catch (Exception exception)
            {
                logger.Warning($"Failed to remove owned opening annotation '{uniqueId}': {exception.Message}");
            }
        }

        return removed;
    }

    private static void TryEnsureCropContainsLayout(
        ViewSection view,
        OpeningViewAnnotationLayout layout,
        Document document,
        ICollection<string> messages,
        ITrueBimLogger logger)
    {
        using SubTransaction subTransaction = new(document);
        try
        {
            subTransaction.Start();
            BoundingBoxXYZ cropBox = view.CropBox;
            Transform inverse = cropBox.Transform.Inverse;
            IReadOnlyList<XYZ> requiredCorners =
            [
                ToWorldPoint(view, layout.RequiredMinHorizontal, layout.RequiredMinVertical),
                ToWorldPoint(view, layout.RequiredMaxHorizontal, layout.RequiredMinVertical),
                ToWorldPoint(view, layout.RequiredMinHorizontal, layout.RequiredMaxVertical),
                ToWorldPoint(view, layout.RequiredMaxHorizontal, layout.RequiredMaxVertical)
            ];
            IReadOnlyList<XYZ> localCorners = requiredCorners.Select(inverse.OfPoint).ToList();
            cropBox.Min = new XYZ(
                Math.Min(cropBox.Min.X, localCorners.Min(point => point.X)),
                Math.Min(cropBox.Min.Y, localCorners.Min(point => point.Y)),
                cropBox.Min.Z);
            cropBox.Max = new XYZ(
                Math.Max(cropBox.Max.X, localCorners.Max(point => point.X)),
                Math.Max(cropBox.Max.Y, localCorners.Max(point => point.Y)),
                cropBox.Max.Z);
            view.CropBox = cropBox;
            subTransaction.Commit();
        }
        catch (Exception exception)
        {
            if (subTransaction.GetStatus() == TransactionStatus.Started)
            {
                subTransaction.RollBack();
            }

            string message = $"Crop не расширен под оформление: {exception.Message}";
            messages.Add(message);
            logger.Warning(message);
        }
    }

    private static OpeningViewProjectedBounds ProjectBounds(OpeningViewBounds bounds, ViewSection view)
    {
        XYZ origin = view.Origin;
        XYZ right = view.RightDirection.Normalize();
        XYZ up = view.UpDirection.Normalize();
        IReadOnlyList<XYZ> corners =
        [
            new XYZ(bounds.MinX, bounds.MinY, bounds.MinZ),
            new XYZ(bounds.MaxX, bounds.MinY, bounds.MinZ),
            new XYZ(bounds.MinX, bounds.MaxY, bounds.MinZ),
            new XYZ(bounds.MaxX, bounds.MaxY, bounds.MinZ),
            new XYZ(bounds.MinX, bounds.MinY, bounds.MaxZ),
            new XYZ(bounds.MaxX, bounds.MinY, bounds.MaxZ),
            new XYZ(bounds.MinX, bounds.MaxY, bounds.MaxZ),
            new XYZ(bounds.MaxX, bounds.MaxY, bounds.MaxZ)
        ];
        IReadOnlyList<double> horizontal = corners.Select(point => (point - origin).DotProduct(right)).ToList();
        IReadOnlyList<double> vertical = corners.Select(point => (point - origin).DotProduct(up)).ToList();
        return new OpeningViewProjectedBounds(horizontal.Min(), horizontal.Max(), vertical.Min(), vertical.Max());
    }

    private static XYZ ToWorldPoint(ViewSection view, double horizontal, double vertical)
    {
        return view.Origin
            + view.RightDirection.Normalize().Multiply(horizontal)
            + view.UpDirection.Normalize().Multiply(vertical);
    }

    private static OpeningAnnotationReferences ResolveReferences(
        Document document,
        ViewSection view,
        Element source)
    {
        if (source is FamilyInstance familyInstance)
        {
            return ResolveFamilyInstanceReferences(document, view, familyInstance);
        }

        return source is Wall wall && OpeningViewElementClassifier.IsCurtainWall(wall)
            ? ResolveCurtainWallReferences(document, view, wall)
            : OpeningAnnotationReferences.Empty;
    }

    private static OpeningAnnotationReferences ResolveFamilyInstanceReferences(
        Document document,
        ViewSection view,
        FamilyInstance familyInstance)
    {
        Reference? standardLeft = GetReference(familyInstance, FamilyInstanceReferenceType.Left);
        Reference? standardRight = GetReference(familyInstance, FamilyInstanceReferenceType.Right);
        Reference? standardBottom = GetReference(familyInstance, FamilyInstanceReferenceType.Bottom);
        Reference? standardTop = GetReference(familyInstance, FamilyInstanceReferenceType.Top);

        Reference? semanticLeft = standardLeft ?? GetNamedReference(familyInstance, OpeningViewReferenceSide.Left);
        Reference? semanticRight = standardRight ?? GetNamedReference(familyInstance, OpeningViewReferenceSide.Right);
        Reference? semanticBottom = standardBottom ?? GetNamedReference(familyInstance, OpeningViewReferenceSide.Bottom);
        Reference? semanticTop = standardTop ?? GetNamedReference(familyInstance, OpeningViewReferenceSide.Top);

        bool hasSemanticWidth = IsUsablePair(document, semanticLeft, semanticRight);
        bool hasSemanticHeight = IsUsablePair(document, semanticBottom, semanticTop);
        OpeningAnnotationReferences geometry = hasSemanticWidth && hasSemanticHeight
            ? OpeningAnnotationReferences.Empty
            : ResolveGeometryReferences(
                view,
                [familyInstance],
                DimensionReferenceSource.VisibleFamilyGeometry);

        Reference? left = hasSemanticWidth ? semanticLeft : geometry.Left;
        Reference? right = hasSemanticWidth ? semanticRight : geometry.Right;
        Reference? bottom = hasSemanticHeight ? semanticBottom : geometry.Bottom;
        Reference? top = hasSemanticHeight ? semanticTop : geometry.Top;

        DimensionReferenceSource widthSource = hasSemanticWidth
            ? standardLeft is not null && standardRight is not null
                ? DimensionReferenceSource.StandardFamilyPlanes
                : DimensionReferenceSource.NamedFamilyPlanes
            : geometry.WidthSource;
        DimensionReferenceSource heightSource = hasSemanticHeight
            ? standardBottom is not null && standardTop is not null
                ? DimensionReferenceSource.StandardFamilyPlanes
                : DimensionReferenceSource.NamedFamilyPlanes
            : geometry.HeightSource;

        return new OpeningAnnotationReferences(left, right, bottom, top, widthSource, heightSource);
    }

    private static OpeningAnnotationReferences ResolveCurtainWallReferences(
        Document document,
        ViewSection view,
        Wall wall)
    {
        return ResolveGeometryReferences(
            view,
            CollectCurtainWallGeometryElements(document, wall),
            DimensionReferenceSource.CurtainWallGeometry);
    }

    private static OpeningAnnotationReferences ResolveGeometryReferences(
        ViewSection view,
        IEnumerable<Element> elements,
        DimensionReferenceSource source)
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
            catch (Exception)
            {
            }
        }

        Reference? left = SelectReference(horizontal, minimum: true);
        Reference? rightReference = SelectReference(horizontal, minimum: false);
        Reference? bottom = SelectReference(vertical, minimum: true);
        Reference? top = SelectReference(vertical, minimum: false);
        return new OpeningAnnotationReferences(
            left,
            rightReference,
            bottom,
            top,
            left is not null && rightReference is not null ? source : DimensionReferenceSource.None,
            bottom is not null && top is not null ? source : DimensionReferenceSource.None);
    }

    private static IReadOnlyList<Element> CollectCurtainWallGeometryElements(Document document, Wall wall)
    {
        List<Element> elements = [wall];
        HashSet<long> elementIds = [RevitElementIds.GetValue(wall.Id)];
        CurtainGrid? grid = wall.CurtainGrid;
        if (grid is null)
        {
            return elements;
        }

        foreach (ElementId elementId in grid.GetPanelIds().Concat(grid.GetMullionIds()))
        {
            long value = RevitElementIds.GetValue(elementId);
            if (elementIds.Add(value) && document.GetElement(elementId) is Element element)
            {
                elements.Add(element);
            }
        }

        return elements;
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
                    GeometryElement symbolGeometry = instance.GetSymbolGeometry();
                    CollectReferenceCandidates(
                        symbolGeometry,
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
                    planarFace.Area));
            }

            if (Math.Abs(normal.DotProduct(up)) >= 0.98)
            {
                vertical.Add(new ReferenceCandidate(
                    planarFace.Reference,
                    (point - origin).DotProduct(up),
                    planarFace.Area));
            }
        }
    }

    private static Reference? SelectReference(IReadOnlyList<ReferenceCandidate> candidates, bool minimum)
    {
        if (!OpeningViewReferencePairSelector.TrySelect(
            candidates.Select(candidate => candidate.Position).ToList(),
            candidates.Select(candidate => candidate.Area).ToList(),
            out int minimumIndex,
            out int maximumIndex))
        {
            return null;
        }

        return candidates[minimum ? minimumIndex : maximumIndex].Reference;
    }

    private static Reference? GetNamedReference(
        FamilyInstance source,
        OpeningViewReferenceSide side)
    {
        List<NamedReferenceCandidate> candidates = [];
        CollectNamedReferences(source, FamilyInstanceReferenceType.StrongReference, side, typePriority: 10, candidates);
        CollectNamedReferences(source, FamilyInstanceReferenceType.WeakReference, side, typePriority: 0, candidates);
        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(candidate => candidate.Reference)
            .FirstOrDefault();
    }

    private static void CollectNamedReferences(
        FamilyInstance source,
        FamilyInstanceReferenceType referenceType,
        OpeningViewReferenceSide side,
        int typePriority,
        ICollection<NamedReferenceCandidate> candidates)
    {
        try
        {
            foreach (Reference reference in source.GetReferences(referenceType) ?? [])
            {
                string name = source.GetReferenceName(reference) ?? string.Empty;
                int score = OpeningViewReferenceNameMatcher.Score(name, side);
                if (score > 0)
                {
                    candidates.Add(new NamedReferenceCandidate(reference, name, score + typePriority));
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private static bool IsUsablePair(Document document, Reference? first, Reference? second)
    {
        if (first is null || second is null)
        {
            return false;
        }

        try
        {
            return !string.Equals(
                first.ConvertToStableRepresentation(document),
                second.ConvertToStableRepresentation(document),
                StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return !ReferenceEquals(first, second);
        }
    }

    private static Reference? GetReference(FamilyInstance source, FamilyInstanceReferenceType referenceType)
    {
        try
        {
            return source.GetReferences(referenceType)?.FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string ResolveReferenceSourceDisplay(DimensionReferenceSource source)
    {
        return source switch
        {
            DimensionReferenceSource.StandardFamilyPlanes => "стандартные planes семейства",
            DimensionReferenceSource.NamedFamilyPlanes => "именованные planes семейства",
            DimensionReferenceSource.VisibleFamilyGeometry => "крайние видимые грани изделия",
            DimensionReferenceSource.CurtainWallGeometry => "крайние грани витража",
            _ => string.Empty
        };
    }

    private static string ResolveDimensionTarget(string categoryKey, DimensionReferenceSource source)
    {
        if (OpeningViewCategoryKeys.Normalize(categoryKey) == OpeningViewCategoryKeys.CurtainWall)
        {
            return "габарита витража";
        }

        return source == DimensionReferenceSource.VisibleFamilyGeometry
            ? "габарита изделия"
            : "проёма";
    }

    private static ElementId ResolveTextNoteTypeId(Document document)
    {
        ElementId defaultId = document.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
        if (defaultId != ElementId.InvalidElementId && document.GetElement(defaultId) is TextNoteType)
        {
            return defaultId;
        }

        return new FilteredElementCollector(document)
            .OfClass(typeof(TextNoteType))
            .Cast<TextNoteType>()
            .OrderBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(type => type.Id)
            .FirstOrDefault() ?? ElementId.InvalidElementId;
    }

    private sealed record OpeningAnnotationReferences(
        Reference? Left,
        Reference? Right,
        Reference? Bottom,
        Reference? Top,
        DimensionReferenceSource WidthSource,
        DimensionReferenceSource HeightSource)
    {
        public static OpeningAnnotationReferences Empty { get; } = new(
            null,
            null,
            null,
            null,
            DimensionReferenceSource.None,
            DimensionReferenceSource.None);
    }

    private sealed record ReferenceCandidate(Reference Reference, double Position, double Area);

    private sealed record NamedReferenceCandidate(Reference Reference, string Name, int Score);

    private enum DimensionReferenceSource
    {
        None,
        StandardFamilyPlanes,
        NamedFamilyPlanes,
        VisibleFamilyGeometry,
        CurtainWallGeometry
    }
}
