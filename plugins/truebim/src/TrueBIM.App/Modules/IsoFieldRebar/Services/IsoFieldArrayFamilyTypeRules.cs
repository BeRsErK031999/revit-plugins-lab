using System.Text.RegularExpressions;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public static class IsoFieldArrayFamilyTypeRules
{
    public const string RequiredSteelGrade = "A500С";

    public static bool SizeMatches(
        double? actualDiameterMillimeters,
        double? actualSpacingMillimeters,
        double expectedDiameterMillimeters,
        double expectedSpacingMillimeters)
    {
        return PositiveFinite(actualDiameterMillimeters)
            && PositiveFinite(actualSpacingMillimeters)
            && PositiveFinite(expectedDiameterMillimeters)
            && PositiveFinite(expectedSpacingMillimeters)
            && Math.Abs(actualDiameterMillimeters!.Value - expectedDiameterMillimeters) <= 0.2
            && Math.Abs(actualSpacingMillimeters!.Value - expectedSpacingMillimeters) <= 0.2;
    }

    public static bool HasVerifiedSteelGrade(string? lookupProductName, double? rollCode)
    {
        if (string.IsNullOrWhiteSpace(lookupProductName)
            || rollCode is null
            || double.IsNaN(rollCode.Value)
            || double.IsInfinity(rollCode.Value))
        {
            return false;
        }

        string normalized = lookupProductName!.ToUpperInvariant().Replace('А', 'A').Replace('С', 'C');
        return Regex.IsMatch(normalized, @"(?<![\p{L}\p{N}])A500C(?![\p{L}\p{N}])", RegexOptions.CultureInvariant)
            && !Regex.IsMatch(normalized, @"(?<![\p{L}\p{N}])A400(?![\p{L}\p{N}])", RegexOptions.CultureInvariant);
    }

    public static bool GeometryMatches(
        double? actualLengthMillimeters,
        double? actualWidthMillimeters,
        double? actualBarCount,
        double expectedLengthMillimeters,
        double expectedWidthMillimeters,
        int expectedBarCount)
    {
        return PositiveFinite(actualLengthMillimeters)
            && PositiveFinite(actualWidthMillimeters)
            && PositiveFinite(actualBarCount)
            && PositiveFinite(expectedLengthMillimeters)
            && PositiveFinite(expectedWidthMillimeters)
            && expectedBarCount > 0
            && Math.Abs(actualLengthMillimeters!.Value - expectedLengthMillimeters) <= 1
            && Math.Abs(actualWidthMillimeters!.Value - expectedWidthMillimeters) <= 1
            && Math.Abs(actualBarCount!.Value - expectedBarCount) <= 1e-6;
    }

    private static bool PositiveFinite(double? value)
    {
        return value is not null
            && !double.IsNaN(value.Value)
            && !double.IsInfinity(value.Value)
            && value.Value > 0;
    }
}
