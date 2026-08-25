using TwinQuota.Core;

namespace TwinQuota.Core.Tests;

public sealed class AntigravityResponseParserTests
{
    [Fact]
    public void ParsesGeminiAndThirdPartyQuotaGroups()
    {
        const string json = """
            {
              "response": {
                "groups": [
                  {
                    "displayName": "Gemini Models",
                    "description": "Gemini Flash, Gemini Pro",
                    "buckets": [
                      {
                        "bucketId": "gemini-weekly",
                        "displayName": "Weekly Limit Remaining",
                        "window": "weekly",
                        "remainingFraction": 0.75,
                        "resetTime": "2026-09-01T09:52:01Z"
                      }
                    ]
                  },
                  {
                    "displayName": "Claude and GPT models",
                    "buckets": [
                      {
                        "bucketId": "3p-5h",
                        "displayName": "Five Hour Limit Remaining",
                        "window": "5h",
                        "remainingFraction": 1,
                        "resetTime": "2026-08-25T15:43:33Z"
                      }
                    ]
                  }
                ]
              }
            }
            """;

        var groups = AntigravityResponseParser.ParseQuotaSummary(json);

        Assert.Equal(2, groups.Count);
        Assert.Equal("gemini-weekly", groups[0].Buckets[0].Id);
        Assert.Equal(0.75, groups[0].Buckets[0].RemainingFraction);
        Assert.Equal("Claude and GPT models", groups[1].DisplayName);
        Assert.Equal("5h", groups[1].Buckets[0].Window);
    }

    [Fact]
    public void ParsesOnlyRecommendedAgentModelsAndNormalizesProviders()
    {
        const string json = """
            {
              "response": {
                "models": {
                  "gemini-3.7-flash-high": {
                    "displayName": "Gemini 3.7 Flash (High)",
                    "modelProvider": "MODEL_PROVIDER_GOOGLE",
                    "quotaInfo": { "remainingFraction": 0.8, "resetTime": "2026-08-25T14:52:01Z" }
                  },
                  "claude-sonnet-4-6": {
                    "displayName": "Claude Sonnet 4.6 (Thinking)",
                    "modelProvider": "MODEL_PROVIDER_ANTHROPIC",
                    "quotaInfo": { "remainingFraction": 1 }
                  },
                  "gpt-oss-120b-medium": {
                    "displayName": "GPT-OSS 120B (Medium)",
                    "modelProvider": "MODEL_PROVIDER_OPENAI",
                    "quotaInfo": { "remainingFraction": 1 }
                  },
                  "internal-model": {
                    "displayName": "Internal model",
                    "modelProvider": "MODEL_PROVIDER_GOOGLE"
                  }
                },
                "agentModelSorts": [
                  { "groups": [ { "modelIds": ["gemini-3.7-flash-high", "claude-sonnet-4-6", "gpt-oss-120b-medium"] } ] }
                ]
              }
            }
            """;

        var models = AntigravityResponseParser.ParseAvailableModels(json);

        Assert.Equal(3, models.Count);
        Assert.Collection(
            models,
            model => Assert.Equal("Google", model.Provider),
            model => Assert.Equal("Anthropic", model.Provider),
            model => Assert.Equal("OpenAI", model.Provider));
        Assert.DoesNotContain(models, model => model.Id == "internal-model");
    }

    [Fact]
    public void ParsesCliJsonAndLegacyTextLists()
    {
        const string json = """
            { "models": [
              { "id": "gemini-3.7-flash-high", "displayName": "Gemini 3.7 Flash (High)" },
              { "slug": "claude-sonnet-4-6", "name": "Claude Sonnet 4.6 (Thinking)" }
            ] }
            """;
        const string text = """
            gemini-3.7-flash-high     Gemini 3.7 Flash (High)
            gpt-oss-120b-medium       GPT-OSS 120B (Medium)
            """;

        var jsonModels = AntigravityResponseParser.ParseCliModels(json);
        var textModels = AntigravityResponseParser.ParseCliModels(text);

        Assert.Equal(2, jsonModels.Count);
        Assert.Equal("Anthropic", jsonModels[1].Provider);
        Assert.Equal(2, textModels.Count);
        Assert.Equal("OpenAI", textModels[1].Provider);
    }
}
