using System.Globalization;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.OpeningViews.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.OpeningViews.Services;

public static class OpeningViewSourceResolver
{
    private static readonly Regex DefaultNamePattern = new(
        @"(?:^|_)Opening_(?:(?:Door|Window)_)?(?<id>\d+)(?:_|$)",
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
            message = "Откройте созданный TrueBIM фасад двери или окна в Project Browser.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    public static bool TryResolve(
        Document document,
        View activeView,
        out FamilyInstance? source,
        out string message)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(activeView, nameof(activeView));

        OpeningViewMetadata? metadata = OpeningViewMetadataService.Read(activeView);
        if (metadata is not null)
        {
            source = ResolveMetadataElement(document, metadata);
            if (IsDoorOrWindow(source))
            {
                message = "Источник найден по метаданным TrueBIM.";
                return true;
            }
        }

        if (TryExtractElementId(activeView.Name, out long elementId))
        {
            try
            {
                source = document.GetElement(RevitElementIds.Create(elementId)) as FamilyInstance;
                if (IsDoorOrWindow(source))
                {
                    message = "Источник найден по ElementId в имени вида.";
                    return true;
                }
            }
            catch (Exception)
            {
            }
        }

        List<FamilyInstance> visibleOpenings = new FilteredElementCollector(document, activeView.Id)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(IsDoorOrWindow)
            .ToList();
        if (visibleOpenings.Count == 1)
        {
            source = visibleOpenings[0];
            message = "Источник найден как единственная видимая дверь или окно.";
            return true;
        }

        source = null;
        message = visibleOpenings.Count == 0
            ? "На активном фасаде не найдена видимая дверь или окно."
            : "На активном фасаде видно несколько дверей/окон, а связь TrueBIM отсутствует. Пересоздайте вид или оставьте в crop один проём.";
        return false;
    }

    public static bool TryExtractElementId(string? viewName, out long elementId)
    {
        elementId = 0;
        Match match = DefaultNamePattern.Match(viewName ?? string.Empty);
        return match.Success
            && long.TryParse(match.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out elementId);
    }

    public static string GetCategoryKey(FamilyInstance source)
    {
        Guard.NotNull(source, nameof(source));
        return IsCategory(source, BuiltInCategory.OST_Windows)
            ? OpeningViewCategoryKeys.Window
            : OpeningViewCategoryKeys.Door;
    }

    private static FamilyInstance? ResolveMetadataElement(Document document, OpeningViewMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.SourceElementUniqueId))
        {
            FamilyInstance? byUniqueId = document.GetElement(metadata.SourceElementUniqueId) as FamilyInstance;
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
            return document.GetElement(RevitElementIds.Create(metadata.SourceElementId)) as FamilyInstance;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsDoorOrWindow(FamilyInstance? source)
    {
        return source is not null
            && (IsCategory(source, BuiltInCategory.OST_Doors) || IsCategory(source, BuiltInCategory.OST_Windows));
    }

    private static bool IsCategory(FamilyInstance source, BuiltInCategory category)
    {
        return source.Category is not null
            && RevitElementIds.GetValue(source.Category.Id) == (long)category;
    }
}
