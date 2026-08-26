using System.Text.Json.Nodes;
using TwinQuota.Core;

namespace TwinQuota.Core.Tests;

public sealed class AntigravityHookRegistrationTests
{
    [Fact]
    public void AddsAndRemovesOnlyTheTwinQuotaHook()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"TwinQuota.Tests.{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "hooks.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, """
                {
                  "existing-hook": {
                    "enabled": true,
                    "PreInvocation": [{ "type": "command", "command": "existing-command" }]
                  }
                }
                """);
            var registration = new AntigravityHookRegistration(path);

            registration.EnsureRegistered(@"C:\TwinQuota\TwinQuota.Hook.exe");

            var registered = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Assert.NotNull(registered["existing-hook"]);
            Assert.Equal(
                @"C:\TwinQuota\TwinQuota.Hook.exe",
                registered[AntigravityHookRegistration.HookName]!["PreInvocation"]![0]!["command"]!.GetValue<string>());

            registration.Remove();

            var removed = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Assert.NotNull(removed["existing-hook"]);
            Assert.Null(removed[AntigravityHookRegistration.HookName]);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void LeavesInvalidUserConfigurationUntouched()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"TwinQuota.Tests.{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "hooks.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "not-json");
            var registration = new AntigravityHookRegistration(path);

            registration.EnsureRegistered(@"C:\TwinQuota\TwinQuota.exe");

            Assert.Equal("not-json", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
