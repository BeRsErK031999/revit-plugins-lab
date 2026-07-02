using TrueBIM.App.UI;

namespace TrueBIM.App.Modules.Print;

public sealed class PrintModule : ITrueBimModule
{
    public string Id => "truebim.print";

    public string DisplayName => "Печать";

    public string Description => "Пакетная печать и экспорт листов Revit в PDF, DWG и DXF.";

    public TrueBimIcon Icon => TrueBimIcon.Print;

    public bool IsEnabledByDefault => true;
}
