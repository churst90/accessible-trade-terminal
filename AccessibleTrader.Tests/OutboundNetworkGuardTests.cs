using System.Net;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Sdk.Alerts;

namespace AccessibleTrader.Tests;

/// <summary>
/// SSRF wall for the alert channels. Both channels connect to wherever the user
/// typed — a webhook URL, an SMTP host/port — and on the hosted server that was an
/// arbitrary probe of loopback, the private network and the cloud metadata service,
/// with delivery success/failure spoken back as a boolean oracle. Every registered
/// user could reach it: registration is open. These pin the deny-list, the
/// connect-time enforcement (inside ConnectCallback, so DNS rebinding has nothing
/// to rebind), the no-redirect rule, and — just as deliberately — that the desktop
/// (Full mode) keeps its LAN targets, which are a feature there.
/// </summary>
public class OutboundNetworkGuardTests
{
    // ── The deny-list itself ────────────────────────────────────────────────

    [Theory]
    [InlineData("127.0.0.1")]          // loopback
    [InlineData("127.8.8.8")]          // all of 127/8
    [InlineData("10.0.0.5")]           // RFC1918
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.20")]
    [InlineData("169.254.169.254")]    // cloud metadata / link-local
    [InlineData("100.64.0.1")]         // CGNAT
    [InlineData("0.0.0.0")]
    [InlineData("192.0.0.1")]          // protocol assignments
    [InlineData("192.0.2.1")]          // TEST-NET-1
    [InlineData("198.18.0.1")]         // benchmarking
    [InlineData("224.0.0.1")]          // multicast
    [InlineData("255.255.255.255")]    // broadcast
    [InlineData("::1")]                // v6 loopback
    [InlineData("::")]                 // v6 unspecified
    [InlineData("fe80::1")]            // v6 link-local
    [InlineData("fc00::1")]            // v6 unique-local
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]            // v6 multicast
    [InlineData("::ffff:10.0.0.1")]    // v4-mapped private must not smuggle through
    [InlineData("::ffff:127.0.0.1")]
    public void Private_loopback_and_special_addresses_are_not_public(string ip)
        => Assert.False(OutboundNetworkGuard.IsPublic(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    [InlineData("172.32.0.1")]         // just past RFC1918's 172.16/12
    [InlineData("100.128.0.1")]        // just past CGNAT's 100.64/10
    [InlineData("2606:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]     // v4-mapped public is fine
    public void Public_addresses_are_public(string ip)
        => Assert.True(OutboundNetworkGuard.IsPublic(IPAddress.Parse(ip)));

    // ── The mixed-answer rule: DNS rebinding ────────────────────────────────
    //
    // A2e SURVIVOR (E27). Weakening `resolved.Any(a => !IsPublic(a))` to `All(...)` passed the
    // entire suite. The cases around it were well covered — a private literal, a name that
    // resolves only to loopback — but the one the rule exists for was not: an attacker controls
    // their own DNS record, so a name answering with one public address AND one private one is
    // still a probe at the private one. Under the mutant that name was let through.
    //
    // It was untestable where it sat, inline after a live Dns.GetHostAddressesAsync. So the rule
    // was extracted to OutboundNetworkGuard.AllPublic, which needs no network.

    [Fact]
    public void A_name_answering_with_one_public_and_one_private_address_is_refused()
    {
        Assert.False(OutboundNetworkGuard.AllPublic(new[]
        {
            IPAddress.Parse("8.8.8.8"),      // looks fine
            IPAddress.Parse("10.0.0.1"),     // and this is the actual target
        }));
    }

    [Fact]
    public void A_name_answering_only_with_public_addresses_is_allowed()
    {
        // The control. Without it, "return false always" would pass the test above.
        Assert.True(OutboundNetworkGuard.AllPublic(new[]
        {
            IPAddress.Parse("8.8.8.8"),
            IPAddress.Parse("1.1.1.1"),
        }));
    }

    [Fact]
    public void A_name_that_resolves_to_nothing_fails_closed()
    {
        // Part of the rule, not a null guard: All() over an empty sequence is TRUE, so an empty
        // answer would be "every address is public" if the count check were dropped.
        Assert.False(OutboundNetworkGuard.AllPublic(Array.Empty<IPAddress>()));
    }

    [Fact]
    public void TheResolverUsesThatRule_AndNotACopyOfIt()
    {
        // A pure function tested in isolation says nothing about whether the caller reads it —
        // the "scan guard checks presence, not path" trap. localhost resolves offline to
        // loopback, so this drives the real resolver down the same rule.
        Assert.False(OutboundNetworkGuard.AllPublic(new[] { IPAddress.Loopback }));
        var ex = Assert.ThrowsAsync<HttpRequestException>(
            () => OutboundNetworkGuard.ResolvePublicOrThrowAsync("localhost")).GetAwaiter().GetResult();
        Assert.Contains("public internet", ex.Message);
    }

    // ── Resolution path ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    [InlineData("169.254.169.254")]
    [InlineData("localhost")]          // resolves offline, to loopback — must still be caught
    public async Task Resolving_a_private_target_throws(string host)
    {
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => OutboundNetworkGuard.ResolvePublicOrThrowAsync(host));
        Assert.Contains("public internet", ex.Message);
    }

    [Fact]
    public async Task A_public_ip_literal_passes_without_dns()
    {
        var addresses = await OutboundNetworkGuard.ResolvePublicOrThrowAsync("8.8.8.8");
        Assert.Equal(IPAddress.Parse("8.8.8.8"), Assert.Single(addresses));
    }

    // ── Enforcement lives in the connect, not in a pre-check ────────────────

    [Fact]
    public async Task Guarded_client_refuses_loopback_before_any_connection_is_made()
    {
        using var client = AlertChannelHttpClient.Create(blockPrivateNetworks: true);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("https://127.0.0.1:9/"));
        Assert.Contains("public internet", ex.ToString());
    }

    [Fact]
    public async Task Unguarded_desktop_client_fails_on_loopback_only_because_nothing_listens()
    {
        // Vacuity check for the guard: same request, guard off — the error is a
        // plain connection failure, NOT the policy refusal. Port 9 (discard) is
        // closed, so nothing is actually contacted and the test stays offline.
        using var client = AlertChannelHttpClient.Create(blockPrivateNetworks: false);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("https://127.0.0.1:9/"));
        Assert.DoesNotContain("public internet", ex.ToString());
    }

    [Fact]
    public async Task Redirects_are_never_followed()
    {
        // An open redirect on an approved host must not re-aim a delivery. The
        // listener answers 302 pointing at the metadata service; the client must
        // hand back the 302 rather than chase it.
        using var listener = new HttpListener();
        int port = FreePort();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var serve = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            ctx.Response.StatusCode = 302;
            ctx.Response.RedirectLocation = "http://169.254.169.254/latest/meta-data/";
            ctx.Response.Close();
        });

        using var client = AlertChannelHttpClient.Create(blockPrivateNetworks: false);
        using var resp = await client.GetAsync($"http://127.0.0.1:{port}/hook");

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        await serve;
        listener.Stop();
    }

    private static int FreePort()
    {
        var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    // ── The channels themselves ─────────────────────────────────────────────

    private static AlertFired SampleAlert() => new(
        new AlertDefinition
        {
            Id = "a1",
            Name = "Gold crossed 2500",
            Target = AlertTarget.Price,
            Condition = AlertCondition.CrossesAbove,
            Delivery = AlertDelivery.Both,
            WebhookTarget = "Default",
        },
        TriggeringValue: 2501.5,
        PreviousValue: 2498.0,
        SpeechText: "Gold crossed above 2500.");

    [Fact]
    public async Task Hosted_webhook_to_an_internal_target_is_refused_at_connect()
    {
        var channel = new WebhookAlertChannel(
            AlertChannelHttpClient.Create(blockPrivateNetworks: true),
            () => new WebhookAlertChannelConfig
            {
                Webhooks = new[] { new NamedWebhook { Name = "Default", Url = "https://192.168.1.20/hook" } }
            });

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => channel.SendAsync(SampleAlert()));
        Assert.Contains("public internet", ex.ToString());
    }

    private static EmailAlertChannelConfig EmailCfg(string host, int port) => new()
    {
        Host = host, Port = port, FromAddress = "a@b.c", ToAddress = "d@e.f",
    };

    [Fact]
    public async Task Hosted_smtp_to_the_metadata_service_is_refused()
    {
        var channel = new EmailAlertChannel(
            () => EmailCfg("169.254.169.254", 587), new DemoPolicy(HostMode.Hosted));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => channel.SendAsync(SampleAlert()));
        Assert.Contains("public internet", ex.Message);
    }

    [Fact]
    public async Task Hosted_smtp_port_scanning_is_refused_before_any_lookup()
    {
        // 6379 is Redis — the classic internal probe. The port allow-list fires
        // before DNS, so even a public-looking host cannot be used to scan.
        var channel = new EmailAlertChannel(
            () => EmailCfg("smtp.example.com", 6379), new DemoPolicy(HostMode.Hosted));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => channel.SendAsync(SampleAlert()));
        Assert.Contains("mail submission", ex.Message);
    }

    [Fact]
    public async Task An_out_of_range_port_is_named_not_thrown_from_smtpclient_internals()
    {
        var channel = new EmailAlertChannel(() => EmailCfg("smtp.example.com", 0),
            new DemoPolicy(HostMode.Full));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => channel.SendAsync(SampleAlert()));
        Assert.Contains("not a valid port", ex.Message);
    }

    [Fact]
    public async Task Desktop_smtp_keeps_lan_targets()
    {
        // Vacuity check: Full mode applies no target policy. Loopback with a closed
        // port fails as a CONNECTION error from SmtpClient, never as the policy
        // refusal — proving the wall is off where LAN mail relays are legitimate.
        var channel = new EmailAlertChannel(() => EmailCfg("127.0.0.1", FreePort()),
            new DemoPolicy(HostMode.Full));
        var ex = await Record.ExceptionAsync(() => channel.SendAsync(SampleAlert()));
        Assert.NotNull(ex);
        Assert.DoesNotContain("public internet", ex!.Message);
        Assert.DoesNotContain("mail submission", ex.Message);
    }
}
