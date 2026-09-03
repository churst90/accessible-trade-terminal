using System.Text.Json;
using Microsoft.Playwright;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// Does focus STAY on the modal heading after it lands there?
///
/// <para>
/// The report that motivated this probe (2026-09-03, Orca + a real browser): "When I press Alt+T,
/// focus is placed on the order type dropdown, not on the heading… some, like F12, dropped me on
/// the Close button at the bottom. Others, like the help screen, seemed to work fine."
/// </para>
///
/// <para>
/// Every existing browser assertion about modal focus goes through
/// <see cref="TerminalPage.WaitForFocusAsync"/>, which POLLS UNTIL focus is the target and returns
/// the instant it lands. It is structurally incapable of seeing focus that lands correctly and is
/// then moved by a later async render — and <c>TradingDashboardModal.ShowAsync</c> focuses
/// <c>trade-title</c> and only THEN awaits the account load and the order-book refresh, and then
/// arms a 2000&#160;ms timer that re-renders the dialog for as long as it is open. So this probe
/// keeps looking: it records where focus is at +250, +1000, +2500 and +4000&#160;ms after it
/// landed, and it records a trail of every focus change (event listener AND a 50&#160;ms poller,
/// because removing the focused element from the DOM does NOT dispatch focusout in Chromium — a
/// silent drop to &lt;body&gt; is exactly the shape that leaves no event behind).
/// </para>
///
/// <para>
/// It asserts almost nothing on purpose, in the manner of <see cref="A3SurveyProbe"/>: its job is
/// to produce the evidence a real assertion can then be written against.
/// </para>
///
/// <para>
/// WHAT IT MEASURED, 2026-09-03. The async-render hypothesis above is REFUTED: with the fixture's
/// <c>NO_AT_BRIDGE=1</c>, all seven cold-start routes hold their heading through +4000&#160;ms —
/// through the account load, the book refresh and two ticks of the timer — and Chromium's own AX
/// node for <c>activeElement</c> agrees (<c>role='heading' focused=True</c>, not ignored). The
/// focus DOES wander, but only when the AT-SPI bridge is open: 6 of 14 opens across two bridge-on
/// runs, 0 of 14 with the bridge suppressed and nothing else changed. Every wandering
/// <c>focusin</c> carries an EMPTY JavaScript stack, where the app's own call carries
/// <c>keyboard.js focusElement</c> — so the mover is the embedder, not page script. Two of the
/// landing sites were BEHIND the modal, and <c>inert</c> appears nowhere in the component library;
/// <c>aria-modal="true"</c> alone does not make background content unfocusable.
/// </para>
/// </summary>
[Collection("Terminal browser")]
public sealed class ModalFocusPersistenceProbe
{
    private readonly TerminalBrowserFixture _fixture;
    public ModalFocusPersistenceProbe(TerminalBrowserFixture fixture) => _fixture = fixture;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Installed BEFORE the chord is pressed. Two independent records of the same thing:
    /// <list type="bullet">
    /// <item>the event trail — <c>focusin</c>/<c>focusout</c> in the capture phase, with the JS
    /// stack at dispatch time, which names the caller when something called <c>.focus()</c>;</item>
    /// <item>the poll trail — <c>document.activeElement</c> every 50&#160;ms, which is the only
    /// witness to a focus that vanished because its element was removed from the DOM.</item>
    /// </list>
    /// </summary>
    private const string InstallTrail = """
        () => {
            window.__t0 = performance.now();
            window.__trail = [];
            window.__poll = [];
            const desc = el => {
                if (!el) return '(null)';
                if (el === document.body) return 'BODY';
                const id = el.id ? "#" + el.id : '';
                const role = el.getAttribute ? (el.getAttribute('role') || '') : '';
                const label = el.getAttribute ? (el.getAttribute('aria-label') || '') : '';
                const txt = (el.textContent || '').replace(/\s+/g, ' ').trim().slice(0, 40);
                return el.tagName + id + (role ? "[role=" + role + "]" : '')
                     + (label ? " aria-label='" + label + "'" : '')
                     + (txt ? " “" + txt + "”" : '');
            };
            window.__desc = desc;
            const now = () => Math.round(performance.now() - window.__t0);
            const rec = type => e => {
                let stack = '';
                try { stack = (new Error()).stack || ''; } catch (_) {}
                window.__trail.push({
                    t: now(), type,
                    target: desc(e.target),
                    related: desc(e.relatedTarget),
                    stack: stack.split('\n').slice(1, 6).join(' | ')
                });
            };
            document.addEventListener('focusin', rec('focusin'), true);
            document.addEventListener('focusout', rec('focusout'), true);
            let last = null;
            window.__pollId = setInterval(() => {
                const d = desc(document.activeElement);
                if (d !== last) { window.__poll.push({ t: now(), el: d }); last = d; }
            }, 50);
        }
        """;

    private static string Describe(JsonElement node)
    {
        string role = node.TryGetProperty("role", out var r) && r.TryGetProperty("value", out var rv)
            ? rv.GetString() ?? "" : "";
        string name = node.TryGetProperty("name", out var n) && n.TryGetProperty("value", out var nv)
            ? nv.GetString() ?? "" : "";
        bool ignored = node.TryGetProperty("ignored", out var ig) && ig.GetBoolean();
        var flags = new List<string>();
        if (node.TryGetProperty("properties", out var props))
            foreach (var p in props.EnumerateArray())
            {
                var pn = p.GetProperty("name").GetString();
                if (pn is "focused" or "focusable" or "hidden" or "hiddenRoot")
                    flags.Add(pn + "=" + p.GetProperty("value").GetProperty("value"));
            }
        string ignoredReasons = "";
        if (ignored && node.TryGetProperty("ignoredReasons", out var irs))
            ignoredReasons = " ignoredReasons=[" + string.Join(",",
                irs.EnumerateArray().Select(x => x.GetProperty("name").GetString())) + "]";
        return $"role='{role}' name='{name}'" + (ignored ? " IGNORED" : "")
               + ignoredReasons + (flags.Count > 0 ? " " + string.Join(" ", flags) : "");
    }

    /// <summary>
    /// Chromium's own accessibility node for whatever currently has focus — the authoritative
    /// read, taken by handing <c>document.activeElement</c>'s remote object straight to
    /// <c>Accessibility.getPartialAXTree</c>. Playwright's <c>GetByRole</c> is not the
    /// accessibility tree and a scan of <c>getFullAXTree</c> for the first <c>focused=true</c>
    /// node is not either: Chromium marks the <c>RootWebArea</c> focused whenever the document
    /// has focus, so the first hit is the document, every time.
    /// </summary>
    private static async Task<(string Node, string AllFocused)> AxFocusedAsync(TerminalPage t)
    {
        try
        {
            var cdp = await t.Page.Context.NewCDPSessionAsync(t.Page);
            await cdp.SendAsync("Accessibility.enable");
            await cdp.SendAsync("DOM.enable");

            // Every node the tree calls focused, in order — so "the RootWebArea is also focused"
            // is visible as the artefact it is rather than being mistaken for the answer.
            var all = new List<string>();
            var full = await cdp.SendAsync("Accessibility.getFullAXTree");
            if (full is not null)
                foreach (var node in full.Value.GetProperty("nodes").EnumerateArray())
                {
                    if (!node.TryGetProperty("properties", out var props)) continue;
                    bool focused = props.EnumerateArray().Any(p =>
                        p.GetProperty("name").GetString() == "focused"
                        && p.GetProperty("value").TryGetProperty("value", out var bv)
                        && bv.ValueKind == JsonValueKind.True);
                    if (focused) all.Add(Describe(node));
                }

            var evaluated = await cdp.SendAsync("Runtime.evaluate", new Dictionary<string, object>
            {
                ["expression"] = "document.activeElement",
            });
            string? objectId = evaluated?.GetProperty("result").TryGetProperty("objectId", out var oid) == true
                ? oid.GetString() : null;
            if (objectId is null)
                return ("(activeElement had no remote object)", string.Join(" ;; ", all));

            var partial = await cdp.SendAsync("Accessibility.getPartialAXTree",
                new Dictionary<string, object> { ["objectId"] = objectId, ["fetchRelatives"] = false });
            if (partial is null) return ("(no partial AX tree)", string.Join(" ;; ", all));

            var nodes = partial.Value.GetProperty("nodes").EnumerateArray().ToList();
            return (nodes.Count == 0
                        ? "(activeElement has NO node in the accessibility tree)"
                        : string.Join(" ;; ", nodes.Select(Describe)),
                    string.Join(" ;; ", all));
        }
        catch (Exception ex)
        {
            var msg = "(AX read failed: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0] + ")";
            return (msg, msg);
        }
    }

    [BrowserFact]
    public async Task Where_is_focus_250ms_1s_2_5s_and_4s_after_a_modal_opens()
    {
        // Every cold-start-reachable keyboard route. Shortcut-only, so no toolbar button has to
        // exist for the probe to reach the dialog.
        var routes = ModalRoutes.Keyboard.Where(r => r.ColdStartReachable).ToList();

        var report = new List<Dictionary<string, object?>>();
        var lines = new List<string>();
        lines.Add(string.Format("{0,-24} {1,-10} {2,-6} {3}", "modal", "chord", "landed", "activeElement at +250 / +1000 / +2500 / +4000 ms"));

        foreach (var route in routes)
        {
            var row = new Dictionary<string, object?>
            {
                ["modal"] = route.Modal,
                ["chord"] = route.Trigger,
                ["expectedFocus"] = route.ExpectedFocusId,
            };

            await using var t = await _fixture.NewPageAsync();
            try
            {
                await t.Page.EvaluateAsync(InstallTrail);

                await t.PressAsync(route.Trigger);

                bool opened = await t.WaitForDialogAsync(8_000);
                row["opened"] = opened;
                if (!opened)
                {
                    row["note"] = "dialog never appeared";
                    report.Add(row);
                    lines.Add(string.Format("{0,-24} {1,-10} {2,-6} {3}", route.Modal, route.Trigger, "-", "DIALOG NEVER OPENED"));
                    continue;
                }

                bool landed = await t.WaitForFocusAsync(route.ExpectedFocusId, 4_000);
                row["landedOnHeading"] = landed;
                row["atLand"] = (await t.ActiveElementAsync()).Describe();

                // Samples are cumulative from the moment of landing, so the gaps are the deltas.
                var samples = new Dictionary<string, string>();
                int[] offsets = { 250, 1000, 2500, 4000 };
                int previous = 0;
                foreach (int offset in offsets)
                {
                    await t.Page.WaitForTimeoutAsync(offset - previous);
                    previous = offset;
                    samples["+" + offset + "ms"] = (await t.ActiveElementAsync()).Describe();
                    if (offset == 2500)
                    {
                        var (node, allFocused) = await AxFocusedAsync(t);
                        row["axFocusedAt2500"] = node;
                        row["axAllFocusedNodesAt2500"] = allFocused;
                    }
                }
                row["samples"] = samples;

                row["stillOnHeadingAt4000"] =
                    (await t.ActiveElementAsync()).Id == route.ExpectedFocusId;

                row["trail"] = await t.Page.EvaluateAsync<JsonElement>("() => window.__trail || []");
                row["pollTrail"] = await t.Page.EvaluateAsync<JsonElement>("() => window.__poll || []");
                row["dialogIds"] = await t.VisibleDialogIdsAsync();
                row["headingStillInDom"] = await t.Page.EvaluateAsync<bool>(
                    "id => !!document.getElementById(id)", route.ExpectedFocusId);

                lines.Add(string.Format("{0,-24} {1,-10} {2,-6} {3}",
                    route.Modal, route.Trigger, landed ? "YES" : "NO",
                    string.Join("  |  ", offsets.Select(o => samples["+" + o + "ms"]))));
            }
            catch (Exception ex)
            {
                row["error"] = ex.GetType().Name + ": " + ex.Message.Split('\n')[0];
                lines.Add(string.Format("{0,-24} {1,-10} {2,-6} {3}", route.Modal, route.Trigger, "ERR", row["error"]));
            }

            report.Add(row);
        }

        var text = string.Join("\n", lines);
        Console.WriteLine();
        Console.WriteLine("===== MODAL FOCUS PERSISTENCE =====");
        Console.WriteLine(text);
        Console.WriteLine("===================================");

        var dir = Path.Combine(RepoRoot(), "scratchpad");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "modal_focus_persistence.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(dir, "modal_focus_persistence.txt"), text);

        // The only assertion: the probe covered what it claims to cover. Findings are the file.
        Assert.Equal(routes.Count, report.Count);
    }

    /// <summary>
    /// The same measurement, in the environment the REPORT came from: a Chromium with the AT-SPI
    /// bridge left ON, so whatever assistive technology is running on this box is attached to the
    /// page while the modals open.
    ///
    /// <para>
    /// <see cref="TerminalBrowserFixture"/> sets <c>NO_AT_BRIDGE=1</c> on purpose, and the reason
    /// recorded there is precisely the thing under investigation: with Orca attached, Chromium was
    /// observed MOVING KEYBOARD FOCUS to follow Orca's caret, with no <c>focus()</c> call, no key
    /// and no DOM change. A harness that suppresses the bridge can never see a focus move that
    /// only exists when the bridge is up — which is exactly the gap between "every browser test is
    /// green" and "when I press Alt+T I am on the order type dropdown".
    /// </para>
    ///
    /// <para>
    /// This test therefore launches its OWN browser rather than the fixture's. It is a probe, not
    /// a contract: what it measures depends on whether an AT is running on the machine, so it
    /// records that fact alongside the result and asserts nothing about focus. Reading the two
    /// files side by side is the finding.
    /// </para>
    /// </summary>
    [BrowserFact]
    public Task Where_is_focus_when_the_AT_SPI_bridge_is_left_ON() =>
        BridgeRunAsync(suppressBridge: false, "modal_focus_persistence_atspi");

    /// <summary>
    /// The control for the test above, and the reason its result can be attributed to anything.
    /// It differs in exactly ONE thing: <c>NO_AT_BRIDGE=1</c>. Both runs pass
    /// <c>--force-renderer-accessibility</c>, so Chromium builds the same full accessibility tree
    /// in both — which rules out "the AX tree existing" as the cause and leaves only "a client is
    /// attached to it". A run that wandered here as well would mean the wandering is Chromium's
    /// own, and the finding would be a different one.
    /// </summary>
    [BrowserFact]
    public Task Control_same_browser_but_the_bridge_suppressed() =>
        BridgeRunAsync(suppressBridge: true, "modal_focus_persistence_bridge_off");

    private async Task BridgeRunAsync(bool suppressBridge, string fileStem)
    {
        var routes = ModalRoutes.Keyboard.Where(r => r.ColdStartReachable).ToList();

        // What was attached while this ran, recorded so a green-looking row can be told apart
        // from a row measured on a box with no assistive technology running at all.
        var environment = new Dictionary<string, object?>
        {
            ["atSpiBusSocket"] = Directory.Exists("/run/user/" + (Environment.GetEnvironmentVariable("UID") ?? "1000") + "/at-spi"),
            ["DISPLAY"] = Environment.GetEnvironmentVariable("DISPLAY"),
            ["orcaRunning"] = System.Diagnostics.Process.GetProcessesByName("orca").Length > 0,
        };

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Channel = "chromium",
            // NO_AT_BRIDGE is the ONE thing that varies between this run and its control.
            // --force-renderer-accessibility is passed by both, so Chromium builds the same full
            // AX tree either way and the only remaining difference is whether a client can attach.
            Args = new[] { "--no-sandbox", "--disable-dev-shm-usage", "--force-renderer-accessibility" },
            Env = suppressBridge
                ? new Dictionary<string, string> { ["NO_AT_BRIDGE"] = "1" }
                : null,
        });

        var report = new List<Dictionary<string, object?>> { new() { ["environment"] = environment } };
        environment["NO_AT_BRIDGE"] = suppressBridge ? "1" : "(unset)";
        var lines = new List<string>
        {
            (suppressBridge ? "AT-SPI BRIDGE SUPPRESSED" : "AT-SPI BRIDGE LEFT ON")
                + " — orcaRunning=" + environment["orcaRunning"],
            string.Format("{0,-24} {1,-10} {2,-6} {3}", "modal", "chord", "landed", "activeElement at +250 / +1000 / +2500 / +4000 ms"),
        };

        foreach (var route in routes)
        {
            var row = new Dictionary<string, object?>
            {
                ["modal"] = route.Modal,
                ["chord"] = route.Trigger,
                ["expectedFocus"] = route.ExpectedFocusId,
            };

            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1400, Height = 950 },
            });
            var page = await context.NewPageAsync();
            await using var t = new TerminalPage(page, context);
            try
            {
                await t.GotoAppAsync(_fixture.RootUrl);
                await t.Page.EvaluateAsync(InstallTrail);
                await t.PressAsync(route.Trigger);

                bool opened = await t.WaitForDialogAsync(8_000);
                row["opened"] = opened;
                if (!opened) { row["note"] = "dialog never appeared"; report.Add(row); continue; }

                bool landed = await t.WaitForFocusAsync(route.ExpectedFocusId, 4_000);
                row["landedOnHeading"] = landed;
                row["atLand"] = (await t.ActiveElementAsync()).Describe();

                var samples = new Dictionary<string, string>();
                int[] offsets = { 250, 1000, 2500, 4000 };
                int previous = 0;
                foreach (int offset in offsets)
                {
                    await t.Page.WaitForTimeoutAsync(offset - previous);
                    previous = offset;
                    samples["+" + offset + "ms"] = (await t.ActiveElementAsync()).Describe();
                    if (offset == 2500)
                    {
                        var (node, allFocused) = await AxFocusedAsync(t);
                        row["axFocusedAt2500"] = node;
                        row["axAllFocusedNodesAt2500"] = allFocused;
                    }
                }
                row["samples"] = samples;
                row["stillOnHeadingAt4000"] = (await t.ActiveElementAsync()).Id == route.ExpectedFocusId;
                row["trail"] = await t.Page.EvaluateAsync<JsonElement>("() => window.__trail || []");
                row["pollTrail"] = await t.Page.EvaluateAsync<JsonElement>("() => window.__poll || []");

                lines.Add(string.Format("{0,-24} {1,-10} {2,-6} {3}",
                    route.Modal, route.Trigger, landed ? "YES" : "NO",
                    string.Join("  |  ", offsets.Select(o => samples["+" + o + "ms"]))));
            }
            catch (Exception ex)
            {
                row["error"] = ex.GetType().Name + ": " + ex.Message.Split('\n')[0];
                lines.Add(string.Format("{0,-24} {1,-10} {2,-6} {3}", route.Modal, route.Trigger, "ERR", row["error"]));
            }

            report.Add(row);
        }

        var text = string.Join("\n", lines);
        Console.WriteLine();
        Console.WriteLine("===== MODAL FOCUS PERSISTENCE (" + fileStem + ") =====");
        Console.WriteLine(text);
        Console.WriteLine("==================================================");

        var dir = Path.Combine(RepoRoot(), "scratchpad");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileStem + ".json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(dir, fileStem + ".txt"), text);

        Assert.Equal(routes.Count + 1, report.Count);
    }
}
