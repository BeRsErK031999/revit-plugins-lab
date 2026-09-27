using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public static class FinishDescriptionSourceRules
{
    // BuiltInParameter.SYMBOL_NAME_PARAM (also ALL_MODEL_TYPE_NAME).
    private const long TypeNameParameterId = -1002001;

    public static bool IsTypeName(ParameterReference? reference)
    {
        return reference is not null
            && reference.IdentityKind == ParameterIdentityKind.BuiltIn
            && reference.BuiltInParameterId == TypeNameParameterId;
    }
}
