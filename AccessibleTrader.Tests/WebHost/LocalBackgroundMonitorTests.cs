using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.WebHost.Services;
using AccessibleTrader.WebHost.Services.Tray;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

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
        // Moved with the rest of the delivery machinery when the presenter seam was extracted;
        // the monitor no longer generates or plays anything itself.
        byte[] wav = ProcessDesktopAlertPresenter.GenerateDefaultBeepWav();

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

    // ── Dead-feed escalation (N23's twin, 2026-08-29) ────────────────────────
    //
    // The hosted monitor's identical escalation was pinned by four cases after mutant N23
    // survived a green suite. This one was left alone because the class could not be built in a
    // test: its constructor probed the PATH for notify-send / gdbus / spd-say / paplay and every
    // delivery path called Process.Start. That is now behind IDesktopAlertPresenter, so the
    // policy is drivable and the process spawning is not in the test's way.
    //
    // It matters more here than in the hosted twin, not less: this is the monitor whose whole
    // reason to exist is that it can SPEAK to somebody sitting at the machine with the browser
    // closed. If the escalation breaks, a blind user's alerts stop being watched and the
    // application's only signal for that — the one it is uniquely able to give — never fires.

    /// <summary>Records what would have been played, toasted and spoken, spawning nothing.</summary>
    private sealed class SpyPresenter : IDesktopAlertPresenter
    {
        public readonly List<(string Title, string Text, bool Urgent)> Toasts = new();
        public readonly List<string> Spoken = new();
        public int SoundsPlayed;

        public string Describe() => "spy";
        public bool CanNotify => true;
        public void PlayNotificationSound() => SoundsPlayed++;
        public void Notify(string title, string text, bool urgent) => Toasts.Add((title, text, urgent));
        public void Speak(string text) => Spoken.Add(text);
    }

    private static LocalBackgroundMonitor Monitor(out SpyPresenter presenter)
    {
        presenter = new SpyPresenter();
        // The scope factory is never touched by the dead-feed path — it is reached from the
        // poll loop, and these tests drive the escalation directly.
        return new LocalBackgroundMonitor(
            Substitute.For<IServiceScopeFactory>(),
            new DemoPolicy(isDemo: false),
            new RecentAlertsBuffer(),
            new AlertSnooze(),
            presenter,
            NullLogger<LocalBackgroundMonitor>.Instance);
    }

    private static IReadOnlyList<string> Stopped(SpyPresenter p) =>
        p.Spoken.Where(s => s.Contains("stopped", StringComparison.OrdinalIgnoreCase)).ToList();

    private static IReadOnlyList<string> Resumed(SpyPresenter p) =>
        p.Spoken.Where(s => s.Contains("resumed", StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// Two consecutive failures are transient and stay silent; the third is spoken, and it is
    /// spoken ONCE. The counts are asserted as behaviour and the threshold constant is
    /// deliberately not referenced — reading it back would make this test agree with whatever
    /// value the constant took, which is the shape that left the bound untested to begin with.
    /// </summary>
    [Fact]
    public void A_dead_feed_is_spoken_on_the_third_consecutive_failure_and_only_once()
    {
        var monitor = Monitor(out var presenter);

        monitor.NoteFeedFailure("BTC/USD", "Bitstamp");
        Assert.Empty(Stopped(presenter));

        monitor.NoteFeedFailure("BTC/USD", "Bitstamp");
        Assert.Empty(Stopped(presenter));

        monitor.NoteFeedFailure("BTC/USD", "Bitstamp");
        string said = Assert.Single(Stopped(presenter));
        Assert.Contains("BTC/USD", said);
        Assert.Contains("Bitstamp", said);

        // The toast goes out with it, at critical urgency: this is the monitor reporting that
        // it can no longer do its job, not an alert firing.
        var toast = Assert.Single(presenter.Toasts);
        Assert.True(toast.Urgent);
        Assert.Equal("Alert monitoring", toast.Title);

        // Still once, five polls later — a warning repeated every minute trains a user to
        // ignore it, which is the same outcome as never saying it.
        for (int i = 0; i < 5; i++) monitor.NoteFeedFailure("BTC/USD", "Bitstamp");
        Assert.Single(Stopped(presenter));
        Assert.Single(presenter.Toasts);

        // And nothing was filed as an alert: no sound, because a dead feed is not a fill.
        Assert.Equal(0, presenter.SoundsPlayed);
    }

    /// <summary>
    /// The counter is CONSECUTIVE, so a good poll resets it — two failures either side of a
    /// success must not add up to a report.
    /// </summary>
    [Fact]
    public void Recovery_resets_the_counter_so_scattered_failures_never_accumulate()
    {
        var monitor = Monitor(out var presenter);

        monitor.NoteFeedFailure("BTC/USD", "Bitstamp");
        monitor.NoteFeedFailure("BTC/USD", "Bitstamp");
        monitor.NoteFeedRecovered("BTC/USD");
        monitor.NoteFeedFailure("BTC/USD", "Bitstamp");
        monitor.NoteFeedFailure("BTC/USD", "Bitstamp");

        Assert.Empty(Stopped(presenter));

        // The third after the reset does report, which proves the silence above is the reset
        // working rather than the escalation being broken outright.
        monitor.NoteFeedFailure("BTC/USD", "Bitstamp");
        Assert.Single(Stopped(presenter));
    }

    /// <summary>
    /// Recovery is spoken, but only when the failure was — this is the half the hosted monitor
    /// deliberately does not have. A user who heard "alerts on this symbol are not being
    /// watched" has no other way to learn they are live again; a user who heard nothing must
    /// not be told a feed recovered from a failure they were never told about.
    /// </summary>
    [Fact]
    public void Recovery_is_announced_only_when_the_failure_was()
    {
        var monitor = Monitor(out var presenter);

        // An ordinary good poll on a healthy feed says nothing at all.
        monitor.NoteFeedRecovered("BTC/USD");
        monitor.NoteFeedFailure("BTC/USD", "Bitstamp");
        monitor.NoteFeedRecovered("BTC/USD");
        Assert.Empty(presenter.Spoken);

        // After a reported failure, it does.
        for (int i = 0; i < 3; i++) monitor.NoteFeedFailure("BTC/USD", "Bitstamp");
        Assert.Single(Stopped(presenter));

        monitor.NoteFeedRecovered("BTC/USD");
        Assert.Contains("BTC/USD", Assert.Single(Resumed(presenter)));

        // Said once: a second good poll is not more news.
        monitor.NoteFeedRecovered("BTC/USD");
        Assert.Single(Resumed(presenter));
    }

    /// <summary>
    /// The count is per symbol. Three failures spread over three feeds are three transient
    /// blips, not one dead feed, and reporting them would be a false alarm about a symbol that
    /// has failed once.
    /// </summary>
    [Fact]
    public void Failures_do_not_pool_across_symbols()
    {
        var monitor = Monitor(out var presenter);

        monitor.NoteFeedFailure("BTC/USD", "Bitstamp");
        monitor.NoteFeedFailure("ETH/USD", "Bitstamp");
        monitor.NoteFeedFailure("XRP/USD", "Bitstamp");

        Assert.Empty(Stopped(presenter));
    }

    /// <summary>
    /// The seam stays a seam. Every test above exists because the monitor no longer probes the
    /// PATH or starts processes itself — put either back and the class becomes unconstructible
    /// in a test again, which is precisely how its escalation went untested while the hosted
    /// twin's was pinned. The delivery belongs in <see cref="IDesktopAlertPresenter"/>.
    /// </summary>
    [Fact]
    public void The_monitor_itself_neither_probes_the_path_nor_starts_processes()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        string source = File.ReadAllText(Path.Combine(
            dir!.FullName, "AccessibleTrader.WebHost", "Services", "LocalBackgroundMonitor.cs"));

        // Vacuity floor: the right file, and one that still delivers something.
        Assert.Contains("class LocalBackgroundMonitor", source, StringComparison.Ordinal);
        Assert.Contains("_presenter.Speak", source, StringComparison.Ordinal);

        Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FindOnPath", source, StringComparison.Ordinal);
    }
}
