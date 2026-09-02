namespace TwinQuota.Core;

public enum AntigravitySurface
{
    Desktop2,
    Ide,
    Cli,
    VsCode
}

public sealed record ProductStatus(
    AntigravitySurface Surface,
    string DisplayName,
    bool Installed,
    bool Running,
    bool HasLocalData,
    string? Version,
    string? ExecutablePath,
    string Detail);

public sealed record QuotaBucket(
    string Id,
    string DisplayName,
    string Window,
    double RemainingFraction,
    DateTimeOffset? ResetTime,
    string? Description);

public sealed record QuotaGroup(
    string DisplayName,
    string? Description,
    IReadOnlyList<QuotaBucket> Buckets);

public sealed record ModelAvailability(
    string Id,
    string DisplayName,
    string Provider,
    double? RemainingFraction,
    DateTimeOffset? ResetTime);

public sealed record ContextUsage(
    int UsedTokens,
    int MaxTokens,
    double UsedPercent,
    string UsedK,
    string MaxK,
    string RemainingK,
    string HoverText)
{
    public static ContextUsage Create(int usedTokens, int maxTokens)
    {
        var clampedUsed = Math.Max(0, usedTokens);
        var effectiveMax = Math.Max(1, maxTokens);
        var usedPercent = Math.Clamp((double)clampedUsed / effectiveMax * 100.0, 0, 100);
        var remainingTokens = Math.Max(0, effectiveMax - clampedUsed);

        var usedK = FormatK(clampedUsed);
        var maxK = FormatK(effectiveMax);
        var remainingK = FormatK(remainingTokens);
        var hoverText = $"{usedK} / {maxK} ({remainingK} remaining)";

        return new ContextUsage(
            clampedUsed,
            effectiveMax,
            usedPercent,
            usedK,
            maxK,
            remainingK,
            hoverText);
    }

    private static string FormatK(int tokens)
    {
        if (tokens >= 1000)
        {
            var k = tokens / 1000.0;
            return $"{k:0}k";
        }

        return $"{tokens}";
    }
}

public sealed record TwinQuotaSnapshot(
    DateTimeOffset UpdatedAt,
    bool IsLive,
    string? Source,
    IReadOnlyList<ProductStatus> Products,
    IReadOnlyList<QuotaGroup> QuotaGroups,
    IReadOnlyList<ModelAvailability> Models,
    string? Message)
{
    public string? ActiveModelId { get; init; }
    public ContextUsage? ContextUsage { get; init; }

    public static TwinQuotaSnapshot Empty(string message) => new(
        DateTimeOffset.Now,
        false,
        null,
        [],
        [],
        [],
        message);
}

public sealed record LanguageServerEndpoint(
    AntigravitySurface Surface,
    int ProcessId,
    int HttpPort,
    string CsrfToken);
