using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.FamilyReplacement.Models;
using TrueBIM.App.Modules.BimTools.FamilyReplacement.Services;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.FamilyReplacement.Revit;

internal sealed class FamilyReplacementParameterService
{
    public FamilyReplacementParameterSnapshot Capture(FamilyInstance source)
    {
        List<FamilyReplacementParameterEntry> entries = [];
        foreach (Parameter parameter in source.Parameters.Cast<Parameter>())
        {
            if (parameter.Definition is null || parameter.StorageType == StorageType.None)
            {
                continue;
            }

            int? builtInId = GetBuiltInId(parameter);
            bool required = IsRequiredBuiltIn(builtInId);
            if (builtInId.HasValue && !required)
            {
                // Уровень, смещения, тип, рабочий набор, фазы и инженерные системы
                // управляются отдельной частью замены и не копируются как ручные параметры.
                continue;
            }

            if (!required && (parameter.IsReadOnly || !parameter.UserModifiable || !parameter.HasValue))
            {
                continue;
            }

            entries.Add(new FamilyReplacementParameterEntry(
                Describe(parameter, source.Document),
                ReadValue(parameter),
                required));
        }

        return new FamilyReplacementParameterSnapshot(source.Document, Array.AsReadOnly(entries.ToArray()));
    }

    public void Apply(FamilyInstance target, FamilyReplacementParameterSnapshot snapshot)
    {
        Process(target, snapshot, writeValues: true);
    }

    /// <summary>Проверяет значения после регенерации и удаления источника; транзакцией управляет вызывающий код.</summary>
    public void Validate(FamilyInstance target, FamilyReplacementParameterSnapshot snapshot)
    {
        Process(target, snapshot, writeValues: false);
    }

    private static void Process(FamilyInstance target, FamilyReplacementParameterSnapshot snapshot, bool writeValues)
    {
        if (!target.Document.Equals(snapshot.SourceDocument))
        {
            throw new InvalidOperationException("Перенос параметров разрешён только между экземплярами одного проекта.");
        }

        List<Parameter> targets = target.Parameters.Cast<Parameter>()
            .Where(parameter => parameter.Definition is not null && parameter.StorageType != StorageType.None)
            .ToList();
        List<FamilyReplacementParameterDefinition> definitions = targets.Select(parameter => Describe(parameter, target.Document)).ToList();

        foreach (FamilyReplacementParameterEntry entry in snapshot.Entries)
        {
            FamilyReplacementParameterMatch match = FamilyReplacementParameterPolicy.Match(entry.Definition, definitions);
            if (match.TargetIndex is not int targetIndex)
            {
                if (entry.Required || entry.Value.IsMeaningful)
                {
                    throw CannotPreserve(entry, match.Reason);
                }

                continue;
            }

            Parameter targetParameter = targets[targetIndex];
            if (entry.Value.Storage == FamilyReplacementParameterStorage.ElementId
                && entry.Value.Integer > 0
                && target.Document.GetElement(RevitElementIds.Create(entry.Value.Integer)) is null)
            {
                throw CannotPreserve(entry, "объект, на который ссылается значение, отсутствует в проекте");
            }

            if (FamilyReplacementParameterPolicy.ValuesEqual(entry.Value, ReadValue(targetParameter)))
            {
                continue;
            }

            if (!writeValues)
            {
                throw CannotPreserve(entry, "значение изменилось после регенерации или удаления исходного экземпляра");
            }

            if (targetParameter.IsReadOnly)
            {
                throw CannotPreserve(entry, "параметр нового экземпляра доступен только для чтения");
            }

            try
            {
                bool written = WriteValue(targetParameter, entry.Value);
                if (!written && !FamilyReplacementParameterPolicy.ValuesEqual(entry.Value, ReadValue(targetParameter)))
                {
                    throw CannotPreserve(entry, "Revit отклонил запись значения");
                }
            }
            catch (Exception exception) when (exception is not Autodesk.Revit.Exceptions.RegenerationFailedException
                && (exception is Autodesk.Revit.Exceptions.ApplicationException or Autodesk.Revit.Exceptions.ArgumentException))
            {
                throw CannotPreserve(entry, exception.Message, exception);
            }
        }
    }

    private static FamilyReplacementParameterDefinition Describe(Parameter parameter, Document document)
    {
        int? builtInId = GetBuiltInId(parameter);
        Guid? guid = parameter.IsShared ? parameter.GUID : null;
        long? projectDefinitionId = guid is null && builtInId is null && document.GetElement(parameter.Id) is ParameterElement
            ? RevitElementIds.GetValue(parameter.Id)
            : null;
#if REVIT2022_OR_GREATER
        string dataType = parameter.Definition.GetDataType().TypeId;
#elif REVIT2021_OR_GREATER
        string dataType = parameter.Definition.GetSpecTypeId().TypeId + ":" + parameter.Definition.ParameterType;
#else
        string dataType = parameter.Definition.ParameterType.ToString();
#endif
        return new FamilyReplacementParameterDefinition(
            parameter.Definition.Name,
            guid,
            builtInId,
            projectDefinitionId,
            ToStorage(parameter.StorageType),
            dataType);
    }

    private static int? GetBuiltInId(Parameter parameter)
    {
        return parameter.Definition is InternalDefinition definition && definition.BuiltInParameter != BuiltInParameter.INVALID
            ? (int)definition.BuiltInParameter
            : null;
    }

    private static bool IsRequiredBuiltIn(int? id)
    {
        return id == (int)BuiltInParameter.ALL_MODEL_MARK || id == (int)BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS;
    }

    private static FamilyReplacementParameterStorage ToStorage(StorageType storage)
    {
        return storage switch
        {
            StorageType.String => FamilyReplacementParameterStorage.String,
            StorageType.Integer => FamilyReplacementParameterStorage.Integer,
            StorageType.Double => FamilyReplacementParameterStorage.Double,
            StorageType.ElementId => FamilyReplacementParameterStorage.ElementId,
            _ => throw new InvalidOperationException("Неподдерживаемый тип хранения параметра.")
        };
    }

    private static FamilyReplacementParameterValue ReadValue(Parameter parameter)
    {
        FamilyReplacementParameterStorage storage = ToStorage(parameter.StorageType);
        return storage switch
        {
            FamilyReplacementParameterStorage.String => new(storage, Text: parameter.AsString() ?? string.Empty),
            FamilyReplacementParameterStorage.Integer => new(storage, Integer: parameter.AsInteger()),
            FamilyReplacementParameterStorage.Double => new(storage, Number: parameter.AsDouble()),
            FamilyReplacementParameterStorage.ElementId => new(storage, Integer: RevitElementIds.GetValue(parameter.AsElementId())),
            _ => throw new InvalidOperationException("Неподдерживаемый тип хранения параметра.")
        };
    }

    private static bool WriteValue(Parameter parameter, FamilyReplacementParameterValue value)
    {
        return value.Storage switch
        {
            FamilyReplacementParameterStorage.String => parameter.Set(value.Text),
            FamilyReplacementParameterStorage.Integer => parameter.Set(checked((int)value.Integer)),
            FamilyReplacementParameterStorage.Double => parameter.Set(value.Number),
            FamilyReplacementParameterStorage.ElementId => parameter.Set(RevitElementIds.Create(value.Integer)),
            _ => false
        };
    }

    private static InvalidOperationException CannotPreserve(FamilyReplacementParameterEntry entry, string reason, Exception? inner = null)
    {
        return new InvalidOperationException($"Не удалось сохранить параметр «{entry.Definition.Name}»: {reason}.", inner);
    }
}

internal sealed record FamilyReplacementParameterEntry(
    FamilyReplacementParameterDefinition Definition,
    FamilyReplacementParameterValue Value,
    bool Required);

internal sealed record FamilyReplacementParameterSnapshot(
    Document SourceDocument,
    IReadOnlyList<FamilyReplacementParameterEntry> Entries);
