using System.Text.Json;

namespace TwinQuota.Core;

public sealed class TwinQuotaMonitor
{
    private readonly LanguageServerEndpointDiscovery _endpointDiscovery;
    private readonly AntigravityInstallationDetector _installationDetector;
    private readonly AntigravityRpcClient _rpcClient;
    private readonly SnapshotCache _cache;
    private readonly ActiveModelObservationStore _activeModelStore;

    public TwinQuotaMonitor(
        LanguageServerEndpointDiscovery? endpointDiscovery = null,
        AntigravityInstallationDetector? installationDetector = null,
        AntigravityRpcClient? rpcClient = null,
        AntigravityCliClient? cliClient = null,
        SnapshotCache? cache = null,
        ActiveModelObservationStore? activeModelStore = null)
    {
        _endpointDiscovery = endpointDiscovery ?? new LanguageServerEndpointDiscovery();
        _installationDetector = installationDetector ?? new AntigravityInstallationDetector();
        _rpcClient = rpcClient ?? new AntigravityRpcClient();
        _cache = cache ?? new SnapshotCache();
        _activeModelStore = activeModelStore ?? new ActiveModelObservationStore();
    }

    public async Task<TwinQuotaSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var endpoints = await _endpointDiscovery.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var products = _installationDetector.Detect(endpoints);
        var failures = new List<string>();

        foreach (var endpoint in endpoints)
        {
            try
            {
                var quotaTask = _rpcClient.GetQuotaSummaryAsync(endpoint, cancellationToken);
                var modelsTask = _rpcClient.GetAvailableModelsAsync(endpoint, cancellationToken);
                var observedModel = await _activeModelStore.LoadAsync(cancellationToken).ConfigureAwait(false);
                var contextUsageTask = observedModel?.ConversationId is { Length: > 0 } conversationId
                    ? TryGetCurrentContextUsageAsync(endpoint, conversationId, cancellationToken)
                    : Task.FromResult<ContextWindowUsage?>(null);
                await Task.WhenAll(quotaTask, modelsTask).ConfigureAwait(false);
                var quotaGroups = AntigravityResponseParser.ParseQuotaSummary(await quotaTask.ConfigureAwait(false));
                var modelsJson = await modelsTask.ConfigureAwait(false);
                var modelResolution = ActiveModelResolver.Resolve(modelsJson, observedModel);
                var models = modelResolution.Models;
                var activeModel = modelResolution.ActiveModel;

                var liveContextUsage = await contextUsageTask.ConfigureAwait(false);
                var contextLimit = activeModel?.MaxTokens;
                var contextUsage = liveContextUsage is { } liveUsage
                    ? ContextUsage.Create(
                        liveUsage.UsedTokens,
                        liveUsage.MaxTokens is > 0
                            ? liveUsage.MaxTokens.Value
                            : contextLimit is > 0
                                ? contextLimit.Value
                            : ContextUsageCalculator.GetModelContextLimit(activeModel?.Id ?? observedModel?.ModelId))
                    : ContextUsageCalculator.Calculate(
                        observedModel?.ConversationId,
                        activeModel?.Id ?? observedModel?.ModelId,
                        modelContextLimit: contextLimit);

                var snapshot = new TwinQuotaSnapshot(
                    DateTimeOffset.Now,
                    true,
                    SurfaceName(endpoint.Surface),
                    products,
                    quotaGroups,
                    models,
                    null)
                {
                    ActiveModelId = activeModel?.Id,
                    ContextUsage = contextUsage
                };
                await _cache.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
                return snapshot;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
            {
                failures.Add($"{SurfaceName(endpoint.Surface)}: {exception.Message}");
            }
        }

        var cached = await _cache.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached with
            {
                IsLive = false,
                Products = products,
                Message = "Showing the last successful snapshot. Start Antigravity and refresh for live data."
            };
        }

        var fallbackObservation = await _activeModelStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var fallbackModels = fallbackObservation is { ModelId.Length: > 0 }
            ? new[] { CreateObservedFallbackModel(fallbackObservation.ModelId) }
            : Array.Empty<ModelAvailability>();
        var fallbackContext = fallbackObservation is { ModelId.Length: > 0 }
            ? ContextUsageCalculator.Calculate(fallbackObservation.ConversationId, fallbackObservation.ModelId)
            : null;
        var message = failures.Count > 0
            ? "Antigravity was detected, but live data could not be read. " + string.Join(" | ", failures)
            : "Start Antigravity 2.0, Antigravity IDE, or Antigravity CLI, then refresh.";
        return new TwinQuotaSnapshot(DateTimeOffset.Now, false, null, products, [], fallbackModels, message)
        {
            ActiveModelId = fallbackObservation?.ModelId,
            ContextUsage = fallbackContext
        };
    }

    private static ModelAvailability CreateObservedFallbackModel(string modelId)
    {
        var provider = modelId.Contains("claude", StringComparison.OrdinalIgnoreCase)
            ? "Anthropic"
            : modelId.Contains("gpt", StringComparison.OrdinalIgnoreCase)
                ? "OpenAI"
                : modelId.Contains("gemini", StringComparison.OrdinalIgnoreCase)
                    ? "Google"
                    : "Other";
        var displayName = FormatObservedModelDisplayName(modelId);
        return new ModelAvailability(modelId, displayName, provider, null, null);
    }

    private static string FormatObservedModelDisplayName(string modelId)
    {
        var tokens = modelId.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return modelId;
        }

        var parts = tokens.Select(token =>
            token.Equals("gpt", StringComparison.OrdinalIgnoreCase)
                ? "GPT"
                : char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant()).ToArray();
        if (parts.Length >= 2 && parts[^1] is "Low" or "Medium" or "High" or "Thinking")
        {
            return $"{string.Join(' ', parts[..^1])} ({parts[^1]})";
        }

        return string.Join(' ', parts);
    }

    private static string SurfaceName(AntigravitySurface surface) => surface switch
    {
        AntigravitySurface.Desktop2 => "Antigravity 2.0",
        AntigravitySurface.Ide => "Antigravity IDE",
        AntigravitySurface.Cli => "Antigravity CLI",
        AntigravitySurface.VsCode => "Antigravity for VS Code",
        _ => "Antigravity"
    };

    private async Task<ContextWindowUsage?> TryGetCurrentContextUsageAsync(
        LanguageServerEndpoint endpoint,
        string conversationId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _rpcClient.GetCurrentContextUsageAsync(endpoint, conversationId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }
}
