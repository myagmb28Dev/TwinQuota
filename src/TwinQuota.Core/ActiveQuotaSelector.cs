namespace TwinQuota.Core;

public static class ActiveQuotaSelector
{
    public static IReadOnlyList<QuotaGroup> Select(
        IReadOnlyList<QuotaGroup> groups,
        ModelAvailability? activeModel)
    {
        if (activeModel is null || groups.Count == 0)
        {
            return [];
        }

        var providerTerms = activeModel.Provider switch
        {
            "Google" => new[] { "Gemini", "Google" },
            "Anthropic" => new[] { "Claude", "Anthropic" },
            "OpenAI" => new[] { "GPT", "OpenAI" },
            _ => Array.Empty<string>()
        };

        var matches = groups
            .Where(group => providerTerms.Any(term =>
                group.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (matches.Length > 0)
        {
            return matches;
        }

        return groups.Count == 1 ? groups : [];
    }
}
