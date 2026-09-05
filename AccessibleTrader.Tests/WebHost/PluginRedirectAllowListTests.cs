using System.Reflection;
using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.Tests.WebHost
{
    /// <summary>
    /// <b>A plugin's HTTP client does not follow a redirect off its own allow-list.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// <c>HostAllowListHandler</c> is a <see cref="DelegatingHandler"/> layered <i>above</i>
    /// <c>new HttpClientHandler()</c>, which has <c>AllowAutoRedirect = true</c> by default.
    /// The redirect is followed <b>inside the inner handler, below the delegating one</b>, so
    /// the allow-list is checked once — on the initial URI — and never on the hop.
    /// </para>
    ///
    /// <para>
    /// An allow-listed host answering
    /// <c>302 Location: http://169.254.169.254/latest/meta-data/</c> was followed and the body
    /// handed back to the plugin. That address is the cloud instance-metadata endpoint; on a
    /// hosted deployment it is the classic SSRF pivot to credentials.
    /// </para>
    ///
    /// <para>
    /// The repo already knew: <c>AllowAutoRedirect = false</c> appears in
    /// <c>OutboundNetworkGuard</c> and <c>WebHostIntegrationHarness</c> — the alert-channel
    /// factory got this right and documented why. The plugin factory did not, and both copies
    /// of it were byte-identical in that respect.
    /// </para>
    ///
    /// <para>
    /// ── What is enforced ───────────────────────────────────────────────────────
    /// The handler chain of the <c>HttpClient</c> the factory actually returns, walked by
    /// reflection down to the inner <see cref="HttpClientHandler"/>. Checking the object that
    /// ships beats checking the source that builds it.
    /// </para>
    /// </summary>
    public class PluginRedirectAllowListTests
    {
        private static HttpClientPolicy Policy() =>
            new("TestProvider", new[] { "api.example.com" });

        /// <summary>Walks an HttpClient's private handler chain to the innermost handler.</summary>
        private static object InnermostHandler(HttpClient client)
        {
            object? current = typeof(HttpMessageInvoker)
                .GetField("_handler", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(client);

            Assert.NotNull(current);

            while (current is DelegatingHandler d)
            {
                var inner = typeof(DelegatingHandler)
                    .GetProperty("InnerHandler", BindingFlags.Public | BindingFlags.Instance)!
                    .GetValue(d);
                if (inner == null) break;
                current = inner;
            }

            return current!;
        }

        /// <summary>
        /// Only the WebHost factory can be exercised here: the test project references
        /// <c>AccessibleTrader.BlazorClient.Components</c>, not the MAUI head, which does not
        /// build on this box for want of the MAUI workloads. The MAUI copy is byte-identical
        /// in this respect and is covered by
        /// <see cref="BothCopiesOfTheFactoryDisableAutoRedirect"/> — a scan, stated as one
        /// rather than dressed up as a behavioural check.
        /// </summary>
        public static TheoryData<IPluginHttpClientFactory> Factories() => new()
        {
            new AccessibleTrader.WebHost.Services.WebHostPluginHttpClientFactory(
                new AccessibleTrader.Core.Services.DemoPolicy(AccessibleTrader.Core.Services.HostMode.Hosted)),
        };

        [Theory]
        [MemberData(nameof(Factories))]
        public void APluginClientRefusesToFollowRedirectsItself(IPluginHttpClientFactory factory)
        {
            using var client = factory.Create(Policy());

            var inner = InnermostHandler(client);

            var handler = Assert.IsType<SocketsHttpHandler>(inner);
            Assert.False(handler.AllowAutoRedirect,
                "A redirect followed inside the inner handler never passes the allow-list — "
                + "it is checked once on the initial URI and never on the hop.");
        }

        // ── The DNS half (hosted notes §5c, open from 2026-08-24 to 2026-09-05) ──────────
        //
        // The allow-list matched the NAME. An allow-listed hostname that RESOLVES to loopback,
        // RFC1918 or the cloud metadata address was still connected to. The guard is the one
        // the alert channels already had: resolve inside the connect and refuse a non-public
        // address, so the address checked is the address reached.

        private static (System.Net.Sockets.TcpListener listener, int port, Task<bool> reached) LocalListener()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            var reached = Task.Run(async () =>
            {
                using var accept = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    using var socket = await listener.AcceptSocketAsync(accept.Token);
                    var reply = System.Text.Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
                    await socket.SendAsync(reply, System.Net.Sockets.SocketFlags.None);
                    return true;
                }
                catch { return false; }   // cancelled, or the listener was stopped under it
            });
            return (listener, port, reached);
        }

        [Fact]
        public async Task OnAHostedHead_AnAllowListedNameThatResolvesPrivate_IsRefusedAtConnect()
        {
            // "localhost" is ON the allow-list — the name check passes. The socket must still
            // refuse, because what it resolves to is not public.
            var (listener, port, reached) = LocalListener();
            try
            {
                var factory = new AccessibleTrader.WebHost.Services.WebHostPluginHttpClientFactory(
                    new AccessibleTrader.Core.Services.DemoPolicy(AccessibleTrader.Core.Services.HostMode.Hosted));
                using var client = factory.Create(new HttpClientPolicy("TestProvider", new[] { "localhost" }));

                var ex = await Assert.ThrowsAsync<HttpRequestException>(
                    () => client.GetAsync($"http://localhost:{port}/"));

                Assert.Contains("not on the public internet", ex.Message);
                listener.Stop();
                Assert.False(await reached, "the listener was reached — the guard checked the name and not the address");
            }
            finally { listener.Stop(); }
        }

        [Fact]
        public async Task OnTheDesktop_TheSameRequestReachesTheLocalGateway()
        {
            // The control, and the reason the guard is a POLICY rather than always on: on the
            // desktop a plugin talking to a gateway on the user's own machine (IBKR's Client
            // Portal) is the whole point. Full mode does not block private targets.
            var (listener, port, reached) = LocalListener();
            try
            {
                var factory = new AccessibleTrader.WebHost.Services.WebHostPluginHttpClientFactory(
                    new AccessibleTrader.Core.Services.DemoPolicy(AccessibleTrader.Core.Services.HostMode.Full));
                using var client = factory.Create(new HttpClientPolicy("TestProvider", new[] { "localhost" }));

                var resp = await client.GetAsync($"http://localhost:{port}/");

                Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
                Assert.True(await reached);
            }
            finally { listener.Stop(); }
        }

        [Fact]
        public void WithNoPolicy_TheFactoryFailsClosed()
        {
            // A factory built without a policy behaves as the public server does. The
            // alternative — "no policy, no guard" — is the configuration a forgotten
            // registration produces, on the one head that faces the internet.
            var factory = new AccessibleTrader.WebHost.Services.WebHostPluginHttpClientFactory();
            using var client = factory.Create(Policy());

            var handler = Assert.IsType<SocketsHttpHandler>(InnermostHandler(client));
            Assert.NotNull(handler.ConnectCallback);
        }

        [Theory]
        [MemberData(nameof(Factories))]
        public void TheAllowListHandlerIsStillInTheChain(IPluginHttpClientFactory factory)
        {
            // Vacuity check: an HttpClient built with no delegating handler at all would
            // satisfy the assertion above by having no allow-list to escape from.
            using var client = factory.Create(Policy());

            object? current = typeof(HttpMessageInvoker)
                .GetField("_handler", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(client);

            Assert.IsAssignableFrom<DelegatingHandler>(current);
        }

        [Theory]
        [MemberData(nameof(Factories))]
        public void APolicyWithNoAllowedHostsIsRefused(IPluginHttpClientFactory factory)
        {
            // The allow-list is the whole security property; an empty one is a configuration
            // bug that must not silently produce an unrestricted client.
            Assert.ThrowsAny<ArgumentException>(
                () => factory.Create(new HttpClientPolicy("TestProvider", Array.Empty<string>())));
        }

        [Theory]
        [InlineData("AccessibleTrader.WebHost/Services/WebHostPluginHttpClientFactory.cs")]
        [InlineData("AccessibleTrader.BlazorClient/Services/MauiPluginHttpClientFactory.cs")]
        public void BothCopiesOfTheFactoryBuildOnTheSharedOutboundGuard(string relativePath)
        {
            // The two files are byte-identical in this respect and the defect was in both, so
            // fixing one and testing one would leave the desktop head exposed. The MAUI head
            // cannot be referenced from this project, so this is the check that can be made:
            // both build their inner handler through OutboundNetworkGuard.CreateHandler, which
            // is where AllowAutoRedirect = false and the connect-time address check live.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);

            var text = File.ReadAllText(Path.Combine(dir!.FullName, relativePath));

            Assert.Contains("OutboundNetworkGuard.CreateHandler(_blockPrivateNetworks)", text,
                StringComparison.Ordinal);
            Assert.Contains("policy?.BlockPrivateNetworkTargets ?? true", text, StringComparison.Ordinal);
            Assert.DoesNotContain("new HttpClientHandler", text, StringComparison.Ordinal);
        }
    }
}
