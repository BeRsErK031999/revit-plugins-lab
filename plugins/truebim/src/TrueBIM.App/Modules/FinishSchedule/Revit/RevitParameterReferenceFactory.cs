using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

internal static class RevitParameterReferenceFactory
{
    public static ParameterReference? Create(
        Parameter parameter,
        ParameterBindingKind bindingKind)
    {
        if (parameter is null)
        {
            throw new ArgumentNullException(nameof(parameter));
        }

        Definition? definition = parameter.Definition;
        if (definition is null || parameter.Id == ElementId.InvalidElementId)
        {
            return null;
        }

        string name = definition.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        ParameterStorageKind storageKind = MapStorageKind(parameter.StorageType);
        long definitionElementId = RevitElementIds.GetValue(parameter.Id);

        if (definition is InternalDefinition internalDefinition
            && internalDefinition.BuiltInParameter != BuiltInParameter.INVALID)
        {
            return ParameterReference.BuiltIn(
                name,
                (long)internalDefinition.BuiltInParameter,
                bindingKind,
                storageKind,
                definitionElementId);
        }

        if (parameter.IsShared)
        {
            Guid guid = parameter.GUID;
            if (guid != Guid.Empty)
            {
                return ParameterReference.Shared(
                    name,
                    guid,
                    definitionElementId,
                    bindingKind,
                    storageKind);
            }
        }

        return ParameterReference.Project(
            name,
            definitionElementId,
            bindingKind,
            storageKind);
    }

    private static ParameterStorageKind MapStorageKind(StorageType storageType)
    {
        return storageType switch
        {
            StorageType.String => ParameterStorageKind.String,
            StorageType.Integer => ParameterStorageKind.Integer,
            StorageType.Double => ParameterStorageKind.Double,
            StorageType.ElementId => ParameterStorageKind.ElementId,
            StorageType.None => ParameterStorageKind.None,
            _ => ParameterStorageKind.None
        };
    }
}
