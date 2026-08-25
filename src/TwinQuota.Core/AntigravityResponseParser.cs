using System.Text.Json;

namespace TwinQuota.Core;

public static class AntigravityResponseParser
{
    public static IReadOnlyList<QuotaGroup> ParseQuotaSummary(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!TryGetResponse(document.RootElement, out var response)
            || !response.TryGetProperty("groups", out var groupsElement)
            || groupsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var groups = new List<QuotaGroup>();
        foreach (var groupElement in groupsElement.EnumerateArray())
        {
            var displayName = GetString(groupElement, "displayName") ?? "Quota";
            var description = GetString(groupElement, "description");
            var buckets = new List<QuotaBucket>();

            if (groupElement.TryGetProperty("buckets", out var bucketsElement)
                && bucketsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var bucketElement in bucketsElement.EnumerateArray())
                {
                    var id = GetString(bucketElement, "bucketId") ?? Guid.NewGuid().ToString("N");
                    var bucketName = GetString(bucketElement, "displayName") ?? id;
                    var window = GetString(bucketElement, "window") ?? "unknown";
                    var remaining = GetDouble(bucketElement, "remainingFraction") ?? 0;
                    buckets.Add(new QuotaBucket(
                        id,
                        bucketName,
                        window,
                        Math.Clamp(remaining, 0, 1),
                        GetDateTimeOffset(bucketElement, "resetTime"),
                        GetString(bucketElement, "description")));
                }
            }

            groups.Add(new QuotaGroup(displayName, description, buckets));
        }

        return groups;
    }

    public static IReadOnlyList<ModelAvailability> ParseAvailableModels(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!TryGetResponse(document.RootElement, out var response)
            || !response.TryGetProperty("models", out var modelsElement)
            || modelsElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var recommendedIds = ReadRecommendedModelIds(response);
        var models = new List<ModelAvailability>();

        foreach (var property in modelsElement.EnumerateObject())
        {
            if (recommendedIds.Count > 0 && !recommendedIds.Contains(property.Name))
            {
                continue;
            }

            var model = property.Value;
            var displayName = GetString(model, "displayName");
            if (string.IsNullOrWhiteSpace(displayName))
            {
                continue;
            }

            var provider = NormalizeProvider(GetString(model, "modelProvider"));
            double? remaining = null;
            DateTimeOffset? resetTime = null;
            if (model.TryGetProperty("quotaInfo", out var quotaInfo))
            {
                remaining = GetDouble(quotaInfo, "remainingFraction");
                resetTime = GetDateTimeOffset(quotaInfo, "resetTime");
            }

            models.Add(new ModelAvailability(
                property.Name,
                displayName,
                provider,
                remaining is null ? null : Math.Clamp(remaining.Value, 0, 1),
                resetTime));
        }

        return models
            .OrderBy(model => ProviderOrder(model.Provider))
            .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<ModelAvailability> ParseCliModels(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            var results = new List<ModelAvailability>();
            CollectCliModels(document.RootElement, results);
            if (results.Count > 0)
            {
                return results
                    .DistinctBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(model => ProviderOrder(model.Provider))
                    .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
        catch (JsonException)
        {
            // Older CLI builds print an aligned text table.
        }

        var textModels = new List<ModelAvailability>();
        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var separator = line.IndexOf("  ", StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var id = line[..separator].Trim();
            var displayName = line[separator..].Trim();
            if (id.Length == 0 || displayName.Length == 0 || id.Contains(' '))
            {
                continue;
            }

            textModels.Add(new ModelAvailability(id, displayName, InferProvider(displayName), null, null));
        }

        return textModels;
    }

    private static HashSet<string> ReadRecommendedModelIds(JsonElement response)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!response.TryGetProperty("agentModelSorts", out var sorts)
            || sorts.ValueKind != JsonValueKind.Array)
        {
            return ids;
        }

        foreach (var sort in sorts.EnumerateArray())
        {
            if (!sort.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var group in groups.EnumerateArray())
            {
                if (!group.TryGetProperty("modelIds", out var modelIds) || modelIds.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var modelId in modelIds.EnumerateArray())
                {
                    if (modelId.GetString() is { Length: > 0 } id)
                    {
                        ids.Add(id);
                    }
                }
            }
        }

        return ids;
    }

    private static void CollectCliModels(JsonElement element, List<ModelAvailability> results)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                CollectCliModels(child, results);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var id = GetString(element, "id")
            ?? GetString(element, "slug")
            ?? GetString(element, "modelId");
        var displayName = GetString(element, "displayName")
            ?? GetString(element, "name")
            ?? GetString(element, "label");
        if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(displayName))
        {
            var provider = GetString(element, "provider") ?? InferProvider(displayName);
            results.Add(new ModelAvailability(id, displayName, NormalizeProvider(provider), null, null));
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
            {
                CollectCliModels(property.Value, results);
            }
        }
    }

    private static bool TryGetResponse(JsonElement root, out JsonElement response)
    {
        if (root.TryGetProperty("response", out response) && response.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        response = root;
        return root.ValueKind == JsonValueKind.Object;
    }

    private static string NormalizeProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return "Other";
        }

        var value = provider
            .Replace("MODEL_PROVIDER_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace('_', ' ')
            .Trim();
        return value.ToUpperInvariant() switch
        {
            "GOOGLE" or "GEMINI" => "Google",
            "ANTHROPIC" or "CLAUDE" => "Anthropic",
            "OPENAI" or "GPT" => "OpenAI",
            _ => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant())
        };
    }

    private static string InferProvider(string displayName)
    {
        if (displayName.Contains("Claude", StringComparison.OrdinalIgnoreCase))
        {
            return "Anthropic";
        }

        if (displayName.Contains("GPT", StringComparison.OrdinalIgnoreCase))
        {
            return "OpenAI";
        }

        if (displayName.Contains("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            return "Google";
        }

        return "Other";
    }

    private static int ProviderOrder(string provider) => provider switch
    {
        "Google" => 0,
        "Anthropic" => 1,
        "OpenAI" => 2,
        _ => 3
    };

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? GetDouble(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.TryGetDouble(out var result)
            ? result
            : null;

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string propertyName) =>
        GetString(element, propertyName) is { } value
        && DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var result)
            ? result
            : null;
}
