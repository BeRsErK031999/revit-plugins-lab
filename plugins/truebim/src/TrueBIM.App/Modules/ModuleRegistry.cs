using TrueBIM.App.Modules.SheetNumbering;

namespace TrueBIM.App.Modules;

public sealed class ModuleRegistry
{
    private readonly List<ITrueBimModule> modules = new();

    public IReadOnlyCollection<ITrueBimModule> Modules => modules;

    public static ModuleRegistry CreateDefault()
    {
        ModuleRegistry registry = new();
        registry.Register(new SheetNumberingModule());
        return registry;
    }

    private void Register(ITrueBimModule module)
    {
        modules.Add(module);
    }
}
