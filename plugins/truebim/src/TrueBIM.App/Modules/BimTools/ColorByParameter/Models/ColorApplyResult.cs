using System.Text;

namespace TrueBIM.App.Modules.BimTools.ColorByParameter.Models;

public sealed class ColorApplyResult
{
    public ColorApplyResult(
        int createdFilterCount,
        int updatedFilterCount,
        int appliedFilterCount,
        int skippedValueCount,
        int clearedFilterCount,
        IReadOnlyList<string> messages)
    {
        CreatedFilterCount = createdFilterCount;
        UpdatedFilterCount = updatedFilterCount;
        AppliedFilterCount = appliedFilterCount;
        SkippedValueCount = skippedValueCount;
        ClearedFilterCount = clearedFilterCount;
        Messages = messages ?? [];
    }

    public int CreatedFilterCount { get; }

    public int UpdatedFilterCount { get; }

    public int AppliedFilterCount { get; }

    public int SkippedValueCount { get; }

    public int ClearedFilterCount { get; }

    public IReadOnlyList<string> Messages { get; }

    public string ToDialogText()
    {
        StringBuilder builder = new();
        builder.AppendLine($"Создано фильтров: {CreatedFilterCount}");
        builder.AppendLine($"Обновлено фильтров: {UpdatedFilterCount}");
        builder.AppendLine($"Применено к активному виду: {AppliedFilterCount}");
        builder.AppendLine($"Пропущено значений: {SkippedValueCount}");

        if (ClearedFilterCount > 0)
        {
            builder.AppendLine($"Очищено фильтров с активного вида: {ClearedFilterCount}");
        }

        if (Messages.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Сообщения:");
            foreach (string message in Messages.Take(8))
            {
                builder.AppendLine($"- {message}");
            }
        }

        return builder.ToString().TrimEnd();
    }
}
