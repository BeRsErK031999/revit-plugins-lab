namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public enum ScheduleFilterRule
{
    None,
    Equal,
    NotEqual,
    Contains,
    NotContains,
    BeginsWith,
    EndsWith,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    HasValue,
    HasNoValue
}

public sealed record ScheduleFilterRuleOption(ScheduleFilterRule Rule, string DisplayName)
{
    public static IReadOnlyList<ScheduleFilterRuleOption> All { get; } =
    [
        new(ScheduleFilterRule.None, "Без условия"),
        new(ScheduleFilterRule.Equal, "Равно"),
        new(ScheduleFilterRule.NotEqual, "Не равно"),
        new(ScheduleFilterRule.Contains, "Содержит"),
        new(ScheduleFilterRule.NotContains, "Не содержит"),
        new(ScheduleFilterRule.BeginsWith, "Начинается с"),
        new(ScheduleFilterRule.EndsWith, "Заканчивается на"),
        new(ScheduleFilterRule.GreaterThan, "Больше"),
        new(ScheduleFilterRule.GreaterThanOrEqual, "Больше или равно"),
        new(ScheduleFilterRule.LessThan, "Меньше"),
        new(ScheduleFilterRule.LessThanOrEqual, "Меньше или равно"),
        new(ScheduleFilterRule.HasValue, "Есть значение"),
        new(ScheduleFilterRule.HasNoValue, "Нет значения")
    ];
}
