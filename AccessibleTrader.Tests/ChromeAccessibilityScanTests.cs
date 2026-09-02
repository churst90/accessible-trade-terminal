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
