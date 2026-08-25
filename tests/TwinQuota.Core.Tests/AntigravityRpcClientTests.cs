using System.Net;
using TwinQuota.Core;

namespace TwinQuota.Core.Tests;

public sealed class AntigravityRpcClientTests
{
    [Fact]
    public async Task UsesLoopbackConnectRpcAndCsrfHeader()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"response\":{\"groups\":[]}}")
            };
        });
        var client = new AntigravityRpcClient(new HttpClient(handler));
        var endpoint = new LanguageServerEndpoint(AntigravitySurface.Desktop2, 12, 54321, "local-secret");

        await client.GetQuotaSummaryAsync(endpoint, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal("127.0.0.1", capturedRequest.RequestUri?.Host);
        Assert.Equal(54321, capturedRequest.RequestUri?.Port);
        Assert.EndsWith("/RetrieveUserQuotaSummary", capturedRequest.RequestUri?.AbsolutePath);
        Assert.Equal("local-secret", capturedRequest.Headers.GetValues("x-codeium-csrf-token").Single());
        Assert.Equal("1", capturedRequest.Headers.GetValues("connect-protocol-version").Single());
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }
}
