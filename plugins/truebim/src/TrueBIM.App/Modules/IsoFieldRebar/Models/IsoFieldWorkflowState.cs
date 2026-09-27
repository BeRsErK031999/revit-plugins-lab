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
    bool HasSupportedHostGeometry = true,
    bool HasComparedWithModel = false)
{
    public const int RequiredStepCount = 6;

    public bool HasReadyHost => HasHost && HasSupportedHostGeometry && HasValidHostBinding;

    public int CompletedStepCount => new[]
    {
        HasSource,
        HasConfirmedLayerMappings,
        HasZones,
        HasReadyHost,
        HasValidRules,
        HasComparedWithModel
    }.Count(value => value);

    public bool CanRunRecognition => HasSource && CanProcessSource;

    public bool CanShowRevitPreview => HasZones && HasReadyHost;

    public bool CanClearRevitPreview => HasActiveRevitPreview;

    public bool CanCalculateRules => HasZones && HasReadyHost;

    public bool CanCompareWithModel =>
        HasZones && HasReadyHost && HasValidRules && HasConfirmedLayerMappings;

    public bool CanCreateRebar => CanCompareWithModel && HasComparedWithModel;

    public string NextAction => (HasSource, HasZones, HasHost, HasValidRules, HasConfirmedLayerMappings, HasValidHostBinding, HasSupportedHostGeometry, HasComparedWithModel) switch
    {
        (false, _, _, _, _, _, _, _) => "Выберите четыре карты изополей или готовый файл с зонами.",
        (true, false, _, _, _, _, _, _) when !CanProcessSource => "Не удалось обработать карты; выберите готовый файл с зонами.",
        (true, false, _, _, _, _, _, _) => "Нажмите «Найти зоны на 4 картах».",
        (true, true, _, _, false, _, _, _) => "Проверьте, на какую сторону конструкции назначена каждая карта.",
        (true, true, false, _, true, _, _, _) => "Выберите стену или плиту в модели.",
        (true, true, true, _, true, _, false, _) => "Выберите прямую базовую стену или горизонтальную плиту.",
        (true, true, true, _, true, false, true, _) => "Совместите зоны с выбранной конструкцией по трём контрольным точкам.",
        (true, true, true, false, true, true, true, _) => "Нажмите «Рассчитать раскладку» в этапе 4.",
        (true, true, true, true, true, true, true, false) => "Нажмите «Сравнить с моделью» в этапе 4 — без этого применение недоступно.",
        _ => "Проверьте таблицу изменений и нажмите «Применить изменения»."
    };
}
