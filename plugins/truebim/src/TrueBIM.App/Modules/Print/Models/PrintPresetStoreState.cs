namespace TrueBIM.App.Modules.Print.Models;

public sealed class PrintPresetStoreState
{
    public List<PrintPreset> Presets { get; set; } = [];

    public string? LastSelectedPresetName { get; set; }

    public PrintPreset? FindPreset(string? presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
        {
            return null;
        }

        return Presets.FirstOrDefault(preset =>
            string.Equals(preset.Name, presetName!.Trim(), StringComparison.CurrentCultureIgnoreCase));
    }

    public void UpsertPreset(PrintPreset preset)
    {
        Guard.NotNull(preset, nameof(preset));

        string presetName = string.IsNullOrWhiteSpace(preset.Name)
            ? PrintPreset.DefaultPresetName
            : preset.Name.Trim();
        PrintPreset normalizedPreset = preset.Clone();
        normalizedPreset.Name = presetName;

        int existingIndex = Presets.FindIndex(existing =>
            string.Equals(existing.Name, presetName, StringComparison.CurrentCultureIgnoreCase));
        if (existingIndex >= 0)
        {
            Presets[existingIndex] = normalizedPreset;
        }
        else
        {
            Presets.Add(normalizedPreset);
        }

        LastSelectedPresetName = presetName;
    }

    public bool RemovePreset(string? presetName)
    {
        PrintPreset? preset = FindPreset(presetName);
        if (preset is null)
        {
            return false;
        }

        Presets.Remove(preset);
        LastSelectedPresetName = Presets.FirstOrDefault()?.Name;
        return true;
    }
}
