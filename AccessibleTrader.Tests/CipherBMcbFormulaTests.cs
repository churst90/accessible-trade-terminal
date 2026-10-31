using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cipher B's numbers, fitted to real Market Cipher B (2026-10-17; lines and buy dot re-read
/// from MCB 2.4.0's own plotted values 2026-10-23).</b>
///
/// <para>Four dated TradingView screenshots were digitised (BTCUSD 1D Bitstamp Heikin-Ashi ×2,
/// BTCUSDT 1D Binance, BTCUSDT 30m Binance) and our maths searched against them; the fits are in
/// CipherBProvider's comments. Each new default keeps the old behaviour behind a parameter, because
/// saved strategies and StrategyLab runs were measured on the old numbers. These tests pin the
/// PROPERTIES the fits chose — which bars, which candles, which window — rather than restating the
/// arithmetic.</para>
/// </summary>
public sealed class CipherBMcbFormulaTests
{
    private static Dictionary<string, double[]> Run(IReadOnlyList<Ohlcv> bars, params (string Key, object Value)[] p)
    {
        var pars = new Dictionary<string, object>();
        foreach (var (k, v) in p) pars[k] = v;
        var results = new Dictionary<string, double[]>();
        new CipherBProvider().Calculate("CIPHER_B", bars.ToArray(), pars, new IndicatorResultBuffer(results, bars.Count));
        return results;
    }

    private static List<int> Fired(double[] a) => Enumerable.Range(0, a.Length).Where(i => !double.IsNaN(a[i])).ToList();

    // ── The slow wave ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The slow wave is the mean of the LAST THREE fast-wave values (MCB's crosses landed on our
    /// cross bar 164/182 times with 3, 121/182 with the old 4); WTSignalPeriod = 4 brings 4 back.
    /// </summary>
    [Theory]
    [InlineData(null, 3)]
    [InlineData(4, 4)]
    public void The_slow_wave_averages_the_last_three_fast_values_by_default(int? setting, int window)
    {
        var bars = CausalityProbeSeries.Bars(0, 400);
        var r = setting is { } s ? Run(bars, ("WTSignalPeriod", s)) : Run(bars);
        var fast = r[CipherBProvider.CompWT1]; var slow = r[CipherBProvider.CompWT2];
        int checkedBars = 0;
        for (int i = 100; i < 400; i++)
        {
            double mean = Enumerable.Range(i - window + 1, window).Average(j => fast[j]);
            Assert.Equal(mean, slow[i], 9);
            checkedBars++;
        }
        Assert.True(checkedBars > 0);
    }

    /// <summary>The cross dots sit ON the slow wave, where MCB draws them (they sat 3 points off it).</summary>
    [Fact]
    public void Cross_dots_sit_on_the_slow_wave()
    {
        var r = Run(CausalityProbeSeries.Bars(1, 600));
        var slow = r[CipherBProvider.CompWT2];
        var up = Fired(r[CipherBProvider.CompCrossBull]); var down = Fired(r[CipherBProvider.CompCrossBear]);
        Assert.NotEmpty(up); Assert.NotEmpty(down);
        Assert.All(up, i => Assert.Equal(slow[i], r[CipherBProvider.CompCrossBull][i]));
        Assert.All(down, i => Assert.Equal(slow[i], r[CipherBProvider.CompCrossBear][i]));
    }

    // ── The green buy dot ─────────────────────────────────────────────────────────

    /// <summary>
    /// MCB's big green dot: every cross up (Pine's crossover) with the FAST wave below −60, and
    /// nothing else — no confirmation, no cooldown, no anchor gate — drawn in the money-flow bar.
    /// Read from MCB 2.4.0's own "Green Dot" column on BTCUSD 1D (2026-10-23): 64 of 64 on candles,
    /// 68 of 68 on Heikin-Ashi, no extra; the slow-wave test used before added 10 and 7.
    /// The older gated rule (BuyDotMode = Gated) was removed on 2026-10-31: an old save's
    /// "Gated" now draws the same dots.
    /// </summary>
    [Fact]
    public void The_buy_dot_is_every_cross_up_with_the_fast_wave_below_minus_60()
    {
        int total = 0, slowRuleDiffers = 0;
        foreach (int flavour in CausalityProbeSeries.HourlyFlavours)
        {
            var bars = CausalityProbeSeries.Bars(flavour, 1400);
            var mcb = Run(bars); var gated = Run(bars, ("BuyDotMode", "Gated"));
            var fast = mcb[CipherBProvider.CompWT1]; var slow = mcb[CipherBProvider.CompWT2];
            List<int> Crosses(Func<int, bool> deep) => Enumerable.Range(1, bars.Count - 1).Where(i =>
                !double.IsNaN(fast[i - 1]) && !double.IsNaN(slow[i - 1]) && !double.IsNaN(fast[i]) && !double.IsNaN(slow[i])
                && fast[i - 1] <= slow[i - 1] && fast[i] > slow[i] && deep(i)).ToList();
            var expected = Crosses(i => fast[i] < -60);
            var dots = Fired(mcb[CipherBProvider.CompBlue]);
            Assert.Equal(expected, dots);
            Assert.All(dots, i => Assert.Equal(CipherBProvider.McbBuyDotY, mcb[CipherBProvider.CompBlue][i]));
            Assert.Equal(dots, Fired(gated[CipherBProvider.CompBlue]));
            if (!Crosses(i => slow[i] <= -60).SequenceEqual(expected)) slowRuleDiffers++;
            total += dots.Count;
        }
        Assert.True(total > 0, "no MCB buy dot fired on any probe series — nothing was tested");
        Assert.True(slowRuleDiffers > 0, "the slow-wave rule fires on the same bars on every probe series — the test cannot tell them apart");
    }

    // ── Money flow ────────────────────────────────────────────────────────────────

    /// <summary>
    /// MCB's money flow reads only the TYPICAL PRICE, (high + low + close) / 3, of the candles the
    /// chart shows (exact against Market Cipher B 2.4.0, 2026-10-24). Moving every open inside its
    /// candle, or stretching one candle's wicks equally up and down, leaves every typical price where
    /// it was, and the money flow does not move on any bar; raising one high alone moves it. (The
    /// 2026-10-23 fit read Heikin-Ashi bodies, which the open moves.)
    /// </summary>
    [Fact]
    public void Money_flow_reads_only_the_typical_price()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        const int k = 150;
        List<Ohlcv> Series(double openShift, double bothWicks, double highOnly) => Enumerable.Range(0, 300).Select(i =>
        {
            double o = 100 + 0.2 * i + (i % 7 == 3 ? -0.5 : 0), c = o + 0.2 * (i % 5 == 0 ? -1 : 1);
            double w = i == k ? bothWicks : 0.0, up = i == k ? highOnly : 0.0;
            // The wicks reach 0.9 past the body, so an open moved by up to 0.8 stays inside the candle.
            return new Ohlcv(start.AddDays(i), o + openShift * Math.Sin(i), Math.Max(o, c) + 0.9 + w + up, Math.Min(o, c) - 0.9 - w, c, 1000);
        }).ToList();
        var steady = Run(Series(0, 0, 0))[CipherBProvider.CompMoneyFlowWave];
        var reopened = Run(Series(0.8, 0, 0))[CipherBProvider.CompMoneyFlowWave];
        var stretched = Run(Series(0, 5, 0))[CipherBProvider.CompMoneyFlowWave];
        var raised = Run(Series(0, 0, 3))[CipherBProvider.CompMoneyFlowWave];
        for (int i = 0; i < 300; i++)
        {
            Assert.Equal(steady[i], reopened[i], 9);
            Assert.Equal(steady[i], stretched[i], 9);
        }
        Assert.True(Math.Abs(raised[k] - steady[k]) > 0.1, $"raising one high moved the money flow by only {raised[k] - steady[k]:F4}");
    }

    /// <summary>
    /// MCB's money flow is LINEAR (no square root) and SIZE-FREE: each typical price's step from its
    /// five-bar mean is measured against the mean size of the last five such steps, so a run of
    /// steady rising candles reads the full 200/3 whatever their size, and the same run at ten times
    /// the price reads the same. The legacy square root of the raw candle's body share gave
    /// 175 × √0.1 = 55 here; that mode was removed on 2026-10-31, and an old save's "Legacy" now
    /// reads the same 200/3.
    /// </summary>
    [Fact]
    public void Money_flow_reads_200_over_3_on_steady_candles_whatever_their_size()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        List<Ohlcv> Steady(double unit) => Enumerable.Range(0, 300).Select(i =>
        {
            double o = unit * (100 + 0.2 * i), c = o + unit * 0.2;
            return new Ohlcv(start.AddDays(i), o, c + unit * 0.9, o - unit * 0.9, c, 1000);
        }).ToList();
        var mf = Run(Steady(1))[CipherBProvider.CompMoneyFlowWave];
        var big = Run(Steady(10))[CipherBProvider.CompMoneyFlowWave];
        var legacy = Run(Steady(1), ("MoneyFlowMode", "Legacy"))[CipherBProvider.CompMoneyFlowWave];
        // The typical price climbs 0.2 a bar: it sits 0.4 above its 5-bar mean every bar, ratio 1.
        Assert.Equal(200.0 / 3.0, mf[250], 6);
        Assert.Equal(mf[250], big[250], 6);
        Assert.Equal(mf, legacy);
    }

    /// <summary>
    /// The whole formula restated from its definition on a random walk: d = typical price less its
    /// 5-bar mean, d ÷ the mean size of the last FIVE d, a 60-bar mean of that, × 200/3. The first
    /// value is on bar 68 (index 67), where MCB's line starts: 4 + 4 + 59 bars of warm-up.
    /// </summary>
    [Fact]
    public void Money_flow_is_200_over_3_times_the_60_bar_mean_of_typical_deviation_over_its_five_bar_mean_size()
    {
        var bars = CausalityProbeSeries.Bars(3, 400);
        var mf = Run(bars)[CipherBProvider.CompMoneyFlowWave];
        AssertRestatesTheFormula(bars, mf);
    }

    /// <summary>
    /// On a Heikin-Ashi chart the money flow reads the Heikin-Ashi candles' typical price, as MCB
    /// does (exact on BTCUSD 1D Heikin-Ashi, 5,446 bars): one Heikin-Ashi, not Heikin-Ashi of
    /// Heikin-Ashi, and not the raw candles.
    /// </summary>
    [Fact]
    public void Money_flow_reads_heikin_ashi_candles_on_a_heikin_ashi_chart()
    {
        var bars = CausalityProbeSeries.Bars(3, 400);
        var onHa = Run(bars, (IndicatorCandles.HeikinAshiKey, 1.0))[CipherBProvider.CompMoneyFlowWave];
        var ha = ChartMath.CalculateHeikinAshi(bars.ToList());
        AssertRestatesTheFormula(ha, onHa);
        Assert.NotEqual(Run(bars)[CipherBProvider.CompMoneyFlowWave][200], onHa[200], 6);
    }

    private static void AssertRestatesTheFormula(IReadOnlyList<Ohlcv> bars, double[] mf)
    {
        int n = bars.Count;
        double Typical(int i) => (bars[i].High + bars[i].Low + bars[i].Close) / 3;
        double D(int i) => Typical(i) - Enumerable.Range(i - 4, 5).Average(Typical);
        double Share(int i) => D(i) / Enumerable.Range(i - 4, 5).Average(j => Math.Abs(D(j)));
        int checkedBars = 0;
        for (int i = 67; i < n; i += 17)
        {
            double expected = Math.Clamp(200.0 / 3.0 * Enumerable.Range(i - 59, 60).Average(Share), -100, 100);
            Assert.Equal(expected, mf[i], 6);
            checkedBars++;
        }
        Assert.True(checkedBars > 10);
        Assert.False(double.IsNaN(mf[67]), "MCB's line starts on bar 68");
        Assert.True(double.IsNaN(mf[66]), "the first value needs 4 + 4 + 59 bars before it");
    }

    /// <summary>
    /// MCB keeps its 60-bar money-flow window on intraday charts (a 30-minute MCB chart fits 60 best;
    /// the 24 bars our timeframe scaling chose flipped sign 25 times where MCB flipped 4). The
    /// chart's timeframe does not change money flow, and since Legacy mode was removed on
    /// 2026-10-31 an old save's "Legacy" does not bring the scaling back.
    /// </summary>
    [Fact]
    public void Money_flow_keeps_its_window_on_intraday_charts()
    {
        var bars = CausalityProbeSeries.Bars(0, 500);
        var m30 = Run(bars, ("__timeframe", "30m"))[CipherBProvider.CompMoneyFlowWave];
        var d1 = Run(bars, ("__timeframe", "1d"))[CipherBProvider.CompMoneyFlowWave];
        Assert.Equal(d1, m30);
        var l30 = Run(bars, ("__timeframe", "30m"), ("MoneyFlowMode", "Legacy"))[CipherBProvider.CompMoneyFlowWave];
        var l1 = Run(bars, ("__timeframe", "1d"), ("MoneyFlowMode", "Legacy"))[CipherBProvider.CompMoneyFlowWave];
        Assert.Equal(m30, l30);
        Assert.Equal(m30, l1);
    }

    // ── The two stochastic lines ──────────────────────────────────────────────────

    /// <summary>
    /// MCB's magenta "RSI" is a 40-bar stochastic of the CLOSE and its green/red "Sto RSI" an
    /// 81-bar one, both smoothed by SMA 2 — exact against MCB 2.4.0's plotted values on BTCUSD 1D
    /// (5,513 bars, RMSE 0.000003; hlc3, 80 bars or SMA 3 are 0.7–2.4 points off). Restated here
    /// from the definition, on candles whose close sits away from hlc3 so each choice shows.
    /// </summary>
    [Fact]
    public void The_two_lines_are_40_and_81_bar_stochastics_of_the_close_smoothed_over_2()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = Enumerable.Range(0, 300).Select(i =>
        {
            double p = 100 + 8 * Math.Sin(i / 7.0) + 3 * Math.Sin(i / 2.3);
            return new Ohlcv(start.AddDays(i), p - 0.5, p + 1.5 + (i % 5), p - 1.2 - (i % 3), p + 0.7 * Math.Cos(i), 1000);
        }).ToList();
        double Stoch(int i, int n)
        {
            double hh = double.MinValue, ll = double.MaxValue;
            for (int j = i - n + 1; j <= i; j++) { hh = Math.Max(hh, bars[j].High); ll = Math.Min(ll, bars[j].Low); }
            return 100 * (bars[i].Close - ll) / (hh - ll);
        }
        var r = Run(bars);
        for (int i = 100; i < 300; i++)
        {
            Assert.Equal((Stoch(i, 40) + Stoch(i - 1, 40)) / 2, r[CipherBProvider.CompMcbRsi][i], 9);
            Assert.Equal((Stoch(i, 81) + Stoch(i - 1, 81)) / 2, r[CipherBProvider.CompMcbStoch][i], 9);
        }
        foreach (var name in new[] { CipherBProvider.CompMcbRsi, CipherBProvider.CompMcbStoch })
            Assert.All(r[name].Where(v => !double.IsNaN(v)), v => Assert.InRange(v, 0.0, 100.0));
    }
}
