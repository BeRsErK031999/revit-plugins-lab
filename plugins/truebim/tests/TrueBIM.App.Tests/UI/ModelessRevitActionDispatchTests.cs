using System.Reflection;
using TrueBIM.App.Modules.BimTools.AutoTags.UI;
using TrueBIM.App.Modules.BimTools.ClashReport.UI;
using TrueBIM.App.Modules.BimTools.ColorByParameter.UI;
using TrueBIM.App.Modules.BimTools.DatumExtents.UI;
using TrueBIM.App.Modules.BimTools.OpeningViews.UI;
using TrueBIM.App.Modules.BimTools.TitleBlockFill.UI;
using TrueBIM.App.Modules.BimTools.Worksets.UI;
using TrueBIM.App.Modules.IsoFieldRebar.UI;
using TrueBIM.App.Modules.Lintels.UI;
using TrueBIM.App.Modules.Print.UI;
using TrueBIM.App.Modules.SheetNumbering.UI;
using TrueBIM.App.Modules.SharedParameters.UI;
using Xunit;

namespace TrueBIM.App.Tests.UI;

public sealed class ModelessRevitActionDispatchTests
{
    public static TheoryData<Type> ModelessWindowsWithRevitActions => new()
    {
        typeof(ColorByParameterWindow),
        typeof(CreateWorksetsWindow),
        typeof(ClashReportWindow),
        typeof(AutoTagWindow),
        typeof(IsoFieldRebarWindow),
        typeof(LintelsWindow),
        typeof(DatumExtentWindow),
        typeof(OpeningViewsWindow),
        typeof(TitleBlockFillWindow),
        typeof(PrintWindow),
        typeof(SheetNumberingWindow),
        typeof(SharedParameterInspectorWindow)
    };

    [Theory]
    [MemberData(nameof(ModelessWindowsWithRevitActions))]
    public void ModelessWindow_WithRevitActions_OwnsExternalEventDispatcher(Type windowType)
    {
        FieldInfo? dispatcherField = windowType
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .SingleOrDefault(field => string.Equals(field.Name, "revitActions", StringComparison.Ordinal));

        Assert.NotNull(dispatcherField);
    }
}
