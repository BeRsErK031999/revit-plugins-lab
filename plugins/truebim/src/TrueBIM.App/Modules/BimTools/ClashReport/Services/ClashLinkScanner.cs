using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.ClashReport.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.ClashReport.Services;

public sealed class ClashLinkScanner
{
    private const int MaxHostElements = 900;
    private const int MaxLinkedElementsPerLink = 900;
    private const int MaxClashes = 1000;

    public ClashLinkScanResult Scan(Document document, View activeView)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(activeView, nameof(activeView));

        List<string> messages = [];
        List<ModelElementBox> hostBoxes = CollectModelBoxes(
            new FilteredElementCollector(document, activeView.Id).WhereElementIsNotElementType(),
            Transform.Identity,
            MaxHostElements,
            out bool hostTruncated);
        if (hostTruncated)
        {
            messages.Add($"Основная модель ограничена первыми {MaxHostElements} видимыми элементами активного вида.");
        }

        List<ClashItem> clashes = [];
        IReadOnlyList<RevitLinkInstance> links = new FilteredElementCollector(document)
            .OfClass(typeof(RevitLinkInstance))
            .Cast<RevitLinkInstance>()
            .OrderBy(link => link.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (links.Count == 0)
        {
            messages.Add("В проекте не найдены загруженные RVT-связи.");
            return new ClashLinkScanResult(clashes, messages);
        }

        foreach (RevitLinkInstance link in links)
        {
            Document? linkDocument = link.GetLinkDocument();
            if (linkDocument is null)
            {
                messages.Add($"Связь '{link.Name}' не загружена или недоступна для чтения.");
                continue;
            }

            List<ModelElementBox> linkedBoxes = CollectModelBoxes(
                new FilteredElementCollector(linkDocument).WhereElementIsNotElementType(),
                link.GetTotalTransform(),
                MaxLinkedElementsPerLink,
                out bool linkTruncated);
            if (linkTruncated)
            {
                messages.Add($"Связь '{linkDocument.Title}' ограничена первыми {MaxLinkedElementsPerLink} модельными элементами.");
            }

            foreach (ModelElementBox host in hostBoxes)
            {
                foreach (ModelElementBox linked in linkedBoxes)
                {
                    if (!host.Intersects(linked))
                    {
                        continue;
                    }

                    clashes.Add(CreateItem(document.Title, link, linkDocument.Title, host, linked));
                    if (clashes.Count >= MaxClashes)
                    {
                        messages.Add($"Найдено {MaxClashes} коллизий. Сканирование остановлено, чтобы не подвесить Revit.");
                        return new ClashLinkScanResult(clashes, messages);
                    }
                }
            }
        }

        if (clashes.Count == 0)
        {
            messages.Add("Пересечения между видимыми элементами активного вида и RVT-связями не найдены.");
        }

        return new ClashLinkScanResult(clashes, messages);
    }

    private static List<ModelElementBox> CollectModelBoxes(
        FilteredElementCollector collector,
        Transform transform,
        int limit,
        out bool truncated)
    {
        List<ModelElementBox> boxes = [];
        truncated = false;

        foreach (Element element in collector)
        {
            if (!CanUseElement(element))
            {
                continue;
            }

            BoundingBoxXYZ? boundingBox = element.get_BoundingBox(null);
            if (boundingBox is null)
            {
                continue;
            }

            boxes.Add(ModelElementBox.Create(element, transform, boundingBox));
            if (boxes.Count >= limit)
            {
                truncated = true;
                break;
            }
        }

        return boxes;
    }

    private static bool CanUseElement(Element element)
    {
        Category? category = element.Category;
        return category is not null
            && category.Id != ElementId.InvalidElementId
            && category.CategoryType == CategoryType.Model
            && element is not RevitLinkInstance
            && !element.ViewSpecific;
    }

    private static ClashItem CreateItem(
        string hostDocumentName,
        RevitLinkInstance link,
        string linkDocumentName,
        ModelElementBox host,
        ModelElementBox linked)
    {
        ModelElementBox intersection = host.Intersection(linked);
        XYZ center = intersection.Center;
        string hostLabel = $"{host.CategoryName}: {host.ElementName}";
        string linkedLabel = $"{linkDocumentName}: {linked.CategoryName}: {linked.ElementName}";
        long linkInstanceId = RevitElementIds.GetValue(link.Id);
        ClashItem item = new(
            $"RVT-{linkInstanceId}-{host.ElementId}-{linked.ElementId}",
            $"{host.CategoryName} x {linkDocumentName}",
            host.ElementId,
            linkInstanceId,
            center.X,
            center.Y,
            center.Z,
            ClashStatus.Open,
            string.Empty,
            hostDocumentName,
            linkDocumentName,
            linked.ElementId)
        {
            IsElement1Resolved = true,
            IsElement2Resolved = true,
            Element1Name = hostLabel,
            Element2Name = linkedLabel,
            Message = $"Коллизия найдена через RVT-связь '{link.Name}'."
        };
        item.SetNavigationBounds(
            intersection.MinX,
            intersection.MinY,
            intersection.MinZ,
            intersection.MaxX,
            intersection.MaxY,
            intersection.MaxZ);
        return item;
    }

    private sealed record ModelElementBox(
        long ElementId,
        string CategoryName,
        string ElementName,
        double MinX,
        double MinY,
        double MinZ,
        double MaxX,
        double MaxY,
        double MaxZ)
    {
        public XYZ Center => new((MinX + MaxX) * 0.5, (MinY + MaxY) * 0.5, (MinZ + MaxZ) * 0.5);

        public static ModelElementBox Create(Element element, Transform transform, BoundingBoxXYZ boundingBox)
        {
            XYZ[] points =
            [
                transform.OfPoint(new XYZ(boundingBox.Min.X, boundingBox.Min.Y, boundingBox.Min.Z)),
                transform.OfPoint(new XYZ(boundingBox.Min.X, boundingBox.Min.Y, boundingBox.Max.Z)),
                transform.OfPoint(new XYZ(boundingBox.Min.X, boundingBox.Max.Y, boundingBox.Min.Z)),
                transform.OfPoint(new XYZ(boundingBox.Min.X, boundingBox.Max.Y, boundingBox.Max.Z)),
                transform.OfPoint(new XYZ(boundingBox.Max.X, boundingBox.Min.Y, boundingBox.Min.Z)),
                transform.OfPoint(new XYZ(boundingBox.Max.X, boundingBox.Min.Y, boundingBox.Max.Z)),
                transform.OfPoint(new XYZ(boundingBox.Max.X, boundingBox.Max.Y, boundingBox.Min.Z)),
                transform.OfPoint(new XYZ(boundingBox.Max.X, boundingBox.Max.Y, boundingBox.Max.Z))
            ];

            return new ModelElementBox(
                RevitElementIds.GetValue(element.Id),
                element.Category?.Name ?? "Без категории",
                GetElementName(element),
                points.Min(point => point.X),
                points.Min(point => point.Y),
                points.Min(point => point.Z),
                points.Max(point => point.X),
                points.Max(point => point.Y),
                points.Max(point => point.Z));
        }

        public bool Intersects(ModelElementBox other)
        {
            return MinX <= other.MaxX
                && MaxX >= other.MinX
                && MinY <= other.MaxY
                && MaxY >= other.MinY
                && MinZ <= other.MaxZ
                && MaxZ >= other.MinZ;
        }

        public ModelElementBox Intersection(ModelElementBox other)
        {
            return this with
            {
                MinX = Math.Max(MinX, other.MinX),
                MinY = Math.Max(MinY, other.MinY),
                MinZ = Math.Max(MinZ, other.MinZ),
                MaxX = Math.Min(MaxX, other.MaxX),
                MaxY = Math.Min(MaxY, other.MaxY),
                MaxZ = Math.Min(MaxZ, other.MaxZ)
            };
        }

        private static string GetElementName(Element element)
        {
            try
            {
                return string.IsNullOrWhiteSpace(element.Name)
                    ? element.GetType().Name
                    : element.Name;
            }
            catch (InvalidOperationException)
            {
                return element.GetType().Name;
            }
        }
    }
}

public sealed record ClashLinkScanResult(IReadOnlyList<ClashItem> Items, IReadOnlyList<string> Messages);
