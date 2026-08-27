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
            new AccessibleTrader.WebHost.Services.WebHostPluginHttpClientFactory(),
        };

        [Theory]
        [MemberData(nameof(Factories))]
        public void APluginClientRefusesToFollowRedirectsItself(IPluginHttpClientFactory factory)
        {
            using var client = factory.Create(Policy());

            var inner = InnermostHandler(client);

            var handler = Assert.IsType<HttpClientHandler>(inner);
            Assert.False(handler.AllowAutoRedirect,
                "A redirect followed inside the inner handler never passes the allow-list — "
                + "it is checked once on the initial URI and never on the hop.");
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
        public void BothCopiesOfTheFactoryDisableAutoRedirect(string relativePath)
        {
            // The two files are byte-identical in this respect and the defect was in both, so
            // fixing one and testing one would leave the desktop head exposed. The MAUI head
            // cannot be referenced from this project, so this is the check that can be made.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);

            var text = File.ReadAllText(Path.Combine(dir!.FullName, relativePath));

            Assert.Contains("new HttpClientHandler { AllowAutoRedirect = false }", text,
                StringComparison.Ordinal);
            Assert.DoesNotContain("new HttpClientHandler();", text, StringComparison.Ordinal);
        }
    }
}
