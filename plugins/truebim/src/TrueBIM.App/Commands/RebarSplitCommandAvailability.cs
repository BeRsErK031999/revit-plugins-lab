using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace TrueBIM.App.Commands;

public sealed class RebarSplitCommandAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
    {
        try
        {
            // Revit опрашивает доступность в контексте только для чтения:
            // IsReadOnly здесь не определяет возможность запуска команды.
            // Предварительное выделение не требуется: наборы можно выбрать в команде.
            return applicationData.ActiveUIDocument is { Document.IsFamilyDocument: false };
        }
        catch
        {
            return false;
        }
    }
}
