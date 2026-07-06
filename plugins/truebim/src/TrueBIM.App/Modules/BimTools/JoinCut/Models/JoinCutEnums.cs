namespace TrueBIM.App.Modules.BimTools.JoinCut.Models;

public enum JoinCutTab
{
    Join,
    Cut
}

public enum JoinAction
{
    Join,
    Unjoin,
    SwitchJoinOrder
}

public enum CutAction
{
    Cut,
    Uncut
}

public enum ProcessingScope
{
    SelectedElements,
    ActiveView,
    EntireProject
}

public enum FilterLogicalOperator
{
    And,
    Or
}

public enum ParameterCompareOperator
{
    Equals,
    NotEquals,
    Contains,
    NotContains,
    Exists,
    NotExists,
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual
}
