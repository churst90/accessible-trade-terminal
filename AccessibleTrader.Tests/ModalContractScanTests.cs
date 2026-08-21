using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Debt item 5: source-level enforcement of the modal contract. The
    /// SaveWorkspaceModal Escape bug happened because a modal could silently skip
    /// part of the contract (override OnInitialized without base call) and nothing
    /// noticed until a user did. This scanner walks every dialog component and
    /// asserts the contract STRUCTURALLY, so a new modal that forgets Escape
    /// handling or mismatches its open/close names fails CI, not the user.
    ///
    /// Contract, per component containing role="dialog":
    ///   1. Inherits ModalBase (which arms everything), OR self-implements:
    ///      a. subscribes CloseTopModalEvent comparing e.ModalName to a name,
    ///      b. publishes ModalStateChangedEvent(true, name) and (false, name),
    ///      c. all of those names are the SAME string.
    /// </summary>
    public class ModalContractScanTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static IEnumerable<string> DialogComponents()
        {
            string componentsDir = Path.Combine(RepoRoot(), "AccessibleTrader.BlazorClient.Components");
            foreach (var file in Directory.EnumerateFiles(componentsDir, "*.razor", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                if (text.Contains("role=\"dialog\"") || text.Contains("role='dialog'"))
                    yield return file;
            }
        }

        [Fact]
        public void EveryDialog_HonoursTheModalContract()
        {
            var failures = new List<string>();
            int scanned = 0;

            foreach (var file in DialogComponents())
            {
                scanned++;
                string name = Path.GetFileName(file);
                string text = File.ReadAllText(file);

                if (text.Contains("@inherits ModalBase"))
                {
                    // Base-class path: an OnInitialized override MUST call base —
                    // the exact SaveWorkspaceModal bug (ModalBase also self-heals in
                    // ShowModalAsync now, but the base call keeps Escape armed even
                    // for modals shown before first render).
                    if (Regex.IsMatch(text, @"override\s+void\s+OnInitialized\s*\(")
                        && !text.Contains("base.OnInitialized()"))
                        failures.Add($"{name}: inherits ModalBase but overrides OnInitialized without base.OnInitialized().");
                    continue;
                }

                // Self-implemented path.
                var openNames = Regex.Matches(text, @"ModalStateChangedEvent\(\s*true\s*,\s*""([^""]+)""")
                    .Select(m => m.Groups[1].Value).Distinct().ToList();
                var closeNames = Regex.Matches(text, @"ModalStateChangedEvent\(\s*false\s*,\s*""([^""]+)""")
                    .Select(m => m.Groups[1].Value).Distinct().ToList();
                var escapeNames = Regex.Matches(text, @"e\.ModalName\s*==\s*""([^""]+)""")
                    .Select(m => m.Groups[1].Value).Distinct().ToList();

                if (openNames.Count == 0)
                    failures.Add($"{name}: never publishes ModalStateChangedEvent(true, …) — the open announcement and modal-stack tracking are missing.");
                if (closeNames.Count == 0)
                    failures.Add($"{name}: never publishes ModalStateChangedEvent(false, …) — the modal stack leaks and chart commands stay suppressed.");
                if (!text.Contains("CloseTopModalEvent"))
                    failures.Add($"{name}: no CloseTopModalEvent subscription — Escape cannot close it.");

                var allNames = openNames.Concat(closeNames).Concat(escapeNames).Distinct().ToList();
                if (allNames.Count > 1)
                    failures.Add($"{name}: open/close/Escape names disagree: {string.Join(", ", allNames)}.");
            }

            Assert.True(scanned >= 15, $"Only {scanned} dialogs scanned — the glob is broken, not the modals.");
            Assert.True(failures.Count == 0,
                "Modal contract violations:\n" + string.Join("\n", failures));
        }

        // ── ARIA state attributes must be strings ────────────────────────────

        /// <summary>
        /// ARIA state attributes whose only valid values are the literal tokens
        /// <c>"true"</c> / <c>"false"</c>.
        /// </summary>
        private static readonly string[] TokenValuedAria =
        {
            "aria-selected", "aria-expanded", "aria-pressed", "aria-checked",
            "aria-disabled", "aria-hidden", "aria-invalid", "aria-required",
            "aria-busy", "aria-modal", "aria-multiselectable", "aria-readonly",
        };

        /// <summary>
        /// The Razor expression bound to <paramref name="attr"/>, once per occurrence.
        ///
        /// <para>
        /// Delimiter-matched rather than regex-matched, and that is not fussiness. The first
        /// version of this scan used <c>"(@[^"]*(?:"[^"]*"[^"]*)*)"</c> to allow quotes nested
        /// inside <c>@(...)</c>. On a real file that runs away: it swallowed the rest of the
        /// component, which contains <c>"true"</c> and <c>"false"</c> in other attributes, so
        /// every binding looked correctly stringified and the scan passed **with the defect
        /// deliberately reintroduced**. A guard that cannot fail is worse than no guard,
        /// because it is counted as coverage.
        /// </para>
        /// </summary>
        internal static IEnumerable<string> AriaBindings(string text, string attr)
        {
            int i = 0;
            while (true)
            {
                int a = text.IndexOf(attr + "=\"", i, StringComparison.Ordinal);
                if (a < 0) yield break;

                int at = a + attr.Length + 2;
                i = at;
                if (at >= text.Length || text[at] != '@') continue;   // a literal value, nothing to check

                string? expr = RazorExpressionAt(text, at);
                if (expr == null) continue;
                i = at + expr.Length;
                yield return expr;
            }
        }

        /// <summary>
        /// One Razor expression starting at <paramref name="at"/> (which must be the '@').
        /// Either a parenthesised expression, delimiter-matched with string literals skipped,
        /// or a bare member chain like <c>@_showTemplates</c>.
        /// </summary>
        internal static string? RazorExpressionAt(string text, int at)
        {
            int i = at + 1;
            if (i < text.Length && text[i] == '(')
            {
                int depth = 0;
                for (int j = i; j < text.Length; j++)
                {
                    char c = text[j];
                    if (c == '"' || c == '\'')
                    {
                        char quote = c;
                        j++;
                        while (j < text.Length && text[j] != quote) j++;
                        continue;
                    }
                    if (c == '(') depth++;
                    else if (c == ')' && --depth == 0) return text.Substring(at, j - at + 1);
                }
                return null;
            }

            int k = i;
            while (k < text.Length && (char.IsLetterOrDigit(text[k]) || text[k] == '_' || text[k] == '.'
                                       || text[k] == '(' || text[k] == ')'))
                k++;
            return k > i ? text.Substring(at, k - at) : null;
        }

        [Fact]
        public void TheAriaScannerReadsOneExpressionAndStopsThere()
        {
            // Pinning the exact false negative described above: the bare-bool binding must be
            // reported even though "true"/"false" appear later in the same file.
            const string razor = """
                <button role="tab" aria-selected="@(_activeTab == "library")"
                        style="background: @(_activeTab == "library" ? "#2a4a7f" : "transparent");">
                <button role="tab" aria-selected="@(_activeTab == "build" ? "true" : "false")">
                <button aria-expanded="@_showTemplates">
                <button aria-pressed="@PressedAttr">
                """;

            var selected = AriaBindings(razor, "aria-selected").ToList();

            Assert.Equal(2, selected.Count);
            Assert.Equal("@(_activeTab == \"library\")", selected[0]);
            Assert.Equal("@(_activeTab == \"build\" ? \"true\" : \"false\")", selected[1]);

            Assert.Equal("@_showTemplates", Assert.Single(AriaBindings(razor, "aria-expanded")));
            Assert.Equal("@PressedAttr", Assert.Single(AriaBindings(razor, "aria-pressed")));
        }

        [Fact]
        public void NoAriaStateAttributeIsBoundToABareBoolean()
        {
            // Blazor's RenderTreeBuilder.AddAttribute(int, string, bool) OMITS the attribute
            // when false and emits it VALUELESS when true. An empty string is not a valid
            // true|false token, so assistive tech falls back to the role default — false.
            // The result: a screen-reader user in the Strategy Manager hears no "selected" on
            // ANY of its six tabs, and the only other cue is a background colour.
            //
            // TabBar, SettingsModal, PropertiesModal and TradingDashboardModal all use the
            // correct ternary, so this was drift rather than ignorance — which is exactly the
            // kind of thing a scan catches and a review does not.
            //
            // Accepted forms: a "true"/"false" ternary, .ToString().ToLower(), or a C#
            // string-typed member. What fails is a bare bool expression.
            var failures = new List<string>();
            int bindings = 0;

            string componentsDir = Path.Combine(RepoRoot(), "AccessibleTrader.BlazorClient.Components");
            foreach (var file in Directory.EnumerateFiles(componentsDir, "*.razor", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

                string text = File.ReadAllText(file);
                string name = Path.GetFileName(file);

                foreach (var attr in TokenValuedAria)
                {
                    foreach (string value in AriaBindings(text, attr))
                    {
                        bindings++;

                        // A literal token pair anywhere in the expression, or an explicit
                        // string conversion, means the value reaches the DOM as text.
                        bool yieldsString =
                            (value.Contains("\"true\"") && value.Contains("\"false\""))
                            || value.Contains("ToString()")
                            || value.Contains("Attr");   // a string-typed member, e.g. PressedAttr

                        if (!yieldsString)
                            failures.Add($"{name}: {attr}=\"{value}\" — bind a string, not a bool.");
                    }
                }
            }

            Assert.True(bindings > 0, "Found no ARIA state bindings at all — the scan has lost its source root.");
            Assert.True(failures.Count == 0,
                "ARIA state attributes bound to a bare bool. Blazor emits these valueless, so AT reads "
              + "the role default and the state is never announced — the control looks correct and says "
              + "nothing:\n  " + string.Join("\n  ", failures));
        }
    }
}
