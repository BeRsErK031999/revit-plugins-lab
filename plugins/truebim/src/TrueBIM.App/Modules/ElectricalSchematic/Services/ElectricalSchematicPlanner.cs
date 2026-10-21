using System.Globalization;
using System.Text.RegularExpressions;

namespace TrueBIM.App.Modules.ElectricalSchematic.Services;

public sealed record ElectricalConsumer(long Id, string Group, double? Watts, string Name, string Error = "");

public sealed record ElectricalScheme(long Id, string Group, string View, double? Power, string Consumers, string Error = "");

public sealed class ElectricalSchematicRow
{
    public ElectricalScheme Scheme { get; init; } = null!;
    public long Id => Scheme.Id;
    public string Group => Scheme.Group;
    public string View => Scheme.View;
    public string PreviousPower => Format(Scheme.Power);
    public string PreviousConsumers => Scheme.Consumers;
    public int Count { get; init; }
    public double? Power { get; init; }
    public string NewPower => Format(Power);
    public string Consumers { get; init; } = "";
    public string Message { get; set; } = "";
    public bool CanApply { get; init; }
    public bool IsSelected { get; set; }

    private static string Format(double? value) => value?.ToString("0.######", CultureInfo.CurrentCulture) ?? "—";
}

public static class ElectricalSchematicPlanner
{
    public static string NormalizeGroup(string? value)
    {
        string group = (value ?? "").Trim();
        group = Regex.Replace(group, @"^Гр\.\s*", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return group.Trim().Replace('c', 'с').Replace('C', 'С').ToUpperInvariant();
    }

    public static IReadOnlyList<ElectricalSchematicRow> Build(
        IEnumerable<ElectricalScheme> schemes, IEnumerable<ElectricalConsumer> consumers)
    {
        ElectricalScheme[] targets = schemes.GroupBy(s => s.Id).Select(g => g.First()).ToArray();
        var targetGroups = targets.GroupBy(s => NormalizeGroup(s.Group)).ToDictionary(g => g.Key, g => g.Count());
        var sourceGroups = consumers.GroupBy(c => c.Id).Select(g => g.First())
            .Where(c => NormalizeGroup(c.Group).Length > 0)
            .GroupBy(c => NormalizeGroup(c.Group)).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Id).ToArray());
        List<ElectricalSchematicRow> result = [];
        foreach (ElectricalScheme target in targets.OrderBy(s => s.Group, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Id))
        {
            string key = NormalizeGroup(target.Group);
            sourceGroups.TryGetValue(key, out ElectricalConsumer[]? sources);
            sources ??= [];
            string error = target.Error;
            if (key.Length == 0) error = "Пропуск: не заполнена группа схемы.";
            else if (targetGroups[key] > 1) error = "Пропуск: несколько элементов схемы с этой группой в проекте.";
            else if (sources.Length == 0) error = "Пропуск: потребители не найдены; прежние значения сохранены.";
            else if (sources.Any(c => c.Error.Length > 0 || !c.Watts.HasValue || !IsValidPower(c.Watts.Value)))
                error = "Пропуск: проверьте мощность/параметры потребителей ID " + string.Join(", ", sources
                    .Where(c => c.Error.Length > 0 || !c.Watts.HasValue || !IsValidPower(c.Watts.Value))
                    .Select(c => c.Id + (c.Error.Length > 0 ? " (" + c.Error + ")" : ""))) + ".";
            double total = sources.Sum(c => c.Watts ?? 0) / 1000;
            if (!IsValidPower(total) && error.Length == 0) error = "Пропуск: недопустимая сумма мощности.";
            string names = string.Join(", ", sources.Select(c => c.Name.Trim()).Where(n => n.Length > 0)
                .Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal));
            result.Add(new ElectricalSchematicRow
            {
                Scheme = target, Count = sources.Length, Power = error.Length == 0 ? total : null,
                Consumers = names, CanApply = error.Length == 0,
                Message = error.Length > 0 ? error : sources.Any(c => string.IsNullOrWhiteSpace(c.Name))
                    ? "Готово; пустые наименования не включены в список." : "Готово к обновлению."
            });
        }
        return result;
    }

    private static bool IsValidPower(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
}
