using TwinQuota.Core;

namespace TwinQuota.Core.Tests;

public sealed class ActiveModelResolverTests
{
    [Fact]
    public void PrefersTheActuallyInvokedPriorityOverTheDefaultModel()
    {
        const string json = """
            {
              "response": {
                "defaultAgentModelId": "gemini-3.7-flash-high",
                "models": {
                  "gemini-3.7-flash-high": {
                    "displayName": "Gemini 3.7 Flash (High)",
                    "modelProvider": "MODEL_PROVIDER_GOOGLE"
                  },
                  "gemini-3.7-flash-medium": {
                    "displayName": "Gemini 3.7 Flash (Medium)",
                    "modelProvider": "MODEL_PROVIDER_GOOGLE"
                  }
                }
              }
            }
            """;
        var observation = new ActiveModelObservation(
            "gemini-3.7-flash-medium",
            DateTimeOffset.Now);

        var resolution = ActiveModelResolver.Resolve(json, observation);

        Assert.NotNull(resolution.ActiveModel);
        Assert.Equal("gemini-3.7-flash-medium", resolution.ActiveModel.Id);
        Assert.Equal("Gemini 3.7 Flash (Medium)", resolution.ActiveModel.DisplayName);
    }

    [Fact]
    public void FallsBackToTheDefaultWhenTheInvokedModelIsNoLongerAvailable()
    {
        const string json = """
            {
              "response": {
                "defaultAgentModelId": "gemini-3.7-flash-high",
                "models": {
                  "gemini-3.7-flash-high": {
                    "displayName": "Gemini 3.7 Flash (High)",
                    "modelProvider": "MODEL_PROVIDER_GOOGLE"
                  }
                }
              }
            }
            """;
        var observation = new ActiveModelObservation("removed-model", DateTimeOffset.Now);

        var resolution = ActiveModelResolver.Resolve(json, observation);

        Assert.NotNull(resolution.ActiveModel);
        Assert.Equal("gemini-3.7-flash-high", resolution.ActiveModel.Id);
    }
}
