using System.Net;
using System.Net.Sockets;
using System.Text;
using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>With no host bridge, <see cref="PluginHostServices.CreateHttpClient"/> still honours the
    /// timeout and the response cap the provider asked for.</b>
    ///
    /// <para>
    /// A2p PH1/PH2: ignoring the caller's timeout, and dropping the response cap, both left the
    /// suite green on this fallback path. It is the path every plugin takes in the CLI, in
    /// StrategyLab, and on any head that has not installed the factory — and its own doc says it
    /// exists "so tests don't accidentally allow unbounded responses". Both are checked against a
    /// real HTTP server on loopback: a request that must give up, and a body that must be refused.
    /// </para>
    /// </summary>
    // Same collection as every class that touches the global PluginHostServices bridge.
    [Collection("ProviderCredentialBridge")]
    public class PluginHostFallbackHttpClientTests
    {
        private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(20);

        [Fact]
        public async Task A_provider_asking_for_a_short_timeout_gives_up_at_that_timeout()
        {
            var prior = PluginHostServices.HttpClientFactory;
            PluginHostServices.HttpClientFactory = null;
            try
            {
                using var stop = new CancellationTokenSource();
                using var server = LoopbackHttp.Start(async ctx =>
                {
                    // Never answers until the test is over.
                    try { await Task.Delay(Timeout.Infinite, stop.Token); } catch (OperationCanceledException) { }
                    try { ctx.Response.Abort(); } catch (Exception) { }
                });

                using var http = PluginHostServices.CreateHttpClient(
                    "a2p-timeout", new[] { "127.0.0.1" }, timeout: TimeSpan.FromMilliseconds(300));

                var request = http.GetStringAsync(server.Url);
                var finished = await Task.WhenAny(request, Task.Delay(Ceiling));
                stop.Cancel();

                Assert.True(finished == request,
                    "A request with a 300 ms timeout was still waiting after 20 s: the provider's timeout was ignored.");
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
            }
            finally { PluginHostServices.HttpClientFactory = prior; }
        }

        [Fact]
        public async Task A_response_larger_than_the_provider_cap_is_refused()
        {
            var prior = PluginHostServices.HttpClientFactory;
            PluginHostServices.HttpClientFactory = null;
            try
            {
                var body = Encoding.ASCII.GetBytes(new string('x', 64 * 1024));
                using var server = LoopbackHttp.Start(async ctx =>
                {
                    ctx.Response.ContentLength64 = body.Length;
                    await ctx.Response.OutputStream.WriteAsync(body);
                    ctx.Response.Close();
                });

                using var http = PluginHostServices.CreateHttpClient(
                    "a2p-cap", new[] { "127.0.0.1" }, maxResponseBytes: 1024);

                await Assert.ThrowsAsync<HttpRequestException>(() => http.GetStringAsync(server.Url).WaitAsync(Ceiling));
            }
            finally { PluginHostServices.HttpClientFactory = prior; }
        }

        private sealed class LoopbackHttp : IDisposable
        {
            private readonly HttpListener _listener = new();
            public string Url { get; }

            private LoopbackHttp(Func<HttpListenerContext, Task> handler)
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                int port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                _listener.Start();
                Url = $"http://127.0.0.1:{port}/";
                _ = Task.Run(async () =>
                {
                    while (_listener.IsListening)
                    {
                        HttpListenerContext ctx;
                        try { ctx = await _listener.GetContextAsync(); }
                        catch (Exception) { return; }
                        _ = Task.Run(async () => { try { await handler(ctx); } catch (Exception) { } });
                    }
                });
            }

            public static LoopbackHttp Start(Func<HttpListenerContext, Task> handler) => new(handler);

            public void Dispose()
            {
                try { _listener.Stop(); _listener.Close(); } catch (Exception) { }
            }
        }
    }
}
