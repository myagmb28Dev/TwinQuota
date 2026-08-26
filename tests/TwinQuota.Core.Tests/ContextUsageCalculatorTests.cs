using TwinQuota.Core;

namespace TwinQuota.Core.Tests;

public sealed class ContextUsageCalculatorTests
{
    [Fact]
    public void CalculatesContextTokensFromActiveSteps()
    {
        const string transcript = """
            {"step_index":0,"source":"USER_EXPLICIT","type":"USER_INPUT","content":"Hello world"}
            {"step_index":1,"source":"MODEL","type":"PLANNER_RESPONSE","content":"Hello! How can I help you today?"}
            """;

        var usage = ContextUsageCalculator.CalculateFromTranscriptContent(transcript, "gemini-3.7-flash-high");

        Assert.NotNull(usage);
        Assert.True(usage.UsedTokens > 0);
        Assert.Equal(1_000_000, usage.MaxTokens);
        Assert.Contains("/", usage.HoverText);
    }

    [Fact]
    public void ResetsContextUsageUponCheckpointCompaction()
    {
        // Step 0 & 1 are before the checkpoint.
        // Step 2 is the CHECKPOINT.
        // Step 3 is after the checkpoint.
        const string transcriptBeforeCheckpoint = """
            {"step_index":0,"source":"USER_EXPLICIT","type":"USER_INPUT","content":"Very long conversation step 0 with lots of tokens..."}
            {"step_index":1,"source":"MODEL","type":"PLANNER_RESPONSE","content":"Very long conversation step 1 with lots of response content..."}
            """;

        const string transcriptAfterCheckpoint = """
            {"step_index":0,"source":"USER_EXPLICIT","type":"USER_INPUT","content":"Very long conversation step 0 with lots of tokens..."}
            {"step_index":1,"source":"MODEL","type":"PLANNER_RESPONSE","content":"Very long conversation step 1 with lots of response content..."}
            {"step_index":2,"source":"SYSTEM","type":"CHECKPOINT","content":"Summary of conversation"}
            {"step_index":3,"source":"MODEL","type":"PLANNER_RESPONSE","content":"Short reply"}
            """;

        var usageBefore = ContextUsageCalculator.CalculateFromTranscriptContent(transcriptBeforeCheckpoint, "gemini-3.7-flash-high");
        var usageAfter = ContextUsageCalculator.CalculateFromTranscriptContent(transcriptAfterCheckpoint, "gemini-3.7-flash-high");

        Assert.NotNull(usageBefore);
        Assert.NotNull(usageAfter);
        // After checkpoint, steps 0 and 1 are excluded from the active window calculation.
        // Only step 2 and step 3 are counted.
        Assert.True(usageAfter.UsedTokens < usageBefore.UsedTokens);
    }

    [Theory]
    [InlineData("gemini-3.7-flash-high", 1_000_000)]
    [InlineData("claude-sonnet-4-6", 200_000)]
    [InlineData("gpt-oss-120b-medium", 128_000)]
    [InlineData(null, 1_000_000)]
    public void UsesConfiguredModelContextLimits(string? modelId, int expectedLimit)
    {
        var limit = ContextUsageCalculator.GetModelContextLimit(modelId);
        Assert.Equal(expectedLimit, limit);
    }

    [Fact]
    public void FormatsHoverTextWithSlashAndRemainingK()
    {
        var usage = ContextUsage.Create(45_200, 200_000);

        Assert.Equal("45k", usage.UsedK);
        Assert.Equal("200k", usage.MaxK);
        Assert.Equal("155k", usage.RemainingK);
        Assert.Equal("45k / 200k (155k remaining)", usage.HoverText);
        Assert.Equal(22.6, usage.UsedPercent);
    }

    [Fact]
    public void HandlesMultipleCheckpointsAndPicksTheLatest()
    {
        const string transcript = """
            {"step_index":0,"source":"USER_EXPLICIT","type":"USER_INPUT","content":"Initial start"}
            {"step_index":1,"source":"SYSTEM","type":"CHECKPOINT","content":"First checkpoint"}
            {"step_index":2,"source":"MODEL","type":"PLANNER_RESPONSE","content":"Middle work"}
            {"step_index":3,"source":"SYSTEM","type":"CHECKPOINT","content":"Second checkpoint"}
            {"step_index":4,"source":"MODEL","type":"PLANNER_RESPONSE","content":"Latest response"}
            """;

        var usage = ContextUsageCalculator.CalculateFromTranscriptContent(transcript, "gemini-3.7-flash-high");

        Assert.NotNull(usage);
        // Only semantic payload from steps 3 and 4 should be counted.
        var step3and4Chars = "Second checkpoint".Length + "Latest response".Length;
        var expectedTokens = (int)Math.Ceiling(step3and4Chars / 4.0);

        Assert.Equal(expectedTokens, usage.UsedTokens);
    }

    [Fact]
    public void HandlesEmptyOrWhitespaceTranscript()
    {
        var usage = ContextUsageCalculator.CalculateFromTranscriptContent("", "gemini-3.7-flash-high");
        Assert.Equal(0, usage.UsedTokens);
        Assert.Equal(0, usage.UsedPercent);
        Assert.Equal("0 / 1000k (1000k remaining)", usage.HoverText);
    }

    [Fact]
    public void ClampsOverflownUsedTokensToMaxTokensForPercentage()
    {
        var usage = ContextUsage.Create(250_000, 200_000);
        Assert.Equal(100.0, usage.UsedPercent);
        Assert.Equal("0", usage.RemainingK);
        Assert.Equal("250k", usage.UsedK);
    }

    [Fact]
    public void IgnoresJsonMetadataWhenEstimatingTokens()
    {
        const string transcript = """
            {"step_index":42,"source":"MODEL","status":"DONE","type":"PLANNER_RESPONSE","content":"12345678"}
            """;

        var usage = ContextUsageCalculator.CalculateFromTranscriptContent(transcript, "gemini-3.7-flash-high");

        Assert.Equal(2, usage.UsedTokens);
    }

    [Fact]
    public void ResolvesTranscriptFromTheActiveConversationOnly()
    {
        var profile = Path.Combine(Path.GetTempPath(), $"TwinQuota.Tests.{Guid.NewGuid():N}");
        try
        {
            var activeLogs = Path.Combine(
                profile,
                ".gemini",
                "antigravity",
                "brain",
                "active-conversation",
                ".system_generated",
                "logs");
            var otherLogs = Path.Combine(
                profile,
                ".gemini",
                "antigravity",
                "brain",
                "other-conversation",
                ".system_generated",
                "logs");
            Directory.CreateDirectory(activeLogs);
            Directory.CreateDirectory(otherLogs);
            var activeTranscript = Path.Combine(activeLogs, "transcript_full.jsonl");
            File.WriteAllText(activeTranscript, "{}");
            var otherTranscript = Path.Combine(otherLogs, "transcript.jsonl");
            File.WriteAllText(otherTranscript, "{}");
            File.SetLastWriteTimeUtc(otherTranscript, DateTime.UtcNow.AddMinutes(1));

            var resolved = ContextUsageCalculator.ResolveTranscriptPath(
                "active-conversation",
                profile);

            Assert.Equal(activeTranscript, resolved);
        }
        finally
        {
            if (Directory.Exists(profile))
            {
                Directory.Delete(profile, true);
            }
        }
    }

    [Fact]
    public void DoesNotFallBackToAnotherConversation()
    {
        var profile = Path.Combine(Path.GetTempPath(), $"TwinQuota.Tests.{Guid.NewGuid():N}");
        try
        {
            var otherLogs = Path.Combine(
                profile,
                ".gemini",
                "antigravity",
                "brain",
                "other-conversation",
                ".system_generated",
                "logs");
            Directory.CreateDirectory(otherLogs);
            File.WriteAllText(Path.Combine(otherLogs, "transcript.jsonl"), "{}");

            var resolved = ContextUsageCalculator.ResolveTranscriptPath(
                "missing-conversation",
                profile);

            Assert.Null(resolved);
        }
        finally
        {
            if (Directory.Exists(profile))
            {
                Directory.Delete(profile, true);
            }
        }
    }
}
