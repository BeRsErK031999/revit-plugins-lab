using System.IO;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Lintels.Models;
using TrueBIM.App.Modules.Lintels.Services;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.Lintels.Revit;

public sealed class LintelTypeImageService
{
    private const int ExportPixelWidth = 1600;
    private readonly ITrueBimLogger logger;

    public LintelTypeImageService(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public LintelTypeImageResult ExportAndAssign(
        Document document,
        View view,
        long? lintelTypeId,
        string imageFileName)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (view is null)
        {
            throw new ArgumentNullException(nameof(view));
        }

        ExportedImage exportedImage;
        try
        {
            exportedImage = ExportView(document, view, imageFileName);
        }
        catch (Exception exception)
        {
            string message = $"Не удалось экспортировать оформленный вид в PNG: {exception.Message}";
            logger.Warning(message);
            return LintelTypeImageResult.Failed(message);
        }

        if (lintelTypeId is not > 0)
        {
            string message =
                "PNG экспортирован, но исходный типоразмер перемычки не определён; параметр «Изображение типоразмера» не изменён.";
            logger.Warning(message);
            return LintelTypeImageResult.Failed(message, true, exportedImage.FilePath);
        }

        if (document.IsReadOnly)
        {
            return LintelTypeImageResult.Failed(
                "PNG экспортирован, но документ Revit доступен только для чтения; параметр «Изображение типоразмера» не изменён.",
                true,
                exportedImage.FilePath);
        }

        using Transaction transaction = new(document, "TrueBIM: изображение типоразмера перемычки");
        try
        {
            EnsureStatus(
                transaction.Start(),
                TransactionStatus.Started,
                "Revit не начал транзакцию назначения изображения типоразмера.");

            Element? lintelType = document.GetElement(RevitElementIds.Create(lintelTypeId.Value));
            if (lintelType is not ElementType)
            {
                throw new InvalidOperationException(
                    $"Элемент типа перемычки ID {lintelTypeId.Value} не найден.");
            }

            Parameter? typeImageParameter = lintelType.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_IMAGE);
            if (typeImageParameter is null)
            {
                throw new InvalidOperationException(
                    "У исходного типоразмера отсутствует встроенный параметр «Изображение типоразмера».");
            }

            if (typeImageParameter.IsReadOnly)
            {
                throw new InvalidOperationException(
                    "Встроенный параметр «Изображение типоразмера» недоступен для записи.");
            }

            if (typeImageParameter.StorageType != StorageType.ElementId)
            {
                throw new InvalidOperationException(
                    "Встроенный параметр «Изображение типоразмера» имеет неподдерживаемый тип данных.");
            }

            ImageType? existingImageType = ResolveExistingImageType(
                document,
                typeImageParameter,
                exportedImage.FilePath);
            ImageType imageType;
#if REVIT2020_OR_GREATER
#if REVIT2021_OR_GREATER
            using ImageTypeOptions imageOptions = new(
                exportedImage.FilePath,
                false,
                ImageTypeSource.Import);
#else
            using ImageTypeOptions imageOptions = new(
                exportedImage.FilePath,
                false);
#endif
            if (!imageOptions.IsValid(document))
            {
                throw new InvalidOperationException(
                    "Revit не принимает экспортированный PNG для импорта в проект.");
            }

            if (existingImageType is null)
            {
                imageType = ImageType.Create(document, imageOptions);
            }
            else
            {
                existingImageType.ReloadFrom(imageOptions);
                imageType = existingImageType;
            }
#else
            if (existingImageType is null)
            {
                imageType = ImageType.Create(document, exportedImage.FilePath);
            }
            else
            {
                existingImageType.ReloadFrom(exportedImage.FilePath);
                imageType = existingImageType;
            }
#endif

            if (!typeImageParameter.AsElementId().Equals(imageType.Id)
                && !typeImageParameter.Set(imageType.Id))
            {
                throw new InvalidOperationException(
                    "Revit отклонил назначение PNG параметру «Изображение типоразмера».");
            }

            document.Regenerate();

            EnsureStatus(
                transaction.Commit(),
                TransactionStatus.Committed,
                "Revit откатил транзакцию назначения изображения типоразмера.");

            LintelTypeImageResult result = new(
                true,
                true,
                exportedImage.FilePath,
                exportedImage.PixelWidth,
                exportedImage.PixelHeight,
                ["PNG импортирован в проект и назначен встроенному параметру типа «Изображение типоразмера»."]);
            logger.Info(
                $"Lintels type image assigned. TypeId={lintelTypeId.Value}; View='{view.Name}'; "
                + $"Image='{exportedImage.FilePath}'; Pixels={exportedImage.PixelWidth}x{exportedImage.PixelHeight}; "
                + $"ImageTypeId={RevitElementIds.GetValue(imageType.Id)}.");
            return result;
        }
        catch (Exception exception)
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            string message =
                $"PNG экспортирован, но не назначен параметру «Изображение типоразмера»: {exception.Message}";
            logger.Warning(message);
            return LintelTypeImageResult.Failed(message, true, exportedImage.FilePath);
        }
    }

    private static ExportedImage ExportView(
        Document document,
        View view,
        string imageFileName)
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }

        string projectName = !string.IsNullOrWhiteSpace(document.PathName)
            ? Path.GetFileNameWithoutExtension(document.PathName)
            : document.Title;
        string imageFilePath = LintelTypeImagePathBuilder.Build(
            localAppData,
            projectName,
            imageFileName);
        string outputDirectory = Path.GetDirectoryName(imageFilePath)
            ?? throw new InvalidOperationException("Не удалось определить каталог PNG.");
        Directory.CreateDirectory(outputDirectory);

        string temporaryDirectory = Path.Combine(
            outputDirectory,
            $".export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            using ImageExportOptions options = new()
            {
                ExportRange = ExportRange.SetOfViews,
                FilePath = Path.Combine(temporaryDirectory, "lintel"),
                FitDirection = FitDirectionType.Horizontal,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ShadowViewsFileType = ImageFileType.PNG,
                ImageResolution = ImageResolution.DPI_150,
                PixelSize = ExportPixelWidth,
                ZoomType = ZoomFitType.FitToPage,
                ShouldCreateWebSite = false
            };
            options.SetViewsAndSheets([view.Id]);
            document.ExportImage(options);

            string? exportedFilePath = Directory
                .EnumerateFiles(temporaryDirectory, "*.png", SearchOption.AllDirectories)
                .OrderByDescending(path => new FileInfo(path).Length)
                .FirstOrDefault();
            if (exportedFilePath is null)
            {
                throw new InvalidOperationException("Revit не создал PNG-файл.");
            }

            FileInfo exportedFile = new(exportedFilePath);
            if (exportedFile.Length == 0)
            {
                throw new InvalidOperationException("Revit создал пустой PNG-файл.");
            }

            File.Copy(exportedFilePath, imageFilePath, true);
            (int pixelWidth, int pixelHeight) = ReadPngSize(imageFilePath);
            if (pixelWidth != ExportPixelWidth || pixelHeight <= 0)
            {
                throw new InvalidOperationException(
                    $"Revit создал PNG неожиданного размера {pixelWidth} × {pixelHeight} px вместо ширины {ExportPixelWidth} px.");
            }

            return new ExportedImage(imageFilePath, pixelWidth, pixelHeight);
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryDirectory))
                {
                    Directory.Delete(temporaryDirectory, true);
                }
            }
            catch
            {
            }
        }
    }

    private static (int Width, int Height) ReadPngSize(string filePath)
    {
        byte[] header = new byte[24];
        using FileStream stream = File.OpenRead(filePath);
        int read = stream.Read(header, 0, header.Length);
        if (read != header.Length
            || header[0] != 137
            || header[1] != 80
            || header[2] != 78
            || header[3] != 71
            || header[12] != 73
            || header[13] != 72
            || header[14] != 68
            || header[15] != 82)
        {
            throw new InvalidOperationException("Revit создал файл с некорректным PNG-заголовком.");
        }

        int width = ReadBigEndianInt32(header, 16);
        int height = ReadBigEndianInt32(header, 20);
        return (width, height);
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset)
    {
        return (bytes[offset] << 24)
            | (bytes[offset + 1] << 16)
            | (bytes[offset + 2] << 8)
            | bytes[offset + 3];
    }

    private static ImageType? ResolveExistingImageType(
        Document document,
        Parameter typeImageParameter,
        string imageFilePath)
    {
        ElementId currentImageTypeId = typeImageParameter.AsElementId();
        if (currentImageTypeId != ElementId.InvalidElementId
            && document.GetElement(currentImageTypeId) is ImageType currentImageType
            && PathsEqual(currentImageType.Path, imageFilePath))
        {
            return currentImageType;
        }

        return new FilteredElementCollector(document)
            .OfClass(typeof(ImageType))
            .Cast<ImageType>()
            .FirstOrDefault(imageType => PathsEqual(imageType.Path, imageFilePath));
    }

    private static bool PathsEqual(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(first),
                Path.GetFullPath(second),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void EnsureStatus(
        TransactionStatus actual,
        TransactionStatus expected,
        string message)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException($"{message} Status={actual}.");
        }
    }

    private sealed record ExportedImage(
        string FilePath,
        int PixelWidth,
        int PixelHeight);
}
