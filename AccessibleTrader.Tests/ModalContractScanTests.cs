using System.Text.RegularExpressions;

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
    ///      c. all of those names are the SAME string,
    ///   2. and (2026-09-02) the dialog element wears that same name as data-modal-name,
    ///      which is how keyboard.js maps the top of the shared ModalStack to an element.
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

        /// <summary>
        /// The file with its comments removed — razor <c>@* … *@</c> blocks, <c>/* … */</c> blocks
        /// and <c>//</c> line comments.
        ///
        /// <para>
        /// Every check below used to run against the raw text, so a COMMENTED-OUT
        /// <c>base.OnInitialized()</c> satisfied the contract — which is close to the exact shape
        /// of the SaveWorkspaceModal bug this scanner exists to catch, since commenting the call
        /// out while debugging Escape is a plausible way to arrive there. A note ABOUT the
        /// contract must not read as an implementation of it.
        /// </para>
        /// </summary>
        internal static string CodeOnly(string text)
        {
            text = Regex.Replace(text, @"@\*.*?\*@", "", RegexOptions.Singleline);
            text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
            // Line comments only when the "//" starts the line (after whitespace): a bare
            // strip would eat "https://" out of a URL attribute and shorten real markup.
            text = Regex.Replace(text, @"(?m)^\s*//.*$", "");
            return text;
        }

        /// <summary>
        /// Every component that puts up a dialog, by its declared role.
        ///
        /// <para>
        /// <c>alertdialog</c> counts, and leaving it out was a live hole rather than a
        /// theoretical one: the Toolbar's shape-change confirmation declared
        /// <c>role="alertdialog"</c> and skipped the entire contract — no
        /// <c>ModalStateChangedEvent</c>, no focus move, no Escape — and this scan, which exists
        /// to catch exactly that, could not see it. A role the scanner does not know is a way
        /// out of the contract, so the list is the whole ARIA dialog family rather than the one
        /// role most modals happen to use.
        /// </para>
        /// </summary>
        private static IEnumerable<string> DialogComponents()
        {
            string[] roles = { "dialog", "alertdialog" };
            string componentsDir = Path.Combine(RepoRoot(), "AccessibleTrader.BlazorClient.Components");
            foreach (var file in Directory.EnumerateFiles(componentsDir, "*.razor", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                if (roles.Any(r => text.Contains($"role=\"{r}\"") || text.Contains($"role='{r}'")))
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
                string text = CodeOnly(File.ReadAllText(file));

                bool inheritsModalBase = text.Contains("@inherits ModalBase");

                if (inheritsModalBase)
                {
                    // Base-class path: an OnInitialized override MUST call base —
                    // the exact SaveWorkspaceModal bug (ModalBase also self-heals in
                    // ShowModalAsync now, but the base call keeps Escape armed even
                    // for modals shown before first render).
                    if (Regex.IsMatch(text, @"override\s+void\s+OnInitialized\s*\(")
                        && !text.Contains("base.OnInitialized()"))
                        failures.Add($"{name}: inherits ModalBase but overrides OnInitialized without base.OnInitialized().");
                }

                // Name agreement, for BOTH paths.
                //
                // This used to `continue` past here for every ModalBase inheritor, so an
                // inheritor that published its own ModalStateChangedEvent(true, "Foo") /
                // (false, "Bar") — or whose events disagreed with its own ModalName override —
                // was never checked at all. ModalBase arms the subscription; it cannot stop a
                // subclass publishing a second, differently-named pair alongside it, and a
                // close event under the wrong name leaks the modal stack exactly as it does on
                // the self-implemented path.
                var openNames = Regex.Matches(text, @"ModalStateChangedEvent\(\s*true\s*,\s*""([^""]+)""")
                    .Select(m => m.Groups[1].Value).Distinct().ToList();
                var closeNames = Regex.Matches(text, @"ModalStateChangedEvent\(\s*false\s*,\s*""([^""]+)""")
                    .Select(m => m.Groups[1].Value).Distinct().ToList();
                var escapeNames = Regex.Matches(text, @"e\.ModalName\s*==\s*""([^""]+)""")
                    .Select(m => m.Groups[1].Value).Distinct().ToList();
                // A ModalBase inheritor names itself through the override, not through a literal.
                var declaredNames = Regex.Matches(text, @"override\s+string\s+ModalName\s*=>\s*""([^""]+)""")
                    .Select(m => m.Groups[1].Value).Distinct().ToList();

                if (!inheritsModalBase)
                {
                    // Self-implemented path: the base class is not doing any of this for it.
                    if (openNames.Count == 0)
                        failures.Add($"{name}: never publishes ModalStateChangedEvent(true, …) — the open announcement and modal-stack tracking are missing.");
                    if (closeNames.Count == 0)
                        failures.Add($"{name}: never publishes ModalStateChangedEvent(false, …) — the modal stack leaks and chart commands stay suppressed.");
                    if (!text.Contains("CloseTopModalEvent"))
                        failures.Add($"{name}: no CloseTopModalEvent subscription — Escape cannot close it.");
                }

                // The dialog element must WEAR the name it publishes, as data-modal-name. The
                // browser's Tab trap resolves the top of the shared ModalStack — a NAME — to
                // a dialog element through this attribute; a dialog without it, or with a
                // different spelling, is a dialog the trap cannot find, and the trap then
                // falls back to DOM order, which is the exact defect the stack replaced
                // (Settings then F1: focus trapped underneath Help). This is checked on the
                // opening tag, not the file, so an attribute on some other element does not
                // count.
                var dialogTags = OpeningTagsContaining(text, "role=\"dialog\"")
                    .Concat(OpeningTagsContaining(text, "role='dialog'"))
                    .Concat(OpeningTagsContaining(text, "role=\"alertdialog\""))
                    .Concat(OpeningTagsContaining(text, "role='alertdialog'"))
                    .ToList();
                var attrNames = new List<string>();
                foreach (var tag in dialogTags)
                {
                    var m = Regex.Match(tag, @"data-modal-name\s*=\s*[""']([^""']*)[""']");
                    if (!m.Success)
                        failures.Add($"{name}: dialog element has no data-modal-name — the Tab trap cannot map the modal stack's top to this dialog.");
                    else if (m.Groups[1].Value.Length == 0 || m.Groups[1].Value.StartsWith("@"))
                        failures.Add($"{name}: data-modal-name must be the literal published name, not \"{m.Groups[1].Value}\".");
                    else
                        attrNames.Add(m.Groups[1].Value);
                }

                var allNames = openNames.Concat(closeNames).Concat(escapeNames).Concat(declaredNames)
                    .Concat(attrNames).Distinct().ToList();
                if (allNames.Count > 1)
                    failures.Add($"{name}: open/close/Escape/data-modal-name names disagree: {string.Join(", ", allNames)}.");

                // A ModalBase inheritor with no literal anywhere publishes ModalBase's default —
                // the class name with "Modal" stripped — so that is what the attribute must say.
                if (inheritsModalBase && openNames.Count == 0 && declaredNames.Count == 0)
                {
                    string cls = Path.GetFileNameWithoutExtension(file);
                    string expected = cls.EndsWith("Modal", StringComparison.Ordinal) ? cls[..^5] : cls;
                    foreach (var a in attrNames.Where(a => a != expected))
                        failures.Add($"{name}: data-modal-name is \"{a}\" but ModalBase will publish \"{expected}\" (the default ModalName).");
                }
            }

            Assert.True(scanned >= 15, $"Only {scanned} dialogs scanned — the glob is broken, not the modals.");
            Assert.True(failures.Count == 0,
                "Modal contract violations:\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// The comment stripper, on its own. The contract scan above is a source scan, so its
        /// correctness IS this function's correctness — and its failure mode (a commented-out
        /// call reading as a real one) is invisible from the scan's own green.
        /// </summary>
        [Fact]
        public void TheCommentStripper_RemovesCommentsAndKeepsCode()
        {
            const string markup = """
                @* base.OnInitialized(); *@
                @inherits ModalBase
                // base.OnInitialized();
                /* base.OnInitialized(); */
                <a href="https://example.com/x">link</a>
                protected override void OnInitialized() { }
                """;

            string code = ModalContractScanTests.CodeOnly(markup);

            Assert.DoesNotContain("base.OnInitialized()", code);
            Assert.Contains("@inherits ModalBase", code);
            // A URL is not a line comment: stripping "//" anywhere would shorten real markup.
            Assert.Contains("https://example.com/x", code);
        }

        /// <summary>
        /// The name-agreement check now runs for ModalBase inheritors too, and today no inheritor
        /// publishes its own state events — so that branch is currently VACUOUS. This pins the
        /// detection itself against a synthetic file, so the rule is known to work on the day
        /// someone writes the modal that needs it.
        /// </summary>
        [Fact]
        public void NameDisagreement_IsDetected_EvenForAModalBaseInheritor()
        {
            const string markup = """
                @inherits ModalBase
                <div role="dialog"></div>
                @code {
                    protected override string ModalName => "Foo";
                    void Open()  { EventBus.Publish(new ModalStateChangedEvent(true, "Foo")); }
                    void Close() { EventBus.Publish(new ModalStateChangedEvent(false, "Bar")); }
                }
                """;

            string text = ModalContractScanTests.CodeOnly(markup);

            var open = Regex.Matches(text, @"ModalStateChangedEvent\(\s*true\s*,\s*""([^""]+)""")
                .Select(m => m.Groups[1].Value);
            var close = Regex.Matches(text, @"ModalStateChangedEvent\(\s*false\s*,\s*""([^""]+)""")
                .Select(m => m.Groups[1].Value);
            var declared = Regex.Matches(text, @"override\s+string\s+ModalName\s*=>\s*""([^""]+)""")
                .Select(m => m.Groups[1].Value);

            var all = open.Concat(close).Concat(declared).Distinct().ToList();

            Assert.Equal(new[] { "Foo", "Bar" }, all);
            Assert.True(all.Count > 1, "The scan would not have flagged this modal.");
        }

        // ── Tablists must handle arrow keys ──────────────────────────────────

        /// <summary>
        /// The full opening tag containing each occurrence of <paramref name="needle"/> —
        /// from its <c>&lt;</c> to the <c>&gt;</c> that closes it, with quoted attribute
        /// values skipped so a <c>&gt;</c> inside an inline style cannot end the tag early.
        /// </summary>
        /// <summary>
        /// Razor comments removed, so a scan cannot be tripped — or satisfied — by prose.
        ///
        /// <para>
        /// Found 2026-09-02: a comment in GatedButton.razor explaining why the reason span
        /// carries role="none" mentions <c>role="tablist"</c>, and
        /// <see cref="EveryTablistHandlesArrowKeys"/> promptly failed a component that has no
        /// tablist in it. The same hole runs the other way and is worse — a comment quoting
        /// the shape a scan looks FOR makes the scan pass on a file that no longer has it.
        /// <c>DashboardSourceReader.Stripped()</c> exists for exactly this reason; these
        /// scans predate it.
        /// </para>
        /// </summary>
        internal static string WithoutRazorComments(string markup)
        {
            var sb = new System.Text.StringBuilder(markup.Length);
            int i = 0;
            while (i < markup.Length)
            {
                int open = markup.IndexOf("@*", i, StringComparison.Ordinal);
                if (open < 0) { sb.Append(markup, i, markup.Length - i); break; }
                sb.Append(markup, i, open - i);
                int close = markup.IndexOf("*@", open + 2, StringComparison.Ordinal);
                if (close < 0) break;                      // unterminated: drop the rest
                // Keep the newlines so any line-based reader downstream still lines up.
                for (int k = open; k < close + 2; k++) if (markup[k] == '\n') sb.Append('\n');
                i = close + 2;
            }
            return sb.ToString();
        }

        internal static IEnumerable<string> OpeningTagsContaining(string markup, string needle)
        {
            int from = 0;
            while (true)
            {
                int hit = markup.IndexOf(needle, from, StringComparison.Ordinal);
                if (hit < 0) yield break;
                from = hit + needle.Length;

                int open = markup.LastIndexOf('<', hit);
                if (open < 0) continue;

                int i = open;
                for (; i < markup.Length; i++)
                {
                    char c = markup[i];
                    if (c == '"' || c == '\'')
                    {
                        char quote = c;
                        i++;
                        while (i < markup.Length && markup[i] != quote) i++;
                        continue;
                    }
                    if (c == '>') break;
                }
                if (i < markup.Length) yield return markup.Substring(open, i - open + 1);
            }
        }

        [Fact]
        public void EveryTablistHandlesArrowKeys()
        {
            // A role="tablist" tells assistive tech that arrow keys move between the tabs,
            // and a roving tabindex (0 on the active tab, -1 on the rest) tells the BROWSER
            // the same thing. Declare either and implement neither and the tabs become
            // unreachable by keyboard — which is what happened to Settings: five of its six
            // tabs, including the whole keyboard-rebinding UI and the paper-account reset,
            // were mouse-only.
            //
            // There were eight tablists built by different hands: one used
            // aria-activedescendant with its own handler, six left every tab a plain Tab
            // stop, and one set the roving tabindex and stopped. This is the assertion that
            // stops the ninth inventing a tenth convention.
            var failures = new List<string>();
            int tablists = 0;

            string componentsDir = Path.Combine(RepoRoot(), "AccessibleTrader.BlazorClient.Components");
            foreach (var file in Directory.EnumerateFiles(componentsDir, "*.razor", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

                string text = WithoutRazorComments(File.ReadAllText(file));
                foreach (var tag in OpeningTagsContaining(text, "role=\"tablist\""))
                {
                    tablists++;
                    if (!tag.Contains("@onkeydown", StringComparison.Ordinal))
                        failures.Add($"{Path.GetFileName(file)}: a role=\"tablist\" with no @onkeydown handler.");
                }
            }

            Assert.True(tablists >= 8, $"Only {tablists} tablists found — the scan has lost its source root.");
            Assert.True(failures.Count == 0,
                "Tablists that promise arrow-key navigation and do not implement it. Wire the "
              + "container's @onkeydown to TablistNavigator (ModalBase.NavigateTablistAsync does "
              + "the focus move for ModalBase modals):\n  " + string.Join("\n  ", failures));
        }

        [Fact]
        public void TheTablistScannerReadsWholeTagsNotFragments()
        {
            // An inline style containing '>' must not end the tag early, or a handler
            // declared after it would be invisible and the scan would report a false
            // failure — or worse, miss a real one on the next attribute.
            const string markup = """
                <div role="tablist" style="grid-template: a > b;" @onkeydown="OnKey">
                <div role="tablist" aria-label="bare">
                """;

            var tags = OpeningTagsContaining(markup, "role=\"tablist\"").ToList();

            Assert.Equal(2, tags.Count);
            Assert.Contains("@onkeydown", tags[0]);
            Assert.DoesNotContain("@onkeydown", tags[1]);
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
