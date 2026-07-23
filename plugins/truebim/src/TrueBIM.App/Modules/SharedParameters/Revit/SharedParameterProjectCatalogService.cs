using Autodesk.Revit.DB;
using TrueBIM.App.Modules.SharedParameters.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.SharedParameters.Revit;

public sealed class SharedParameterProjectCatalogService
{
    private readonly ISharedParameterVersionAdapter versionAdapter;

    public SharedParameterProjectCatalogService(ISharedParameterVersionAdapter versionAdapter)
    {
        this.versionAdapter = versionAdapter ?? throw new ArgumentNullException(nameof(versionAdapter));
    }

    public DocumentIdentity GetDocumentIdentity(Document document)
    {
        Guard.NotNull(document, nameof(document));
        return new DocumentIdentity(
            document.Title,
            document.PathName ?? string.Empty,
            document.Application.VersionNumber,
            document.IsFamilyDocument,
            document.IsWorkshared);
    }

    public IReadOnlyList<SharedParameterDescriptor> Collect(Document document)
    {
        Guard.NotNull(document, nameof(document));
        if (document.IsFamilyDocument)
        {
            return [];
        }

        IReadOnlyList<BindingDescriptor> bindings = CollectBindings(document);
        return new FilteredElementCollector(document)
            .OfClass(typeof(SharedParameterElement))
            .Cast<SharedParameterElement>()
            .Select(parameter => CreateDescriptor(parameter, bindings))
            .OrderBy(parameter => parameter.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(parameter => parameter.Guid)
            .ToList();
    }

    public SharedParameterDescriptor? Find(Document document, Guid guid)
    {
        Guard.NotNull(document, nameof(document));
        SharedParameterElement? parameter = SharedParameterElement.Lookup(document, guid);
        if (parameter is null)
        {
            return null;
        }

        return CreateDescriptor(parameter, CollectBindings(document));
    }

    private SharedParameterDescriptor CreateDescriptor(
        SharedParameterElement parameter,
        IReadOnlyList<BindingDescriptor> bindings)
    {
        Definition definition = parameter.GetDefinition();
        long parameterId = RevitElementIds.GetValue(parameter.Id);
        BindingDescriptor? binding = bindings.FirstOrDefault(candidate =>
            candidate.DefinitionElementId == parameterId
            || candidate.Guid == parameter.GuidValue);

        return new SharedParameterDescriptor(
            parameterId,
            parameter.GuidValue,
            definition.Name,
            versionAdapter.GetDataTypeName(definition),
            versionAdapter.GetParameterGroupName(definition),
            binding?.BindingKind ?? SharedParameterBindingKind.None,
            binding?.Categories ?? [],
            binding is not null,
            definition is InternalDefinition internalDefinition && internalDefinition.VariesAcrossGroups);
    }

    private static IReadOnlyList<BindingDescriptor> CollectBindings(Document document)
    {
        List<BindingDescriptor> bindings = [];
        DefinitionBindingMapIterator iterator = document.ParameterBindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            Definition definition = iterator.Key;
            Binding binding = (Binding)iterator.Current;
            long? definitionId = definition is InternalDefinition internalDefinition
                ? RevitElementIds.GetValue(internalDefinition.Id)
                : null;
            Guid? guid = definition is ExternalDefinition externalDefinition
                ? externalDefinition.GUID
                : null;
            IReadOnlyList<CategoryDescriptor> categories = binding is ElementBinding elementBinding
                ? elementBinding.Categories
                    .Cast<Category>()
                    .Where(category => category is not null)
                    .Select(category => new CategoryDescriptor(
                        RevitElementIds.GetValue(category.Id),
                        category.Name))
                    .OrderBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()
                : [];
            bindings.Add(new BindingDescriptor(
                definitionId,
                guid,
                binding is TypeBinding ? SharedParameterBindingKind.Type : SharedParameterBindingKind.Instance,
                categories));
        }

        return bindings;
    }

    private sealed record BindingDescriptor(
        long? DefinitionElementId,
        Guid? Guid,
        SharedParameterBindingKind BindingKind,
        IReadOnlyList<CategoryDescriptor> Categories);
}
