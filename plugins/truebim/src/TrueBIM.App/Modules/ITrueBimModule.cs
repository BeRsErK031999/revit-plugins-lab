using TrueBIM.App.UI;

namespace TrueBIM.App.Modules;

public interface ITrueBimModule
{
    string Id { get; }

    string DisplayName { get; }

    string Description { get; }

    TrueBimIcon Icon { get; }

    bool IsEnabledByDefault { get; }
}
