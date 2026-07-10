using System.Globalization;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.OpeningViews.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.OpeningViews.Services;

public static class OpeningViewSourceResolver
{
    private static readonly Regex DefaultNamePattern = new(
        @"(?:^|_)Opening_(?:(?:Door|Window|CurtainWall)_)?(?<id>\d+)(?:_|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool CanUseActiveView(View? activeView, out string message)
    {
        if (activeView is null)
        {
            message = "Активный вид не найден.";
            return false;
        }

        if (activeView.IsTemplate)
        {
            message = "Оформление фасада недоступно на шаблоне вида.";
            return false;
        }

        if (activeView is not ViewSection || activeView.ViewType != ViewType.Elevation)
        {
            message = "Откройте созданный TrueBIM фасад двери, окна или витража в Project Browser.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    public static bool TryResolve(
        Document document,
        View activeView,
        out Element? source,
        out string message)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(activeView, nameof(activeView));

        OpeningViewMetadata? metadata = OpeningViewMetadataService.Read(activeView);
        if (metadata is not null)
        {
            source = ResolveMetadataElement(document, metadata);
            if (OpeningViewElementClassifier.IsSupported(source))
            {
                message = "Источник найден по метаданным TrueBIM.";
                return true;
            }
        }

        if (TryExtractElementId(activeView.Name, out long elementId))
        {
            try
            {
                source = document.GetElement(RevitElementIds.Create(elementId));
                if (OpeningViewElementClassifier.IsSupported(source))
                {
                    message = "Источник найден по ElementId в имени вида.";
                    return true;
                }
            }
            catch (Exception)
            {
            }
        }

        List<Element> visibleOpenings = new FilteredElementCollector(document, activeView.Id)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(OpeningViewElementClassifier.IsSupported)
            .Cast<Element>()
            .ToList();
        visibleOpenings.AddRange(new FilteredElementCollector(document, activeView.Id)
            .OfClass(typeof(Wall))
            .WhereElementIsNotElementType()
            .Cast<Wall>()
            .Where(OpeningViewElementClassifier.IsCurtainWall));
        if (visibleOpenings.Count == 1)
        {
            source = visibleOpenings[0];
            message = "Источник найден как единственная видимая дверь, окно или витраж.";
            return true;
        }

        source = null;
        message = visibleOpenings.Count == 0
            ? "На активном фасаде не найдена видимая дверь, окно или витраж."
            : "На активном фасаде видно несколько дверей, окон или витражей, а связь TrueBIM отсутствует. Пересоздайте вид или оставьте в crop один элемент.";
        return false;
    }

    public static bool TryExtractElementId(string? viewName, out long elementId)
    {
        elementId = 0;
        Match match = DefaultNamePattern.Match(viewName ?? string.Empty);
        return match.Success
            && long.TryParse(match.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out elementId);
    }

    public static string GetCategoryKey(Element source)
    {
        return OpeningViewElementClassifier.GetCategoryKey(source);
    }

    private static Element? ResolveMetadataElement(Document document, OpeningViewMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.SourceElementUniqueId))
        {
            Element? byUniqueId = document.GetElement(metadata.SourceElementUniqueId);
            if (byUniqueId is not null)
            {
                return byUniqueId;
            }
        }

        if (metadata.SourceElementId <= 0)
        {
            return null;
        }

        try
        {
            return document.GetElement(RevitElementIds.Create(metadata.SourceElementId));
        }
        catch (Exception)
        {
            return null;
        }
    }
}
