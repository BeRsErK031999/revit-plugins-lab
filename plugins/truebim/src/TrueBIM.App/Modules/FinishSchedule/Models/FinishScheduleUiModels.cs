namespace TrueBIM.App.Modules.FinishSchedule.Models;

public sealed record FinishScheduleLevelOption(
    long ElementId,
    string DisplayName,
    double Elevation);

public sealed record FinishScheduleParameterOption(
    ParameterReference Reference,
    string DisplayName);

public sealed record FinishScheduleLaunchState(
    bool IsConfigurationValid,
    bool CanGenerate,
    string StatusText,
    string GenerateToolTip)
{
    public static FinishScheduleLaunchState Create(
        FinishScheduleValidationResult validation,
        bool workflowAvailable)
    {
        if (validation is null)
        {
            throw new ArgumentNullException(nameof(validation));
        }

        if (!validation.IsValid)
        {
            return new FinishScheduleLaunchState(
                IsConfigurationValid: false,
                CanGenerate: false,
                StatusText: $"Настройка не завершена: обязательных или несовместимых полей — {validation.Issues.Count}.",
                GenerateToolTip: "Сначала заполните обязательные поля и выберите совместимые параметры.");
        }

        if (!workflowAvailable)
        {
            return new FinishScheduleLaunchState(
                IsConfigurationValid: true,
                CanGenerate: false,
                StatusText: "Настройки совместимы и готовы к сохранению. Workflow записи недоступен.",
                GenerateToolTip: "Конфигурация валидна, но текущий документ недоступен для записи.");
        }

        return new FinishScheduleLaunchState(
            IsConfigurationValid: true,
            CanGenerate: true,
            StatusText: "Настройки совместимы. Можно рассчитать и записать параметры помещений.",
            GenerateToolTip: "Рассчитать отделку и атомарно записать параметры. Создание спецификации будет подключено в FS-008.");
    }
}
