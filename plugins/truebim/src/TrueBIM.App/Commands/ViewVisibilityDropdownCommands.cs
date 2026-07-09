using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.ViewVisibility.Models;
using TrueBIM.App.Modules.ViewVisibility.Services;
using TrueBIM.App.Modules.ViewVisibility.UI;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

public abstract class ViewVisibilityCommandBase : IExternalCommand
{
    protected abstract string DisplayName { get; }

    protected virtual BuiltInCategory? TargetBuiltInCategory => null;

    protected virtual CategoryType? TargetCategoryType => null;

    protected virtual bool ShowAllCategories => false;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                logger.Warning($"View Visibility dropdown action '{DisplayName}' requested without an active document.");
                TaskDialog.Show("Видимость", "Откройте документ Revit перед настройкой видимости.");
                return Result.Succeeded;
            }

            Document document = uiDocument.Document;
            View activeView = document.ActiveView;
            if (activeView.IsTemplate)
            {
                logger.Warning($"View Visibility dropdown action '{DisplayName}' requested for a view template.");
                TaskDialog.Show("Видимость", "Откройте рабочий вид Revit. Шаблон вида нельзя настроить этим инструментом.");
                return Result.Succeeded;
            }

            ViewCategoryVisibilityService service = new(logger);
            IReadOnlyList<ViewCategoryVisibilityUpdate> updates = BuildUpdates(document, activeView, service, logger);
            if (updates.Count == 0)
            {
                ViewVisibilityRibbonState.Update(document, activeView);
                return Result.Succeeded;
            }

            ViewCategoryVisibilityApplyResult result = service.Apply(document, activeView, updates);
            ViewVisibilityRibbonState.Update(document, activeView);
            logger.Info(
                $"View Visibility dropdown action '{DisplayName}' applied for view '{activeView.Name}'. Updated: {result.UpdatedCount}; shown: {result.ShownCount}; hidden: {result.HiddenCount}; skipped: {result.SkippedCount}.");
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error($"Failed to apply View Visibility dropdown action '{DisplayName}'.", exception);
            TaskDialog.Show("Видимость", $"Не удалось изменить видимость: {DisplayName}. Используйте логи для диагностики.");
            return Result.Failed;
        }
    }

    private IReadOnlyList<ViewCategoryVisibilityUpdate> BuildUpdates(
        Document document,
        View activeView,
        ViewCategoryVisibilityService service,
        ITrueBimLogger logger)
    {
        if (ShowAllCategories)
        {
            return service
                .Collect(document, activeView)
                .Where(item => !item.IsVisible)
                .Select(item => new ViewCategoryVisibilityUpdate(item.CategoryId, IsVisible: true))
                .ToList();
        }

        if (TargetBuiltInCategory is BuiltInCategory builtInCategory)
        {
            if (!service.TryCollect(document, activeView, builtInCategory, out ViewCategoryVisibilityItem? item) || item is null)
            {
                logger.Warning($"View Visibility dropdown category '{DisplayName}' is not controllable for view '{activeView.Name}'.");
                return [];
            }

            return [new ViewCategoryVisibilityUpdate(item.CategoryId, !item.IsVisible)];
        }

        if (TargetCategoryType is CategoryType categoryType)
        {
            IReadOnlyList<ViewCategoryVisibilityItem> items = service
                .Collect(document, activeView)
                .Where(item => item.CategoryType == categoryType)
                .ToList();
            if (items.Count == 0)
            {
                logger.Warning($"View Visibility dropdown group '{DisplayName}' has no controllable categories for view '{activeView.Name}'.");
                return [];
            }

            bool shouldShow = items.Any(item => !item.IsVisible);
            return items
                .Select(item => new ViewCategoryVisibilityUpdate(item.CategoryId, shouldShow))
                .ToList();
        }

        logger.Warning($"View Visibility dropdown action '{DisplayName}' has no target.");
        return [];
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleWindowsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Окна";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_Windows;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleDoorsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Двери";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_Doors;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleWallsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Стены";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_Walls;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleColumnsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Колонны";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_Columns;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleStructuralFramingVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Каркас несущий";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_StructuralFraming;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleStructuralFoundationVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Фундамент несущей конструкции";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_StructuralFoundation;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleRebarVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Армирование";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_Rebar;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleComponentsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Компоненты";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_SpecialityEquipment;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleGenericModelsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Обобщенные модели";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_GenericModel;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleRoofsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Крыши";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_Roofs;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleFloorsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Перекрытия";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_Floors;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleCeilingsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Потолки";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_Ceilings;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleStairsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Лестницы";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_Stairs;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleRailingsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Ограждения";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_Railings;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleRampsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Пандусы";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_Ramps;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleGridsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Оси";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_Grids;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleLevelsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Уровни";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_Levels;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleSectionsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Разрезы";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_Sections;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleElevationsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Фасады";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_Elev;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleTagsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Марки";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_Tags;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleReferencePlanesVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Опорные плоскости";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_CLines;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleLinesVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Линии";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_Lines;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleMassVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Формообразующие";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_Mass;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleDuctsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Воздуховоды";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_DuctCurves;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleFlexDuctsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Гибкие воздуховоды";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_FlexDuctCurves;
}

[Transaction(TransactionMode.Manual)]
public sealed class TogglePipesVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Трубы";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_PipeCurves;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleWiresVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Провода";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_Wire;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleCableTraysVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Кабельные лотки";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_CableTray;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleConduitsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Короба";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_Conduit;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleMechanicalEquipmentVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Оборудование";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_MechanicalEquipment;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleElectricalEquipmentVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Электрооборудование";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_ElectricalEquipment;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleAnalyticalModelVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Все категории аналитической модели";

    protected override CategoryType? TargetCategoryType => CategoryType.AnalyticalModel;
}

[Transaction(TransactionMode.Manual)]
public sealed class TogglePointCloudsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Облака точек";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_PointClouds;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleLinksVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Связи";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_RvtLinks;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleImportSymbolsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Обозначения импорта";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_ImportObjectStyles;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleRasterImagesVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Растровые изображения";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_RasterImages;
}

[Transaction(TransactionMode.Manual)]
public sealed class ToggleGenericAnnotationsVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Аннотации";

    protected override BuiltInCategory? TargetBuiltInCategory => BuiltInCategory.OST_GenericAnnotation;
}
