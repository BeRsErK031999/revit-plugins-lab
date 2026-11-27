namespace TrueBIM.App.Modules.SlabReinforcement;

public static class SlabLayoutRules
{
    // All lengths are in millimetres. Accommodate Revit's sub-millimetre modelling noise.
    private const double Tolerance = 0.05;
    public static IReadOnlyList<double> Stations(double width, double step)
    {
        if (double.IsNaN(width) || double.IsInfinity(width) || double.IsNaN(step) || double.IsInfinity(step) || step < 50)
            throw new ArgumentOutOfRangeException(nameof(step), "Шаг должен составлять не менее 50 мм.");
        double span = width - 100;
        if (span < 50 - Tolerance)
            throw new InvalidOperationException("Недостаточно места для двух рядов с отступами 50 мм.");
        int full = (int)Math.Floor((span + Tolerance) / step);
        double remainder = span - full * step;
        var gaps = Enumerable.Repeat(step, full).ToList();
        if (remainder > Tolerance)
        {
            double end = Math.Round(remainder / 5) * 5;
            if (end >= 50 && Math.Abs(remainder - end) <= Tolerance)
                gaps.Add(end);
            else
            {
                double pair = step + remainder;
                double pairRounded = Math.Round(pair / 5) * 5;
                double last = Math.Max(50, Math.Ceiling(remainder / 5) * 5);
                if (full == 0 || Math.Abs(pair - pairRounded) > Tolerance || last > step || pairRounded - last < 50 || pairRounded - last > step)
                    throw new InvalidOperationException($"При ширине {width:F2} мм и шаге {step} мм крайние интервалы не получаются кратными 5 мм и не меньше 50 мм. Измените шаг.");
                gaps[full - 1] = pairRounded - last;
                gaps.Add(last);
            }
        }

        var result = new List<double>
        {
            50
        };
        foreach (double gap in gaps)
            result.Add(result[result.Count - 1] + gap);
        return result;
    }

    // Inputs and the outside dimension are in millimetres. Only numerical noise at exact boundaries is ignored.
    public static bool IsShortSpan(double clearLength, double legLength) => clearLength + 1e-7 < legLength;

    public static double StirrupLength(double available)
    {
        double rounded = 5 * Math.Floor((available + 1e-7) / 5);
        return rounded >= 120 ? rounded : 0;
    }

    public static double LegLength(double diameter, double rs, double rbt) => DevelopmentLength(diameter, rs, rbt, 2);
    public static double OpeningAnchor(double diameter, double rs, double rbt) => DevelopmentLength(diameter, rs, rbt, 1);

    private static double DevelopmentLength(double diameter, double rs, double rbt, double factor)
    {
        if (rs <= 0 || rbt <= 0 || diameter <= 0 || diameter > 40.001 || (diameter > 32.001 && Math.Min(Math.Abs(diameter - 36), Math.Abs(diameter - 40)) > 0.05))
            throw new InvalidOperationException("Уточните расчётные сопротивления и диаметр или задайте длину вручную.");
        double bond = 2.5 * (diameter <= 32.001 ? 1 : 0.9) * rbt;
        double basicLength = rs * diameter / (4 * bond);
        double lap = Math.Max(factor * basicLength, Math.Max(20 * diameter, 250));
        return Math.Ceiling(lap / 50 - 1e-10) * 50;
    }
}
