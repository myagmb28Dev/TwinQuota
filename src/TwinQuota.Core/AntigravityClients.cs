using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;

namespace TwinQuota.Core;

public sealed class AntigravityRpcClient
{
    private readonly HttpClient _httpClient;

    public AntigravityRpcClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    public Task<string> GetQuotaSummaryAsync(LanguageServerEndpoint endpoint, CancellationToken cancellationToken) =>
        PostAsync(endpoint, "RetrieveUserQuotaSummary", cancellationToken);

    public Task<string> GetAvailableModelsAsync(LanguageServerEndpoint endpoint, CancellationToken cancellationToken) =>
        PostAsync(endpoint, "GetAvailableModels", cancellationToken);

    private async Task<string> PostAsync(
        LanguageServerEndpoint endpoint,
        string method,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            $"http://127.0.0.1:{endpoint.HttpPort}/exa.language_server_pb.LanguageServerService/{method}");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
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
