using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Source-level enforcement that shipped features are REACHABLE.
    ///
    /// <para>
    /// This exists because of a real gap: the watchlist, screener, level-respect report, bar
    /// replay and split view all shipped working, with keyboard shortcuts and modals wired, and
    /// no button anywhere on screen. Everything passed. The features were, for practical purposes,
    /// invisible — you had to already know the shortcut to find out they existed.
    /// </para>
    ///
    /// <para>
    /// A unit test can't judge discoverability, but it can pin the mechanical part: every feature
    /// listed here has a toolbar button, every button names an icon that exists in the sprite, and
    /// every button carries an accessible name. Those three are exactly what was missing.
    /// </para>
    /// </summary>
    public class ToolbarControlSurfaceTests
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

        private static string Toolbar() => File.ReadAllText(Path.Combine(ComponentsDir(), "Toolbar.razor"));

        private static string Sprite() => File.ReadAllText(Path.Combine(ComponentsDir(), "IconSprite.razor"));

        /// <summary>
        /// Feature → the event its toolbar button must publish. Adding a user-facing feature to
        /// the app means adding a line here, which is the point: the list is the checklist.
        /// </summary>
        public static IEnumerable<object[]> ReachableFeatures() => new[]
        {
            new object[] { "Watchlist and screener", "OpenWatchlistEvent" },
            new object[] { "Level respect report",   "OpenLevelReportEvent" },
            new object[] { "Journal",                "OpenJournalEvent" },
            new object[] { "AI analyst",             "OpenAIAnalystEvent" },
            new object[] { "Split view",             "SplitViewCommandEvent" },
            new object[] { "Bar replay",             "ReplayCommandEvent" },
            new object[] { "Object tree",            "OpenObjectTreeEvent" },
            new object[] { "Drawing tools",          "OpenDrawingToolsEvent" },
            new object[] { "Sound designer",         "OpenSoundDesignerEvent" },
            new object[] { "Trading dashboard",      "OpenTradingDashboardEvent" },
            new object[] { "Order book",             "OpenOrderBookEvent" },
            new object[] { "Strategies",             "OpenStrategiesEvent" },
            new object[] { "Alerts",                 "OpenAlertsEvent" },
            new object[] { "API keys",               "OpenApiKeysEvent" },
            new object[] { "Deposit address",        "OpenWalletEvent" },
            new object[] { "Withdraw",               "OpenWithdrawEvent" },
        };

        [Theory]
        [MemberData(nameof(ReachableFeatures))]
        public void EveryFeature_hasAToolbarControl(string feature, string eventName)
        {
            string toolbar = Toolbar();

            // Either published directly from an OnClick lambda, or from a named handler in the
            // component's own code block — both are real wiring; a shortcut alone is not.
            Assert.True(toolbar.Contains($"new {eventName}("),
                $"{feature} has no toolbar control: Toolbar.razor never constructs {eventName}. " +
                "A keyboard shortcut on its own leaves the feature undiscoverable.");
        }

        [Fact]
        public void SplitAndReplay_sitOnTheSecondRowWithTheOtherChartToggles()
        {
            // Row 1 opens panels; row 2 changes how the chart behaves. Split and replay belong to
            // the second group, next to Heatmap / Heikin / Log — pinned so a later edit doesn't
            // scatter them back into the panel row.
            string toolbar = Toolbar();

            int logScale = toolbar.IndexOf("Icon=\"log-scale\"", StringComparison.Ordinal);
            int split    = toolbar.IndexOf("Icon=\"split-view\"", StringComparison.Ordinal);
            int replay   = toolbar.IndexOf("Icon=\"replay\"", StringComparison.Ordinal);

            Assert.True(logScale > 0, "Log scale button not found — the visual-toggle row moved.");
            Assert.True(split > logScale, "Split view button is not in the visual-toggle row.");
            Assert.True(replay > logScale, "Replay button is not in the visual-toggle row.");
        }

        [Fact]
        public void SplitAndReplay_reportTheirOwnStateSoTheButtonIsNotAWriteOnlyToggle()
        {
            string toolbar = Toolbar();

            Assert.Contains("IsToggleOn=\"@IsSplitActive\"", toolbar);
            Assert.Contains("IsToggleOn=\"@IsReplayActive\"", toolbar);
        }

        [Fact]
        public void EveryToolbarIconButton_namesAnIconThatExistsInTheSprite()
        {
            // A typo'd icon name renders an empty <use> — a button with no visible glyph. It looks
            // like a spacing bug rather than a missing feature, so it can survive review.
            var declared = Regex.Matches(Sprite(), @"<symbol id=""icon-([a-z0-9\-]+)""")
                                .Select(m => m.Groups[1].Value)
                                .ToHashSet(StringComparer.Ordinal);

            var missing = new List<string>();
            foreach (var file in Directory.EnumerateFiles(ComponentsDir(), "*.razor", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                foreach (Match m in Regex.Matches(text, @"<ToolbarIconButton[^>]*?Icon=""([a-z0-9\-]+)"""))
                {
                    string icon = m.Groups[1].Value;
                    if (!declared.Contains(icon))
                        missing.Add($"{Path.GetFileName(file)} references icon '{icon}'");
                }
            }

            Assert.True(missing.Count == 0,
                "Toolbar buttons reference icons with no sprite symbol:\n  " + string.Join("\n  ", missing));
        }

        [Fact]
        public void EveryToolbarIconButton_announcesANameThatContainsItsVisibleLabel()
        {
            // This assertion used to be `button.Contains("AriaLabel=")` — the 2026-09-01 audit
            // listed it as gate 4 of ten gates that assert the wrong thing: "PRESENCE, not
            // containment". It could not fail for any of the ten WCAG 2.5.3 Label-in-Name
            // failures that were live in this very file, because every one of them HAD an
            // AriaLabel; what was wrong was what the AriaLabel said. And it forced an AriaLabel
            // onto buttons whose Label already IS the whole name ("Drawings", "Pan left"), where
            // the only thing an override can do is break the containment.
            //
            // The property is the one the component actually implements: the accessible name is
            // `AriaLabel ?? Label`, it must be non-empty, and it must CONTAIN the visible Label
            // (WCAG 2.5.3). Dynamic values are compared literal-by-literal rather than skipped —
            // skipping them is gate 3 of the same list, and `@(x ? "Hide" : "Show")` is exactly
            // the case the rule exists for.
            //
            // LabelInNameRenderSweepTests asks the same question of the rendered DOM and is the
            // stronger instrument; this one reads source, so it also covers call sites behind a
            // conditional that a cold render never reaches.
            var offenders = new List<string>();
            int swept = 0, compared = 0;

            foreach (var file in Directory.EnumerateFiles(ComponentsDir(), "*.razor", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                // Each button spans several lines; split on the tag and take up to the closing "/>".
                foreach (var chunk in text.Split("<ToolbarIconButton").Skip(1))
                {
                    int end = chunk.IndexOf("/>", StringComparison.Ordinal);
                    string button = end > 0 ? chunk[..end] : chunk;
                    swept++;

                    string? label = AttributeValue(button, "Label");
                    string? aria  = AttributeValue(button, "AriaLabel");
                    string? icon  = AttributeValue(button, "Icon");
                    string where = $"{Path.GetFileName(file)}: button '{icon ?? "?"}'";

                    if (label is null || label.Trim().Length == 0)
                    {
                        offenders.Add($"{where} has no Label, so it has no accessible name at all");
                        continue;
                    }
                    if (aria is null) continue;   // name IS the label: 2.5.3 holds by construction

                    // A Razor expression yields several possible strings; every literal the Label
                    // can take must appear inside some literal the AriaLabel can take.
                    var visibleValues = Literals(label);
                    var announcedValues = Literals(aria);
                    if (visibleValues.Count == 0 || announcedValues.Count == 0)
                    {
                        // An expression this file cannot read is NOT a pass. Skipping it silently
                        // is how the previous version of this guard came to assert nothing about
                        // the only two buttons it was written for.
                        offenders.Add($"{where}: cannot read the string values of "
                                      + $"Label=\"{label}\" / AriaLabel=\"{aria}\" — this guard has "
                                      + "no opinion about it, which means it is unguarded");
                        continue;
                    }

                    compared++;
                    foreach (var visible in visibleValues)
                    {
                        if (announcedValues.Any(n => n.Contains(visible, StringComparison.OrdinalIgnoreCase)))
                            continue;
                        offenders.Add($"{where}: visible \"{visible}\" is not inside its announced name "
                                      + $"\"{aria}\"");
                    }
                }
            }

            // Two vacuity floors, and the SECOND is the one that matters. Counting call sites
            // found says nothing about call sites compared: the previous version of this guard
            // swept 34 and compared 27, and the 7 it did not compare included both of the
            // buttons whose defect it was written to catch.
            Assert.True(swept >= 30,
                $"the scan found only {swept} ToolbarIconButton call sites; there were 34 when "
                + "this floor was written, so the tag split has stopped matching.");
            Assert.True(compared >= 25,
                $"the scan compared only {compared} of {swept} call sites. A button whose Label "
                + "and AriaLabel were both read is guarded; the rest are not.");
            Assert.True(offenders.Count == 0,
                "Toolbar buttons whose announced name does not contain their visible label "
                + "(WCAG 2.5.3 — extend the visible words, do not replace them):\n  "
                + string.Join("\n  ", offenders));
        }

        [Fact]
        public void NoIconButtonCarriesBothAPressedStateAndAStateBearingLabel()
        {
            // ToolbarIconButton offers two toggle surfaces and they are mutually exclusive by
            // design: `IsToggleOn` emits aria-pressed and belongs to a button whose Label is
            // CONSTANT ("Heikin", pressed); `Highlighted` is the same visual on-state with no
            // ARIA at all, for a button whose Label carries the state itself ("Hide" / "Show").
            //
            // Passing both is the defect IndicatorBar shipped until 2026-09-03: a muted series
            // announced "Unmute SMA 20, toggle button, PRESSED" — the name said unmute, the state
            // said pressed, and the two adjacent buttons used opposite polarity, so "pressed"
            // meant hidden on one and muted on the other one control apart.
            var offenders = new List<string>();
            int swept = 0;

            foreach (var file in Directory.EnumerateFiles(ComponentsDir(), "*.razor", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                foreach (var chunk in text.Split("<ToolbarIconButton").Skip(1))
                {
                    int end = chunk.IndexOf("/>", StringComparison.Ordinal);
                    string button = end > 0 ? chunk[..end] : chunk;
                    swept++;

                    bool pressed     = button.Contains("IsToggleOn=", StringComparison.Ordinal);
                    bool highlighted = button.Contains("Highlighted=", StringComparison.Ordinal);
                    string? label = AttributeValue(button, "Label");
                    bool dynamicLabel = label is not null && label.Contains('@');

                    if (pressed && highlighted)
                        offenders.Add($"{Path.GetFileName(file)}: passes BOTH IsToggleOn and Highlighted");
                    else if (pressed && dynamicLabel)
                        offenders.Add($"{Path.GetFileName(file)}: aria-pressed on a button whose Label "
                                      + $"changes with state ({label}) — use Highlighted");
                }
            }

            Assert.True(swept >= 30, $"the scan found only {swept} ToolbarIconButton call sites.");
            Assert.True(offenders.Count == 0, string.Join("\n  ", offenders));
        }

        /// <summary>
        /// The value of one attribute on a Razor component tag, quote-nesting and all.
        ///
        /// <para>NOT a regex, and the reason is the whole point of this file's second rewrite.
        /// <c>Label="([^"]*)"</c> stops at the FIRST inner quote, so
        /// <c>Label="@(x ? "Hide" : "Show")"</c> captured <c>@(x ? </c> — a string with no
        /// quotes left in it — and the literal extractor below then returned nothing, and the
        /// comparison loop ran zero times. The guard was green on the fix AND green on the
        /// original defect it names in its own comment: measured over the real component
        /// directory, 27 of 34 call sites were compared, 5 correctly skipped for having no
        /// AriaLabel, and 2 — IndicatorBar's two toggles, the entire population this rewrite
        /// existed for — asserted nothing at all.</para>
        ///
        /// <para>Razor's own rule is what makes this parseable: a quoted attribute value ends at
        /// the first quote that is not inside a balanced <c>@( … )</c> expression. So walk it.</para>
        /// </summary>
        private static string? AttributeValue(string tag, string attributeName)
        {
            var m = Regex.Match(tag, @"\b" + Regex.Escape(attributeName) + @"\s*=\s*""");
            if (!m.Success) return null;

            int i = m.Index + m.Length;
            int depth = 0;
            char? inLiteral = null;
            var sb = new System.Text.StringBuilder();
            for (; i < tag.Length; i++)
            {
                char c = tag[i];
                if (inLiteral is not null)
                {
                    if (c == inLiteral) inLiteral = null;
                    sb.Append(c);
                    continue;
                }
                if (depth > 0 && (c == '"' || c == '\'')) { inLiteral = c; sb.Append(c); continue; }
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == '"' && depth <= 0) return sb.ToString();
                sb.Append(c);
            }
            return null;   // unterminated — the caller treats a null as "not present"
        }

        /// <summary>
        /// The string values a Razor attribute can take. A plain value is itself; an expression
        /// contributes each of its double- or single-quoted literals, so
        /// <c>@(x ? "Hide" : "Show")</c> yields both arms and neither is skipped. An expression
        /// with no literals in it yields NOTHING, and the caller treats that as unguarded rather
        /// than as a pass.
        /// </summary>
        private static IReadOnlyList<string> Literals(string attributeValue)
        {
            string v = attributeValue.Trim();
            if (!v.Contains('@')) return v.Length == 0 ? Array.Empty<string>() : new[] { v };

            return Regex.Matches(v, "\"([^\"]*)\"|'([^']*)'")
                .Select(m => (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Trim())
                .Where(x => x.Length > 0)
                .ToList();
        }

        [Fact]
        public void ToolbarTooltips_nameTheKeyboardShortcutForThePanelOpeners()
        {
            // The toolbar is how a feature is DISCOVERED; the tooltip is how the keyboard user
            // learns the faster route. Pinning the six newest so the pattern isn't dropped.
            string toolbar = Toolbar();

            Assert.Contains("(Alt+M)", toolbar);                 // watchlist
            Assert.Contains("(Alt+R)", toolbar);                 // level respect report
            Assert.Contains("(Ctrl+Alt+Shift+J)", toolbar);      // journal
            Assert.Contains("(Ctrl+Alt+Shift+A)", toolbar);      // AI analyst
            Assert.Contains("(Ctrl+Alt+Shift+S)", toolbar);      // split view
            Assert.Contains("(Ctrl+Alt+Shift+P)", toolbar);      // bar replay
        }

        [Fact]
        public void TheToolbar_watchesTheStateItDisplays()
        {
            // Pan and Zoom disable on Store.State.Data.Count; Heatmap, Heikin and Log show pressed
            // state from the store's own flags. The toolbar read all of that without subscribing
            // to the store, so it only repainted when something unrelated happened to fire — pan
            // and zoom could sit greyed out over a chart full of data, and toggling Heikin from
            // the keyboard left the button showing the opposite of the truth. A control that lies
            // about its own state is worse than one that is missing.
            string toolbar = Toolbar();

            Assert.Contains("Store.StateStream.Subscribe", toolbar);
            Assert.Contains("_stateSub?.Dispose();", toolbar);
        }

        [Fact]
        public void EveryStoreBackedToolbarFlag_hasSomethingToRefreshIt()
        {
            // The specific flags that made this a bug, named so a future one is added with its
            // refresh path in mind rather than discovered in a screenshot.
            string toolbar = Toolbar();

            foreach (var flag in new[] { "HasChartData", "IsHeatmapVisible",
                                         "Store.State.IsHeikinAshi", "Store.State.IsLogScale" })
                Assert.True(toolbar.Contains(flag), $"{flag} is no longer present — update this test with what replaced it.");

            Assert.Contains("StateStream", toolbar);
        }

        [Fact]
        public void IconSprite_hasNoDuplicateSymbolIds()
        {
            // Two <symbol> elements with the same id makes the second unreachable — the button
            // silently keeps drawing the first one's glyph.
            var ids = Regex.Matches(Sprite(), @"<symbol id=""(icon-[a-z0-9\-]+)""")
                           .Select(m => m.Groups[1].Value).ToList();

            var dupes = ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

            Assert.True(dupes.Count == 0, "Duplicate sprite symbol ids: " + string.Join(", ", dupes));
        }
    }
}
