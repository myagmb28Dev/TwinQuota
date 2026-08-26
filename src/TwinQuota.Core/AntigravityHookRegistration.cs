using System.Text.Json;
using System.Text.Json.Nodes;

namespace TwinQuota.Core;

public sealed class AntigravityHookRegistration
{
    public const string HookName = "twinquota-active-model";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _hooksPath;

    public AntigravityHookRegistration(string? hooksPath = null)
    {
        _hooksPath = hooksPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".gemini",
            "config",
            "hooks.json");
    }

    public void EnsureRegistered(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        var root = ReadRoot();
        if (root is null)
        {
            return;
        }

        var hook = new JsonObject
        {
            ["PreInvocation"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = executablePath,
                    ["timeout"] = 5
                }
            }
        };

        if (JsonNode.DeepEquals(root[HookName], hook))
        {
            return;
        }

        root[HookName] = hook;
        WriteRoot(root);
    }

    public void Remove()
    {
        if (!File.Exists(_hooksPath))
        {
            return;
        }

        var root = ReadRoot();
        if (root is null || !root.Remove(HookName))
        {
            return;
        }

        WriteRoot(root);
    }

    private JsonObject? ReadRoot()
    {
        if (!File.Exists(_hooksPath))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(_hooksPath)) as JsonObject;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private void WriteRoot(JsonObject root)
    {
        var directory = Path.GetDirectoryName(_hooksPath);
        if (directory is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);
            var temporaryPath = $"{_hooksPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temporaryPath, root.ToJsonString(Options));
                File.Move(temporaryPath, _hooksPath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Keep TwinQuota usable when Antigravity customization storage is unavailable.
        }
    }
}
