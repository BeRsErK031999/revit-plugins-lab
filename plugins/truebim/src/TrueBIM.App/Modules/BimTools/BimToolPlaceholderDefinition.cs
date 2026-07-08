using TrueBIM.App.UI;

namespace TrueBIM.App.Modules.BimTools;

public sealed record BimToolPlaceholderDefinition(
    string Title,
    TrueBimIcon Icon,
    string Description,
    IReadOnlyList<string> WorkflowSteps,
    IReadOnlyList<string> ImplementationScope,
    string SettingsKey = "general");
