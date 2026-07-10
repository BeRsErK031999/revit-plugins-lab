using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.OpeningViews.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.OpeningViews.Services;

public sealed class OpeningViewAnnotationService
{
    private const string OpeningSuffix = " (проём)";
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

        bool isCurtainWall = OpeningViewElementClassifier.IsCurtainWall(source);
        return new OpeningViewAnnotationPreview(
            ResolveTitle(document, source),
            canCreateTitle,
            canCreateWidth,
            canCreateHeight,
            warnings,
            isCurtainWall);
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
        string dimensionSuffix = ResolveDimensionSuffix(categoryKey);
        string dimensionTarget = categoryKey == OpeningViewCategoryKeys.CurtainWall ? "габарита витража" : "проёма";
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
                    () => CreateWidthDimension(document, view, references.Left!, references.Right!, layout, dimensionSuffix),
                    $"Размер ширины {dimensionTarget} создан.",
                    "Не удалось создать размер ширины",
                    createdAnnotations,
                    messages,
                    logger);
            }

            if (preview.CanCreateHeightDimension && references.Bottom is not null && references.Top is not null)
            {
                TryCreateAnnotation(
                    document,
                    () => CreateHeightDimension(document, view, references.Bottom!, references.Top!, layout, dimensionSuffix),
                    $"Размер высоты {dimensionTarget} создан.",
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

    public static string ResolveDimensionSuffix(string? categoryKey)
    {
        return OpeningViewCategoryKeys.Normalize(categoryKey) == OpeningViewCategoryKeys.CurtainWall
            ? CurtainWallSuffix
            : OpeningSuffix;
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
            return new OpeningAnnotationReferences(
                GetReference(familyInstance, FamilyInstanceReferenceType.Left),
                GetReference(familyInstance, FamilyInstanceReferenceType.Right),
                GetReference(familyInstance, FamilyInstanceReferenceType.Bottom),
                GetReference(familyInstance, FamilyInstanceReferenceType.Top));
        }

        return source is Wall wall && OpeningViewElementClassifier.IsCurtainWall(wall)
            ? ResolveCurtainWallReferences(document, view, wall)
            : OpeningAnnotationReferences.Empty;
    }

    private static OpeningAnnotationReferences ResolveCurtainWallReferences(
        Document document,
        ViewSection view,
        Wall wall)
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

        foreach (Element element in CollectCurtainWallGeometryElements(document, wall))
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

        return new OpeningAnnotationReferences(
            SelectReference(horizontal, minimum: true),
            SelectReference(horizontal, minimum: false),
            SelectReference(vertical, minimum: true),
            SelectReference(vertical, minimum: false));
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
                horizontal.Add(new ReferenceCandidate(planarFace.Reference, (point - origin).DotProduct(right)));
            }

            if (Math.Abs(normal.DotProduct(up)) >= 0.98)
            {
                vertical.Add(new ReferenceCandidate(planarFace.Reference, (point - origin).DotProduct(up)));
            }
        }
    }

    private static Reference? SelectReference(IReadOnlyList<ReferenceCandidate> candidates, bool minimum)
    {
        if (!OpeningViewReferencePairSelector.TrySelect(
            candidates.Select(candidate => candidate.Position).ToList(),
            out int minimumIndex,
            out int maximumIndex))
        {
            return null;
        }

        return candidates[minimum ? minimumIndex : maximumIndex].Reference;
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
        Reference? Top)
    {
        public static OpeningAnnotationReferences Empty { get; } = new(null, null, null, null);
    }

    private sealed record ReferenceCandidate(Reference Reference, double Position);
}
