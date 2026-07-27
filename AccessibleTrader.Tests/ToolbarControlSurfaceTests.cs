using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

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
        public void EveryToolbarIconButton_carriesAnAccessibleName()
        {
            // Label is the visible text; AriaLabel is what a screen reader announces. Buttons in
            // this app are icon-plus-label, and the label is often an abbreviation ("Watch", "AI"),
            // so the aria-label is the only place the full name lives.
            var offenders = new List<string>();

            foreach (var file in Directory.EnumerateFiles(ComponentsDir(), "*.razor", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                // Each button spans several lines; split on the tag and take up to the closing "/>".
                foreach (var chunk in text.Split("<ToolbarIconButton").Skip(1))
                {
                    int end = chunk.IndexOf("/>", StringComparison.Ordinal);
                    string button = end > 0 ? chunk[..end] : chunk;
                    if (!button.Contains("AriaLabel="))
                    {
                        var icon = Regex.Match(button, @"Icon=""([a-z0-9\-]+)""");
                        offenders.Add($"{Path.GetFileName(file)}: button '{(icon.Success ? icon.Groups[1].Value : "?")}'");
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                "Toolbar buttons without an AriaLabel:\n  " + string.Join("\n  ", offenders));
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
