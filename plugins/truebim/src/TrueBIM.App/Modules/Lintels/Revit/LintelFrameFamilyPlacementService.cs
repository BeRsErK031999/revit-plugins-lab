using System.IO;
using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.Lintels.Revit;

internal sealed class LintelFrameFamilyPlacementService
{
    private string? resolvedFamilyPath;
    private string? resolvedFamilyUniqueId;

    public FamilyInstance Place(
        Document document,
        View view,
        string familyFilePath,
        XYZ insertionPoint)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (view is null)
        {
            throw new ArgumentNullException(nameof(view));
        }

        if (string.IsNullOrWhiteSpace(familyFilePath))
        {
            throw new ArgumentException("Frame family file path is required.", nameof(familyFilePath));
        }

        string normalizedPath = Path.GetFullPath(familyFilePath);
        if (!File.Exists(normalizedPath))
        {
            throw new FileNotFoundException("Выбранный файл семейства рамки не найден.", normalizedPath);
        }

        if (!string.Equals(Path.GetExtension(normalizedPath), ".rfa", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Для рамки требуется файл загружаемого семейства Revit с расширением .rfa.");
        }

        Family family = ResolveOrLoadFamily(document, normalizedPath);
        FamilySymbol symbol = family.GetFamilySymbolIds()
            .Select(document.GetElement)
            .OfType<FamilySymbol>()
            .OrderBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"В семействе «{family.Name}» нет доступного типоразмера для размещения.");

        if (!symbol.IsActive)
        {
            symbol.Activate();
            document.Regenerate();
        }

        try
        {
            return document.Create.NewFamilyInstance(insertionPoint, symbol, view);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Семейство «{family.Name} : {symbol.Name}» нельзя разместить как аннотацию на боковом виде. "
                + "Выберите семейство категории «Типовая аннотация» с точкой вставки в центре рамки.",
                exception);
        }
    }

    private Family ResolveOrLoadFamily(Document document, string familyFilePath)
    {
        if (string.Equals(
                resolvedFamilyPath,
                familyFilePath,
                StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(resolvedFamilyUniqueId)
            && document.GetElement(resolvedFamilyUniqueId) is Family resolvedFamily)
        {
            return resolvedFamily;
        }

        string expectedFamilyName = Path.GetFileNameWithoutExtension(familyFilePath);
        Family? existing = FindFamily(document, expectedFamilyName);
        if (existing is not null)
        {
            Remember(familyFilePath, existing);
            return existing;
        }

        document.LoadFamily(familyFilePath, out Family loadedFamily);
        if (loadedFamily is not null)
        {
            Remember(familyFilePath, loadedFamily);
            return loadedFamily;
        }

        existing = FindFamily(document, expectedFamilyName);
        if (existing is null)
        {
            throw new InvalidOperationException(
                $"Revit не загрузил семейство рамки «{expectedFamilyName}». Проверьте совместимость файла .rfa с текущей версией Revit.");
        }

        Remember(familyFilePath, existing);
        return existing;
    }

    private void Remember(string familyFilePath, Family family)
    {
        resolvedFamilyPath = familyFilePath;
        resolvedFamilyUniqueId = family.UniqueId;
    }

    private static Family? FindFamily(Document document, string familyName)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(Family))
            .Cast<Family>()
            .FirstOrDefault(family => string.Equals(
                family.Name,
                familyName,
                StringComparison.CurrentCultureIgnoreCase));
    }
}
