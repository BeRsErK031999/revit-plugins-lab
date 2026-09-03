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

    public bool CanShowRevitPreview => HasZones && HasReadyHost;

    public bool CanClearRevitPreview => HasActiveRevitPreview;

    public bool CanCalculateRules => HasZones && HasReadyHost;

    public bool CanCreateRebar => HasZones && HasReadyHost && HasValidRules && HasConfirmedLayerMappings;

    public string NextAction => (HasSource, HasZones, HasHost, HasValidRules, HasConfirmedLayerMappings, HasValidHostBinding, HasSupportedHostGeometry) switch
    {
        (false, _, _, _, _, _, _) => "Выберите четыре карты изополей или готовый файл с зонами.",
        (true, false, _, _, _, _, _) when !CanProcessSource => "Не удалось обработать карты; выберите готовый файл с зонами.",
        (true, false, _, _, _, _, _) => "Найдите зоны на картах или загрузите готовые зоны.",
        (true, true, _, _, false, _, _) => "Подтвердите сторону конструкции для каждой карты.",
        (true, true, false, _, true, _, _) => "Выберите стену или плиту в модели.",
        (true, true, true, _, true, _, false) => "Выберите прямую базовую стену или горизонтальную плиту.",
        (true, true, true, _, true, false, true) => "Совместите зоны с выбранной конструкцией по трём контрольным точкам.",
        (true, true, true, false, true, true, true) => "Рассчитайте и проверьте раскладку арматуры.",
        _ => "Сравните раскладку с моделью и примените изменения после проверки."
    };
}
