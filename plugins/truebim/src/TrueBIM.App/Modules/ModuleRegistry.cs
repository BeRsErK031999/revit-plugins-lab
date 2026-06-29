using System.IO;
using TrueBIM.App.Modules.SheetNumbering;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules;

public sealed class ModuleRegistry
{
    private const string RevitVersion = "2025";
    private readonly List<ModuleRegistryEntry> modules = new();

    public IReadOnlyCollection<ModuleRegistryEntry> Modules => modules;

    public static ModuleRegistry CreateForRevit2025(ITrueBimLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string trueBimRoot = Path.Combine(appData, "TrueBIM", RevitVersion);
        string modulesRoot = Path.Combine(trueBimRoot, "Modules");
        string settingsPath = Path.Combine(trueBimRoot, "module-settings.json");

        return Create(
            modulesRoot,
            settingsPath,
            RevitVersion,
            new ModuleManifestLoader(logger),
            new ModuleSettingsService(settingsPath, logger),
            logger);
    }

    public static ModuleRegistry Create(
        string modulesRoot,
        string settingsPath,
        string revitVersion,
        ModuleManifestLoader manifestLoader,
        ModuleSettingsService settingsService,
        ITrueBimLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulesRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(revitVersion);
        ArgumentNullException.ThrowIfNull(manifestLoader);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(logger);

        Dictionary<string, ITrueBimModule> implementations = CreateAvailableImplementations();
        ModuleRegistry registry = new();
        ModuleManifestLoadResult loadResult = manifestLoader.Load(modulesRoot, revitVersion);

        if (!loadResult.ManifestRootExists)
        {
            logger.Warning("TrueBIM module manifest folder is missing. Falling back to built-in development modules.");
            foreach (ITrueBimModule implementation in implementations.Values)
            {
                ModuleManifest fallbackManifest = CreateFallbackManifest(implementation, revitVersion);
                registry.Register(new ModuleRegistryEntry(
                    implementation,
                    fallbackManifest,
                    settingsService.IsEnabled(fallbackManifest)));
            }

            return registry;
        }

        foreach (ModuleManifest manifest in loadResult.Manifests)
        {
            if (!implementations.TryGetValue(manifest.Id, out ITrueBimModule? implementation))
            {
                logger.Warning($"Installed module manifest id '{manifest.Id}' has no known runtime implementation and was skipped.");
                continue;
            }

            registry.Register(new ModuleRegistryEntry(
                implementation,
                manifest,
                settingsService.IsEnabled(manifest)));
        }

        return registry;
    }

    private static Dictionary<string, ITrueBimModule> CreateAvailableImplementations()
    {
        ITrueBimModule[] implementations =
        [
            new SheetNumberingModule()
        ];

        return implementations.ToDictionary(module => module.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static ModuleManifest CreateFallbackManifest(ITrueBimModule module, string revitVersion)
    {
        return new ModuleManifest(
            module.Id,
            module.DisplayName,
            module.Description,
            string.Empty,
            module.IsEnabledByDefault,
            [revitVersion]);
    }

    private void Register(ModuleRegistryEntry module)
    {
        modules.Add(module);
    }
}
