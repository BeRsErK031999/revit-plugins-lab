namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldWorkflowState(
    bool HasSource,
    bool HasZones,
    bool HasHost,
    bool HasValidRules,
    bool HasActiveRevitPreview,
    bool CanProcessSource)
{
    public int CompletedStepCount => new[] { HasSource, HasZones, HasHost, HasValidRules }.Count(value => value);

    public bool CanRunRecognition => HasSource && CanProcessSource;

    public bool CanShowRevitPreview => HasZones;

    public bool CanClearRevitPreview => HasActiveRevitPreview;

    public bool CanCalculateRules => HasZones && HasHost;

    public bool CanCreateRebar => HasZones && HasHost && HasValidRules;

    public string NextAction => (HasSource, HasZones, HasHost, HasValidRules) switch
    {
        (false, _, _, _) => "Выберите JSON или изображение изополей.",
        (true, false, _, _) when !CanProcessSource => "Для изображения настройте worker или выберите готовый JSON.",
        (true, false, _, _) => "Загрузите или распознайте зоны изополей.",
        (true, true, false, _) => "Выберите стену или плиту в модели.",
        (true, true, true, false) => "Рассчитайте и проверьте правила армирования.",
        _ => "Проверьте правила и создайте пробное армирование."
    };
}
