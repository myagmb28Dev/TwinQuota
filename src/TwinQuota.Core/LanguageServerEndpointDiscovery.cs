using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TwinQuota.Core;

public sealed partial class LanguageServerEndpointDiscovery
{
    private readonly string _roamingAppData;
    private readonly string _userProfile;
    private readonly Func<CancellationToken, Task<string>> _processReader;
    private readonly HttpClient _httpClient;

    public LanguageServerEndpointDiscovery(
        string? roamingAppData = null,
        string? userProfile = null,
        Func<CancellationToken, Task<string>>? processReader = null,
        HttpClient? httpClient = null)
    {
        _roamingAppData = roamingAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _userProfile = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _processReader = processReader ?? ReadLanguageServerProcessesAsync;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    }

    public async Task<IReadOnlyList<LanguageServerEndpoint>> DiscoverAsync(CancellationToken cancellationToken)
    {
        string processJson;
        try
        {
            processJson = await _processReader(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return [];
        }

        var processes = ParseProcessList(processJson);
        var daemonEndpoints = ReadDaemonEndpointsByPid();
        var endpoints = new List<LanguageServerEndpoint>();
        foreach (var process in processes)
        {
            if (daemonEndpoints.TryGetValue(process.ProcessId, out var daemonEndpoint))
            {
                var daemonSurface = InferSurface(process.CommandLine, HubFlagRegex().IsMatch(process.CommandLine));
                endpoints.Add(new LanguageServerEndpoint(
                    daemonSurface,
                    process.ProcessId,
                    daemonEndpoint.HttpPort,
                    daemonEndpoint.CsrfToken));
                continue;
            }

            if (!TryParseCommandLine(process.CommandLine, out var surface, out var csrfToken, out var explicitPort))
            {
                continue;
            }

            var port = explicitPort;
            if (port <= 0)
            {
                var logPath = FindLogPath(surface);
                if (logPath is null || !TryReadLatestHttpPort(logPath, out port))
                {
                    continue;
                }
            }

            if (string.IsNullOrEmpty(csrfToken))
            {
                csrfToken = await TryFetchHubCsrfTokenAsync(port, cancellationToken).ConfigureAwait(false) ?? string.Empty;
            }

            if (string.IsNullOrEmpty(csrfToken))
            {
                continue;
            }

            endpoints.Add(new LanguageServerEndpoint(surface, process.ProcessId, port, csrfToken));
        }

        return endpoints;
    }

    public static IReadOnlyList<(int ProcessId, string CommandLine)> ParseProcessList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var start = json.IndexOfAny(['[', '{']);
        if (start < 0)
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json[start..]);
            var results = new List<(int, string)>();
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    AddProcess(element, results);
                }
            }
            else
            {
                AddProcess(document.RootElement, results);
            }

            return results;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static bool TryParseCommandLine(
        string commandLine,
        out AntigravitySurface surface,
        out string csrfToken) =>
        TryParseCommandLine(commandLine, out surface, out csrfToken, out _);

    public static bool TryParseCommandLine(
        string commandLine,
        out AntigravitySurface surface,
        out string csrfToken,
        out int explicitPort)
    {
        surface = AntigravitySurface.Desktop2;
        csrfToken = string.Empty;
        explicitPort = 0;

        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return false;
        }

        var csrf = CsrfRegex().Match(commandLine);
        if (csrf.Success)
        {
            csrfToken = csrf.Groups[1].Success ? csrf.Groups[1].Value : csrf.Groups[2].Value;
        }

        var hubPortMatch = HubPortRegex().Match(commandLine);
        if (hubPortMatch.Success)
        {
            var portString = hubPortMatch.Groups[1].Success ? hubPortMatch.Groups[1].Value : hubPortMatch.Groups[2].Value;
            if (int.TryParse(portString, out var parsedPort) && parsedPort is > 0 and <= 65535)
            {
                explicitPort = parsedPort;
            }
        }

        var isHub = HubFlagRegex().IsMatch(commandLine);
        if (!csrf.Success && !isHub)
        {
            return false;
        }

        surface = InferSurface(commandLine, isHub);
        return true;
    }

    private static AntigravitySurface InferSurface(string commandLine, bool isHub)
    {
        var appData = AppDataRegex().Match(commandLine);
        var appDataValue = appData.Success
            ? (appData.Groups[1].Success ? appData.Groups[1].Value : appData.Groups[2].Value)
            : string.Empty;
        var subclient = SubclientRegex().Match(commandLine);
        var subclientValue = subclient.Success
            ? (subclient.Groups[1].Success ? subclient.Groups[1].Value : subclient.Groups[2].Value)
            : string.Empty;
        var identity = $"{appDataValue} {subclientValue}".ToLowerInvariant();

        if (isHub || identity.Contains("vscode", StringComparison.Ordinal) || identity.Contains("vs-code", StringComparison.Ordinal))
        {
            return AntigravitySurface.VsCode;
        }

        if (identity.Contains("ide", StringComparison.Ordinal))
        {
            return AntigravitySurface.Ide;
        }

        if (identity.Contains("cli", StringComparison.Ordinal) || identity.Contains("agy", StringComparison.Ordinal))
        {
            return AntigravitySurface.Cli;
        }

        return AntigravitySurface.Desktop2;
    }

    private Dictionary<int, (int HttpPort, string CsrfToken)> ReadDaemonEndpointsByPid()
    {
        var result = new Dictionary<int, (int HttpPort, string CsrfToken)>();
        var daemonDir = Path.Combine(_userProfile, ".gemini", "antigravity", "daemon");
        if (!Directory.Exists(daemonDir))
        {
            return result;
        }

        IEnumerable<FileInfo> files;
        try
        {
            files = new DirectoryInfo(daemonDir)
                .EnumerateFiles("hub_*.json")
                .OrderByDescending(file => file.LastWriteTimeUtc);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return result;
        }

        foreach (var file in files)
        {
            try
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var document = JsonDocument.Parse(stream);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("pid", out var pidElement)
                    || !pidElement.TryGetInt32(out var pid)
                    || pid <= 0
                    || !root.TryGetProperty("httpPort", out var portElement)
                    || !portElement.TryGetInt32(out var httpPort)
                    || httpPort is <= 0 or > 65535
                    || !root.TryGetProperty("csrfToken", out var tokenElement)
                    || tokenElement.GetString() is not { Length: > 0 } csrfToken)
                {
                    continue;
                }

                result.TryAdd(pid, (httpPort, csrfToken));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }

        return result;
    }

    public static bool TryExtractCsrfTokenFromHtml(string html, out string csrfToken)
    {
        csrfToken = string.Empty;
        if (string.IsNullOrWhiteSpace(html))
        {
            return false;
        }

        var match = HtmlCsrfTokenRegex().Match(html);
        if (match.Success)
        {
            csrfToken = match.Groups[1].Value;
            return true;
        }

        return false;
    }

    public static bool TryReadLatestHttpPort(string logPath, out int port)
    {
        port = 0;
        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            var matches = HttpPortRegex().Matches(content);
            return matches.Count > 0
                && int.TryParse(matches[^1].Groups[1].Value, out port)
                && port is > 0 and <= 65535;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void AddProcess(JsonElement element, List<(int, string)> results)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("ProcessId", out var processIdElement)
            || !processIdElement.TryGetInt32(out var processId)
            || !element.TryGetProperty("CommandLine", out var commandLineElement)
            || commandLineElement.GetString() is not { Length: > 0 } commandLine)
        {
            return;
        }

        results.Add((processId, commandLine));
    }

    private async Task<string?> TryFetchHubCsrfTokenAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/");
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return TryExtractCsrfTokenFromHtml(html, out var token) ? token : null;
        }
        catch
        {
            return null;
        }
    }

    private string? FindLogPath(AntigravitySurface surface)
    {
        if (surface == AntigravitySurface.Desktop2)
        {
            return ExistingFile(Path.Combine(_roamingAppData, "Antigravity", "logs", "language_server.log"));
        }

        if (surface == AntigravitySurface.Ide)
        {
            var logsRoot = Path.Combine(_roamingAppData, "Antigravity IDE", "logs");
            return FindNewestFile(logsRoot, "ls-main.log");
        }

        if (surface == AntigravitySurface.VsCode)
        {
            var logsRoot = Path.Combine(_userProfile, ".gemini", "antigravity", "log");
            return FindNewestFile(logsRoot, "*.log")
                ?? ExistingFile(Path.Combine(_roamingAppData, "Antigravity", "logs", "language_server.log"));
        }

        var candidates = new[]
        {
            Path.Combine(_roamingAppData, "Antigravity CLI", "logs", "language_server.log"),
            Path.Combine(_userProfile, ".gemini", "antigravity-cli", "logs", "language_server.log"),
            FindNewestFile(Path.Combine(_userProfile, ".gemini", "antigravity", "log"), "*.log")
        };
        return candidates.Select(ExistingFile).FirstOrDefault(path => path is not null);
    }

    private static string? ExistingFile(string? path) => path is not null && File.Exists(path) ? path : null;

    private static string? FindNewestFile(string root, string pattern)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        try
        {
            return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault();
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<string> ReadLanguageServerProcessesAsync(CancellationToken cancellationToken)
    {
        var powerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        var script = "$ErrorActionPreference='Stop'; @(Get-CimInstance Win32_Process | "
            + "Where-Object { $_.Name -in @('language_server.exe','language_server_windows_x64.exe','agy.exe','agy') } | "
            + "Select-Object ProcessId,CommandLine) | ConvertTo-Json -Compress";
        var startInfo = new ProcessStartInfo
        {
            FileName = powerShell,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        return process.ExitCode == 0 ? output : string.Empty;
    }

    [GeneratedRegex("--csrf_token\\s+(?:\"([^\"]+)\"|(\\S+))", RegexOptions.IgnoreCase)]
    private static partial Regex CsrfRegex();

    [GeneratedRegex("--hub-port(?:=|\\s+)(?:\"([^\"]+)\"|(\\d+))", RegexOptions.IgnoreCase)]
    private static partial Regex HubPortRegex();

    [GeneratedRegex("(?:^|\\s)--hub(?:\\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex HubFlagRegex();

    [GeneratedRegex("--app_data_dir\\s+(?:\"([^\"]+)\"|(\\S+))", RegexOptions.IgnoreCase)]
    private static partial Regex AppDataRegex();

    [GeneratedRegex("--subclient_type\\s+(?:\"([^\"]+)\"|(\\S+))", RegexOptions.IgnoreCase)]
    private static partial Regex SubclientRegex();

    [GeneratedRegex("random port at (\\d+) for HTTP", RegexOptions.IgnoreCase)]
    private static partial Regex HttpPortRegex();

    [GeneratedRegex("\"csrfToken\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlCsrfTokenRegex();
}
