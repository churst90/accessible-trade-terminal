using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Scan guards for the application CHROME — the surfaces outside any dialog, which the modal
    /// contract scanners deliberately do not walk.
    ///
    /// <para>
    /// Every guard here was written against a defect the 2026-09-01 accessibility audit
    /// demonstrated, and every one has been proved red by reintroducing that defect. They exist
    /// because the audit's dominant finding was not any single WCAG failure: it was TEN tests that
    /// were green while asserting something other than what they claimed. Two of the four rules
    /// below are aimed squarely at that class — a guard written in C# does not protect the
    /// JavaScript it describes, and a comment is not an implementation.
    /// </para>
    ///
    /// <para>
    /// All markup checks run against <see cref="ModalContractScanTests.CodeOnly"/>. That is not a
    /// nicety: the fixes for these very defects added comments that NAME the banned constructs
    /// ("No role=toolbar…", "No outline: none here…"), so a raw-text scan would fail on its own
    /// documentation and be deleted by the next person who hit it.
    /// </para>
    /// </summary>
    public class ChromeAccessibilityScanTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string ComponentsDir() =>
            Path.Combine(RepoRoot(), "AccessibleTrader.BlazorClient.Components");

        private static IEnumerable<string> RazorFiles() =>
            Directory.EnumerateFiles(ComponentsDir(), "*.razor", SearchOption.AllDirectories);

        private static IEnumerable<string> Stylesheets()
        {
            yield return Path.Combine(RepoRoot(), "AccessibleTrader.WebHost", "wwwroot", "app.css");
            yield return Path.Combine(RepoRoot(), "AccessibleTrader.BlazorClient", "wwwroot", "app.css");
        }

        private static string KeyboardJs() => File.ReadAllText(
            Path.Combine(ComponentsDir(), "wwwroot", "js", "keyboard.js"));

        // ── 1. Inline `outline: none` on a focusable element ────────────────────────

        [Fact]
        public void NoFocusableElementSuppressesItsOwnFocusRingInline()
        {
            // The chart carried `outline: none` in its inline style attribute. An inline
            // declaration beats any author stylesheet rule without !important, so it silently
            // defeated app.css's own `[tabindex]:focus-visible { outline: var(--focus-outline) }`
            // — and unlike the app's three OTHER outline:none sites, nothing replaced it. The
            // application's primary control, and the gate for every single-letter command, had no
            // focus indicator at all.
            //
            // The rule is deliberately narrow: suppressing the ring in a STYLESHEET is legitimate
            // and the app does it three times, each with a replacement on the next line. What is
            // not legitimate is doing it inline on a focusable element, where nothing in CSS can
            // ever put it back.
            var failures = new List<string>();
            int scanned = 0;

            foreach (var file in RazorFiles())
            {
                string text = ModalContractScanTests.CodeOnly(File.ReadAllText(file));
                foreach (var tag in ModalContractScanTests.OpeningTagsContaining(text, "style="))
                {
                    bool focusable =
                        Regex.IsMatch(tag, @"\btabindex\s*=\s*""(?!-1)") ||
                        Regex.IsMatch(tag, @"^<\s*(button|a|input|select|textarea|summary)\b");
                    if (!focusable) continue;
                    scanned++;

                    if (Regex.IsMatch(tag, @"outline\s*:\s*none", RegexOptions.IgnoreCase))
                        failures.Add($"{Path.GetFileName(file)}: focusable element has inline " +
                                     $"'outline: none', which no stylesheet can override — {Trim(tag)}");
                }
            }

            // Anti-vacuity: this scan is worthless if the tag walker stops matching focusable
            // elements. The chart div alone guarantees one; the floor is set well below the real
            // count so ordinary edits do not trip it.
            Assert.True(scanned >= 5,
                $"Only {scanned} focusable styled elements were found. The scan is not walking the " +
                "markup any more, so its green result means nothing.");
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        // ── 2. The JS Tab trap must know the whole ARIA dialog family ───────────────

        [Fact]
        public void TheJsTabTrapCoversTheWholeAriaDialogFamily()
        {
            // This is the C#/JavaScript drift guard, and it is the one this repo most needed.
            //
            // ModalContractScanTests.DialogComponents() was widened to { dialog, alertdialog } on
            // 2026-08-29 after a recorded live miss, with a comment explaining that "a role the
            // scanner does not know is a way out of the contract". The JS trap's selector was not
            // widened with it. So the C# scanner knew about alertdialog and the JavaScript did
            // not, and Toolbar's destructive "strip your indicators and drawings" confirmation
            // armed the trap (it publishes ModalStateChangedEvent, so the counter went up) and
            // then fell straight through `if (dialogs.length === 0) return`.
            //
            // No accessibility guard in either test project walked any .js at all before this one,
            // which is why four Critical findings of the 2026-09-01 audit lived there.
            string js = KeyboardJs();

            var selector = Regex.Match(js, @"querySelectorAll\(\s*(['""])(?<sel>\[role=.+?)\1",
                RegexOptions.Singleline);
            Assert.True(selector.Success,
                "keyboard.js no longer contains a querySelectorAll on a role selector. The Tab " +
                "trap has been restructured, so this guard is asserting nothing and must be " +
                "rewritten against whatever replaced it.");

            string sel = selector.Groups["sel"].Value;
            foreach (var role in new[] { "dialog", "alertdialog" })
                Assert.True(sel.Contains($"role=\"{role}\"") || sel.Contains($"role='{role}'"),
                    $"keyboard.js's Tab trap selects on '{sel}', which does not include " +
                    $"role=\"{role}\". An overlay with that role increments _openModalCount, arms " +
                    "the trap, and is then invisible to it — so Tab walks out of it. The C# " +
                    "scanner covers the whole dialog family; this selector must too.");

            // The selector is not the pipeline. The 2026-09-02 review found this very guard green
            // while the alertdialog was still escaping: the selector DID name alertdialog, and the
            // line under it filtered `el.offsetParent !== null`, which CSSOM-View defines as null
            // for an element that is itself position:fixed — Toolbar's alertdialog exactly. A
            // filter one line below a widened selector is invisible to a scan that reads the
            // selector string. So this reads the whole Tab-trap block, comments stripped, for the
            // property that was wrong: nothing in it may decide visibility by offsetParent.
            string trap = TabTrapBlockCodeOnly(js);
            Assert.True(trap.Length > 200,
                "The Tab-trap block in keyboard.js could not be delimited (from the '── Tab trap' " +
                "banner to its '}, true);' registration). The trap has been restructured, so " +
                "rewrite this guard against whatever replaced it rather than deleting it.");
            Assert.False(trap.Contains("offsetParent"),
                "keyboard.js's Tab trap decides visibility by offsetParent again. That is null for " +
                "a rendered element that is itself position:fixed — Toolbar's alertdialog — so the " +
                "widened selector finds the dialog and the filter throws it away. Use " +
                "getClientRects().length > 0, which is empty only for something with no layout box.");
            Assert.True(trap.Contains("getClientRects()"),
                "keyboard.js's Tab trap no longer filters on getClientRects(). If visibility is now " +
                "decided some other way, prove it against a position:fixed dialog in " +
                "tools/jstests/keyboard-tests.mjs (the `fixed: true` node) before changing this.");
        }

        /// <summary>
        /// The Tab-trap keydown listener in keyboard.js, from its banner comment to the
        /// <c>}, true);</c> that registers it, with every <c>//</c> comment line removed — the
        /// fix for this defect documents the banned identifier by name.
        /// </summary>
        private static string TabTrapBlockCodeOnly(string js)
        {
            int start = js.IndexOf("── Tab trap", StringComparison.Ordinal);
            if (start < 0) return "";
            int end = js.IndexOf("}, true);", start, StringComparison.Ordinal);
            if (end < 0) return "";
            var lines = js.Substring(start, end - start).Split('\n')
                .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal));
            return string.Join("\n", lines);
        }

        // ── 2b. The browser harness must see what the trap sees, and fail when it sees nothing ──

        [Fact]
        public void TheBrowserHarnessDiscoversDialogsTheWayTheTrapDoes()
        {
            // The third of the four reasons the alertdialog escape stayed green. The browser
            // containment predicates in ModalBrowserContractTests used `[role="dialog"]` plus the
            // same offsetParent filter as the trap — so they could not see the alertdialog either —
            // and then returned TRUE when they found zero dialogs. "Inside" was also what "no
            // dialog visible" looked like. A probe whose empty branch is the passing branch cannot
            // fail on the dialog it cannot see.
            //
            // The harness is a separate project the main suite does not reference, so this reads
            // its sources. Comments are stripped for the same reason as everywhere in this file.
            var dir = Path.Combine(RepoRoot(), "AccessibleTrader.BrowserTests");
            var files = Directory.EnumerateFiles(dir, "*.cs", SearchOption.TopDirectoryOnly)
                .Where(f => !Path.GetFileName(f).StartsWith("A3", StringComparison.Ordinal)) // survey probes, not gates
                .ToList();
            Assert.True(files.Count >= 5, $"Only {files.Count} sources under {dir}; is the harness still there?");

            var failures = new List<string>();
            foreach (var f in files)
            {
                string code = string.Join("\n", File.ReadAllLines(f)
                    .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)
                             && !l.TrimStart().StartsWith("///", StringComparison.Ordinal)));
                string name = Path.GetFileName(f);

                // Dialog discovery: any querySelectorAll on role="dialog" must also name
                // alertdialog, and must not be followed by an offsetParent filter.
                foreach (Match m in Regex.Matches(code, @"querySelectorAll\((?<q>['""]+)(?<sel>\[role=[^)]*?)\k<q>\)\)"))
                {
                    string sel = m.Groups["sel"].Value;
                    if (!sel.Contains("dialog")) continue;
                    if (!sel.Contains("alertdialog"))
                        failures.Add($"{name}: dialog discovery on '{sel}' does not include alertdialog");
                    string tail = code.Substring(m.Index, Math.Min(200, code.Length - m.Index));
                    int stop = tail.IndexOf(';');
                    if (stop > 0) tail = tail.Substring(0, stop);
                    if (tail.Contains("offsetParent"))
                        failures.Add($"{name}: dialog discovery on '{sel}' filters by offsetParent, " +
                                     "which is null for a dialog that is itself position:fixed");
                }

                if (Regex.IsMatch(code, @"dialogs\.length\s*===\s*0\)\s*return\s+true"))
                    failures.Add($"{name}: a dialog predicate returns TRUE when it sees zero dialogs — " +
                                 "'inside' is then also what 'invisible to the trap' looks like");
            }

            // Anti-vacuity: the harness must still contain dialog discovery at all.
            Assert.True(File.ReadAllText(Path.Combine(dir, "TerminalPage.cs")).Contains("alertdialog"),
                "TerminalPage.cs no longer mentions alertdialog; dialog discovery has moved and this " +
                "guard is reading the wrong file.");
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        // ── 3. role="toolbar" is an arrow-key promise ───────────────────────────────

        [Fact]
        public void NoContainerDeclaresToolbarWithoutAnArrowKeyModel()
        {
            // The same rule EveryTablistHandlesArrowKeys enforces for role="tablist", applied to
            // the role that had four instances and zero implementations.
            //
            // role="toolbar" tells assistive tech to expect a single tab stop with arrow-key
            // traversal between the controls. All four sites were flat Tab stops by deliberate
            // design — and one of them, the main toolbar, put the role on a <nav>, where an
            // explicit role overrides the implicit one and deleted the application's ONLY
            // navigation landmark. Pressing D in NVDA gave banner/main/contentinfo and could not
            // reach the ~25 primary controls.
            //
            // Flat Tab stops remain the right choice here. Declaring a keyboard model the
            // component does not implement is what is banned.
            var failures = new List<string>();

            foreach (var file in RazorFiles())
            {
                string text = ModalContractScanTests.CodeOnly(File.ReadAllText(file));
                if (!text.Contains("role=\"toolbar\"") && !text.Contains("role='toolbar'")) continue;

                if (!text.Contains("@onkeydown"))
                    failures.Add($"{Path.GetFileName(file)}: declares role=\"toolbar\" but the file " +
                                 "has no @onkeydown handler, so the arrow-key traversal the role " +
                                 "promises does not exist. Use <nav aria-label=…> for chrome, or " +
                                 "role=\"group\" inside a dialog, unless you implement roving tabindex.");

                if (Regex.IsMatch(text, @"<nav\b[^>]*role\s*=\s*[""']toolbar"))
                    failures.Add($"{Path.GetFileName(file)}: role=\"toolbar\" on a <nav> overrides the " +
                                 "navigation landmark, so the element is exposed as a toolbar and the " +
                                 "landmark is lost entirely.");
            }

            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        // ── 4. Chart chrome takes its colours from the theme ────────────────────────

        [Fact]
        public void TheChartsStatusChromeTakesItsColoursFromTheTheme()
        {
            // Two measured 1.00:1 failures, both the same shape: a themeable surface with a
            // hardcoded foreground drawn on it.
            //
            //   - The status headline hardcoded `color: #fff` INSIDE a parent that had already
            //     computed the correct GetThemeTextHex(). On High Contrast Light that is white on
            //     #ffffff — an invisible headline, on the one screen that explains why the chart
            //     is empty, in the theme a low-vision user is most likely to have chosen.
            //   - The hover crosshair hardcoded rgba(255,255,255,0.45): 1.00:1 on High Contrast
            //     Light, 1.02:1 on Paper. It is the ONLY visual marker of which bar the cursor is
            //     on, and it vanished while the price readout beside it kept updating.
            //
            // Both now read theme values. theme.AxisText measures 21.00:1 on High Contrast Light;
            // theme.Crosshair measures 11.45:1.
            string chart = ModalContractScanTests.CodeOnly(
                File.ReadAllText(Path.Combine(ComponentsDir(), "ChartArea.razor")));

            var overlay = Regex.Match(chart, @"blackout-overlay.*?(?=@code)", RegexOptions.Singleline);
            Assert.True(overlay.Success, "ChartArea.razor no longer contains a blackout-overlay block; " +
                                         "this guard is asserting nothing.");

            Assert.False(Regex.IsMatch(overlay.Value, @"color\s*:\s*#(fff|ffffff)\b", RegexOptions.IgnoreCase),
                "ChartArea's blackout overlay hardcodes a white foreground. The overlay's own " +
                "background is theme.Background, so on the light themes this is white on white " +
                "(1.00:1 on High Contrast Light). Inherit the parent's GetThemeTextHex() instead.");

            Assert.False(chart.Contains("rgba(255,255,255,0.45)"),
                "ChartArea's crosshair hardcodes a translucent white. Composited against a light " +
                "theme's chart background that is 1.00:1 — the bar-position indicator disappears. " +
                "Use var(--crosshair-color), which ThemeCssBridge publishes from theme.Crosshair.");

            Assert.True(chart.Contains("var(--crosshair-color)"),
                "ChartArea's crosshair no longer reads --crosshair-color. If the crosshair moved, " +
                "move this guard with it rather than deleting it.");

            // The variable has to survive the whole way to the browser, in BOTH stylesheet copies —
            // which have already drifted from each other once. ThemeCoverageTests enforces the
            // VariableNames/:root parity; this is the consumer half.
            foreach (var css in Stylesheets())
                Assert.True(File.ReadAllText(css).Contains("--crosshair-color"),
                    $"{Path.GetFileName(Path.GetDirectoryName(css))}/app.css has no --crosshair-color " +
                    "fallback, so a JS-interop failure leaves the crosshair with no colour at all.");
        }

        private static string Trim(string tag) =>
            Regex.Replace(tag, @"\s+", " ").Trim() is { Length: > 160 } s ? s[..160] + "…" : Regex.Replace(tag, @"\s+", " ").Trim();
    }
}
