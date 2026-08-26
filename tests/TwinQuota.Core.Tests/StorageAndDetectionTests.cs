using TwinQuota.Core;

namespace TwinQuota.Core.Tests;

public sealed class StorageAndDetectionTests
{
    [Fact]
    public void GroupsPriorityVariantsIntoOneModelFamily()
    {
        ModelAvailability[] models =
        [
            new("flash-low", "Gemini 3.7 Flash (Low)", "Google", null, null),
            new("flash-high", "Gemini 3.7 Flash (High)", "Google", null, null),
            new("flash-medium", "Gemini 3.7 Flash (Medium)", "Google", null, null),
            new("claude", "Claude Sonnet (Thinking)", "Anthropic", null, null)
        ];

        var families = ModelFamilyGrouper.Group(models);

        var gemini = Assert.Single(families, family => family.DisplayName == "Gemini 3.7 Flash");
        Assert.Equal(["Low", "Medium", "High"], gemini.Priorities);
        Assert.Equal(3, gemini.Models.Count);
        var claude = Assert.Single(families, family => family.DisplayName == "Claude Sonnet (Thinking)");
        Assert.Empty(claude.Priorities);
    }

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
                "Live")
            {
                ActiveModelId = "gemini"
            };

            await cache.SaveAsync(snapshot, CancellationToken.None);
            var loaded = await cache.LoadAsync(CancellationToken.None);
            var content = await File.ReadAllTextAsync(path);

            Assert.NotNull(loaded);
            Assert.Single(loaded.Models);
            Assert.Equal("gemini", loaded.ActiveModelId);
            Assert.DoesNotContain("csrf", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
