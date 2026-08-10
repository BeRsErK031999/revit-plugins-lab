using TrueBIM.App.Modules.BimTools.ParameterAudit.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ParameterAudit;

public sealed class ParameterAuditGuideCatalogTests
{
    [Fact]
    public void Guide_CoversRequiredWorkflowAndSafetyBoundaries()
    {
        string text = string.Join(
            "\n",
            ParameterAuditGuideCatalog.Sections.SelectMany(section =>
                new[] { section.Title }.Concat(section.Steps)));

        Assert.Contains("шаблон CSV", text, StringComparison.CurrentCultureIgnoreCase);
        Assert.Contains("A1", text, StringComparison.Ordinal);
        Assert.Contains("знак +", text, StringComparison.CurrentCultureIgnoreCase);
        Assert.Contains("GUID", text, StringComparison.Ordinal);
        Assert.Contains("пробел", text, StringComparison.CurrentCultureIgnoreCase);
        Assert.Contains("RVT-связ", text, StringComparison.CurrentCultureIgnoreCase);
        Assert.Contains("ничего не записывает", text, StringComparison.CurrentCultureIgnoreCase);
        Assert.Contains("Экспорт ошибок CSV", text, StringComparison.CurrentCultureIgnoreCase);
    }
}
