using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using TrueBIM.App.Modules.RebarFill.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.RebarFill.Revit;

public sealed class RebarFillParameter
{
    public string Key { get; }
    public string Name { get; }
    public string Kind { get; }
    public string Identity { get; }
    public int PresentCount { get; internal set; }
    public int WritableCount { get; internal set; }
    public string Label => $"{Name} · {Kind} · доступно {WritableCount}/{PresentCount}";

    internal RebarFillParameter(Parameter parameter)
    {
        Key = RebarFillService.ParameterKey(parameter);
        Name = parameter.Definition.Name;
        Kind = RebarFillService.IsYesNo(parameter) ? "Да/Нет" : parameter.StorageType switch
        {
            StorageType.String => "Текст",
            StorageType.Integer => "Целое число",
            StorageType.Double => "Число / величина",
            _ => "Ссылка на элемент — только просмотр"
        };
        Identity = parameter.IsShared ? $"Общий параметр · {parameter.GUID}" : $"ID параметра · {RevitElementIds.GetValue(parameter.Id)}";
    }
}

public sealed class RebarFillRow : RebarFillSelectionItem
{
    public string ElementName { get; }
    public string CurrentValue { get; }
    public int BarCount { get; }
    public string Status { get; internal set; }
    public bool HasValue { get; }
    public bool CanWrite { get; }
    internal object? ExpectedValue { get; set; }

    internal RebarFillRow(Element element, Parameter? parameter, string status, bool canWrite)
        : base(RevitElementIds.GetValue(element.Id), RevitElementIds.GetValue(element.GetTypeId()),
            element is FamilyInstance instance ? $"{instance.Symbol.FamilyName}: {instance.Symbol.Name}"
                : element.Document.GetElement(element.GetTypeId())?.Name ?? element.Name)
    {
        ElementName = element is FamilyInstance family ? $"{family.Symbol.FamilyName}: {family.Name}" : element.Name;
        CurrentValue = parameter is null ? "—" : RebarFillService.Display(parameter);
        BarCount = element is Rebar rebar ? rebar.Quantity : element is RebarInSystem systemRebar ? systemRebar.Quantity : 1;
        HasValue = parameter is not null && !RebarFillService.IsEmpty(parameter);
        Status = status;
        CanWrite = canWrite;
    }
}

/// <summary>Записывает только параметры экземпляров, сохраняя вложенность и принадлежность системе.</summary>
public sealed class RebarFillService
{
    public static bool IsReinforcement(Element element) => element is Rebar or RebarInSystem
        || element is FamilyInstance && element.Category?.Id == new ElementId(BuiltInCategory.OST_Rebar);

    public static bool IsGenericModel(Element element) => element is FamilyInstance
        && element.Category?.Id == new ElementId(BuiltInCategory.OST_GenericModel);

    public IReadOnlyList<Element> ResolveTargets(Document document, IEnumerable<ElementId> selection)
    {
        HashSet<ElementId> visited = new();
        Dictionary<ElementId, Element> targets = new();
        foreach (ElementId id in selection.Distinct())
        {
            Element? element = document.GetElement(id);
            if (element is null) continue;
            if (IsReinforcement(element)) targets[id] = element;
            else if (IsGenericModel(element)) Visit((FamilyInstance)element);
        }
        return targets.Values.OrderBy(e => RevitElementIds.GetValue(e.Id)).ToList();

        void Visit(FamilyInstance parent)
        {
            if (!visited.Add(parent.Id)) return;
            foreach (ElementId childId in parent.GetSubComponentIds())
            {
                Element? child = document.GetElement(childId);
                if (child is null) continue;
                if (IsReinforcement(child)) targets[childId] = child;
                if (child is FamilyInstance nested) Visit(nested);
            }
        }
    }

    public IReadOnlyList<RebarFillParameter> CollectParameters(IReadOnlyList<Element> targets)
    {
        Dictionary<string, RebarFillParameter> options = new(StringComparer.Ordinal);
        foreach (Element element in targets)
        {
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (Parameter parameter in element.Parameters)
            {
                if (parameter.Definition is null || parameter.StorageType == StorageType.None) continue;
                string key = ParameterKey(parameter);
                if (!seen.Add(key)) continue;
                if (!options.TryGetValue(key, out RebarFillParameter? option))
                    options.Add(key, option = new RebarFillParameter(parameter));
                option.PresentCount++;
                if (!parameter.IsReadOnly && Supported(parameter)) option.WritableCount++;
            }
        }
        return options.Values.OrderByDescending(p => p.WritableCount > 0)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Key).ToList();
    }

    public IReadOnlyList<RebarFillRow> Inspect(IReadOnlyList<Element> targets, RebarFillParameter option)
    {
        return targets.Select(element =>
        {
            Parameter? parameter = Find(element, option.Key);
            string reason = parameter is null ? "Параметр отсутствует"
                : !Supported(parameter) ? "Ссылочный параметр: запись не поддерживается"
                : parameter.IsReadOnly ? "Заблокирован Revit / управляется формулой или связью"
                : element.Document.IsWorkshared && WorksharingUtils.GetCheckoutStatus(element.Document, element.Id) == CheckoutStatus.OwnedByOtherUser
                    ? "Занят другим пользователем"
                : string.Empty;
            return new RebarFillRow(element, parameter, reason.Length == 0 ? (IsEmpty(parameter!) ? "Пустой" : "Уже заполнен") : reason, reason.Length == 0);
        }).ToList();
    }

    public void ValidateInput(Document document, IReadOnlyList<RebarFillRow> rows, RebarFillParameter option, string input)
    {
        foreach (RebarFillRow row in rows.Where(r => r.CanWrite))
        {
            Parameter parameter = Find(document.GetElement(RevitElementIds.Create(row.ElementId)), option.Key)
                ?? throw new InvalidOperationException($"Параметр элемента {row.ElementId} больше не найден.");
            row.ExpectedValue = Parse(document, parameter, input);
        }
    }

    public IReadOnlyList<RebarFillRow> Apply(Document document, IReadOnlyList<Element> targets, RebarFillParameter option, string input, bool onlyEmpty)
    {
        IReadOnlyList<RebarFillRow> rows = Inspect(targets, option);
        List<RebarFillRow> candidates = rows.Where(r => r.CanWrite && (!onlyEmpty || !r.HasValue)).ToList();
        ValidateInput(document, candidates, option, input);
        foreach (RebarFillRow row in rows.Where(r => r.CanWrite && onlyEmpty && r.HasValue)) row.Status = "Пропущен: уже заполнен";
        if (candidates.Count == 0) return rows;

        using TransactionGroup group = new(document, "TrueBIM: заполнение арматуры");
        group.Start();
        try
        {
            using (Transaction transaction = new(document, "Заполнить параметр арматуры"))
            {
                transaction.Start();
                foreach (RebarFillRow row in candidates)
                {
                    using SubTransaction one = new(document);
                    one.Start();
                    try
                    {
                        Element element = document.GetElement(RevitElementIds.Create(row.ElementId));
                        Parameter parameter = Find(element, option.Key) ?? throw new InvalidOperationException("Параметр исчез");
                        if (Matches(parameter, row.ExpectedValue!)) { row.Status = "Уже задано"; one.RollBack(); continue; }
                        bool written = row.ExpectedValue switch
                        {
                            string text => parameter.Set(text),
                            int number => parameter.Set(number),
                            double number => parameter.Set(number),
                            _ => false
                        };
                        if (!written || !Matches(parameter, row.ExpectedValue!)) throw new InvalidOperationException("Revit не принял значение");
                        if (one.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Запись отменена Revit");
                        row.Status = "Записано";
                    }
                    catch (Exception exception) when (exception is not Autodesk.Revit.Exceptions.RegenerationFailedException)
                    {
                        if (one.GetStatus() == TransactionStatus.Started) one.RollBack();
                        row.Status = "Пропущен: " + exception.Message;
                    }
                }
                if (transaction.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit отменил транзакцию заполнения.");
            }
            foreach (RebarFillRow row in candidates.Where(r => r.Status == "Записано" || r.Status == "Уже задано"))
            {
                Parameter? parameter = Find(document.GetElement(RevitElementIds.Create(row.ElementId)), option.Key);
                if (parameter is null || !Matches(parameter, row.ExpectedValue!))
                    throw new InvalidOperationException($"Значение элемента {row.ElementId} изменилось после пересчёта. Вся операция отменена.");
            }
            if (group.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("Операция заполнения не завершена.");
            return rows;
        }
        catch
        {
            if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
            throw;
        }
    }

    internal static string ParameterKey(Parameter parameter)
    {
        string identity = parameter.IsShared ? "guid:" + parameter.GUID
            : "id:" + RevitElementIds.GetValue(parameter.Id);
#if REVIT2022_OR_GREATER
        string spec = parameter.Definition.GetDataType().TypeId;
#else
        string spec = parameter.Definition.ParameterType.ToString();
#endif
        return identity + "|" + parameter.StorageType + "|" + spec;
    }

    public static Parameter? Find(Element? element, string key) => element?.Parameters.Cast<Parameter>()
        .FirstOrDefault(p => p.Definition is not null && ParameterKey(p) == key);

    internal static bool IsYesNo(Parameter parameter)
    {
#if REVIT2022_OR_GREATER
        return parameter.Definition.GetDataType() == SpecTypeId.Boolean.YesNo;
#else
        return parameter.Definition.ParameterType == ParameterType.YesNo;
#endif
    }

    private static bool Supported(Parameter parameter) => parameter.StorageType is StorageType.String or StorageType.Integer or StorageType.Double;
    internal static bool IsEmpty(Parameter parameter) => !parameter.HasValue
        || parameter.StorageType == StorageType.String && string.IsNullOrWhiteSpace(parameter.AsString());

    internal static string Display(Parameter parameter) => parameter.StorageType == StorageType.String
        ? parameter.AsString() ?? string.Empty : parameter.AsValueString() ?? (parameter.StorageType == StorageType.Integer
            ? parameter.AsInteger().ToString(CultureInfo.CurrentCulture) : string.Empty);

    private static object Parse(Document document, Parameter parameter, string input)
    {
        if (parameter.StorageType == StorageType.String) return input;
        if (parameter.StorageType == StorageType.Integer)
        {
            string normalized = input.Trim().ToLowerInvariant();
            if (IsYesNo(parameter))
                return normalized switch { "да" or "yes" or "true" or "1" => 1, "нет" or "no" or "false" or "0" => 0,
                    _ => throw new ArgumentException("Для «Да/Нет» введите Да, Нет, 1 или 0.") };
            if (int.TryParse(input, NumberStyles.Integer, CultureInfo.CurrentCulture, out int integer)) return integer;
            throw new ArgumentException("Введите целое число.");
        }
        if (parameter.StorageType == StorageType.Double)
        {
            double value;
#if REVIT2022_OR_GREATER
            bool parsed = UnitFormatUtils.TryParse(document.GetUnits(), parameter.Definition.GetDataType(), input, out value);
#elif REVIT2021_OR_GREATER
            bool parsed = UnitFormatUtils.TryParse(document.GetUnits(), parameter.Definition.GetSpecTypeId(), input, out value);
#else
            bool parsed = UnitFormatUtils.TryParse(document.GetUnits(), parameter.Definition.UnitType, input, out value);
#endif
            if (parsed && !double.IsNaN(value) && !double.IsInfinity(value)) return value;
            throw new ArgumentException("Введите число в единицах проекта, при необходимости с обозначением единиц (например, 250 мм).");
        }
        throw new ArgumentException("Этот вид параметра не поддерживает запись.");
    }

    private static bool Matches(Parameter parameter, object expected) => expected switch
    {
        string text => parameter.StorageType == StorageType.String && string.Equals(parameter.AsString() ?? string.Empty, text, StringComparison.Ordinal),
        int number => parameter.HasValue && parameter.StorageType == StorageType.Integer && parameter.AsInteger() == number,
        double number => parameter.HasValue && parameter.StorageType == StorageType.Double && Math.Abs(parameter.AsDouble() - number) <= 1e-9 * Math.Max(1, Math.Abs(number)),
        _ => false
    };
}
