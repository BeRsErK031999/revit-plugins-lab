using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static PrintSettings DefaultSettings { get; } = new(
        ExportFolder: null,
        FileNameMask: PrintFileNameTemplateService.DefaultTemplate,
        IncludePlaceholders: false,
        ExportPdf: true,
        CombinePdf: false,
        CombinedPdfFileName: PrintPdfExportService.BuildCombinedPdfFileName(null),
        PdfColorMode: PrintPdfExportService.DefaultSettings.ColorMode,
        PdfRasterQuality: PrintPdfExportService.DefaultSettings.RasterQuality,
        AlwaysUseRasterPdf: PrintPdfExportService.DefaultSettings.AlwaysUseRaster,
        ExportDwg: false,
        ExportDxf: false,
        ExportDwf: false,
        CombineDwg: false,
        ExportSeparatePdfWithCombined: false,
        DwgSetupName: null,
        DxfSetupName: null);

    private readonly string settingsPath;
    private readonly ITrueBimLogger logger;

    public PrintSettingsService(string settingsPath, ITrueBimLogger logger)
    {
        Guard.NotNullOrWhiteSpace(settingsPath, nameof(settingsPath));
        this.settingsPath = settingsPath;
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool SettingsFileExists => File.Exists(settingsPath);

    public static string CreateSettingsPath(string revitVersion)
    {
        Guard.NotNullOrWhiteSpace(revitVersion, nameof(revitVersion));

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "TrueBIM", revitVersion, "print-settings.json");
    }

    public PrintSettings Load()
    {
        if (!File.Exists(settingsPath))
        {
            logger.Info($"Print settings file was not found. Defaults will be used: '{settingsPath}'.");
            return DefaultSettings;
        }

        try
        {
            PrintSettingsDto dto = JsonSerializer.Deserialize<PrintSettingsDto>(
                File.ReadAllText(settingsPath),
                SerializerOptions) ?? new PrintSettingsDto();

            return Normalize(FromDto(dto));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.Warning($"Failed to read print settings '{settingsPath}'. Defaults will be used: {exception.Message}");
            return DefaultSettings;
        }
    }

    public void Save(PrintSettings settings)
    {
        Guard.NotNull(settings, nameof(settings));

        try
        {
            string? directory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            PrintSettings normalizedSettings = Normalize(settings);
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(ToDto(normalizedSettings), SerializerOptions));
            logger.Info($"Print settings saved: '{settingsPath}'.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Warning($"Failed to save print settings '{settingsPath}': {exception.Message}");
        }
    }

    public static PrintSettings Normalize(PrintSettings settings)
    {
        Guard.NotNull(settings, nameof(settings));

        return new PrintSettings(
            ExportFolder: NormalizeOptionalText(settings.ExportFolder),
            FileNameMask: string.IsNullOrWhiteSpace(settings.FileNameMask)
                ? DefaultSettings.FileNameMask
                : settings.FileNameMask.Trim(),
            IncludePlaceholders: settings.IncludePlaceholders,
            ExportPdf: settings.ExportPdf,
            CombinePdf: settings.CombinePdf,
            CombinedPdfFileName: PrintPdfExportService.BuildCombinedPdfFileName(settings.CombinedPdfFileName),
            PdfColorMode: Enum.IsDefined(typeof(PrintPdfColorMode), settings.PdfColorMode)
                ? settings.PdfColorMode
                : DefaultSettings.PdfColorMode,
            PdfRasterQuality: Enum.IsDefined(typeof(PrintPdfRasterQuality), settings.PdfRasterQuality)
                ? settings.PdfRasterQuality
                : DefaultSettings.PdfRasterQuality,
            AlwaysUseRasterPdf: settings.AlwaysUseRasterPdf,
            ExportDwg: settings.ExportDwg,
            ExportDxf: settings.ExportDxf,
            ExportDwf: settings.ExportDwf,
            CombineDwg: settings.CombineDwg,
            ExportSeparatePdfWithCombined: settings.ExportSeparatePdfWithCombined,
            DwgSetupName: PrintCadExportSetupService.NormalizeSetupName(settings.DwgSetupName),
            DxfSetupName: PrintCadExportSetupService.NormalizeSetupName(settings.DxfSetupName));
    }

    private static PrintSettings FromDto(PrintSettingsDto dto)
    {
        return new PrintSettings(
            dto.ExportFolder,
            dto.FileNameMask ?? DefaultSettings.FileNameMask,
            dto.IncludePlaceholders ?? DefaultSettings.IncludePlaceholders,
            dto.ExportPdf ?? DefaultSettings.ExportPdf,
            dto.CombinePdf ?? DefaultSettings.CombinePdf,
            dto.CombinedPdfFileName ?? DefaultSettings.CombinedPdfFileName,
            dto.PdfColorMode ?? DefaultSettings.PdfColorMode,
            dto.PdfRasterQuality ?? DefaultSettings.PdfRasterQuality,
            dto.AlwaysUseRasterPdf ?? DefaultSettings.AlwaysUseRasterPdf,
            dto.ExportDwg ?? DefaultSettings.ExportDwg,
            dto.ExportDxf ?? DefaultSettings.ExportDxf,
            dto.ExportDwf ?? DefaultSettings.ExportDwf,
            dto.CombineDwg ?? DefaultSettings.CombineDwg,
            dto.ExportSeparatePdfWithCombined ?? DefaultSettings.ExportSeparatePdfWithCombined,
            dto.DwgSetupName,
            dto.DxfSetupName);
    }

    private static PrintSettingsDto ToDto(PrintSettings settings)
    {
        return new PrintSettingsDto
        {
            ExportFolder = settings.ExportFolder,
            FileNameMask = settings.FileNameMask,
            IncludePlaceholders = settings.IncludePlaceholders,
            ExportPdf = settings.ExportPdf,
            CombinePdf = settings.CombinePdf,
            CombinedPdfFileName = settings.CombinedPdfFileName,
            PdfColorMode = settings.PdfColorMode,
            PdfRasterQuality = settings.PdfRasterQuality,
            AlwaysUseRasterPdf = settings.AlwaysUseRasterPdf,
            ExportDwg = settings.ExportDwg,
            ExportDxf = settings.ExportDxf,
            ExportDwf = settings.ExportDwf,
            CombineDwg = settings.CombineDwg,
            ExportSeparatePdfWithCombined = settings.ExportSeparatePdfWithCombined,
            DwgSetupName = settings.DwgSetupName,
            DxfSetupName = settings.DxfSetupName
        };
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value!.Trim();
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record PrintSettingsDto
    {
        [JsonPropertyName("exportFolder")]
        public string? ExportFolder { get; init; }

        [JsonPropertyName("fileNameMask")]
        public string? FileNameMask { get; init; }

        [JsonPropertyName("includePlaceholders")]
        public bool? IncludePlaceholders { get; init; }

        [JsonPropertyName("exportPdf")]
        public bool? ExportPdf { get; init; }

        [JsonPropertyName("combinePdf")]
        public bool? CombinePdf { get; init; }

        [JsonPropertyName("combinedPdfFileName")]
        public string? CombinedPdfFileName { get; init; }

        [JsonPropertyName("pdfColorMode")]
        public PrintPdfColorMode? PdfColorMode { get; init; }

        [JsonPropertyName("pdfRasterQuality")]
        public PrintPdfRasterQuality? PdfRasterQuality { get; init; }

        [JsonPropertyName("alwaysUseRasterPdf")]
        public bool? AlwaysUseRasterPdf { get; init; }

        [JsonPropertyName("exportDwg")]
        public bool? ExportDwg { get; init; }

        [JsonPropertyName("exportDxf")]
        public bool? ExportDxf { get; init; }

        [JsonPropertyName("exportDwf")]
        public bool? ExportDwf { get; init; }

        [JsonPropertyName("combineDwg")]
        public bool? CombineDwg { get; init; }

        [JsonPropertyName("exportSeparatePdfWithCombined")]
        public bool? ExportSeparatePdfWithCombined { get; init; }

        [JsonPropertyName("dwgSetupName")]
        public string? DwgSetupName { get; init; }

        [JsonPropertyName("dxfSetupName")]
        public string? DxfSetupName { get; init; }
    }
}
