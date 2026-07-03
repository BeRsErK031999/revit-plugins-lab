using TrueBIM.App.Modules.VoltageDrop.Models;

namespace TrueBIM.App.Modules.VoltageDrop.Services;

public sealed class VoltageDropCalculationService
{
    private const double ThreePhaseVoltage = 0.38;
    private const double SinglePhaseVoltage = 0.22;
    private const double Sqrt3Rounded = 1.73;
    private readonly VoltageDropReferenceCatalog referenceCatalog;

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

    public VoltageDropCalculationService()
        : this(VoltageDropReferenceCatalog.Default)
    {
    }

    public VoltageDropCalculationService(VoltageDropReferenceCatalog referenceCatalog)
    {
        this.referenceCatalog = referenceCatalog ?? throw new ArgumentNullException(nameof(referenceCatalog));
    }

    public VoltageDropResult CalculateVoltageDrop(VoltageDropInputs inputs)
    {
        if (inputs is null)
        {
            throw new ArgumentNullException(nameof(inputs));
        }

        ValidateVoltageDropInputs(inputs);

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

    public VoltageDropSelectedResult CalculateSelectedVoltageDrop(
        VoltageDropInputs inputs,
        VoltageDropConductorMaterial material,
        double voltage)
    {
        if (inputs is null)
        {
            throw new ArgumentNullException(nameof(inputs));
        }

        ValidateVoltageDropInputs(inputs);

        double loadMoment = inputs.LineLength * inputs.Power;
        VoltageDropCoefficientEntry coefficient = referenceCatalog.GetVoltageDropCoefficientEntry(material, voltage);
        return new VoltageDropSelectedResult(
            coefficient,
            loadMoment,
            Divide(loadMoment, coefficient.Coefficient * inputs.CableSection));
    }

    public ApartmentDemandResult CalculateApartmentDemand(ApartmentDemandInputs inputs)
    {
        if (inputs is null)
        {
            throw new ArgumentNullException(nameof(inputs));
        }

        ValidateApartmentDemandInputs(inputs);

        double apartmentSpecificDemand = referenceCatalog.CalculateStandardApartmentSpecificDemand(inputs.ApartmentCount);
        double apartmentInstalledPower = inputs.ApartmentCount * inputs.ApartmentUnitPower;
        double apartmentActivePower = inputs.ApartmentCount
            * apartmentSpecificDemand
            * inputs.ApartmentUsageFactor
            * inputs.ApartmentCoincidenceFactor;
        double liftDemandFactor = referenceCatalog.CalculateLiftDemandFactor(inputs.Floors, inputs.ElevatorCount);
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

        ValidateHighComfortApartmentDemandInputs(inputs);

        double unitPowerDemandFactor = referenceCatalog.CalculateHighComfortUnitPowerDemandFactor(inputs.ApartmentUnitPower);
        double apartmentCountDemandFactor = referenceCatalog.CalculateHighComfortApartmentCountDemandFactor(inputs.SpecificDemandApartmentCount);
        double apartmentInstalledPower = inputs.ApartmentCount * inputs.ApartmentUnitPower;
        double apartmentActivePower = inputs.ApartmentCount
            * inputs.ApartmentUnitPower
            * unitPowerDemandFactor
            * apartmentCountDemandFactor
            * inputs.ApartmentUsageFactor
            * inputs.ApartmentCoincidenceFactor;
        double liftDemandFactor = referenceCatalog.CalculateLiftDemandFactor(inputs.Floors, inputs.ElevatorCount);
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
        EnsureRange(nameof(ApartmentDemandInputs.ApartmentCount), apartmentCount, 1, 1000, "Количество квартир должно быть в диапазоне 1..1000.");
        return VoltageDropReferenceCatalog.Default.CalculateStandardApartmentSpecificDemand(apartmentCount);
    }

    public static double CalculateHighComfortApartmentCountDemandFactor(double apartmentCount)
    {
        EnsureRange(nameof(HighComfortApartmentDemandInputs.SpecificDemandApartmentCount), apartmentCount, 1, 1000, "Количество квартир для коэффициента должно быть в диапазоне 1..1000.");
        return VoltageDropReferenceCatalog.Default.CalculateHighComfortApartmentCountDemandFactor(apartmentCount);
    }

    public static double CalculateHighComfortUnitPowerDemandFactor(double unitPower)
    {
        EnsureRange(nameof(HighComfortApartmentDemandInputs.ApartmentUnitPower), unitPower, 1, 70, "Ру. для квартир повышенной комфортности должно быть в диапазоне 1..70 кВт.");
        return VoltageDropReferenceCatalog.Default.CalculateHighComfortUnitPowerDemandFactor(unitPower);
    }

    public static double CalculateLiftDemandFactor(double floors, double elevatorCount)
    {
        EnsurePositive(nameof(ApartmentDemandInputs.Floors), floors, "Количество этажей должно быть больше 0.");
        EnsureWholeNumber(nameof(ApartmentDemandInputs.Floors), floors, "Количество этажей должно быть целым числом.");
        EnsurePositive(nameof(ApartmentDemandInputs.ElevatorCount), elevatorCount, "Количество лифтов должно быть больше 0.");
        EnsureWholeNumber(nameof(ApartmentDemandInputs.ElevatorCount), elevatorCount, "Количество лифтов должно быть целым числом.");

        return VoltageDropReferenceCatalog.Default.CalculateLiftDemandFactor(floors, elevatorCount);
    }

    private static void ValidateVoltageDropInputs(VoltageDropInputs inputs)
    {
        List<VoltageDropValidationError> errors = new();
        AddPositive(errors, nameof(VoltageDropInputs.AluminumCoefficient400), inputs.AluminumCoefficient400, "Коэффициент C для Al, 400 В должен быть больше 0.");
        AddPositive(errors, nameof(VoltageDropInputs.CopperCoefficient400), inputs.CopperCoefficient400, "Коэффициент C для Cu, 400 В должен быть больше 0.");
        AddPositive(errors, nameof(VoltageDropInputs.CopperCoefficient230), inputs.CopperCoefficient230, "Коэффициент C для Cu, 230 В должен быть больше 0.");
        AddPositive(errors, nameof(VoltageDropInputs.AluminumCoefficient230), inputs.AluminumCoefficient230, "Коэффициент C для Al, 230 В должен быть больше 0.");
        AddNonNegative(errors, nameof(VoltageDropInputs.LineLength), inputs.LineLength, "Длина линии не может быть отрицательной.");
        AddPositive(errors, nameof(VoltageDropInputs.CableSection), inputs.CableSection, "Сечение кабеля должно быть больше 0.");
        AddNonNegative(errors, nameof(VoltageDropInputs.Power), inputs.Power, "Мощность не может быть отрицательной.");
        ThrowIfAny(errors);
    }

    private static void ValidateApartmentDemandInputs(ApartmentDemandInputs inputs)
    {
        List<VoltageDropValidationError> errors = new();
        AddPositiveInteger(errors, nameof(ApartmentDemandInputs.Floors), inputs.Floors, "Количество этажей должно быть целым числом больше 0.");
        AddIntegerRange(errors, nameof(ApartmentDemandInputs.ApartmentCount), inputs.ApartmentCount, 1, 1000, "Количество квартир должно быть целым числом в диапазоне 1..1000.");
        AddPositiveInteger(errors, nameof(ApartmentDemandInputs.ElevatorCount), inputs.ElevatorCount, "Количество лифтов должно быть целым числом больше 0.");
        AddNonNegative(errors, nameof(ApartmentDemandInputs.ApartmentUnitPower), inputs.ApartmentUnitPower, "Ру. на квартиру не может быть отрицательной.");
        AddFactor(errors, nameof(ApartmentDemandInputs.ApartmentUsageFactor), inputs.ApartmentUsageFactor, "Кс квартир должен быть в диапазоне 0..1.");
        AddFactor(errors, nameof(ApartmentDemandInputs.ApartmentCoincidenceFactor), inputs.ApartmentCoincidenceFactor, "Кс.об квартир должен быть в диапазоне 0..1.");
        AddCosPhi(errors, nameof(ApartmentDemandInputs.ApartmentCosPhi), inputs.ApartmentCosPhi, "cos(φ) квартир должен быть больше 0 и не больше 1.");
        AddNonNegative(errors, nameof(ApartmentDemandInputs.LiftInstalledPower), inputs.LiftInstalledPower, "Ру.общ. лифтов не может быть отрицательной.");
        AddFactor(errors, nameof(ApartmentDemandInputs.LiftCoincidenceFactor), inputs.LiftCoincidenceFactor, "Кс.об лифтов должен быть в диапазоне 0..1.");
        AddCosPhi(errors, nameof(ApartmentDemandInputs.LiftCosPhi), inputs.LiftCosPhi, "cos(φ) лифтов должен быть больше 0 и не больше 1.");
        ThrowIfAny(errors);
    }

    private static void ValidateHighComfortApartmentDemandInputs(HighComfortApartmentDemandInputs inputs)
    {
        List<VoltageDropValidationError> errors = new();
        AddPositiveInteger(errors, nameof(HighComfortApartmentDemandInputs.Floors), inputs.Floors, "Количество этажей должно быть целым числом больше 0.");
        AddIntegerRange(errors, nameof(HighComfortApartmentDemandInputs.ApartmentCount), inputs.ApartmentCount, 1, 1000, "Количество квартир должно быть целым числом в диапазоне 1..1000.");
        AddIntegerRange(errors, nameof(HighComfortApartmentDemandInputs.SpecificDemandApartmentCount), inputs.SpecificDemandApartmentCount, 1, 1000, "Количество квартир для коэффициента должно быть целым числом в диапазоне 1..1000.");
        AddPositiveInteger(errors, nameof(HighComfortApartmentDemandInputs.ElevatorCount), inputs.ElevatorCount, "Количество лифтов должно быть целым числом больше 0.");
        AddRange(errors, nameof(HighComfortApartmentDemandInputs.ApartmentUnitPower), inputs.ApartmentUnitPower, 1, 70, "Ру. для квартир повышенной комфортности должно быть в диапазоне 1..70 кВт.");
        AddFactor(errors, nameof(HighComfortApartmentDemandInputs.ApartmentUsageFactor), inputs.ApartmentUsageFactor, "Кс квартир должен быть в диапазоне 0..1.");
        AddFactor(errors, nameof(HighComfortApartmentDemandInputs.ApartmentCoincidenceFactor), inputs.ApartmentCoincidenceFactor, "Кс.об квартир должен быть в диапазоне 0..1.");
        AddCosPhi(errors, nameof(HighComfortApartmentDemandInputs.ApartmentCosPhi), inputs.ApartmentCosPhi, "cos(φ) квартир должен быть больше 0 и не больше 1.");
        AddNonNegative(errors, nameof(HighComfortApartmentDemandInputs.LiftInstalledPower), inputs.LiftInstalledPower, "Ру.общ. лифтов не может быть отрицательной.");
        AddFactor(errors, nameof(HighComfortApartmentDemandInputs.LiftCoincidenceFactor), inputs.LiftCoincidenceFactor, "Кс.об лифтов должен быть в диапазоне 0..1.");
        AddCosPhi(errors, nameof(HighComfortApartmentDemandInputs.LiftCosPhi), inputs.LiftCosPhi, "cos(φ) лифтов должен быть больше 0 и не больше 1.");
        AddCosPhi(errors, nameof(HighComfortApartmentDemandInputs.CombinedApartmentCosPhi), inputs.CombinedApartmentCosPhi, "cos(φ) общего расчета должен быть больше 0 и не больше 1.");
        ThrowIfAny(errors);
    }

    private static void AddPositiveInteger(List<VoltageDropValidationError> errors, string fieldKey, double value, string message)
    {
        AddPositive(errors, fieldKey, value, message);
        AddWholeNumber(errors, fieldKey, value, message);
    }

    private static void AddIntegerRange(List<VoltageDropValidationError> errors, string fieldKey, double value, double minimum, double maximum, string message)
    {
        AddRange(errors, fieldKey, value, minimum, maximum, message);
        AddWholeNumber(errors, fieldKey, value, message);
    }

    private static void AddPositive(List<VoltageDropValidationError> errors, string fieldKey, double value, string message)
    {
        if (IsInvalidNumber(value) || value <= 0)
        {
            errors.Add(new VoltageDropValidationError(fieldKey, message));
        }
    }

    private static void AddNonNegative(List<VoltageDropValidationError> errors, string fieldKey, double value, string message)
    {
        if (IsInvalidNumber(value) || value < 0)
        {
            errors.Add(new VoltageDropValidationError(fieldKey, message));
        }
    }

    private static void AddFactor(List<VoltageDropValidationError> errors, string fieldKey, double value, string message)
    {
        AddRange(errors, fieldKey, value, 0, 1, message);
    }

    private static void AddCosPhi(List<VoltageDropValidationError> errors, string fieldKey, double value, string message)
    {
        if (IsInvalidNumber(value) || value <= 0 || value > 1)
        {
            errors.Add(new VoltageDropValidationError(fieldKey, message));
        }
    }

    private static void AddRange(List<VoltageDropValidationError> errors, string fieldKey, double value, double minimum, double maximum, string message)
    {
        if (IsInvalidNumber(value) || value < minimum || value > maximum)
        {
            errors.Add(new VoltageDropValidationError(fieldKey, message));
        }
    }

    private static void AddWholeNumber(List<VoltageDropValidationError> errors, string fieldKey, double value, string message)
    {
        if (IsInvalidNumber(value))
        {
            return;
        }

        if (Math.Abs(value - Math.Round(value)) > 0.000000001)
        {
            errors.Add(new VoltageDropValidationError(fieldKey, message));
        }
    }

    private static void EnsurePositive(string fieldKey, double value, string message)
    {
        if (IsInvalidNumber(value) || value <= 0)
        {
            throw new VoltageDropValidationException([new VoltageDropValidationError(fieldKey, message)]);
        }
    }

    private static void EnsureRange(string fieldKey, double value, double minimum, double maximum, string message)
    {
        if (IsInvalidNumber(value) || value < minimum || value > maximum)
        {
            throw new VoltageDropValidationException([new VoltageDropValidationError(fieldKey, message)]);
        }
    }

    private static void EnsureWholeNumber(string fieldKey, double value, string message)
    {
        if (IsInvalidNumber(value))
        {
            throw new VoltageDropValidationException([new VoltageDropValidationError(fieldKey, message)]);
        }

        if (Math.Abs(value - Math.Round(value)) > 0.000000001)
        {
            throw new VoltageDropValidationException([new VoltageDropValidationError(fieldKey, message)]);
        }
    }

    private static void ThrowIfAny(IReadOnlyList<VoltageDropValidationError> errors)
    {
        if (errors.Count > 0)
        {
            throw new VoltageDropValidationException(errors);
        }
    }

    private static bool IsInvalidNumber(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value);
    }

    private static ElectricalLoadResult CreateLoad(double activePower, double cosPhi)
    {
        double apparentPower = Divide(activePower, cosPhi);
        double reactivePower = Math.Sqrt(Math.Max(0, apparentPower * apparentPower - activePower * activePower));
        double current = Divide(activePower, ThreePhaseVoltage * Math.Sqrt(3) * cosPhi);
        return new ElectricalLoadResult(activePower, cosPhi, reactivePower, apparentPower, current);
    }

    private static double Divide(double numerator, double denominator)
    {
        return denominator == 0 ? double.NaN : numerator / denominator;
    }

    private static string FormatCosPhi(double cosPhi)
    {
        return cosPhi.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
