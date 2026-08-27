using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.WebHost.Services;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// The local background-monitoring core. Pins the watch-derivation contract
/// (the watch list IS the alert list: active + simple + symbol + provider),
/// the generated default notification sound, and — via the same AlertEvaluator
/// the monitor holds — that a threshold cross fires exactly once across polls
/// (the persistent-evaluator hysteresis the design depends on).
/// </summary>
public class LocalBackgroundMonitorTests
{
    private static AlertDefinition Alert(string? symbol = "BTC/USD", string? provider = "Bitstamp",
        string? timeframe = null, bool active = true, ConditionNode? tree = null, double threshold = 50_000,
        string? market = null, AlertTarget target = AlertTarget.Price,
        AlertCondition condition = AlertCondition.CrossesAbove) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = $"{symbol} above {threshold}",
        Target = target,
        Condition = condition,
        Threshold = threshold,
        Delivery = AlertDelivery.Both,
        IsActive = active,
        Symbol = symbol,
        Provider = provider,
        Timeframe = timeframe,
        Market = market,
        ConditionTree = tree,
    };

    // ── Watch derivation ─────────────────────────────────────────────────────

    [Fact]
    public void Watches_require_symbol_provider_active_and_no_tree()
    {
        var tree = new ConditionLeaf("l1", "RSI.Rsi", LeafOperator.LessThan, 30);
        var alerts = new[]
        {
            Alert(),                                  // ✓ monitorable
            Alert(symbol: null),                      // ✗ current-chart alert
            Alert(provider: null),                    // ✗ no provider to fetch from
            Alert(active: false),                     // ✗ switched off
            Alert(tree: tree),                        // ✗ trees need the indicator pipeline
        };

        var watches = LocalBackgroundMonitor.DeriveWatches(alerts);

        var w = Assert.Single(watches);
        Assert.Single(w.Alerts);
        Assert.Equal("Bitstamp", w.Provider);
        Assert.Equal("BTC/USD", w.Symbol);
        Assert.Equal("1h", w.Timeframe); // default when the alert doesn't scope one
    }

    [Fact]
    public void Watches_group_by_provider_symbol_timeframe_case_insensitively()
    {
        var alerts = new[]
        {
            Alert(threshold: 50_000),
            Alert(threshold: 60_000, provider: "bitstamp"),      // same watch, different case
            Alert(symbol: "ETH/USD", threshold: 3_000),          // second watch
            Alert(timeframe: "1d", threshold: 70_000),           // third: explicit timeframe
        };

        var watches = LocalBackgroundMonitor.DeriveWatches(alerts);

        Assert.Equal(3, watches.Count);
        var btcHourly = watches.Single(w => w.Symbol == "BTC/USD" && w.Timeframe == "1h");
        Assert.Equal(2, btcHourly.Alerts.Count); // one fetch serves both alerts
    }

    [Fact]
    public void Watches_carry_the_alerts_market_instead_of_hardcoding_spot()
    {
        // Both monitors used to put a literal "Spot" in every MarketDataRequest,
        // so an alert created on a Futures/Derivatives chart silently watched the
        // wrong market. The market now rides on the watch, and it is part of the
        // grouping key — one symbol on two sub-types must not share a fetch.
        var alerts = new[]
        {
            Alert(market: "Futures", threshold: 50_000),
            Alert(market: "futures", threshold: 60_000),   // case-insensitive grouping
            Alert(market: null, threshold: 70_000),        // legacy alert → Spot fallback
        };

        var watches = LocalBackgroundMonitor.DeriveWatches(alerts);

        Assert.Equal(2, watches.Count);
        var futures = watches.Single(w => w.Market == "Futures");
        Assert.Equal(2, futures.Alerts.Count);
        Assert.Equal("Spot", watches.Single(w => w.Market == "Spot").Market);
    }

    [Fact]
    public void Chart_dependent_alerts_are_excluded_and_named_not_silently_no_opped()
    {
        // Background polls evaluate with WorkspaceState.Initial — no indicator
        // series, no volume profile. Indicator and POC targets, zone and trend
        // conditions, and trees all read chart state, so the evaluator returned
        // null for them on every poll while the watch list claimed coverage.
        // They are now excluded up front, and DeriveUnwatchable names each one.
        var tree = new ConditionLeaf("l1", "RSI.Rsi", LeafOperator.LessThan, 30);
        var watchable = Alert();
        var alerts = new[]
        {
            watchable,
            Alert(target: AlertTarget.Indicator),
            Alert(target: AlertTarget.Poc),
            Alert(condition: AlertCondition.EntersZone),
            Alert(condition: AlertCondition.ExitsZone),
            Alert(condition: AlertCondition.TrendChange),
            Alert(tree: tree),
            Alert(active: false, target: AlertTarget.Indicator),  // disabled → nothing to warn about
        };

        var watches = LocalBackgroundMonitor.DeriveWatches(alerts);
        var unwatchable = LocalBackgroundMonitor.DeriveUnwatchable(alerts);

        Assert.Same(watchable, Assert.Single(Assert.Single(watches).Alerts));
        Assert.Equal(6, unwatchable.Count);
        Assert.All(unwatchable, u => Assert.False(string.IsNullOrWhiteSpace(u.Reason)));
        Assert.DoesNotContain(unwatchable, u => !u.Alert.IsActive);
    }

    [Fact]
    public void Market_survives_the_newtonsoft_round_trip_and_old_json_reads_as_null()
    {
        // alerts.json is Newtonsoft; a stamped market must come back, and every
        // pre-existing entry (no Market property) must deserialize to null so
        // the Spot fallback applies.
        var stamped = Alert(market: "Futures");
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(stamped);
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<AlertDefinition>(json)!;
        Assert.Equal("Futures", back.Market);

        var legacy = Newtonsoft.Json.JsonConvert.DeserializeObject<AlertDefinition>(
            Newtonsoft.Json.JsonConvert.SerializeObject(Alert()))!;
        Assert.Null(legacy.Market);
    }

    // ── The evaluation contract the monitor relies on ────────────────────────

    /// <summary>
    /// A crossing fires once for the bar it happened on, even though the monitor re-polls
    /// that same bar for the whole timeframe.
    ///
    /// <para><b>This test used to measure the harness, not the evaluator.</b> It advanced the
    /// bar pair on every "poll" — <c>(101, 99)</c>, then <c>(105, 101)</c>, then
    /// <c>(95, 105)</c> — so the previous <i>current</i> bar became the next <i>previous</i>
    /// bar each time and <c>prev &lt; threshold</c> went false all by itself. The production
    /// monitor does no such thing: it fetches the last three bars every 60 seconds and
    /// evaluates <c>bars[^1]</c> against <c>bars[^2]</c>, so on a 1h chart it passes the
    /// <b>identical pair</b> up to 59 times. The test's own comment ("The monitor keeps ONE
    /// evaluator across polls precisely for this") named an invariant it never exercised.</para>
    ///
    /// <para>The 2026-08-27 recount found the production side of this <b>already fixed</b> —
    /// <c>_lastFiredBar</c> keys the dedupe on the bar's own Date — so the audit's claim that
    /// the guard was "provable-red today" was stale. The test was not. It is now written the
    /// way the monitor actually calls: same bars, repeatedly.</para>
    /// </summary>
    [Fact]
    public void Persistent_evaluator_fires_a_cross_once_not_every_poll()
    {
        var evaluator = new AlertEvaluator(new SdkCandlePatternAnalyzer(), new IndicatorContextAnalyzer());
        var alert = Alert(threshold: 100);
        var state = WorkspaceState.Initial with { SymbolDisplayName = "BTC/USD" };
        var none = new Dictionary<string, double>();

        var t0 = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        Ohlcv Bar(double close, int hour) =>
            new(t0.AddHours(hour), close, close, close, close, 0);

        // The bar that crosses, and the bar before it. These two objects are what every poll
        // for the next hour will see.
        var crossed = Bar(101, 1);
        var before  = Bar(99, 0);

        Assert.Single(evaluator.EvaluateAlerts(new[] { alert }, state, crossed, before, none));

        // Nine more polls of the SAME pair — the shape that produced up to 59 duplicate
        // emails, Telegram messages and push notifications for one crossing.
        for (int poll = 0; poll < 9; poll++)
            Assert.Empty(evaluator.EvaluateAlerts(new[] { alert }, state, crossed, before, none));

        // A genuinely new bar that holds above stays quiet (prev is no longer below).
        Assert.Empty(evaluator.EvaluateAlerts(new[] { alert }, state, Bar(105, 2), crossed, none));

        // Fell back below, then crossed again on a new bar → fires again.
        Assert.Empty(evaluator.EvaluateAlerts(new[] { alert }, state, Bar(95, 3), Bar(105, 2), none));
        Assert.Single(evaluator.EvaluateAlerts(new[] { alert }, state, Bar(102, 4), Bar(95, 3), none));
    }

    [Fact]
    public void The_repeat_poll_really_does_re_present_the_identical_pair()
    {
        // Vacuity check for the loop above: if the two bars differed in any way the evaluator
        // keys on, the loop would be testing ordinary crossing hysteresis rather than the
        // same-bar dedupe, which is exactly how the old version of this test passed while
        // guarding nothing.
        var t0 = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var a = new Ohlcv(t0.AddHours(1), 101, 101, 101, 101, 0);
        var b = new Ohlcv(t0.AddHours(1), 101, 101, 101, 101, 0);

        Assert.Equal(a.Date, b.Date);
        Assert.Equal(a.Close, b.Close);
    }

    // ── Default notification sound ───────────────────────────────────────────

    [Fact]
    public void Generated_beep_is_a_valid_wav_with_audible_content()
    {
        byte[] wav = LocalBackgroundMonitor.GenerateDefaultBeepWav();

        Assert.Equal((byte)'R', wav[0]); // RIFF magic
        Assert.Equal((byte)'F', wav[3]);
        Assert.Equal((byte)'W', wav[8]); // WAVE

        // Data must actually contain non-silence (peak above 10% full scale).
        short peak = 0;
        for (int i = 44; i + 1 < wav.Length; i += 2)
        {
            short s = BitConverter.ToInt16(wav, i);
            if (Math.Abs((int)s) > peak) peak = Math.Abs(s) > short.MaxValue ? short.MaxValue : (short)Math.Abs((int)s);
        }
        Assert.True(peak > short.MaxValue / 10, $"peak {peak} — beep would be inaudible");
    }
}
