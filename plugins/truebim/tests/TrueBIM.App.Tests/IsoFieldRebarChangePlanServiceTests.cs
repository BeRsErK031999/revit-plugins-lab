using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldRebarChangePlanServiceTests
{
    private readonly IsoFieldRebarChangePlanService service = new();

    [Fact]
    public void Build_ClassifiesAddUpdateDeleteAndUnchanged()
    {
        IsoFieldRebarChangePlan plan = service.Build(
            [
                new IsoFieldRebarPlanItem("add", "sig-add"),
                new IsoFieldRebarPlanItem("update", "sig-new"),
                new IsoFieldRebarPlanItem("same", "sig-same")
            ],
            [
                new IsoFieldOwnedRebarSnapshot(10, "update", "sig-old"),
                new IsoFieldOwnedRebarSnapshot(11, "same", "sig-same"),
                new IsoFieldOwnedRebarSnapshot(12, "delete", "sig-delete")
            ]);

        Assert.True(plan.CanApply);
        Assert.True(plan.HasChanges);
        Assert.Equal(1, plan.AddCount);
        Assert.Equal(1, plan.UpdateCount);
        Assert.Equal(1, plan.DeleteCount);
        Assert.Equal(1, plan.UnchangedCount);
        Assert.Equal(2, plan.ExistingElementDeleteCount);
    }

    [Fact]
    public void Build_UpdatesLegacyOwnedElementWithoutSignature()
    {
        IsoFieldRebarChangePlan plan = service.Build(
            [new IsoFieldRebarPlanItem("stable-a", "sig-a")],
            [new IsoFieldOwnedRebarSnapshot(20, "stable-a", null)]);

        IsoFieldRebarChange change = Assert.Single(plan.Changes);
        Assert.Equal(IsoFieldRebarChangeKind.Update, change.Kind);
        Assert.Equal([20L], change.ExistingElementIds);
    }

    [Fact]
    public void Build_ReplacesDuplicateExistingStableIds()
    {
        IsoFieldRebarChangePlan plan = service.Build(
            [new IsoFieldRebarPlanItem("stable-a", "sig-a")],
            [
                new IsoFieldOwnedRebarSnapshot(20, "stable-a", "sig-a"),
                new IsoFieldOwnedRebarSnapshot(21, "stable-a", "sig-a")
            ]);

        IsoFieldRebarChange change = Assert.Single(plan.Changes);
        Assert.Equal(IsoFieldRebarChangeKind.Update, change.Kind);
        Assert.Equal([20L, 21L], change.ExistingElementIds);
        Assert.Equal(2, plan.ExistingElementDeleteCount);
    }

    [Fact]
    public void Build_BlocksDuplicatePlannedStableIds()
    {
        IsoFieldRebarChangePlan plan = service.Build(
            [
                new IsoFieldRebarPlanItem("stable-a", "sig-a"),
                new IsoFieldRebarPlanItem("stable-a", "sig-b")
            ],
            Array.Empty<IsoFieldOwnedRebarSnapshot>());

        Assert.False(plan.CanApply);
        Assert.False(plan.HasChanges);
        Assert.Contains(plan.Diagnostics, diagnostic =>
            diagnostic.Contains("повторяющийся", StringComparison.Ordinal));
    }

    [Fact]
    public void TryParseOwnedComment_RequiresModulePrefixAndStableId()
    {
        Assert.True(service.TryParseOwnedComment(
            42,
            "TrueBIM IsoFieldRebar; id=As1X:zone-a:c0:r0:b0; sig=abc123; host=7",
            out IsoFieldOwnedRebarSnapshot? snapshot));
        Assert.Equal(42, snapshot!.ElementId);
        Assert.Equal("As1X:zone-a:c0:r0:b0", snapshot.StableId);
        Assert.Equal("abc123", snapshot.Signature);

        Assert.False(service.TryParseOwnedComment(
            43,
            "TrueBIM IsoFieldRebar Test: Zone A",
            out _));
        Assert.False(service.TryParseOwnedComment(
            44,
            "Manual rebar; id=As1X:zone-a:c0:r0:b0; sig=abc123",
            out _));
    }

    [Fact]
    public void BuildSignature_IsStableAndChangesWithGeometry()
    {
        IsoFieldRebarPlacement placement = CreatePlacement(endXFeet: 2);

        string signature = service.BuildSignature(placement);

        Assert.Equal(signature, service.BuildSignature(placement));
        Assert.Equal(24, signature.Length);
        Assert.NotEqual(signature, service.BuildSignature(CreatePlacement(endXFeet: 2.1)));
    }

    private static IsoFieldRebarPlacement CreatePlacement(double endXFeet)
    {
        IsoFieldRebarComponent component = new(12, 200, 0, 1);
        RebarRule rule = new(
            "Rule A",
            "Slab",
            component.BarTypeName,
            component.SpacingMillimeters,
            PlacementDirection: "X",
            RequiredAreaSquareCentimetersPerMeter: component.AreaSquareCentimetersPerMeter,
            ProvidedAreaSquareCentimetersPerMeter: component.AreaSquareCentimetersPerMeter,
            ReinforcementLabel: component.DisplayName,
            LayerRole: IsoFieldLayerRole.As1X,
            Face: IsoFieldRebarFace.Bottom,
            Components: [component],
            ReinforcementMode: IsoFieldReinforcementMode.FullCombination);
        return new IsoFieldRebarPlacement(
            "As1X:zone-a",
            "Zone A",
            rule,
            new IsoFieldRebarPoint3D(0, 0, 0.1),
            new IsoFieldRebarPoint3D(endXFeet, 0, 0.1),
            new IsoFieldRebarPoint3D(0, 0, 1),
            component,
            "As1X:zone-a:c0:r0:b0");
    }
}
