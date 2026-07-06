using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace TrueBIM.App.Modules.BimTools.CopyParameters.Services;

public sealed class ElementSelectionService
{
    public Element ResolveSourceElement(UIDocument uiDocument)
    {
        Document document = uiDocument.Document;
        ICollection<ElementId> selectedIds = uiDocument.Selection.GetElementIds();
        if (selectedIds.Count == 1)
        {
            Element? selectedElement = document.GetElement(selectedIds.Single());
            if (selectedElement is not null && selectedElement is not ElementType)
            {
                return selectedElement;
            }
        }

        Reference reference = uiDocument.Selection.PickObject(
            ObjectType.Element,
            new CopyParameterSelectionFilter(null),
            "Выберите исходный элемент для копирования параметров.");

        return document.GetElement(reference.ElementId)
            ?? throw new InvalidOperationException("Не удалось получить выбранный исходный элемент.");
    }

    public IReadOnlyList<Element> PickTargetElements(UIDocument uiDocument, Element sourceElement)
    {
        IList<Reference> references = uiDocument.Selection.PickObjects(
            ObjectType.Element,
            new CopyParameterSelectionFilter(sourceElement.Id),
            "Выберите элементы-получатели параметров.");

        return references
            .Select(reference => uiDocument.Document.GetElement(reference.ElementId))
            .Where(element => element is not null)
            .Cast<Element>()
            .Where(element => !element.Id.Equals(sourceElement.Id))
            .ToList();
    }

    private sealed class CopyParameterSelectionFilter : ISelectionFilter
    {
        private readonly ElementId? excludedElementId;

        public CopyParameterSelectionFilter(ElementId? excludedElementId)
        {
            this.excludedElementId = excludedElementId;
        }

        public bool AllowElement(Element element)
        {
            if (element is ElementType)
            {
                return false;
            }

            return excludedElementId is null || !element.Id.Equals(excludedElementId);
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return true;
        }
    }
}
