using AccessibleTrader.Core.Services.Indicators;
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
    /// BuyDotMode = Gated keeps the older gated rule.
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
            Assert.NotEqual(Fired(gated[CipherBProvider.CompBlue]), dots);
            if (!Crosses(i => slow[i] <= -60).SequenceEqual(expected)) slowRuleDiffers++;
            total += dots.Count;
        }
        Assert.True(total > 0, "no MCB buy dot fired on any probe series — nothing was tested");
        Assert.True(slowRuleDiffers > 0, "the slow-wave rule fires on the same bars on every probe series — the test cannot tell them apart");
    }

    // ── Money flow ────────────────────────────────────────────────────────────────

    /// <summary>
    /// MCB's money flow reads HEIKIN-ASHI candle bodies, not raw ones. A steady climb of dojis
    /// (open = close on every raw candle) has no raw body at all — the legacy formula reads it as
    /// zero — while its Heikin-Ashi candles are all rising, so MCB's money flow is positive.
    /// </summary>
    [Fact]
    public void Money_flow_reads_heikin_ashi_bodies()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var dojis = Enumerable.Range(0, 200).Select(i =>
        {
            double p = 100 + i;
            return new Ohlcv(start.AddDays(i), p, p + 1, p - 1, p, 1000);
        }).ToList();
        var mcb = Run(dojis)[CipherBProvider.CompMoneyFlowWave];
        var legacy = Run(dojis, ("MoneyFlowMode", "Legacy"))[CipherBProvider.CompMoneyFlowWave];
        Assert.Equal(0.0, legacy[150], 9);
        Assert.True(mcb[150] > 5, $"money flow on rising Heikin-Ashi candles reads {mcb[150]:F2}, not positive");
    }

    /// <summary>
    /// MCB's money flow is LINEAR in the average body (no square root): candles whose bodies are a
    /// steady share of their range give 180 × that share once the Heikin-Ashi candles settle. The
    /// legacy square root gave 175 × √share — 2.2× as tall for a 10% share, the over-tall wave that
    /// covered the VWAP.
    /// </summary>
    [Fact]
    public void Money_flow_is_linear_in_the_average_body()
    {
        // Each candle opens where the last closed and rises 0.2 with a range of 2: the Heikin-Ashi
        // body settles at a fixed share of its range.
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = Enumerable.Range(0, 300).Select(i =>
        {
            double o = 100 + 0.2 * i, c = o + 0.2;
            return new Ohlcv(start.AddDays(i), o, c + 0.9, o - 0.9, c, 1000);
        }).ToList();
        var mf = Run(bars)[CipherBProvider.CompMoneyFlowWave];
        var legacy = Run(bars, ("MoneyFlowMode", "Legacy"))[CipherBProvider.CompMoneyFlowWave];
        // Heikin-Ashi: close = ohlc4 = o + 0.1; open = the previous HA midpoint, which settles at
        // o − 0.3 (x = (x − 0.2 + o − 0.1) / 2 ⇒ x = o − 0.3); high/low = the raw ones (o + 1.1 /
        // o − 0.9). Body 0.4, range 2.0 → share 0.2 → 36. The legacy square root of the RAW share
        // (0.1) gives 175 × 0.316 = 55.
        Assert.Equal(180 * 0.2, mf[250], 2);
        Assert.True(legacy[250] > 1.4 * mf[250], $"legacy {legacy[250]:F1} vs MCB {mf[250]:F1}");
    }

    /// <summary>
    /// MCB keeps its 60-bar money-flow window on intraday charts (a 30-minute MCB chart fits 60 best;
    /// the 24 bars our timeframe scaling chose flipped sign 25 times where MCB flipped 4). In MCB
    /// mode the chart's timeframe no longer changes money flow; in Legacy mode it still does.
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
        Assert.NotEqual(l1, l30);
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
