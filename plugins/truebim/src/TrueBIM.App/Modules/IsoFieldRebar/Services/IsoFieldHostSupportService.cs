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
                "Профиль стены не зафиксирован. Выберите host заново, чтобы подтвердить прямую базовую стену до расчёта и записи.");
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
            $"Тип host '{hostElement.HostKind}' не поддерживается. Выберите прямую базовую стену или горизонтальную плиту.");
    }

    private static IsoFieldHostSupportResult CreateStraightWallResult()
    {
        return new IsoFieldHostSupportResult(
            IsoFieldHostSupportMode.Engineering,
            "WALL_STRAIGHT_BASIC_ENGINEERING",
            "Прямая базовая стена поддерживается в инженерном режиме после проверки трёхточечной привязки наружной плоскости.");
    }

    private static IsoFieldHostSupportResult CreateUnsupportedWallResult()
    {
        return new IsoFieldHostSupportResult(
            IsoFieldHostSupportMode.Unsupported,
            "WALL_GEOMETRY_UNSUPPORTED",
            "Поддерживаются только прямые базовые стены. Криволинейные, составные и витражные стены пока заблокированы до расчёта и записи.");
    }

    private static IsoFieldHostSupportResult CreateUnresolvedWallResult()
    {
        return new IsoFieldHostSupportResult(
            IsoFieldHostSupportMode.Unsupported,
            "WALL_PLANE_UNRESOLVED",
            "Единственная непрерывная наружная плоскость прямой стены не распознана. Фрагментированная геометрия пока не поддерживается; расчёт и запись заблокированы.");
    }

    private static IsoFieldHostSupportResult CreateHorizontalSlabResult()
    {
        return new IsoFieldHostSupportResult(
            IsoFieldHostSupportMode.Engineering,
            "SLAB_HORIZONTAL_ENGINEERING",
            "Горизонтальная плита поддерживается после проверки трёхточечной привязки.");
    }

    private static IsoFieldHostSupportResult CreateUnsupportedSlabResult()
    {
        return new IsoFieldHostSupportResult(
            IsoFieldHostSupportMode.Unsupported,
            "SLAB_GEOMETRY_UNSUPPORTED",
            "Горизонтальная верхняя грань плиты не распознана. Наклонные и геометрически неоднозначные плиты пока заблокированы до расчёта и записи.");
    }
}
