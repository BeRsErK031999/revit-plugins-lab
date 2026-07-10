using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.OpeningViews.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.OpeningViews.Services;

public sealed class OpeningViewAnnotationService
{
    private const string OpeningSuffix = " (проём)";

    public OpeningViewAnnotationPreview Preview(Document document, ViewSection view, FamilyInstance source)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(view, nameof(view));
        Guard.NotNull(source, nameof(source));

        List<string> warnings = [];
        bool hasBounds = OpeningViewBoundsResolver.Resolve(source, view) is not null;
        Reference? left = GetReference(source, FamilyInstanceReferenceType.Left);
        Reference? right = GetReference(source, FamilyInstanceReferenceType.Right);
        Reference? bottom = GetReference(source, FamilyInstanceReferenceType.Bottom);
        Reference? top = GetReference(source, FamilyInstanceReferenceType.Top);
        ElementId textTypeId = ResolveTextNoteTypeId(document);

        bool hasTextType = textTypeId != ElementId.InvalidElementId;
        bool canCreateTitle = hasTextType && hasBounds;
        bool canCreateWidth = left is not null && right is not null && hasBounds;
        bool canCreateHeight = bottom is not null && top is not null && hasBounds;
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
            warnings.Add("Семейство не содержит стабильные reference planes Left/Right; ширина не будет нанесена.");
        }

        if (!canCreateHeight)
        {
            warnings.Add("Семейство не содержит стабильные reference planes Bottom/Top; высота не будет нанесена.");
        }

        return new OpeningViewAnnotationPreview(
            ResolveTitle(source),
            canCreateTitle,
            canCreateWidth,
            canCreateHeight,
            warnings);
    }

    public OpeningViewAnnotationResult Apply(
        Document document,
        ViewSection view,
        FamilyInstance source,
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
        Reference? left = GetReference(source, FamilyInstanceReferenceType.Left);
        Reference? right = GetReference(source, FamilyInstanceReferenceType.Right);
        Reference? bottom = GetReference(source, FamilyInstanceReferenceType.Bottom);
        Reference? top = GetReference(source, FamilyInstanceReferenceType.Top);
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

            if (preview.CanCreateWidthDimension && left is not null && right is not null)
            {
                TryCreateAnnotation(
                    document,
                    () => CreateWidthDimension(document, view, left, right, layout),
                    "Размер ширины проёма создан.",
                    "Не удалось создать размер ширины",
                    createdAnnotations,
                    messages,
                    logger);
            }

            if (preview.CanCreateHeightDimension && bottom is not null && top is not null)
            {
                TryCreateAnnotation(
                    document,
                    () => CreateHeightDimension(document, view, bottom, top, layout),
                    "Размер высоты проёма создан.",
                    "Не удалось создать размер высоты",
                    createdAnnotations,
                    messages,
                    logger);
            }

            string categoryKey = OpeningViewSourceResolver.GetCategoryKey(source);
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

        string categoryName = OpeningViewCategoryKeys.Normalize(categoryKey) == OpeningViewCategoryKeys.Window
            ? "Окно"
            : "Дверь";
        return $"{categoryName} {elementId}";
    }

    private static string ResolveTitle(FamilyInstance source)
    {
        FamilySymbol? symbol = source.Symbol;
        return ResolveTitle(
            GetParameterText(symbol?.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)),
            GetParameterText(source.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)),
            symbol?.Name,
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
        OpeningViewAnnotationLayout layout)
    {
        ReferenceArray references = new();
        references.Append(left);
        references.Append(right);
        Line line = Line.CreateBound(
            ToWorldPoint(view, layout.HorizontalStart, layout.HorizontalPosition),
            ToWorldPoint(view, layout.HorizontalEnd, layout.HorizontalPosition));
        Dimension dimension = document.Create.NewDimension(view, line, references);
        dimension.Suffix = OpeningSuffix;
        return dimension;
    }

    private static Element CreateHeightDimension(
        Document document,
        ViewSection view,
        Reference bottom,
        Reference top,
        OpeningViewAnnotationLayout layout)
    {
        ReferenceArray references = new();
        references.Append(bottom);
        references.Append(top);
        Line line = Line.CreateBound(
            ToWorldPoint(view, layout.VerticalPosition, layout.VerticalStart),
            ToWorldPoint(view, layout.VerticalPosition, layout.VerticalEnd));
        Dimension dimension = document.Create.NewDimension(view, line, references);
        dimension.Suffix = OpeningSuffix;
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
}
