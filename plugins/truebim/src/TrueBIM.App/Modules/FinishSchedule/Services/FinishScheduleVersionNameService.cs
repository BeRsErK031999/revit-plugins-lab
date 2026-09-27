namespace TrueBIM.App.Modules.FinishSchedule.Services;

public static class FinishScheduleVersionNameService
{
    public static string CreateUniqueName(string baseName, IEnumerable<string> existingNames)
    {
        if (string.IsNullOrWhiteSpace(baseName))
        {
            throw new ArgumentException("Название ведомости не задано.", nameof(baseName));
        }

        string name = baseName.Trim();
        HashSet<string> used = new(existingNames, StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(name))
        {
            return name;
        }

        for (int version = 2; ; version++)
        {
            string candidate = $"{name} ({version})";
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
