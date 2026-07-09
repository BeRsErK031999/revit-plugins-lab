namespace TrueBIM.App.Modules.Print.Models;

public sealed class DwgExportProfileStoreState
{
    public List<DwgExportProfile> Profiles { get; set; } = [];

    public string? LastSelectedProfileName { get; set; }

    public string? LastFolder { get; set; }

    public string? LastNameMask { get; set; }

    public string? LastFormatSelection { get; set; }

    public static DwgExportProfileStoreState Empty { get; } = new();

    public DwgExportProfile? FindProfile(string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return null;
        }

        return Profiles.FirstOrDefault(profile =>
            string.Equals(profile.ProfileName, profileName!.Trim(), StringComparison.CurrentCultureIgnoreCase));
    }

    public void UpsertProfile(DwgExportProfile profile)
    {
        Guard.NotNull(profile, nameof(profile));

        string profileName = string.IsNullOrWhiteSpace(profile.ProfileName)
            ? DwgExportProfile.DefaultProfileName
            : profile.ProfileName.Trim();
        profile.ProfileName = profileName;

        int existingIndex = Profiles.FindIndex(existing =>
            string.Equals(existing.ProfileName, profileName, StringComparison.CurrentCultureIgnoreCase));
        if (existingIndex >= 0)
        {
            Profiles[existingIndex] = profile.Clone();
        }
        else
        {
            Profiles.Add(profile.Clone());
        }

        LastSelectedProfileName = profileName;
    }
}
