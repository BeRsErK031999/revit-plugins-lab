using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.IsoFieldRebar.Revit;

public sealed class IsoFieldHostSelectionService
{
    private const string WallHostKind = "Wall";
    private const string SlabHostKind = "Slab";

    public IsoFieldHostElement PickHost(UIDocument uiDocument)
    {
        if (uiDocument is null)
        {
            throw new ArgumentNullException(nameof(uiDocument));
        }

        Document document = uiDocument.Document;
        Element? preselectedHost = ResolveSinglePreselectedHost(uiDocument, document);
        if (preselectedHost is not null)
        {
            return CreateHostElement(preselectedHost);
        }

        Reference reference = uiDocument.Selection.PickObject(
            ObjectType.Element,
            new HostSelectionFilter(),
            "Выберите стену или плиту для армирования по изополям.");

        Element selectedElement = document.GetElement(reference.ElementId)
            ?? throw new InvalidOperationException("Не удалось получить выбранный host-элемент.");

        return CreateHostElement(selectedElement);
    }

    public static bool IsSupportedHostCategory(long categoryId)
    {
        return categoryId == (long)BuiltInCategory.OST_Walls
            || categoryId == (long)BuiltInCategory.OST_Floors;
    }

    private static Element? ResolveSinglePreselectedHost(UIDocument uiDocument, Document document)
    {
        ICollection<ElementId> selectedIds = uiDocument.Selection.GetElementIds();
        if (selectedIds.Count != 1)
        {
            return null;
        }

        Element? selectedElement = document.GetElement(selectedIds.Single());
        return selectedElement is not null && IsSupportedHost(selectedElement)
            ? selectedElement
            : null;
    }

    private static IsoFieldHostElement CreateHostElement(Element element)
    {
        Category category = element.Category
            ?? throw new InvalidOperationException("У выбранного элемента нет категории.");

        long categoryId = RevitElementIds.GetValue(category.Id);
        if (!TryResolveHostKind(categoryId, out string hostKind, out string hostKindDisplayName))
        {
            throw new InvalidOperationException("Выбранный элемент не является стеной или плитой.");
        }

        long elementId = RevitElementIds.GetValue(element.Id);
        return new IsoFieldHostElement(
            elementId,
            hostKind,
            hostKindDisplayName,
            ResolveElementName(element, elementId));
    }

    private static bool IsSupportedHost(Element element)
    {
        if (element is ElementType || element.Category is null)
        {
            return false;
        }

        return IsSupportedHostCategory(RevitElementIds.GetValue(element.Category.Id));
    }

    private static bool TryResolveHostKind(
        long categoryId,
        out string hostKind,
        out string hostKindDisplayName)
    {
        if (categoryId == (long)BuiltInCategory.OST_Walls)
        {
            hostKind = WallHostKind;
            hostKindDisplayName = "Стена";
            return true;
        }

        if (categoryId == (long)BuiltInCategory.OST_Floors)
        {
            hostKind = SlabHostKind;
            hostKindDisplayName = "Плита";
            return true;
        }

        hostKind = string.Empty;
        hostKindDisplayName = string.Empty;
        return false;
    }

    private static string ResolveElementName(Element element, long elementId)
    {
        string? instanceName = element.Name;
        if (!string.IsNullOrWhiteSpace(instanceName))
        {
            return instanceName!;
        }

        ElementId typeId = element.GetTypeId();
        if (typeId != ElementId.InvalidElementId)
        {
            string? typeName = element.Document.GetElement(typeId)?.Name;
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                return typeName!;
            }
        }

        return $"Element {elementId}";
    }

    private sealed class HostSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element element)
        {
            return IsSupportedHost(element);
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return true;
        }
    }
}
