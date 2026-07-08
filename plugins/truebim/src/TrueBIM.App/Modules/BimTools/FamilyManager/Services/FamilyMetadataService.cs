using System.IO;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.FamilyManager.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Services;

public sealed class FamilyMetadataService
{
    public FamilyMetadataResult Read(Application application, string filePath, ITrueBimLogger logger)
    {
        Guard.NotNull(application, nameof(application));
        Guard.NotNullOrWhiteSpace(filePath, nameof(filePath));
        Guard.NotNull(logger, nameof(logger));

        string normalizedPath = FamilyPathNormalizer.Normalize(filePath);
        if (!File.Exists(normalizedPath))
        {
            return new FamilyMetadataResult
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
                return new FamilyMetadataResult
                {
                    Succeeded = false,
                    Message = "Файл открыт, но не является family-документом."
                };
            }

            string category = familyDocument.OwnerFamily?.FamilyCategory?.Name ?? FamilyManagerDefaults.UnknownCategory;
            List<FamilyTypeInfo> types = CollectTypes(familyDocument);
            return new FamilyMetadataResult
            {
                Succeeded = true,
                Category = string.IsNullOrWhiteSpace(category) ? FamilyManagerDefaults.UnknownCategory : category,
                Types = types,
                Message = $"Метаданные обновлены. Типов в файле: {types.Count}."
            };
        }
        catch (Exception exception)
        {
            logger.Warning($"Failed to read family metadata for '{normalizedPath}': {exception.Message}");
            return new FamilyMetadataResult
            {
                Succeeded = false,
                Message = $"Не удалось прочитать метаданные: {exception.Message}"
            };
        }
        finally
        {
            CloseWithoutSaving(familyDocument, normalizedPath, logger);
        }
    }

    private static List<FamilyTypeInfo> CollectTypes(Document familyDocument)
    {
        List<FamilyTypeInfo> types = [];
        FamilyTypeSet familyTypes = familyDocument.FamilyManager.Types;
        foreach (FamilyType familyType in familyTypes)
        {
            if (string.IsNullOrWhiteSpace(familyType.Name))
            {
                continue;
            }

            types.Add(new FamilyTypeInfo(0, familyType.Name.Trim()));
        }

        return types
            .GroupBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group.First())
            .OrderBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
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
            logger.Warning($"Failed to close family metadata document '{normalizedPath}': {exception.Message}");
        }
    }
}
