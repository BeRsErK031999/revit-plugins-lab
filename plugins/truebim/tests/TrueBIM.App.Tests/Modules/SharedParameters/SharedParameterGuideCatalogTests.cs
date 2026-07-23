using TrueBIM.App.Modules.SharedParameters.UI;
using Xunit;

namespace TrueBIM.App.Tests.Modules.SharedParameters;

public sealed class SharedParameterGuideCatalogTests
{
    [Fact]
    public void Overview_DescribesAllMainTabsAndSafeWorkflow()
    {
        SharedParameterGuidePage page = SharedParameterGuideCatalog.Get(
            SharedParameterGuideTopic.Overview);
        string text = Flatten(page);

        Assert.Contains("«Проект»", text);
        Assert.Contains("«Семейства»", text);
        Assert.Contains("«Отчёт»", text);
        Assert.Contains("dry run", text);
        Assert.Contains("не сохраняет проект", text);
    }

    [Fact]
    public void ContextTopics_HaveUniqueCompletePages()
    {
        Assert.Equal(5, SharedParameterGuideCatalog.ContextTopics.Count);

        SharedParameterGuidePage[] pages = SharedParameterGuideCatalog.ContextTopics
            .Select(SharedParameterGuideCatalog.Get)
            .ToArray();

        Assert.Equal(pages.Length, pages.Select(page => page.Title).Distinct().Count());
        Assert.All(pages, page =>
        {
            Assert.False(string.IsNullOrWhiteSpace(page.Title));
            Assert.False(string.IsNullOrWhiteSpace(page.Summary));
            Assert.True(page.Sections.Count >= 3);
            Assert.All(page.Sections, section =>
            {
                Assert.False(string.IsNullOrWhiteSpace(section.Title));
                Assert.True(section.Items.Count >= 3);
                Assert.DoesNotContain(section.Items, string.IsNullOrWhiteSpace);
            });
        });
    }

    [Fact]
    public void SafeDeletion_StatesConfirmationRollbackAndHonestResult()
    {
        SharedParameterGuidePage page = SharedParameterGuideCatalog.Get(
            SharedParameterGuideTopic.SafeDeletion);
        string text = Flatten(page);

        Assert.Contains("dry run", text);
        Assert.Contains("откатит", text);
        Assert.Contains("подтверждения", text);
        Assert.Contains("не сохраняется", text);
        Assert.Contains("«полностью удалён» не используется", text);
    }

    private static string Flatten(SharedParameterGuidePage page)
    {
        return string.Join(
            "\n",
            new[] { page.Title, page.Summary }
                .Concat(page.Sections.Select(section => section.Title))
                .Concat(page.Sections.SelectMany(section => section.Items)));
    }
}
