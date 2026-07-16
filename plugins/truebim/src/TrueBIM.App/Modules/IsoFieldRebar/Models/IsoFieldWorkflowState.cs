namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldWorkflowState(
    bool HasSource,
    bool HasZones,
    bool HasHost,
    bool HasValidRules,
    bool HasActiveRevitPreview,
    bool CanProcessSource,
    bool HasConfirmedLayerMappings,
    bool HasValidHostBinding = true)
{
    public bool HasReadyHost => HasHost && HasValidHostBinding;

    public int CompletedStepCount => new[]
    {
        HasSource,
        HasConfirmedLayerMappings,
        HasZones,
        HasReadyHost,
        HasValidRules
    }.Count(value => value);

    public bool CanRunRecognition => HasSource && CanProcessSource;

    public bool CanShowRevitPreview => HasZones;

    public bool CanClearRevitPreview => HasActiveRevitPreview;

    public bool CanCalculateRules => HasZones && HasReadyHost;

    public bool CanCreateRebar => HasZones && HasReadyHost && HasValidRules && HasConfirmedLayerMappings;

    public string NextAction => (HasSource, HasZones, HasHost, HasValidRules, HasConfirmedLayerMappings, HasValidHostBinding) switch
    {
        (false, _, _, _, _, _) => "Выберите JSON или изображение изополей.",
        (true, false, _, _, _, _) when !CanProcessSource => "Обработчик изображений недоступен; выберите готовый JSON.",
        (true, false, _, _, _, _) => "Загрузите или распознайте зоны изополей.",
        (true, true, _, _, false, _) => "Подтвердите назначение верх/низ для всех расчётных слоёв.",
        (true, true, false, _, true, _) => "Выберите стену или плиту в модели.",
        (true, true, true, _, true, false) => "Привяжите зоны к плите по трём контрольным точкам.",
        (true, true, true, false, true, true) => "Рассчитайте и проверьте правила армирования.",
        _ => "Проверьте раскладку и создайте армирование после подтверждения."
    };
}
