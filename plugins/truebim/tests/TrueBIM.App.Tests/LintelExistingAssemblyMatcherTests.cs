using TrueBIM.App.Modules.Lintels.Models;
using TrueBIM.App.Modules.Lintels.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class LintelExistingAssemblyMatcherTests
{
    [Fact]
    public void Apply_MarksOnlyTypesWithMatchingTrueBimAssembly()
    {
        LintelTypeDiagnostic existingType = CreateType(100, "ПР-1");
        LintelTypeDiagnostic newType = CreateType(200, "ПР-2");
        LintelDiagnosticResult source = new(
            LintelDiagnosticSource.ActiveView,
            [existingType, newType],
            [],
            []);
        string existingAssemblyName = LintelArtifactNameBuilder.Build(existingType).AssemblyName;

        LintelDiagnosticResult result = LintelExistingAssemblyMatcher.Apply(
            source,
            [existingAssemblyName.ToUpperInvariant(), "Другая сборка"]);

        Assert.Equal(existingAssemblyName, result.Types[0].ExistingAssemblyName);
        Assert.True(result.Types[0].HasExistingAssembly);
        Assert.Null(result.Types[1].ExistingAssemblyName);
        Assert.False(result.Types[1].HasExistingAssembly);
    }

    private static LintelTypeDiagnostic CreateType(long typeId, string typeName)
    {
        return new LintelTypeDiagnostic(
            typeId,
            "Перемычка сварная",
            typeName,
            1,
            1,
            10 + typeId,
            false,
            1,
            1,
            [20 + typeId],
            []);
    }
}
