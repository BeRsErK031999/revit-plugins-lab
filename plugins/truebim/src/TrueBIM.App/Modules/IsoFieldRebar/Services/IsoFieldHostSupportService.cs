using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldHostSupportService
{
    private const string WallHostKind = "Wall";
    private const string SlabHostKind = "Slab";

    public IsoFieldHostSupportResult Analyze(IsoFieldHostElement hostElement)
    {
        if (hostElement is null)
        {
            throw new ArgumentNullException(nameof(hostElement));
        }

        return hostElement.GeometryProfile switch
        {
            IsoFieldHostGeometryProfile.StraightBasicWall when hostElement.Geometry is not null => CreateStraightWallResult(),
            IsoFieldHostGeometryProfile.StraightBasicWall => CreateUnresolvedWallResult(),
            IsoFieldHostGeometryProfile.UnsupportedWall => CreateUnsupportedWallResult(),
            IsoFieldHostGeometryProfile.HorizontalSlab when hostElement.Geometry is not null => CreateHorizontalSlabResult(),
            IsoFieldHostGeometryProfile.HorizontalSlab => CreateUnsupportedSlabResult(),
            IsoFieldHostGeometryProfile.NonHorizontalOrUnresolvedSlab => CreateUnsupportedSlabResult(),
            _ => AnalyzeLegacyHost(hostElement)
        };
    }

    private static IsoFieldHostSupportResult AnalyzeLegacyHost(IsoFieldHostElement hostElement)
    {
        if (string.Equals(hostElement.HostKind, WallHostKind, StringComparison.Ordinal))
        {
            return new IsoFieldHostSupportResult(
                IsoFieldHostSupportMode.Unsupported,
                "WALL_PROFILE_UNRESOLVED",
                "Не удалось проверить форму стены. Выберите стену заново перед расчётом.");
        }

        if (string.Equals(hostElement.HostKind, SlabHostKind, StringComparison.Ordinal))
        {
            return hostElement.Geometry is null
                ? CreateUnsupportedSlabResult()
                : CreateHorizontalSlabResult();
        }

        return new IsoFieldHostSupportResult(
            IsoFieldHostSupportMode.Unsupported,
            "HOST_KIND_UNSUPPORTED",
                "Эта конструкция не поддерживается. Выберите прямую обычную стену или горизонтальную плиту.");
    }

    private static IsoFieldHostSupportResult CreateStraightWallResult()
    {
        return new IsoFieldHostSupportResult(
            IsoFieldHostSupportMode.Engineering,
            "WALL_STRAIGHT_BASIC_ENGINEERING",
                "Прямая обычная стена подходит для расчёта. Осталось проверить совмещение карты с наружной стороной по трём точкам.");
    }

    private static IsoFieldHostSupportResult CreateUnsupportedWallResult()
    {
        return new IsoFieldHostSupportResult(
            IsoFieldHostSupportMode.Unsupported,
            "WALL_GEOMETRY_UNSUPPORTED",
                "Поддерживаются только прямые обычные стены. Криволинейные, составные и витражные стены пока нельзя рассчитать.");
    }

    private static IsoFieldHostSupportResult CreateUnresolvedWallResult()
    {
        return new IsoFieldHostSupportResult(
            IsoFieldHostSupportMode.Unsupported,
            "WALL_PLANE_UNRESOLVED",
                "Не удалось определить одну непрерывную наружную сторону стены. Стены со сложной или разделённой поверхностью пока не поддерживаются.");
    }

    private static IsoFieldHostSupportResult CreateHorizontalSlabResult()
    {
        return new IsoFieldHostSupportResult(
            IsoFieldHostSupportMode.Engineering,
            "SLAB_HORIZONTAL_ENGINEERING",
                "Горизонтальная плита подходит для расчёта. Осталось проверить совмещение карты с верхней стороной по трём точкам.");
    }

    private static IsoFieldHostSupportResult CreateUnsupportedSlabResult()
    {
        return new IsoFieldHostSupportResult(
            IsoFieldHostSupportMode.Unsupported,
            "SLAB_GEOMETRY_UNSUPPORTED",
                "Не удалось определить ровную верхнюю сторону плиты. Наклонные плиты и плиты сложной формы пока не поддерживаются.");
    }
}
