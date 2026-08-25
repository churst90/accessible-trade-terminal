using System.Net;
using AccessibleTrader.Sdk.Services;
using AccessibleTrader.WebHost.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// Full-mode (local single-user desktop host, no flags) over HTTP: the
/// recent-alerts review page the tray opens, the dev-gated diagnostics
/// endpoint, and the 2026-08-22 plugin-bridge wiring — Full mode installs
/// <see cref="PluginHostServices.SecureStorage"/> so the Schwab OAuth refresh
/// token persists, while the hosted multi-user head deliberately leaves the
/// bridge null (its secret store is process-wide; a persisted token there
/// would be shared by every signed-in user).
///
/// In the ProviderCredentialBridge collection because booting a Full-mode host
/// mutates that process-wide static; the fixture snapshots and restores it.
/// </summary>
[Collection("ProviderCredentialBridge")]
public sealed class WebHostFullModeIntegrationTests : IDisposable
{
    private readonly IPluginSecureStorage? _previousBridge;

    public WebHostFullModeIntegrationTests()
    {
        _previousBridge = PluginHostServices.SecureStorage;
    }

    public void Dispose()
    {
        PluginHostServices.SecureStorage = _previousBridge;
    }

    [Fact]
    public async Task Alerts_page_lifecycle_no_alerts_then_read_then_dismiss()
    {
        using var factory = WebHostIntegration.FullFactory();
        using var client = WebHostIntegration.NewClient(factory);

        var empty = await client.GetAsync("/alerts/recent");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Contains("No recent alerts", await empty.Content.ReadAsStringAsync());

        // The monitor/tray normally feed this buffer; drive it directly.
        var buffer = factory.Services.GetRequiredService<RecentAlertsBuffer>();
        buffer.Add("BTC crossed 100000", "BTC/USD");
        var alert = Assert.Single(buffer.Snapshot());

        var listed = await client.GetAsync("/alerts/recent");
        var listedHtml = await listed.Content.ReadAsStringAsync();
        Assert.Contains("BTC crossed 100000", listedHtml);
        Assert.Contains("unread", listedHtml);
        Assert.Contains($"/alerts/recent/{alert.Id}/dismiss", listedHtml);

        var read = await client.PostAsync($"/alerts/recent/{alert.Id}/read", null);
        Assert.Equal(HttpStatusCode.Found, read.StatusCode);
        Assert.Equal("/alerts/recent", read.Headers.Location!.OriginalString);
        Assert.Equal(RecentAlertState.Read, buffer.Snapshot().Single().State);

        var dismissed = await client.PostAsync($"/alerts/recent/{alert.Id}/dismiss", null);
        Assert.Equal(HttpStatusCode.Found, dismissed.StatusCode);
        Assert.Empty(buffer.Snapshot());
        Assert.Contains("No recent alerts", await client.GetStringAsync("/alerts/recent"));
    }

    [Fact]
    public async Task Mark_all_read_clears_every_unread_alert()
    {
        using var factory = WebHostIntegration.FullFactory();
        using var client = WebHostIntegration.NewClient(factory);

        var buffer = factory.Services.GetRequiredService<RecentAlertsBuffer>();
        buffer.Add("first", null);
        buffer.Add("second", "ETH/USD");
        Assert.Equal(2, buffer.UnreadCount);

        var resp = await client.PostAsync("/alerts/recent/read-all", null);
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Equal(0, buffer.UnreadCount);
        Assert.Equal(2, buffer.Snapshot().Count); // read, not dismissed
    }

    [Fact]
    public async Task Diag_journal_serves_json_in_development_and_404s_in_production()
    {
        using (var dev = WebHostIntegration.FullFactory("Development"))
        using (var devClient = WebHostIntegration.NewClient(dev))
        {
            var resp = await devClient.GetAsync("/diag/journal");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Equal("application/json", resp.Content.Headers.ContentType!.MediaType);
        }

        using (var prod = WebHostIntegration.FullFactory("Production"))
        using (var prodClient = WebHostIntegration.NewClient(prod))
        {
            var resp = await prodClient.GetAsync("/diag/journal");
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }
    }

    [Fact]
    public async Task Full_mode_installs_the_plugin_secure_storage_bridge_and_hosted_does_not()
    {
        PluginHostServices.SecureStorage = null;

        // Hosted head: booting and serving must leave the bridge null — its
        // secret store is shared across users, so a static bridge would let
        // one user's persisted Schwab token be read by every other user.
        var hostedRoot = Directory.CreateTempSubdirectory("att-bridge-hosted-").FullName;
        try
        {
            using (var hosted = WebHostIntegration.HostedFactory(hostedRoot))
            using (var hostedClient = WebHostIntegration.NewClient(hosted))
            {
                await hostedClient.GetAsync("/terminal/account/login");
                Assert.Null(PluginHostServices.SecureStorage);
            }
        }
        finally
        {
            try { Directory.Delete(hostedRoot, recursive: true); } catch { }
        }

        // Full mode: the single-user host wires the bridge at startup so the
        // Schwab plugin's SecureStorage-only persistence has somewhere to go.
        using (var full = WebHostIntegration.FullFactory())
        using (var fullClient = WebHostIntegration.NewClient(full))
        {
            await fullClient.GetAsync("/alerts/recent");
            Assert.IsType<WebHostSecureStorageService>(PluginHostServices.SecureStorage);
        }
    }
}
