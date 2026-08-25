using TwinQuota.Core;

namespace TwinQuota.Core.Tests;

public sealed class StorageAndDetectionTests
{
    [Fact]
    public void DetectsInstalledRunningAndDataOnlySurfaces()
    {
        var root = Path.Combine(Path.GetTempPath(), "TwinQuotaTests", Guid.NewGuid().ToString("N"));
        var local = Path.Combine(root, "Local");
        var roaming = Path.Combine(root, "Roaming");
        var profile = Path.Combine(root, "Profile");
        try
        {
            var desktopPath = Path.Combine(local, "Programs", "Antigravity", "Antigravity.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(desktopPath)!);
            File.WriteAllBytes(desktopPath, []);
            Directory.CreateDirectory(Path.Combine(roaming, "Antigravity IDE"));
            var cliPath = Path.Combine(local, "agy", "bin", "agy.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(cliPath)!);
            File.WriteAllBytes(cliPath, []);

            var detector = new AntigravityInstallationDetector(local, roaming, profile);
            var products = detector.Detect(
                [new LanguageServerEndpoint(AntigravitySurface.Desktop2, 1, 50000, "secret")]);

            Assert.True(products[0].Installed);
            Assert.True(products[0].Running);
            Assert.False(products[1].Installed);
            Assert.True(products[1].HasLocalData);
            Assert.True(products[2].Installed);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SnapshotCacheRoundTripsWithoutEndpointSecrets()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TwinQuotaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "snapshot.json");
        try
        {
            var cache = new SnapshotCache(path);
            var snapshot = new TwinQuotaSnapshot(
                DateTimeOffset.Parse("2026-08-25T10:00:00Z"),
                true,
                "Antigravity 2.0",
                [],
                [new QuotaGroup("Gemini Models", null, [])],
                [new ModelAvailability("gemini", "Gemini", "Google", 0.5, null)],
                "Live");

            await cache.SaveAsync(snapshot, CancellationToken.None);
            var loaded = await cache.LoadAsync(CancellationToken.None);
            var content = await File.ReadAllTextAsync(path);

            Assert.NotNull(loaded);
            Assert.Single(loaded.Models);
            Assert.DoesNotContain("csrf", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
