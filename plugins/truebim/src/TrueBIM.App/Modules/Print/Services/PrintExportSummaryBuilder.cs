using TrueBIM.App.Modules.Print.Models;

namespace TrueBIM.App.Modules.Print.Services;

public static class PrintExportSummaryBuilder
{
    private const int MainContentItemLimit = 3;

    public static PrintExportSummary Build(
        IReadOnlyList<string> exportedFiles,
        int failureCount,
        IReadOnlyList<string> failureMessages)
    {
        Guard.NotNull(exportedFiles, nameof(exportedFiles));
        Guard.NotNull(failureMessages, nameof(failureMessages));
        if (failureCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(failureCount));
        }

        List<string> actualPaths = exportedFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        List<string> actualFailures = failureMessages
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => message.Trim())
            .ToList();

        string mainInstruction = GetMainInstruction(actualPaths.Count, failureCount);
        string mainContent = BuildMainContent(actualPaths, failureCount, actualFailures);
        string? expandedContent = BuildExpandedContent(actualPaths, actualFailures);
        return new PrintExportSummary(mainInstruction, mainContent, expandedContent, actualPaths.Count);
    }

    private static string GetMainInstruction(int exportedFileCount, int failureCount)
    {
        if (exportedFileCount == 0)
        {
            return failureCount > 0
                ? "Экспорт не выполнен"
                : "Экспорт завершен без файлов";
        }

        return failureCount > 0
            ? "Экспорт завершен с ошибками"
            : "Экспорт завершен";
    }

    private static string BuildMainContent(
        IReadOnlyList<string> exportedFiles,
        int failureCount,
        IReadOnlyList<string> failureMessages)
    {
        List<string> lines = new()
        {
            $"Экспортировано файлов: {exportedFiles.Count}",
            $"Ошибок: {failureCount}"
        };

        if (exportedFiles.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Созданные файлы:");
            lines.AddRange(exportedFiles.Take(MainContentItemLimit).Select(path => $"• {path}"));
            if (exportedFiles.Count > MainContentItemLimit)
            {
                lines.Add($"• Ещё файлов: {exportedFiles.Count - MainContentItemLimit}. Полный список — в подробностях.");
            }
        }
        else
        {
            lines.Add(string.Empty);
            lines.Add("Файлы не созданы.");
        }

        if (failureMessages.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Ошибки:");
            lines.AddRange(failureMessages.Take(MainContentItemLimit).Select(message => $"• {message}"));
            if (failureMessages.Count > MainContentItemLimit)
            {
                lines.Add($"• Ещё ошибок: {failureMessages.Count - MainContentItemLimit}. Полный список — в подробностях.");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string? BuildExpandedContent(
        IReadOnlyList<string> exportedFiles,
        IReadOnlyList<string> failureMessages)
    {
        if (exportedFiles.Count <= MainContentItemLimit
            && failureMessages.Count <= MainContentItemLimit)
        {
            return null;
        }

        List<string> lines = new();
        if (exportedFiles.Count > 0)
        {
            lines.Add($"Созданные файлы ({exportedFiles.Count}):");
            lines.AddRange(exportedFiles.Select((path, index) => $"{index + 1}. {path}"));
        }

        if (failureMessages.Count > 0)
        {
            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            lines.Add($"Ошибки ({failureMessages.Count}):");
            lines.AddRange(failureMessages.Select((message, index) => $"{index + 1}. {message}"));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
