using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.ClashReport.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.ClashReport.Services;

public sealed class ClashElementResolver
{
    public void Resolve(Document document, ClashItem item)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(item, nameof(item));

        Element? element1 = GetElement(document, item.ElementId1);
        Element? element2 = GetElement(document, item.ElementId2);

        item.IsElement1Resolved = element1 is not null;
        item.IsElement2Resolved = element2 is not null;
        item.Element1Name = CreateElementLabel(element1);
        item.Element2Name = CreateElementLabel(element2);
        item.Message = CreateMessage(item);
    }

    private static Element? GetElement(Document document, long? elementId)
    {
        if (elementId is not > 0)
        {
            return null;
        }

        try
        {
            return document.GetElement(RevitElementIds.Create(elementId.Value));
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static string CreateElementLabel(Element? element)
    {
        if (element is null)
        {
            return string.Empty;
        }

        string category = element.Category?.Name ?? "Без категории";
        string name = GetElementName(element);
        return string.IsNullOrWhiteSpace(name)
            ? category
            : $"{category}: {name}";
    }

    private static string GetElementName(Element element)
    {
        try
        {
            return element.Name ?? string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static string CreateMessage(ClashItem item)
    {
        int expected = (item.ElementId1.HasValue ? 1 : 0) + (item.ElementId2.HasValue ? 1 : 0);
        int resolved = (item.IsElement1Resolved ? 1 : 0) + (item.IsElement2Resolved ? 1 : 0);
        if (expected == 0)
        {
            return item.HasPoint
                ? "ElementId не задан, доступна навигация по точке."
                : "Нет ElementId и координат.";
        }

        return resolved == expected
            ? "Элементы найдены."
            : $"Найдено элементов: {resolved}/{expected}.";
    }
}
