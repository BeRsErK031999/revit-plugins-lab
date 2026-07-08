using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Models;

public sealed class FamilyFileItem : INotifyPropertyChanged
{
    private bool isFavorite;
    private string status = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FilePath { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string DirectoryPath { get; set; } = string.Empty;

    public string Category { get; set; } = FamilyManagerDefaults.UnknownCategory;

    public long SizeBytes { get; set; }

    public DateTimeOffset LastWriteTimeUtc { get; set; }

    public DateTimeOffset? LastLoadedAtUtc { get; set; }

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

    public string SizeDisplay => SizeBytes <= 0
        ? "-"
        : $"{Math.Round(SizeBytes / 1024d, 1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} KB";

    public string LastWriteDisplay => LastWriteTimeUtc == default
        ? "-"
        : LastWriteTimeUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.CurrentCulture);

    public string FavoriteDisplay => IsFavorite ? "Да" : "Нет";

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
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
