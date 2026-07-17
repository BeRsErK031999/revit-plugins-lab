namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldWorkflowState(
    bool HasSource,
    bool HasZones,
    bool HasHost,
    bool HasValidRules,
    bool HasActiveRevitPreview,
    bool CanProcessSource,
    bool HasConfirmedLayerMappings,
    bool HasValidHostBinding = true,
    bool HasSupportedHostGeometry = true)
{
    public bool HasReadyHost => HasHost && HasSupportedHostGeometry && HasValidHostBinding;

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

    public string NextAction => (HasSource, HasZones, HasHost, HasValidRules, HasConfirmedLayerMappings, HasValidHostBinding, HasSupportedHostGeometry) switch
    {
        (false, _, _, _, _, _, _) => "Выберите JSON или изображение изополей.",
        (true, false, _, _, _, _, _) when !CanProcessSource => "Обработчик изображений недоступен; выберите готовый JSON.",
        (true, false, _, _, _, _, _) => "Загрузите или распознайте зоны изополей.",
        (true, true, _, _, false, _, _) => "Подтвердите назначение верх/низ для всех расчётных слоёв.",
        (true, true, false, _, true, _, _) => "Выберите стену или плиту в модели.",
        (true, true, true, _, true, _, false) => "Выберите прямую базовую стену или горизонтальную плиту.",
        (true, true, true, _, true, false, true) => "Привяжите зоны к плоскости host по трём контрольным точкам.",
        (true, true, true, false, true, true, true) => "Рассчитайте и проверьте правила армирования.",
        _ => "Проверьте раскладку и создайте армирование после подтверждения."
    };
}
