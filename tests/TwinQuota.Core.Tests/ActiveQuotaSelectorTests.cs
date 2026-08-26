using TwinQuota.Core;

namespace TwinQuota.Core.Tests;

public sealed class ActiveQuotaSelectorTests
{
    private static readonly QuotaGroup Gemini = new("Gemini Models", null, []);
    private static readonly QuotaGroup ThirdParty = new("Claude and GPT models", null, []);

    [Theory]
    [InlineData("Google", "Gemini Models")]
    [InlineData("Anthropic", "Claude and GPT models")]
    [InlineData("OpenAI", "Claude and GPT models")]
    public void SelectsOnlyTheQuotaGroupForTheActiveProvider(string provider, string expectedGroup)
    {
        var model = new ModelAvailability("active", "Active model", provider, null, null);

        var selected = ActiveQuotaSelector.Select([Gemini, ThirdParty], model);

        Assert.Single(selected);
        Assert.Equal(expectedGroup, selected[0].DisplayName);
    }

    [Fact]
    public void ReturnsNoQuotaGroupsWithoutAnActiveModel()
    {
        Assert.Empty(ActiveQuotaSelector.Select([Gemini, ThirdParty], null));
    }
}
