using Microsoft.Win32;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldFilePicker : IIsoFieldFilePicker
{
    private const string DialogFilter =
        "Файлы изополей (*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.json)|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.json|Изображения (*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|JSON (*.json)|*.json|Все файлы (*.*)|*.*";

    public string? PickIsoFieldSourceFile()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Выбрать файл изополей",
            Filter = DialogFilter,
            Multiselect = false,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }
}
