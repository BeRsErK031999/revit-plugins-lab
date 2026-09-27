using TrueBIM.App.Modules.BimTools.FamilyReplacement.Models;
using TrueBIM.App.Modules.BimTools.FamilyReplacement.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FamilyReplacement;

public sealed class FamilyReplacementParameterPolicyTests
{
    private static readonly Guid SourceGuid = Guid.Parse("429c8e5b-e129-4f08-8645-c59fc8e57d21");
    private static readonly Guid OtherGuid = Guid.Parse("19af3dc0-708b-4867-bb32-dfe02eb679e8");

    [Fact]
    public void SharedParameter_MatchesGuidEvenWhenNameChanged()
    {
        FamilyReplacementParameterDefinition source = Text("Марка оборудования") with { SharedGuid = SourceGuid };
        FamilyReplacementParameterDefinition sameName = source with { SharedGuid = OtherGuid };
        FamilyReplacementParameterDefinition sameGuid = source with { Name = "Новое имя" };

        FamilyReplacementParameterMatch result = FamilyReplacementParameterPolicy.Match(source, [sameName, sameGuid]);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.TargetIndex);
    }

    [Fact]
    public void SharedParameter_DoesNotFallBackToSameNameWithDifferentGuidOrNonsharedParameter()
    {
        FamilyReplacementParameterDefinition source = Text("Марка оборудования") with { SharedGuid = SourceGuid };

        Assert.False(FamilyReplacementParameterPolicy.Match(source, [source with { SharedGuid = OtherGuid }]).Succeeded);
        Assert.False(FamilyReplacementParameterPolicy.Match(source, [source with { SharedGuid = null }]).Succeeded);
    }

    [Fact]
    public void BuiltInParameter_UsesIdAndRejectsSameNameCustomParameter()
    {
        FamilyReplacementParameterDefinition source = Text("Комментарии") with { BuiltInId = -1001 };
        FamilyReplacementParameterDefinition wrongId = source with { BuiltInId = -1002 };
        FamilyReplacementParameterDefinition sameId = source with { Name = "Comments" };

        Assert.Equal(1, FamilyReplacementParameterPolicy.Match(source, [wrongId, sameId]).TargetIndex);
        Assert.False(FamilyReplacementParameterPolicy.Match(source, [Text("Комментарии")]).Succeeded);
    }

    [Fact]
    public void ProjectParameter_UsesExistingDefinitionBeforeNameFallback()
    {
        FamilyReplacementParameterDefinition source = Text("Код") with { ProjectDefinitionId = 123 };
        FamilyReplacementParameterDefinition sameName = source with { ProjectDefinitionId = 456 };
        FamilyReplacementParameterDefinition sameDefinition = source with { Name = "Иное имя" };

        Assert.Equal(1, FamilyReplacementParameterPolicy.Match(source, [sameName, sameDefinition]).TargetIndex);
    }

    [Fact]
    public void ProjectParameter_CanFallBackToUnambiguousCompatibleLocalName()
    {
        FamilyReplacementParameterDefinition source = Text("Код") with { ProjectDefinitionId = 123 };

        Assert.Equal(0, FamilyReplacementParameterPolicy.Match(source, [Text("Код")]).TargetIndex);
    }

    [Fact]
    public void FamilyParameter_RejectsAmbiguousNames()
    {
        FamilyReplacementParameterDefinition source = Text("Код");

        FamilyReplacementParameterMatch result = FamilyReplacementParameterPolicy.Match(source, [source, source]);

        Assert.False(result.Succeeded);
        Assert.Contains("несколько", result.Reason);
    }

    [Fact]
    public void FamilyParameter_UsesExactNameAndDoesNotClaimSharedOrBuiltInValues()
    {
        FamilyReplacementParameterDefinition source = Text("Код");

        Assert.False(FamilyReplacementParameterPolicy.Match(source, [Text("код")]).Succeeded);
        Assert.False(FamilyReplacementParameterPolicy.Match(source, [source with { SharedGuid = SourceGuid }]).Succeeded);
        Assert.False(FamilyReplacementParameterPolicy.Match(source, [source with { BuiltInId = -1001 }]).Succeeded);
    }

    [Fact]
    public void SameStorageWithDifferentPhysicalDimension_IsRejected()
    {
        FamilyReplacementParameterDefinition source = new(
            "Размер", SourceGuid, null, null, FamilyReplacementParameterStorage.Double, "autodesk.spec.aec:length-2.0.0");
        FamilyReplacementParameterDefinition area = source with { DataType = "autodesk.spec.aec:area-2.0.0" };

        Assert.False(FamilyReplacementParameterPolicy.Match(source, [area]).Succeeded);
        Assert.False(FamilyReplacementParameterPolicy.Match(source with { SharedGuid = null }, [area with { SharedGuid = null }]).Succeeded);
    }

    [Fact]
    public void SameGuidWithWrongStorage_DoesNotFallBackToCompatibleName()
    {
        FamilyReplacementParameterDefinition source = Text("Код") with { SharedGuid = SourceGuid };
        FamilyReplacementParameterDefinition sameGuidWrongStorage = source with { Storage = FamilyReplacementParameterStorage.Integer };
        FamilyReplacementParameterDefinition sameNameRightStorage = source with { SharedGuid = OtherGuid };

        Assert.False(FamilyReplacementParameterPolicy.Match(source, [sameGuidWrongStorage, sameNameRightStorage]).Succeeded);
    }

    [Fact]
    public void FamilyParameter_NameFallbackRequiresKnownDataType()
    {
        FamilyReplacementParameterDefinition source = Text("Код") with { DataType = string.Empty };

        Assert.False(FamilyReplacementParameterPolicy.Match(source, [source]).Succeeded);
    }

    [Fact]
    public void NameFallback_CanChooseOnlyCandidateWithExactSemanticType()
    {
        FamilyReplacementParameterDefinition source = Text("Код");
        FamilyReplacementParameterDefinition otherType = source with { DataType = "autodesk.spec:spec.string.url-2.0.0" };

        Assert.Equal(1, FamilyReplacementParameterPolicy.Match(source, [otherType, source]).TargetIndex);
    }

    [Fact]
    public void ZeroAndFalse_AreMeaningfulAndCannotBeSilentlyDropped()
    {
        Assert.True(new FamilyReplacementParameterValue(FamilyReplacementParameterStorage.Integer, Integer: 0).IsMeaningful);
        Assert.True(new FamilyReplacementParameterValue(FamilyReplacementParameterStorage.Double, Number: 0).IsMeaningful);
        Assert.False(new FamilyReplacementParameterValue(FamilyReplacementParameterStorage.String).IsMeaningful);
        Assert.True(new FamilyReplacementParameterValue(FamilyReplacementParameterStorage.String, Text: " ").IsMeaningful);
        Assert.False(new FamilyReplacementParameterValue(FamilyReplacementParameterStorage.ElementId, Integer: -1).IsMeaningful);
        Assert.True(new FamilyReplacementParameterValue(FamilyReplacementParameterStorage.ElementId, Integer: -2).IsMeaningful);
    }

    [Fact]
    public void Verification_PreservesTextExactlyAndDetectsChangedValueOrStorage()
    {
        FamilyReplacementParameterValue source = new(FamilyReplacementParameterStorage.String, Text: "ПС-01 ");

        Assert.True(FamilyReplacementParameterPolicy.ValuesEqual(source, source with { }));
        Assert.False(FamilyReplacementParameterPolicy.ValuesEqual(source, source with { Text = "ПС-01" }));
        Assert.False(FamilyReplacementParameterPolicy.ValuesEqual(source, source with { Storage = FamilyReplacementParameterStorage.Integer }));
    }

    [Fact]
    public void NumericVerification_ToleratesRoundoffAndRejectsChangedValues()
    {
        FamilyReplacementParameterValue source = new(FamilyReplacementParameterStorage.Double, Number: 3.28);

        Assert.True(FamilyReplacementParameterPolicy.ValuesEqual(source, source with { Number = 3.28 + 1e-11 }));
        Assert.False(FamilyReplacementParameterPolicy.ValuesEqual(source, source with { Number = 3.281 }));
    }

    private static FamilyReplacementParameterDefinition Text(string name)
    {
        return new FamilyReplacementParameterDefinition(
            name, null, null, null, FamilyReplacementParameterStorage.String, "autodesk.spec:spec.string-2.0.0");
    }
}
