namespace TwinQuota.Core;

public sealed class TwinQuotaMonitor
{
    private readonly LanguageServerEndpointDiscovery _endpointDiscovery;
    private readonly AntigravityInstallationDetector _installationDetector;
    private readonly AntigravityRpcClient _rpcClient;
    private readonly AntigravityCliClient _cliClient;
    private readonly SnapshotCache _cache;

    public TwinQuotaMonitor(
        LanguageServerEndpointDiscovery? endpointDiscovery = null,
        AntigravityInstallationDetector? installationDetector = null,
        AntigravityRpcClient? rpcClient = null,
        AntigravityCliClient? cliClient = null,
        SnapshotCache? cache = null)
    {
        _endpointDiscovery = endpointDiscovery ?? new LanguageServerEndpointDiscovery();
        _installationDetector = installationDetector ?? new AntigravityInstallationDetector();
        _rpcClient = rpcClient ?? new AntigravityRpcClient();
        _cliClient = cliClient ?? new AntigravityCliClient();
        _cache = cache ?? new SnapshotCache();
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
                var activeModel = AntigravityResponseParser.ParseActiveModel(await modelsTask.ConfigureAwait(false));
                IReadOnlyList<ModelAvailability> models = activeModel is null ? [] : [activeModel];
                var snapshot = new TwinQuotaSnapshot(
                    DateTimeOffset.Now,
                    true,
                    SurfaceName(endpoint.Surface),
                    products,
                    quotaGroups,
                    models,
                    "Live data from Antigravity localhost RPC");
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
                        [],
                        "Antigravity CLI is available, but it did not report an active model. Start a session for active-model quota details.");
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
            var cachedModels = cached.Models.Count == 1 ? cached.Models : [];
            return cached with
            {
                IsLive = false,
                Products = products,
                Models = cachedModels,
                QuotaGroups = cachedModels.Count == 1 ? cached.QuotaGroups : [],
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
        _ => "Antigravity"
    };
}
