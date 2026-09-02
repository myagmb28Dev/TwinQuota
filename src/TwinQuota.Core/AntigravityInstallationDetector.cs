using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace TwinQuota.Core;

public sealed class AntigravityInstallationDetector
{
    private readonly string _localAppData;
    private readonly string _roamingAppData;
    private readonly string _userProfile;

    public AntigravityInstallationDetector(
        string? localAppData = null,
        string? roamingAppData = null,
        string? userProfile = null)
    {
        _localAppData = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _roamingAppData = roamingAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _userProfile = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public IReadOnlyList<ProductStatus> Detect(IReadOnlyList<LanguageServerEndpoint> endpoints)
    {
        var desktopPath = Path.Combine(_localAppData, "Programs", "Antigravity", "Antigravity.exe");
        var ideDirectory = Path.Combine(_localAppData, "Programs", "Antigravity IDE");
        var idePath = new[]
        {
            Path.Combine(ideDirectory, "Antigravity IDE.exe"),
            Path.Combine(ideDirectory, "Antigravity.exe")
        }.FirstOrDefault(File.Exists);
        var cliPath = FindCliPath();
        var vsCodeInfo = FindVsCodeExtension();

        return
        [
            CreateStatus(
                AntigravitySurface.Desktop2,
                "Antigravity 2.0",
                File.Exists(desktopPath) ? desktopPath : null,
                Directory.Exists(Path.Combine(_roamingAppData, "Antigravity")),
                ReadRegisteredVersion("Antigravity"),
                endpoints),
            CreateStatus(
                AntigravitySurface.Ide,
                "Antigravity IDE",
                idePath,
                Directory.Exists(Path.Combine(_roamingAppData, "Antigravity IDE")),
                ReadRegisteredVersion("Antigravity IDE"),
                endpoints),
            CreateStatus(
                AntigravitySurface.Cli,
                "Antigravity CLI",
                cliPath,
                Directory.Exists(Path.Combine(_userProfile, ".gemini", "antigravity-cli")),
                ReadFileVersion(cliPath),
                endpoints),
            CreateStatus(
                AntigravitySurface.VsCode,
                "Antigravity for VS Code",
                vsCodeInfo.ExecutablePath,
                Directory.Exists(Path.Combine(_userProfile, ".gemini", "antigravity")),
                vsCodeInfo.Version,
                endpoints)
        ];
    }

    private static ProductStatus CreateStatus(
        AntigravitySurface surface,
        string displayName,
        string? executablePath,
        bool hasLocalData,
        string? version,
        IReadOnlyList<LanguageServerEndpoint> endpoints)
    {
        var installed = executablePath is not null;
        var running = endpoints.Any(endpoint => endpoint.Surface == surface);
        var detail = running
            ? "Running · live quota available"
            : installed
                ? "Installed · start it for live refresh"
                : hasLocalData
                    ? "Not installed · local data remains"
                    : "Not installed";
        return new ProductStatus(surface, displayName, installed, running, hasLocalData, version, executablePath, detail);
    }

    private string? FindCliPath()
    {
        var knownPath = Path.Combine(_localAppData, "agy", "bin", "agy.exe");
        if (File.Exists(knownPath))
        {
            return knownPath;
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, "agy.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    private static string? ReadRegisteredVersion(string productPrefix)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var uninstall = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null)
            {
                return null;
            }

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                using var subKey = uninstall.OpenSubKey(subKeyName);
                var displayName = subKey?.GetValue("DisplayName") as string;
                var matchesProduct = displayName is not null
                    && (productPrefix.Equals("Antigravity", StringComparison.OrdinalIgnoreCase)
                        ? displayName.StartsWith("Antigravity ", StringComparison.OrdinalIgnoreCase)
                          && !displayName.StartsWith("Antigravity IDE", StringComparison.OrdinalIgnoreCase)
                        : displayName.StartsWith(productPrefix, StringComparison.OrdinalIgnoreCase));
                if (matchesProduct)
                {
                    return subKey?.GetValue("DisplayVersion") as string;
                }
            }
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }

        return null;
    }

    private static string? ReadFileVersion(string? path)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            return FileVersionInfo.GetVersionInfo(path).FileVersion;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private (string? ExtensionPath, string? Version, string? ExecutablePath) FindVsCodeExtension()
    {
        var extensionRoots = new[]
        {
            Path.Combine(_userProfile, ".vscode", "extensions"),
            Path.Combine(_userProfile, ".vscode-insiders", "extensions"),
            Path.Combine(_userProfile, ".cursor", "extensions"),
            Path.Combine(_userProfile, ".windsurf", "extensions")
        };

        string? foundExtensionPath = null;
        string? foundVersion = null;

        foreach (var root in extensionRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                var match = Directory.EnumerateDirectories(root, "google.google-antigravity*")
                    .Concat(Directory.EnumerateDirectories(root, "google.antigravity*"))
                    .OrderByDescending(dir => Directory.GetLastWriteTimeUtc(dir))
                    .FirstOrDefault();

                if (match is not null)
                {
                    foundExtensionPath = match;
                    foundVersion = ReadExtensionPackageVersion(Path.Combine(match, "package.json"));
                    break;
                }
            }
            catch
            {
                // Ignore directory enumeration issues.
            }
        }

        var geminiAgy = Path.Combine(_userProfile, ".gemini", "bin", "agy.exe");
        var executablePath = File.Exists(geminiAgy) ? geminiAgy : foundExtensionPath;

        return (foundExtensionPath, foundVersion, executablePath);
    }

    private static string? ReadExtensionPackageVersion(string packageJsonPath)
    {
        if (!File.Exists(packageJsonPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(packageJsonPath);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.TryGetProperty("version", out var versionElement))
            {
                return versionElement.GetString();
            }
        }
        catch
        {
            // Ignore package JSON reading issues.
        }

        return null;
    }
}
