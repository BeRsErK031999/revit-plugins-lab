using TrueBIM.App.Modules.VoltageDrop.Models;

namespace TrueBIM.App.Modules.VoltageDrop.Services;

public sealed class VoltageDropCalculationService
{
    private const double ThreePhaseVoltage = 0.38;
    private const double SinglePhaseVoltage = 0.22;
    private const double Sqrt3Rounded = 1.73;

    private static readonly (double CosPhi, string Note)[] CurrentRows =
    [
        (0.98, "Нагревательные приборы"),
        (0.96, "Освещение"),
        (0.95, "Технологическое оборудование, оборудование СС"),
        (0.9, "Если по паспорту оборудования"),
        (0.85, "Насосы, вентиляторы до 1,0 кВт. Офисы. ИТП"),
        (0.8, "Если по паспорту оборудования"),
        (0.65, "Насосы, вентиляторы свыше 1,0 кВт")
    ];

    public VoltageDropResult CalculateVoltageDrop(VoltageDropInputs inputs)
    {
        if (inputs is null)
        {
            throw new ArgumentNullException(nameof(inputs));
        }

        double loadMoment = inputs.LineLength * inputs.Power;
        return new VoltageDropResult(
            loadMoment,
            Divide(loadMoment, inputs.AluminumCoefficient400 * inputs.CableSection),
            Divide(loadMoment, inputs.CopperCoefficient400 * inputs.CableSection),
            Divide(loadMoment, inputs.CopperCoefficient230 * inputs.CableSection),
            Divide(loadMoment, inputs.AluminumCoefficient230 * inputs.CableSection),
            CurrentRows
                .Select(row => new PhaseCurrentResult(
                    $"I3ф при cos={FormatCosPhi(row.CosPhi)}",
                    row.CosPhi,
                    Divide(inputs.Power, ThreePhaseVoltage * Sqrt3Rounded * row.CosPhi),
                    row.Note))
                .ToList(),
            CurrentRows
                .Select(row => new PhaseCurrentResult(
                    $"I1ф при cos={FormatCosPhi(row.CosPhi)}",
                    row.CosPhi,
                    Divide(inputs.Power, SinglePhaseVoltage * row.CosPhi),
                    row.Note))
                .ToList());
    }

    public ApartmentDemandResult CalculateApartmentDemand(ApartmentDemandInputs inputs)
    {
        if (inputs is null)
        {
            throw new ArgumentNullException(nameof(inputs));
        }

        double apartmentSpecificDemand = CalculateStandardApartmentSpecificDemand(inputs.ApartmentCount);
        double apartmentInstalledPower = inputs.ApartmentCount * inputs.ApartmentUnitPower;
        double apartmentActivePower = inputs.ApartmentCount
            * apartmentSpecificDemand
            * inputs.ApartmentUsageFactor
            * inputs.ApartmentCoincidenceFactor;
        double liftDemandFactor = CalculateLiftDemandFactor(inputs.Floors, inputs.ElevatorCount);
        double liftActivePower = inputs.LiftInstalledPower * liftDemandFactor * inputs.LiftCoincidenceFactor;

        return new ApartmentDemandResult(
            apartmentSpecificDemand,
            apartmentInstalledPower,
            CreateLoad(apartmentActivePower, inputs.ApartmentCosPhi),
            liftDemandFactor,
            CreateLoad(liftActivePower, inputs.LiftCosPhi));
    }

    public HighComfortApartmentDemandResult CalculateHighComfortApartmentDemand(HighComfortApartmentDemandInputs inputs)
    {
        return CalculateHighComfortApartmentDemand(
            inputs,
            CalculateApartmentDemand(ApartmentDemandInputs.Default).ApartmentLoad.ActivePower);
    }

    public HighComfortApartmentDemandResult CalculateHighComfortApartmentDemand(
        HighComfortApartmentDemandInputs inputs,
        double standardApartmentActivePower)
    {
        if (inputs is null)
        {
            throw new ArgumentNullException(nameof(inputs));
        }

        double unitPowerDemandFactor = CalculateHighComfortUnitPowerDemandFactor(inputs.ApartmentUnitPower);
        double apartmentCountDemandFactor = CalculateHighComfortApartmentCountDemandFactor(inputs.SpecificDemandApartmentCount);
        double apartmentInstalledPower = inputs.ApartmentCount * inputs.ApartmentUnitPower;
        double apartmentActivePower = inputs.ApartmentCount
            * inputs.ApartmentUnitPower
            * unitPowerDemandFactor
            * apartmentCountDemandFactor
            * inputs.ApartmentUsageFactor
            * inputs.ApartmentCoincidenceFactor;
        double liftDemandFactor = CalculateLiftDemandFactor(inputs.Floors, inputs.ElevatorCount);
        double liftActivePower = inputs.LiftInstalledPower * liftDemandFactor * inputs.LiftCoincidenceFactor;

        return new HighComfortApartmentDemandResult(
            unitPowerDemandFactor,
            apartmentCountDemandFactor,
            apartmentInstalledPower,
            CreateLoad(apartmentActivePower, inputs.ApartmentCosPhi),
            liftDemandFactor,
            CreateLoad(liftActivePower, inputs.LiftCosPhi),
            CreateLoad(apartmentActivePower + standardApartmentActivePower, inputs.CombinedApartmentCosPhi));
    }

    public static double CalculateStandardApartmentSpecificDemand(double apartmentCount)
    {
        return Interpolate(apartmentCount, StandardApartmentDemandPoints, belowRangeValue: 10);
    }

    public static double CalculateHighComfortApartmentCountDemandFactor(double apartmentCount)
    {
        return Interpolate(apartmentCount, HighComfortApartmentCountDemandPoints, belowRangeValue: 1);
    }

    public static double CalculateHighComfortUnitPowerDemandFactor(double unitPower)
    {
        if (unitPower >= 60 && unitPower <= 70)
        {
            return Linear(unitPower, 60, 70, 0.48, 0.45);
        }

        if (unitPower >= 50 && unitPower <= 59)
        {
            return Linear(unitPower, 50, 60, 0.5, 0.48);
        }

        if (unitPower >= 40 && unitPower <= 49)
        {
            return 0.55 - ((0.55 - 0.5) / (50 - 40) * (unitPower - 400));
        }

        if (unitPower >= 30 && unitPower <= 39)
        {
            return Linear(unitPower, 30, 40, 0.6, 0.55);
        }

        if (unitPower >= 20 && unitPower <= 29)
        {
            return Linear(unitPower, 20, 30, 0.65, 0.6);
        }

        if (unitPower >= 14 && unitPower <= 19)
        {
            return Linear(unitPower, 14, 20, 0.8, 0.65);
        }

        if (unitPower >= 1 && unitPower <= 14)
        {
            return 0.8;
        }

        return double.NaN;
    }

    public static double CalculateLiftDemandFactor(double floors, double elevatorCount)
    {
        if (floors > 11)
        {
            return CalculateLiftDemandFactorForTallBuildings(elevatorCount);
        }

        if (floors < 12)
        {
            return CalculateLiftDemandFactorForLowBuildings(elevatorCount);
        }

        return double.NaN;
    }

    private static ElectricalLoadResult CreateLoad(double activePower, double cosPhi)
    {
        double apparentPower = Divide(activePower, cosPhi);
        double reactivePower = Math.Sqrt(Math.Max(0, apparentPower * apparentPower - activePower * activePower));
        double current = Divide(activePower, ThreePhaseVoltage * Math.Sqrt(3) * cosPhi);
        return new ElectricalLoadResult(activePower, cosPhi, reactivePower, apparentPower, current);
    }

    private static double CalculateLiftDemandFactorForTallBuildings(double elevatorCount)
    {
        if (elevatorCount == 1)
        {
            return 1;
        }

        if (elevatorCount == 2 || elevatorCount == 3)
        {
            return 0.9;
        }

        if (elevatorCount == 4 || elevatorCount == 5)
        {
            return 0.8;
        }

        if (elevatorCount == 10)
        {
            return 0.6;
        }

        if (elevatorCount >= 6 && elevatorCount <= 10)
        {
            return Linear(elevatorCount, 6, 10, 0.75, 0.6);
        }

        if (elevatorCount >= 11 && elevatorCount <= 20)
        {
            return Linear(elevatorCount, 10, 20, 0.6, 0.5);
        }

        if (elevatorCount >= 21 && elevatorCount <= 25)
        {
            return Linear(elevatorCount, 20, 25, 0.5, 0.4);
        }

        return elevatorCount > 25 ? 0.4 : double.NaN;
    }

    private static double CalculateLiftDemandFactorForLowBuildings(double elevatorCount)
    {
        if (elevatorCount == 1)
        {
            return 1;
        }

        if (elevatorCount == 2 || elevatorCount == 3)
        {
            return 0.8;
        }

        if (elevatorCount == 4 || elevatorCount == 5)
        {
            return 0.7;
        }

        if (elevatorCount == 10)
        {
            return 0.5;
        }

        if (elevatorCount >= 6 && elevatorCount <= 10)
        {
            return Linear(elevatorCount, 6, 10, 0.65, 0.5);
        }

        if (elevatorCount >= 11 && elevatorCount <= 20)
        {
            return Linear(elevatorCount, 10, 20, 0.5, 0.4);
        }

        if (elevatorCount >= 21 && elevatorCount <= 25)
        {
            return Linear(elevatorCount, 20, 25, 0.4, 0.35);
        }

        return elevatorCount > 25 ? 0.35 : double.NaN;
    }

    private static double Interpolate(double value, IReadOnlyList<DemandPoint> points, double belowRangeValue)
    {
        if (value >= 1 && value <= 5)
        {
            return belowRangeValue;
        }

        for (int index = 0; index < points.Count - 1; index++)
        {
            DemandPoint current = points[index];
            DemandPoint next = points[index + 1];
            if (value >= current.Count && value <= next.Count - 1)
            {
                return Linear(value, current.Count, next.Count, current.Value, next.Value);
            }
        }

        if (value >= 600 && value <= 1000)
        {
            DemandPoint current = points[points.Count - 2];
            DemandPoint next = points[points.Count - 1];
            return Linear(value, current.Count, next.Count, current.Value, next.Value);
        }

        return double.NaN;
    }

    private static double Linear(double value, double fromValue, double toValue, double fromResult, double toResult)
    {
        return fromResult - ((fromResult - toResult) / (toValue - fromValue) * (value - fromValue));
    }

    private static double Divide(double numerator, double denominator)
    {
        return denominator == 0 ? double.NaN : numerator / denominator;
    }

    private static string FormatCosPhi(double cosPhi)
    {
        return cosPhi.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed record DemandPoint(double Count, double Value);

    private static readonly DemandPoint[] StandardApartmentDemandPoints =
    [
        new(6, 5.1),
        new(9, 3.8),
        new(12, 3.2),
        new(15, 2.8),
        new(18, 2.6),
        new(24, 2.2),
        new(40, 1.95),
        new(60, 1.7),
        new(100, 1.5),
        new(200, 1.36),
        new(400, 1.27),
        new(600, 1.23),
        new(1000, 1.19)
    ];

    private static readonly DemandPoint[] HighComfortApartmentCountDemandPoints =
    [
        new(6, 0.51),
        new(9, 0.38),
        new(12, 0.32),
        new(15, 0.29),
        new(18, 0.26),
        new(24, 0.24),
        new(40, 0.2),
        new(60, 0.18),
        new(100, 0.16),
        new(200, 0.14),
        new(400, 0.13),
        new(600, 0.11),
        new(1000, 0.11)
    ];
}
