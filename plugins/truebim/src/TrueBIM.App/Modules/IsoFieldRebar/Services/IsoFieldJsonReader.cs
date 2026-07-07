using System.IO;
using System.Text;
using System.Text.Json;
using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldJsonReader : IIsoFieldJsonReader
{
    private const string SupportedSchemaVersion = "1.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public IsoFieldRecognitionResult Read(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("JSON file path is required.", nameof(filePath));
        }

        string json = File.ReadAllText(filePath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("IsoField recognition JSON is empty.");
        }

        RecognitionContract contract;
        try
        {
            contract = JsonSerializer.Deserialize<RecognitionContract>(json, JsonOptions)
                ?? throw new InvalidDataException("IsoField recognition JSON root object is missing.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("IsoField recognition JSON is not valid.", exception);
        }

        ValidateSchemaVersion(contract.SchemaVersion);

        if (contract.Polylines is null)
        {
            throw new InvalidDataException("IsoField recognition JSON must contain a polylines array.");
        }

        List<IsoFieldPolyline> polylines = new();
        for (int index = 0; index < contract.Polylines.Count; index++)
        {
            polylines.Add(MapPolyline(contract.Polylines[index], index));
        }

        IReadOnlyList<string> diagnostics = contract.Diagnostics?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .ToArray() ?? Array.Empty<string>();

        return new IsoFieldRecognitionResult(polylines, diagnostics);
    }

    private static void ValidateSchemaVersion(string? schemaVersion)
    {
        if (!string.Equals(schemaVersion, SupportedSchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported IsoField recognition schemaVersion '{schemaVersion ?? "<missing>"}'. Expected '{SupportedSchemaVersion}'.");
        }
    }

    private static IsoFieldPolyline MapPolyline(PolylineContract polyline, int index)
    {
        string polylineId = (polyline.Id ?? string.Empty).Trim();
        if (polylineId.Length == 0)
        {
            throw new InvalidDataException($"IsoField polyline at index {index} must contain a non-empty id.");
        }

        List<PointContract>? pointContracts = polyline.Points;

        if (pointContracts is null || pointContracts.Count < 2)
        {
            throw new InvalidDataException($"IsoField polyline '{polylineId}' must contain at least two points.");
        }

        List<IsoFieldPoint> points = new();
        for (int pointIndex = 0; pointIndex < pointContracts.Count; pointIndex++)
        {
            points.Add(MapPoint(polylineId, pointContracts[pointIndex], pointIndex));
        }

        string? zoneName = polyline.ZoneName;
        string? normalizedZoneName = string.IsNullOrWhiteSpace(zoneName) ? null : zoneName!.Trim();

        return new IsoFieldPolyline(
            polylineId,
            points,
            normalizedZoneName,
            polyline.Confidence);
    }

    private static IsoFieldPoint MapPoint(string polylineId, PointContract point, int pointIndex)
    {
        if (!point.X.HasValue || !point.Y.HasValue)
        {
            throw new InvalidDataException($"IsoField point {pointIndex} in polyline '{polylineId}' must contain x and y.");
        }

        if (!IsFinite(point.X.Value) || !IsFinite(point.Y.Value))
        {
            throw new InvalidDataException($"IsoField point {pointIndex} in polyline '{polylineId}' must contain finite coordinates.");
        }

        return new IsoFieldPoint(point.X.Value, point.Y.Value);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private sealed class RecognitionContract
    {
        public string? SchemaVersion { get; set; }

        public List<PolylineContract>? Polylines { get; set; }

        public List<string>? Diagnostics { get; set; }
    }

    private sealed class PolylineContract
    {
        public string? Id { get; set; }

        public string? ZoneName { get; set; }

        public double? Confidence { get; set; }

        public List<PointContract>? Points { get; set; }
    }

    private sealed class PointContract
    {
        public double? X { get; set; }

        public double? Y { get; set; }
    }
}
