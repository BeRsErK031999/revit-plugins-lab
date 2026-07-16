using System.IO;
using Microsoft.Win32;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldFilePicker : IIsoFieldFilePicker
{
    private const string DialogFilter =
        "Файлы изополей (*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.json)|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.json|Изображения (*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|JSON (*.json)|*.json|Все файлы (*.*)|*.*";

    public IReadOnlyList<string> PickIsoFieldSourceFiles()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Выбрать JSON или комплект карт изополей",
            Filter = DialogFilter,
            Multiselect = true,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true
            ? dialog.FileNames
            : Array.Empty<string>();
    }

    public string? PickSourceSetManifestSavePath(string? initialDirectory, string? suggestedFileName)
    {
        SaveFileDialog dialog = new()
        {
            Title = "Сохранить manifest комплекта изополей",
            Filter = "Manifest комплекта (*.isofield-set.json)|*.isofield-set.json",
            FileName = string.IsNullOrWhiteSpace(suggestedFileName)
                ? IsoFieldSourceSetManifestService.DefaultManifestFileName
                : suggestedFileName,
            DefaultExt = ".isofield-set.json",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }
}
