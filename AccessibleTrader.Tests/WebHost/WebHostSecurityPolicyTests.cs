using AccessibleTrader.WebHost.Services;
using Microsoft.AspNetCore.Http;
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
