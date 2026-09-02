using TwinQuota.Core;

namespace TwinQuota.Core.Tests;

public sealed class LanguageServerEndpointDiscoveryTests
{
    [Theory]
    [InlineData("--csrf_token secret --app_data_dir antigravity --subclient_type desktop", AntigravitySurface.Desktop2)]
    [InlineData("--csrf_token secret --app_data_dir antigravity-ide --subclient_type ide", AntigravitySurface.Ide)]
    [InlineData("--csrf_token secret --app_data_dir antigravity-cli --subclient_type cli", AntigravitySurface.Cli)]
    [InlineData("--csrf_token secret --app_data_dir antigravity --subclient_type vs-code", AntigravitySurface.VsCode)]
    [InlineData("--hub --hub-port=65383 --app_data_dir=antigravity", AntigravitySurface.VsCode)]
    public void MapsLanguageServerCommandLineToSurface(string commandLine, AntigravitySurface expected)
    {
        var parsed = LanguageServerEndpointDiscovery.TryParseCommandLine(
            commandLine,
            out var surface,
            out var csrfToken);

        Assert.True(parsed);
        Assert.Equal(expected, surface);
    }

    [Fact]
    public void ParsesHubCommandLineWithExplicitPort()
    {
        const string commandLine = @"C:\Users\test\.gemini\bin\agy.exe --hub --hub-port=65383 --app_data_dir=antigravity --add-dir=d:\Codes\TwinQuota";
        var parsed = LanguageServerEndpointDiscovery.TryParseCommandLine(
            commandLine,
            out var surface,
            out var csrfToken,
            out var explicitPort);

        Assert.True(parsed);
        Assert.Equal(AntigravitySurface.VsCode, surface);
        Assert.Equal(65383, explicitPort);
        Assert.Empty(csrfToken);
    }

    [Fact]
    public void ExtractsCsrfTokenFromHubHtml()
    {
        const string html = "<!doctype html><html><head><script>window.__APP_CONFIG__ = {\"productName\":\"antigravity\",\"csrfToken\":\"81fc55a1-fe04-4d7f-be07-446a69da2d89\",\"appVersion\":\"\",\"devMode\":false};</script></head><body></body></html>";
        var extracted = LanguageServerEndpointDiscovery.TryExtractCsrfTokenFromHtml(html, out var token);

        Assert.True(extracted);
        Assert.Equal("81fc55a1-fe04-4d7f-be07-446a69da2d89", token);
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
