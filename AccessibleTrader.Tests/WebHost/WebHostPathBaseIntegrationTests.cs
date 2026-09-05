using System.Net;
using System.Net.Http.Json;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// Both public heads run under a path prefix and nothing else: <c>--demo</c> under <c>/app/</c>,
/// <c>--accounts</c> under <c>/terminal/</c>, behind nginx. The prefix-only bug class is real —
/// <c>a535c744</c>: routing ran before <c>UsePathBase</c>, so every login POST under a prefix
/// answered 405 off the GET-only static fallback — and until 2026-09-05 the DEMO head had no
/// automated coverage of it at all, because <c>--demo</c> was read from argv alone and a
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{T}"/> cannot pass argv
/// (hosted notes §3). These are the server's post-deploy curl checks, as tests.
/// </summary>
[Collection("ProviderCredentialBridge")]
public sealed class WebHostPathBaseIntegrationTests : IDisposable
{
    private readonly PluginBridgeScope _bridges = new();
    public void Dispose() => _bridges.Dispose();

    private static async Task<string> BaseHrefOf(HttpResponseMessage resp)
    {
        string html = await resp.Content.ReadAsStringAsync();
        var m = System.Text.RegularExpressions.Regex.Match(html, "<base href=\"([^\"]*)\"");
        Assert.True(m.Success, "the app shell carries no <base href>");
        return m.Groups[1].Value;
    }

    [Fact]
    public async Task The_demo_head_serves_the_app_shell_under_slash_app()
    {
        using var factory = WebHostIntegration.DemoFactory();
        using var client = WebHostIntegration.NewClient(factory);

        var resp = await client.GetAsync("/app/");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("/app/", await BaseHrefOf(resp));
    }

    [Fact]
    public async Task The_demo_head_negotiates_a_circuit_under_its_prefix()
    {
        // The one POST a visitor's browser always makes. A sticky 405 from a pre-PathBase
        // routing pass lands here first.
        using var factory = WebHostIntegration.DemoFactory();
        using var client = WebHostIntegration.NewClient(factory);

        var resp = await client.PostAsync("/app/_blazor/negotiate?negotiateVersion=1", new StringContent(""));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(body.TryGetProperty("connectionToken", out _), "negotiate answered without a connection token");
    }

    // NOT here: "a POST to /app/account/login is never 405". There is no login page on the demo
    // head, and with the GET/HEAD-only static fallback mapped, 405 IS the framework's answer for
    // a POST that matches nothing — on any prefix, with routing in the right place. The a535c744
    // regression was a 405 on a page that EXISTS; on the demo head the POST that exists is the
    // SignalR negotiate above, and that is the probe.

    [Fact]
    public async Task The_hosted_head_answers_a_prefixed_login_post_with_400_not_405()
    {
        // The server's own post-deploy check, verbatim: a login POST with no antiforgery token
        // is a 400. A 405 means the UsePathBase regression is back.
        string dataRoot = TestTemp.NewDir("att-pathbase-");
        try
        {
            using var factory = WebHostIntegration.HostedFactory(dataRoot);
            using var client = WebHostIntegration.NewClient(factory);

            var resp = await client.PostAsync("/terminal/account/login", WebHostIntegration.Form(
                ("Input.Email", "x@y.z"), ("Input.Password", "nope")));

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally { try { Directory.Delete(dataRoot, true); } catch { } }
    }

    [Fact]
    public async Task The_hosted_head_refuses_a_circuit_before_login_without_405()
    {
        string dataRoot = TestTemp.NewDir("att-pathbase-");
        try
        {
            using var factory = WebHostIntegration.HostedFactory(dataRoot);
            using var client = WebHostIntegration.NewClient(factory);

            var resp = await client.PostAsync("/terminal/_blazor/negotiate?negotiateVersion=1", new StringContent(""));

            Assert.NotEqual(HttpStatusCode.MethodNotAllowed, resp.StatusCode);
            Assert.NotEqual(HttpStatusCode.OK, resp.StatusCode);   // the circuit is behind login
        }
        finally { try { Directory.Delete(dataRoot, true); } catch { } }
    }

    [Fact]
    public async Task A_configured_PathBase_moves_the_full_mode_terminal_under_it()
    {
        // The override the browser harness runs on: Full mode, the honest surface for the
        // keyboard and modal sweeps, served under the hosted terminal's prefix.
        using var factory = WebHostIntegration.FullFactoryUnder("/terminal");
        using var client = WebHostIntegration.NewClient(factory);

        var resp = await client.GetAsync("/terminal/");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("/terminal/", await BaseHrefOf(resp));

        var negotiate = await client.PostAsync("/terminal/_blazor/negotiate?negotiateVersion=1", new StringContent(""));
        Assert.Equal(HttpStatusCode.OK, negotiate.StatusCode);
    }

    [Fact]
    public async Task The_base_href_follows_the_request_not_the_mode()
    {
        // Full mode, no prefix: root. The same component that says "/terminal/" above says "/"
        // here, because it reads Request.PathBase rather than re-deriving the mode's default.
        using var factory = WebHostIntegration.FullFactory();
        using var client = WebHostIntegration.NewClient(factory);

        var resp = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("/", await BaseHrefOf(resp));
    }
}
