using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cipher C is a cycle-low indicator after Camel Finance's CF Cycle Trading Indicator
/// (rebuilt 2026-10-21).</b>
///
/// <para>The golden answers are not the C#. They are CF's own labels — the two Bitcoin daily cycle
/// lows CF printed on Camel's 2026 videos — and properties any cycle-low detector of this kind must
/// keep whatever its tuning: a low is the low of its bar and is confirmed later; a cancellation is a
/// close under the low before the minimum gap; a failure is one after it; a count is the gap in days
/// counting both ends. The fit to CF's 25 SPX labels lives in docs/CIPHER_C_DESIGN.md (the SPX data
/// is Yahoo's and is not committed).</para>
/// </summary>
public sealed class CipherCProviderTests
{
    private readonly ITestOutputHelper _out;
    public CipherCProviderTests(ITestOutputHelper output) => _out = output;

    private static readonly CipherCProvider Provider = new();

    private static IndicatorMetadata Meta() => Provider.GetIndicators().Single();

    private static Dictionary<string, double[]> Run(IReadOnlyList<Ohlcv> bars, string? symbol, Dictionary<string, object>? extra = null)
    {
        var p = new Dictionary<string, object>();
        if (symbol != null) p["__symbol"] = symbol;
        foreach (var (k, v) in extra ?? new()) p[k] = v;
        return CipherCProvider.Compute(bars.ToArray(), p);
    }

    private static Dictionary<string, double[]>? _btc;
    private static Dictionary<string, double[]> Btc() => _btc ??= Run(CipherSrSpeechTests.BtcDaily(), "BTC/USD");

    private static IEnumerable<int> Marked(double[] a) => Enumerable.Range(0, a.Length).Where(i => !double.IsNaN(a[i]));

    // ── 1. What it is ─────────────────────────────────────────────────────────────

    /// <summary>
    /// On the candles, with no levels: the old Cipher C was a ±100 oscillator in its own pane, and
    /// CF draws on the price chart.
    /// </summary>
    [Fact]
    public void It_draws_on_the_price_chart_with_no_levels()
    {
        var m = Meta();
        Assert.Equal(CipherCProvider.Code, m.Code);
        Assert.Equal("Cipher C", m.Name);
        Assert.Equal("Main", m.DefaultPane);
        Assert.False(m.IsDeprecated);
        Assert.Empty(Provider.GetDefaultLevels(CipherCProvider.Code));
    }

    /// <summary>
    /// What is drawn ON a low's bar knows the future (the low is only known once confirmed bars
    /// later) and is declared so, which keeps it out of strategies; every event is decided on its
    /// own bar and is declared causal, so a strategy can use it.
    /// </summary>
    [Theory]
    [InlineData(CipherCProvider.CompYcl, ComponentCausality.Lookahead)]
    [InlineData(CipherCProvider.CompWcl, ComponentCausality.Lookahead)]
    [InlineData(CipherCProvider.CompDcl, ComponentCausality.Lookahead)]
    [InlineData(CipherCProvider.CompDclCount, ComponentCausality.Lookahead)]
    [InlineData(CipherCProvider.CompWclCount, ComponentCausality.Lookahead)]
    [InlineData(CipherCProvider.CompLeftTop, ComponentCausality.Lookahead)]
    [InlineData(CipherCProvider.CompMidTop, ComponentCausality.Lookahead)]
    [InlineData(CipherCProvider.CompRightTop, ComponentCausality.Lookahead)]
    [InlineData(CipherCProvider.CompArc, ComponentCausality.Lookahead)]
    [InlineData(CipherCProvider.CompDclConfirmed, ComponentCausality.Causal)]
    [InlineData(CipherCProvider.CompCancelled, ComponentCausality.Causal)]
    [InlineData(CipherCProvider.CompFailed, ComponentCausality.Causal)]
    [InlineData(CipherCProvider.CompDclDay, ComponentCausality.Causal)]
    [InlineData(CipherCProvider.CompInDclWindow, ComponentCausality.Causal)]
    [InlineData(CipherCProvider.CompConfluence, ComponentCausality.Causal)]
    [InlineData(CipherCProvider.CompDclWindowStart, ComponentCausality.Lookahead)]
    [InlineData(CipherCProvider.CompWclWindowEnd, ComponentCausality.Lookahead)]
    [InlineData(CipherCProvider.CompPreset, ComponentCausality.Lookahead)]
    public void Each_component_declares_whether_it_knows_the_future(string name, ComponentCausality expected)
    {
        var m = Meta();
        var c = m.Components.Single(x => x.Name == name);
        Assert.Equal(expected, c.Causality ?? m.Causality);
    }

    /// <summary>
    /// The events a decision is made on are causal in fact, not only in declaration: computing on
    /// the first N bars gives the same value on bar N-1 as computing on the whole history.
    /// </summary>
    [Fact]
    public void The_events_on_a_bar_do_not_change_when_later_bars_arrive()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var whole = Btc();
        var m = Meta();
        var causal = m.Components.Where(c => (c.Causality ?? m.Causality) == ComponentCausality.Causal).Select(c => c.Name).ToList();
        Assert.Contains(CipherCProvider.CompDclConfirmed, causal);
        var everCompared = new HashSet<string>();
        foreach (int cut in new[] { 3000, 3800, 4500, 5200 })
        {
            var part = Run(bars.Take(cut).ToList(), "BTC/USD");
            foreach (var c in causal)
                for (int i = cut - 400; i < cut; i++)
                {
                    if (!double.IsNaN(whole[c][i]) && whole[c][i] != 0) everCompared.Add(c);
                    Assert.True(Same(part[c][i], whole[c][i]), $"{c} on {bars[i].Date:yyyy-MM-dd}: {part[c][i]} with {cut} bars, {whole[c][i]} with all");
                }
        }
        // A component that never fired in these stretches was not checked at all. (No yearly low is
        // confirmed in them: the 2022 one is confirmed on bar ~4,100, outside every 400-bar stretch.)
        Assert.Empty(causal.Except(everCompared).Except(new[] { CipherCProvider.CompYclConfirmed }));
        static bool Same(double a, double b) => double.IsNaN(a) ? double.IsNaN(b) : Math.Abs(a - b) < 1e-9;
    }

    // ── 2. CF's own Bitcoin labels ──────────────────────────────────────────────────

    /// <summary>
    /// CF labelled two Bitcoin daily cycle lows on Camel's 2026 videos: 2026-02-04 and 2026-03-29
    /// (read off video frames; ±1–3 days). Neither was used to fit the detector.
    /// </summary>
    [Theory]
    [InlineData("2026-02-04")]
    [InlineData("2026-03-29")]
    public void It_finds_the_Bitcoin_daily_cycle_lows_CF_labelled(string cf)
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var target = DateTime.Parse(cf);
        var lows = Marked(Btc()[CipherCProvider.CompDcl]).Select(i => bars[i].Date).ToList();
        _out.WriteLine(string.Join(", ", lows.Where(d => d.Year >= 2025).Select(d => d.ToString("yyyy-MM-dd"))));
        Assert.Contains(lows, d => Math.Abs((d - target).TotalDays) <= 4);
    }

    // ── 3. What a cycle low is ─────────────────────────────────────────────────────

    /// <summary>
    /// Every daily cycle low sits on its bar's low and is confirmed on a later bar at that price:
    /// by the daily detector, which restarts the day count from it — or, for a low the daily
    /// detector skipped, by the weekly or yearly one.
    /// </summary>
    [Fact]
    public void Every_low_is_its_bars_low_and_is_confirmed_later()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var r = Btc();
        var lows = Marked(r[CipherCProvider.CompDcl]).ToList();
        Assert.True(lows.Count > 60, $"only {lows.Count} daily cycle lows in fifteen years of Bitcoin");
        var lags = new List<int>();
        int byHigher = 0;
        foreach (int c in lows)
        {
            Assert.Equal(bars[c].Low, r[CipherCProvider.CompDcl][c]);
            int at = ConfirmedAt(CipherCProvider.CompDclConfirmed, c);
            if (at > c)
            {
                Assert.Equal(1, r[CipherCProvider.CompDclDay][at] - (at - c));
                lags.Add(at - c);
                continue;
            }
            Assert.True(ConfirmedAt(CipherCProvider.CompWclConfirmed, c) > c || ConfirmedAt(CipherCProvider.CompYclConfirmed, c) > c,
                $"the low on {bars[c].Date:yyyy-MM-dd} was never confirmed");
            byHigher++;
        }
        lags.Sort();
        _out.WriteLine($"{lows.Count} lows, {byHigher} confirmed only by a higher cycle, daily confirmation lag median {lags[lags.Count / 2]} days");
        Assert.True(byHigher <= lows.Count / 10, $"{byHigher} of {lows.Count} daily lows came from the higher cycles");

        int ConfirmedAt(string comp, int c) =>
            Marked(r[comp]).FirstOrDefault(t => t > c && r[comp][t] == bars[c].Low, -1);
    }

    /// <summary>
    /// A cancellation is a close under the low before the minimum gap; a failure is a close under
    /// it after. Both carry the low's price, and both happen on Bitcoin.
    /// </summary>
    [Fact]
    public void A_cancellation_and_a_failure_are_closes_under_the_low()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var r = Btc();
        var cancelled = Marked(r[CipherCProvider.CompCancelled]).ToList();
        var failed = Marked(r[CipherCProvider.CompFailed]).ToList();
        _out.WriteLine($"{cancelled.Count} cancelled, {failed.Count} failed");
        Assert.NotEmpty(cancelled);
        Assert.NotEmpty(failed);
        foreach (int t in cancelled.Concat(failed))
            Assert.True(bars[t].Close < r[CipherCProvider.CompCancelled][t] || bars[t].Close < r[CipherCProvider.CompFailed][t],
                $"{bars[t].Date:yyyy-MM-dd}: close {bars[t].Close} is not under the low");

        var tier = CipherCEngine.Tier(54, 66, 1, CipherCEngine.MeasureBars(bars.ToArray()));
        foreach (int t in failed)
            Assert.True(r[CipherCProvider.CompDclDay][t] - 1 >= tier.MinGap, $"{bars[t].Date:yyyy-MM-dd}: a failure before the minimum gap");
    }

    /// <summary>The number under a low is the cycle that ended there, in days, counting both ends — CF's convention.</summary>
    [Fact]
    public void The_count_under_a_low_is_the_cycle_in_days_counting_both_ends()
    {
        var r = Btc();
        var lows = Marked(r[CipherCProvider.CompDcl]).ToList();
        for (int k = 1; k < lows.Count; k++)
            Assert.Equal(lows[k] - lows[k - 1] + 1, r[CipherCProvider.CompDclCount][lows[k]]);
        Assert.True(double.IsNaN(r[CipherCProvider.CompDclCount][lows[0]]));
    }

    /// <summary>
    /// A weekly low is always one of the daily lows, a yearly low one of the weekly ones — CF's
    /// stacks sit on one another. Bitcoin's 5 August 2024 crash is the case that needs it: the
    /// daily detector skips it (31 days after the last low, inside the minimum gap, closed above it)
    /// and the weekly one finds it.
    /// </summary>
    [Fact]
    public void A_weekly_low_sits_on_a_daily_low_and_a_yearly_low_on_a_weekly_one()
    {
        var r = Btc();
        var d = Marked(r[CipherCProvider.CompDcl]).ToHashSet();
        var w = Marked(r[CipherCProvider.CompWcl]).ToList();
        var y = Marked(r[CipherCProvider.CompYcl]).ToList();
        _out.WriteLine($"{d.Count} daily, {w.Count} weekly, {y.Count} yearly");
        Assert.NotEmpty(w);
        Assert.All(w, i => Assert.Contains(i, d));
        Assert.All(y, i => Assert.Contains(i, w));
        var bars = CipherSrSpeechTests.BtcDaily();
        Assert.Contains(w, i => bars[i].Date == new DateTime(2024, 8, 5));
    }

    // ── 4. Tiers and timeframes ─────────────────────────────────────────────────────

    private static List<Ohlcv> Weekly(IReadOnlyList<Ohlcv> daily)
    {
        var weeks = new List<Ohlcv>();
        foreach (var g in daily.GroupBy(b => b.Date.AddDays(-(((int)b.Date.DayOfWeek + 6) % 7)).Date))
        {
            var b = g.ToList();
            weeks.Add(new Ohlcv(g.Key, b[0].Open, b.Max(x => x.High), b.Min(x => x.Low), b[^1].Close, b.Sum(x => x.Volume)));
        }
        return weeks;
    }

    /// <summary>
    /// On a weekly chart there is no daily cycle — no "DCL" (Cody's complaint about Loukas on the
    /// weekly chart) — and the weekly cycle runs, carrying the events instead.
    /// </summary>
    [Fact]
    public void A_weekly_chart_has_no_daily_cycle_and_runs_the_weekly_one()
    {
        var weeks = Weekly(CipherSrSpeechTests.BtcDaily());
        var r = Run(weeks, "BTC/USD");
        Assert.Empty(Marked(r[CipherCProvider.CompDcl]));
        Assert.Empty(Marked(r[CipherCProvider.CompDclWindowStart]));
        Assert.NotEmpty(Marked(r[CipherCProvider.CompWcl]));
        Assert.NotEmpty(Marked(r[CipherCProvider.CompWclWindowStart]));
        Assert.NotEmpty(Marked(r[CipherCProvider.CompPending]));
        var words = CipherCProvider.StateWords(n => r.TryGetValue(n, out var a) ? a : null, weeks.Count - 1);
        _out.WriteLine(string.Join(". ", words));
        Assert.DoesNotContain(words, s => s.Contains("daily", StringComparison.Ordinal));
        Assert.Contains(words, s => s.StartsWith("weekly cycle week ", StringComparison.Ordinal));
    }

    private static List<Ohlcv> Weekdays(int count, DateTime from)
    {
        var bars = new List<Ohlcv>();
        var d = from;
        for (int i = 0; bars.Count < count; i++, d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            double c = 100 + 10 * Math.Sin(2 * Math.PI * bars.Count / 40.0) + bars.Count * 0.02;
            bars.Add(new Ohlcv(d, c - 0.3, c + 1, c - 1, c, 1000));
        }
        return bars;
    }

    /// <summary>
    /// A stock's weekly bar is five trading days, so the weekly cycle runs on a weekly stock chart.
    /// (The bar clock once had the factor upside down — 9.8 trading days a bar — and switched the
    /// weekly tier off on every weekly stock chart.)
    /// </summary>
    [Fact]
    public void A_weekly_stock_chart_runs_the_weekly_cycle()
    {
        var weeks = Weekly(Weekdays(3000, new DateTime(2010, 1, 4)));
        var clock = CipherCEngine.MeasureBars(weeks.ToArray());
        Assert.False(clock.TradesEveryDay);
        Assert.Equal(0.2, clock.BarsPerDay, 3);
        var r = Run(weeks, "SPY");
        Assert.Equal(22.0, r[CipherCProvider.CompWclWindowStart][^1]);
        Assert.Empty(Marked(r[CipherCProvider.CompDclWindowStart]));
    }

    /// <summary>
    /// A preset's days are the chart's days: Bitcoin's ranges are calendar days, so on a weekday-only
    /// chart 54 calendar days are 54 × 5/7 ≈ 38.6 trading days.
    /// </summary>
    [Fact]
    public void A_calendar_day_preset_on_a_weekday_chart_is_converted_to_trading_days()
    {
        var bars = Weekdays(800, new DateTime(2022, 1, 3));
        var r = Run(bars, "IBIT");
        Assert.Equal(38.6, r[CipherCProvider.CompDclWindowStart][^1]);
        Assert.Equal(47.1, r[CipherCProvider.CompDclWindowEnd][^1]);
        var spx = Run(bars, "SPY");
        Assert.Equal(36.0, spx[CipherCProvider.CompDclWindowStart][^1]);
    }

    /// <summary>COIN's CF ranges are already in trading days on a weekday chart.</summary>
    [Fact]
    public void A_COINS_daily_window_is_its_CF_trading_day_range_on_a_weekday_chart()
    {
        var bars = Weekdays(800, new DateTime(2022, 1, 3));
        var r = Run(bars, "COIN");
        Assert.Equal(40.0, r[CipherCProvider.CompDclWindowStart][^1]);
        Assert.Equal(90.0, r[CipherCProvider.CompDclWindowEnd][^1]);
    }

    // ── 5. The sixteen assets ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("SPY", "SPX")]
    [InlineData("^GSPC", "SPX")]
    [InlineData("SP:SPX", "SPX")]
    [InlineData("BTC/USD", "BTC")]
    [InlineData("BTCUSDT", "BTC")]
    [InlineData("XAUUSD", "GOLD")]
    [InlineData("TVC:GOLD", "GOLD")]
    [InlineData("ETH-USD", "ETH")]
    [InlineData("DIA", "DJI")]
    [InlineData("QQQ", "NDQ")]
    [InlineData("MSTR", "MSTR")]
    [InlineData("AAPL", null)]
    [InlineData("SOL/USD", null)]
    public void A_symbol_finds_its_preset(string symbol, string? key)
        => Assert.Equal(key, CipherCEngine.PresetForSymbol(symbol)?.Key);

    [Fact]
    public void There_are_sixteen_presets_and_an_unknown_asset_falls_back_by_whether_it_trades_every_day()
    {
        Assert.Equal(16, CipherCEngine.Presets.Count);
        Assert.Equal(16, CipherCEngine.Presets.Select(p => p.Key).Distinct().Count());
        Assert.Equal(("BTC", false), Key(CipherCEngine.Resolve("Auto", "SOL/USD", tradesEveryDay: true)));
        Assert.Equal(("SPX", false), Key(CipherCEngine.Resolve("Auto", "AAPL", tradesEveryDay: false)));
        Assert.Equal(("GOLD", true), Key(CipherCEngine.Resolve("GOLD", "AAPL", tradesEveryDay: false)));
        Assert.Equal(("ETH", true), Key(CipherCEngine.Resolve("", "ETH/USD", tradesEveryDay: true)));
        static (string, bool) Key((CyclePreset P, bool F) r) => (r.P.Key, r.F);
    }

    [Theory]
    [InlineData("COIN", 40, 90, 24, 42, 4, 4, false)]
    [InlineData("MSTR", 40, 90, 24, 42, 4, 4, false)]
    [InlineData("MARA", 36, 44, 22, 26, 4, 4, false)]
    [InlineData("RIOT", 36, 44, 22, 26, 4, 4, false)]
    [InlineData("GDX", 22, 28, 22, 26, 8, 8, false)]
    [InlineData("GOLD", 22, 28, 20, 28, 8, 8, false)]
    [InlineData("BTC", 54, 66, 22, 42, 4, 4, true)]
    public void CF_read_presets_have_the_recorded_ranges(string key, double dailyMin, double dailyMax,
        double weeklyMin, double weeklyMax, double yearlyMin, double yearlyMax, bool calendarDays)
    {
        var p = CipherCEngine.Preset(key);
        Assert.Equal(dailyMin, p.DailyMin);
        Assert.Equal(dailyMax, p.DailyMax);
        Assert.Equal(weeklyMin, p.WeeklyMin);
        Assert.Equal(weeklyMax, p.WeeklyMax);
        Assert.Equal(yearlyMin, p.YearlyMin);
        Assert.Equal(yearlyMax, p.YearlyMax);
        Assert.Equal(calendarDays, p.CalendarDays);
        Assert.Equal("CF's published ranges", p.Source);
    }

    /// <summary>The preset is said with where its numbers came from; an asset CF does not tune says so.</summary>
    [Fact]
    public void The_preset_is_said_with_its_source()
    {
        var gold = CipherCProvider.PresetWords(4, new DateTime(2025, 1, 1));
        Assert.StartsWith("Gold ranges, CF's published ranges: daily 22 to 28 trading days", gold);
        var dji = CipherCProvider.PresetWords(2, new DateTime(2025, 1, 1));
        Assert.Contains("estimated, not CF's", dji);
        var other = CipherCProvider.PresetWords(-1 - 11, new DateTime(2025, 1, 1));
        Assert.StartsWith("not one of CF's sixteen assets, so Bitcoin's ranges", other);
        Assert.DoesNotContain("halving", other);
        var btc = CipherCProvider.PresetWords(11, new DateTime(2026, 6, 1));
        Assert.Contains("after the April 2024 halving", btc);
    }

    // ── 6. Speech ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(20, 36, 44, "daily cycle day 20, its window opens in 16 days")]
    [InlineData(35, 36, 44, "daily cycle day 35, its window opens in 1 day")]
    [InlineData(40, 36, 44, "daily cycle day 40, in its window, 4 days left")]
    [InlineData(43, 36, 44, "daily cycle day 43, in its window, 1 day left")]
    [InlineData(44, 36, 44, "daily cycle day 44, the last day of its window")]
    [InlineData(50, 36, 44, "daily cycle day 50, 6 days past its window")]
    public void The_day_count_is_said_against_the_window(double count, double start, double end, string expected)
        => Assert.Equal(expected, CipherCProvider.WindowWords("daily cycle", "day", "days", count, start, end));

    [Fact]
    public void A_tier_that_does_not_run_says_nothing_and_one_with_no_low_yet_says_so()
    {
        Assert.Null(CipherCProvider.WindowWords("daily cycle", "day", "days", double.NaN, double.NaN, double.NaN));
        Assert.Equal("no daily cycle low confirmed yet", CipherCProvider.WindowWords("daily cycle", "day", "days", double.NaN, 36, 44));
    }

    /// <summary>On a confirming bar the detail fact names the confluence; Alt+Shift+D reads the same words.</summary>
    [Fact]
    public void The_detail_fact_on_a_confirming_bar_names_the_window_and_the_confluence()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var r = Btc();
        int t = Marked(r[CipherCProvider.CompDclConfirmed]).Last();
        var fact = Provider.GetDetailFact(CipherCProvider.Code, bars.ToArray(), r, t, new());
        _out.WriteLine(fact);
        Assert.StartsWith("Daily cycle day ", fact);
        Assert.Matches("Cycle low confirmed, (high|mid|low) confluence", fact);
        Assert.Contains(". Bitcoin ranges", fact);
        Assert.DoesNotMatch(@"\. [a-z]", fact);
    }

    // ── 7. A Cipher C saved before the rebuild ──────────────────────────────────────

    /// <summary>Every component the oscillator Cipher C declared — what an old chart carries.</summary>
    internal static readonly string[] SavedBeforeRebuild =
    {
        "Top Triple", "Top Double", "Top Single", "Cycle Sine", "Bottom Single", "Bottom Double",
        "Bottom Triple", "Lead Sine", "Shallow Peak", "Shallow Trough",
    };

    private static readonly IndicatorModelFactory Factory = new(
        new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
        new MockIndicatorPreferencesService());

    private static IndicatorEngine Engine()
    {
        var providers = new List<IIndicatorProvider> { new CipherCProvider() };
        return new IndicatorEngine(new IndicatorService(providers, NullLogger<IndicatorService>.Instance), new CustomIndicatorRegistry(), providers);
    }

    private static ChartSeries Restored(string[] savedComponents)
    {
        var meta = new CipherCProvider().GetIndicators();
        var saved = new SeriesConfig { Id = "cc-1", Name = "Cipher C 10 3 3 75", IndicatorCode = "CIPHER_C", Pane = "Pane_CIPHER_C" };
        saved.Parameters["CyclePeriod"] = 10; saved.Parameters["SmoothPeriod"] = 3; saved.Parameters["OBLevel"] = 75;
        foreach (var n in savedComponents)
            saved.Components.Add(new ComponentConfig { Name = n, DisplayName = n, IsVisible = true, IsEnabled = true, Volume = 1f });
        foreach (var (name, v) in new[] { ("Extreme OB", 90.0), ("Overbought", 75.0), ("Zero", 0.0), ("Oversold", -75.0), ("Extreme OS", -90.0) })
            saved.Levels.Add(new LevelConfig { Name = name, Value = v, IsVisible = true });
        WorkspaceInitializer.MigrateSeriesConfig(saved, meta);
        return SeriesManagementService.MaterializeSaved(saved, meta[0], Factory,
            Array.Empty<IReadOnlyDictionary<string, object>>(), Engine(), new MockIndicatorPreferencesService());
    }

    private static string Landing(ChartSeries series, List<Ohlcv> bars, IndicatorEngine engine, int index, int at)
    {
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(series),
            PrimarySeriesId = series.Id, FocusedSeriesId = series.Id, FocusedComponentIndex = index,
            CurrentDataIndex = at, ViewportStartIndex = Math.Max(0, at - 50), ViewportLength = 100,
            LastInteractionContext = InteractionContext.Component,
        };
        return new SpeechFormatter(NullLogger<SpeechFormatter>.Instance, engine)
            .FormatPointFeedback(state, isXMove: false, isYMove: true, series, bars[at], "");
    }

    /// <summary>
    /// An old chart's Cipher C comes back as the new one: on the candles, with the new components,
    /// none of the old (so no keyboard stop speaks a removed signal), and without its ±75/±90
    /// oscillator levels — on the price pane those were prices near zero that would crush the
    /// candles into the top of the pane.
    /// </summary>
    [Fact]
    public void A_Cipher_C_saved_before_the_rebuild_restores_as_the_new_one()
    {
        var series = Restored(SavedBeforeRebuild);
        var names = series.Components.Select(c => c.Name).ToList();
        _out.WriteLine(string.Join(" | ", names));
        Assert.Equal("Main", series.Config.Pane);
        Assert.Equal(Meta().Components.Select(c => c.Name), names);
        Assert.Empty(names.Intersect(SavedBeforeRebuild));
        Assert.Empty(series.Levels);

        var bars = CipherSrSpeechTests.BtcDaily();
        foreach (var kv in Btc()) series.Data.ComponentData[kv.Key] = kv.Value;
        var engine = Engine();
        int at = Marked(Btc()[CipherCProvider.CompDcl]).Last();
        var said = Enumerable.Range(0, series.Components.Count).Select(i => Landing(series, bars, engine, i, at)).ToList();
        foreach (var s in said) _out.WriteLine(s);
        Assert.DoesNotContain(said, s => SavedBeforeRebuild.Any(o => s.Contains(o, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Alt+Shift+D on the Cipher C reads where the cycle is against its window.</summary>
    [Fact]
    public void Bar_detail_reads_the_cycle_against_its_window()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var series = Restored(Array.Empty<string>());
        foreach (var kv in Btc()) series.Data.ComponentData[kv.Key] = kv.Value;
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(series),
            PrimarySeriesId = series.Id, FocusedSeriesId = series.Id,
            CurrentDataIndex = bars.Count - 1, ViewportStartIndex = bars.Count - 100, ViewportLength = 100,
        };
        var bus = new SpyEventBus();
        new BarDetailService(bus, null).AnnounceDetails(state);
        var said = bus.Log.OfType<AnnouncementEvent>().Last().Message;
        _out.WriteLine(said);
        Assert.Matches("daily cycle day [0-9]+, ", said);
    }

    // ── 8. Strategies and alerts that named a removed component ─────────────────────

    private static readonly SignalCatalog Catalog = new(new IIndicatorProvider[] { new CipherCProvider() });

    /// <summary>
    /// A condition on any of the ten removed components is false and says why, naming the date and
    /// what to use instead; a condition on a live one says nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(RemovedIds))]
    public void A_condition_on_a_removed_Cipher_C_signal_says_it_was_removed(string id)
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var series = Restored(Array.Empty<string>());
        foreach (var kv in Btc()) series.Data.ComponentData[kv.Key] = kv.Value;
        var state = WorkspaceState.Initial with { Data = new TimeSeriesBuffer<Ohlcv>(bars), ActiveSeries = ImmutableList.Create(series) };
        var eval = new ConditionEvaluator(Catalog);

        var result = eval.Evaluate(new ConditionLeaf("a", id, LeafOperator.Fired), bars, state);
        Assert.False(result.OverallTrue);
        Assert.NotNull(eval.LastDegradation);
        Assert.Contains("removed on 2026-10-21", eval.LastDegradation!);
        Assert.Contains("CF Cycle Trading Indicator", eval.LastDegradation!);

        eval.Evaluate(new ConditionLeaf("b", "CIPHER_C.DCL Confirmed", LeafOperator.Fired), bars, state);
        Assert.Null(eval.LastDegradation);
    }

    public static IEnumerable<object[]> RemovedIds() => SavedBeforeRebuild.Select(n => new object[] { $"CIPHER_C.{n}" });

    /// <summary>An alert set on the old Bottom Triple says once that it cannot fire, naming it.</summary>
    [Fact]
    public void An_alert_on_the_old_bottom_says_it_cannot_fire()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var series = Restored(Array.Empty<string>());
        foreach (var kv in Btc()) series.Data.ComponentData[kv.Key] = kv.Value;
        var state = WorkspaceState.Initial with { Data = new TimeSeriesBuffer<Ohlcv>(bars), ActiveSeries = ImmutableList.Create(series), CurrentDataIndex = bars.Count - 1 };
        var ev = new AlertEvaluator(new SdkCandlePatternAnalyzer(), new IndicatorContextAnalyzer());
        var said = new List<string>();
        ev.EvaluationDegraded += (_, why) => said.Add(why);
        var alert = new AlertDefinition
        {
            Id = "bottom", Name = "bottom", Delivery = AlertDelivery.Speech, Target = AlertTarget.Indicator,
            Condition = AlertCondition.CrossesAbove, Threshold = 0, IndicatorCode = "CIPHER_C", ComponentName = "Bottom Triple",
        };
        for (int i = 0; i < 2; i++)
            Assert.Empty(ev.EvaluateAlerts(new[] { alert }, state, bars[^1], bars[^2], new Dictionary<string, double>()));
        var why = Assert.Single(said);
        _out.WriteLine(why);
        Assert.Contains("Bottom Triple", why);
    }

    // ── 9. Loukas ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loukas Cycles is retired in favour of Cipher C: it still works on charts that have it, but
    /// is no longer offered, and its description says what replaced it.
    /// </summary>
    [Fact]
    public void Loukas_cycles_is_retired_in_favour_of_Cipher_C()
    {
        var m = new LoukasCyclesProvider().GetIndicators().Single();
        Assert.True(m.IsDeprecated);
        Assert.StartsWith("Retired: use Cipher C", m.Description);
    }
}
