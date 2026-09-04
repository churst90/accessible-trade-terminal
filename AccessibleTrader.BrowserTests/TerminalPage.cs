using System.Text.Json;
using Microsoft.Playwright;

namespace AccessibleTrader.BrowserTests;

/// <summary>One thing the app said, and whether it cut off whatever was already speaking.</summary>
internal sealed record Utterance(string Text, bool Interrupt);

/// <summary>What the browser says has keyboard focus right now.</summary>
internal sealed record ActiveElement(string Id, string Tag, string? Role, string? Label, string Text)
{
    public string Describe() =>
        $"<{Tag.ToLowerInvariant()}" +
        (Id.Length > 0 ? $" id='{Id}'" : " (no id)") +
        (Role is { Length: > 0 } ? $" role='{Role}'" : "") +
        (Label is { Length: > 0 } ? $" aria-label='{Label}'" : "") +
        ">" +
        (Text.Length > 0 ? $" “{Trim(Text)}”" : "");

    private static string Trim(string s) =>
        s.Length <= 60 ? s.Replace('\n', ' ') : s[..60].Replace('\n', ' ') + "…";
}

/// <summary>
/// The instrument. One page on the running terminal, with the handful of questions this audit
/// asks: what has focus, which dialog is open, what is that dialog called, and does every control
/// in it have a name a screen reader can read out.
///
/// <para>
/// Everything here goes through the real browser. That is the entire point of the harness: the
/// bug that motivated it (Alt+T opened the trading dashboard without moving focus) survived a
/// dedicated focus-contract suite, a modal catalog and 4,830 green tests because bUnit applies a
/// render synchronously and a browser does not.
/// </para>
/// </summary>
internal sealed class TerminalPage : IAsyncDisposable
{
    private readonly IBrowserContext _context;

    // Everything the browser complained about, recorded from construction so a navigation that
    // fails still has a record of WHY. These are the three channels a Blazor Server page dies
    // through — a missing asset (requestfailed), a script that threw (pageerror), and the
    // framework's own diagnostics (console). See AppNeverLoadedException.
    // Playwright raises these events on its own dispatch loop, so one lock guards all three —
    // the failure report reads them together and must see a consistent set.
    private readonly Lock _diagLock = new();
    private readonly List<string> _consoleErrors = new();
    private readonly List<string> _pageErrors = new();
    private readonly List<string> _failedRequests = new();

    /// <summary>Server-side log lines, supplied by the fixture so failures can report both ends.</summary>
    private readonly Func<IReadOnlyList<string>> _serverLog;

    public IPage Page { get; }

    public TerminalPage(IPage page, IBrowserContext context, Func<IReadOnlyList<string>>? serverLog = null)
    {
        Page = page;
        _context = context;
        _serverLog = serverLog ?? (static () => Array.Empty<string>());

        Page.Console += (_, msg) =>
        {
            if (msg.Type is "error" or "warning")
                lock (_diagLock) _consoleErrors.Add($"[{msg.Type}] {msg.Text}");
        };
        Page.PageError += (_, err) => { lock (_diagLock) _pageErrors.Add(err); };
        Page.RequestFailed += (_, req) =>
        {
            lock (_diagLock) _failedRequests.Add($"{req.Method} {req.Url} — {req.Failure}");
        };
        Page.Response += (_, res) =>
        {
            // A 404 is not a "failed request" to Playwright — it is a perfectly good response
            // carrying bad news. blazor.web.js going missing lands here, not above.
            if (res.Status >= 400)
                lock (_diagLock) _failedRequests.Add($"HTTP {res.Status} {res.Url}");
        };
    }

    /// <summary>
    /// Navigate and wait until a keystroke would actually reach the app.
    ///
    /// <para>
    /// Three separate waits, and the third is the one that matters. Server-rendered markup
    /// arrives first, the Blazor circuit connects second, and only then does
    /// <c>GlobalInputService.InitializeAsync</c> attach the window keydown listener. A key
    /// pressed before that is dropped on the floor with no error anywhere — which is
    /// indistinguishable from a shortcut that does not exist. Waiting on
    /// <c>accessibleTrader._inputReady</c> means every failure this harness reports is about the
    /// app's behaviour rather than about the harness being early.
    /// </para>
    ///
    /// <para>
    /// Each wait reports its own failure through <see cref="AppNeverLoadedException"/>, carrying
    /// both what the browser saw and what the server logged. The bare Playwright timeout this
    /// replaced said only "the locator never matched", which on CI was the entire evidence
    /// available for a suite in which all 45 tests failed identically.
    /// </para>
    /// </summary>
    public async Task GotoAppAsync(string rootUrl)
    {
        await InstallSpeechRecorderAsync();   // must precede navigation — see the method's remarks

        IResponse? response = null;
        try
        {
            response = await Page.GotoAsync(rootUrl,
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        }
        catch (Exception ex)
        {
            throw await DescribeFailureAsync("the navigation itself failed", rootUrl, null, ex);
        }

        try
        {
            // 60s, not 30s. Nothing here is server-rendered, so this single wait covers the whole
            // cold path: the framework script, the WebSocket handshake, the per-visitor DI scope,
            // and MainLayout's first render — which eagerly resolves some twenty services and
            // awaits IAppStartupService.InitializeAsync. On a two-core CI runner with a
            // just-started host that is a different order of magnitude from a warm developer box,
            // and 30s was chosen against the latter. The fail-fast latch in TerminalBrowserFixture
            // is what makes the longer bound affordable: the suite now spends this wait once
            // rather than once per test.
            await Page.Locator("#main-heading").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });
        }
        catch (Exception ex)
        {
            throw await DescribeFailureAsync(
                "#main-heading never appeared, so the Blazor circuit never rendered the app "
                + "(App.razor mounts it with prerender: false — nothing this harness looks for "
                + "exists in the server's first response)",
                rootUrl, response?.Status, ex);
        }

        try
        {
            await Page.WaitForFunctionAsync(
                "() => window.accessibleTrader && window.accessibleTrader._inputReady === true",
                null, new PageWaitForFunctionOptions { Timeout = 60_000 });
        }
        catch (Exception ex)
        {
            throw await DescribeFailureAsync(
                "the app rendered but the input pipeline never armed "
                + "(window.accessibleTrader._inputReady stayed false), so no keystroke would reach it",
                rootUrl, response?.Status, ex);
        }
    }

    /// <summary>
    /// Collects both ends of the failure. Reading the document is itself allowed to fail — if the
    /// page is gone there is nothing to read, and losing the rest of the report to that would
    /// defeat the purpose.
    /// </summary>
    private async Task<AppNeverLoadedException> DescribeFailureAsync(
        string stage, string rootUrl, int? httpStatus, Exception inner)
    {
        string? html = null;
        try { html = await Page.ContentAsync(); } catch { /* best effort */ }

        lock (_diagLock)
            return AppNeverLoadedException.Build(
                stage, rootUrl, httpStatus, html,
                _consoleErrors.ToList(), _pageErrors.ToList(), _failedRequests.ToList(),
                _serverLog(), inner);
    }

    /// <summary>
    /// Wraps <c>accessibleTrader.speak(text, interrupt)</c> so every announcement the app makes is
    /// recorded, with its interrupt flag, exactly at the boundary where it leaves .NET.
    ///
    /// <para>
    /// This is the seam A2/F2 said did not exist: the whole 4,830-test suite contains ONE
    /// assertion touching an <c>interrupt:</c> value and it is a grep over <c>.razor</c> source,
    /// so changing an order fill from interrupting to not interrupting broke nothing. For a
    /// screen-reader user that flag is the difference between hearing "Order filled, 0.5 BTC at
    /// 67,234" now and hearing it after the navigation chatter already queued has drained.
    /// </para>
    ///
    /// <para>
    /// It has to go in as an INIT script, and the reason cost an hour: <b>Blazor memoizes the
    /// function it resolves for a JS interop identifier.</b> The first
    /// <c>accessibleTrader.speak</c> call happens during circuit start-up (a <c>Silence()</c>
    /// with empty text), so a wrapper installed after the page settles is never called again —
    /// .NET keeps invoking the function object it cached. The harness recorded zero utterances
    /// while the server log showed the bridge dispatching every one of them, which reads exactly
    /// like "the browser voice is dead" and is not.
    /// </para>
    ///
    /// <para>
    /// Two layers of interception are needed because <c>keyboard.js</c> REPLACES
    /// <c>window.accessibleTrader</c> with a fresh object literal (so a property defined on the
    /// old object is thrown away), while <c>webSpeech.js</c> later assigns <c>speak</c> onto
    /// whatever object is there. So: hook the <c>window.accessibleTrader</c> assignment, and on
    /// each new object hook its <c>speak</c> assignment.
    /// </para>
    /// </summary>
    private async Task InstallSpeechRecorderAsync()
    {
        await Page.AddInitScriptAsync(@"
            (function () {
                window.__spoken = [];
                let obj;
                function hook(o) {
                    if (!o || o.__speechHooked) return o;
                    let real = o.speak;
                    Object.defineProperty(o, 'speak', {
                        configurable: true,
                        get: function () {
                            return function (text, interrupt) {
                                window.__spoken.push({
                                    text: String(text === undefined || text === null ? '' : text),
                                    interrupt: !!interrupt
                                });
                                if (typeof real === 'function') {
                                    try { return real.apply(this, arguments); } catch (e) { }
                                }
                            };
                        },
                        set: function (v) { real = v; }
                    });
                    Object.defineProperty(o, '__speechHooked', { value: true, enumerable: false });
                    return o;
                }
                Object.defineProperty(window, 'accessibleTrader', {
                    configurable: true,
                    get: function () { return obj; },
                    set: function (v) { obj = hook(v); }
                });
            })();");
    }

    /// <summary>Everything the app has spoken since the last <see cref="ClearSpokenAsync"/>.</summary>
    public async Task<IReadOnlyList<Utterance>> SpokenAsync()
    {
        var json = await Page.EvaluateAsync<string>("() => JSON.stringify(window.__spoken || [])");
        return JsonSerializer.Deserialize<List<Utterance>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    public Task ClearSpokenAsync() => Page.EvaluateAsync("() => { window.__spoken = []; }");

    /// <summary>Waits until something has been spoken, or gives up. Returns what is there either way.</summary>
    public async Task<IReadOnlyList<Utterance>> WaitForSpeechAsync(int timeoutMs = 5_000)
    {
        try
        {
            await Page.WaitForFunctionAsync("() => (window.__spoken || []).length > 0",
                null, new PageWaitForFunctionOptions { Timeout = timeoutMs });
        }
        catch (TimeoutException) { }
        catch (PlaywrightException) { }
        return await SpokenAsync();
    }

    // ── focus ────────────────────────────────────────────────────────────────

    /// <summary>The browser's own <c>document.activeElement</c>, not a record of a focus call.</summary>
    public async Task<ActiveElement> ActiveElementAsync()
    {
        var json = await Page.EvaluateAsync<string>(@"() => {
            const el = document.activeElement;
            if (!el) return JSON.stringify({ id: '', tag: 'NONE', role: null, label: null, text: '' });
            return JSON.stringify({
                id:    el.id || '',
                tag:   el.tagName || '',
                role:  el.getAttribute ? el.getAttribute('role') : null,
                label: el.getAttribute ? el.getAttribute('aria-label') : null,
                text:  (el.textContent || '').trim()
            });
        }");
        var d = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        return new ActiveElement(
            d["id"].GetString() ?? "",
            d["tag"].GetString() ?? "",
            d["role"].ValueKind == JsonValueKind.Null ? null : d["role"].GetString(),
            d["label"].ValueKind == JsonValueKind.Null ? null : d["label"].GetString(),
            d["text"].GetString() ?? "");
    }

    /// <summary>
    /// Poll <c>document.activeElement.id</c> until it is <paramref name="elementId"/>. Returns
    /// false on timeout rather than throwing, so the caller can report where focus actually went
    /// — "focus is on the heading, not the amount field" is the finding, and an exception here
    /// would throw that away.
    /// </summary>
    public async Task<bool> WaitForFocusAsync(string elementId, int timeoutMs = 5_000)
    {
        try
        {
            await Page.WaitForFunctionAsync(
                "id => document.activeElement && document.activeElement.id === id",
                elementId, new PageWaitForFunctionOptions { Timeout = timeoutMs });
            return true;
        }
        catch (TimeoutException) { return false; }
        catch (PlaywrightException) { return false; }
    }

    // ── dialogs ──────────────────────────────────────────────────────────────

    // Every dialog-discovery expression below selects the WHOLE ARIA dialog family and filters
    // on `getClientRects().length > 0`, never `offsetParent` — the same two choices keyboard.js's
    // Tab trap makes, for the same reason. The 2026-09-02 review found this harness green for a
    // dialog it could not see: Toolbar's destructive "strip your indicators" confirmation is an
    // alertdialog that is itself position:fixed, and CSSOM-View makes offsetParent null for such
    // an element. `[role="dialog"]` plus an offsetParent filter missed it twice over, and the two
    // containment predicates in ModalBrowserContractTests then returned TRUE when they found zero
    // dialogs — "inside" was also what "no dialog visible" looked like. One predicate now, whose
    // empty case is the failing case (FocusPlace.NoDialogSeen). ChromeAccessibilityScanTests reads
    // this project's sources for both regressions.

    /// <summary>
    /// Where focus is relative to the topmost visible dialog, judged the way the Tab trap judges
    /// it. <see cref="FocusPlace.NoDialogSeen"/> is deliberately its own answer, so that a test
    /// which opened a dialog can fail on "the trap cannot see it" instead of passing on it.
    /// </summary>
    public async Task<FocusPlace> FocusRelativeToTopDialogAsync()
    {
        int code = await Page.EvaluateAsync<int>(@"() => {
            const dialogs = Array.from(document.querySelectorAll('[role=""dialog""], [role=""alertdialog""]'))
                                 .filter(el => el.getClientRects().length > 0);
            if (dialogs.length === 0) return 0;
            const active = document.activeElement;
            const top = dialogs.find(d => active && d.contains(active)) || dialogs[dialogs.length - 1];
            return top.contains(active) ? 2 : 1;
        }");
        return code switch { 2 => FocusPlace.Inside, 1 => FocusPlace.Outside, _ => FocusPlace.NoDialogSeen };
    }

    /// <summary>
    /// The number of real tab stops in the topmost visible dialog — the vacuity floor for the two
    /// trap theories. Zero when no dialog is visible, which those theories already fail on.
    /// </summary>
    public Task<int> TabStopCountInTopDialogAsync() =>
        Page.EvaluateAsync<int>(@"() => {
            const dialogs = Array.from(document.querySelectorAll('[role=""dialog""], [role=""alertdialog""]'))
                                 .filter(el => el.getClientRects().length > 0);
            if (dialogs.length === 0) return 0;
            const d = dialogs[dialogs.length - 1];
            return Array.from(d.querySelectorAll(
                'button, a[href], input, select, textarea, summary, [tabindex]:not([tabindex=""-1""])'))
                .filter(el => el.getClientRects().length > 0 && !el.hasAttribute('disabled')
                           && el.getAttribute('tabindex') !== '-1').length;
        }");

    /// <summary>
    /// Where focus is relative to ONE named dialog — the one whose <c>aria-labelledby</c> is
    /// <paramref name="labelledBy"/> — rather than "the top one". With two dialogs open, which is
    /// on top is exactly the question under test, so a stacked-dialog test must name the dialog
    /// it means by the id it opened rather than let the harness pick. Same predicate shape as
    /// <see cref="FocusRelativeToTopDialogAsync"/>: whole ARIA dialog family, rendered means
    /// <c>getClientRects().length &gt; 0</c>, and the dialog-not-visible case is its own answer.
    /// </summary>
    public async Task<FocusPlace> FocusRelativeToDialogAsync(string labelledBy)
    {
        int code = await Page.EvaluateAsync<int>(@"(id) => {
            const d = Array.from(document.querySelectorAll('[role=""dialog""], [role=""alertdialog""]'))
                           .filter(el => el.getClientRects().length > 0)
                           .find(el => el.getAttribute('aria-labelledby') === id);
            if (!d) return 0;
            return d.contains(document.activeElement) ? 2 : 1;
        }", labelledBy);
        return code switch { 2 => FocusPlace.Inside, 1 => FocusPlace.Outside, _ => FocusPlace.NoDialogSeen };
    }

    /// <summary>Wait until the dialog labelled by <paramref name="labelledBy"/> is no longer rendered.</summary>
    public async Task<bool> WaitForDialogGoneAsync(string labelledBy, int timeoutMs = 10_000)
    {
        try
        {
            await Page.WaitForFunctionAsync(
                @"(id) => !Array.from(document.querySelectorAll('[role=""dialog""], [role=""alertdialog""]'))
                              .filter(el => el.getClientRects().length > 0)
                              .some(el => el.getAttribute('aria-labelledby') === id)",
                labelledBy, new PageWaitForFunctionOptions { Timeout = timeoutMs });
            return true;
        }
        catch (TimeoutException) { return false; }
        catch (PlaywrightException) { return false; }
    }

    /// <summary>Drop focus onto &lt;body&gt; the way a click on an inert part of an overlay does.</summary>
    public Task BlurActiveElementAsync() =>
        Page.EvaluateAsync("() => { const a = document.activeElement; if (a && a.blur) a.blur(); }");

    /// <summary>
    /// The app's own ordered modal stack as the Tab trap sees it — bottom first, top last. Empty
    /// when the app does not expose one, which is itself a finding.
    /// </summary>
    public async Task<IReadOnlyList<string>> ModalStackAsync()
    {
        var json = await Page.EvaluateAsync<string>(@"() => {
            const at = window.accessibleTrader;
            const stack = at && Array.isArray(at._modalStack) ? at._modalStack : [];
            return JSON.stringify(stack.map(e => (e && typeof e === 'object') ? String(e.name) : String(e)));
        }");
        return JsonSerializer.Deserialize<List<string>>(json)!;
    }

    /// <summary>Ids (or a positional fallback) of every VISIBLE dialog-family element on the page.</summary>
    public async Task<IReadOnlyList<string>> VisibleDialogIdsAsync()
    {
        var json = await Page.EvaluateAsync<string>(@"() => JSON.stringify(
            Array.from(document.querySelectorAll('[role=""dialog""], [role=""alertdialog""]'))
                 .filter(el => el.getClientRects().length > 0)
                 .map((el, i) => el.id || ('(unnamed dialog #' + i + ')')))");
        return JsonSerializer.Deserialize<List<string>>(json)!;
    }

    /// <summary>The topmost visible dialog — the one the Tab trap and Escape act on.</summary>
    public ILocator TopDialog() => Page.Locator("[role='dialog']:visible, [role='alertdialog']:visible").Last;

    public async Task<bool> WaitForDialogAsync(int timeoutMs = 10_000)
    {
        try
        {
            await Page.Locator("[role='dialog']:visible, [role='alertdialog']:visible").Last
                      .WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs });
            return true;
        }
        catch (TimeoutException) { return false; }
        catch (PlaywrightException) { return false; }
    }

    public async Task<bool> WaitForNoDialogAsync(int timeoutMs = 10_000)
    {
        try
        {
            await Page.WaitForFunctionAsync(
                @"() => Array.from(document.querySelectorAll('[role=""dialog""], [role=""alertdialog""]'))
                             .filter(el => el.getClientRects().length > 0).length === 0",
                null, new PageWaitForFunctionOptions { Timeout = timeoutMs });
            return true;
        }
        catch (TimeoutException) { return false; }
        catch (PlaywrightException) { return false; }
    }

    /// <summary>
    /// The accessible name of the topmost dialog, resolved the way a screen reader resolves it:
    /// <c>aria-labelledby</c> → the referenced element's text, else <c>aria-label</c>. Returns
    /// null when the dialog has neither, and the empty string when it points at something that
    /// does not exist or has no text — a distinction worth keeping, because a dangling
    /// <c>aria-labelledby</c> announces as an unnamed dialog and looks correct in the markup.
    /// </summary>
    public async Task<string?> TopDialogAccessibleNameAsync()
    {
        return await Page.EvaluateAsync<string?>(@"() => {
            const dialogs = Array.from(document.querySelectorAll('[role=""dialog""], [role=""alertdialog""]'))
                                 .filter(el => el.getClientRects().length > 0);
            if (dialogs.length === 0) return null;
            const d = dialogs[dialogs.length - 1];
            const by = d.getAttribute('aria-labelledby');
            if (by) {
                return by.split(/\s+/)
                         .map(id => { const t = document.getElementById(id); return t ? (t.textContent || '').trim() : ''; })
                         .join(' ')
                         .trim();
            }
            const label = d.getAttribute('aria-label');
            return label === null ? null : label.trim();
        }");
    }

    // ── keyboard ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Press a chord as a real user would, on the document body.
    ///
    /// <para>
    /// The app's keydown listener is on <c>window</c> in the capture phase, so where focus sits
    /// does not change whether the key is seen — but it does change what the key MEANS: bare
    /// letters are gated on <c>_chartFocused</c>, and text inputs keep their own keystrokes. Any
    /// test using a bare-letter shortcut has to put focus on the chart first, which is
    /// <see cref="FocusChartAsync"/>.
    /// </para>
    /// </summary>
    public Task PressAsync(string chord) => Page.Keyboard.PressAsync(chord);

    /// <summary>Ctrl+Alt+Shift+C — the app's own "put focus in the chart" command.</summary>
    public async Task FocusChartAsync()
    {
        await PressAsync("Control+Alt+Shift+KeyC");
        await Page.WaitForFunctionAsync(
            "() => window.accessibleTrader && window.accessibleTrader._chartFocused === true",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    /// <summary>The app's own count of open modals — what arms the Tab trap and Escape routing.</summary>
    public Task<int> OpenModalCountAsync() =>
        Page.EvaluateAsync<int>("() => (window.accessibleTrader && window.accessibleTrader._openModalCount) || 0");

    // ── seeded chart ─────────────────────────────────────────────────────────

    /// <summary>
    /// Drives the toolbar's Market → Provider → Symbol → Load cascade onto the offline dataset
    /// <see cref="TerminalServerFactory"/> seeded, and returns once the chart actually holds
    /// series. Every route in this suite reaches the app at cold start with an empty chart;
    /// this is the one call that changes that.
    ///
    /// <para>
    /// The completion signal is the Indicator bar's own <c>#indicator-select</c>, whose options
    /// ARE <c>Store.State.ActiveSeries</c>. It is deliberately not the Object Tree: the tree is
    /// the thing these tests are here to measure, and waiting on it would make every assertion
    /// about it circular. It is also not the status strip, which is a mirror of the last spoken
    /// sentence and says "loaded" for a load that produced nothing.
    /// </para>
    ///
    /// <para>
    /// Each <c>SelectOptionAsync</c> is followed by a wait on the NEXT dropdown's contents rather
    /// than a delay, because the cascade is asynchronous end to end — picking a market asks the
    /// data service for providers, picking a provider asks the provider for symbols — and a
    /// select whose options have not arrived yet accepts the value and silently keeps the old one.
    /// </para>
    /// </summary>
    public async Task LoadSeededChartAsync(int timeoutMs = 30_000)
    {
        await Page.SelectOptionAsync("#market-select", TerminalServerFactory.SeededMarket);
        await Page.WaitForFunctionAsync(
            "provider => [...document.querySelectorAll('#provider-select option')].some(o => o.value === provider)",
            TerminalServerFactory.SeededProvider,
            new PageWaitForFunctionOptions { Timeout = timeoutMs });

        await Page.SelectOptionAsync("#provider-select", TerminalServerFactory.SeededProvider);
        await Page.WaitForFunctionAsync(
            "symbol => [...document.querySelectorAll('#symbol-select option')].some(o => o.value === symbol)",
            TerminalServerFactory.SeededSymbol,
            new PageWaitForFunctionOptions { Timeout = timeoutMs });

        await Page.SelectOptionAsync("#symbol-select", TerminalServerFactory.SeededSymbol);

        // Wait for the ORCHESTRATOR to have adopted the symbol, not merely for the <select> to
        // show it. The Load button is gated on MarketOrchestrator.SelectedSymbol being non-empty,
        // and a gate is re-read at click time — so clicking one render too early does not load a
        // chart, it speaks "Choose a symbol first." and returns, leaving the wait below to time
        // out 30 seconds later with nothing to say about why. The select's `title` is bound to
        // the orchestrator's own SelectedSymbol, so it is that state made observable.
        await Page.WaitForFunctionAsync(
            "symbol => document.querySelector('#symbol-select')?.getAttribute('title') === symbol",
            TerminalServerFactory.SeededSymbol,
            new PageWaitForFunctionOptions { Timeout = timeoutMs });

        await Page.ClickAsync("#toolbar-load-btn");

        try
        {
            await Page.WaitForFunctionAsync(
                "() => document.querySelectorAll('#indicator-select option').length > 0",
                null, new PageWaitForFunctionOptions { Timeout = timeoutMs });
        }
        catch (TimeoutException)
        {
            // A bare "Timeout 30000ms exceeded" names nothing, and neither does the page's
            // generic error banner — #blazor-error-ui is in the DOM of every Blazor page and
            // hidden by CSS, so reading its text without checking `display` reports a crash on a
            // perfectly healthy circuit (it did, on 2026-09-04, and cost an hour). Report the
            // page's actual state: whether that banner is SHOWN, what the series picker holds,
            // what the toolbar's own [role=alert] says, and what the terminal last spoke.
            string state = await Page.EvaluateAsync<string>(@"() => {
                const e = document.querySelector('#blazor-error-ui');
                const shown = e && getComputedStyle(e).display !== 'none';
                const sel = document.querySelector('#indicator-select');
                const load = document.querySelector('[role=alert]:not(#blazor-error-ui)');
                const tabs = [...document.querySelectorAll('[role=tab]')].map(t => t.getAttribute('aria-label') || t.textContent.trim());
                return `blazor-error-ui shown=${!!shown}; indicator-select present=${!!sel}` +
                       ` options=${sel ? sel.options.length : 'n/a'}` +
                       `; toolbar alert=${load ? load.textContent.trim() : '(none)'}` +
                       `; title=${document.title}` +
                       `; tabs=${tabs.length}[${tabs.join(' / ')}]`;
            }");
            var spoken = await SpokenAsync();
            var errors = _serverLog()
                .Where(l => l.StartsWith("[Error]", StringComparison.Ordinal)
                         || l.StartsWith("[Critical]", StringComparison.Ordinal))
                .TakeLast(6);
            throw new InvalidOperationException(
                "The seeded chart never produced a series.\n  Page: "
                + (state.Length > 0 ? state.ReplaceLineEndings(" ") : "(unreadable)")
                + "\n  Last spoken: "
                + (spoken.Count > 0 ? string.Join(" | ", spoken.TakeLast(8).Select(u => u.Text)) : "(nothing)")
                + "\n  Server errors:\n    " + string.Join("\n    ", errors.DefaultIfEmpty("(none)"))
                + "\n  Browser errors:\n    " + string.Join("\n    ", BrowserDiagnostics().DefaultIfEmpty("(none)")));
        }
    }

    /// <summary>
    /// Everything the browser complained about: a script that threw, a console error, a request
    /// that never arrived. A Blazor circuit that dies takes the DOM with it and leaves the page
    /// showing "An unhandled error has occurred" — and that banner names nothing, so a harness
    /// that reports only the banner reports nothing.
    /// </summary>
    public IReadOnlyList<string> BrowserDiagnostics()
    {
        lock (_diagLock)
            return _pageErrors.Select(e => "pageerror: " + e)
                .Concat(_consoleErrors.Select(e => "console: " + e))
                .Concat(_failedRequests.Select(e => "request: " + e))
                .TakeLast(8).ToList();
    }

    /// <summary>The friendly names in the Indicator bar's series picker — i.e. the chart's series.</summary>
    public async Task<IReadOnlyList<string>> ActiveSeriesNamesAsync() =>
        await Page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('#indicator-select option')].map(o => o.textContent.trim())");

    // ── names ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every focusable control inside the topmost dialog, with the accessible name Chromium
    /// computes for it — not a re-implementation of the accname algorithm, which would just be
    /// this repo's standing "a test that mirrors the logic it guards" mistake in a new place.
    /// Playwright's aria snapshot is the browser's own accessibility tree.
    /// </summary>
    public async Task<string> TopDialogAriaSnapshotAsync() => await TopDialog().AriaSnapshotAsync();

    /// <summary>
    /// Controls in the topmost dialog that a screen reader would announce with no name at all.
    /// Each entry is a short description of the offending element, for the failure message.
    /// </summary>
    public Task<IReadOnlyList<string>> UnnamedControlsInTopDialogAsync() => UnnamedControlsAsync(inDialog: true);

    /// <summary>Same sweep over the whole page — the toolbar, tab bar, chart and status bar.</summary>
    public Task<IReadOnlyList<string>> UnnamedControlsOnPageAsync() => UnnamedControlsAsync(inDialog: false);

    /// <summary>
    /// How many controls the unnamed-control sweep actually looked at in the topmost dialog.
    ///
    /// <para>
    /// The vacuity floor for <c>AccessibleNameSweepTests</c>, which no longer carries an
    /// exemption list: "no unnamed controls" and "no controls" are the same empty list, and only
    /// this number tells them apart. It deliberately repeats the selector and the visibility
    /// filters — not the naming logic, which is the part that would be a test mirroring the
    /// thing it guards.
    /// </para>
    /// </summary>
    public Task<int> ControlCountInTopDialogAsync() =>
        Page.EvaluateAsync<int>(@"() => {
            const dialogs = Array.from(document.querySelectorAll('[role=""dialog""], [role=""alertdialog""]'))
                                 .filter(el => el.getClientRects().length > 0);
            if (dialogs.length === 0) return 0;
            const d = dialogs[dialogs.length - 1];
            const sel = 'button, a[href], input, select, textarea, summary, ' +
                        '[tabindex]:not([tabindex=""-1""]), [role=""button""], [role=""tab""], ' +
                        '[role=""checkbox""], [role=""switch""], [role=""radio""], [role=""combobox""]';
            let n = 0;
            for (const el of d.querySelectorAll(sel)) {
                if (el.offsetParent === null) continue;
                if (el.hasAttribute('disabled')) continue;
                if (el.closest('[aria-hidden=""true""]')) continue;
                if (el.tagName === 'INPUT' && (el.getAttribute('type') || '').toLowerCase() === 'hidden') continue;
                n++;
            }
            return n;
        }");

    private async Task<IReadOnlyList<string>> UnnamedControlsAsync(bool inDialog)
    {
        var json = await Page.EvaluateAsync<string>(@"(inDialog) => {
            let d = document.body;
            if (inDialog) {
                const dialogs = Array.from(document.querySelectorAll('[role=""dialog""], [role=""alertdialog""]'))
                                     .filter(el => el.getClientRects().length > 0);
                if (dialogs.length === 0) return '[]';
                d = dialogs[dialogs.length - 1];
            }

            // `a[href]`, not `[href]`: the toolbar's icons are inline <svg><use href='#icon-x'>,
            // and a bare [href] selector matched all 29 of them as nameless controls in the first
            // survey. They are inside aria-hidden spans and are not focusable — a sweep that
            // reports them is a sweep nobody will read twice.
            const sel = 'button, a[href], input, select, textarea, summary, ' +
                        '[tabindex]:not([tabindex=""-1""]), [role=""button""], [role=""tab""], ' +
                        '[role=""checkbox""], [role=""switch""], [role=""radio""], [role=""combobox""]';

            const textOf = el => (el.textContent || '').replace(/\s+/g, ' ').trim();

            const nameOf = el => {
                const by = el.getAttribute('aria-labelledby');
                if (by) {
                    const t = by.split(/\s+/)
                                .map(id => { const r = document.getElementById(id); return r ? textOf(r) : ''; })
                                .join(' ').trim();
                    if (t) return t;
                }
                const al = el.getAttribute('aria-label');
                if (al && al.trim()) return al.trim();

                if (el.id) {
                    const lab = document.querySelector('label[for=""' + CSS.escape(el.id) + '""]');
                    if (lab && textOf(lab)) return textOf(lab);
                }
                const wrapping = el.closest('label');
                if (wrapping && textOf(wrapping)) return textOf(wrapping);

                const type = (el.getAttribute('type') || '').toLowerCase();
                const isPushButton = el.tagName === 'INPUT'
                                  && (type === 'submit' || type === 'button' || type === 'reset');
                // A <select>'s option text and a <textarea>'s content are NOT its name. Only roles
                // that support name-from-content (button, link, tab, heading…) get the textContent
                // fallback; textbox / combobox / listbox never do. Without this carve-out the
                // sweep reads a select's option list as its label and passes every unlabelled
                // dropdown in the app — which it did, on the first run.
                const isFormField = (el.tagName === 'SELECT' || el.tagName === 'TEXTAREA'
                                  || (el.tagName === 'INPUT' && !isPushButton));

                if (!isFormField) {
                    const t = textOf(el);
                    if (t) return t;
                }
                if (isPushButton && (el.value || '').trim()) return el.value.trim();

                const title = el.getAttribute('title');
                if (title && title.trim()) return title.trim();

                // Placeholder is the last resort in the accname spec and a poor name — it
                // disappears the moment the field has content — but it IS announced, so a field
                // that has one is not silent. Reported separately by placeholderOnly below.
                if (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA') {
                    const ph = el.getAttribute('placeholder');
                    if (ph && ph.trim()) return ph.trim();
                }
                return '';
            };

            /// True when the ONLY thing naming this control is its placeholder.
            const placeholderOnly = el => {
                if (el.tagName !== 'INPUT' && el.tagName !== 'TEXTAREA') return false;
                const ph = (el.getAttribute('placeholder') || '').trim();
                if (!ph) return false;
                return nameOf(el) === ph;
            };

            const describe = el => {
                const bits = [el.tagName.toLowerCase()];
                if (el.id) bits.push('#' + el.id);
                const role = el.getAttribute('role');
                if (role) bits.push('role=' + role);
                const cls = (el.getAttribute('class') || '').split(/\s+/).filter(Boolean)[0];
                if (cls) bits.push('.' + cls);
                const type = el.getAttribute('type');
                if (type) bits.push('type=' + type);
                // The visible text immediately before the control. Almost every unlabelled field
                // in this app has one — that is the whole shape of the defect, a <label> with no
                // `for` sitting next to the input — and without it the report reads
                // ""six inputs of type number"" and nobody can find them.
                let prev = el.previousElementSibling;
                while (prev && !textOf(prev)) prev = prev.previousElementSibling;
                if (prev) bits.push('after “' + textOf(prev).slice(0, 30) + '”');
                return bits.join(' ');
            };

            const out = [];
            for (const el of d.querySelectorAll(sel)) {
                if (el.offsetParent === null) continue;               // not rendered
                if (el.hasAttribute('disabled')) continue;            // announces as unavailable
                // closest(), not the attribute on the element itself: aria-hidden is inherited by
                // the whole subtree, and this app puts it on the wrapper span around each glyph.
                if (el.closest('[aria-hidden=""true""]')) continue;
                if (el.tagName === 'INPUT' && (el.getAttribute('type') || '').toLowerCase() === 'hidden') continue;
                if (!nameOf(el))          out.push(describe(el));
                else if (placeholderOnly(el)) out.push('[placeholder-only] ' + describe(el));
            }
            return JSON.stringify(out);
        }", inDialog);
        return JsonSerializer.Deserialize<List<string>>(json)!;
    }

    /// <summary>
    /// Tabs in the topmost dialog, by accessible name. Most of this app's dialogs are tabbed, and
    /// a sweep of only the tab that happens to be open on first render misses most of the
    /// controls in the application — which is how 181 of 193 literal <c>aria-label</c> values
    /// ended up unpinned by any test (A2/F9).
    /// </summary>
    public async Task<IReadOnlyList<string>> TopDialogTabNamesAsync()
    {
        var json = await Page.EvaluateAsync<string>(@"() => {
            const dialogs = Array.from(document.querySelectorAll('[role=""dialog""], [role=""alertdialog""]'))
                                 .filter(el => el.getClientRects().length > 0);
            if (dialogs.length === 0) return '[]';
            const d = dialogs[dialogs.length - 1];
            return JSON.stringify(Array.from(d.querySelectorAll('[role=""tab""]'))
                .filter(el => el.offsetParent !== null)
                .map(el => (el.getAttribute('aria-label') || el.textContent || '').replace(/\s+/g, ' ').trim()));
        }");
        return JsonSerializer.Deserialize<List<string>>(json)!;
    }

    /// <summary>Click a tab in the topmost dialog by its accessible name.</summary>
    public async Task<bool> ClickTopDialogTabAsync(string tabName)
    {
        var tab = TopDialog().GetByRole(AriaRole.Tab, new() { Name = tabName, Exact = true });
        if (await tab.CountAsync() == 0) return false;
        await tab.First.ClickAsync();
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        try { await _context.CloseAsync(); } catch { /* the browser may already be gone */ }
    }
}

/// <summary>Where focus is relative to the topmost visible dialog. See <see cref="TerminalPage.FocusRelativeToTopDialogAsync"/>.</summary>
public enum FocusPlace
{
    /// <summary>The dialog-family predicate found nothing rendered. For a test that opened a dialog, this is a failure, not "inside".</summary>
    NoDialogSeen,
    Outside,
    Inside,
}
