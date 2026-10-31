using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cipher B's adaptive overbought/oversold thresholds are not components (2026-10-17).</b>
///
/// <para>Cody's 2026-10-17 report: "the adaptive os/ob components, do I really need these? they're
/// hidden, don't the reference lines cover this already and they shouldn't be components with no
/// values?" They were two hidden Oscillator components. Hidden components are still keyboard stops
/// (Ctrl+Up/Down walks every component in the pane), so arrowing through Cipher B landed on
/// "Hidden. Adaptive OB …" and "Hidden. Adaptive OS …" — in the default Fixed threshold mode a
/// constant ±53 on every bar, the same number the ±53 reference lines already say. In Percentile
/// mode they were the thresholds the dots were gated on, so the numbers were kept as companion
/// arrays until 2026-10-31, when Percentile mode itself was removed (Market Cipher B's levels are
/// fixed).</para>
/// </summary>
public sealed class CipherBAdaptiveThresholdTests
{
    private readonly ITestOutputHelper _out;
    public CipherBAdaptiveThresholdTests(ITestOutputHelper output) => _out = output;

    private static readonly IndicatorModelFactory Factory = new(
        new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
        new MockIndicatorPreferencesService());

    /// <summary>Every component Cipher B declared on 2026-10-16, in order — what a chart saved
    /// before today carries.</summary>
    private static readonly string[] SavedBeforeToday =
    {
        "Anchor Wave", "Anchor Wave 2", "WT Histogram", "Wave Trend", "Wave Trend 2", "Money Flow Wave",
        "WaveTrend Cross Bull", "WaveTrend Cross Bear", "Adaptive OB", "Adaptive OS", "Anchor Polarity",
        "Oversold Crossover", "Overbought Crossover", "Triple Confluence Buy", "Bullish Divergence",
        "Bearish Divergence", "Hidden Bull Continuation", "Hidden Bear Continuation",
    };

    /// <summary>
    /// A Cipher B restored from a chart saved before today, through the real restore path
    /// (<see cref="WorkspaceInitializer.MigrateSeriesConfig"/> then
    /// <see cref="SeriesManagementService.MaterializeSaved"/>), holding the provider's own output on
    /// real-shaped bars.
    /// </summary>
    private static (ChartSeries Series, List<Ohlcv> Bars, IndicatorEngine Engine) RestoredCipherB(bool unhideAdaptive = false)
    {
        var providers = new List<IIndicatorProvider> { new CipherBProvider() };
        var indicators = new IndicatorService(providers, NullLogger<IndicatorService>.Instance);
        var engine = new IndicatorEngine(indicators, new CustomIndicatorRegistry(), providers);
        var meta = new CipherBProvider().GetIndicators();

        var saved = new SeriesConfig { Id = "cb-1", Name = "Cipher B", IndicatorCode = "CIPHER_B", Pane = "Pane_CIPHER_B" };
        foreach (var n in SavedBeforeToday)
            saved.Components.Add(new ComponentConfig
            {
                Name = n, DisplayName = n,
                // As declared then: the anchors and the two thresholds hidden. A user who un-hid the
                // thresholds saved them visible.
                IsVisible = n is "Adaptive OB" or "Adaptive OS" ? unhideAdaptive : !n.StartsWith("Anchor"),
                IsEnabled = true, Volume = 1f,
            });

        WorkspaceInitializer.MigrateSeriesConfig(saved, meta);
        var series = SeriesManagementService.MaterializeSaved(saved, meta[0], Factory,
            Array.Empty<IReadOnlyDictionary<string, object>>(), engine, new MockIndicatorPreferencesService());

        var bars = CausalityProbeSeries.Bars(CausalityProbeSeries.HourlyFlavours.First(), 600);
        var results = new Dictionary<string, double[]>();
        new CipherBProvider().Calculate("CIPHER_B", System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
            new Dictionary<string, object>(), new IndicatorResultBuffer(results, bars.Count));
        foreach (var kv in results) series.Data.ComponentData[kv.Key] = kv.Value;
        return (series, bars, engine);
    }

    /// <summary>What the chart says when Ctrl+Up/Down lands on component <paramref name="index"/>:
    /// the formatter the speech router uses, with the indicator engine (so the provider's own
    /// speech answers, as in the app).</summary>
    private static string Landing(ChartSeries series, List<Ohlcv> bars, IndicatorEngine engine, int index)
    {
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(series),
            PrimarySeriesId = series.Id,
            FocusedSeriesId = series.Id,
            FocusedComponentIndex = index,
            CurrentDataIndex = bars.Count - 1,
            ViewportStartIndex = bars.Count - 100,
            ViewportLength = 100,
            LastInteractionContext = InteractionContext.Component,
        };
        return new SpeechFormatter(NullLogger<SpeechFormatter>.Instance, engine)
            .FormatPointFeedback(state, isXMove: false, isYMove: true, series, bars[^1], "");
    }

    /// <summary>
    /// Every keyboard stop on a restored Cipher B, as spoken: none of them is an adaptive
    /// threshold. Hidden or not — Ctrl+Up/Down walks hidden components too
    /// (<c>CommandDispatcher.HandleIntraPaneNavigation</c>), and a saved chart where the user had
    /// un-hidden them must lose them as well.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void No_keyboard_stop_on_a_restored_Cipher_B_is_an_adaptive_threshold(bool unhidden)
    {
        var (series, bars, engine) = RestoredCipherB(unhidden);
        var said = Enumerable.Range(0, series.Components.Count)
            .Select(i => $"{series.Components[i].Name}: {Landing(series, bars, engine, i)}").ToList();
        foreach (var s in said) _out.WriteLine(s);

        var stops = said.Where(s => s.Contains("Adaptive", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(stops.Count == 0,
            $"Cipher B still has {stops.Count} adaptive-threshold stops:\n  {string.Join("\n  ", stops)}");
    }

    // ── The numbers are still there for the dots ────────────────────────────────────

    /// <summary>
    /// The thresholds themselves went with Percentile mode on 2026-10-31 (Cody: "remove all of that
    /// old code from cipher b"): Market Cipher B's levels are fixed, so the provider writes no
    /// threshold arrays and no old component names, and a leftover ThresholdMode does nothing.
    /// </summary>
    [Fact]
    public void No_threshold_arrays_are_written_and_the_old_mode_does_nothing()
    {
        var bars = CausalityProbeSeries.Bars(CausalityProbeSeries.HourlyFlavours.First(), 900);
        Dictionary<string, double[]> Run(Dictionary<string, object> p)
        {
            var results = new Dictionary<string, double[]>();
            new CipherBProvider().Calculate("CIPHER_B", System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
                p, new IndicatorResultBuffer(results, bars.Count));
            return results;
        }
        var plain = Run(new Dictionary<string, object>());
        var old = Run(new Dictionary<string, object> { ["ThresholdMode"] = "Percentile", ["AdaptiveLookback"] = 200 });
        Assert.DoesNotContain(plain.Keys, k => k.Contains("adaptive", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(plain.Keys.OrderBy(k => k), old.Keys.OrderBy(k => k));
        foreach (var k in plain.Keys)
            Assert.True(plain[k].Zip(old[k]).All(z => z.First.Equals(z.Second)), $"ThresholdMode changed '{k}'");
    }

    // ── A strategy or alert that named them is told so ──────────────────────────────

    private static readonly SignalCatalog Catalog = new(new IIndicatorProvider[] { new CipherBProvider() });

    /// <summary>
    /// A condition leaf on the retired signal is false — there is nothing to read — and the
    /// evaluator says why, which is what a tree alert announces once ("can't be fully evaluated and
    /// may stay silent: …"). Before, the descriptor lookup returned null and the leaf was false in
    /// silence for as long as the alert lived.
    /// </summary>
    [Theory]
    [InlineData("CIPHER_B.Adaptive OB")]
    [InlineData("CIPHER_B.Adaptive OS")]
    public void A_condition_on_a_removed_threshold_says_it_was_removed(string id)
    {
        var (series, bars, _) = RestoredCipherB();
        var state = WorkspaceState.Initial with { Data = new TimeSeriesBuffer<Ohlcv>(bars), ActiveSeries = ImmutableList.Create(series) };
        var eval = new ConditionEvaluator(Catalog);

        var result = eval.Evaluate(new ConditionLeaf("a", id, LeafOperator.LessThan, 0), bars, state);

        Assert.False(result.OverallTrue);
        Assert.NotNull(eval.LastDegradation);
        Assert.Contains("removed on 2026-10-17", eval.LastDegradation!);

        // Vacuity: a leaf on a signal Cipher B still has degrades nothing.
        eval.Evaluate(new ConditionLeaf("b", "CIPHER_B.Wave Trend", LeafOperator.LessThan, 0), bars, state);
        Assert.Null(eval.LastDegradation);
    }

    /// <summary>A running strategy with such a leaf says, once, that the condition can never be
    /// true — rather than running for ever without firing.</summary>
    [Fact]
    public void A_strategy_with_a_removed_threshold_says_so_once()
    {
        var (series, bars, _) = RestoredCipherB();
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars), ActiveSeries = ImmutableList.Create(series),
            Identity = new ChartIdentity("Spot", "binance", "BTC/USDT", "1h"),
        };
        var spec = new StrategySpec("spec.adaptive", "Old Cipher setup", "desc", OrderSide.Buy,
            new ConditionGroup("root", LogicOperator.And, new List<ConditionNode>
            {
                new ConditionLeaf("a", "CIPHER_B.Wave Trend", LeafOperator.LessThan, 0),
                new ConditionLeaf("b", "CIPHER_B.Adaptive OS", LeafOperator.GreaterThan, -60),
            }),
            new RiskPlan(
                Stop: new StopSource(StopSourceKind.PercentOfPrice, PercentValue: 1.0),
                TpLadder: Array.Empty<TpLadderRung>(),
                Sizing: new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005),
                Entry: new EntryTrigger(EntryTriggerKind.Immediate)),
            StrategyExecutionMode.Suggestion);
        var bus = new SpyEventBus();
        var strategy = new ConfigurableStrategy(spec, new ConditionEvaluator(Catalog), new NoRisk(), Catalog, bus,
            instanceId: "t", mtf: null);
        strategy.Initialize(bars, state, new Dictionary<string, object>());

        strategy.OnBar(bars[^1], bars, state);
        strategy.OnBar(bars[^1], bars, state);

        var said = bus.Log.OfType<FeedbackRequestEvent>()
            .Where(e => e.Message != null && e.Message.Contains("Adaptive OS")).ToList();
        var once = Assert.Single(said);
        Assert.Equal(FeedbackType.Error, once.Type);
        Assert.Contains("Old Cipher setup", once.Message);
    }

    private sealed class NoRisk : IRiskPlanResolver
    {
        public ResolvedRiskPlan? Resolve(RiskPlan plan, OrderSide side, IReadOnlyList<Ohlcv> history, WorkspaceState state, double? accountEquity = null) => null;
    }

    /// <summary>
    /// A plain alert set on the Adaptive OB component: the series is on the chart but the
    /// component is not. That returned null in silence; it now degrades once, naming it.
    /// </summary>
    [Fact]
    public void An_alert_on_a_removed_threshold_says_it_cannot_fire()
    {
        var (series, bars, _) = RestoredCipherB();
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars), ActiveSeries = ImmutableList.Create(series), CurrentDataIndex = bars.Count - 1,
        };
        var ev = new AlertEvaluator(new SdkCandlePatternAnalyzer(), new IndicatorContextAnalyzer());
        var said = new List<string>();
        ev.EvaluationDegraded += (_, why) => said.Add(why);
        var alert = new AlertDefinition
        {
            Id = "ob", Name = "ob", Delivery = AlertDelivery.Speech, Target = AlertTarget.Indicator,
            Condition = AlertCondition.CrossesAbove, Threshold = 0,
            IndicatorCode = "CIPHER_B", ComponentName = "Adaptive OB",
        };

        for (int i = 0; i < 2; i++)
            Assert.Empty(ev.EvaluateAlerts(new[] { alert }, state, bars[^1], bars[^2], new Dictionary<string, double>()));

        var why = Assert.Single(said);
        Assert.Contains("Adaptive OB", why);
        Assert.Contains("cannot fire", why);
    }
}
