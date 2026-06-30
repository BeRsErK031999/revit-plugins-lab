namespace TrueBIM.App.Modules.SheetNumbering;

public sealed class SheetNumberingModule : ITrueBimModule
{
    public string Id => "truebim.sheet-numbering";

    public string DisplayName => "Нумерация листов";

    public string Description => "Renumber Revit sheets with preview and duplicate protection.";

    public bool IsEnabledByDefault => true;
}
