using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishElementOwnershipResolverTests
{
    [Fact]
    public void VerifiedContourOwnerWinsSmallContactDifferenceAndKeepsNativeArea()
    {
        FinishQuantityResult result = new FinishElementOwnershipResolver().Resolve(
        [
            new(210, 10, FinishPreviewCategory.Walls, 1.134, FinishQuantityMethod.ProjectedRoomFootprint),
            new(211, 10, FinishPreviewCategory.Walls, 1.026, FinishQuantityMethod.ProjectedRoomFootprint)
        ], new Dictionary<long, double> { [10] = 1.134 }, new Dictionary<long, long> { [10] = 211 });
        FinishOccurrence owner = Assert.Single(result.Occurrences);
        Assert.Equal(211, owner.RoomId);
        Assert.Equal(1.134, owner.AreaSquareMeters);
        Assert.Equal(FinishQuantityMethod.ConnectedWallContour, owner.Method);
        Assert.Contains("единого контура", Assert.Single(result.Warnings).Message);
    }

    [Fact]
    public void ContourCannotAssignAnElementToARoomWithoutContact()
    {
        FinishQuantityResult result = new FinishElementOwnershipResolver().Resolve(
            [new(211, 10, FinishPreviewCategory.Walls, 2, FinishQuantityMethod.ProjectedRoomFootprint)],
            new Dictionary<long, double> { [10] = 3 }, new Dictionary<long, long> { [10] = 999 });
        Assert.Equal(211, Assert.Single(result.Occurrences).RoomId);
    }
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

    [Theory]
    [InlineData(0.08, 0.80, true)]
    [InlineData(0.08, 0.79, false)]
    [InlineData(0.10, 1.50, false)]
    public void SmallCompetingWallContactTakesPrecedenceOnlyInsideBothLimits(double small, double large, bool automatic)
    {
        var contacts = new[] {
            new FinishOccurrence(10, 100, FinishPreviewCategory.Walls, small, FinishQuantityMethod.WallProbeIntersection),
            new FinishOccurrence(20, 100, FinishPreviewCategory.Walls, large, FinishQuantityMethod.WallProbeIntersection)
        };
        var result = new FinishElementOwnershipResolver().Resolve(contacts,
            new Dictionary<long, double> { [100] = 2 }, new Dictionary<long, long> { [100] = 10 });
        Assert.Equal(automatic ? 20 : 10, Assert.Single(result.Occurrences).RoomId);
        Assert.Equal(2, Assert.Single(result.Occurrences).AreaSquareMeters);
        Assert.Equal(automatic, Assert.Single(result.Warnings).SmallWallContactResolved);
    }

    [Fact]
    public void SingleSmallWallRetainsItsFullSystemArea()
    {
        var contact = new FinishOccurrence(10, 100, FinishPreviewCategory.Walls, 0.05, FinishQuantityMethod.WallProbeIntersection);
        var result = new FinishElementOwnershipResolver().Resolve([contact], new Dictionary<long, double> { [100] = 0.066 });
        Assert.Equal(0.066, Assert.Single(result.Occurrences).AreaSquareMeters);
        Assert.Empty(result.Warnings);
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
