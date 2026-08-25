using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TwinQuota.Core;

public sealed partial class LanguageServerEndpointDiscovery
{
    private readonly string _roamingAppData;
    private readonly string _userProfile;
    private readonly Func<CancellationToken, Task<string>> _processReader;

    public LanguageServerEndpointDiscovery(
        string? roamingAppData = null,
        string? userProfile = null,
        Func<CancellationToken, Task<string>>? processReader = null)
    {
        _roamingAppData = roamingAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _userProfile = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _processReader = processReader ?? ReadLanguageServerProcessesAsync;
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
        var endpoints = new List<LanguageServerEndpoint>();
        foreach (var process in processes)
        {
            if (!TryParseCommandLine(process.CommandLine, out var surface, out var csrfToken))
            {
                continue;
            }

            var logPath = FindLogPath(surface);
            if (logPath is null || !TryReadLatestHttpPort(logPath, out var port))
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
        out string csrfToken)
    {
        surface = AntigravitySurface.Desktop2;
        csrfToken = string.Empty;
        var csrf = CsrfRegex().Match(commandLine);
        if (!csrf.Success)
        {
            return false;
        }

        csrfToken = csrf.Groups[1].Success ? csrf.Groups[1].Value : csrf.Groups[2].Value;
        var appData = AppDataRegex().Match(commandLine);
        var appDataValue = appData.Success
            ? (appData.Groups[1].Success ? appData.Groups[1].Value : appData.Groups[2].Value)
            : string.Empty;
        var subclient = SubclientRegex().Match(commandLine);
        var subclientValue = subclient.Success
            ? (subclient.Groups[1].Success ? subclient.Groups[1].Value : subclient.Groups[2].Value)
            : string.Empty;
        var identity = $"{appDataValue} {subclientValue}".ToLowerInvariant();

        surface = identity.Contains("ide", StringComparison.Ordinal)
            ? AntigravitySurface.Ide
            : identity.Contains("cli", StringComparison.Ordinal) || identity.Contains("agy", StringComparison.Ordinal)
                ? AntigravitySurface.Cli
                : AntigravitySurface.Desktop2;
        return csrfToken.Length > 0;
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

        var candidates = new[]
        {
            Path.Combine(_roamingAppData, "Antigravity CLI", "logs", "language_server.log"),
            Path.Combine(_userProfile, ".gemini", "antigravity-cli", "logs", "language_server.log")
        };
        return candidates.Select(ExistingFile).FirstOrDefault(path => path is not null);
    }

    private static string? ExistingFile(string path) => File.Exists(path) ? path : null;

    private static string? FindNewestFile(string root, string fileName)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        try
        {
            return Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories)
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
            + "Where-Object { $_.Name -in @('language_server.exe','language_server_windows_x64.exe') } | "
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

    [GeneratedRegex("--app_data_dir\\s+(?:\"([^\"]+)\"|(\\S+))", RegexOptions.IgnoreCase)]
    private static partial Regex AppDataRegex();

    [GeneratedRegex("--subclient_type\\s+(?:\"([^\"]+)\"|(\\S+))", RegexOptions.IgnoreCase)]
    private static partial Regex SubclientRegex();

    [GeneratedRegex("random port at (\\d+) for HTTP", RegexOptions.IgnoreCase)]
    private static partial Regex HttpPortRegex();
}
