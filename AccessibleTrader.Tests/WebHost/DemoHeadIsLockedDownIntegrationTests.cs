using System.Net;
using AccessibleTrader.Core.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// The public <c>--demo</c> head is the LOCKED-DOWN tier, observed from the booted process.
///
/// <para>
/// <b>What nothing checked (A2o, survivor O22).</b> <c>Program.cs</c> derives one
/// <see cref="HostMode"/> from the two flags, and every gate in the product — live trading,
/// the API-keys modal, server-side Roslyn scripts, the AI analyst, the unauthenticated
/// Full-mode alert endpoints, the bind guard — reads it. Mutating the demo arm of that
/// ternary to <c>HostMode.Full</c> left all 8,088 tests green: the demo head was booted by
/// <see cref="WebHostPathBaseIntegrationTests"/> (prefix, base href, negotiate) and by
/// nothing that asked WHICH tier it had booted into. <see cref="DemoPolicy"/>'s own tests
/// construct the policy directly, so they pin what each tier allows, never which tier the
/// public process is. The demo is the one head an anonymous stranger reaches from the
/// marketing homepage.
/// </para>
///
/// <para>
/// Asserted three ways, from most to least direct: the registered policy; a Full-only
/// endpoint that must not exist on the wire; and the framing headers, which are the one
/// place the demo is deliberately LOOSER than every other mode (it is embedded in a
/// same-origin iframe) and so also pin that the demo was not collapsed into the strict set.
/// </para>
/// </summary>
[Collection("ProviderCredentialBridge")]
public sealed class DemoHeadIsLockedDownIntegrationTests : IDisposable
{
    private readonly PluginBridgeScope _bridges = new();
    public void Dispose() => _bridges.Dispose();

    /// <summary>
    /// The demo factory, minus the two desktop-only hosted services — only reachable if the
    /// head has wrongly booted as Full, and they shell out to D-Bus / notify-send, which a
    /// red run of this test must not do to the machine running it.
    /// </summary>
    private static WebApplicationFactory<WebHostDemoMode> Demo()
        => WebHostIntegration.DemoFactory().WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            var desktopOnly = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                         && (d.ImplementationType == typeof(AccessibleTrader.WebHost.Services.LocalBackgroundMonitor)
                          || d.ImplementationType == typeof(AccessibleTrader.WebHost.Services.Tray.DesktopTrayService)))
                .ToList();
            foreach (var d in desktopOnly) services.Remove(d);
        }));

    [Fact]
    public void The_demo_process_runs_the_demo_tier_with_every_desktop_power_off()
    {
        using var factory = Demo();
        var policy = factory.Services.GetRequiredService<DemoPolicy>();

        Assert.Equal(HostMode.Demo, policy.Mode);
        Assert.True(policy.IsDemo);
        Assert.False(policy.AllowLiveTrading, "the public demo must never trade real money");
        Assert.False(policy.AllowApiKeysModal, "the public demo must never accept broker keys");
        Assert.False(policy.AllowCustomScripts, "server-side Roslyn on the public demo is remote code execution");
        Assert.True(policy.BlockPrivateNetworkTargets, "the demo server must not reach its own private network");
    }

    [Fact]
    public async Task The_full_mode_alert_endpoints_do_not_exist_on_the_demo()
    {
        using var factory = Demo();
        using var client = WebHostIntegration.NewClient(factory);

        // Positive control first: the demo IS serving under its prefix, so a 404 below is
        // about the route, not about a head that failed to boot.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/app/")).StatusCode);

        // /alerts/recent is Full mode's unauthenticated local surface, with antiforgery off.
        var alerts = await client.PostAsync("/app/alerts/recent/read-all", null);
        Assert.NotEqual(HttpStatusCode.Found, alerts.StatusCode);
        var page = await client.GetAsync("/app/alerts/recent");
        Assert.DoesNotContain("Recent alerts", await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_demo_permits_same_origin_framing_and_nothing_wider()
    {
        using var factory = Demo();
        using var client = WebHostIntegration.NewClient(factory);

        var resp = await client.GetAsync("/app/");

        Assert.Equal("SAMEORIGIN", Assert.Single(resp.Headers.GetValues("X-Frame-Options")));

        // Blazor's interactive-server endpoint adds a SECOND Content-Security-Policy header of
        // its own, "frame-ancestors 'self'", on every mode (browsers enforce the intersection,
        // so the strict modes' 'none' still wins). That header would satisfy a naive check
        // whatever tier booted, so read the app's own policy — the one that sets default-src.
        string appCsp = Assert.Single(resp.Headers.GetValues("Content-Security-Policy"),
            v => v.Contains("default-src", StringComparison.Ordinal));
        Assert.Contains("frame-ancestors 'self'", appCsp, StringComparison.Ordinal);
        Assert.DoesNotContain("frame-ancestors 'none'", appCsp, StringComparison.Ordinal);
    }
}
