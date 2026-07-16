using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.Lintels.Models;
using TrueBIM.App.Modules.Lintels.Services;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.Lintels.Revit;

public sealed class LintelDiagnosticCollectorService
{
    private readonly ITrueBimLogger logger;
    private readonly LintelDiagnosticReportBuilder reportBuilder = new();

    public LintelDiagnosticCollectorService(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public LintelDiagnosticResult Collect(UIDocument uiDocument)
    {
        if (uiDocument is null)
        {
            throw new ArgumentNullException(nameof(uiDocument));
        }

        ICollection<ElementId> selectedIds = uiDocument.Selection.GetElementIds();
        LintelWizardSourceMode sourceMode = selectedIds.Count > 0
            ? LintelWizardSourceMode.CurrentSelection
            : LintelWizardSourceMode.ActiveView;
        return Collect(uiDocument, sourceMode);
    }

    public LintelDiagnosticResult Collect(UIDocument uiDocument, LintelWizardSourceMode sourceMode)
    {
        if (uiDocument is null)
        {
            throw new ArgumentNullException(nameof(uiDocument));
        }

        if (sourceMode is not (LintelWizardSourceMode.CurrentSelection or LintelWizardSourceMode.ActiveView))
        {
            throw new NotSupportedException($"Источник «{LintelWizardSourceCatalog.GetTitle(sourceMode)}» пока недоступен.");
        }

        Document document = uiDocument.Document;
        ICollection<ElementId> selectedIds = uiDocument.Selection.GetElementIds();
        LintelDiagnosticSource source = sourceMode == LintelWizardSourceMode.CurrentSelection
            ? LintelDiagnosticSource.Selection
            : LintelDiagnosticSource.ActiveView;
        IReadOnlyList<Element> sourceElements = source == LintelDiagnosticSource.Selection
            ? ResolveSelectedElements(document, selectedIds)
            : ResolveVisibleFamilyInstances(document, uiDocument.ActiveView);

        List<LintelInstanceSnapshot> instances = [];
        List<LintelExcludedElement> excluded = [];
        List<string> diagnostics = [];

        if (source == LintelDiagnosticSource.ActiveView)
        {
            diagnostics.Add("Автопоиск использует временное правило: имя семейства, типоразмера или экземпляра содержит «перемыч» либо «lintel».");
        }

        foreach (Element element in sourceElements)
        {
            if (element is not FamilyInstance familyInstance)
            {
                excluded.Add(new LintelExcludedElement(
                    RevitElementIds.GetValue(element.Id),
                    GetElementName(element),
                    "элемент не является экземпляром семейства"));
                continue;
            }

            string familyName = GetFamilyName(familyInstance);
            string typeName = GetTypeName(familyInstance);
            if (source == LintelDiagnosticSource.ActiveView
                && !LintelCandidateMatcher.IsMatch(familyName, typeName, familyInstance.Name))
            {
                excluded.Add(new LintelExcludedElement(
                    RevitElementIds.GetValue(familyInstance.Id),
                    $"{familyName} : {typeName}",
                    "имя не соответствует временному правилу автопоиска"));
                continue;
            }

            try
            {
                instances.Add(CreateSnapshot(document, familyInstance, familyName, typeName));
            }
            catch (Exception exception)
            {
                long elementId = RevitElementIds.GetValue(familyInstance.Id);
                logger.Error($"Failed to inspect lintel candidate ID {elementId}.", exception);
                excluded.Add(new LintelExcludedElement(
                    elementId,
                    $"{familyName} : {typeName}",
                    "ошибка чтения вложенных компонентов; подробности записаны в лог"));
            }
        }

        if (instances.Count == 0)
        {
            diagnostics.Add(source == LintelDiagnosticSource.Selection
                ? "В выделении нет подходящих экземпляров семейств. Выберите родительские перемычки и повторите запуск."
                : "На активном виде перемычки не найдены. Выберите нужные экземпляры явно либо уточните правило именования по рабочему RVT-файлу.");
        }

        LintelDiagnosticResult result = LintelExistingAssemblyMatcher.Apply(
            reportBuilder.Build(source, instances, excluded, diagnostics),
            CollectExistingAssemblyNames(document));
        logger.Info($"Lintels diagnostic completed. Source={source}; Instances={result.InstanceCount}; Types={result.Types.Count}; ReadyTypes={result.ReadyTypeCount}; Excluded={result.ExcludedElements.Count}.");
        return result;
    }

    private static IReadOnlyList<Element> ResolveSelectedElements(
        Document document,
        IEnumerable<ElementId> selectedIds)
    {
        return selectedIds
            .Select(document.GetElement)
            .Where(element => element is not null)
            .Cast<Element>()
            .ToArray();
    }

    private static IReadOnlyList<Element> ResolveVisibleFamilyInstances(Document document, View activeView)
    {
        if (activeView.IsTemplate)
        {
            return Array.Empty<Element>();
        }

        return new FilteredElementCollector(document, activeView.Id)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .ToElements()
            .ToArray();
    }

    private static IEnumerable<string> CollectExistingAssemblyNames(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(AssemblyInstance))
            .Cast<AssemblyInstance>()
            .Select(assembly => assembly.AssemblyTypeName)
            .Where(name => !string.IsNullOrWhiteSpace(name));
    }

    private static LintelInstanceSnapshot CreateSnapshot(
        Document document,
        FamilyInstance familyInstance,
        string familyName,
        string typeName)
    {
        long elementId = RevitElementIds.GetValue(familyInstance.Id);
        long typeId = RevitElementIds.GetValue(familyInstance.GetTypeId());
        if (typeId <= 0)
        {
            typeId = elementId;
        }

        IReadOnlyList<LintelNestedComponentSnapshot> nestedComponents = CollectNestedComponents(
            document,
            familyInstance);
        return new LintelInstanceSnapshot(
            elementId,
            typeId,
            familyName,
            typeName,
            HasModelGeometry(familyInstance),
            nestedComponents);
    }

    private static IReadOnlyList<LintelNestedComponentSnapshot> CollectNestedComponents(
        Document document,
        FamilyInstance parent)
    {
        List<LintelNestedComponentSnapshot> components = [];
        Queue<ElementId> pending = new(parent.GetSubComponentIds());
        HashSet<long> visited = [RevitElementIds.GetValue(parent.Id)];

        while (pending.Count > 0)
        {
            ElementId componentId = pending.Dequeue();
            long componentIdValue = RevitElementIds.GetValue(componentId);
            if (!visited.Add(componentIdValue) || document.GetElement(componentId) is not Element component)
            {
                continue;
            }

            components.Add(new LintelNestedComponentSnapshot(
                componentIdValue,
                component.Category?.Name ?? "Без категории",
                component is FamilyInstance nestedFamilyInstance
                    ? GetFamilyName(nestedFamilyInstance)
                    : component.GetType().Name,
                component is FamilyInstance typedFamilyInstance
                    ? GetTypeName(typedFamilyInstance)
                    : GetElementName(component),
                HasModelGeometry(component)));

            if (component is FamilyInstance nested)
            {
                foreach (ElementId nestedId in nested.GetSubComponentIds())
                {
                    pending.Enqueue(nestedId);
                }
            }
        }

        return components;
    }

    private static bool HasModelGeometry(Element element)
    {
        Options options = new()
        {
            ComputeReferences = false,
            IncludeNonVisibleObjects = false,
            DetailLevel = ViewDetailLevel.Fine
        };
        GeometryElement? geometry = element.get_Geometry(options);
        return geometry is not null && ContainsModelGeometry(geometry);
    }

    private static bool ContainsModelGeometry(GeometryElement geometry)
    {
        foreach (GeometryObject geometryObject in geometry)
        {
            switch (geometryObject)
            {
                case Solid solid when solid.Faces.Size > 0 && solid.Edges.Size > 0:
                case Mesh mesh when mesh.NumTriangles > 0:
                    return true;
                case GeometryInstance instance when ContainsModelGeometry(instance.GetSymbolGeometry()):
                    return true;
            }
        }

        return false;
    }

    private static string GetFamilyName(FamilyInstance familyInstance)
    {
        return familyInstance.Symbol?.Family?.Name
            ?? "Семейство не определено";
    }

    private static string GetTypeName(FamilyInstance familyInstance)
    {
        return familyInstance.Symbol?.Name
            ?? GetElementName(familyInstance);
    }

    private static string GetElementName(Element element)
    {
        return string.IsNullOrWhiteSpace(element.Name)
            ? $"Element {RevitElementIds.GetValue(element.Id)}"
            : element.Name;
    }
}
