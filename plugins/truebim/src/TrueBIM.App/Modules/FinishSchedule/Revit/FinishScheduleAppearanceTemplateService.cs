using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

/// <summary>Переносит настройку разделительной строки из шаблона вида проекта.</summary>
public static class FinishScheduleAppearanceTemplateService
{
    public const string TemplateName = FinishRoomScheduleStyleRules.AppearanceTemplateName;

    public static string? Validate(Document document)
    {
        ViewSchedule? template = Find(document);
        if (template is null)
        {
            return $"Подготовьте шаблон вида «{TemplateName}»: откройте спецификацию помещений, "
                + "в её свойствах на вкладке «Вид» снимите «Отделять данные пустой строкой», "
                + "затем создайте из неё шаблон вида с этим именем. "
                + "Плагин будет переносить его оформление в каждый новый выпуск. "
                + "Порядок настройки описан в методичке, раздел «Шаблон без пустой строки».";
        }

        if (!template.GetTemplateParameterIds().Any(IsAppearanceParameter))
        {
            return $"Шаблон «{TemplateName}» не поддерживает перенос внешнего вида спецификации. "
                + "Создайте его из обычной спецификации категории «Помещения».";
        }

        return null;
    }

    // Вызывается внутри транзакции создания нового выпуска.
    public static void Apply(ViewSchedule schedule)
    {
        Document document = schedule.Document;
        string? issue = Validate(document);
        if (issue is not null)
        {
            throw new InvalidOperationException(issue);
        }

        ViewSchedule source = Find(document)!;
        // View.Duplicate не поддерживает шаблоны вида; для них используется CopyElements.
        using CopyPasteOptions options = new();
        options.SetDuplicateTypeNamesHandler(new UseDestinationTypesHandler());
        ICollection<ElementId> copiedIds = ElementTransformUtils.CopyElements(
            document, [source.Id], document, Transform.Identity, options);
        ViewSchedule temporary = copiedIds
            .Select(document.GetElement)
            .OfType<ViewSchedule>()
            .Single(view => view.IsTemplate && view.Id != source.Id);
        try
        {
            // Меняем только собственную копию. Поля, фильтры, сортировка и стадия
            // шаблона не должны менять состав помещений нового выпуска.
            temporary.SetNonControlledTemplateParameterIds(
                temporary.GetTemplateParameterIds().Where(id => !IsAppearanceParameter(id)).ToList());
            schedule.ApplyViewTemplateParameters(temporary);
        }
        finally
        {
            document.Delete(temporary.Id);
        }
    }

    private static ViewSchedule? Find(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .FirstOrDefault(view => view.IsTemplate
                && RevitElementIds.GetValue(view.Definition.CategoryId) == (long)BuiltInCategory.OST_Rooms
                && string.Equals(view.Name, TemplateName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAppearanceParameter(ElementId id)
    {
        return RevitElementIds.GetValue(id) == (long)BuiltInParameter.SCHEDULE_SHEET_APPEARANCE_PARAM;
    }

    private sealed class UseDestinationTypesHandler : IDuplicateTypeNamesHandler
    {
        public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
        {
            return DuplicateTypeAction.UseDestinationTypes;
        }
    }
}
