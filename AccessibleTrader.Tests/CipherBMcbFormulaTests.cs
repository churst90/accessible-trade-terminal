using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cipher B's numbers, fitted to real Market Cipher B (2026-10-17).</b>
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
    /// MCB's big green dot: every cross up with the slow wave at or below −60, and nothing else —
    /// no confirmation, no cooldown, no anchor gate — drawn in the money-flow bar. Eleven of eleven
    /// MCB green dots sit on such a cross; our gated rule matched three. BuyDotMode = Gated keeps
    /// the old rule.
    /// </summary>
    [Fact]
    public void The_buy_dot_is_every_cross_up_at_or_below_minus_60()
    {
        int total = 0;
        foreach (int flavour in CausalityProbeSeries.HourlyFlavours)
        {
            var bars = CausalityProbeSeries.Bars(flavour, 1400);
            var mcb = Run(bars); var gated = Run(bars, ("BuyDotMode", "Gated"));
            var fast = mcb[CipherBProvider.CompWT1]; var slow = mcb[CipherBProvider.CompWT2];
            var expected = Enumerable.Range(1, bars.Count - 1).Where(i =>
                !double.IsNaN(fast[i - 1]) && !double.IsNaN(slow[i - 1]) && !double.IsNaN(fast[i]) && !double.IsNaN(slow[i])
                && fast[i - 1] < slow[i - 1] && fast[i] >= slow[i] && slow[i] <= -60).ToList();
            var dots = Fired(mcb[CipherBProvider.CompBlue]);
            Assert.Equal(expected, dots);
            Assert.All(dots, i => Assert.Equal(CipherBProvider.McbBuyDotY, mcb[CipherBProvider.CompBlue][i]));
            Assert.NotEqual(Fired(gated[CipherBProvider.CompBlue]), dots);
            total += dots.Count;
        }
        Assert.True(total > 0, "no MCB buy dot fired on any probe series — nothing was tested");
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
    /// MCB's magenta "RSI" is a 40-bar stochastic and its green/red "Stochastic" an 80-bar one, both
    /// smoothed over 3 (RMSE 2.1 and 1.2 points against the digitised lines). A price spike 60 bars
    /// back is outside the first's window and inside the second's; both stay within 0–100.
    /// </summary>
    [Fact]
    public void The_rsi_line_looks_back_40_bars_and_the_stochastic_80()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        List<Ohlcv> Series(bool spike) => Enumerable.Range(0, 200).Select(i =>
        {
            double p = 100 + 5 * Math.Sin(i / 6.0);
            double hi = p + 1 + (spike && i == 140 ? 50 : 0);
            return new Ohlcv(start.AddDays(i), p, hi, p - 1, p, 1000);
        }).ToList();
        var plain = Run(Series(false)); var spiked = Run(Series(true));
        Assert.Equal(plain[CipherBProvider.CompMcbRsi][199], spiked[CipherBProvider.CompMcbRsi][199], 9);
        Assert.NotEqual(plain[CipherBProvider.CompMcbStoch][199], spiked[CipherBProvider.CompMcbStoch][199]);
        foreach (var name in new[] { CipherBProvider.CompMcbRsi, CipherBProvider.CompMcbStoch })
            Assert.All(plain[name].Where(v => !double.IsNaN(v)), v => Assert.InRange(v, 0.0, 100.0));
    }
}
