using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldHostSupportServiceTests
{
    private readonly IsoFieldHostSupportService service = new();

    [Fact]
    public void Analyze_StraightBasicWall_ReturnsEngineeringCapability()
    {
        IsoFieldHostElement host = new(
            10,
            "Wall",
            "Стена",
            "Basic Wall",
            CreateWallGeometry(),
            GeometryProfile: IsoFieldHostGeometryProfile.StraightBasicWall);

        IsoFieldHostSupportResult result = service.Analyze(host);

        Assert.Equal(IsoFieldHostSupportMode.Engineering, result.Mode);
        Assert.True(result.CanCalculateRules);
        Assert.True(result.CanApplyRebar);
        Assert.True(result.RequiresPlanarBinding);
        Assert.Equal("WALL_STRAIGHT_BASIC_ENGINEERING", result.Code);
    }

    [Fact]
    public void Analyze_StraightWallWithoutResolvedPlane_BlocksWorkflow()
    {
        IsoFieldHostElement host = new(
            16,
            "Wall",
            "Стена",
            "Basic Wall",
            GeometryProfile: IsoFieldHostGeometryProfile.StraightBasicWall);

        IsoFieldHostSupportResult result = service.Analyze(host);

        Assert.False(result.IsSupported);
        Assert.Equal("WALL_PLANE_UNRESOLVED", result.Code);
    }

    [Fact]
    public void Analyze_UnsupportedWall_BlocksCalculationAndApplyWithSpecificReason()
    {
        IsoFieldHostElement host = new(
            11,
            "Wall",
            "Стена",
            "Curved Wall",
            GeometryProfile: IsoFieldHostGeometryProfile.UnsupportedWall);

        IsoFieldHostSupportResult result = service.Analyze(host);

        Assert.Equal(IsoFieldHostSupportMode.Unsupported, result.Mode);
        Assert.False(result.CanCalculateRules);
        Assert.False(result.CanApplyRebar);
        Assert.Equal("WALL_GEOMETRY_UNSUPPORTED", result.Code);
        Assert.Contains("Криволинейные", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_HorizontalSlab_ReturnsEngineeringCapability()
    {
        IsoFieldHostElement host = new(
            12,
            "Slab",
            "Плита",
            "Floor 200",
            CreateSlabGeometry(),
            IsoFieldHostGeometryProfile.HorizontalSlab);

        IsoFieldHostSupportResult result = service.Analyze(host);

        Assert.Equal(IsoFieldHostSupportMode.Engineering, result.Mode);
        Assert.True(result.CanCalculateRules);
        Assert.True(result.CanApplyRebar);
        Assert.True(result.RequiresSlabBinding);
        Assert.Equal("SLAB_HORIZONTAL_ENGINEERING", result.Code);
    }

    [Theory]
    [InlineData(IsoFieldHostGeometryProfile.HorizontalSlab)]
    [InlineData(IsoFieldHostGeometryProfile.NonHorizontalOrUnresolvedSlab)]
    public void Analyze_SlabWithoutResolvedHorizontalGeometry_BlocksWorkflow(
        IsoFieldHostGeometryProfile profile)
    {
        IsoFieldHostElement host = new(
            13,
            "Slab",
            "Плита",
            "Sloped Floor",
            GeometryProfile: profile);

        IsoFieldHostSupportResult result = service.Analyze(host);

        Assert.Equal(IsoFieldHostSupportMode.Unsupported, result.Mode);
        Assert.False(result.IsSupported);
        Assert.Equal("SLAB_GEOMETRY_UNSUPPORTED", result.Code);
        Assert.Contains("Наклонные", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_UnknownHostKind_ReturnsUnsupportedCapability()
    {
        IsoFieldHostElement host = new(14, "Roof", "Крыша", "Roof 1");

        IsoFieldHostSupportResult result = service.Analyze(host);

        Assert.False(result.IsSupported);
        Assert.Equal("HOST_KIND_UNSUPPORTED", result.Code);
    }

    [Fact]
    public void Analyze_WallWithoutResolvedProfile_RequiresFreshSelection()
    {
        IsoFieldHostElement host = new(15, "Wall", "Стена", "Old selection");

        IsoFieldHostSupportResult result = service.Analyze(host);

        Assert.False(result.IsSupported);
        Assert.Equal("WALL_PROFILE_UNRESOLVED", result.Code);
        Assert.Contains("Выберите host заново", result.Message, StringComparison.Ordinal);
    }

    private static IsoFieldHostGeometry CreateSlabGeometry()
    {
        return new IsoFieldHostGeometry(
            new IsoFieldRebarPoint3D(0, 0, 0),
            new IsoFieldRebarPoint3D(1, 0, 0),
            new IsoFieldRebarPoint3D(0, 1, 0),
            new IsoFieldRebarPoint3D(0, 0, 1),
            [
                [
                    new IsoFieldPoint(0, 0),
                    new IsoFieldPoint(10, 0),
                    new IsoFieldPoint(10, 10),
                    new IsoFieldPoint(0, 10),
                    new IsoFieldPoint(0, 0)
                ]
            ]);
    }

    private static IsoFieldHostGeometry CreateWallGeometry()
    {
        return new IsoFieldHostGeometry(
            new IsoFieldRebarPoint3D(0, 0, 0),
            new IsoFieldRebarPoint3D(1, 0, 0),
            new IsoFieldRebarPoint3D(0, 0, 1),
            new IsoFieldRebarPoint3D(0, -1, 0),
            [
                [
                    new IsoFieldPoint(0, 0),
                    new IsoFieldPoint(10, 0),
                    new IsoFieldPoint(10, 3),
                    new IsoFieldPoint(0, 3),
                    new IsoFieldPoint(0, 0)
                ]
            ]);
    }
}
