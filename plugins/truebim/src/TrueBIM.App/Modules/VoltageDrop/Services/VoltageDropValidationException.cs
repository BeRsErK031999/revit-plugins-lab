using TrueBIM.App.Modules.VoltageDrop.Models;

namespace TrueBIM.App.Modules.VoltageDrop.Services;

public sealed class VoltageDropValidationException : Exception
{
    public VoltageDropValidationException(IReadOnlyList<VoltageDropValidationError> errors)
        : base(CreateMessage(errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<VoltageDropValidationError> Errors { get; }

    private static string CreateMessage(IReadOnlyList<VoltageDropValidationError> errors)
    {
        return errors.Count == 0
            ? "Проверьте исходные данные расчета."
            : string.Join(Environment.NewLine, errors.Select(error => error.Message));
    }
}
