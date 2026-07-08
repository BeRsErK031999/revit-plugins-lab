using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.ClashReport.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.ClashReport.Services;

public sealed class ClashLinkScanner
{
    private const int MaxHostElements = 900;
    private const int MaxLinkedElementsPerLink = 900;
    private const int MaxClashes = 1000;
    private const double FeetPerMillimeter = 1.0 / 304.8;

    public ClashLinkScanResult Scan(Document document, View activeView)
    {
        return Scan(document, activeView, new ClashScanOptions { ScanCurrentModel = false, ScanRvtLinks = true });
    }

    public ClashLinkScanResult Scan(Document document, View activeView, ClashScanOptions options)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(activeView, nameof(activeView));
        Guard.NotNull(options, nameof(options));

        ClashScanOptions normalized = NormalizeOptions(options);
        List<string> messages = [];
        List<ClashItem> clashes = [];

        if (!normalized.ScanCurrentModel && !normalized.ScanRvtLinks && !normalized.ScanLinksAgainstEachOther)
        {
            messages.Add("Не выбран ни один режим проверки.");
            return new ClashLinkScanResult(clashes, messages);
        }

        double minimumOverlap = normalized.MinimumOverlapMm * FeetPerMillimeter;
        bool needsHostBoxes = normalized.ScanCurrentModel || normalized.ScanRvtLinks;
        List<ModelElementBox> hostBoxes = needsHostBoxes
            ? CollectModelBoxes(
                document.Title,
                new FilteredElementCollector(document, activeView.Id).WhereElementIsNotElementType(),
                Transform.Identity,
                MaxHostElements,
                linkInstanceId: null,
                out bool hostTruncated)
            : [];

        if (needsHostBoxes && hostBoxes.Count == 0)
        {
            messages.Add("В активном виде текущей модели не найдено модельных элементов для проверки.");
        }

        if (needsHostBoxes && hostBoxes.Count >= MaxHostElements)
        {
            messages.Add($"Текущая модель ограничена первыми {MaxHostElements} видимыми элементами активного вида.");
        }

        IReadOnlyList<LinkedModelBoxes> links = normalized.ScanRvtLinks || normalized.ScanLinksAgainstEachOther
            ? CollectLinkBoxes(document, messages)
            : [];

        if (normalized.ScanCurrentModel)
        {
            ScanCurrentModel(document.Title, hostBoxes, clashes, messages, minimumOverlap);
            if (clashes.Count >= MaxClashes)
            {
                return new ClashLinkScanResult(clashes, messages);
            }
        }

        if (normalized.ScanRvtLinks)
        {
            ScanHostAgainstLinks(document.Title, hostBoxes, links, clashes, messages, minimumOverlap);
            if (clashes.Count >= MaxClashes)
            {
                return new ClashLinkScanResult(clashes, messages);
            }
        }

        if (normalized.ScanLinksAgainstEachOther)
        {
            ScanLinksAgainstLinks(links, clashes, messages, minimumOverlap);
        }

        if (clashes.Count == 0)
        {
            messages.Add("Пересечения по выбранным режимам проверки не найдены.");
        }

        return new ClashLinkScanResult(clashes, messages);
    }

    private static ClashScanOptions NormalizeOptions(ClashScanOptions options)
    {
        return new ClashScanOptions
        {
            ScanCurrentModel = options.ScanCurrentModel,
            ScanRvtLinks = options.ScanRvtLinks,
            ScanLinksAgainstEachOther = options.ScanLinksAgainstEachOther,
            MinimumOverlapMm = Clamp(options.MinimumOverlapMm, 0, 1000)
        };
    }

    private static IReadOnlyList<LinkedModelBoxes> CollectLinkBoxes(Document document, ICollection<string> messages)
    {
        IReadOnlyList<RevitLinkInstance> links = new FilteredElementCollector(document)
            .OfClass(typeof(RevitLinkInstance))
            .Cast<RevitLinkInstance>()
            .OrderBy(link => link.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (links.Count == 0)
        {
            messages.Add("В проекте не найдены загруженные RVT-связи.");
            return [];
        }

        List<LinkedModelBoxes> result = [];
        foreach (RevitLinkInstance link in links)
        {
            Document? linkDocument = link.GetLinkDocument();
            if (linkDocument is null)
            {
                messages.Add($"Связь '{link.Name}' не загружена или недоступна для чтения.");
                continue;
            }

            List<ModelElementBox> linkedBoxes = CollectModelBoxes(
                linkDocument.Title,
                new FilteredElementCollector(linkDocument).WhereElementIsNotElementType(),
                link.GetTotalTransform(),
                MaxLinkedElementsPerLink,
                RevitElementIds.GetValue(link.Id),
                out bool linkTruncated);
            if (linkTruncated)
            {
                messages.Add($"Связь '{linkDocument.Title}' ограничена первыми {MaxLinkedElementsPerLink} модельными элементами.");
            }

            if (linkedBoxes.Count == 0)
            {
                messages.Add($"Связь '{linkDocument.Title}' не содержит доступных модельных элементов.");
            }

            result.Add(new LinkedModelBoxes(link.Name, linkDocument.Title, linkedBoxes));
        }

        return result;
    }

    private static void ScanCurrentModel(
        string documentTitle,
        IReadOnlyList<ModelElementBox> hostBoxes,
        ICollection<ClashItem> clashes,
        ICollection<string> messages,
        double minimumOverlap)
    {
        int initialCount = clashes.Count;
        for (int firstIndex = 0; firstIndex < hostBoxes.Count; firstIndex++)
        {
            for (int secondIndex = firstIndex + 1; secondIndex < hostBoxes.Count; secondIndex++)
            {
                ModelElementBox first = hostBoxes[firstIndex];
                ModelElementBox second = hostBoxes[secondIndex];
                if (!CanReportIntersection(first, second, minimumOverlap))
                {
                    continue;
                }

                clashes.Add(CreateItem(
                    "Текущая модель",
                    $"SELF-{first.ElementId}-{second.ElementId}",
                    $"{first.CategoryName} x {second.CategoryName}",
                    first,
                    second,
                    $"Коллизия найдена внутри текущей модели '{documentTitle}'."));

                if (StopIfLimitReached(clashes, messages))
                {
                    return;
                }
            }
        }

        messages.Add($"Текущая модель: найдено {clashes.Count - initialCount} пересечений.");
    }

    private static void ScanHostAgainstLinks(
        string documentTitle,
        IReadOnlyList<ModelElementBox> hostBoxes,
        IReadOnlyList<LinkedModelBoxes> links,
        ICollection<ClashItem> clashes,
        ICollection<string> messages,
        double minimumOverlap)
    {
        int initialCount = clashes.Count;
        foreach (LinkedModelBoxes link in links)
        {
            foreach (ModelElementBox host in hostBoxes)
            {
                foreach (ModelElementBox linked in link.Boxes)
                {
                    if (!CanReportIntersection(host, linked, minimumOverlap))
                    {
                        continue;
                    }

                    clashes.Add(CreateItem(
                        "Текущая модель ↔ RVT-связь",
                        $"RVT-{linked.ElementId}-{host.ElementId}-{linked.LinkedElementId}",
                        $"{host.CategoryName} x {link.DocumentTitle}",
                        host,
                        linked,
                        $"Коллизия найдена между текущей моделью '{documentTitle}' и RVT-связью '{link.LinkName}'."));

                    if (StopIfLimitReached(clashes, messages))
                    {
                        return;
                    }
                }
            }
        }

        messages.Add($"Текущая модель ↔ RVT-связи: найдено {clashes.Count - initialCount} пересечений.");
    }

    private static void ScanLinksAgainstLinks(
        IReadOnlyList<LinkedModelBoxes> links,
        ICollection<ClashItem> clashes,
        ICollection<string> messages,
        double minimumOverlap)
    {
        int initialCount = clashes.Count;
        for (int firstLinkIndex = 0; firstLinkIndex < links.Count; firstLinkIndex++)
        {
            for (int secondLinkIndex = firstLinkIndex + 1; secondLinkIndex < links.Count; secondLinkIndex++)
            {
                LinkedModelBoxes firstLink = links[firstLinkIndex];
                LinkedModelBoxes secondLink = links[secondLinkIndex];
                foreach (ModelElementBox first in firstLink.Boxes)
                {
                    foreach (ModelElementBox second in secondLink.Boxes)
                    {
                        if (!CanReportIntersection(first, second, minimumOverlap))
                        {
                            continue;
                        }

                        clashes.Add(CreateItem(
                            "RVT-связь ↔ RVT-связь",
                            $"RVT-RVT-{first.ElementId}-{first.LinkedElementId}-{second.ElementId}-{second.LinkedElementId}",
                            $"{firstLink.DocumentTitle} x {secondLink.DocumentTitle}",
                            first,
                            second,
                            $"Коллизия найдена между RVT-связями '{firstLink.LinkName}' и '{secondLink.LinkName}'."));

                        if (StopIfLimitReached(clashes, messages))
                        {
                            return;
                        }
                    }
                }
            }
        }

        messages.Add($"RVT-связи между собой: найдено {clashes.Count - initialCount} пересечений.");
    }

    private static List<ModelElementBox> CollectModelBoxes(
        string sourceName,
        FilteredElementCollector collector,
        Transform transform,
        int limit,
        long? linkInstanceId,
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

            boxes.Add(ModelElementBox.Create(sourceName, element, transform, boundingBox, linkInstanceId));
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

    private static bool CanReportIntersection(ModelElementBox first, ModelElementBox second, double minimumOverlap)
    {
        return first.Intersects(second) && first.Intersection(second).HasMinimumSize(minimumOverlap);
    }

    private static ClashItem CreateItem(
        string source,
        string clashId,
        string name,
        ModelElementBox first,
        ModelElementBox second,
        string message)
    {
        ModelElementBox intersection = first.Intersection(second);
        XYZ center = intersection.Center;
        ClashItem item = new(
            clashId,
            name,
            first.ElementId,
            second.ElementId,
            center.X,
            center.Y,
            center.Z,
            ClashStatus.Open,
            string.Empty,
            first.SourceName,
            second.SourceName,
            second.LinkedElementId,
            first.LinkedElementId,
            source)
        {
            IsElement1Resolved = true,
            IsElement2Resolved = true,
            Element1Name = first.Label,
            Element2Name = second.Label,
            Message = message
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

    private static bool StopIfLimitReached(ICollection<ClashItem> clashes, ICollection<string> messages)
    {
        if (clashes.Count < MaxClashes)
        {
            return false;
        }

        messages.Add($"Найдено {MaxClashes} коллизий. Сканирование остановлено, чтобы не подвесить Revit.");
        return true;
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return minimum;
        }

        if (value < minimum)
        {
            return minimum;
        }

        return value > maximum ? maximum : value;
    }

    private sealed record LinkedModelBoxes(
        string LinkName,
        string DocumentTitle,
        IReadOnlyList<ModelElementBox> Boxes);

    private sealed record ModelElementBox(
        string SourceName,
        long ElementId,
        long? LinkedElementId,
        string CategoryName,
        string ElementName,
        double MinX,
        double MinY,
        double MinZ,
        double MaxX,
        double MaxY,
        double MaxZ)
    {
        public string Label => $"{SourceName}: {CategoryName}: {ElementName}";

        public XYZ Center => new((MinX + MaxX) * 0.5, (MinY + MaxY) * 0.5, (MinZ + MaxZ) * 0.5);

        public static ModelElementBox Create(
            string sourceName,
            Element element,
            Transform transform,
            BoundingBoxXYZ boundingBox,
            long? linkInstanceId)
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

            long elementId = RevitElementIds.GetValue(element.Id);
            return new ModelElementBox(
                sourceName,
                linkInstanceId ?? elementId,
                linkInstanceId.HasValue ? elementId : null,
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

        public bool HasMinimumSize(double minimum)
        {
            return MaxX - MinX >= minimum
                && MaxY - MinY >= minimum
                && MaxZ - MinZ >= minimum;
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
