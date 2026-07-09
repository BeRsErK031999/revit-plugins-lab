using System.Drawing;
using System.IO;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.FamilyManager.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Services;

public sealed class FamilyThumbnailService
{
    private const int PreviewSize = 256;

    private readonly FamilyThumbnailCacheService cacheService;

    public FamilyThumbnailService()
        : this(new FamilyThumbnailCacheService())
    {
    }

    public FamilyThumbnailService(FamilyThumbnailCacheService cacheService)
    {
        this.cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    }

    public string? TryGetCachedThumbnail(FamilyFileItem family)
    {
        return cacheService.TryGetCachedThumbnail(family);
    }

    public FamilyThumbnailResult Refresh(Application application, FamilyFileItem family, ITrueBimLogger logger)
    {
        Guard.NotNull(application, nameof(application));
        Guard.NotNull(family, nameof(family));
        Guard.NotNull(logger, nameof(logger));

        string normalizedPath = FamilyPathNormalizer.Normalize(family.FilePath);
        if (!File.Exists(normalizedPath))
        {
            return new FamilyThumbnailResult
            {
                Succeeded = false,
                Message = "Файл семейства не найден."
            };
        }

        Document? familyDocument = null;
        try
        {
            familyDocument = application.OpenDocumentFile(normalizedPath);
            if (!familyDocument.IsFamilyDocument)
            {
                return new FamilyThumbnailResult
                {
                    Succeeded = false,
                    Message = "Файл открыт, но не является family-документом."
                };
            }

            using Bitmap? preview = TryCreatePreviewBitmap(familyDocument, logger);
            if (preview is null)
            {
                return new FamilyThumbnailResult
                {
                    Succeeded = false,
                    Message = "Для выбранного семейства Revit не вернул preview."
                };
            }

            string thumbnailPath = cacheService.Save(family, preview);
            return new FamilyThumbnailResult
            {
                Succeeded = true,
                ThumbnailPath = thumbnailPath,
                Message = "Preview обновлен."
            };
        }
        catch (Exception exception)
        {
            logger.Warning($"Failed to refresh family thumbnail for '{normalizedPath}': {exception.Message}");
            return new FamilyThumbnailResult
            {
                Succeeded = false,
                Message = $"Не удалось обновить preview: {exception.Message}"
            };
        }
        finally
        {
            CloseWithoutSaving(familyDocument, normalizedPath, logger);
        }
    }

    private static Bitmap? TryCreatePreviewBitmap(Document familyDocument, ITrueBimLogger logger)
    {
        foreach (ElementType elementType in CollectPreviewTypes(familyDocument))
        {
            try
            {
                using Bitmap? preview = elementType.GetPreviewImage(new Size(PreviewSize, PreviewSize));
                if (preview is null)
                {
                    continue;
                }

                return new Bitmap(preview);
            }
            catch (Exception exception)
            {
                logger.Warning($"Failed to read preview image for element type '{elementType.Name}': {exception.Message}");
            }
        }

        return null;
    }

    private static IReadOnlyList<ElementType> CollectPreviewTypes(Document familyDocument)
    {
        HashSet<string> familyTypeNames = CollectFamilyTypeNames(familyDocument);
        return new FilteredElementCollector(familyDocument)
            .WhereElementIsElementType()
            .OfType<ElementType>()
            .OrderBy(elementType => elementType is FamilySymbol ? 0 : 1)
            .ThenBy(elementType => familyTypeNames.Contains(elementType.Name) ? 0 : 1)
            .ThenBy(elementType => elementType.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static HashSet<string> CollectFamilyTypeNames(Document familyDocument)
    {
        HashSet<string> result = new(StringComparer.CurrentCultureIgnoreCase);
        foreach (FamilyType familyType in familyDocument.FamilyManager.Types)
        {
            if (!string.IsNullOrWhiteSpace(familyType.Name))
            {
                result.Add(familyType.Name.Trim());
            }
        }

        return result;
    }

    private static void CloseWithoutSaving(Document? familyDocument, string normalizedPath, ITrueBimLogger logger)
    {
        if (familyDocument is null)
        {
            return;
        }

        try
        {
            familyDocument.Close(false);
        }
        catch (Exception exception)
        {
            logger.Warning($"Failed to close family thumbnail document '{normalizedPath}': {exception.Message}");
        }
    }
}
