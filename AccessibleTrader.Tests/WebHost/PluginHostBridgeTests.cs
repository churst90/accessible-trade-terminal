using AccessibleTrader.Sdk.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests.WebHost
{
    /// <summary>
    /// The plugin outbound-host allow-list must actually be installed on the WebHost.
    ///
    /// <c>MauiProgram</c> assigns five <c>PluginHostServices</c> bridges; <c>Program.cs</c>
    /// assigned one (<c>SecureStorage</c>, Full-mode only). So on demo, hosted AND local
    /// Full, <c>PluginHostServices.HttpClientFactory</c> was null and
    /// <c>PluginHostServices.CreateHttpClient</c> fell through to a bare
    /// <c>new HttpClient()</c> with no host check whatsoever — on the one head that faces
    /// the public internet. <c>WebHostPluginHttpClientFactory</c> was registered in DI but
    /// nothing ever resolved <c>IPluginHttpClientFactory</c> on this host, so it was dead
    /// code, and the SDK doc claiming enforcement "before bytes leave the process" was true
    /// only on the desktop head.
    ///
    /// These tests boot the REAL <c>Program.cs</c> and assert the bridge is present and
    /// enforcing, rather than asserting that the factory class works in isolation (it
    /// already did — that was never the problem).
    /// </summary>
    // Must be the SAME collection as every other class that touches the global
    // PluginHostServices bridge — a private collection name would still let this run in
    // parallel with the classes that install fake factories into that static, which is the
    // flake ProviderCredentialBridgeEnrollmentTests was written to prevent. (It caught this
    // file on its first full-suite run.)
    [Collection("ProviderCredentialBridge")]
    public class PluginHostBridgeTests
    {
        /// <summary>
        /// PluginHostServices is process-wide static state. Booting a host mutates it, so
        /// every test here restores whatever was there before — otherwise this file would
        /// leak a factory into unrelated tests and make failures depend on ordering.
        /// </summary>
        private sealed class BridgeSnapshot : IDisposable
        {
            private readonly IPluginHttpClientFactory? _prior;
            public BridgeSnapshot() => _prior = PluginHostServices.HttpClientFactory;
            public void Dispose() => PluginHostServices.HttpClientFactory = _prior;
        }

        private static string TempRoot()
        {
            string dir = Path.Combine(Path.GetTempPath(), "at-bridge-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void HostedBoot_InstallsThePluginHttpClientFactoryBridge()
        {
            using var snapshot = new BridgeSnapshot();
            PluginHostServices.HttpClientFactory = null;

            string root = TempRoot();
            try
            {
                using var factory = WebHostIntegration.HostedFactory(root);
                _ = factory.Services; // force the host to build and run Program.cs's bridge block

                Assert.True(PluginHostServices.HttpClientFactory != null,
                    "PluginHostServices.HttpClientFactory is null after the hosted head booted. " +
                    "Every plugin's CreateHttpClient then falls back to a bare HttpClient with no " +
                    "outbound-host allow-list, on the public-facing head.");
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public void FullBoot_InstallsThePluginHttpClientFactoryBridge()
        {
            // Full mode is the WebHost mode that trades real money with real keys, so the
            // allow-list matters most here.
            using var snapshot = new BridgeSnapshot();
            PluginHostServices.HttpClientFactory = null;

            using var factory = WebHostIntegration.FullFactory();
            _ = factory.Services;

            Assert.True(PluginHostServices.HttpClientFactory != null,
                "PluginHostServices.HttpClientFactory is null after the Full head booted.");
        }

        [Fact]
        public void TheBridgedFactory_IsResolvableFromTheRootProvider()
        {
            // The reason the bridge could not be written before: IPluginHttpClientFactory was
            // registered Scoped, and a process-wide static cannot hold a scoped service. If
            // someone makes it Scoped again, Program.cs's GetRequiredService throws at boot —
            // this test names why rather than leaving a startup crash to be diagnosed cold.
            using var snapshot = new BridgeSnapshot();

            using var factory = WebHostIntegration.FullFactory();
            var fromRoot = factory.Services.GetService<IPluginHttpClientFactory>();

            Assert.True(fromRoot != null,
                "IPluginHttpClientFactory cannot be resolved from the root provider. It must stay " +
                "Singleton — it is stateless, and PluginHostServices.HttpClientFactory is a static.");
        }

        [Fact]
        public async Task TheBridgedFactory_RefusesAHostOutsideThePluginsAllowList()
        {
            // End-to-end through the bridge the same way a plugin reaches it: the point is
            // that CreateHttpClient now returns an ENFORCING client on this head, not that
            // HostAllowListHandler works when constructed by hand.
            using var snapshot = new BridgeSnapshot();
            PluginHostServices.HttpClientFactory = null;

            using var factory = WebHostIntegration.FullFactory();
            _ = factory.Services;

            using var http = PluginHostServices.CreateHttpClient(
                "bridge-test", new[] { "api.example.com" });

            var blocked = await Assert.ThrowsAsync<HttpRequestException>(
                () => http.GetAsync("https://169.254.169.254/latest/meta-data/"));

            Assert.Contains("not in its outbound allow-list", blocked.Message, StringComparison.Ordinal);
        }
    }
}
