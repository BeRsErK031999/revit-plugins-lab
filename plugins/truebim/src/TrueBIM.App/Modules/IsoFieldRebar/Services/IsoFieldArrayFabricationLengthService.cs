namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

/// <summary>Подбирает фактическую длину для проверенного семейства массива формы 000.</summary>
public sealed class IsoFieldArrayFabricationLengthService
{
    private const double ConversionToleranceMillimeters = 1e-6;
    // Источник: формула загруженного семейства 000 в локальном Revit 2023,
    // inspection response-01-inspect.json, параметр «Кратная длина» = 1.
    // Это размеры конкретного семейства, а не нормативная таблица раскроя.
    private static readonly double[] FamilyLengthsMillimeters =
    [
        1170, 1300, 1460, 1670, 1950, 2340, 2920, 3900, 4390, 4880,
        5850, 6820, 7310, 7800, 8780, 9360, 9750, 10030, 10400, 11700
    ];

    public double NormalizeMinimumLengthMillimeters(double minimumLengthMillimeters)
    {
        if (double.IsNaN(minimumLengthMillimeters) || double.IsInfinity(minimumLengthMillimeters)
            || minimumLengthMillimeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumLengthMillimeters),
                "Минимальная длина стержня должна быть положительным конечным числом.");
        }

        foreach (double familyLength in FamilyLengthsMillimeters)
        {
            // Не повторяем round(Aмм) семейства: он может уменьшить требуемую длину.
            // Перевод геометрии feet -> мм даёт, например, 1170.0000000000002.
            // Не перескакиваем на следующий размер из-за погрешности double.
            if (familyLength + ConversionToleranceMillimeters >= minimumLengthMillimeters)
            {
                return familyLength;
            }
        }

        throw new InvalidOperationException(
            $"Требуемая длина {minimumLengthMillimeters:0.###} мм превышает максимальную проверенную длину семейства 000 — 11700 мм. Требуется разделение пятна.");
    }
}
