using AccessibleTrader.WebHost.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// Pins the Phase A (2026-07) WebHost hardening: response security headers on
/// every mode, and the two-tier per-IP rate limiter that throttles
/// credential-guessing POSTs far below general browsing.
/// </summary>
public class WebHostSecurityPolicyTests
{
    // ── Security headers ──────────────────────────────────────────────────

    [Fact]
    public void Apply_SetsTheCoreHeaderSet()
    {
        var ctx = new DefaultHttpContext();
        SecurityHeadersPolicy.Apply(ctx);

        var h = ctx.Response.Headers;
        Assert.Equal("nosniff", h["X-Content-Type-Options"]);
        Assert.Equal("DENY", h["X-Frame-Options"]);
        Assert.Equal("strict-origin-when-cross-origin", h["Referrer-Policy"]);
        Assert.Equal(SecurityHeadersPolicy.ContentSecurityPolicy, h["Content-Security-Policy"]);
        Assert.False(string.IsNullOrEmpty(h["Permissions-Policy"]));
    }

    [Fact]
    public void Apply_DemoMode_AllowsSameOriginFraming()
    {
        // The --demo build is embedded in a same-origin iframe on the marketing site.
        var services = new ServiceCollection();
        services.AddSingleton(new AccessibleTrader.Core.Services.DemoPolicy(
            AccessibleTrader.Core.Services.HostMode.Demo));
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        SecurityHeadersPolicy.Apply(ctx);

        var h = ctx.Response.Headers;
        Assert.Equal("SAMEORIGIN", h["X-Frame-Options"]);
        Assert.Contains("frame-ancestors 'self'", h["Content-Security-Policy"].ToString());
        Assert.DoesNotContain("frame-ancestors 'none'", h["Content-Security-Policy"].ToString());
    }

    [Fact]
    public void DemoCsp_DiffersFromStrictCsp_OnlyInFrameAncestors()
    {
        // The demo CSP is a full copy of the strict one with a single directive
        // changed — this pin stops the copies drifting apart when someone edits
        // one and forgets the other (new script-src, connect-src, etc.).
        string strictNormalized = SecurityHeadersPolicy.ContentSecurityPolicy
            .Replace("frame-ancestors 'none'", "frame-ancestors 'self'");
        Assert.Equal(strictNormalized, SecurityHeadersPolicy.DemoContentSecurityPolicy);
    }

    [Fact]
    public void Apply_HostedAndDesktop_RefuseAllFraming()
    {
        // A non-demo context (hosted accounts / desktop) keeps DENY + 'none'.
        var services = new ServiceCollection();
        services.AddSingleton(new AccessibleTrader.Core.Services.DemoPolicy(
            AccessibleTrader.Core.Services.HostMode.Hosted));
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        SecurityHeadersPolicy.Apply(ctx);

        var h = ctx.Response.Headers;
        Assert.Equal("DENY", h["X-Frame-Options"]);
        Assert.Contains("frame-ancestors 'none'", h["Content-Security-Policy"].ToString());
    }

    [Fact]
    public void Hsts_IsSentOnlyOverHttps()
    {
        var plain = new DefaultHttpContext();
        SecurityHeadersPolicy.Apply(plain);
        Assert.True(string.IsNullOrEmpty(plain.Response.Headers["Strict-Transport-Security"]));

        var tls = new DefaultHttpContext();
        tls.Request.Scheme = "https"; // what UseForwardedHeaders produces behind nginx
        SecurityHeadersPolicy.Apply(tls);
        Assert.Equal(SecurityHeadersPolicy.StrictTransportSecurity,
            tls.Response.Headers["Strict-Transport-Security"]);
    }

    [Fact]
    public void Csp_LocksDownScriptsFramesAndObjects_WithoutBreakingBlazorServer()
    {
        var csp = SecurityHeadersPolicy.ContentSecurityPolicy;

        // Hard guarantees.
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("base-uri 'self'", csp);
        Assert.DoesNotContain("unsafe-eval", csp);

        // Blazor Server requirements: the SignalR websocket and the inline
        // style attributes components use for positioning.
        Assert.Contains("wss:", csp);
        Assert.Contains("style-src 'self' 'unsafe-inline'", csp);
        // data: favicon in App.razor.
        Assert.Contains("img-src 'self' data:", csp);
    }

    // ── Auth rate limiting ────────────────────────────────────────────────

    [Theory]
    [InlineData("POST", "/account/login", true)]
    [InlineData("POST", "/account/register", true)]
    [InlineData("POST", "/Account/Login", true)]   // route casing must not bypass
    [InlineData("POST", "/account/loginwith2fa", true)]      // 2FA code guessing
    [InlineData("POST", "/account/loginwithrecovery", true)] // recovery-code guessing
    [InlineData("POST", "/account/forgotpassword", true)]    // writes attacker-supplied audit records
    [InlineData("POST", "/account/resetpassword", true)]     // token guessing
    [InlineData("GET", "/account/forgotpassword", false)]    // rendering the form is general traffic
    [InlineData("GET", "/account/login", false)]   // rendering the form is general traffic
    [InlineData("POST", "/account/logout", false)] // logout is not a guessing surface
    [InlineData("POST", "/", false)]
    [InlineData("GET", "/", false)]
    public void IsAuthMutation_ClassifiesCredentialAttempts(string method, string path, bool expected)
    {
        Assert.Equal(expected, AuthRateLimitPolicy.IsAuthMutation(method, new PathString(path)));
    }

    [Fact]
    public void GetPartition_SeparatesAuthAndGeneralTiers_PerIp()
    {
        var login = new DefaultHttpContext();
        login.Request.Method = "POST";
        login.Request.Path = "/account/login";
        login.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");

        var browse = new DefaultHttpContext();
        browse.Request.Method = "GET";
        browse.Request.Path = "/";
        browse.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");

        var authPartition = AuthRateLimitPolicy.GetPartition(login);
        var generalPartition = AuthRateLimitPolicy.GetPartition(browse);

        // Same IP lands in two independent buckets — burning the strict auth
        // budget must not throttle normal browsing, and vice versa.
        Assert.NotEqual(authPartition.PartitionKey, generalPartition.PartitionKey);
        Assert.StartsWith("auth:", authPartition.PartitionKey);
        Assert.StartsWith("general:", generalPartition.PartitionKey);
    }

    [Fact]
    public void AuthTier_IsFarStricterThanGeneralTier()
    {
        // 10 attempts / 5 min vs 200 requests / 10 s — the auth tier must stay
        // orders of magnitude below page-load rates or it is not doing its job.
        Assert.True(AuthRateLimitPolicy.AuthPermitLimit <= AuthRateLimitPolicy.GeneralPermitLimit / 10);
        Assert.True(AuthRateLimitPolicy.AuthWindow >= AuthRateLimitPolicy.GeneralWindow);
    }
}

/// <summary>
/// Pins the Full-mode bind guard: an unauthenticated fully-trusted terminal
/// (live trading, API keys, server-side scripts) must never be served on an
/// address other machines can reach. Before 2026-08-22 a WebHost started with
/// neither --accounts nor --demo on a public bind gave HostMode.Full to every
/// anonymous visitor.
/// </summary>
public class FullModeBindPolicyTests
{
    [Theory]
    [InlineData("http://localhost:5145")]
    [InlineData("https://LOCALHOST:443")]
    [InlineData("http://127.0.0.1:5000")]
    [InlineData("http://127.0.0.2:5000")]   // whole 127/8 block is loopback
    [InlineData("http://[::1]:5000")]
    public void Loopback_binds_are_allowed(string address)
    {
        Assert.True(FullModeBindPolicy.IsLoopback(address));
        Assert.Null(FullModeBindPolicy.FindNonLoopbackAddress(new[] { address }));
    }

    [Theory]
    [InlineData("http://0.0.0.0:5000")]     // binds every interface
    [InlineData("http://[::]:5000")]        // binds every interface (v6)
    [InlineData("http://+:80")]             // Kestrel wildcard — unparseable, fails closed
    [InlineData("http://*:80")]             // Kestrel wildcard — unparseable, fails closed
    [InlineData("http://192.168.1.20:5000")]
    [InlineData("http://10.0.0.5:5000")]
    [InlineData("https://trade.example.com:443")]
    public void Non_loopback_binds_are_refused(string address)
    {
        Assert.False(FullModeBindPolicy.IsLoopback(address));
        Assert.Equal(address, FullModeBindPolicy.FindNonLoopbackAddress(new[] { address }));
    }

    [Fact]
    public void The_first_offending_address_is_named_even_among_safe_ones()
    {
        var addresses = new[] { "http://localhost:5145", "http://0.0.0.0:8080", "http://[::]:9090" };
        Assert.Equal("http://0.0.0.0:8080", FullModeBindPolicy.FindNonLoopbackAddress(addresses));
    }
}
