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
                StatusText: $"Исправьте настройки: найдено ошибок — {validation.Issues.Count}.",
                GenerateToolTip: "Сначала исправьте все ошибки совместимости параметров и обязательных полей.");
        }

        if (!workflowAvailable)
        {
            return new FinishScheduleLaunchState(
                IsConfigurationValid: true,
                CanGenerate: false,
                StatusText: "Настройки совместимы и готовы к сохранению. Расчёт будет подключён на следующем этапе.",
                GenerateToolTip: "Конфигурация валидна. Формирование станет доступно после подключения сбора помещений и элементов отделки.");
        }

        return new FinishScheduleLaunchState(
            IsConfigurationValid: true,
            CanGenerate: true,
            StatusText: "Настройки совместимы. Можно сформировать ведомость.",
            GenerateToolTip: "Сформировать ведомость отделки по текущим настройкам.");
    }
}
