namespace TrueBIM.App.Modules;

public interface ITrueBimModule
{
    string Id { get; }

    string DisplayName { get; }

    string Description { get; }

    bool IsEnabledByDefault { get; }
}
