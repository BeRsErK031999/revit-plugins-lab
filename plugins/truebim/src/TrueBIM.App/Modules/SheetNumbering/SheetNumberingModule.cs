using TrueBIM.App.UI;

namespace TrueBIM.App.Modules.SheetNumbering;

public sealed class SheetNumberingModule : ITrueBimModule
{
    public string Id => "truebim.sheet-numbering";

    public string DisplayName => "Нумератор листов";

    public string Description => "Перенумерация листов Revit с предпросмотром и защитой от дублей.";

    public TrueBimIcon Icon => TrueBimIcon.SheetNumbering;

    public bool IsEnabledByDefault => true;
}
