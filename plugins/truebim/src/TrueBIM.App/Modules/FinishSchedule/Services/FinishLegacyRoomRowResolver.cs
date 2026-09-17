namespace TrueBIM.App.Modules.FinishSchedule.Services;

/// <summary>Восстанавливает связь старой текстовой строки только по однозначным номерам помещений.</summary>
public static class FinishLegacyRoomRowResolver
{
    public static IReadOnlyList<long> Resolve(string roomList, IReadOnlyDictionary<long, string> roomNumbers)
    {
        string[] numbers = roomList.Split(',').Select(value => value.Trim()).ToArray();
        if (numbers.Length == 0 || numbers.Any(string.IsNullOrWhiteSpace)
            || numbers.Distinct(StringComparer.Ordinal).Count() != numbers.Length)
            throw new InvalidOperationException("Перечень номеров помещений пуст или неоднозначен.");

        List<long> result = [];
        foreach (string number in numbers)
        {
            long[] matches = roomNumbers.Where(pair => string.Equals(pair.Value, number, StringComparison.Ordinal))
                .Select(pair => pair.Key).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"Номер помещения «{number}» отсутствует или повторяется в проекте.");
            result.Add(matches[0]);
        }
        return result;
    }
}
