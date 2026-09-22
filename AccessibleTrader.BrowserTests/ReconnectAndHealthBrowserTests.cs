using Microsoft.Playwright;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// <b>What the terminal says when the circuit drops, and whether anything can ask if it is up.</b>
///
/// <para>
/// Both asked for by the server on 2026-09-21 after a log review. Between 7 and 20 September,
/// <b>100 of 141</b> requests to <c>/terminal/_blazor/negotiate</c> returned 502 — bursts of
/// ten-plus within the same second, from one browser sitting on an open page while the app
/// restarted. From the user's side that is silence: no speech, no message, nothing to act on.
/// </para>
///
/// <para>
/// The silence was literal. There was no <c>#components-reconnect-modal</c> anywhere in the
/// repository, so Blazor's default overlay applied — a plain div with no live region and no focus
/// management — while <c>#blazor-error-ui</c> a few lines above it in the same file has carried
/// <c>role="alert" aria-live="assertive"</c> all along. The accessible treatment was deliberate
/// for unhandled errors and simply never extended to the case that actually happens.
/// </para>
/// </summary>
[Collection("Terminal browser")]
public sealed class ReconnectAndHealthBrowserTests
{
    private readonly TerminalBrowserFixture _fixture;
    public ReconnectAndHealthBrowserTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    // ── The reconnect overlay ───────────────────────────────────────────────────

    [BrowserFact]
    public async Task TheReconnectOverlayExistsAndIsOurs_NotTheFrameworkDefault()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.LoadSeededChartAsync();

        var present = await t.Page.EvaluateAsync<bool>(
            "() => !!document.getElementById('components-reconnect-modal')");

        Assert.True(present,
            "there is no #components-reconnect-modal, so Blazor's default overlay applies — a "
          + "plain div with no live region, which tells a screen reader user nothing when the "
          + "circuit drops");
    }

    /// <summary>
    /// A dedicated status node, because a live region that is merely UNHIDDEN does not reliably
    /// re-announce — and the transitions that matter here (attempt 1 → 2 → 3 → failed) carry no
    /// text change of their own at all.
    /// </summary>
    [BrowserFact]
    public async Task AnAssertiveStatusNodeCarriesTheAnnouncement()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.LoadSeededChartAsync();

        var shape = await t.Page.EvaluateAsync<string>(
            @"() => {
                const n = document.getElementById('reconnect-status');
                if (!n) return 'missing';
                return [n.getAttribute('role'), n.getAttribute('aria-live'), n.getAttribute('aria-atomic')].join('|');
              }");

        Assert.Equal("status|assertive|true", shape);
    }

    /// <summary>
    /// <b>The behaviour, not the markup.</b> Driving the framework's own state classes and
    /// asserting the status text is rewritten each time — which is the whole reason this is not
    /// just an <c>aria-live</c> attribute on the overlay.
    /// </summary>
    [BrowserFact]
    public async Task EveryStateTransitionRewritesTheStatusText()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.LoadSeededChartAsync();

        async Task<string> Transition(string cls)
        {
            await t.Page.EvaluateAsync(
                @"cls => {
                    const m = document.getElementById('components-reconnect-modal');
                    m.className = cls;
                  }", cls);
            await t.Page.WaitForTimeoutAsync(120);
            return await t.Page.EvaluateAsync<string>(
                "() => document.getElementById('reconnect-status').textContent.trim()");
        }

        var reconnecting = await Transition("components-reconnect-show");
        Assert.Contains("Reconnecting", reconnecting, StringComparison.OrdinalIgnoreCase);
        // The question a trader is actually asking when the screen goes quiet.
        Assert.Contains("orders already placed are unaffected", reconnecting, StringComparison.OrdinalIgnoreCase);

        var failed = await Transition("components-reconnect-failed");
        Assert.Contains("restarting", failed, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(reconnecting, failed);

        var rejected = await Transition("components-reconnect-rejected");
        Assert.Contains("session ended", rejected, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(failed, rejected);

        var back = await Transition("components-reconnect-hide");
        Assert.Contains("Reconnected", back, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The overlay must be invisible until the framework shows it. A permanently-visible banner
    /// saying the connection was lost would be worse than none.
    /// </summary>
    [BrowserFact]
    public async Task TheOverlayIsHiddenWhileTheCircuitIsHealthy()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.LoadSeededChartAsync();

        var display = await t.Page.EvaluateAsync<string>(
            "() => getComputedStyle(document.getElementById('components-reconnect-modal')).display");

        Assert.Equal("none", display);
    }

    /// <summary>
    /// The boot path is left at its default ON PURPOSE. Configuring the reconnection back-off
    /// needs <c>autostart="false"</c> plus an explicit <c>Blazor.start()</c>, and that was tried
    /// on 2026-09-21: it stopped the circuit booting at all and every browser test went red with
    /// "the terminal never loaded". The cause was not established, and an unverified change to
    /// the boot path has no business shipping in the same release as the fix for a page that
    /// would not load. This pins the decision so the next person reads the reason rather than
    /// rediscovering the failure.
    /// </summary>
    [BrowserFact]
    public async Task TheBootPathIsUntouched_AndTheCircuitActuallyStarts()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.LoadSeededChartAsync();

        var html = await t.Page.ContentAsync();
        Assert.DoesNotContain("autostart=\"false\"", html, StringComparison.OrdinalIgnoreCase);

        var started = await t.Page.EvaluateAsync<bool>("() => typeof Blazor !== 'undefined'");
        Assert.True(started, "Blazor did not load at all");
    }

    // ── Liveness ────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Cheap, unauthenticated, and honest.</b> There was no health endpoint anywhere in the
    /// solution, so the systemd unit had nothing to gate readiness on and the restart window —
    /// the 502 window — could not be closed.
    /// </summary>
    [BrowserFact]
    public async Task HealthzAnswersOkWithoutAuthentication()
    {
        await using var t = await _fixture.NewPageAsync();

        var result = await t.Page.EvaluateAsync<string>(
            @"async () => {
                const r = await fetch('/healthz', { redirect: 'manual' });
                return r.status + '|' + (await r.text()).trim() + '|' + (r.headers.get('cache-control') || '');
              }");

        var parts = result.Split('|');
        Assert.Equal("200", parts[0]);
        Assert.Equal("ok", parts[1]);
        Assert.Contains("no-store", parts[2], StringComparison.OrdinalIgnoreCase);
    }
}
