using TwinQuota.Core;

namespace TwinQuota.Core.Tests;

public sealed class ActiveModelObservationStoreTests
{
    [Fact]
    public void ParsesOnlyTheHookFieldsNeededForActiveModelTracking()
    {
        const string payload = """
            {
              "conversationId": "conversation-123",
              "workspacePaths": ["C:\\private\\workspace"],
              "transcriptPath": "C:\\private\\transcript.jsonl",
              "modelName": "gemini-3.7-flash-medium",
              "invocationNum": 2
            }
            """;
        var observedAt = new DateTimeOffset(2026, 8, 26, 4, 12, 0, TimeSpan.Zero);

        var observation = ActiveModelObservationStore.ParseHookPayload(payload, observedAt);

        Assert.NotNull(observation);
        Assert.Equal("gemini-3.7-flash-medium", observation.ModelId);
        Assert.Equal(observedAt, observation.ObservedAt);
        Assert.Equal("conversation-123", observation.ConversationId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{ \"conversationId\": \"conversation-123\" }")]
    [InlineData("{ \"modelName\": 123 }")]
    public void IgnoresHookPayloadsWithoutAValidModel(string payload)
    {
        Assert.Null(ActiveModelObservationStore.ParseHookPayload(payload, DateTimeOffset.Now));
    }

    [Fact]
    public async Task RoundTripsTheLatestObservedModel()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"TwinQuota.Tests.{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "active-model.json");
        try
        {
            var store = new ActiveModelObservationStore(path);
            var expected = new ActiveModelObservation(
                "gemini-3.7-flash-medium",
                new DateTimeOffset(2026, 8, 26, 4, 12, 0, TimeSpan.Zero));

            await store.SaveAsync(expected);
            var actual = await store.LoadAsync();

            Assert.Equal(expected, actual);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
