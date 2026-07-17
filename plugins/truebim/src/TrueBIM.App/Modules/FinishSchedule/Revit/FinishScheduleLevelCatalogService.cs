using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

public sealed class FinishScheduleLevelCatalogService
{
    public IReadOnlyList<FinishScheduleLevelOption> Collect(Document document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        return new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(level => level.Elevation)
            .ThenBy(level => level.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(level => new FinishScheduleLevelOption(
                RevitElementIds.GetValue(level.Id),
                level.Name,
                level.Elevation))
            .ToArray();
    }
}
