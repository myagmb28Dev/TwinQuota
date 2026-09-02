namespace TwinQuota.Core;

public sealed class TwinQuotaMonitor
{
    private readonly LanguageServerEndpointDiscovery _endpointDiscovery;
    private readonly AntigravityInstallationDetector _installationDetector;
    private readonly AntigravityRpcClient _rpcClient;
    private readonly AntigravityCliClient _cliClient;
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
        _cliClient = cliClient ?? new AntigravityCliClient();
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
                await Task.WhenAll(quotaTask, modelsTask).ConfigureAwait(false);
                var quotaGroups = AntigravityResponseParser.ParseQuotaSummary(await quotaTask.ConfigureAwait(false));
                var modelsJson = await modelsTask.ConfigureAwait(false);
                var observedModel = await _activeModelStore.LoadAsync(cancellationToken).ConfigureAwait(false);
                var modelResolution = ActiveModelResolver.Resolve(modelsJson, observedModel);
                var models = modelResolution.Models;
                var activeModel = modelResolution.ActiveModel;

                var contextUsage = ContextUsageCalculator.Calculate(
                    observedModel?.ConversationId,
                    activeModel?.Id ?? observedModel?.ModelId);

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

        var cli = products.First(product => product.Surface == AntigravitySurface.Cli);
        if (cli.Installed && cli.ExecutablePath is not null)
        {
            try
            {
                var cliModels = await _cliClient.ReadModelsAsync(cli.ExecutablePath, cancellationToken).ConfigureAwait(false);
                if (cliModels.Count > 0)
                {
                    return new TwinQuotaSnapshot(
                        DateTimeOffset.Now,
                        true,
                        "Antigravity CLI",
                        products,
                        [],
                        cliModels,
                        null);
                }
            }
            catch (Exception exception) when (exception is IOException or TaskCanceledException or InvalidOperationException)
            {
                failures.Add($"Antigravity CLI: {exception.Message}");
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

        var message = failures.Count > 0
            ? "Antigravity was detected, but live data could not be read. " + string.Join(" | ", failures)
            : "Start Antigravity 2.0, Antigravity IDE, or Antigravity CLI, then refresh.";
        return new TwinQuotaSnapshot(DateTimeOffset.Now, false, null, products, [], [], message);
    }

    private static string SurfaceName(AntigravitySurface surface) => surface switch
    {
        AntigravitySurface.Desktop2 => "Antigravity 2.0",
        AntigravitySurface.Ide => "Antigravity IDE",
        AntigravitySurface.Cli => "Antigravity CLI",
        AntigravitySurface.VsCode => "Antigravity for VS Code",
        _ => "Antigravity"
    };
}
