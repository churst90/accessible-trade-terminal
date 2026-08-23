using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// The credential-guessing rate limiter, observed from outside: POSTs to the
/// login page stop being served after <c>AuthRateLimitPolicy.AuthPermitLimit</c>
/// in one window, while GETs (rendering the form) ride the generous general
/// tier. Gets its own factory — and so its own limiter window — because these
/// tests deliberately exhaust the budget every other class is careful to stay
/// under.
/// </summary>
public sealed class WebHostRateLimitIntegrationTests : IDisposable
{
    private readonly string _dataRoot = Directory.CreateTempSubdirectory("att-ratelimit-int-").FullName;
    private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<WebHostDemoMode> _factory;

    public WebHostRateLimitIntegrationTests()
    {
        _factory = WebHostIntegration.HostedFactory(_dataRoot);
    }

    public void Dispose()
    {
        _factory.Dispose();
        try { Directory.Delete(_dataRoot, recursive: true); } catch { }
    }

    [Fact]
    public async Task Eleventh_login_post_in_a_window_is_rejected_with_429()
    {
        using var client = WebHostIntegration.NewClient(_factory);

        // Bare POSTs (no antiforgery token) — they fail with 400, but every
        // one of them spends a credential-guessing permit, which is the point:
        // the limiter must shed load BEFORE any auth work happens.
        for (int attempt = 1; attempt <= AccessibleTrader.WebHost.Services.AuthRateLimitPolicy.AuthPermitLimit; attempt++)
        {
            var resp = await client.PostAsync("/terminal/account/login",
                WebHostIntegration.Form(("Input.Email", $"x{attempt}@example.test"), ("Input.Password", "nope")));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, resp.StatusCode);
        }

        var over = await client.PostAsync("/terminal/account/login",
            WebHostIntegration.Form(("Input.Email", "x@example.test"), ("Input.Password", "nope")));
        Assert.Equal(HttpStatusCode.TooManyRequests, over.StatusCode);

        // Register shares the same strict tier and the same per-IP partition
        // budget — a blocked brute-forcer can't just switch pages.
        var register = await client.PostAsync("/terminal/account/register",
            WebHostIntegration.Form(("Input.Email", "x@example.test"), ("Input.Password", "nope")));
        Assert.Equal(HttpStatusCode.TooManyRequests, register.StatusCode);
    }

    [Fact]
    public async Task Rendering_the_login_form_is_not_charged_to_the_auth_tier()
    {
        using var client = WebHostIntegration.NewClient(_factory);

        // Far more GETs than the auth tier would allow; all must serve. (A
        // screen-reader user re-rendering the form repeatedly must never be
        // locked out of merely SEEING it.)
        for (int i = 0; i < 15; i++)
        {
            var resp = await client.GetAsync("/terminal/account/login");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
    }
}
