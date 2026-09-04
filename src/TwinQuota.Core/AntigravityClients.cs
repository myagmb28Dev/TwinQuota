using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TwinQuota.Core;

public sealed class AntigravityRpcClient
{
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, int> _generatorMetadataCounts = new(StringComparer.Ordinal);

    public AntigravityRpcClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    public Task<string> GetQuotaSummaryAsync(LanguageServerEndpoint endpoint, CancellationToken cancellationToken) =>
        PostAsync(endpoint, "RetrieveUserQuotaSummary", cancellationToken);

    public Task<string> GetAvailableModelsAsync(LanguageServerEndpoint endpoint, CancellationToken cancellationToken) =>
        PostAsync(endpoint, "GetAvailableModels", cancellationToken);

    public async Task<ContextWindowUsage?> GetCurrentContextUsageAsync(
        LanguageServerEndpoint endpoint,
        string conversationId,
        CancellationToken cancellationToken)
    {
        var summariesJson = await PostAsync(
            endpoint,
            "GetAllCascadeTrajectories",
            "{}",
            cancellationToken).ConfigureAwait(false);
        var summary = AntigravityResponseParser.ParseTrajectorySummary(summariesJson, conversationId);
        if (summary is null)
        {
            return null;
        }

        var generatorOffset = _generatorMetadataCounts.TryGetValue(conversationId, out var knownCount)
            ? Math.Max(0, knownCount - 64)
            : Math.Max(0, summary.StepCount / 2 - 128);
        GeneratorMetadataPage? generatorPage = null;
        try
        {
            generatorPage = await ReadGeneratorMetadataPageAsync(
                endpoint,
                conversationId,
                generatorOffset,
                cancellationToken).ConfigureAwait(false);
            if (generatorPage.ItemCount == 0 && generatorOffset >= 256)
            {
                generatorOffset -= 256;
                generatorPage = await ReadGeneratorMetadataPageAsync(
                    endpoint,
                    conversationId,
                    generatorOffset,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Older or busy servers can time out this optional metadata endpoint.
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            // Fall back to the smaller, broadly supported trajectory steps endpoint.
        }

        if (generatorPage?.ItemCount > 0)
        {
            _generatorMetadataCounts[conversationId] = generatorOffset + generatorPage.ItemCount;
        }

        if (generatorPage?.LatestContextWindowUsage is not null)
        {
            return generatorPage.LatestContextWindowUsage;
        }

        if (generatorOffset > 0 && generatorPage?.ItemCount == 0)
        {
            _generatorMetadataCounts.TryRemove(conversationId, out _);
        }

        const int stepPageSize = 200;
        var stepOffset = Math.Max(0, summary.StepCount - stepPageSize);
        var stepsRequestJson = JsonSerializer.Serialize(new
        {
            cascadeId = conversationId,
            stepOffset,
            verbosity = "CLIENT_TRAJECTORY_VERBOSITY_PROD_UI",
            trajectoryVerbosity = "CLIENT_TRAJECTORY_VERBOSITY_PROD_UI",
            disableRehydration = true
        });
        var stepsJson = await PostAsync(
            endpoint,
            "GetCascadeTrajectorySteps",
            stepsRequestJson,
            cancellationToken).ConfigureAwait(false);
        return AntigravityResponseParser.ParseLatestContextTokens(stepsJson) is { } usedTokens
            ? new ContextWindowUsage(usedTokens, null)
            : null;
    }

    private async Task<GeneratorMetadataPage> ReadGeneratorMetadataPageAsync(
        LanguageServerEndpoint endpoint,
        string conversationId,
        int generatorOffset,
        CancellationToken cancellationToken)
    {
        var requestJson = JsonSerializer.Serialize(new
        {
            cascadeId = conversationId,
            generatorMetadataOffset = generatorOffset,
            includeMessages = false
        });
        var json = await PostAsync(
            endpoint,
            "GetCascadeTrajectoryGeneratorMetadata",
            requestJson,
            cancellationToken).ConfigureAwait(false);
        return AntigravityResponseParser.ParseGeneratorMetadataPage(json);
    }

    private async Task<string> PostAsync(
        LanguageServerEndpoint endpoint,
        string method,
        CancellationToken cancellationToken) =>
        await PostAsync(endpoint, method, "{}", cancellationToken).ConfigureAwait(false);

    private async Task<string> PostAsync(
        LanguageServerEndpoint endpoint,
        string method,
        string requestJson,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            $"http://127.0.0.1:{endpoint.HttpPort}/exa.language_server_pb.LanguageServerService/{method}");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("x-codeium-csrf-token", endpoint.CsrfToken);
        request.Headers.TryAddWithoutValidation("connect-protocol-version", "1");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class AntigravityCliClient
{
    public async Task<IReadOnlyList<ModelAvailability>> ReadModelsAsync(
        string executablePath,
        CancellationToken cancellationToken)
    {
        var output = await RunAsync(executablePath, ["models", "--output-format", "json"], cancellationToken)
            .ConfigureAwait(false);
        var models = AntigravityResponseParser.ParseCliModels(output.StandardOutput);
        if (output.ExitCode == 0 && models.Count > 0)
        {
            return models;
        }

        output = await RunAsync(executablePath, ["models"], cancellationToken).ConfigureAwait(false);
        return output.ExitCode == 0
            ? AntigravityResponseParser.ParseCliModels(output.StandardOutput)
            : [];
    }

    private static async Task<CommandResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start agy.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        return new CommandResult(
            process.ExitCode,
            await standardOutput.ConfigureAwait(false),
            await standardError.ConfigureAwait(false));
    }

    private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
}
