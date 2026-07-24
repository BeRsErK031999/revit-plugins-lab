using TrueBIM.App.Modules.Lintels.Models;
using TrueBIM.App.Modules.Lintels.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class LintelArtifactNameBuilderTests
{
    [Fact]
    public void Build_CreatesStableNamesWithTypeIdentity()
    {
        LintelArtifactPreview first = LintelArtifactNameBuilder.Build(CreateType(101));
        LintelArtifactPreview second = LintelArtifactNameBuilder.Build(CreateType(102));

        Assert.Contains("101", first.AssemblyName, StringComparison.Ordinal);
        Assert.EndsWith("_Боковой_1-10", first.ViewName, StringComparison.Ordinal);
        Assert.EndsWith(".png", first.ImageFileName, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(first.AssemblyName, second.AssemblyName);
    }

    [Fact]
    public void Build_RemovesRevitAndFileNameSeparators()
    {
        LintelTypeDiagnostic type = CreateType(
            300,
            "Перемычки / Сборные",
            "ПР:<1>*?");

        LintelArtifactPreview preview = LintelArtifactNameBuilder.Build(type);

        Assert.DoesNotContain('/', preview.AssemblyName);
        Assert.DoesNotContain(':', preview.AssemblyName);
        Assert.DoesNotContain('<', preview.AssemblyName);
        Assert.DoesNotContain('*', preview.ImageFileName);
        Assert.Contains("ПР_1", preview.AssemblyName, StringComparison.CurrentCulture);
    }

    [Fact]
    public void Build_LimitsLongIdentityWithoutDroppingTypeId()
    {
        string longName = new('А', 180);

        LintelArtifactPreview preview = LintelArtifactNameBuilder.Build(CreateType(9001, longName, longName));

        Assert.True(preview.AssemblyName.Length <= 110);
        Assert.EndsWith("_9001", preview.AssemblyName, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("TB_Перемычка_Сварная_11", true)]
    [InlineData("tb_перемычка_Сварная_11", true)]
    [InlineData("Сторонняя сборка", false)]
    [InlineData(null, false)]
    public void IsTrueBimLintelArtifactName_RecognizesOnlyOwnedPrefix(string? value, bool expected)
    {
        Assert.Equal(expected, LintelArtifactNameBuilder.IsTrueBimLintelArtifactName(value));
    }

    [Theory]
    [InlineData("TB_Перемычка_Сварная_2305484", 2305484)]
    [InlineData("tb_перемычка_Наборная_17", 17)]
    public void TryExtractTypeId_ReadsDeterministicSuffix(string artifactName, long expectedTypeId)
    {
        Assert.True(LintelArtifactNameBuilder.TryExtractTypeId(artifactName, out long typeId));
        Assert.Equal(expectedTypeId, typeId);
    }

    [Theory]
    [InlineData("Сторонняя_2305484")]
    [InlineData("TB_Перемычка_Сварная")]
    [InlineData("TB_Перемычка_Сварная_-17")]
    [InlineData(null)]
    public void TryExtractTypeId_RejectsUnownedOrInvalidNames(string? artifactName)
    {
        Assert.False(LintelArtifactNameBuilder.TryExtractTypeId(artifactName, out long typeId));
        Assert.Equal(0, typeId);
    }

    private static LintelTypeDiagnostic CreateType(
        long typeId,
        string familyName = "Перемычки",
        string typeName = "ПР-1")
    {
        return new LintelTypeDiagnostic(
            typeId,
            familyName,
            typeName,
            2,
            2,
            501,
            false,
            3,
            3,
            [601, 602, 603],
            Array.Empty<string>());
    }
}
