using TwinQuota.Core;

namespace TwinQuota.Core.Tests;

public sealed class LanguageServerEndpointDiscoveryTests
{
    [Theory]
    [InlineData("--csrf_token secret --app_data_dir antigravity --subclient_type desktop", AntigravitySurface.Desktop2)]
    [InlineData("--csrf_token secret --app_data_dir antigravity-ide --subclient_type ide", AntigravitySurface.Ide)]
    [InlineData("--csrf_token secret --app_data_dir antigravity-cli --subclient_type cli", AntigravitySurface.Cli)]
    public void MapsLanguageServerCommandLineToSurface(string commandLine, AntigravitySurface expected)
    {
        var parsed = LanguageServerEndpointDiscovery.TryParseCommandLine(
            commandLine,
            out var surface,
            out var csrfToken);

        Assert.True(parsed);
        Assert.Equal(expected, surface);
        Assert.Equal("secret", csrfToken);
    }

    [Fact]
    public void ParsesPowerShellSingletonAndArrayJson()
    {
        const string singleton = "{\"ProcessId\":42,\"CommandLine\":\"agy --csrf_token x\"}";
        const string array = "[{\"ProcessId\":42,\"CommandLine\":\"one\"},{\"ProcessId\":43,\"CommandLine\":\"two\"}]";

        Assert.Single(LanguageServerEndpointDiscovery.ParseProcessList(singleton));
        Assert.Equal(2, LanguageServerEndpointDiscovery.ParseProcessList(array).Count);
    }

    [Fact]
    public void ReadsTheLatestHttpPortFromAnAppendOnlyLog()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TwinQuotaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "language_server.log");
        try
        {
            File.WriteAllText(path, "random port at 40100 for HTTP\nrandom port at 40200 for HTTP\n");

            var parsed = LanguageServerEndpointDiscovery.TryReadLatestHttpPort(path, out var port);

            Assert.True(parsed);
            Assert.Equal(40200, port);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
