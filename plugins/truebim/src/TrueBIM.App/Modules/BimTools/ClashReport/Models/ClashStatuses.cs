namespace TrueBIM.App.Modules.BimTools.ClashReport.Models;

public static class ClashStatuses
{
    public static ClashStatus Parse(string? value)
    {
        string trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return ClashStatus.Open;
        }

        string normalized = Normalize(trimmed);
        return normalized switch
        {
            "open" or "new" or "открыто" or "новая" => ClashStatus.Open,
            "inprogress" or "progress" or "work" or "active" or "вработе" or "активная" => ClashStatus.InProgress,
            "approved" or "approve" or "согласовано" or "согласована" => ClashStatus.Approved,
            "resolved" or "closed" or "done" or "решено" or "закрыто" => ClashStatus.Resolved,
            "ignored" or "ignore" or "пропущено" or "игнор" => ClashStatus.Ignored,
            _ when Enum.TryParse(trimmed, ignoreCase: true, out ClashStatus status) => status,
            _ => ClashStatus.Open
        };
    }

    public static string ToDisplayName(ClashStatus status)
    {
        return status switch
        {
            ClashStatus.Open => "New",
            ClashStatus.InProgress => "Active",
            ClashStatus.Approved => "Approved",
            ClashStatus.Resolved => "Resolved",
            ClashStatus.Ignored => "Ignored",
            _ => "New"
        };
    }

    private static string Normalize(string value)
    {
        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }
}
