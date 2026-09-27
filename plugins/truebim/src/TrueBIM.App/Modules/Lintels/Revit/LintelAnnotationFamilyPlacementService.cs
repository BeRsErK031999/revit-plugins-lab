using System.IO;
using Autodesk.Revit.DB;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.Lintels.Revit;

internal sealed class LintelAnnotationFamilyPlacementService
{
    private string? resolvedFamilyPath;
    private string? resolvedFamilyUniqueId;

    public FamilyInstance Place(
        Document document,
        View view,
        string familyFilePath,
        XYZ insertionPoint,
        string annotationPurpose)
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
            throw new ArgumentException("Annotation family file path is required.", nameof(familyFilePath));
        }

        if (string.IsNullOrWhiteSpace(annotationPurpose))
        {
            throw new ArgumentException("Annotation purpose is required.", nameof(annotationPurpose));
        }

        string normalizedPath = Path.GetFullPath(familyFilePath);
        if (!File.Exists(normalizedPath))
        {
            throw new FileNotFoundException(
                $"Выбранный файл семейства для аннотации «{annotationPurpose}» не найден.",
                normalizedPath);
        }

        if (!string.Equals(Path.GetExtension(normalizedPath), ".rfa", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Для аннотации «{annotationPurpose}» требуется файл загружаемого семейства Revit с расширением .rfa.");
        }

        Family family = ResolveOrLoadFamily(document, normalizedPath, annotationPurpose);
        if (family.FamilyCategory is null
            || RevitElementIds.GetValue(family.FamilyCategory.Id)
                != (long)BuiltInCategory.OST_GenericAnnotation)
        {
            throw new InvalidOperationException(
                $"Семейство «{family.Name}» нельзя использовать для аннотации «{annotationPurpose}»: "
                + "требуется категория «Типовая аннотация».");
        }

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
                $"Семейство «{family.Name} : {symbol.Name}» нельзя разместить как аннотацию «{annotationPurpose}» на боковом виде. "
                + "Проверьте, что это обычное двумерное семейство категории «Типовая аннотация».",
                exception);
        }
    }

    private Family ResolveOrLoadFamily(
        Document document,
        string familyFilePath,
        string annotationPurpose)
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
                $"Revit не загрузил семейство для аннотации «{annotationPurpose}» «{expectedFamilyName}». "
                + "Проверьте совместимость файла .rfa с текущей версией Revit.");
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
