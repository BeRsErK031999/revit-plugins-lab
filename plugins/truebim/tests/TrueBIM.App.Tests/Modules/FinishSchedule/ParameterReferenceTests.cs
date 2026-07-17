using TrueBIM.App.Modules.FinishSchedule.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class ParameterReferenceTests
{
    [Fact]
    public void SharedIdentity_DoesNotDependOnNameOrDefinitionElementId()
    {
        Guid guid = Guid.Parse("20bf7bb7-6304-448d-8e2d-6658f39b1c0d");
        ParameterReference first = ParameterReference.Shared(
            "Описание",
            guid,
            100,
            ParameterBindingKind.Type,
            ParameterStorageKind.String);
        ParameterReference renamed = ParameterReference.Shared(
            "Description",
            guid,
            200,
            ParameterBindingKind.Type,
            ParameterStorageKind.String);

        Assert.Equal(first.StableKey, renamed.StableKey);
    }

    [Fact]
    public void BuiltInIdentity_DoesNotDependOnLocalizedName()
    {
        ParameterReference first = ParameterReference.BuiltIn(
            "Номер",
            -1001203,
            ParameterBindingKind.Instance,
            ParameterStorageKind.String);
        ParameterReference renamed = ParameterReference.BuiltIn(
            "Number",
            -1001203,
            ParameterBindingKind.Instance,
            ParameterStorageKind.String);

        Assert.Equal(first.StableKey, renamed.StableKey);
    }

    [Fact]
    public void ProjectIdentity_UsesDefinitionElementIdBindingAndStorage()
    {
        ParameterReference first = ParameterReference.Project(
            "Параметр",
            101,
            ParameterBindingKind.Instance,
            ParameterStorageKind.String);
        ParameterReference anotherDefinition = ParameterReference.Project(
            "Параметр",
            102,
            ParameterBindingKind.Instance,
            ParameterStorageKind.String);
        ParameterReference typeParameter = ParameterReference.Project(
            "Параметр",
            101,
            ParameterBindingKind.Type,
            ParameterStorageKind.String);

        Assert.NotEqual(first.StableKey, anotherDefinition.StableKey);
        Assert.NotEqual(first.StableKey, typeParameter.StableKey);
    }
}
