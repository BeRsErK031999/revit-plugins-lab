using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.SelectionBox;

public static class SelectionBoxBounds
{
    public const double PaddingFeet = 300.0 / 304.8;

    public static IEnumerable<XYZ> Corners(BoundingBoxXYZ box, Transform transform)
    {
        Transform combined = transform.Multiply(box.Transform);
        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 2; z++)
            yield return combined.OfPoint(new XYZ(
                x == 0 ? box.Min.X : box.Max.X,
                y == 0 ? box.Min.Y : box.Max.Y,
                z == 0 ? box.Min.Z : box.Max.Z));
    }

    public static BoundingBoxXYZ Enclose(IEnumerable<XYZ> worldPoints, Transform boxTransform)
    {
        Transform inverse = boxTransform.Inverse;
        XYZ[] points = worldPoints.Select(inverse.OfPoint).ToArray();
        if (points.Length == 0)
            throw new InvalidOperationException("Сначала выберите элементы модели, затем нажмите «Рамка выбора».");

        return new BoundingBoxXYZ
        {
            Transform = boxTransform,
            Min = new XYZ(points.Min(p => p.X) - PaddingFeet, points.Min(p => p.Y) - PaddingFeet, points.Min(p => p.Z) - PaddingFeet),
            Max = new XYZ(points.Max(p => p.X) + PaddingFeet, points.Max(p => p.Y) + PaddingFeet, points.Max(p => p.Z) + PaddingFeet)
        };
    }

    public static IReadOnlyList<XYZ> Resolve(Document document, IEnumerable<Reference> references)
    {
        List<XYZ> points = [];
        foreach (Reference reference in references)
        {
            Element? element = document.GetElement(reference.ElementId);
            Transform transform = Transform.Identity;
            if (reference.LinkedElementId != ElementId.InvalidElementId)
            {
                if (element is not RevitLinkInstance link || link.GetLinkDocument() is not Document linkedDocument)
                    throw new InvalidOperationException("Выбранная связь не загружена. Загрузите её и повторите выбор.");
                transform = link.GetTotalTransform();
                element = linkedDocument.GetElement(reference.LinkedElementId);
                if (element is RevitLinkInstance)
                    throw new InvalidOperationException("Выбран элемент вложенной связи. В этой версии поддерживаются элементы непосредственно подключённых RVT-файлов.");
            }

            if (element is null || element.ViewSpecific || element.Category?.CategoryType != CategoryType.Model
                || element.get_BoundingBox(null) is not BoundingBoxXYZ bounds)
                throw new InvalidOperationException("У одного из выбранных объектов нет габаритов 3D-модели. Уберите из выбора аннотации и объекты без геометрии и повторите команду.");

            points.AddRange(Corners(bounds, transform));
        }
        if (points.Count == 0)
            throw new InvalidOperationException("Сначала выберите один или несколько элементов модели, затем нажмите «Рамка выбора». Для выбора элемента внутри связи используйте Tab.");
        return points;
    }
}
