using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public static class FinishGeometryAreaRules
{
    private const double MinimumArea = 1e-9;

    public static double? SelectHorizontalProjectedArea(
        IEnumerable<FinishFaceMeasure> faces,
        double normalTolerance = 0.999)
    {
        ValidateTolerance(normalTolerance);
        return SelectMaximumArea(
            faces,
            face => NormalizedAbsoluteDot(face, 0, 0, 1) >= normalTolerance);
    }

    public static bool HasOpposingHorizontalFaces(
        IEnumerable<FinishFaceMeasure> faces,
        double normalTolerance = 0.999)
    {
        if (faces is null)
        {
            throw new ArgumentNullException(nameof(faces));
        }

        ValidateTolerance(normalTolerance);
        bool hasUpwardFace = false;
        bool hasDownwardFace = false;
        foreach (FinishFaceMeasure face in faces)
        {
            double dot = NormalizedDot(face, 0, 0, 1);
            hasUpwardFace |= dot >= normalTolerance;
            hasDownwardFace |= dot <= -normalTolerance;
            if (hasUpwardFace && hasDownwardFace)
            {
                return true;
            }
        }

        return false;
    }

    public static double? SumDownwardFacingArea(
        IEnumerable<FinishFaceMeasure> faces,
        double minimumVerticalComponent = 0.01)
    {
        if (faces is null)
        {
            throw new ArgumentNullException(nameof(faces));
        }

        ValidateTolerance(minimumVerticalComponent);
        double area = faces
            .Where(face => IsFinitePositive(face.Area)
                && NormalizedDot(face, 0, 0, 1) <= -minimumVerticalComponent)
            .Sum(face => face.Area);
        return area > MinimumArea ? area : null;
    }

    public static double? SelectParallelFaceArea(
        IEnumerable<FinishFaceMeasure> faces,
        double referenceX,
        double referenceY,
        double referenceZ,
        double normalTolerance = 0.98)
    {
        ValidateTolerance(normalTolerance);
        double length = Math.Sqrt(
            (referenceX * referenceX)
            + (referenceY * referenceY)
            + (referenceZ * referenceZ));
        if (length <= 1e-12 || double.IsNaN(length) || double.IsInfinity(length))
        {
            throw new ArgumentOutOfRangeException(nameof(referenceX));
        }

        double x = referenceX / length;
        double y = referenceY / length;
        double z = referenceZ / length;
        return SelectMaximumArea(
            faces,
            face => NormalizedAbsoluteDot(face, x, y, z) >= normalTolerance);
    }

    private static double? SelectMaximumArea(
        IEnumerable<FinishFaceMeasure> faces,
        Func<FinishFaceMeasure, bool> predicate)
    {
        if (faces is null)
        {
            throw new ArgumentNullException(nameof(faces));
        }

        double[] areas = faces
            .Where(face => IsFinitePositive(face.Area) && predicate(face))
            .Select(face => face.Area)
            .ToArray();
        return areas.Length == 0 ? null : areas.Max();
    }

    private static double NormalizedAbsoluteDot(
        FinishFaceMeasure face,
        double referenceX,
        double referenceY,
        double referenceZ)
    {
        return Math.Abs(NormalizedDot(face, referenceX, referenceY, referenceZ));
    }

    private static double NormalizedDot(
        FinishFaceMeasure face,
        double referenceX,
        double referenceY,
        double referenceZ)
    {
        double length = Math.Sqrt(
            (face.NormalX * face.NormalX)
            + (face.NormalY * face.NormalY)
            + (face.NormalZ * face.NormalZ));
        if (length <= 1e-12 || double.IsNaN(length) || double.IsInfinity(length))
        {
            return 0;
        }

        double dot = ((face.NormalX / length) * referenceX)
            + ((face.NormalY / length) * referenceY)
            + ((face.NormalZ / length) * referenceZ);
        return dot;
    }

    private static void ValidateTolerance(double tolerance)
    {
        if (tolerance <= 0 || tolerance > 1 || double.IsNaN(tolerance))
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        }
    }

    private static bool IsFinitePositive(double value)
    {
        return value > MinimumArea && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
