using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Vernadskogo.App.Core;

namespace Vernadskogo.App.Revit;

internal static class PanelWriter
{
    public static void Write(Document document, IReadOnlyList<PanelMark> marks,
        IReadOnlyDictionary<string, string> numbers, StartDirection direction)
    {
        using var transaction = new Transaction(document, "Вернадского — маркировка панелей");
        var failures = new WriteFailures();
        transaction.Start();
        transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
            .SetFailuresPreprocessor(failures).SetClearAfterRollback(true).SetForcedModalHandling(true));
        foreach (PanelMark mark in marks)
        {
            Element element = document.GetElement(new ElementId(mark.Panel.Id));
            Set(element, PanelReader.NamedParameter(element, "• Секция"), "• Секция", mark.Section);
            Set(element, PanelReader.NamedParameter(element, "GP_Этаж"), "GP_Этаж", mark.Panel.Floor);
            Set(element, element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK), "Марка", mark.Mark);
        }
        SettingsStorage.Write(document, numbers, direction);
        if (transaction.Commit() != TransactionStatus.Committed)
            throw new InvalidOperationException("Revit не смог сохранить маркировку. Изменения отменены.\n" + failures.Message);
    }

    private static void Set(Element element, Parameter? parameter, string name, string value)
    {
        // No preflight or comparison with existing values: write directly in one transaction.
        if (parameter == null) throw PanelReader.Error(element, "отсутствует параметр экземпляра «" + name + "».");
        try
        {
            if (parameter.StorageType == StorageType.String) parameter.Set(value);
            else if (name == "GP_Этаж" && parameter.StorageType == StorageType.Integer)
                parameter.Set(int.Parse(value, CultureInfo.InvariantCulture));
            else if (name == "GP_Этаж" && parameter.StorageType == StorageType.Double
                && (parameter.Definition.GetDataType() == SpecTypeId.Currency
                    || parameter.Definition.GetDataType() == SpecTypeId.Number))
                // Currency and Number store the numeric value without a physical-unit conversion.
                // Keep the existing parameter definition and project display format unchanged.
                parameter.Set(double.Parse(value, CultureInfo.InvariantCulture));
            else throw new InvalidOperationException("неподдерживаемый тип параметра.");
        }
        catch (Exception exception)
        {
            throw PanelReader.Error(element, "не удалось записать «" + name + "»: " + exception.Message);
        }
        // Set may return false for unchanged values; verify only that Revit accepted the write.
        bool accepted = parameter.StorageType == StorageType.Double
            ? Math.Abs(parameter.AsDouble() - double.Parse(value, CultureInfo.InvariantCulture)) < 1e-9
            : (parameter.StorageType == StorageType.String ? parameter.AsString()
                : parameter.AsInteger().ToString(CultureInfo.InvariantCulture)) == value;
        if (!accepted) throw PanelReader.Error(element, "Revit не записал значение в «" + name + "».");
    }

    private sealed class WriteFailures : IFailuresPreprocessor
    {
        public string Message { get; private set; } = "";

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var errors = accessor.GetFailureMessages().Where(m => m.GetSeverity() == FailureSeverity.Error).ToArray();
            if (errors.Length == 0) return FailureProcessingResult.Continue;
            Message = string.Join("\n", errors.Select(m => m.GetDescriptionText()).Distinct());
            return FailureProcessingResult.ProceedWithRollBack;
        }
    }
}
