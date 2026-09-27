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
            throw new ArgumentException("Не указан путь к файлу с готовыми зонами.", nameof(filePath));
        }

        string json = File.ReadAllText(filePath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("Файл с готовыми зонами пуст.");
        }

        RecognitionContract contract;
        try
        {
            contract = JsonSerializer.Deserialize<RecognitionContract>(json, JsonOptions)
                ?? throw new InvalidDataException("В файле не найдены данные о готовых зонах.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Не удалось прочитать файл с готовыми зонами. Проверьте, что выбран правильный файл.", exception);
        }

        ValidateSchemaVersion(contract.SchemaVersion);

        if (contract.Polylines is null)
        {
            throw new InvalidDataException("В файле отсутствует список границ зон.");
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
            throw new InvalidDataException($"Версия файла с готовыми зонами не поддерживается. Нужна версия {SupportedSchemaVersion}.");
        }
    }

    private static IsoFieldPolyline MapPolyline(PolylineContract polyline, int index)
    {
        string polylineId = (polyline.Id ?? string.Empty).Trim();
        if (polylineId.Length == 0)
        {
            throw new InvalidDataException($"У зоны № {index + 1} нет обозначения.");
        }

        List<PointContract>? pointContracts = polyline.Points;

        if (pointContracts is null || pointContracts.Count < 2)
        {
            throw new InvalidDataException($"Граница зоны «{polylineId}» должна содержать не менее двух точек.");
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
            polyline.Confidence,
            ParseLayerRole(polyline.LayerRole, polylineId));
    }

    private static IsoFieldLayerRole? ParseLayerRole(string? layerRole, string polylineId)
    {
        if (string.IsNullOrWhiteSpace(layerRole))
        {
            return null;
        }

        if (Enum.TryParse(layerRole!.Trim(), ignoreCase: true, out IsoFieldLayerRole role)
            && Enum.IsDefined(typeof(IsoFieldLayerRole), role))
        {
            return role;
        }

        throw new InvalidDataException(
            $"Для зоны «{polylineId}» указано неизвестное назначение карты «{layerRole}».");
    }

    private static IsoFieldPoint MapPoint(string polylineId, PointContract point, int pointIndex)
    {
        if (!point.X.HasValue || !point.Y.HasValue)
        {
            throw new InvalidDataException($"Точка № {pointIndex + 1} границы зоны «{polylineId}» задана не полностью.");
        }

        if (!IsFinite(point.X.Value) || !IsFinite(point.Y.Value))
        {
            throw new InvalidDataException($"Точка № {pointIndex + 1} границы зоны «{polylineId}» задана неверно.");
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

        public string? LayerRole { get; set; }

        public List<PointContract>? Points { get; set; }
    }

    private sealed class PointContract
    {
        public double? X { get; set; }

        public double? Y { get; set; }
    }
}
