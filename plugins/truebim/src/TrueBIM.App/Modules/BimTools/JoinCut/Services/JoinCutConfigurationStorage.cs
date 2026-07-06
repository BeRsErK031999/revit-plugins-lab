using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrueBIM.App.Modules.BimTools.JoinCut.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.JoinCut.Services;

public sealed class JoinCutConfigurationStorage
{
    public const string DefaultConfigurationId = "default";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private readonly string settingsPath;
    private readonly ITrueBimLogger logger;

    public JoinCutConfigurationStorage(string settingsPath, ITrueBimLogger logger)
    {
        Guard.NotNullOrWhiteSpace(settingsPath, nameof(settingsPath));
        this.settingsPath = settingsPath;
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static string CreateDefaultSettingsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TrueBIM",
            "JoinCut",
            "configurations.json");
    }

    public JoinCutConfigurationLoadResult Load()
    {
        if (!File.Exists(settingsPath))
        {
            JoinCutConfigurationState defaultState = CreateDefaultState();
            Save(defaultState);
            logger.Info($"Join/Cut configuration file was created: '{settingsPath}'.");
            return new JoinCutConfigurationLoadResult(defaultState, null);
        }

        try
        {
            JoinCutConfigurationStateDto? dto = JsonSerializer.Deserialize<JoinCutConfigurationStateDto>(
                File.ReadAllText(settingsPath),
                SerializerOptions);

            JoinCutConfigurationState state = NormalizeState(ToState(dto));
            return new JoinCutConfigurationLoadResult(state, null);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            string backupPath = CreateBackupPath(settingsPath);
            TryBackup(backupPath);

            JoinCutConfigurationState defaultState = CreateDefaultState();
            Save(defaultState);

            string message = $"Файл конфигураций Join/Cut поврежден или недоступен. Создана новая стандартная конфигурация, резервная копия: {backupPath}";
            logger.Warning($"{message}. {exception.Message}");
            return new JoinCutConfigurationLoadResult(defaultState, message);
        }
    }

    public void Save(JoinCutConfigurationState state)
    {
        Guard.NotNull(state, nameof(state));
        JoinCutConfigurationState normalizedState = NormalizeState(state);

        string? directory = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(settingsPath, JsonSerializer.Serialize(ToDto(normalizedState), SerializerOptions));
    }

    public JoinCutConfiguration CreateConfiguration(string name)
    {
        return new JoinCutConfiguration
        {
            Name = name
        };
    }

    public JoinRule CreateJoinRule(string name)
    {
        return new JoinRule
        {
            Name = name
        };
    }

    public CutRule CreateCutRule(string name)
    {
        return new CutRule
        {
            Name = name
        };
    }

    private static JoinCutConfigurationState CreateDefaultState()
    {
        JoinCutConfiguration defaultConfiguration = new()
        {
            Id = DefaultConfigurationId,
            Name = "Стандартная",
            IsDefault = true
        };

        return new JoinCutConfigurationState
        {
            SelectedConfigurationId = DefaultConfigurationId,
            Configurations = [defaultConfiguration]
        };
    }

    private static JoinCutConfigurationState NormalizeState(JoinCutConfigurationState? state)
    {
        state ??= new JoinCutConfigurationState();
        state.Configurations ??= [];

        foreach (JoinCutConfiguration configuration in state.Configurations)
        {
            NormalizeConfiguration(configuration);
        }

        JoinCutConfiguration? defaultConfiguration = state.Configurations
            .FirstOrDefault(configuration => configuration.IsDefault || string.Equals(configuration.Id, DefaultConfigurationId, StringComparison.Ordinal));
        if (defaultConfiguration is null)
        {
            defaultConfiguration = CreateDefaultState().Configurations[0];
            state.Configurations.Insert(0, defaultConfiguration);
        }
        else
        {
            defaultConfiguration.Id = DefaultConfigurationId;
            defaultConfiguration.IsDefault = true;
            if (string.IsNullOrWhiteSpace(defaultConfiguration.Name))
            {
                defaultConfiguration.Name = "Стандартная";
            }
        }

        if (string.IsNullOrWhiteSpace(state.SelectedConfigurationId)
            || state.Configurations.All(configuration => !string.Equals(configuration.Id, state.SelectedConfigurationId, StringComparison.Ordinal)))
        {
            state.SelectedConfigurationId = defaultConfiguration.Id;
        }

        return state;
    }

    private static JoinCutConfigurationState ToState(JoinCutConfigurationStateDto? dto)
    {
        return new JoinCutConfigurationState
        {
            SelectedConfigurationId = dto?.SelectedConfigurationId,
            Configurations = dto?.Configurations?.Select(ToConfiguration).ToList() ?? []
        };
    }

    private static JoinCutConfiguration ToConfiguration(JoinCutConfigurationDto dto)
    {
        return new JoinCutConfiguration
        {
            Id = dto.Id ?? string.Empty,
            Name = dto.Name ?? string.Empty,
            IsDefault = dto.IsDefault,
            JoinRules = dto.JoinRules?.Select(ToJoinRule).ToList() ?? [],
            CutRules = dto.CutRules?.Select(ToCutRule).ToList() ?? [],
            LastSelectedTab = dto.LastSelectedTab,
            LastSelectedScope = dto.LastSelectedScope,
            LastSelectedJoinAction = dto.LastSelectedJoinAction,
            LastSelectedCutAction = dto.LastSelectedCutAction
        };
    }

    private static JoinRule ToJoinRule(JoinRuleDto dto)
    {
        return new JoinRule
        {
            Id = dto.Id ?? string.Empty,
            Name = dto.Name ?? string.Empty,
            LeftFilter = ToFilter(dto.LeftFilter),
            RightFilter = ToFilter(dto.RightFilter),
            OnlyParallelWalls = dto.OnlyParallelWalls,
            Enabled = dto.Enabled
        };
    }

    private static CutRule ToCutRule(CutRuleDto dto)
    {
        return new CutRule
        {
            Id = dto.Id ?? string.Empty,
            Name = dto.Name ?? string.Empty,
            CuttingElementsFilter = ToFilter(dto.CuttingElementsFilter),
            CutElementsFilter = ToFilter(dto.CutElementsFilter),
            SplitFacesOfCuttingSolid = dto.SplitFacesOfCuttingSolid,
            Enabled = dto.Enabled
        };
    }

    private static ElementFilterDefinition ToFilter(ElementFilterDefinitionDto? dto)
    {
        return new ElementFilterDefinition
        {
            Categories = dto?.Categories?
                .Select(ParseBuiltInCategory)
                .Where(category => category.HasValue)
                .Select(category => category!.Value)
                .ToList() ?? [],
            ParameterConditions = dto?.ParameterConditions?.Select(ToParameterCondition).ToList() ?? [],
            CategoryAndParameterOperator = dto?.CategoryAndParameterOperator ?? FilterLogicalOperator.And
        };
    }

    private static ParameterFilterCondition ToParameterCondition(ParameterFilterConditionDto dto)
    {
        return new ParameterFilterCondition
        {
            ParameterName = dto.ParameterName,
            ParameterGuid = Guid.TryParse(dto.ParameterGuid, out Guid parameterGuid) ? parameterGuid : null,
            BuiltInParameter = ParseBuiltInParameter(dto.BuiltInParameter),
            Operator = dto.Operator,
            Value = dto.Value
        };
    }

    private static JoinCutConfigurationStateDto ToDto(JoinCutConfigurationState state)
    {
        return new JoinCutConfigurationStateDto
        {
            SelectedConfigurationId = state.SelectedConfigurationId,
            Configurations = state.Configurations.Select(ToDto).ToList()
        };
    }

    private static JoinCutConfigurationDto ToDto(JoinCutConfiguration configuration)
    {
        return new JoinCutConfigurationDto
        {
            Id = configuration.Id,
            Name = configuration.Name,
            IsDefault = configuration.IsDefault,
            JoinRules = configuration.JoinRules.Select(ToDto).ToList(),
            CutRules = configuration.CutRules.Select(ToDto).ToList(),
            LastSelectedTab = configuration.LastSelectedTab,
            LastSelectedScope = configuration.LastSelectedScope,
            LastSelectedJoinAction = configuration.LastSelectedJoinAction,
            LastSelectedCutAction = configuration.LastSelectedCutAction
        };
    }

    private static JoinRuleDto ToDto(JoinRule rule)
    {
        return new JoinRuleDto
        {
            Id = rule.Id,
            Name = rule.Name,
            LeftFilter = ToDto(rule.LeftFilter),
            RightFilter = ToDto(rule.RightFilter),
            OnlyParallelWalls = rule.OnlyParallelWalls,
            Enabled = rule.Enabled
        };
    }

    private static CutRuleDto ToDto(CutRule rule)
    {
        return new CutRuleDto
        {
            Id = rule.Id,
            Name = rule.Name,
            CuttingElementsFilter = ToDto(rule.CuttingElementsFilter),
            CutElementsFilter = ToDto(rule.CutElementsFilter),
            SplitFacesOfCuttingSolid = rule.SplitFacesOfCuttingSolid,
            Enabled = rule.Enabled
        };
    }

    private static ElementFilterDefinitionDto ToDto(ElementFilterDefinition filter)
    {
        return new ElementFilterDefinitionDto
        {
            Categories = filter.Categories.Select(category => category.ToString()).ToList(),
            ParameterConditions = filter.ParameterConditions.Select(ToDto).ToList(),
            CategoryAndParameterOperator = filter.CategoryAndParameterOperator
        };
    }

    private static ParameterFilterConditionDto ToDto(ParameterFilterCondition condition)
    {
        return new ParameterFilterConditionDto
        {
            ParameterName = condition.ParameterName,
            ParameterGuid = condition.ParameterGuid?.ToString("D"),
            BuiltInParameter = condition.BuiltInParameter?.ToString(),
            Operator = condition.Operator,
            Value = condition.Value
        };
    }

    private static void NormalizeConfiguration(JoinCutConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.Id))
        {
            configuration.Id = Guid.NewGuid().ToString("N");
        }

        if (string.IsNullOrWhiteSpace(configuration.Name))
        {
            configuration.Name = "Новая конфигурация";
        }

        configuration.JoinRules ??= [];
        configuration.CutRules ??= [];

        foreach (JoinRule rule in configuration.JoinRules)
        {
            NormalizeJoinRule(rule);
        }

        foreach (CutRule rule in configuration.CutRules)
        {
            NormalizeCutRule(rule);
        }
    }

    private static void NormalizeJoinRule(JoinRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Id))
        {
            rule.Id = Guid.NewGuid().ToString("N");
        }

        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            rule.Name = "Новое правило соединения";
        }

        rule.LeftFilter ??= new ElementFilterDefinition();
        rule.RightFilter ??= new ElementFilterDefinition();
        NormalizeFilter(rule.LeftFilter);
        NormalizeFilter(rule.RightFilter);
    }

    private static void NormalizeCutRule(CutRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Id))
        {
            rule.Id = Guid.NewGuid().ToString("N");
        }

        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            rule.Name = "Новое правило вырезания";
        }

        rule.CuttingElementsFilter ??= new ElementFilterDefinition();
        rule.CutElementsFilter ??= new ElementFilterDefinition();
        NormalizeFilter(rule.CuttingElementsFilter);
        NormalizeFilter(rule.CutElementsFilter);
    }

    private static void NormalizeFilter(ElementFilterDefinition filter)
    {
        filter.Categories ??= [];
        filter.ParameterConditions ??= [];
    }

    private static Autodesk.Revit.DB.BuiltInCategory? ParseBuiltInCategory(string? value)
    {
        return Enum.TryParse(value, ignoreCase: false, out Autodesk.Revit.DB.BuiltInCategory category)
            ? category
            : null;
    }

    private static Autodesk.Revit.DB.BuiltInParameter? ParseBuiltInParameter(string? value)
    {
        return Enum.TryParse(value, ignoreCase: false, out Autodesk.Revit.DB.BuiltInParameter parameter)
            ? parameter
            : null;
    }

    private static string CreateBackupPath(string path)
    {
        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string fileName = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        string timestamp = DateTimeOffset.Now.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        return Path.Combine(directory, $"{fileName}.backup-{timestamp}{extension}");
    }

    private void TryBackup(string backupPath)
    {
        try
        {
            File.Copy(settingsPath, backupPath, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Warning($"Failed to create Join/Cut configuration backup '{backupPath}': {exception.Message}");
        }
    }
}

public sealed record JoinCutConfigurationLoadResult(
    JoinCutConfigurationState State,
    string? WarningMessage);

public sealed class JoinCutConfigurationState
{
    public string? SelectedConfigurationId { get; set; }

    public List<JoinCutConfiguration> Configurations { get; set; } = [];
}

internal sealed class JoinCutConfigurationStateDto
{
    public string? SelectedConfigurationId { get; set; }

    public List<JoinCutConfigurationDto>? Configurations { get; set; }
}

internal sealed class JoinCutConfigurationDto
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public bool IsDefault { get; set; }

    public List<JoinRuleDto>? JoinRules { get; set; }

    public List<CutRuleDto>? CutRules { get; set; }

    public JoinCutTab LastSelectedTab { get; set; } = JoinCutTab.Join;

    public ProcessingScope LastSelectedScope { get; set; } = ProcessingScope.SelectedElements;

    public JoinAction LastSelectedJoinAction { get; set; } = JoinAction.Join;

    public CutAction LastSelectedCutAction { get; set; } = CutAction.Cut;
}

internal sealed class JoinRuleDto
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public ElementFilterDefinitionDto? LeftFilter { get; set; }

    public ElementFilterDefinitionDto? RightFilter { get; set; }

    public bool OnlyParallelWalls { get; set; }

    public bool Enabled { get; set; } = true;
}

internal sealed class CutRuleDto
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public ElementFilterDefinitionDto? CuttingElementsFilter { get; set; }

    public ElementFilterDefinitionDto? CutElementsFilter { get; set; }

    public bool SplitFacesOfCuttingSolid { get; set; }

    public bool Enabled { get; set; } = true;
}

internal sealed class ElementFilterDefinitionDto
{
    public List<string>? Categories { get; set; }

    public List<ParameterFilterConditionDto>? ParameterConditions { get; set; }

    public FilterLogicalOperator CategoryAndParameterOperator { get; set; } = FilterLogicalOperator.And;
}

internal sealed class ParameterFilterConditionDto
{
    public string? ParameterName { get; set; }

    public string? ParameterGuid { get; set; }

    public string? BuiltInParameter { get; set; }

    public ParameterCompareOperator Operator { get; set; } = ParameterCompareOperator.Equals;

    public string? Value { get; set; }
}
