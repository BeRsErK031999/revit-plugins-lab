using System.IO;
using Microsoft.Win32;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldFilePicker : IIsoFieldFilePicker
{
    private const string DialogFilter =
        "Карты или готовые зоны|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.json|Карты изополей|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|Готовые зоны|*.json|Все файлы|*.*";

    public IReadOnlyList<string> PickIsoFieldSourceFiles()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Выбрать четыре карты или готовые зоны",
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
            Title = "Сохранить комплект карт изополей",
            Filter = "Сохранённый комплект TrueBIM|*.isofield-set.json",
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

    public string? PickRebarReportSavePath(string? initialDirectory, string? suggestedFileName)
    {
        SaveFileDialog dialog = new()
        {
            Title = "Сохранить отчёт армирования по изополям",
            Filter = "Отчёт TrueBIM|*.json",
            FileName = string.IsNullOrWhiteSpace(suggestedFileName)
                ? IsoFieldRebarReportService.DefaultFileNamePrefix + ".json"
                : suggestedFileName,
            DefaultExt = ".json",
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
