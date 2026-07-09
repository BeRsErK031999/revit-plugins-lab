using System.IO;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.FamilyManager.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Services;

public sealed class FamilyMetadataService
{
    private readonly FamilyTypeCatalogReader typeCatalogReader;

    public FamilyMetadataService()
        : this(new FamilyTypeCatalogReader())
    {
    }

    public FamilyMetadataService(FamilyTypeCatalogReader typeCatalogReader)
    {
        this.typeCatalogReader = typeCatalogReader ?? throw new ArgumentNullException(nameof(typeCatalogReader));
    }

    public FamilyMetadataResult Read(Application application, string filePath, ITrueBimLogger logger)
    {
        return Read(application, displayUnitsDocument: null, filePath, logger);
    }

    public FamilyMetadataResult Read(Application application, Document? displayUnitsDocument, string filePath, ITrueBimLogger logger)
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

            Units displayUnits = displayUnitsDocument?.GetUnits() ?? familyDocument.GetUnits();
            string category = familyDocument.OwnerFamily?.FamilyCategory?.Name ?? FamilyManagerDefaults.UnknownCategory;
            List<FamilyTypeInfo> types = CollectTypes(familyDocument, displayUnits);
            FamilyTypeCatalogInfo typeCatalog = typeCatalogReader.ReadForFamily(normalizedPath);
            return new FamilyMetadataResult
            {
                Succeeded = true,
                Category = string.IsNullOrWhiteSpace(category) ? FamilyManagerDefaults.UnknownCategory : category,
                Types = types,
                TypeCatalogPath = typeCatalog.Path,
                TypeCatalogTypeNames = typeCatalog.TypeNames.ToList(),
                Message = typeCatalog.Exists
                    ? $"Метаданные обновлены. Типов в файле: {types.Count}. Каталог типов: {typeCatalog.TypeNames.Count}."
                    : $"Метаданные обновлены. Типов в файле: {types.Count}."
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

    private static List<FamilyTypeInfo> CollectTypes(Document familyDocument, Units displayUnits)
    {
        List<FamilyTypeInfo> types = [];
        FamilyTypeSet familyTypes = familyDocument.FamilyManager.Types;
        List<FamilyParameter> parameters = CollectParameters(familyDocument.FamilyManager);
        foreach (FamilyType familyType in familyTypes)
        {
            if (string.IsNullOrWhiteSpace(familyType.Name))
            {
                continue;
            }

            types.Add(new FamilyTypeInfo(
                0,
                familyType.Name.Trim(),
                CollectTypeParameters(familyType, parameters, displayUnits)));
        }

        return types
            .GroupBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group.First())
            .OrderBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static List<FamilyParameter> CollectParameters(Autodesk.Revit.DB.FamilyManager familyManager)
    {
        List<FamilyParameter> parameters = [];
        foreach (FamilyParameter parameter in familyManager.Parameters)
        {
            string? name = parameter.Definition?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            parameters.Add(parameter);
        }

        return parameters
            .OrderBy(parameter => parameter.IsInstance ? 1 : 0)
            .ThenBy(parameter => parameter.Definition?.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static List<FamilyTypeParameterInfo> CollectTypeParameters(
        FamilyType familyType,
        IReadOnlyList<FamilyParameter> parameters,
        Units displayUnits)
    {
        List<FamilyTypeParameterInfo> result = [];
        foreach (FamilyParameter parameter in parameters)
        {
            string? name = parameter.Definition?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string parameterName = name!;
            result.Add(new FamilyTypeParameterInfo(
                parameterName,
                ReadParameterValue(familyType, parameter, displayUnits),
                parameter.StorageType.ToString(),
                parameter.IsInstance ? "Экземпляр" : "Тип",
                parameter.Formula ?? string.Empty));
        }

        return result
            .GroupBy(parameter => $"{parameter.Scope}:{parameter.Name}", StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group.First())
            .OrderBy(parameter => parameter.Scope, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(parameter => parameter.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string ReadParameterValue(FamilyType familyType, FamilyParameter parameter, Units displayUnits)
    {
        try
        {
            return parameter.StorageType switch
            {
                StorageType.String => familyType.AsString(parameter) ?? string.Empty,
                StorageType.Integer => FormatNullableInteger(familyType.AsInteger(parameter)),
                StorageType.Double => FormatNullableDouble(familyType.AsDouble(parameter), parameter, displayUnits),
                StorageType.ElementId => FormatElementId(familyType.AsElementId(parameter)),
                _ => string.Empty
            };
        }
        catch (Exception exception) when (exception is Autodesk.Revit.Exceptions.ArgumentException
            or Autodesk.Revit.Exceptions.InvalidOperationException
            or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static string FormatNullableInteger(int? value)
    {
        return value.HasValue
            ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string FormatNullableDouble(double? value, FamilyParameter parameter, Units displayUnits)
    {
        if (!value.HasValue)
        {
            return string.Empty;
        }

#if REVIT2022_OR_GREATER
        try
        {
            ForgeTypeId dataType = parameter.Definition?.GetDataType() ?? new ForgeTypeId();
            if (UnitUtils.IsMeasurableSpec(dataType))
            {
                return UnitFormatUtils.Format(displayUnits, dataType, value.Value, forEditing: false);
            }
        }
        catch (Exception exception) when (exception is Autodesk.Revit.Exceptions.ArgumentException
            or Autodesk.Revit.Exceptions.ArgumentNullException
            or Autodesk.Revit.Exceptions.InvalidOperationException
            or ArgumentException
            or ArgumentNullException
            or InvalidOperationException)
        {
            return FormatInvariantDouble(value.Value);
        }
#endif

        return FormatInvariantDouble(value.Value);
    }

    private static string FormatInvariantDouble(double value)
    {
        return value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FormatElementId(ElementId? value)
    {
        if (value is null || value == ElementId.InvalidElementId)
        {
            return string.Empty;
        }

        return RevitElementIds.GetValue(value).ToString(System.Globalization.CultureInfo.InvariantCulture);
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
