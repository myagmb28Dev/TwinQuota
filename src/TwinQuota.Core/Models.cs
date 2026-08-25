namespace TwinQuota.Core;

public enum AntigravitySurface
{
    Desktop2,
    Ide,
    Cli
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

public sealed record TwinQuotaSnapshot(
    DateTimeOffset UpdatedAt,
    bool IsLive,
    string? Source,
    IReadOnlyList<ProductStatus> Products,
    IReadOnlyList<QuotaGroup> QuotaGroups,
    IReadOnlyList<ModelAvailability> Models,
    string? Message)
{
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
