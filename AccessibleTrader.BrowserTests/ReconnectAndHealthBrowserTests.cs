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
    /// Two dedicated nodes, because a live region that is merely UNHIDDEN does not reliably
    /// re-announce. The state sentence is assertive, with no role (<c>role="status"</c> implies
    /// polite and contradicts it); the "still reconnecting" progress is polite.
    /// </summary>
    [BrowserFact]
    public async Task AnAssertiveNodeCarriesTheSentence_AndAPoliteOneTheProgress()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.LoadSeededChartAsync();

        var shape = await t.Page.EvaluateAsync<string>(
            @"() => ['reconnect-status', 'reconnect-progress'].map(id => {
                const n = document.getElementById(id);
                if (!n) return 'missing';
                return [n.getAttribute('role') || '', n.getAttribute('aria-live'), n.getAttribute('aria-atomic')].join('|');
              }).join(' ; ')");

        Assert.Equal("|assertive|true ; status|polite|true", shape);
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
    /// <b>The circuit is started by <c>js/boot.js</c>, and the document carries no inline
    /// script.</b> The back-off needs <c>autostart="false"</c> plus an explicit
    /// <c>Blazor.start()</c>. The first attempt (2026-09-21) made that call inline, the CSP
    /// (<c>script-src 'self'</c>) refused it, and the circuit never booted. Measured
    /// 2026-09-24: the browser console reads "Refused to execute inline script because it
    /// violates the following Content Security Policy directive". Any inline script is refused
    /// the same way, so the property pinned is "none at all", not "not this one".
    /// </summary>
    [BrowserFact]
    public async Task TheCircuitIsStartedByBootJs_AndNoScriptIsInline()
    {
        await using var t = await _fixture.NewPageAsync();

        var started = await t.Page.EvaluateAsync<bool>(
            "() => !!(window.terminalBoot && window.terminalBoot.started)");
        Assert.True(started,
            "the page loaded but js/boot.js did not start the circuit, so the framework's "
          + "default retry policy (ten attempts with no delay) is back");

        var inline = await t.Page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('script:not([src])')].map(s => s.textContent.trim().slice(0, 80))");
        Assert.True(inline.Length == 0,
            "inline <script> in the document; the CSP (script-src 'self') refuses it: "
          + string.Join(" | ", inline));
    }

    /// <summary>
    /// <b>The behaviour, measured.</b> Drops the circuit's WebSocket, refuses every reconnect
    /// (the negotiate request is what a restarting server fails), and counts the attempts in
    /// the first 2.5 seconds. The framework default makes ten in the first instant. The
    /// back-off makes two or three (0 s, then ~1 s, then ~2 s after that). Then lets the
    /// server answer and waits for the overlay to say the terminal is back, which is also the
    /// first test in which the FRAMEWORK, not the test, drives the overlay's classes.
    /// </summary>
    [BrowserFact]
    public async Task ADroppedCircuitRetriesWithBackOff_NotABurst()
    {
        await using var t = await _fixture.NewPageAsync();

        // Capture the circuit's socket. The init script runs on the reload below, before
        // blazor.web.js, so the SignalR transport constructs this subclass.
        await t.Page.AddInitScriptAsync(@"
            (() => {
                const Native = window.WebSocket;
                window.__sockets = [];
                window.WebSocket = class extends Native {
                    constructor(...a) { super(...a); window.__sockets.push(this); }
                };
            })();");
        await t.GotoAppAsync(_fixture.RootUrl);

        var sockets = await t.Page.EvaluateAsync<int>("() => window.__sockets.length");
        Assert.True(sockets > 0, "no WebSocket was captured, so this test cannot drop the circuit");

        var attempts = new List<DateTime>();
        var gate = new object();
        await t.Page.RouteAsync("**/_blazor/negotiate**", async route =>
        {
            lock (gate) attempts.Add(DateTime.UtcNow);
            await route.AbortAsync();
        });

        // Count what the assertive node is given. The framework rewrites the attempt counter
        // every second while it waits; until 2026-09-24 each rewrite re-announced the whole
        // sentence.
        await t.Page.EvaluateAsync(@"() => {
            window.__statusWrites = [];
            const n = document.getElementById('reconnect-status');
            new MutationObserver(() => { if (n.textContent) window.__statusWrites.push(n.textContent); })
                .observe(n, { childList: true, characterData: true, subtree: true });
          }");

        var dropped = DateTime.UtcNow;
        await t.Page.EvaluateAsync("() => window.__sockets.forEach(s => s.close())");
        await t.Page.WaitForTimeoutAsync(2_500);

        int inWindow;
        lock (gate) inWindow = attempts.Count(a => a - dropped < TimeSpan.FromMilliseconds(2_500));

        Assert.True(inWindow >= 1,
            "no reconnect attempt at all after the socket closed, so this measured nothing");
        Assert.True(inWindow <= 3,
            $"{inWindow} reconnect attempts in the first 2.5 s after a drop. The framework "
          + "default is ten with no delay, which is the burst in the server's logs");

        var status = await t.Page.EvaluateAsync<string>(
            "() => document.getElementById('reconnect-status').textContent");
        Assert.Contains("Reconnecting", status, StringComparison.OrdinalIgnoreCase);

        var writes = await t.Page.EvaluateAsync<string[]>("() => window.__statusWrites");
        Assert.True(writes.Length == 1,
            $"the reconnect sentence was written {writes.Length} times in 2.5 s of retrying; "
          + "each write is an assertive announcement that cuts off the last: "
          + string.Join(" | ", writes.Select(w => w.Length > 40 ? w[..40] + "…" : w)));

        await t.Page.UnrouteAsync("**/_blazor/negotiate**");
        await t.Page.WaitForFunctionAsync(
            "() => /Reconnected/.test(document.getElementById('reconnect-status').textContent)",
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });
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
