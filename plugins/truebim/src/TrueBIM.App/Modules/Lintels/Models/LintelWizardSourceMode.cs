namespace TrueBIM.App.Modules.Lintels.Models;

public enum LintelWizardSourceMode
{
    CurrentSelection,
    ActiveView,
    EntireProject,
    ExistingItems
}

public sealed record LintelWizardSourceOption(
    LintelWizardSourceMode Mode,
    string Title,
    string Description,
    bool IsAvailable,
    string? UnavailableReason)
{
    public string StatusText => IsAvailable ? "Можно выбрать" : "Следующий этап";
}

public sealed class LintelWizardSourceSelection
{
    private readonly IReadOnlyDictionary<LintelWizardSourceMode, LintelWizardSourceOption> options;

    public LintelWizardSourceSelection(bool hasCurrentSelection)
    {
        Options = LintelWizardSourceCatalog.Create(hasCurrentSelection);
        options = Options.ToDictionary(option => option.Mode);
        SelectedMode = LintelWizardSourceCatalog.ResolveDefault(Options).Mode;
    }

    public IReadOnlyList<LintelWizardSourceOption> Options { get; }

    public LintelWizardSourceMode SelectedMode { get; private set; }

    public LintelWizardSourceOption SelectedOption => options[SelectedMode];

    public bool CanContinue => SelectedOption.IsAvailable;

    public bool TrySelect(LintelWizardSourceMode mode)
    {
        if (!options.TryGetValue(mode, out LintelWizardSourceOption? option) || !option.IsAvailable)
        {
            return false;
        }

        SelectedMode = mode;
        return true;
    }
}

public static class LintelWizardSourceCatalog
{
    public static IReadOnlyList<LintelWizardSourceOption> Create(bool hasCurrentSelection)
    {
        return
        [
            new LintelWizardSourceOption(
                LintelWizardSourceMode.CurrentSelection,
                "Текущее выделение",
                "Использовать перемычки, которые уже выделены в Revit.",
                hasCurrentSelection,
                hasCurrentSelection ? null : "Сначала выделите нужную перемычку в Revit и откройте мастер снова."),
            new LintelWizardSourceOption(
                LintelWizardSourceMode.ActiveView,
                "Активный вид",
                "Найти семейства перемычек, которые видны на открытом виде.",
                true,
                null),
            new LintelWizardSourceOption(
                LintelWizardSourceMode.EntireProject,
                "Весь проект",
                "Найти семейства перемычек во всём открытом проекте.",
                false,
                "Поиск по всему проекту будет подключён на следующем этапе."),
            new LintelWizardSourceOption(
                LintelWizardSourceMode.ExistingItems,
                "Уже созданные",
                "Показать сборки, виды и изображения, ранее созданные TrueBIM.",
                false,
                "Список уже созданных элементов будет подключён отдельным этапом.")
        ];
    }

    public static LintelWizardSourceOption ResolveDefault(IReadOnlyList<LintelWizardSourceOption> options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return options.FirstOrDefault(option => option.IsAvailable)
            ?? throw new InvalidOperationException("Не найден доступный источник перемычек.");
    }

    public static string GetTitle(LintelWizardSourceMode mode)
    {
        return Create(hasCurrentSelection: true)
            .First(option => option.Mode == mode)
            .Title;
    }
}
