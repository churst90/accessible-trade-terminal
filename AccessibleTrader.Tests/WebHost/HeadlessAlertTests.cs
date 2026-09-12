using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Core.Services.Workspace;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.WebHost.Services;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// <b>Phase 3 D4, the alert half: indicator, POC and condition-tree alerts evaluated with the
/// browser closed, driven through the real monitor poll.</b>
///
/// <para>
/// docs/BACKGROUND_MONITOR_PHASE3_SCOPE.md §1 F3 measured the defect: the monitor handed the
/// evaluator <c>WorkspaceState.Initial</c> — no <c>Data</c>, no <c>ActiveSeries</c>, a fresh
/// empty crossover memory every poll — and then REFUSED every alert that would have read them,
/// so four of its five "cannot watch this in the background" reasons described the monitor's
/// own blank state. The chart the narration ladder composes per symbol
/// (<c>HeadlessChart</c>) is exactly the state those alerts need. Each case below is what
/// actually reaches the desktop, with a browser circuit open and with none.
/// </para>
/// </summary>
[Collection("CircuitCoverage")]
public sealed class HeadlessAlertTests : IDisposable
{
    public void Dispose() => CircuitAlertCoverage.ResetForTests();

    // ── Fixtures ─────────────────────────────────────────────────────────────

    /// <summary>Price sits at 100 until hour 100, then steps to 200: an EMA 9 lands at 120 on
    /// the first bar after the step, an EMA 3 at 150.</summary>
    private static double Step(int h) => h < 100 ? 100 : 200;

    private static AlertDefinition Alert(string name, AlertTarget target, AlertCondition condition,
        double? threshold = null, string? indicator = null, string? component = null, ConditionNode? tree = null) => new()
    {
        Id = Guid.NewGuid().ToString(), Name = name, Target = target, Condition = condition,
        Threshold = threshold, IndicatorCode = indicator, ComponentName = component, ConditionTree = tree,
        Delivery = AlertDelivery.Both, IsActive = true,
        Symbol = "BTC/USD", Provider = "Bitstamp", Timeframe = "1h", Market = "Spot",
    };

    private static AlertDefinition EmaAbove(double threshold, string name = "EMA up") =>
        Alert(name, AlertTarget.Indicator, AlertCondition.CrossesAbove, threshold, indicator: "Ema", component: "Ema");

    private static SeriesConfig SavedEma(int period, bool narrated = false)
    {
        var cfg = new SeriesConfig
        {
            Id = $"ema{period}", IndicatorCode = "Ema", Name = "EMA", FriendlyName = $"EMA {period}",
            Pane = "Main", IsAutoNarrated = narrated, IsVisible = true,
        };
        cfg.Parameters["lookbackPeriods"] = period;
        return cfg;
    }

    private static SeriesConfig SavedVolumeProfile() => new()
    {
        Id = "vp", IndicatorCode = "VPVR", Name = "Volume Profile", FriendlyName = "Volume Profile",
        Pane = "Main", IsVisible = true,
    };

    private static IDisposable OpenCircuit(string id, params string[] symbols) =>
        CircuitAlertCoverage.Register(id, () => symbols);

    // ── Indicator alerts ─────────────────────────────────────────────────────

    [Fact]
    public async Task An_indicator_alert_fires_headless_ONCE_when_the_indicator_crosses()
    {
        var alert = EmaAbove(110);
        using var h = new HeadlessMonitorHarness(Array.Empty<SeriesConfig>(), alerts: new[] { alert }, priceAt: Step);
        h.OnlyAlerts();   // a 1-day floor on a 1-hour chart: no bar close, no ladder, alerts unaffected

        await h.PollAsync();                       // first sighting: EMA 100, no memory to cross from
        h.Nothing();

        h.CloseABar();                             // the bar at 200 opens; EMA 9 moves to 120
        await h.PollAsync();
        string one = h.Single();
        Assert.Contains("EMA up", one, StringComparison.Ordinal);
        Assert.Contains("crossed above", one, StringComparison.Ordinal);

        await h.PollAsync();                       // no bar closed; the same crossing must not re-fire
        h.Single();
    }

    /// <summary>
    /// The crossover memory is the whole test above: with a fresh empty dictionary every poll
    /// (the state until 2026-09-11) the previous value is NaN forever and nothing ever crosses.
    /// This case pins the other direction — a value that was already above the threshold when
    /// the monitor first saw it is NOT a crossing.
    /// </summary>
    [Fact]
    public async Task An_indicator_already_past_the_threshold_at_first_sight_does_not_fire()
    {
        var alert = EmaAbove(50);   // EMA is 100 from the start
        using var h = new HeadlessMonitorHarness(Array.Empty<SeriesConfig>(), alerts: new[] { alert }, priceAt: Step);
        h.OnlyAlerts();   // a 1-day floor on a 1-hour chart: no bar close, no ladder, alerts unaffected

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        h.Nothing();
    }

    [Fact]
    public async Task The_alerts_indicator_is_the_one_saved_on_the_tab_not_the_default()
    {
        // An EMA 3 lands at 150 after the step; the default EMA 9 at 120. A threshold of 140
        // therefore fires only if the saved tab's parameters were honoured.
        var alert = EmaAbove(140);
        using var h = new HeadlessMonitorHarness(new[] { SavedEma(3) }, alerts: new[] { alert }, priceAt: Step);
        h.OnlyAlerts();   // a 1-day floor on a 1-hour chart: no bar close, no ladder, alerts unaffected

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        Assert.Contains("EMA up", h.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_indicator_alert_on_a_symbol_a_browser_covers_is_the_browsers()
    {
        var alert = EmaAbove(110);
        using var h = new HeadlessMonitorHarness(Array.Empty<SeriesConfig>(), alerts: new[] { alert }, priceAt: Step);
        h.OnlyAlerts();   // a 1-day floor on a 1-hour chart: no bar close, no ladder, alerts unaffected
        using var _ = OpenCircuit("c1", "BTC/USD");

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        h.Nothing();
    }

    // ── Point-of-control alerts ──────────────────────────────────────────────

    /// <summary>Volume piles up between 100.5 and 100.7 for a hundred bars, then price steps
    /// to 105 — through the profile's point of control.</summary>
    private static double PocStep(int h) => h < 100 ? 100.5 + (Math.Abs(h) % 3) * 0.1 : 105;

    [Fact]
    public async Task A_poc_alert_fires_when_price_crosses_the_saved_profiles_point_of_control()
    {
        var alert = Alert("Through the POC", AlertTarget.Poc, AlertCondition.CrossesAbove);
        using var h = new HeadlessMonitorHarness(new[] { SavedVolumeProfile() }, alerts: new[] { alert }, priceAt: PocStep);
        h.OnlyAlerts();   // a 1-day floor on a 1-hour chart: no bar close, no ladder, alerts unaffected

        await h.PollAsync();
        h.Nothing();

        h.CloseABar();
        await h.PollAsync();
        var one = h.Single();
        Assert.Contains("Through the POC", one, StringComparison.Ordinal);
    }

    [Fact]
    public void A_poc_alert_with_no_profile_saved_for_its_chart_is_named_unwatchable()
    {
        var alert = Alert("Through the POC", AlertTarget.Poc, AlertCondition.CrossesAbove);

        // No saved tab at all.
        var reason = LocalBackgroundMonitor.WhyUnwatchable(alert, session: null);
        Assert.NotNull(reason);
        Assert.Contains("profile", reason, StringComparison.OrdinalIgnoreCase);

        // A saved tab on the symbol, with a profile on it.
        var withProfile = new WorkspaceConfiguration
        {
            Tabs = new List<TabConfiguration>
            {
                new() { Market = "Spot", Provider = "Bitstamp", Symbol = "BTC/USD", Timeframe = "5m",
                        Series = new List<SeriesConfig> { SavedVolumeProfile() } },
            },
        };
        Assert.Null(LocalBackgroundMonitor.WhyUnwatchable(alert, withProfile));

        // The same, without the profile.
        var withoutProfile = new WorkspaceConfiguration
        {
            Tabs = new List<TabConfiguration>
            {
                new() { Market = "Spot", Provider = "Bitstamp", Symbol = "BTC/USD", Timeframe = "1h",
                        Series = new List<SeriesConfig> { SavedEma(9) } },
            },
        };
        Assert.NotNull(LocalBackgroundMonitor.WhyUnwatchable(alert, withoutProfile));
    }

    // ── Condition-tree alerts ────────────────────────────────────────────────

    [Fact]
    public async Task A_condition_tree_alert_evaluates_headless_and_fires_on_its_first_true_bar()
    {
        var tree = new ConditionLeaf("l1", "Ema.Ema", LeafOperator.GreaterThan, 110);
        var alert = Alert("EMA tree", AlertTarget.Price, AlertCondition.CrossesAbove, tree: tree);
        using var h = new HeadlessMonitorHarness(Array.Empty<SeriesConfig>(), alerts: new[] { alert }, priceAt: Step);
        h.OnlyAlerts();   // a 1-day floor on a 1-hour chart: no bar close, no ladder, alerts unaffected

        await h.PollAsync();
        h.Nothing();

        h.CloseABar();
        await h.PollAsync();
        Assert.Contains("EMA tree", h.Single(), StringComparison.Ordinal);
    }

    // ── What cannot be watched is SAID ───────────────────────────────────────

    [Fact]
    public async Task An_alert_on_an_indicator_this_host_cannot_build_is_said_once_not_silently_skipped()
    {
        var alert = Alert("Ghost", AlertTarget.Indicator, AlertCondition.CrossesAbove, 1, indicator: "NOPE", component: "x");
        using var h = new HeadlessMonitorHarness(Array.Empty<SeriesConfig>(), alerts: new[] { alert }, priceAt: Step);
        h.OnlyAlerts();   // a 1-day floor on a 1-hour chart: no bar close, no ladder, alerts unaffected

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        var said = h.Single();
        Assert.Contains("NOPE", said, StringComparison.Ordinal);
        Assert.Contains("not available", said, StringComparison.Ordinal);
        Assert.Contains("Ghost", said, StringComparison.Ordinal);
    }

    // ── The refusal list, as it stands ───────────────────────────────────────

    [Fact]
    public void The_local_monitor_refuses_only_what_it_genuinely_cannot_fetch_or_read()
    {
        var tree = new ConditionLeaf("l1", "RSI.Rsi", LeafOperator.LessThan, 30);
        var indicator = Alert("i", AlertTarget.Indicator, AlertCondition.CrossesAbove, 70, "RSI", "Rsi");
        var zone = Alert("z", AlertTarget.Indicator, AlertCondition.EntersZone, indicator: "RSI", component: "Rsi") with { Zone = AlertZone.Overbought };
        var trend = Alert("t", AlertTarget.Indicator, AlertCondition.TrendChange, indicator: "RSI", component: "Rsi");
        var treeAlert = Alert("tree", AlertTarget.Price, AlertCondition.CrossesAbove, tree: tree);
        var poc = Alert("poc", AlertTarget.Poc, AlertCondition.CrossesAbove);
        var noSymbol = Alert("current chart", AlertTarget.Price, AlertCondition.CrossesAbove, 1) with { Symbol = null };

        var all = new[] { indicator, zone, trend, treeAlert, poc, noSymbol };

        // No saved session: the POC alert has no profile to read, and the current-chart alert
        // has nothing to fetch. Everything else is composed and watched.
        var unwatchable = LocalBackgroundMonitor.DeriveUnwatchable(all, session: null);
        Assert.Equal(new[] { "poc", "current chart" }, unwatchable.Select(u => u.Alert.Name).OrderBy(n => n == "poc" ? 0 : 1));

        // The hosted monitor evaluates against a blank chart and keeps the old list.
        var hosted = LocalBackgroundMonitor.DeriveUnwatchable(all, BackgroundWatchability.WhyUnwatchableWithoutAChart);
        Assert.Equal(6, hosted.Count);
    }
}
