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
                    "maxTokens": 1048576,
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
        Assert.Equal(1_048_576, models[0].MaxTokens);
    }

    [Fact]
    public void ParsesOnlyTheDefaultActiveAgentModel()
    {
        const string json = """
            {
              "response": {
                "defaultAgentModelId": "claude-sonnet-4-6",
                "models": {
                  "gemini-3.7-flash-high": {
                    "displayName": "Gemini 3.7 Flash (High)",
                    "modelProvider": "MODEL_PROVIDER_GOOGLE"
                  },
                  "claude-sonnet-4-6": {
                    "displayName": "Claude Sonnet 4.6 (Thinking)",
                    "modelProvider": "MODEL_PROVIDER_ANTHROPIC",
                    "quotaInfo": { "remainingFraction": 0.42, "resetTime": "2026-08-26T15:43:33Z" }
                  }
                }
              }
            }
            """;

        var model = AntigravityResponseParser.ParseActiveModel(json);

        Assert.NotNull(model);
        Assert.Equal("claude-sonnet-4-6", model.Id);
        Assert.Equal("Anthropic", model.Provider);
        Assert.Equal(0.42, model.RemainingFraction);
    }

    [Fact]
    public void ReturnsNoActiveModelWhenTheServerDoesNotReportADefault()
    {
        const string json = """
            { "response": { "models": {
              "gemini-3.7-flash-high": {
                "displayName": "Gemini 3.7 Flash (High)",
                "modelProvider": "MODEL_PROVIDER_GOOGLE"
              }
            } } }
            """;

        Assert.Null(AntigravityResponseParser.ParseActiveModel(json));
    }

    [Fact]
    public void ResolvesTheActuallyInvokedPriorityVariantById()
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

        var model = AntigravityResponseParser.ParseModelById(json, "gemini-3.7-flash-medium");

        Assert.NotNull(model);
        Assert.Equal("gemini-3.7-flash-medium", model.Id);
        Assert.Equal("Gemini 3.7 Flash (Medium)", model.DisplayName);
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

    [Fact]
    public void ParsesTrajectoryStepCountForTheObservedConversation()
    {
        const string json = """
            {
              "trajectorySummaries": {
                "conversation-123": { "stepCount": 487 },
                "another-conversation": { "stepCount": 12 }
              }
            }
            """;

        var summary = AntigravityResponseParser.ParseTrajectorySummary(json, "conversation-123");

        Assert.NotNull(summary);
        Assert.Equal(487, summary.StepCount);
    }

    [Fact]
    public void ParsesLatestCompletedModelContextFromActualUsageMetadata()
    {
        const string json = """
            {
              "steps": [
                { "metadata": { "modelUsage": {
                  "inputTokens": "1200",
                  "cacheReadTokens": "8000",
                  "outputTokens": "300"
                } } },
                { "metadata": { "modelUsage": {
                  "inputTokens": 2400,
                  "cacheReadTokens": 15000,
                  "outputTokens": 600
                } } },
                { "metadata": {} }
              ]
            }
            """;

        var tokens = AntigravityResponseParser.ParseLatestContextTokens(json);

        Assert.Equal(18_000, tokens);
    }

    [Fact]
    public void ParsesTheLatestAntigravityContextWindowEstimate()
    {
        const string json = """
            {
              "generatorMetadata": [
                { "chatModel": { "chatStartMetadata": { "contextWindowMetadata": {
                  "estimatedTokensUsed": 65489,
                  "maxContextTokens": 256000
                } } } },
                { "otherGenerator": {} },
                { "chatModel": { "chatStartMetadata": { "contextWindowMetadata": {
                  "estimatedTokensUsed": "66101",
                  "maxContextTokens": 256000
                } } } }
              ]
            }
            """;

        var page = AntigravityResponseParser.ParseGeneratorMetadataPage(json);

        Assert.Equal(3, page.ItemCount);
        Assert.NotNull(page.LatestContextWindowUsage);
        Assert.Equal(66_101, page.LatestContextWindowUsage.UsedTokens);
        Assert.Equal(256_000, page.LatestContextWindowUsage.MaxTokens);
    }
}
