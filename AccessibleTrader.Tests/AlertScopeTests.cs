using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Core.Services.Workspace;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Mocks;
using AccessibleTrader.WebHost.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>An alert fires on the chart it was set on, and nowhere else</b> (Cody, 2026-11-22): "it
    /// kept firing over and over when the price hadn't even been near that price ... they don't
    /// need to fire on all workspaces, just the one it was set on". He had six BTCUSDT tabs open
    /// (1d, 4h, 1h, 15m, 1w, 1M), all Heikin-Ashi, with background tabs monitored every ten
    /// seconds, and set "price touches 61,450" on one of them. See <see cref="AlertScope"/>.
    /// </summary>
    public class AlertScopeTests
    {
        private static AlertDefinition Touch(string id, double level, string? timeframe = "4h",
            string? provider = "Bitstamp", string? symbol = "BTCUSDT") => new()
        {
            Id = id, Name = id, Delivery = AlertDelivery.Speech,
            Target = AlertTarget.Price, Condition = AlertCondition.Touches, Threshold = level,
            Symbol = symbol, Provider = provider, Timeframe = timeframe, Market = "Crypto",
        };

        private static ChartIdentity Chart(string timeframe, string provider = "Bitstamp") =>
            new("Crypto", provider, "BTCUSDT", timeframe);

        // ── The rule ─────────────────────────────────────────────────────────────

        [Fact]
        public void An_alert_applies_to_its_own_chart_only()
        {
            var a = Touch("a", 61450);
            Assert.True(AlertScope.AppliesTo(a, "BTCUSDT", Chart("4h")));
            Assert.False(AlertScope.AppliesTo(a, "BTCUSDT", Chart("1d")));
            Assert.False(AlertScope.AppliesTo(a, "BTCUSDT", Chart("4h", "Kraken")));
            Assert.False(AlertScope.AppliesTo(a, "ETHUSDT", new ChartIdentity("Crypto", "Bitstamp", "ETHUSDT", "4h")));
        }

        [Fact]
        public void An_alert_that_records_no_timeframe_or_provider_still_applies_to_any_chart_of_its_symbol()
        {
            var legacy = Touch("old", 61450, timeframe: null, provider: null);
            Assert.True(AlertScope.AppliesTo(legacy, "BTCUSDT", Chart("1d")));
            Assert.True(AlertScope.AppliesTo(legacy, "BTCUSDT", Chart("15m", "Kraken")));
            Assert.True(AlertScope.AppliesTo(Touch("any", 1, null, null, symbol: null), "ETHUSDT", Chart("1h")));
        }

        [Fact]
        public void A_month_is_not_a_minute()
        {
            // Every coverage set in the product is case-insensitive; "1M" is a month and "1m" a
            // minute, so Cody's monthly tab must not claim his one-minute alerts.
            Assert.False(AlertScope.SameTimeframe("1M", "1m"));
            Assert.False(AlertScope.AppliesTo(Touch("m", 61450, "1M"), "BTCUSDT", Chart("1m")));
            Assert.True(AlertScope.SameTimeframe("60m", "1h"));
            var monthKeys = AlertScope.CoverageKeys("BTCUSDT", "1M", "Bitstamp").ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(AlertScope.CoverageKey(Touch("m", 1, "1m"), "BTCUSDT"), monthKeys);
            Assert.Contains(AlertScope.CoverageKey(Touch("M", 1, "1M"), "BTCUSDT"), monthKeys);
        }

        // ── Cody's six tabs, through the real evaluator ──────────────────────────

        private static readonly DateTime Day = new(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Daily candles: the newest spans 60,000 to 62,000, so it contains 61,450.</summary>
        private static List<Ohlcv> DailyBars() => new()
        {
            new(Day.AddDays(-1), 60500, 61800, 60100, 61000, 1),
            new(Day,             61000, 62000, 60000, 61100, 1),
        };

        /// <summary>4-hour candles nowhere near 61,450: the newest spans 60,900 to 61,200.</summary>
        private static List<Ohlcv> FourHourBars() => new()
        {
            new(Day.AddHours(12), 61000, 61150, 60850, 61050, 1),
            new(Day.AddHours(16), 61050, 61200, 60900, 61100, 1),
        };

        private static BackgroundWorkspaceMonitor Monitor(string timeframe, List<Ohlcv> bars,
            IAlertOrchestrator alerts, IAlertEvaluator evaluator, SpyEventBus bus)
        {
            var feeds = Substitute.For<IMarketFeeds>();
            feeds.GetBarsAsync(Arg.Any<ChartIdentity>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns((IReadOnlyList<Ohlcv>)bars);
            var engine = Substitute.For<IStrategyEngine>();
            engine.ActiveStrategies.Returns(new List<ActiveStrategy>());
            return new BackgroundWorkspaceMonitor(
                Chart(timeframe), "BTCUSDT", ImmutableList<ChartSeries>.Empty,
                feeds, Substitute.For<IIndicatorService>(), alerts, evaluator, engine, bus,
                NullLogger.Instance, TimeSpan.FromHours(1));
        }

        private static int Fired(SpyEventBus bus, string id) =>
            bus.Log.OfType<AlertFiredEvent>().Count(e => e.Alert.Definition.Id == id);

        [Fact]
        public async Task A_4h_alert_never_fires_on_the_daily_tab_whose_candle_spans_its_level()
        {
            var alerts = Substitute.For<IAlertOrchestrator>();
            alerts.GetAlerts().Returns(new List<AlertDefinition>
            {
                Touch("cody-61450", 61450),        // set on the 4h tab; only the daily candle reaches it
                Touch("hit-61000", 61000),         // set on the 4h tab; its candle does reach it
            });
            // ONE evaluator, as in the app: every background tab shares the singleton.
            var evaluator = new AlertEvaluator(new SdkCandlePatternAnalyzer(), new IndicatorContextAnalyzer());
            var bus = new SpyEventBus();
            var daily = Monitor("1d", DailyBars(), alerts, evaluator, bus);
            var fourHour = Monitor("4h", FourHourBars(), alerts, evaluator, bus);

            await daily.EvaluateOnceAsync(CancellationToken.None);     // warm-up
            await fourHour.EvaluateOnceAsync(CancellationToken.None);  // warm-up
            for (int poll = 0; poll < 4; poll++)
            {
                await daily.EvaluateOnceAsync(CancellationToken.None);
                await fourHour.EvaluateOnceAsync(CancellationToken.None);
            }

            // Before the fix: the daily tab fired 61,450 on every poll, and 61,000 too, each tab's
            // candle date resetting the once-per-bar memory for the other.
            Assert.Equal(0, Fired(bus, "cody-61450"));
            Assert.Equal(1, Fired(bus, "hit-61000"));
        }

        // ── A Heikin-Ashi chart: the touch is the market's ───────────────────────

        [Fact]
        public void On_a_Heikin_Ashi_chart_price_touches_reads_the_real_high_and_low()
        {
            // A fall from 70,000: the Heikin-Ashi open lags far above the market, and the
            // Heikin-Ashi candle's high reaches that open, so the DRAWN candle spans 61,450 while
            // the market never got above 61,200.
            var t = new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc);
            var bars = new List<Ohlcv>();
            for (int i = 0; i < 6; i++) bars.Add(new(t.AddHours(4 * i), 70000, 70500, 69500, 70000, 1));
            bars.Add(new(t.AddHours(24), 70000, 70000, 61000, 61100, 1));
            bars.Add(new(t.AddHours(28), 61100, 61200, 60900, 61000, 1));

            var state = WorkspaceState.Initial with
            {
                Identity = Chart("4h"), SymbolDisplayName = "BTCUSDT",
                Data = new TimeSeriesBuffer<Ohlcv>(bars), IsHeikinAshi = true,
                CurrentDataIndex = bars.Count - 1,
            };
            var drawn = ChartMath.BarsAsDrawn(state.Data!, bars.Count - 1, 1, isHeikinAshi: true);
            Assert.True(drawn[0].High > 61450, "the precondition: the drawn candle spans the level");

            var ev = new AlertEvaluator(new SdkCandlePatternAnalyzer(), new IndicatorContextAnalyzer());
            var empty = new Dictionary<string, double>();
            Assert.Empty(ev.EvaluateAlerts(new[] { Touch("ha", 61450) }, state, bars[^1], bars[^2], empty));

            // A level the market did trade fires, and says the market's price.
            var f = Assert.Single(ev.EvaluateAlerts(new[] { Touch("real", 61150) }, state, bars[^1], bars[^2], empty));
            Assert.Contains("Current value 61,000", f.SpeechText);
        }

        // ── The focused chart ────────────────────────────────────────────────────

        private static WorkspaceState Focused(string timeframe, double rsi, int barCount)
        {
            var t = new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc);
            var bars = Enumerable.Range(0, barCount)
                .Select(i => new Ohlcv(t.AddHours(i), 61000, 61100, 60900, 61000, 1)).ToList();
            var buffer = new SeriesDataBuffer { SeriesId = "rsi" };
            buffer.ComponentData["Rsi"] = Enumerable.Repeat(rsi, barCount).ToArray();
            var config = new SeriesConfig { Id = "rsi", Name = "Rsi", FriendlyName = "RSI", IndicatorCode = "Rsi", Pane = "rsi" };
            config.Components.Add(new ComponentConfig { Name = "Rsi", DisplayName = "RSI" });
            return WorkspaceState.Initial with
            {
                Identity = Chart(timeframe), SymbolDisplayName = "BTCUSDT",
                Data = new TimeSeriesBuffer<Ohlcv>(bars),
                ActiveSeries = ImmutableList.Create(new ChartSeries(config, buffer)),
                CurrentDataIndex = barCount - 1,
                InitStatus = InitializationStatus.Ready,
            };
        }

        [Fact]
        public void Switching_tabs_is_not_an_indicator_crossing()
        {
            // RSI 50 on the 1d tab and 80 on the 1h tab, neither moving. An "RSI crosses above
            // 70" alert with no timeframe (written before alerts were scoped) applies to both.
            // The orchestrator compared the 1d tab's last RSI with the 1h tab's first and fired.
            var store = new MockWorkspaceStore();
            var bus = new SpyEventBus();
            var library = Substitute.For<IWorkspaceLibraryService>();
            library.LoadAlerts().Returns(new List<AlertDefinition>
            {
                new()
                {
                    Id = "rsi70", Name = "rsi70", Delivery = AlertDelivery.Speech,
                    Target = AlertTarget.Indicator, IndicatorCode = "Rsi", ComponentName = "Rsi",
                    Condition = AlertCondition.CrossesAbove, Threshold = 70, Symbol = "BTCUSDT",
                },
            });
            var orch = new AlertOrchestrator(store,
                new AlertEvaluator(new SdkCandlePatternAnalyzer(), new IndicatorContextAnalyzer()),
                bus, library, NullLogger<AlertOrchestrator>.Instance);
            orch.Start();

            store.EmitState(Focused("1d", 50, 5));   // warm-up
            store.EmitState(Focused("1d", 50, 6));
            store.EmitState(Focused("1h", 80, 7));   // the tab switch
            store.EmitState(Focused("1h", 80, 8));

            Assert.Empty(bus.Log.OfType<AlertFiredEvent>());
        }

        [Fact]
        public void The_focused_chart_evaluates_only_the_alerts_set_on_it()
        {
            var store = new MockWorkspaceStore();
            var evaluator = Substitute.For<IAlertEvaluator>();
            IReadOnlyList<AlertDefinition>? seen = null;
            evaluator.EvaluateAlerts(
                    Arg.Do<IReadOnlyList<AlertDefinition>>(a => seen = a), Arg.Any<WorkspaceState>(),
                    Arg.Any<Ohlcv>(), Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyDictionary<string, double>>())
                .Returns(Enumerable.Empty<AlertFired>());
            var library = Substitute.For<IWorkspaceLibraryService>();
            library.LoadAlerts().Returns(new List<AlertDefinition>
            {
                Touch("on-4h", 61450), Touch("on-1d", 61450, "1d"), Touch("old", 61450, null, null),
            });
            var orch = new AlertOrchestrator(store, evaluator, new SpyEventBus(), library,
                NullLogger<AlertOrchestrator>.Instance);
            orch.Start();

            store.EmitState(Focused("1d", 50, 5));
            store.EmitState(Focused("1d", 50, 6));

            Assert.Equal(new[] { "on-1d", "old" }, seen!.Select(a => a.Id));
        }

        // ── Who watches what, with the browser open ──────────────────────────────

        [Fact]
        public void With_the_1d_tab_on_screen_the_headless_monitor_takes_the_4h_alert()
        {
            // Now that the 1-day tab no longer evaluates a 4-hour alert, "BTCUSDT is on screen"
            // must not mean the 4-hour alert is covered, or nobody would watch it.
            var covered = AlertScope.CoverageKeys("BTCUSDT", "1d", "Bitstamp")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var watch = new LocalBackgroundMonitor.Watch("Bitstamp", "BTCUSDT", "4h",
                new[] { Touch("on-4h", 61450), Touch("old", 61450, null, null) }, "Crypto");
            var daily = new LocalBackgroundMonitor.Watch("Bitstamp", "BTCUSDT", "1d",
                new[] { Touch("on-1d", 61450, "1d") }, "Crypto");

            var owned = LocalBackgroundMonitor.OwnedAlertWatches(new[] { watch, daily }, covered);

            var w = Assert.Single(owned);
            Assert.Equal(new[] { "on-4h" }, w.Alerts.Select(a => a.Id));
        }

        [Fact]
        public void An_open_browser_claims_its_chart_not_only_its_symbol()
        {
            var store = new MockWorkspaceStore();
            store.EmitState(Focused("1d", 50, 3));

            var covered = WebHostBrowserCircuitHandler.CoveredSymbols(store, monitoring: null)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.Contains("BTCUSDT", covered);   // bar closes still go by the symbol
            Assert.Contains(AlertScope.CoverageKey(Touch("d", 1, "1d"), "BTCUSDT"), covered);
            Assert.DoesNotContain(AlertScope.CoverageKey(Touch("h", 1, "4h"), "BTCUSDT"), covered);
        }

        [Fact]
        public void The_hosted_on_screen_set_carries_the_chart_not_only_the_symbol()
        {
            WebHostBrowserCircuitHandler.ResetOnScreenSymbolsForTests();
            try
            {
                WebHostBrowserCircuitHandler.RecordOnScreenSymbol("user", "c1", "BTCUSDT", "1d", "Bitstamp");
                var onScreen = WebHostBrowserCircuitHandler.OnScreenSymbolsForUser("user");
                Assert.Contains("BTCUSDT", onScreen);
                Assert.Contains(AlertScope.CoverageKey(Touch("d", 1, "1d"), "BTCUSDT"), onScreen);
                Assert.DoesNotContain(AlertScope.CoverageKey(Touch("h", 1, "4h"), "BTCUSDT"), onScreen);
            }
            finally { WebHostBrowserCircuitHandler.ResetOnScreenSymbolsForTests(); }
        }
    }
}
