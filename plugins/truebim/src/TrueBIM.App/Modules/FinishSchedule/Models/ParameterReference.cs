namespace TrueBIM.App.Modules.FinishSchedule.Models;

public enum ParameterIdentityKind
{
    BuiltIn,
    Shared,
    Project
}

public enum ParameterBindingKind
{
    Instance,
    Type
}

public enum ParameterStorageKind
{
    None,
    String,
    Integer,
    Double,
    ElementId
}

public sealed record ParameterReference
{
    private ParameterReference(
        string name,
        ParameterIdentityKind identityKind,
        long? builtInParameterId,
        Guid? sharedParameterGuid,
        long? definitionElementId,
        ParameterBindingKind bindingKind,
        ParameterStorageKind storageKind)
    {
        Name = NormalizeName(name);
        IdentityKind = identityKind;
        BuiltInParameterId = builtInParameterId;
        SharedParameterGuid = sharedParameterGuid;
        DefinitionElementId = definitionElementId;
        BindingKind = bindingKind;
        StorageKind = storageKind;
        StableKey = BuildStableKey();
    }

    public string Name { get; }

    public ParameterIdentityKind IdentityKind { get; }

    public long? BuiltInParameterId { get; }

    public Guid? SharedParameterGuid { get; }

    public long? DefinitionElementId { get; }

    public ParameterBindingKind BindingKind { get; }

    public ParameterStorageKind StorageKind { get; }

    public string StableKey { get; }

    public static ParameterReference BuiltIn(
        string name,
        long builtInParameterId,
        ParameterBindingKind bindingKind,
        ParameterStorageKind storageKind,
        long? definitionElementId = null)
    {
        if (builtInParameterId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(builtInParameterId));
        }

        return new ParameterReference(
            name,
            ParameterIdentityKind.BuiltIn,
            builtInParameterId,
            null,
            definitionElementId,
            bindingKind,
            storageKind);
    }

    public static ParameterReference Shared(
        string name,
        Guid sharedParameterGuid,
        long definitionElementId,
        ParameterBindingKind bindingKind,
        ParameterStorageKind storageKind)
    {
        if (sharedParameterGuid == Guid.Empty)
        {
            throw new ArgumentException("Shared parameter GUID must not be empty.", nameof(sharedParameterGuid));
        }

        ValidateDefinitionElementId(definitionElementId);
        return new ParameterReference(
            name,
            ParameterIdentityKind.Shared,
            null,
            sharedParameterGuid,
            definitionElementId,
            bindingKind,
            storageKind);
    }

    public static ParameterReference Project(
        string name,
        long definitionElementId,
        ParameterBindingKind bindingKind,
        ParameterStorageKind storageKind)
    {
        ValidateDefinitionElementId(definitionElementId);
        return new ParameterReference(
            name,
            ParameterIdentityKind.Project,
            null,
            null,
            definitionElementId,
            bindingKind,
            storageKind);
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Parameter name must not be empty.", nameof(name));
        }

        return name.Trim();
    }

    private static void ValidateDefinitionElementId(long definitionElementId)
    {
        if (definitionElementId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(definitionElementId));
        }
    }

    private string BuildStableKey()
    {
        string identity = IdentityKind switch
        {
            ParameterIdentityKind.BuiltIn => BuiltInParameterId!.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ParameterIdentityKind.Shared => SharedParameterGuid!.Value.ToString("D"),
            ParameterIdentityKind.Project => DefinitionElementId!.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Unsupported parameter identity kind: {IdentityKind}.")
        };

        return $"{IdentityKind}:{identity}:{BindingKind}:{StorageKind}";
    }
}
