using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.RebarSplit.Revit;

/// <summary>Снимок значений экземпляра. Параметры формы переносит геометрический адаптер.</summary>
internal sealed class RebarSplitParameters
{
    private static readonly HashSet<BuiltInParameter> LayoutParameters = new()
    {
        BuiltInParameter.REBAR_ELEM_LAYOUT_RULE,
        BuiltInParameter.REBAR_ELEM_QUANTITY_OF_BARS,
        BuiltInParameter.REBAR_QUANITY_BY_DISTRIB,
        BuiltInParameter.REBAR_ELEM_BAR_SPACING,
        BuiltInParameter.REBAR_ELEM_LENGTH,
        BuiltInParameter.REBAR_ELEM_TOTAL_LENGTH,
        BuiltInParameter.REBAR_MIN_LENGTH,
        BuiltInParameter.REBAR_MAX_LENGTH,
        BuiltInParameter.REBAR_DISTRIBUTION_TYPE,
        BuiltInParameter.REBAR_INCLUDE_FIRST_BAR,
        BuiltInParameter.REBAR_INCLUDE_LAST_BAR,
        // Revit пересчитывает системный номер для новой геометрии; пользовательские позиции копируются.
        BuiltInParameter.REBAR_NUMBER
    };

    private readonly List<Value> values;

    private RebarSplitParameters(List<Value> values) => this.values = values;

    public static RebarSplitParameters Capture(Element source, ISet<ElementId> shapeParameters)
    {
        List<Value> result = new();
        foreach (Parameter parameter in source.Parameters)
        {
            if (parameter.IsReadOnly || !parameter.HasValue || shapeParameters.Contains(parameter.Id))
                continue;
            if (parameter.Definition is InternalDefinition definition && LayoutParameters.Contains(definition.BuiltInParameter))
                continue;
            object? value = Read(parameter);
            if (value is not null)
                result.Add(new Value(parameter.Definition, parameter.IsShared ? parameter.GUID : null, parameter.StorageType, value));
        }
        return new RebarSplitParameters(result);
    }

    public IReadOnlyList<string> Apply(Element target)
    {
        List<string> errors = new();
        foreach (Value value in values)
        {
            try
            {
                Parameter? parameter = Find(target, value);
                if (Matches(parameter, value)) continue;
                if (parameter is null || parameter.IsReadOnly || parameter.StorageType != value.StorageType)
                    throw new InvalidOperationException("параметр отсутствует или недоступен для записи");
                bool written = value.StorageType switch
                {
                    StorageType.String => parameter.Set((string)value.Data),
                    StorageType.Integer => parameter.Set((int)value.Data),
                    StorageType.Double => parameter.Set((double)value.Data),
                    StorageType.ElementId => parameter.Set((ElementId)value.Data),
                    _ => false
                };
                if (!written || !Matches(parameter, value))
                    throw new InvalidOperationException("значение не сохранено");
            }
            catch (Exception exception) when (exception is not Autodesk.Revit.Exceptions.RegenerationFailedException)
            {
                errors.Add($"{value.Definition.Name}: {exception.Message}");
            }
        }
        return errors;
    }

    public IReadOnlyList<string> Validate(Element target) => values
        .Where(value => !Matches(Find(target, value), value))
        .Select(value => $"{value.Definition.Name}: значение изменилось после пересчёта модели").ToList();

    private static Parameter? Find(Element target, Value value) => value.SharedGuid.HasValue
        ? target.get_Parameter(value.SharedGuid.Value)
        : target.get_Parameter(value.Definition);

    private static bool Matches(Parameter? parameter, Value value)
    {
        if (parameter is null || parameter.StorageType != value.StorageType) return false;
        object? actual = Read(parameter);
        return value.StorageType == StorageType.Double
            ? actual is double number && Math.Abs(number - (double)value.Data) <= 1e-9
            : Equals(actual, value.Data);
    }

    private static object? Read(Parameter parameter) => parameter.StorageType switch
    {
        StorageType.String => parameter.AsString() ?? string.Empty,
        StorageType.Integer => parameter.AsInteger(),
        StorageType.Double => parameter.AsDouble(),
        StorageType.ElementId => parameter.AsElementId(),
        _ => null
    };

    private sealed record Value(Definition Definition, Guid? SharedGuid, StorageType StorageType, object Data);
}
