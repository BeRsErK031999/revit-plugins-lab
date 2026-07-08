using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.FamilyManager.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Services;

public sealed class FamilyLoadService
{
    public FamilyLoadResult Load(
        Document document,
        string filePath,
        bool overwriteExisting,
        ITrueBimLogger logger)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNullOrWhiteSpace(filePath, nameof(filePath));
        Guard.NotNull(logger, nameof(logger));

        string normalizedPath = FamilyPathNormalizer.Normalize(filePath);
        if (!File.Exists(normalizedPath))
        {
            return new FamilyLoadResult(
                FamilyLoadStatus.Failed,
                Path.GetFileNameWithoutExtension(normalizedPath),
                "Файл семейства не найден.");
        }

        string familyName = Path.GetFileNameWithoutExtension(normalizedPath);
        if (FindFamily(document, familyName) is not null && !overwriteExisting)
        {
            return new FamilyLoadResult(
                FamilyLoadStatus.AlreadyLoaded,
                familyName,
                "Семейство уже загружено в проект.");
        }

        try
        {
            using Transaction transaction = new(document, "TrueBIM Load Family");
            transaction.Start();
            bool loaded = document.LoadFamily(
                normalizedPath,
                new TrueBimFamilyLoadOptions(overwriteExisting),
                out Family family);

            if (loaded)
            {
                transaction.Commit();
                logger.Info($"Loaded Revit family: {normalizedPath}");
                return new FamilyLoadResult(
                    FamilyLoadStatus.Loaded,
                    string.IsNullOrWhiteSpace(family.Name) ? familyName : family.Name,
                    "Семейство загружено.");
            }

            transaction.RollBack();
            return new FamilyLoadResult(
                FamilyLoadStatus.Skipped,
                familyName,
                "Revit не загрузил семейство. Возможно, оно уже есть в проекте или файл несовместим.");
        }
        catch (Exception exception)
        {
            logger.Error($"Failed to load family '{normalizedPath}'.", exception);
            return new FamilyLoadResult(
                FamilyLoadStatus.Failed,
                familyName,
                exception.Message);
        }
    }

    public bool FamilyExists(Document document, string familyName)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNullOrWhiteSpace(familyName, nameof(familyName));

        return FindFamily(document, familyName) is not null;
    }

    public IReadOnlyList<FamilyTypeInfo> CollectLoadedTypes(Document document, string familyName)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNullOrWhiteSpace(familyName, nameof(familyName));

        Family? family = FindFamily(document, familyName);
        if (family is null)
        {
            return [];
        }

        return family
            .GetFamilySymbolIds()
            .Select(id => document.GetElement(id) as FamilySymbol)
            .Where(symbol => symbol is not null)
            .Select(symbol => new FamilyTypeInfo(RevitElementIds.GetValue(symbol!.Id), symbol.Name))
            .OrderBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public FamilySymbol? ResolveSymbol(Document document, string familyName, string? symbolName)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNullOrWhiteSpace(familyName, nameof(familyName));

        Family? family = FindFamily(document, familyName);
        if (family is null)
        {
            return null;
        }

        IReadOnlyList<FamilySymbol> symbols = family
            .GetFamilySymbolIds()
            .Select(id => document.GetElement(id) as FamilySymbol)
            .Where(symbol => symbol is not null)
            .Select(symbol => symbol!)
            .OrderBy(symbol => symbol.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (!string.IsNullOrWhiteSpace(symbolName))
        {
            FamilySymbol? selected = symbols.FirstOrDefault(symbol =>
                string.Equals(symbol.Name, symbolName, StringComparison.CurrentCultureIgnoreCase));
            if (selected is not null)
            {
                return selected;
            }
        }

        return symbols.FirstOrDefault();
    }

    public void ActivateAndRequestPlacement(UIDocument uiDocument, FamilySymbol symbol)
    {
        Guard.NotNull(uiDocument, nameof(uiDocument));
        Guard.NotNull(symbol, nameof(symbol));

        if (!symbol.IsActive)
        {
            using Transaction transaction = new(uiDocument.Document, "TrueBIM Activate Family Type");
            transaction.Start();
            symbol.Activate();
            transaction.Commit();
        }

        uiDocument.PostRequestForElementTypePlacement(symbol);
    }

    private static Family? FindFamily(Document document, string familyName)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(Family))
            .Cast<Family>()
            .FirstOrDefault(family => string.Equals(family.Name, familyName, StringComparison.CurrentCultureIgnoreCase));
    }

    private sealed class TrueBimFamilyLoadOptions : IFamilyLoadOptions
    {
        private readonly bool overwriteExisting;

        public TrueBimFamilyLoadOptions(bool overwriteExisting)
        {
            this.overwriteExisting = overwriteExisting;
        }

        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = overwriteExisting;
            return overwriteExisting;
        }

        public bool OnSharedFamilyFound(
            Family sharedFamily,
            bool familyInUse,
            out FamilySource source,
            out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = overwriteExisting;
            return overwriteExisting;
        }
    }
}
