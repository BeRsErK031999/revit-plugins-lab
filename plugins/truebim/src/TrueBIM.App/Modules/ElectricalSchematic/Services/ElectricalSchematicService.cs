using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.ElectricalSchematic.Services;

public sealed record ElectricalSchematicPreview(IReadOnlyList<ElectricalSchematicRow> Rows, IReadOnlyList<string> Diagnostics);

public sealed class ElectricalSchematicService
{
    public static readonly Guid GroupGuid = new("7ee7e2c2-c477-474f-8bb8-4025078a56c6");
    public static readonly Guid SourcePowerGuid = new("0ca95a87-4287-4c2a-a10b-338d36a8b050");
    public static readonly Guid SourceNameGuid = new("b571b2f9-d6a2-4500-8614-95a9ce6a7dde");
    public static readonly Guid TargetPowerGuid = new("066b671f-c486-4b4c-bf06-de01b57333c5");
    private const string ConsumerName = "• Потребитель";
    private readonly Document document;

    public ElectricalSchematicService(Document document) => this.document = document;

    public ElectricalSchematicPreview Preview()
    {
        List<ElectricalConsumer> consumers = [];
        List<ElectricalScheme> schemes = [];
        List<string> diagnostics = [];
        foreach (FamilyInstance instance in new FilteredElementCollector(document).OfClass(typeof(FamilyInstance)))
        {
            // Shared nested boxes and graphic subcomponents are not independent loads or scheme rows.
            if (instance.SuperComponent != null) continue;
            Parameter? group = instance.get_Parameter(GroupGuid);
            Parameter? sourcePower = instance.get_Parameter(SourcePowerGuid);
            Parameter? targetPower = instance.get_Parameter(TargetPowerGuid);
            bool isDetail = instance.Category?.Id == new ElementId(BuiltInCategory.OST_DetailComponents);
            if (isDetail && targetPower != null)
            {
                Parameter? name = TargetName(instance);
                string error = ValidateTarget(targetPower, name);
                schemes.Add(new ElectricalScheme(Id(instance.Id), ReadText(group),
                    document.GetElement(instance.OwnerViewId)?.Name ?? "—",
                    IsPower(targetPower, true) ? FromInternal(targetPower.AsDouble(), true) : null,
                    ReadText(name), error));
                continue;
            }
            if (instance.Category?.CategoryType != CategoryType.Model || isDetail || (sourcePower == null && group == null)) continue;
            string groupText = ReadText(group);
            if (ElectricalSchematicPlanner.NormalizeGroup(groupText).Length == 0)
            {
                diagnostics.Add($"Потребитель ID {Id(instance.Id)}: не заполнена группа; исключён из расчёта.");
                continue;
            }
            Parameter? sourceName = instance.get_Parameter(SourceNameGuid);
            string sourceError = !IsPower(sourcePower, false) ? "нет параметра мощности в Вт" : "";
            if (sourceName == null || sourceName.StorageType != StorageType.String)
                sourceError = "нет текстового параметра «• Потребитель»";
            consumers.Add(new ElectricalConsumer(Id(instance.Id), groupText,
                IsPower(sourcePower, false) ? (sourcePower!.HasValue ? FromInternal(sourcePower.AsDouble(), false) : 0) : null,
                ReadText(sourceName), sourceError));
        }
        return new ElectricalSchematicPreview(ElectricalSchematicPlanner.Build(schemes, consumers), diagnostics);
    }

    public IReadOnlyDictionary<long, string> Apply(IReadOnlyList<ElectricalSchematicRow> selected)
    {
        Dictionary<long, string> results = [];
        if (document.IsReadOnly || document.IsFamilyDocument)
            throw new InvalidOperationException("Обновление доступно только в редактируемом проекте.");
        var fresh = Preview().Rows.ToDictionary(row => row.Id);
        using TransactionGroup batch = new(document, "TrueBIM: обновить электрическую схему");
        if (batch.Start() != TransactionStatus.Started) throw new InvalidOperationException("Не удалось начать обновление.");
        foreach (ElectricalSchematicRow row in selected.GroupBy(r => r.Id).Select(g => g.First()))
        {
            if (!fresh.TryGetValue(row.Id, out ElectricalSchematicRow? current) || !current.CanApply)
            {
                results[row.Id] = current?.Message ?? "Пропуск: элемент схемы удалён.";
                continue;
            }
            if (current.Group != row.Group || current.Power != row.Power || current.Consumers != row.Consumers ||
                current.Count != row.Count || current.Scheme.Power != row.Scheme.Power || current.PreviousConsumers != row.PreviousConsumers)
            {
                results[row.Id] = "Пропуск: данные изменились. Повторите предпросмотр.";
                continue;
            }
            using Transaction transaction = new(document, "Схема: " + row.Group);
            try
            {
                transaction.Start();
                transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                    .SetFailuresPreprocessor(new RollbackOnFailure()).SetClearAfterRollback(true));
                Element element = document.GetElement(ToElementId(row.Id));
                Parameter power = element.get_Parameter(TargetPowerGuid);
                Parameter? name = TargetName(element);
                string error = ValidateTarget(power, name);
                if (error.Length > 0) throw new InvalidOperationException(error);
                if (!power.Set(ToInternal(current.Power!.Value)) || !name!.Set(current.Consumers))
                    throw new InvalidOperationException("Revit отклонил запись параметров.");
                document.Regenerate();
                if (Math.Abs(FromInternal(power.AsDouble(), true) - current.Power.Value) > 0.000001 || ReadText(name) != current.Consumers)
                    throw new InvalidOperationException("Значения после записи не совпали с расчётом.");
                TransactionStatus status = transaction.Commit();
                results[row.Id] = status == TransactionStatus.Committed
                    ? "Обновлено: мощность и потребители записаны." : "Пропуск: Revit отменил запись группы.";
            }
            catch (Exception exception)
            {
                if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack();
                results[row.Id] = "Пропуск: " + exception.Message;
            }
        }
        if (batch.Assimilate() != TransactionStatus.Committed)
            throw new InvalidOperationException("Revit отменил обновление схемы.");
        return results;
    }

    private static string ValidateTarget(Parameter? power, Parameter? name)
    {
        if (!IsPower(power, true)) return "Пропуск: требуется параметр «• Установленная мощность» в ВА.";
        if (name == null || name.StorageType != StorageType.String) return "Пропуск: параметр «• Потребитель» отсутствует или неоднозначен.";
        return power!.IsReadOnly || name.IsReadOnly ? "Пропуск: параметры схемы недоступны для записи." : "";
    }

    private static Parameter? TargetName(Element element)
    {
        IList<Parameter> matches = element.GetParameters(ConsumerName);
        return matches.Count == 1 ? matches[0] : null;
    }

    private static string ReadText(Parameter? parameter) => parameter?.StorageType == StorageType.String ? parameter.AsString() ?? "" : "";

    private static bool IsPower(Parameter? parameter, bool apparent)
    {
        if (parameter?.StorageType != StorageType.Double) return false;
#if REVIT2022_OR_GREATER
        return parameter.Definition.GetDataType() == (apparent ? SpecTypeId.ApparentPower : SpecTypeId.ElectricalPower);
#else
        return parameter.Definition.ParameterType == (apparent ? ParameterType.ElectricalApparentPower : ParameterType.ElectricalPower);
#endif
    }

    private static double FromInternal(double value, bool apparent)
    {
#if REVIT2021_OR_GREATER
        return UnitUtils.ConvertFromInternalUnits(value, apparent ? UnitTypeId.VoltAmperes : UnitTypeId.Watts);
#else
        return UnitUtils.ConvertFromInternalUnits(value, apparent ? DisplayUnitType.DUT_VOLT_AMPERES : DisplayUnitType.DUT_WATTS);
#endif
    }

    private static double ToInternal(double value)
    {
#if REVIT2021_OR_GREATER
        return UnitUtils.ConvertToInternalUnits(value, UnitTypeId.VoltAmperes);
#else
        return UnitUtils.ConvertToInternalUnits(value, DisplayUnitType.DUT_VOLT_AMPERES);
#endif
    }

    public static long Id(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }

    private static ElementId ToElementId(long id)
    {
#if REVIT2024_OR_GREATER
        return new ElementId(id);
#else
        return new ElementId(checked((int)id));
#endif
    }

    private sealed class RollbackOnFailure : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor) => accessor.GetFailureMessages().Count > 0
            ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
    }
}
