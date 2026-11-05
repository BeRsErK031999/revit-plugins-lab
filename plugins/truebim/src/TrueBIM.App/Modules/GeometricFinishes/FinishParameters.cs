using System.Globalization;
using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.GeometricFinishes;

public static class FinishParameters
{
    public static string Key(Parameter parameter) => parameter.IsShared ? "g:" + parameter.GUID : "p:" + Id(parameter.Id);
    private static long Id(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
    public static string BuiltIn(BuiltInParameter parameter) => "p:" + (int)parameter;
    public static Parameter? Find(Element element, string key)
    {
        if (key.StartsWith("g:", StringComparison.Ordinal) && Guid.TryParse(key.Substring(2), out Guid guid)) return element.get_Parameter(guid);
        if (!key.StartsWith("p:", StringComparison.Ordinal) || !long.TryParse(key.Substring(2), out long id)) return null;
        if (id < 0) return element.get_Parameter((BuiltInParameter)id);
        return element.Parameters.Cast<Parameter>().FirstOrDefault(p => Id(p.Id) == id);
    }
    public static string Text(Parameter? p) => p == null || !p.HasValue ? "" : p.StorageType == StorageType.String
        ? p.AsString() ?? "" : p.AsValueString() ?? (p.StorageType == StorageType.Integer ? p.AsInteger().ToString(CultureInfo.InvariantCulture) : "");

    public static IReadOnlyList<FinishOption> Options(IEnumerable<Element> elements, bool writable)
    {
        return elements.SelectMany(e => e.Parameters.Cast<Parameter>())
            .Where(p => p.StorageType != StorageType.None && (!writable || (!p.IsReadOnly && p.StorageType != StorageType.ElementId)))
            .GroupBy(Key).Select(g => new FinishOption { Key = g.Key, Name = g.First().Definition.Name })
            .OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static IReadOnlyList<FinishOption> TargetOptions(Document doc, BuiltInCategory category)
    {
        var options = Options(new FilteredElementCollector(doc).OfCategory(category).WhereElementIsNotElementType(), true).ToDictionary(o => o.Key);
        foreach (BuiltInParameter builtIn in new[] { BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, BuiltInParameter.ALL_MODEL_MARK })
            options[BuiltIn(builtIn)] = new FinishOption { Key = BuiltIn(builtIn), Name = LabelUtils.GetLabelFor(builtIn) };
        DefinitionBindingMapIterator iterator = doc.ParameterBindings.ForwardIterator();
        iterator.Reset();
        Category wanted = Category.GetCategory(doc, category);
        while (iterator.MoveNext())
        {
            if (iterator.Current is not InstanceBinding binding || !binding.Categories.Contains(wanted)) continue;
            if (iterator.Key is not InternalDefinition definition) continue;
            string key = "p:" + Id(definition.Id);
            if (doc.GetElement(definition.Id) is SharedParameterElement shared) key = "g:" + shared.GuidValue;
            options[key] = new FinishOption { Key = key, Name = definition.Name };
        }
        return options.Values.OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static void Copy(Element room, Element finish, FinishSettings settings)
    {
        if (string.IsNullOrEmpty(settings.RoomParameterKey)) return;
        Parameter source = Find(room, settings.RoomParameterKey) ?? throw new InvalidOperationException("У помещения отсутствует выбранный параметр привязки.");
        Parameter target = Find(finish, settings.FinishParameterKey) ?? throw new InvalidOperationException("У созданной отделки отсутствует выбранный параметр привязки.");
        if (target.IsReadOnly) throw new InvalidOperationException("Выбранный параметр отделки доступен только для чтения.");
        if (!source.HasValue) throw new InvalidOperationException("Выбранный параметр помещения не заполнен.");
        bool written;
        if (target.StorageType == StorageType.String) written = target.Set(Text(source));
        else if (target.StorageType == source.StorageType && SameDataType(source, target))
        {
            written = target.StorageType switch
            {
                StorageType.Integer => target.Set(source.AsInteger()),
                StorageType.Double => target.Set(source.AsDouble()),
                _ => throw new InvalidOperationException("Для привязки выберите текстовый или совместимый числовой параметр.")
            };
        }
        else throw new InvalidOperationException("Типы параметров помещения и отделки несовместимы. Для номера/имени используйте текстовый параметр.");
        if (!written) throw new InvalidOperationException("Revit не записал значение параметра привязки.");
    }
    private static bool SameDataType(Parameter a, Parameter b)
    {
#if REVIT2022_OR_GREATER
        return a.Definition.GetDataType() == b.Definition.GetDataType();
#else
        return a.Definition.ParameterType == b.Definition.ParameterType;
#endif
    }
}
