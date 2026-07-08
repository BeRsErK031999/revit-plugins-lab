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

        ResolveEndpoint(
            document,
            item.ElementId1,
            item.LinkedElementId1,
            item.Element1SourceName,
            out bool isElement1Resolved,
            out string element1Name);
        ResolveEndpoint(
            document,
            item.ElementId2,
            item.LinkedElementId2,
            item.Element2SourceName,
            out bool isElement2Resolved,
            out string element2Name);

        item.IsElement1Resolved = isElement1Resolved;
        item.IsElement2Resolved = isElement2Resolved;
        item.Element1Name = element1Name;
        item.Element2Name = element2Name;
        item.Message = CreateMessage(item);
    }

    private static void ResolveEndpoint(
        Document document,
        long? elementId,
        long? linkedElementId,
        string sourceName,
        out bool isResolved,
        out string label)
    {
        if (!linkedElementId.HasValue)
        {
            Element? element = GetElement(document, elementId);
            isResolved = element is not null;
            label = PrefixSource(sourceName, CreateElementLabel(element));
            return;
        }

        RevitLinkInstance? linkInstance = GetElement(document, elementId) as RevitLinkInstance;
        Element? linkedElement = null;
        Document? linkedDocument = null;

        if (linkInstance is not null)
        {
            linkedDocument = linkInstance.GetLinkDocument();
            linkedElement = linkedDocument?.GetElement(RevitElementIds.Create(linkedElementId.Value));
        }

        isResolved = linkedElement is not null;
        label = PrefixSource(
            string.IsNullOrWhiteSpace(sourceName) ? linkedDocument?.Title ?? linkInstance?.Name ?? string.Empty : sourceName,
            CreateElementLabel(linkedElement));
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

        if (resolved == expected)
        {
            return item.IsLinkDriven
                ? "Элементы найдены. Нажмите «Выбрать» для выбора доступных элементов/экземпляров связей или «Показать в 3D»."
                : "Элементы найдены. Нажмите «Выбрать» или дважды щёлкните строку.";
        }

        return item.IsLinkDriven
            ? $"Найдено элементов: {resolved}/{expected}. Проверьте, что RVT-связи загружены и не были обновлены."
            : $"Найдено элементов: {resolved}/{expected}.";
    }

    private static string PrefixSource(string source, string label)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return label;
        }

        return string.IsNullOrWhiteSpace(label)
            ? source
            : $"{source}: {label}";
    }
}
