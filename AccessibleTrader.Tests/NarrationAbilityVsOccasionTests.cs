using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>Cody's five, 2026-09-11 — the narration coherence pass.</b>
    ///
    /// <para>Four of the five turned out to be one idea: <b>the terminal was answering "there is
    /// nothing here for you" with either silence or an alarm, when the right answer is a
    /// sentence.</b> Narration on a Volume histogram was silence. Pressing 0 on a pane with no
    /// neutral was an alarm — and an alarm that deliberately ignores the earcon mute. Neither is
    /// a failure; both are ordinary facts about the chart, and both are now said.</para>
    /// </summary>
    public class NarrationAbilityVsOccasionTests
    {
        /// <summary>An empty settings store — what a fresh install reads.</summary>
        private sealed class EmptySettings : ISettingsManager
        {
            private readonly Dictionary<string, Newtonsoft.Json.Linq.JToken> _store = new();
            public Newtonsoft.Json.Linq.JToken? GetSetting(string keyPath, Newtonsoft.Json.Linq.JToken? defaultValue = null)
                => _store.TryGetValue(keyPath, out var v) ? v : defaultValue;
            public void SetSetting(string keyPath, Newtonsoft.Json.Linq.JToken value) => _store[keyPath] = value;
            public Newtonsoft.Json.Linq.JObject GetEffectiveSettingsForSeries(string seriesId) => new();
            public void SaveSettings() { }
            public void ResetToDefaults() => _store.Clear();
        }

        private static ComponentConfig Comp(string name, ComponentDisplayType dt) =>
            new() { Name = name, DisplayName = name, DisplayType = dt, IsVisible = true };

        private static ChartSeries Series(string id, string pane, params ComponentConfig[] comps)
        {
            var config = new SeriesConfig { Id = id, IndicatorCode = id, Name = id, Pane = pane };
            foreach (var c in comps) config.Components.Add(c);
            return new ChartSeries(config, new SeriesDataBuffer { SeriesId = id });
        }

        // ── Narratability: "narrating" is a promise ──────────────────────────
        //
        // Until 2026-09-11 these modelled Volume as a HISTOGRAM. The real Volume component is a
        // BAR (CoreIndicatorProvider, DisplayType = ComponentDisplayType.Bar), and the narrator's
        // PrimaryReading accepted only Line and Histogram — so "press 0 and its crossings will
        // speak" was advice a fixture had proved and production could not keep. Match production.

        [Fact]
        public void AVolumeBar_narratesByReadingItsValueAtEachClose()
        {
            // Cody: "pressing n should enable narration which is spoken on new bar closes as part
            // of the indicator ladder ... I may be doing dishes but still want to keep an ear on
            // the volume." A bar is a quantity per bar; its value at the close IS the news.
            var volume = Series("Volume", "Volume", Comp("Volume", ComponentDisplayType.Bar));

            var reading = SeriesNarrationScope.ReadingComponent(volume);

            Assert.NotNull(reading);
            Assert.Equal("Volume", reading!.Name);
            // ...so there is nothing to apologise for.
            Assert.Null(SeriesNarrationScope.WhyNothingToNarrate(volume));
        }

        [Fact]
        public void AHistogram_readsToo_andALineDoesNot()
        {
            Assert.NotNull(SeriesNarrationScope.ReadingComponent(
                Series("Delta", "Delta", Comp("Delta", ComponentDisplayType.Histogram))));
            // A line is a level; the news about a level is what crossed it.
            Assert.Null(SeriesNarrationScope.ReadingComponent(
                Series("OBV", "OBV", Comp("OBV", ComponentDisplayType.Line))));
        }

        [Fact]
        public void ASeriesWithSignals_doesNotAlsoReadItsHistogram()
        {
            // Cipher B prints a WT histogram next to its dots. A running value under a signal
            // ladder is the wall of speech the tiers exist to prevent: signals win, reading off.
            var cipher = Series("CipherB", "CipherB",
                Comp("WT Histogram", ComponentDisplayType.Histogram),
                Comp("Buy", ComponentDisplayType.Dot));

            Assert.Null(SeriesNarrationScope.ReadingComponent(cipher));
        }

        [Fact]
        public void TheNSelection_isHonouredByTheReading()
        {
            var s = Series("Two", "Two",
                Comp("A", ComponentDisplayType.Bar),
                Comp("B", ComponentDisplayType.Bar));
            s.Components[1].IsAutoNarrated = true;   // N on B only

            Assert.Equal("B", SeriesNarrationScope.ReadingComponent(s)!.Name);

            s.Components[1].IsVisible = false;        // ...and a hidden one is not read
            Assert.Null(SeriesNarrationScope.ReadingComponent(s));
        }

        [Fact]
        public void APlainLineOffThePricePane_reportsThatItHasNothingToNarrate_andNamesTheWayOut()
        {
            // The case that remains a dead switch: a line in its own pane with no level to cross.
            var obv = Series("OBV", "OBV", Comp("OBV", ComponentDisplayType.Line));

            string? why = SeriesNarrationScope.WhyNothingToNarrate(obv);

            Assert.NotNull(why);
            Assert.Contains("reference level", why, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TheSameLine_withAReferenceLevel_hasSomethingToNarrate()
        {
            // The negative half, and the one that proves the sentence is not simply always
            // returned. Level crossings DO narrate off the price pane — so the advice the
            // message gives actually works.
            var obv = Series("OBV", "OBV", Comp("OBV", ComponentDisplayType.Line));
            obv.Levels.Add(new LevelConfig { Name = "Level 1", Value = 1000, IsVisible = true });

            Assert.Null(SeriesNarrationScope.WhyNothingToNarrate(obv));
        }

        [Fact]
        public void AnIndicatorWithSignalMarkers_hasSomethingToNarrate()
        {
            var cipher = Series("CipherB", "CipherB",
                Comp("WT1", ComponentDisplayType.Oscillator),
                Comp("Buy", ComponentDisplayType.Dot));

            Assert.Null(SeriesNarrationScope.WhyNothingToNarrate(cipher));
        }

        [Fact]
        public void AnOverlayOnThePricePane_hasSomethingToNarrate()
        {
            // A moving average crosses PRICE, which always exists — so an overlay is narratable
            // even with no levels and no markers.
            var ema = Series("EMA200", "Main", Comp("EMA", ComponentDisplayType.Line));

            Assert.Null(SeriesNarrationScope.WhyNothingToNarrate(ema));
        }

        [Fact]
        public void ASeriesWithNoReadableComponent_isNotAdvisedToAddALevel()
        {
            // A level crossing needs a value to compare with the level. A pane holding only a
            // cloud has none, so "press 0" there would be advice that does not work — the exact
            // defect the Bar/Histogram mismatch above was.
            var cloud = Series("Cloud", "Cloud", Comp("Cloud", ComponentDisplayType.Cloud));

            string? why = SeriesNarrationScope.WhyNothingToNarrate(cloud);

            Assert.NotNull(why);
            Assert.DoesNotContain("reference level", why, StringComparison.OrdinalIgnoreCase);
        }

        // ── The 0 key: a refusal is not a failure ────────────────────────────

        [Fact]
        public void APaneWithNoNeutral_refusesTheZeroKey_withAReasonAndNoPlacement()
        {
            var level = ReferenceLevelPlacement.For(
                "SomeOscillator", cursorPrice: double.NaN, existing: null, paneNeutral: null,
                out string reason, out bool isRefusal);

            Assert.Null(level);
            Assert.True(isRefusal);
            Assert.Contains("neutral", reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ThatRefusal_isSpokenAsInformation_notAsAnError()
        {
            // THE POINT OF THE CHANGE, driven through the REAL dispatcher rather than by
            // re-deriving its rule here. Error earcons deliberately ignore Shift+F3 (the
            // silent-failure rule), so classifying "this pane has no neutral" as an error made a
            // routine "not applicable here" the one sound a user could not mute — on every pane
            // they explored. Nothing failed; the sentence explains it completely.
            var bus = new SpyEventBus();
            var store = new MockWorkspaceStore();
            // An oscillator pane whose component declares NO ReferenceLevel and which carries no
            // Neutral level: the one branch that sets isRefusal.
            var osc = Series("RSIish", "RSIish", Comp("Value", ComponentDisplayType.Oscillator));
            store.EmitState(WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(new List<Ohlcv>
                {
                    new(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), 100, 101, 99, 100, 1),
                }),
                ActiveSeries = System.Collections.Immutable.ImmutableList.Create(osc),
                PrimarySeriesId = osc.Id,
                FocusedSeriesId = osc.Id,
                FocusedComponentIndex = 0,
                CurrentDataIndex = 0,
            });

            var dispatcher = new CommandDispatcher(bus, Substitute.For<INavigationEngine>(), store,
                Substitute.For<IBarDetailService>(), new IndicatorCrossingEngine(store, bus));
            // 0 is chart-scoped; without focus the dispatcher drops it before the handler runs
            // and this test would pass for the wrong reason.
            dispatcher.SetChartActive(true);

            dispatcher.Dispatch(SystemCommand.AddReferenceLevel);

            var refusal = Assert.Single(bus.Log.OfType<FeedbackRequestEvent>());
            Assert.Contains("neutral", refusal.Message ?? "", StringComparison.OrdinalIgnoreCase);
            // Boundary, not Error: the key was understood and has nowhere to go. Error is the one
            // classification that pierces Shift+F3, and a routine "not applicable on this pane"
            // must not be the sound a user cannot mute.
            Assert.Equal(FeedbackType.Boundary, refusal.Type);
        }
            // ── Ability vs occasion: the two new Narration switches ─────────────

        [Fact]
        public void TheFormingPatternDefaults_matchWhatShipped()
        {
            // Candles ON because that is the behaviour that already existed and this change must
            // not remove it; charts OFF because it is a NEW occasion for the terminal to speak,
            // and the standing rule here is that continuous speech is asked for, not imposed.
            // Asserted against a real AppSettings over an empty store, which is what a fresh
            // install actually reads.
            var settings = new AppSettings(new EmptySettings());

            Assert.True(settings.NarrateFormingCandlePatterns);
            Assert.False(settings.NarrateFormingChartPatterns);
        }

        [Fact]
        public void TheFormingSwitchesRoundTrip()
        {
            var settings = new AppSettings(new EmptySettings());

            settings.NarrateFormingCandlePatterns = false;
            settings.NarrateFormingChartPatterns = true;

            Assert.False(settings.NarrateFormingCandlePatterns);
            Assert.True(settings.NarrateFormingChartPatterns);
        }


        // ── The retired chord ───────────────────────────────────────────────

        [Fact]
        public void NarrationHasExactlyOneBinding_andItIsN()
        {
            // Cody, 2026-09-11: remove Ctrl+Alt+Shift+N, N is the narration toggle now. Two
            // bindings for one command is two rows in Help and two things to remember; the chord
            // was kept for one release as "the one that works with focus outside the chart",
            // which is a case where the user cannot see which series they are toggling either.
            var manager = new ShortcutManager(new TempPaths());
            var bindings = manager.CurrentProfile!.Shortcuts
                .Where(b => b.Command == SystemCommand.ToggleNarration)
                .ToList();

            var only = Assert.Single(bindings);
            Assert.Equal("N", only.Key, ignoreCase: true);
            Assert.False(only.Ctrl);
            Assert.False(only.Alt);
            Assert.False(only.Shift);
        }

        /// <summary>An empty app-data dir, so this machine's own shortcuts.json cannot mask the
        /// default profile — the same precaution ShortcutConflictTests takes.</summary>
        private sealed class TempPaths : IPlatformPathService
        {
            public TempPaths()
            {
                AppDataDirectory = TestTemp.NewDir("at-narration-shortcut-");
                CacheDirectory = AppDataDirectory;
            }
            public string AppDataDirectory { get; }
            public string CacheDirectory { get; }
        }
}
}
