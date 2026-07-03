using TrueBIM.App.Modules.VoltageDrop.Models;

namespace TrueBIM.App.Modules.VoltageDrop.Services;

public sealed class VoltageDropReferenceCatalog
{
    public static VoltageDropReferenceCatalog Default { get; } = new();

    public IReadOnlyList<VoltageDropCoefficientEntry> VoltageDropCoefficients => VoltageCoefficientEntries;

    public IReadOnlyList<VoltageDropReferencePoint> StandardApartmentSpecificDemandPoints => StandardApartmentDemandPointsData;

    public IReadOnlyList<VoltageDropReferencePoint> HighComfortApartmentCountDemandPoints => HighComfortApartmentCountDemandPointsData;

    public double GetVoltageDropCoefficient(VoltageDropConductorMaterial material, double voltage)
    {
        return GetVoltageDropCoefficientEntry(material, voltage).Coefficient;
    }

    public VoltageDropCoefficientEntry GetVoltageDropCoefficientEntry(VoltageDropConductorMaterial material, double voltage)
    {
        VoltageDropCoefficientEntry? entry = VoltageCoefficientEntries.FirstOrDefault(
            item => item.Material == material && Math.Abs(item.Voltage - voltage) < 0.000000001);
        if (entry is null)
        {
            throw new VoltageDropValidationException(
            [
                new VoltageDropValidationError(
                    nameof(VoltageDropInputs.AluminumCoefficient400),
                    "Коэффициент C для выбранного материала и напряжения не найден в справочнике.")
            ]);
        }

        return entry;
    }

    public double CalculateStandardApartmentSpecificDemand(double apartmentCount)
    {
        return Interpolate(apartmentCount, StandardApartmentDemandPointsData, belowRangeValue: 10);
    }

    public double CalculateHighComfortApartmentCountDemandFactor(double apartmentCount)
    {
        return Interpolate(apartmentCount, HighComfortApartmentCountDemandPointsData, belowRangeValue: 1);
    }

    public double CalculateHighComfortUnitPowerDemandFactor(double unitPower)
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

    public double CalculateLiftDemandFactor(double floors, double elevatorCount)
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

    private static double Interpolate(double value, IReadOnlyList<VoltageDropReferencePoint> points, double belowRangeValue)
    {
        if (value >= 1 && value <= 5)
        {
            return belowRangeValue;
        }

        for (int index = 0; index < points.Count - 1; index++)
        {
            VoltageDropReferencePoint current = points[index];
            VoltageDropReferencePoint next = points[index + 1];
            if (value >= current.Input && value <= next.Input - 1)
            {
                return Linear(value, current.Input, next.Input, current.Value, next.Value);
            }
        }

        if (value >= 600 && value <= 1000)
        {
            VoltageDropReferencePoint current = points[points.Count - 2];
            VoltageDropReferencePoint next = points[points.Count - 1];
            return Linear(value, current.Input, next.Input, current.Value, next.Value);
        }

        return double.NaN;
    }

    private static double Linear(double value, double fromValue, double toValue, double fromResult, double toResult)
    {
        return fromResult - ((fromResult - toResult) / (toValue - fromValue) * (value - fromValue));
    }

    private static readonly IReadOnlyList<VoltageDropCoefficientEntry> VoltageCoefficientEntries =
    [
        new(VoltageDropConductorMaterial.Aluminum, 400, 44, "Al, трехфазная сеть 400 В"),
        new(VoltageDropConductorMaterial.Copper, 400, 72.2, "Cu, трехфазная сеть 400 В"),
        new(VoltageDropConductorMaterial.Copper, 230, 12.1, "Cu, однофазная сеть 230 В"),
        new(VoltageDropConductorMaterial.Aluminum, 230, 7.7, "Al, однофазная сеть 230 В")
    ];

    private static readonly IReadOnlyList<VoltageDropReferencePoint> StandardApartmentDemandPointsData =
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

    private static readonly IReadOnlyList<VoltageDropReferencePoint> HighComfortApartmentCountDemandPointsData =
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
