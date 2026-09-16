using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishElementOwnershipResolverTests
{
    [Theory]
    [InlineData(FinishPreviewCategory.Walls, FinishQuantityMethod.RoomBoundarySubface)]
    [InlineData(FinishPreviewCategory.Floors, FinishQuantityMethod.FloorProbeIntersection)]
    [InlineData(FinishPreviewCategory.Ceilings, FinishQuantityMethod.CeilingProbeIntersection)]
    public void ContactSizeDoesNotClipFullElementArea(FinishPreviewCategory category, FinishQuantityMethod method)
    {
        FinishElementOwnershipResolver resolver = new();
        Dictionary<long, double> areas = new() { [10] = 172.01 };
        foreach (double contact in new[] { 76.31, 163.21 })
        {
            FinishQuantityResult result = resolver.Resolve([new FinishOccurrence(101, 10, category, contact, method)], areas);
            Assert.Equal(172.01, Assert.Single(result.Occurrences).AreaSquareMeters);
        }
    }

    [Fact]
    public void SmallNeighbourContactDoesNotDuplicateWholeElementOrOwnership()
    {
        FinishQuantityResult result = new FinishElementOwnershipResolver().Resolve(
        [
            new FinishOccurrence(105, 10, FinishPreviewCategory.Walls, 0.04, FinishQuantityMethod.WallProbeIntersection),
            new FinishOccurrence(106, 10, FinishPreviewCategory.Walls, 31.5, FinishQuantityMethod.RoomBoundarySubface)
        ], new Dictionary<long, double> { [10] = 34.96 });
        FinishOccurrence occurrence = Assert.Single(result.Occurrences);
        Assert.Equal(106, occurrence.RoomId);
        Assert.Equal(34.96, occurrence.AreaSquareMeters);
        Assert.Equal(FinishGeometryWarningCode.MultipleRoomContacts, Assert.Single(result.Warnings).Code);
    }

    [Fact]
    public void EqualContactsResolveDeterministicallyAndRemainVisibleInDiagnostics()
    {
        FinishOccurrence[] contacts =
        [
            new(130, 10, FinishPreviewCategory.Walls, 20, FinishQuantityMethod.RoomBoundarySubface),
            new(129, 10, FinishPreviewCategory.Walls, 20, FinishQuantityMethod.RoomBoundarySubface)
        ];
        FinishElementOwnershipResolver resolver = new();
        Dictionary<long, double> areas = new() { [10] = 42.56 };
        Assert.Equal(resolver.Resolve(contacts, areas).Occurrences, resolver.Resolve(contacts.Reverse(), areas).Occurrences);
        Assert.Single(resolver.Resolve(contacts, areas).Warnings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void MissingFullAreaIsReportedInsteadOfFallingBackToClippedContact(double area)
    {
        FinishQuantityResult result = new FinishElementOwnershipResolver().Resolve(
            [new FinishOccurrence(211, 10, FinishPreviewCategory.Ceilings, 15, FinishQuantityMethod.CeilingProbeIntersection)],
            new Dictionary<long, double> { [10] = area });
        Assert.Empty(result.Occurrences);
        Assert.True(FinishGeometryWarningClassifier.AffectsScheduleValue(Assert.Single(result.Warnings)));
    }
}
