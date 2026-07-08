using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TrueBIM.App.Modules.BimTools.ClashReport.Models;

namespace TrueBIM.App.Modules.BimTools.ClashReport.Services;

public sealed class ClashTriageService
{
    private const double FeetToMillimeters = 304.8;

    public ClashTriageResult Create(ClashTriageInput input)
    {
        string groupKey = BuildGroupKey(input);
        double severityScore = CalculateSeverityScore(input.ApproximateVolumeMm3);
        ClashPriority priority = ResolvePriority(input.ApproximateVolumeMm3);
        string fingerprint = BuildFingerprint(input);

        return new ClashTriageResult(fingerprint, groupKey, priority, severityScore);
    }

    public static ClashPriority ResolvePriority(double approximateVolumeMm3)
    {
        if (approximateVolumeMm3 >= 125_000_000)
        {
            return ClashPriority.Critical;
        }

        if (approximateVolumeMm3 >= 8_000_000)
        {
            return ClashPriority.High;
        }

        if (approximateVolumeMm3 >= 1_000_000)
        {
            return ClashPriority.Medium;
        }

        return ClashPriority.Low;
    }

    public static double CalculateSeverityScore(double approximateVolumeMm3)
    {
        if (double.IsNaN(approximateVolumeMm3) || double.IsInfinity(approximateVolumeMm3) || approximateVolumeMm3 <= 0)
        {
            return 0;
        }

        double normalized = (Math.Log10(approximateVolumeMm3) - 5) / 4;
        double clamped = Math.Max(0, Math.Min(1, normalized));
        return Math.Round(clamped * 100, 1);
    }

    public static string BuildGroupKey(string source, string category1, string category2)
    {
        string first = NormalizeLabel(category1, "No category");
        string second = NormalizeLabel(category2, "No category");
        string[] categories = [first, second];
        Array.Sort(categories, StringComparer.OrdinalIgnoreCase);

        return $"{NormalizeLabel(source, "Check")} | {categories[0]} x {categories[1]}";
    }

    private static string BuildGroupKey(ClashTriageInput input)
    {
        return input.GroupingStrategy switch
        {
            ClashGroupingStrategy.ElementPair => BuildElementPairGroupKey(input),
            ClashGroupingStrategy.LocationBucket => BuildLocationGroupKey(input),
            _ => BuildGroupKey(input.Source, input.Element1Category, input.Element2Category)
        };
    }

    private static string BuildElementPairGroupKey(ClashTriageInput input)
    {
        string[] endpoints =
        [
            BuildEndpoint(input.ElementId1, input.LinkedElementId1),
            BuildEndpoint(input.ElementId2, input.LinkedElementId2)
        ];
        Array.Sort(endpoints, StringComparer.OrdinalIgnoreCase);

        return $"{NormalizeLabel(input.Source, "Check")} | {endpoints[0]} x {endpoints[1]}";
    }

    private static string BuildLocationGroupKey(ClashTriageInput input)
    {
        return $"{NormalizeLabel(input.Source, "Check")} | Grid {BuildLocationBucket(input.CenterXFeet, input.CenterYFeet, input.CenterZFeet)}";
    }

    private static string BuildFingerprint(ClashTriageInput input)
    {
        string[] endpoints =
        [
            BuildEndpoint(input.ElementId1, input.LinkedElementId1),
            BuildEndpoint(input.ElementId2, input.LinkedElementId2)
        ];
        Array.Sort(endpoints, StringComparer.OrdinalIgnoreCase);

        string raw = string.Join(
            "|",
            NormalizeToken(input.Source),
            input.ClashType,
            endpoints[0],
            endpoints[1],
            BuildLocationBucket(input.CenterXFeet, input.CenterYFeet, input.CenterZFeet),
            BuildVolumeBucket(input.ApproximateVolumeMm3));

        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        string shortHash = BitConverter.ToString(hash, 0, 6).Replace("-", string.Empty);
        return "CM-" + shortHash;
    }

    private static string BuildEndpoint(long elementId, long? linkedElementId)
    {
        return linkedElementId.HasValue
            ? string.Format(CultureInfo.InvariantCulture, "LINK:{0}:{1}", elementId, linkedElementId.Value)
            : string.Format(CultureInfo.InvariantCulture, "MODEL:{0}", elementId);
    }

    private static string BuildLocationBucket(double xFeet, double yFeet, double zFeet)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}:{1}:{2}",
            RoundToBucket(xFeet * FeetToMillimeters),
            RoundToBucket(yFeet * FeetToMillimeters),
            RoundToBucket(zFeet * FeetToMillimeters));
    }

    private static string BuildVolumeBucket(double approximateVolumeMm3)
    {
        if (approximateVolumeMm3 <= 0 || double.IsNaN(approximateVolumeMm3) || double.IsInfinity(approximateVolumeMm3))
        {
            return "0";
        }

        return Math.Round(Math.Log10(approximateVolumeMm3), 1).ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static double RoundToBucket(double value)
    {
        return Math.Round(value / 100.0, MidpointRounding.AwayFromZero) * 100.0;
    }

    private static string NormalizeLabel(string value, string fallback)
    {
        string normalized = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string NormalizeToken(string value)
    {
        string normalized = NormalizeLabel(value, "Check").ToUpperInvariant();
        StringBuilder builder = new(normalized.Length);
        foreach (char character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.Length == 0 ? "CHECK" : builder.ToString();
    }
}
