namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldWorkflowState(
    bool HasSource,
    bool HasZones,
    bool HasHost,
    bool HasValidRules,
    bool HasActiveRevitPreview,
    bool CanProcessSource,
    bool HasConfirmedLayerMappings)
{
    public int CompletedStepCount => new[]
    {
        HasSource,
        HasConfirmedLayerMappings,
        HasZones,
        HasHost,
        HasValidRules
    }.Count(value => value);

    public bool CanRunRecognition => HasSource && CanProcessSource;

    public bool CanShowRevitPreview => HasZones;

    public bool CanClearRevitPreview => HasActiveRevitPreview;

    public bool CanCalculateRules => HasZones && HasHost;

    public bool CanCreateRebar => HasZones && HasHost && HasValidRules && HasConfirmedLayerMappings;

    public string NextAction => (HasSource, HasZones, HasHost, HasValidRules, HasConfirmedLayerMappings) switch
    {
        (false, _, _, _, _) => "Выберите JSON или изображение изополей.",
        (true, false, _, _, _) when !CanProcessSource => "Для изображения настройте worker или выберите готовый JSON.",
        (true, false, _, _, _) => "Загрузите или распознайте зоны изополей.",
        (true, true, _, _, false) => "Подтвердите назначение верх/низ для всех расчётных слоёв.",
        (true, true, false, _, true) => "Выберите стену или плиту в модели.",
        (true, true, true, false, true) => "Рассчитайте и проверьте правила армирования.",
        _ => "Проверьте правила и создайте пробное армирование."
    };
}
