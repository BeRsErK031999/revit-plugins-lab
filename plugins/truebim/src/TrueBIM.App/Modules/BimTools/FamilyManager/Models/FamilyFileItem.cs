using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Models;

public sealed class FamilyFileItem : INotifyPropertyChanged
{
    private static readonly string[] WidthParameterTokens = ["Width", "Ширина"];
    private static readonly string[] HeightParameterTokens = ["Height", "Высота"];
    private static readonly string[] MaterialParameterTokens = ["Material", "Материал"];

    private bool isFavorite;
    private string searchMatchText = string.Empty;
    private string status = string.Empty;
    private string thumbnailPath = string.Empty;
    private DateTimeOffset? thumbnailUpdatedAtUtc;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FilePath { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string DirectoryPath { get; set; } = string.Empty;

    public string Category { get; set; } = FamilyManagerDefaults.UnknownCategory;

    public long SizeBytes { get; set; }

    public DateTimeOffset LastWriteTimeUtc { get; set; }

    public DateTimeOffset? LastLoadedAtUtc { get; set; }

    public DateTimeOffset? MetadataUpdatedAtUtc { get; set; }

    public string ThumbnailPath
    {
        get => thumbnailPath;
        set => SetField(ref thumbnailPath, value ?? string.Empty);
    }

    public DateTimeOffset? ThumbnailUpdatedAtUtc
    {
        get => thumbnailUpdatedAtUtc;
        set => SetField(ref thumbnailUpdatedAtUtc, value);
    }

    public List<FamilyTypeInfo> CachedTypes { get; set; } = [];

    public string TypeCatalogPath { get; set; } = string.Empty;

    public List<string> TypeCatalogTypeNames { get; set; } = [];

    public bool IsFavorite
    {
        get => isFavorite;
        set => SetField(ref isFavorite, value);
    }

    public string Status
    {
        get => status;
        set => SetField(ref status, value);
    }

    [JsonIgnore]
    public string SearchMatchText
    {
        get => searchMatchText;
        set => SetField(ref searchMatchText, value ?? string.Empty);
    }

    [JsonIgnore]
    public string SearchMatchDisplay => string.IsNullOrWhiteSpace(SearchMatchText) ? "-" : SearchMatchText;

    public string SizeDisplay => SizeBytes <= 0
        ? "-"
        : $"{Math.Round(SizeBytes / 1024d, 1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} KB";

    public string LastWriteDisplay => LastWriteTimeUtc == default
        ? "-"
        : LastWriteTimeUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.CurrentCulture);

    public string FavoriteDisplay => IsFavorite ? "Да" : "Нет";

    public string CachedTypeCountDisplay => CachedTypes.Count == 0
        ? "-"
        : CachedTypes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public bool HasTypeCatalog => !string.IsNullOrWhiteSpace(TypeCatalogPath);

    public string TypeCatalogDisplay => HasTypeCatalog
        ? TypeCatalogTypeNames.Count == 0
            ? "Есть"
            : TypeCatalogTypeNames.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "-";

    public string TypeCatalogDetailsDisplay => HasTypeCatalog
        ? TypeCatalogTypeNames.Count == 0
            ? $"Есть: {TypeCatalogPath}"
            : $"{TypeCatalogTypeNames.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} типов: {TypeCatalogPath}"
        : "Нет";

    public string MetadataDisplay => MetadataUpdatedAtUtc is null
        ? "Не обновлялись"
        : MetadataUpdatedAtUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.CurrentCulture);

    public string ThumbnailDisplay => ThumbnailUpdatedAtUtc is null
        ? "-"
        : ThumbnailUpdatedAtUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.CurrentCulture);

    public string WidthParameterDisplay => FindPresetParameterDisplay(WidthParameterTokens);

    public string HeightParameterDisplay => FindPresetParameterDisplay(HeightParameterTokens);

    public string MaterialParameterDisplay => FindPresetParameterDisplay(MaterialParameterTokens);

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName is nameof(IsFavorite))
        {
            OnPropertyChanged(nameof(FavoriteDisplay));
        }

        if (propertyName is nameof(ThumbnailPath) or nameof(ThumbnailUpdatedAtUtc))
        {
            OnPropertyChanged(nameof(ThumbnailDisplay));
        }

        if (propertyName is nameof(SearchMatchText))
        {
            OnPropertyChanged(nameof(SearchMatchDisplay));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string FindPresetParameterDisplay(IReadOnlyCollection<string> tokens)
    {
        List<string> values = CachedTypes
            .SelectMany(type => type.Parameters)
            .Where(parameter => ParameterNameMatches(parameter.Name, tokens))
            .Select(parameter => parameter.ValueDisplay)
            .Where(value => !string.IsNullOrWhiteSpace(value) && value != "-")
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Take(3)
            .ToList();

        return values.Count == 0
            ? "-"
            : string.Join(", ", values);
    }

    private static bool ParameterNameMatches(string parameterName, IEnumerable<string> tokens)
    {
        return tokens.Any(token =>
            parameterName.Equals(token, StringComparison.CurrentCultureIgnoreCase)
            || parameterName.IndexOf(token, StringComparison.CurrentCultureIgnoreCase) >= 0);
    }
}
