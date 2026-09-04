using System.Text.Json;

namespace TwinQuota.Core;

public static class ContextUsageCalculator
{
    public const int DefaultContextLimit = 1_000_000;

    public static int GetModelContextLimit(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return DefaultContextLimit;
        }

        var id = modelId.ToLowerInvariant();
        if (id.Contains("gemini"))
        {
            return 1_000_000; // Gemini: 1,000,000 (1M / 1000k)
        }

        if (id.Contains("claude"))
        {
            return 200_000; // Claude: 200,000 (200k)
        }

        if (id.Contains("gpt"))
        {
            return 128_000; // GPT: 128,000 (128k)
        }

        return DefaultContextLimit;
    }

    public static ContextUsage? Calculate(
        string? conversationId,
        string? modelId,
        string? userProfile = null,
        int? modelContextLimit = null)
    {
        var resolvedPath = ResolveTranscriptPath(conversationId, userProfile);
        if (resolvedPath is null)
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            return CalculateFromTranscriptContent(content, modelId, modelContextLimit);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static ContextUsage CalculateFromTranscriptContent(
        string transcriptContent,
        string? modelId,
        int? modelContextLimit = null)
    {
        var maxTokens = modelContextLimit is > 0
            ? modelContextLimit.Value
            : GetModelContextLimit(modelId);
        if (string.IsNullOrWhiteSpace(transcriptContent))
        {
            return ContextUsage.Create(0, maxTokens);
        }

        var lines = transcriptContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return ContextUsage.Create(0, maxTokens);
        }

        var lastCheckpointIndex = -1;
        var stepList = new List<(int StepIndex, int PayloadLength)>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var stepIndex = i;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("step_index", out var stepIdxElem) && stepIdxElem.TryGetInt32(out var idx))
                {
                    stepIndex = idx;
                }

                if (root.TryGetProperty("type", out var typeElem) &&
                    typeElem.ValueKind == JsonValueKind.String &&
                    typeElem.ValueEquals("CHECKPOINT"))
                {
                    lastCheckpointIndex = stepIndex;
                }

                stepList.Add((stepIndex, EstimatePayloadLength(root)));
            }
            catch (JsonException)
            {
                // The writer may leave a partial final JSONL record while a response is streaming.
                // Ignore it until the next refresh instead of counting transport syntax as context.
            }
        }

        var activeChars = 0;
        foreach (var (stepIndex, payloadLength) in stepList)
        {
            if (lastCheckpointIndex < 0 || stepIndex >= lastCheckpointIndex)
            {
                activeChars += payloadLength;
            }
        }

        // Antigravity does not expose exact tokenizer counts in the hook payload. Keep this
        // explicitly approximate and count semantic payload fields rather than JSONL metadata.
        var estimatedTokens = (int)Math.Ceiling(activeChars / 4.0);
        return ContextUsage.Create(estimatedTokens, maxTokens);
    }

    public static string? ResolveTranscriptPath(
        string? conversationId,
        string? userProfile = null)
    {
        var profile = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var brainDirectory = Path.GetFullPath(Path.Combine(profile, ".gemini", "antigravity", "brain"));
        if (!Directory.Exists(brainDirectory))
        {
            return null;
        }

        try
        {
            var conversationDirectory = ResolveConversationDirectory(brainDirectory, conversationId);
            if (conversationDirectory is null)
            {
                return null;
            }

            var logDirectory = Path.Combine(conversationDirectory, ".system_generated", "logs");
            return new[]
                {
                    Path.Combine(logDirectory, "transcript_full.jsonl"),
                    Path.Combine(logDirectory, "transcript.jsonl")
                }
                .FirstOrDefault(File.Exists);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string? ResolveConversationDirectory(string brainDirectory, string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId) ||
            !conversationId.Equals(Path.GetFileName(conversationId), StringComparison.Ordinal))
        {
            return null;
        }

        var conversationDirectory = Path.GetFullPath(Path.Combine(brainDirectory, conversationId));
        return IsInsideDirectory(conversationDirectory, brainDirectory)
            ? conversationDirectory
            : null;
    }

    private static bool IsInsideDirectory(string path, string directory)
    {
        var normalizedDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory))
            + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static int EstimatePayloadLength(JsonElement root) =>
        EstimatePropertyLength(root, "content") +
        EstimatePropertyLength(root, "thinking") +
        EstimatePropertyLength(root, "tool_calls");

    private static int EstimatePropertyLength(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return 0;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Length ?? 0
            : value.GetRawText().Length;
    }
}
