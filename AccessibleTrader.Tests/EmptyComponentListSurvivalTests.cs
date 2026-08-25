using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Workspace.Reducers;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// A focused series with ZERO components must not take the terminal's voice away.
    ///
    /// <para>
    /// <c>Math.Clamp(index, 0, s.Components.Count - 1)</c> throws when the list is empty — the
    /// upper bound is -1 and <c>Math.Clamp</c> rejects <c>min &gt; max</c>. Written as a defence,
    /// it was the crash. And these sites live inside EventBus subscribers, where an exception is
    /// not a logged blip: Rx's <c>AutoDetachObserver</c> disposes a throwing observer, so the
    /// subscription is gone and every keypress afterwards is silent — with nothing announcing
    /// that speech has died. The user's only recovery is restarting the app.
    /// </para>
    ///
    /// <para>
    /// A zero-component series is ordinary: an indicator whose provider returned nothing, or one
    /// focused mid-load. <c>AccessibilityFeedbackCoordinator</c> already had a
    /// <c>Components.Count &gt; 0</c> guard for exactly that — one line BELOW the clamp that had
    /// already thrown.
    /// </para>
    /// </summary>
    public class EmptyComponentListSurvivalTests
    {
        // ── Builders ─────────────────────────────────────────────────────────

        private static ChartSeries EmptySeries(string id = "ghost") =>
            new(new SeriesConfig { Id = id, IndicatorCode = "GHOST", Name = "Ghost", FriendlyName = "Ghost" },
                new SeriesDataBuffer { SeriesId = id });

        private static ChartSeries PopulatedSeries(string id = "candles")
        {
            var config = new SeriesConfig { Id = id, IndicatorCode = "candles", Name = "Price", FriendlyName = "Price" };
            var data = new SeriesDataBuffer { SeriesId = id };
            var body = new ComponentConfig { Name = "Body", DisplayName = "Body", IsVisible = true };
            config.Components.Add(body);
            data.ComponentData[body.Name] = new double[] { 1, 2, 3 };
            return new ChartSeries(config, data);
        }

        private static WorkspaceState StateFocusedOn(ChartSeries series, int componentIndex = 0)
        {
            var bars = Enumerable.Range(0, 3)
                .Select(i => new Ohlcv(new DateTime(2026, 1, 1).AddMinutes(i), 100 + i, 101 + i, 99 + i, 100 + i, 10))
                .ToList();
            return WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Spot", "binance", "BTC/USDT", "5m"),
                Data = new TimeSeriesBuffer<Ohlcv>(bars),
                ActiveSeries = ImmutableList.Create(series),
                FocusedSeriesId = series.Id,
                PrimarySeriesId = series.Id,
                CurrentDataIndex = 2,
                FocusedComponentIndex = componentIndex,
                InitStatus = InitializationStatus.Ready,
                LastInteractionContext = InteractionContext.Component,
            };
        }

        // ── The helper's own contract ────────────────────────────────────────

        [Fact]
        public void ClampComponent_ReturnsMinusOneForAnEmptySeries_AndClampsOtherwise()
        {
            // The pair. A helper that always returned -1 would satisfy the survival tests below
            // while silently disabling every component read in the app.
            Assert.Equal(-1, EmptySeries().ClampComponent(0));
            Assert.Equal(-1, EmptySeries().ClampComponent(7));

            var populated = PopulatedSeries();
            Assert.Equal(0, populated.ClampComponent(0));
            Assert.Equal(0, populated.ClampComponent(9));    // clamped down to the only component
            Assert.Equal(0, populated.ClampComponent(-4));   // and up from below
        }

        // ── The damage: a torn-down subscription, not a logged exception ─────

        /// <summary>
        /// The one that describes what the user experiences. Shift+F1 on a zero-component series
        /// threw inside the coordinator's <c>FeedbackRequestEvent</c> subscriber; Rx disposed the
        /// subscriber; the NEXT event — any event, on any key — was never delivered.
        /// </summary>
        [Fact]
        public void ContextSummaryOnAnEmptySeries_LeavesTheFeedbackSubscriptionAlive()
        {
            var bus = new SpyEventBus();
            var speech = new SpySpeechRouter();
            var store = new MockWorkspaceStore();
            store.EmitState(StateFocusedOn(EmptySeries()));

            var coordinator = new AccessibilityFeedbackCoordinator(
                store, new MockNavManager(), speech, new MockAudioRouter(), new SpeechFormatter(),
                bus, new MockEarconService(), new SdkCandlePatternAnalyzer(),
                new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
                new ChartPatternFocus(), new MockAutoNarrationService());
            Assert.NotNull(coordinator);

            bus.Publish(new FeedbackRequestEvent(FeedbackType.Info, "CONTEXT_SUMMARY"));
            int afterSummary = speech.SpeakCallCount;

            // The orientation key itself must have answered…
            Assert.True(afterSummary > 0,
                "CONTEXT_SUMMARY produced no speech on a zero-component series.");

            // …and, the part that actually mattered, the terminal must still be listening.
            bus.Publish(new FeedbackRequestEvent(FeedbackType.Info, "Still here."));
            Assert.True(speech.SpeakCallCount > afterSummary,
                "The FeedbackRequestEvent subscription was torn down — every later keypress is "
                + "silent, with nothing said about it. That is the real cost of the throw.");
        }

        // ── Each site, called directly ──────────────────────────────────────

        [Fact]
        public void NavigationFeedback_SurvivesAnEmptySeries_OnBothAxes()
        {
            var mgr = new NavigationFeedbackManager(new SpySpeechRouter(), new SpeechFormatter()) { IsSpeechEnabled = true };

            var state = StateFocusedOn(EmptySeries());

            // X move: the additional-signals branch. Y move: the sub-pane transition branch,
            // which needs a previous state, so feed one in first.
            mgr.HandleNavigationFeedback(state, isXMove: true, isYMove: false, prefixMessage: "");
            mgr.HandleNavigationFeedback(state, isXMove: false, isYMove: true, prefixMessage: "");
        }

        [Fact]
        public void SeriesReducer_SurvivesAnEmptySeries_InComponentContext()
        {
            // This one throws inside Dispatch rather than inside a subscriber, so it takes the
            // whole action down instead of the speech channel — a different symptom, same line.
            var state = StateFocusedOn(EmptySeries());

            var next = SeriesReducer.Reduce(state, new ToggleMuteAction("ghost", null), new SpyEventBus());

            Assert.NotNull(next);
        }

        [Fact]
        public void NavigationSonifier_SurvivesAnEmptySeries()
        {
            var sonifier = new NavigationSonifier(
                Substitute.For<IAudioDriver>(),
                Substitute.For<ISonificationStrategy>(),
                new SoundPatchRegistry());

            sonifier.SyncNavigationSlots(StateFocusedOn(EmptySeries()));
        }

        // ── The ratchet ─────────────────────────────────────────────────────

        /// <summary>
        /// Nothing in the shipping tree may clamp against <c>Components.Count - 1</c> again.
        /// <c>ChartSeries.ClampComponent</c> is the only way in, and it answers -1 instead of
        /// throwing.
        ///
        /// <para>
        /// The floor is on the POPULATION (how many sites go through the helper), never on the
        /// violation count. A floor on violations shrinks every time someone fixes one, so the
        /// guard goes red for doing its job — a lesson this repo learned writing
        /// <c>BunitAsyncSettleGuardTests</c>.
        /// </para>
        /// </summary>
        [Fact]
        public void NoProductionSourceClampsAgainstAnEmptyComponentList()
        {
            var files = StrategyLibraryPolicyTests.ShippingProjectDirectories()
                .SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories))
                .Where(f => f.EndsWith(".cs") || f.EndsWith(".razor"))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .ToArray();

            var offenders = new List<string>();
            int helperUses = 0;
            foreach (var file in files)
            {
                var code = PipelineIdentityAndResilienceTests.StripCommentsAndStrings(File.ReadAllText(file));
                helperUses += System.Text.RegularExpressions.Regex.Matches(code, @"ClampComponent\s*\(").Count;
                if (code.Contains("Components.Count - 1") || code.Contains("Components.Count-1"))
                    offenders.Add(Path.GetFileName(file));
            }

            Assert.True(helperUses >= 12,
                $"only {helperUses} ClampComponent call sites found; the scan is not seeing the "
                + "tree, or the helper was bypassed wholesale. Fix the discovery — do not lower "
                + "this floor.");

            Assert.True(offenders.Count == 0,
                "These files index a component list by clamping against Components.Count - 1, "
                + "which throws on an empty list and — inside an EventBus subscriber — takes the "
                + "subscription with it, silencing the terminal for the rest of the session. Use "
                + "series.ClampComponent(index) and handle the -1:\n  "
                + string.Join("\n  ", offenders.Distinct().OrderBy(f => f, StringComparer.Ordinal)));
        }
    }
}
