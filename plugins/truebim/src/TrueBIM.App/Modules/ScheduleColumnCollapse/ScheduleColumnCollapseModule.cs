using TrueBIM.App.UI;

namespace TrueBIM.App.Modules.ScheduleColumnCollapse;

public sealed class ScheduleColumnCollapseModule : ITrueBimModule
{
    public string Id => "truebim.schedule-column-collapse";

    public string DisplayName => "Свернуть ВРС";

    public string Description => "Скрывает столбцы выбранной спецификации, где все значения равны нулю.";

    public TrueBimIcon Icon => TrueBimIcon.ScheduleCollapse;

    public bool IsEnabledByDefault => true;
}
