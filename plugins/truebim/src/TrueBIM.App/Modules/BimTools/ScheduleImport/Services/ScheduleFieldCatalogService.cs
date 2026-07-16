using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed class ScheduleFieldCatalogService
{
    public ScheduleFieldCatalogResult LoadFields(Document document, long categoryId)
    {
        Guard.NotNull(document, nameof(document));

        List<string> warnings = [];
        List<string> errors = [];
        List<ScheduleFieldOption> fields = [];
        using Transaction transaction = new(document, "TrueBIM: чтение полей спецификации");
        transaction.Start();
        try
        {
            ElementId revitCategoryId = RevitElementIds.Create(categoryId);
            if (!ViewSchedule.IsValidCategoryForSchedule(revitCategoryId))
            {
                errors.Add("Выбранная категория недоступна для обычной спецификации Revit.");
            }
            else
            {
                ViewSchedule schedule = ViewSchedule.CreateSchedule(document, revitCategoryId);
                fields.AddRange(schedule.Definition.GetSchedulableFields()
                    .Select(field => CreateOption(document, field))
                    .Where(field => field is not null)
                    .Select(field => field!)
                    .GroupBy(field => field.Key, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .OrderBy(field => field.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(field => FieldTypePriority(field.FieldTypeName))
                    .ToList());
            }
        }
        catch (Exception exception)
        {
            errors.Add($"Не удалось получить поля категории Revit: {exception.Message}");
        }
        finally
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }
        }

        if (errors.Count == 0 && fields.Count == 0)
        {
            warnings.Add("У выбранной категории Revit нет доступных полей спецификации.");
        }

        return new ScheduleFieldCatalogResult(
            categoryId,
            fields,
            warnings,
            errors);
    }

    private static ScheduleFieldOption? CreateOption(Document document, SchedulableField field)
    {
        string name = field.GetName(document)?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return null;
        }

        long parameterId = RevitElementIds.GetValue(field.ParameterId);
        int fieldTypeValue = (int)field.FieldType;
        string fieldTypeName = field.FieldType.ToString();
        string suffix = field.FieldType switch
        {
            ScheduleFieldType.Instance => "экземпляр",
            ScheduleFieldType.ElementType => "тип",
            _ => fieldTypeName
        };
        return new ScheduleFieldOption(
            $"{fieldTypeValue}:{parameterId}",
            name,
            $"{name} ({suffix})",
            parameterId,
            fieldTypeValue,
            fieldTypeName);
    }

    private static int FieldTypePriority(string fieldTypeName)
    {
        return fieldTypeName switch
        {
            "Instance" => 0,
            "ElementType" => 1,
            _ => 2
        };
    }
}
