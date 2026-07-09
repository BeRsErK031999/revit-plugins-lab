using Autodesk.Revit.Attributes;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class OpenViewVisibilityCommand : ViewVisibilityCommandBase
{
    protected override string DisplayName => "Все";

    protected override bool ShowAllCategories => true;
}
