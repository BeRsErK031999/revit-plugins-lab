using TrueBIM.App.Modules.BimTools.ScheduleRegister.Models;

namespace TrueBIM.App.Modules.BimTools.ScheduleRegister.Services;

public static class ScheduleRegisterNameService
{
    public static string CreateUniqueName(IEnumerable<string> existingNames)
    {
        Guard.NotNull(existingNames, nameof(existingNames));
        HashSet<string> names = new(existingNames, StringComparer.CurrentCultureIgnoreCase);
        for (int copyNumber = 2; copyNumber < int.MaxValue; copyNumber++)
        {
            string candidate = $"{ScheduleRegisterConstants.TemplateScheduleName} ({copyNumber})";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Не удалось подобрать уникальное имя ведомости спецификаций.");
    }

    public static bool IsTemplateOrGeneratedCopy(string? scheduleName)
    {
        if (string.Equals(
                scheduleName,
                ScheduleRegisterConstants.TemplateScheduleName,
                StringComparison.CurrentCultureIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(scheduleName)
            || !scheduleName!.StartsWith(
                ScheduleRegisterConstants.TemplateScheduleName + " (",
                StringComparison.CurrentCultureIgnoreCase)
            || !scheduleName.EndsWith(")", StringComparison.Ordinal))
        {
            return false;
        }

        int numberStart = ScheduleRegisterConstants.TemplateScheduleName.Length + 2;
        string number = scheduleName!.Substring(numberStart, scheduleName.Length - numberStart - 1);
        return int.TryParse(number, out int copyNumber) && copyNumber >= 2;
    }
}
