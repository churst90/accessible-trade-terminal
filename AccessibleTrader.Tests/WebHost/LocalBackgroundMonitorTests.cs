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

    [Fact]
    public void Persistent_evaluator_fires_a_cross_once_not_every_poll()
    {
        // The monitor keeps ONE evaluator across polls precisely for this:
        // price crosses the level → fire; price STAYS above on later polls →
        // silence, until it dips back below and crosses again.
        var evaluator = new AlertEvaluator(new SdkCandlePatternAnalyzer(), new IndicatorContextAnalyzer());
        var alert = Alert(threshold: 100);
        var state = WorkspaceState.Initial with { SymbolDisplayName = "BTC/USD" };
        var none = new Dictionary<string, double>();

        Ohlcv Bar(double close) => new(DateTime.UtcNow, close, close, close, close, 0);

        Assert.Single(evaluator.EvaluateAlerts(new[] { alert }, state, Bar(101), Bar(99), none));  // cross → fires
        Assert.Empty(evaluator.EvaluateAlerts(new[] { alert }, state, Bar(105), Bar(101), none));  // still above → quiet
        Assert.Empty(evaluator.EvaluateAlerts(new[] { alert }, state, Bar(95), Bar(105), none));   // fell back → re-arms
        Assert.Single(evaluator.EvaluateAlerts(new[] { alert }, state, Bar(102), Bar(95), none));  // crosses again → fires
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
