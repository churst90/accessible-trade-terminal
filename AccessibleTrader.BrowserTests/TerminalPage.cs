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

    public IPage Page { get; }

    public TerminalPage(IPage page, IBrowserContext context)
    {
        Page = page;
        _context = context;
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
    /// </summary>
    public async Task GotoAppAsync(string rootUrl)
    {
        await InstallSpeechRecorderAsync();   // must precede navigation — see the method's remarks
        await Page.GotoAsync(rootUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Page.Locator("#main-heading").WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        await Page.WaitForFunctionAsync(
            "() => window.accessibleTrader && window.accessibleTrader._inputReady === true",
            null, new PageWaitForFunctionOptions { Timeout = 60_000 });
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

    /// <summary>Ids (or a positional fallback) of every VISIBLE role="dialog" on the page.</summary>
    public async Task<IReadOnlyList<string>> VisibleDialogIdsAsync()
    {
        var json = await Page.EvaluateAsync<string>(@"() => JSON.stringify(
            Array.from(document.querySelectorAll('[role=""dialog""]'))
                 .filter(el => el.offsetParent !== null)
                 .map((el, i) => el.id || ('(unnamed dialog #' + i + ')')))");
        return JsonSerializer.Deserialize<List<string>>(json)!;
    }

    /// <summary>The topmost visible dialog — the one the Tab trap and Escape act on.</summary>
    public ILocator TopDialog() => Page.Locator("[role='dialog']:visible").Last;

    public async Task<bool> WaitForDialogAsync(int timeoutMs = 10_000)
    {
        try
        {
            await Page.Locator("[role='dialog']:visible").Last
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
                @"() => Array.from(document.querySelectorAll('[role=""dialog""]'))
                             .filter(el => el.offsetParent !== null).length === 0",
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
            const dialogs = Array.from(document.querySelectorAll('[role=""dialog""]'))
                                 .filter(el => el.offsetParent !== null);
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

    private async Task<IReadOnlyList<string>> UnnamedControlsAsync(bool inDialog)
    {
        var json = await Page.EvaluateAsync<string>(@"(inDialog) => {
            let d = document.body;
            if (inDialog) {
                const dialogs = Array.from(document.querySelectorAll('[role=""dialog""]'))
                                     .filter(el => el.offsetParent !== null);
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
            const dialogs = Array.from(document.querySelectorAll('[role=""dialog""]'))
                                 .filter(el => el.offsetParent !== null);
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
