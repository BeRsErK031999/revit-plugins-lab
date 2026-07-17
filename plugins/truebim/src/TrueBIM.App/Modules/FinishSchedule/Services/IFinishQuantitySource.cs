using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public interface IFinishQuantitySource
{
    FinishQuantityResult Calculate(FinishQuantityRequest request);
}
