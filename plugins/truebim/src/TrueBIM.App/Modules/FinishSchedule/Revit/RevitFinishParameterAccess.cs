using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

internal static class RevitFinishParameterAccess
{
    public static FinishParameterWriteCandidate Inspect(
        Document document,
        FinishParameterTargetValue target)
    {
        Element? element = document.GetElement(RevitElementIds.Create(target.ElementId));
        if (element is null)
        {
            return Blocked(
                target,
                FinishWriteIssueCode.TargetElementMissing,
                $"{target.Role}: элемент {target.ElementId} не найден.");
        }

        if (IsOwnedByOtherUser(document, element.Id))
        {
            return Blocked(
                target,
                FinishWriteIssueCode.TargetElementLocked,
                $"{target.Role}: элемент {target.ElementId} занят другим пользователем.");
        }

        Parameter? parameter = FindParameter(element, target.Reference);
        if (parameter is null)
        {
            return Blocked(
                target,
                FinishWriteIssueCode.ParameterMissing,
                $"{target.Role}: параметр «{target.Reference.Name}» отсутствует у элемента {target.ElementId}.");
        }

        if (parameter.StorageType != StorageType.String)
        {
            return Blocked(
                target,
                FinishWriteIssueCode.ParameterStorageMismatch,
                $"{target.Role}: параметр «{target.Reference.Name}» элемента {target.ElementId} не является текстовым.");
        }

        if (parameter.IsReadOnly)
        {
            return Blocked(
                target,
                FinishWriteIssueCode.ParameterReadOnly,
                $"{target.Role}: параметр «{target.Reference.Name}» элемента {target.ElementId} доступен только для чтения.");
        }

        return new FinishParameterWriteCandidate(
            target,
            parameter.AsString() ?? string.Empty);
    }

    public static Parameter ResolveWritableParameter(
        Document document,
        FinishParameterChange change)
    {
        Element? element = document.GetElement(RevitElementIds.Create(change.ElementId));
        if (element is null)
        {
            throw new InvalidOperationException($"Элемент {change.ElementId} не найден после предпросмотра записи.");
        }

        if (IsOwnedByOtherUser(document, element.Id))
        {
            throw new InvalidOperationException($"Элемент {change.ElementId} занят другим пользователем.");
        }

        Parameter? parameter = FindParameter(element, change.Reference);
        if (parameter is null)
        {
            throw new InvalidOperationException(
                $"Параметр «{change.Reference.Name}» отсутствует у элемента {change.ElementId}.");
        }

        if (parameter.StorageType != StorageType.String)
        {
            throw new InvalidOperationException(
                $"Параметр «{change.Reference.Name}» элемента {change.ElementId} не является текстовым.");
        }

        if (parameter.IsReadOnly)
        {
            throw new InvalidOperationException(
                $"Параметр «{change.Reference.Name}» элемента {change.ElementId} доступен только для чтения.");
        }

        return parameter;
    }

    public static bool IsOwnedByOtherUser(Document document, ElementId elementId)
    {
        if (!document.IsWorkshared)
        {
            return false;
        }

        try
        {
            return WorksharingUtils.GetCheckoutStatus(document, elementId)
                == CheckoutStatus.OwnedByOtherUser;
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            return false;
        }
    }

    private static Parameter? FindParameter(Element element, ParameterReference reference)
    {
        foreach (Parameter parameter in element.Parameters)
        {
            ParameterReference? candidate = RevitParameterReferenceFactory.Create(
                parameter,
                ParameterBindingKind.Instance);
            if (candidate?.StableKey == reference.StableKey)
            {
                return parameter;
            }
        }

        return null;
    }

    private static FinishParameterWriteCandidate Blocked(
        FinishParameterTargetValue target,
        FinishWriteIssueCode code,
        string message)
    {
        return new FinishParameterWriteCandidate(target, string.Empty, code, message);
    }
}
