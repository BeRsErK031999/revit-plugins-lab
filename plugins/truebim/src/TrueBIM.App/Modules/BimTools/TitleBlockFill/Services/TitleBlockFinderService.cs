using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.BimTools.TitleBlockFill.Services;

public sealed class TitleBlockFinderService
{
    public IReadOnlyList<FamilyInstance> Find(Document document, ViewSheet sheet)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(sheet, nameof(sheet));

        return new FilteredElementCollector(document, sheet.Id)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .WhereElementIsNotElementType()
            .OfType<FamilyInstance>()
            .OrderBy(instance => instance.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public string GetDisplayStatus(Document document, ViewSheet sheet)
    {
        IReadOnlyList<FamilyInstance> titleBlocks = Find(document, sheet);
        return titleBlocks.Count switch
        {
            0 => "Нет",
            1 => titleBlocks[0].Name,
            _ => $"{titleBlocks[0].Name} (+{titleBlocks.Count - 1})"
        };
    }
}
