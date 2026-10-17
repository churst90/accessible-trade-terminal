using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>DSS Bressert computes Bressert's double smoothed stochastic, and gets everything a
/// Stochastic gets.</b>
///
/// <para>
/// The provider's maths is checked three ways, none of them a copy of the provider's loops:
/// </para>
/// <list type="bullet">
///   <item>small cases worked by hand, in exact fractions, small enough to check with a pencil;</item>
///   <item>an oracle written straight from the published formula in a different SHAPE — every
///         EMA as its closed-form weighted sum, every window as a LINQ slice — over several
///         parameter sets, including each camp's defaults;</item>
///   <item>numbers from INDEPENDENT ports — the TradingView Pine form and the MetaQuotes MT4/MT5
///         code, ported to Python from their sources and run on 160 real BTC daily bars
///         (Bitstamp, 2020-05-22 to 2020-10-28). Pine and this provider seed their EMAs alike and
///         agree on every published bar; MetaTrader seeds at 0 or 50, disagrees by up to 32 points
///         on the first published bar, and agrees with both to 1e-11 by the last. The Python is
///         in tools/dss_bressert_reference.py, and the
///         numbers are pasted here.</item>
/// </list>
/// <para>
/// The rest is the part a blind trader actually uses: the levels it is narrated against, the
/// component the narrator speaks for, and the sound it is given — held to be the Stochastic's.
/// </para>
/// </summary>
public sealed class DssBressertProviderTests
{
    private static readonly DssBressertProvider Provider = new();

    private static List<Ohlcv> Bars(IReadOnlyList<(double High, double Low, double Close)> hlc)
    {
        var t = new DateTime(2020, 5, 22, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<Ohlcv>(hlc.Count);
        for (int i = 0; i < hlc.Count; i++)
        {
            double open = i == 0 ? hlc[i].Close : hlc[i - 1].Close;
            bars.Add(new Ohlcv(t.AddDays(i), open, hlc[i].High, hlc[i].Low, hlc[i].Close, 1000));
        }
        return bars;
    }

    private static Dictionary<string, double[]> Run(List<Ohlcv> bars, int? p = null, int? e = null, int? t = null)
    {
        var pars = new Dictionary<string, object>();
        if (p.HasValue) pars[DssBressertProvider.ParamStoch] = p.Value;
        if (e.HasValue) pars[DssBressertProvider.ParamEma] = e.Value;
        if (t.HasValue) pars[DssBressertProvider.ParamTrigger] = t.Value;
        var results = new Dictionary<string, double[]>();
        var buffer = new IndicatorResultBuffer(results, bars.Count);
        Provider.Calculate(DssBressertProvider.Code,
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars), pars, buffer);
        return results;
    }

    /// <summary>A deterministic wandering market with real ranges — no flat windows.</summary>
    private static List<Ohlcv> Walk(int n, int seed)
    {
        var rng = new Random(seed);
        var hlc = new List<(double, double, double)>(n);
        double c = 100;
        for (int i = 0; i < n; i++)
        {
            double drift = 3 * Math.Sin(i / 9.0) + (rng.NextDouble() - 0.5) * 4;
            double next = Math.Max(5, c + drift);
            double hi = Math.Max(c, next) + rng.NextDouble() * 2 + 0.1;
            double lo = Math.Min(c, next) - rng.NextDouble() * 2 - 0.1;
            hlc.Add((hi, lo, next));
            c = next;
        }
        return Bars(hlc);
    }

    // ── 1. By hand ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Eight bars, P = 3. The stochastic of price over each 3-bar window, worked by hand:
    /// bar 2 closes at the top of 8–12 (100), bar 3 one third up 9–12 (100/3), bar 4 on the
    /// floor of 8–12 (0), bar 5 two fifths up 7–12 (40), bar 6 at the top of 7–13 (100), bar 7
    /// six sevenths up 7–14 (600/7).
    /// </summary>
    private static readonly (double, double, double)[] Eight =
    {
        (10, 8, 9), (11, 9, 10), (12, 10, 12), (12, 9, 10),
        (11, 8, 8), (10, 7, 9), (13, 9, 13), (14, 12, 13),
    };

    /// <summary>
    /// With E = 1 the EMAs are the identity (α = 2/2 = 1), so MIT IS the stochastic above and
    /// the DSS is a stochastic of THOSE over 3 bars: bar 4 sits at the bottom of {100, 100/3, 0}
    /// (0); bars 5 and 6 at the top of {100/3, 0, 40} and {0, 40, 100} (100); bar 7 has 600/7 in
    /// {40, 100, 600/7}, i.e. (600/7 − 40)/60 = 1600/21. Blank before bar 4 = 2P + E − 3.
    /// This is the case that fails if the second stochastic is missing (DSS would be the raw
    /// stochastic) or is taken over price's range instead of MIT's own.
    /// </summary>
    [Fact]
    public void WithNoSmoothing_TheDssIsAStochasticOfTheStochastic()
    {
        var r = Run(Bars(Eight), p: 3, e: 1, t: 1);
        var dss = r[DssBressertProvider.CompDss];

        for (int i = 0; i < 4; i++) Assert.True(double.IsNaN(dss[i]), $"bar {i} should be warm-up, is {dss[i]}");
        Assert.Equal(0.0, dss[4], 9);
        Assert.Equal(100.0, dss[5], 9);
        Assert.Equal(100.0, dss[6], 9);
        Assert.Equal(1600.0 / 21.0, dss[7], 9);
    }

    /// <summary>
    /// E = 2, so α = 2/3. MIT: 100, then 100 + ⅔(100/3 − 100) = 500/9, then 500/9 + ⅔(0 − 500/9)
    /// = 500/27, then 500/27 + ⅔(40 − 500/27) = 2660/81, … The second stochastic at bar 4 is 0
    /// (500/27 is the low of its window) and seeds the DSS; bar 5 reads 116/3 so the DSS is
    /// ⅔·116/3 = 232/9; bars 6 and 7 read 100, giving 2032/27 and 7432/81. Published from bar 5
    /// (2P + E − 3). An SMA, a 1/n Wilder weight or an EMA seeded at 0 or 50 gives other numbers.
    /// </summary>
    [Fact]
    public void WithAlphaTwoThirds_TheSmoothingIsAnEmaSeededAtItsFirstInput()
    {
        var r = Run(Bars(Eight), p: 3, e: 2, t: 1);
        var dss = r[DssBressertProvider.CompDss];
        var mit = r[DssBressertProvider.CompMit];

        Assert.True(double.IsNaN(dss[4]), $"bar 4 is still warm-up at E = 2, reads {dss[4]}");
        Assert.Equal(232.0 / 9.0, dss[5], 9);
        Assert.Equal(2032.0 / 27.0, dss[6], 9);
        Assert.Equal(7432.0 / 81.0, dss[7], 9);

        // MIT, published once its EMA has had E = 2 inputs (bar 3 onwards).
        Assert.True(double.IsNaN(mit[2]));
        Assert.Equal(500.0 / 9.0, mit[3], 9);
        Assert.Equal(500.0 / 27.0, mit[4], 9);
        Assert.Equal(2660.0 / 81.0, mit[5], 9);
    }

    // ── 2. The formula, written another way ─────────────────────────────────────────────────

    /// <summary>An EMA seeded at its first input, as a closed-form weighted sum rather than a recurrence.</summary>
    private static double ClosedFormEma(IReadOnlyList<double> xs, int period)
    {
        double a = 2.0 / (period + 1.0);
        int n = xs.Count;
        double sum = Math.Pow(1 - a, n - 1) * xs[0];
        for (int j = 1; j < n; j++) sum += a * Math.Pow(1 - a, n - 1 - j) * xs[j];
        return sum;
    }

    /// <summary>The cited formula, line by line, with no flat windows to worry about.</summary>
    private static (double[] Dss, double[] Trigger) Oracle(List<Ohlcv> bars, int p, int e, int t)
    {
        int n = bars.Count;
        double Stoch(double x, IEnumerable<double> hi, IEnumerable<double> lo)
        {
            double hh = hi.Max(), ll = lo.Min();
            return 100.0 * (x - ll) / (hh - ll);
        }

        var stoch1 = Enumerable.Range(p - 1, n - p + 1)
            .Select(i => Stoch(bars[i].Close,
                bars.Skip(i - p + 1).Take(p).Select(b => b.High),
                bars.Skip(i - p + 1).Take(p).Select(b => b.Low)))
            .ToList();                                         // stoch1[k] is bar k + p − 1
        var mit = Enumerable.Range(0, stoch1.Count).Select(k => ClosedFormEma(stoch1.Take(k + 1).ToList(), e)).ToList();
        var stoch2 = Enumerable.Range(p - 1, mit.Count - p + 1)
            .Select(k => Stoch(mit[k], mit.Skip(k - p + 1).Take(p), mit.Skip(k - p + 1).Take(p)))
            .ToList();                                         // stoch2[k] is bar k + 2p − 2
        var dssRaw = Enumerable.Range(0, stoch2.Count).Select(k => ClosedFormEma(stoch2.Take(k + 1).ToList(), e)).ToList();
        var trgRaw = Enumerable.Range(0, dssRaw.Count).Select(k => ClosedFormEma(dssRaw.Take(k + 1).ToList(), t)).ToList();

        var dss = Enumerable.Repeat(double.NaN, n).ToArray();
        var trg = Enumerable.Repeat(double.NaN, n).ToArray();
        int firstDss = 2 * p + e - 3, firstTrg = Math.Max(firstDss, 2 * p + t - 3);
        for (int k = 0; k < dssRaw.Count; k++)
        {
            int bar = k + 2 * p - 2;
            if (bar >= firstDss) dss[bar] = dssRaw[k];
            if (bar >= firstTrg) trg[bar] = trgRaw[k];
        }
        return (dss, trg);
    }

    /// <summary>
    /// Every bar, every component, against the formula — at the MetaTrader defaults (13/8), the
    /// TradingView ones (10/9/5), NinjaTrader's (10/3) and two odd shapes, so a P/E swap, an SMA
    /// or a dropped stage cannot pass by coinciding at one setting. The blanking is part of the
    /// oracle: NaN exactly where it says, numbers exactly where it says.
    /// </summary>
    [Theory]
    [InlineData(13, 8, 5)]
    [InlineData(10, 9, 5)]
    [InlineData(10, 3, 3)]
    [InlineData(5, 1, 2)]
    [InlineData(7, 12, 3)]
    public void EveryBar_MatchesTheFormulaWrittenAsClosedFormSums(int p, int e, int t)
    {
        var bars = Walk(260, seed: 7);
        var r = Run(bars, p, e, t);
        var (dss, trg) = Oracle(bars, p, e, t);

        int compared = 0;
        for (int i = 0; i < bars.Count; i++)
        {
            Assert.True(double.IsNaN(dss[i]) == double.IsNaN(r["DSS"][i]),
                $"P={p} E={e}: bar {i} is {(double.IsNaN(dss[i]) ? "warm-up" : "published")} by the formula, provider reads {r["DSS"][i]}");
            Assert.True(double.IsNaN(trg[i]) == double.IsNaN(r["Trigger"][i]),
                $"P={p} E={e} T={t}: trigger at bar {i} — formula {trg[i]}, provider {r["Trigger"][i]}");
            if (double.IsNaN(dss[i])) continue;
            Assert.True(Math.Abs(dss[i] - r["DSS"][i]) < 1e-8, $"P={p} E={e}: bar {i} DSS {r["DSS"][i]}, formula {dss[i]}");
            if (!double.IsNaN(trg[i]))
                Assert.True(Math.Abs(trg[i] - r["Trigger"][i]) < 1e-8, $"P={p} E={e} T={t}: bar {i} trigger {r["Trigger"][i]}, formula {trg[i]}");
            compared++;
        }
        Assert.True(compared > 200, $"only {compared} bars compared");
    }

    // ── 3. Other implementations, on real bars ──────────────────────────────────────────────

    /// <summary>Bitstamp BTC/USD daily, 2020-05-22 to 2020-10-28, as (high, low, close).</summary>
    private static readonly (double, double, double)[] Btc2020 =
    {
        (9272.97, 8924.91, 9171.96), (9316.12, 9084.83, 9183.46), (9310.91, 8669.0, 8718.77), (8984.16, 8630.0, 8903.56),
        (9012.0, 8690.0, 8840.91), (9230.21, 8810.99, 9210.0), (9625.01, 9105.79, 9577.38), (9610.0, 9330.0, 9421.04),
        (9758.27, 9326.28, 9705.1), (9711.7, 9373.96, 9446.19), (10429.26, 9416.5, 10203.64), (10241.26, 9135.68, 9525.73),
        (9699.95, 9380.99, 9668.99), (9888.0, 9430.0, 9791.92), (9856.48, 9592.74, 9619.99), (9744.39, 9525.76, 9669.85),
        (9829.31, 9356.0, 9750.04), (9813.67, 9645.0, 9783.0), (9897.46, 9566.81, 9776.84), (10011.4, 9578.0, 9894.32),
        (9981.24, 9078.91, 9264.26), (9555.0, 9230.0, 9463.07), (9500.0, 9345.71, 9473.46), (9479.59, 9235.01, 9329.99),
        (9505.4, 8900.0, 9435.15), (9596.31, 9374.9, 9530.3), (9566.53, 9230.32, 9461.29), (9478.69, 9250.0, 9374.78),
        (9429.99, 9225.15, 9304.32), (9400.0, 9138.0, 9355.46), (9421.26, 9271.52, 9283.03), (9798.76, 9267.2, 9683.12),
        (9725.3, 9571.0, 9622.81), (9666.35, 9193.99, 9286.06), (9343.49, 8989.0, 9239.48), (9289.71, 9024.61, 9158.26),
        (9189.0, 8830.63, 9009.3), (9191.72, 8933.06, 9118.01), (9240.36, 9014.92, 9188.88), (9201.95, 9060.0, 9132.96),
        (9300.0, 9089.8, 9233.38), (9268.54, 8939.0, 9092.92), (9127.11, 9027.17, 9063.08), (9196.0, 9045.94, 9138.52),
        (9147.37, 8905.0, 9076.82), (9376.34, 9060.5, 9348.22), (9385.81, 9201.78, 9257.16), (9479.57, 9228.91, 9437.16),
        (9448.5, 9157.11, 9234.9), (9318.92, 9053.14, 9283.53), (9304.95, 9180.0, 9238.3), (9347.76, 9153.48, 9306.17),
        (9351.17, 9190.49, 9233.1), (9282.08, 9095.0, 9251.59), (9276.98, 9153.38, 9190.07), (9217.5, 9026.51, 9132.87),
        (9184.28, 9073.53, 9156.5), (9214.63, 9122.72, 9177.43), (9243.36, 9106.3, 9211.99), (9224.73, 9128.21, 9162.43),
        (9442.89, 9152.23, 9391.25), (9573.0, 9280.8, 9539.51), (9690.0, 9453.38, 9617.2), (9650.55, 9474.35, 9547.91),
        (9751.82, 9532.79, 9710.98), (10200.0, 9657.0, 9944.65), (11417.11, 9933.77, 11046.19), (11263.85, 10583.48, 10939.27),
        (11358.47, 10855.81, 11111.17), (11189.0, 10832.0, 11112.12), (11457.95, 10973.8, 11356.74), (11883.36, 11228.0, 11817.49),
        (12100.0, 10559.59, 11071.66), (11480.0, 10933.26, 11236.39), (11420.0, 11000.79, 11192.42), (11795.86, 11094.35, 11755.24),
        (11913.5, 11575.57, 11770.08), (11920.0, 11219.0, 11600.85), (11817.95, 11527.0, 11776.9), (11808.28, 11527.92, 11683.03),
        (12080.0, 11510.0, 11895.12), (11945.05, 11130.0, 11392.64), (11625.0, 11150.0, 11565.35), (11806.61, 11270.91, 11793.26),
        (11862.0, 11652.01, 11779.77), (11986.93, 11667.7, 11852.2), (11938.55, 11685.97, 11914.02), (12473.0, 11771.44, 12298.38),
        (12408.53, 11823.0, 11958.1), (12022.18, 11574.4, 11757.55), (11894.12, 11675.29, 11859.37), (11887.67, 11485.49, 11531.74),
        (11694.02, 11367.0, 11667.7), (11715.09, 11524.7, 11644.65), (11832.55, 11588.61, 11761.8), (11772.22, 11112.75, 11328.88),
        (11549.8, 11250.14, 11469.3), (11602.92, 11122.72, 11330.0), (11555.61, 11277.95, 11536.58), (11592.0, 11423.64, 11471.71),
        (11715.69, 11458.62, 11708.97), (11780.0, 11573.39, 11655.0), (12065.82, 11515.0, 11923.96), (11956.45, 11155.32, 11398.44),
        (11477.35, 9987.86, 10160.0), (10646.75, 9902.43, 10456.32), (10567.9, 9850.0, 10167.19), (10353.39, 10000.0, 10260.17),
        (10415.66, 9880.0, 10377.14), (10446.18, 9825.07, 10123.96), (10353.39, 9981.38, 10232.8), (10495.75, 10220.0, 10342.59),
        (10409.44, 10200.5, 10393.04), (10482.42, 10275.0, 10443.33), (10576.78, 10214.9, 10334.78), (10765.03, 10250.13, 10675.15),
        (10940.01, 10611.17, 10783.83), (11100.0, 10662.81, 10951.54), (11054.0, 10739.23, 10940.47), (11040.18, 10816.54, 10931.2),
        (11183.01, 10890.53, 11077.22), (11079.25, 10755.9, 10923.5), (10996.98, 10286.56, 10411.29), (10573.05, 10353.39, 10534.31),
        (10541.39, 10138.3, 10233.42), (10795.53, 10193.2, 10744.02), (10762.65, 10553.14, 10689.81), (10830.0, 10655.29, 10729.09),
        (10807.14, 10593.58, 10780.66), (10950.0, 10628.52, 10689.48), (10865.0, 10636.66, 10836.42), (10847.5, 10658.56, 10778.22),
        (10940.22, 10437.08, 10622.71), (10667.53, 10380.0, 10578.54), (10605.79, 10500.0, 10553.66), (10699.88, 10524.81, 10669.63),
        (10805.82, 10620.71, 10795.0), (10801.86, 10525.0, 10606.33), (10682.56, 10547.71, 10669.14), (10957.0, 10535.06, 10925.2),
        (11118.49, 10826.4, 11052.0), (11496.06, 11059.62, 11301.2), (11447.44, 11272.11, 11372.93), (11731.7, 11175.99, 11535.98),
        (11567.4, 11314.53, 11427.98), (11555.1, 11287.42, 11428.84), (11623.6, 11250.01, 11511.11), (11548.65, 11200.0, 11321.86),
        (11415.48, 11264.01, 11365.8), (11526.34, 11352.79, 11519.97), (11840.0, 11409.94, 11764.14), (12059.94, 11680.0, 11921.11),
        (13235.0, 11899.1, 12802.67), (13208.25, 12686.73, 12983.26), (13037.26, 12729.82, 12941.59), (13182.83, 12887.38, 13122.73),
        (13363.53, 12903.74, 13039.09), (13250.0, 12773.5, 13066.73), (13788.0, 13057.09, 13647.9), (13864.89, 12892.0, 13274.73),
    };

    /// <summary>
    /// The TradingView form —
    /// <c>ta.ema(ta.stoch(ta.ema(ta.stoch(close, high, low, P), E), …, P), E)</c> with
    /// <c>ta.ema(dss, 5)</c> for the trigger — ported to Python from the published Pine and run
    /// on the bars above (bar index, DSS, trigger). It seeds its EMAs at the first input, as this
    /// provider does, so the two should agree on every published bar, the first included.
    /// </summary>
    public static IEnumerable<object[]> PineReference() => new[]
    {
        new object[] { 31, 29.58094834261994, 24.627099579567197 },
        new object[] { 32, 45.2296264887044, 31.494608549279597 },
        new object[] { 40, 17.890481128298486, 26.921040275843506 },
        new object[] { 60, 45.105938921461714, 56.1146206526264 },
        new object[] { 80, 47.79182409165953, 56.3764092064478 },
        new object[] { 100, 3.839839516097285, 5.254668177549123 },
        new object[] { 120, 72.24718085320343, 52.963558102400114 },
        new object[] { 140, 79.90147485446019, 69.47842527326907 },
        new object[] { 159, 94.58844851038245, 96.27385182853968 },
    };

    [Theory]
    [MemberData(nameof(PineReference))]
    public void OnRealBars_EveryPublishedValueIsTheTradingViewValue(int bar, double dss, double trigger)
    {
        var r = Run(Bars(Btc2020));
        Assert.Equal(dss, r["DSS"][bar], 9);
        Assert.Equal(trigger, r["Trigger"][bar], 9);
    }

    /// <summary>
    /// MetaQuotes' MT4 file starts its EMAs at 0 and the MT5 file at 50, ported line for line.
    /// On the first published bar they read 61.88 and 39.10 where this reads 29.58 — the seed,
    /// amplified by the second stochastic, and the reason this one starts from the first input.
    /// By the last bar all three have forgotten where they started: MT4 94.58844851038482, MT5
    /// 94.58844851038282, and the MT4 MIT line 85.82400532062633.
    /// </summary>
    [Fact]
    public void OnRealBars_TheMetaTraderVersionsAgreeOnceTheirSeedHasDecayed()
    {
        var r = Run(Bars(Btc2020));
        Assert.Equal(94.58844851038482, r["DSS"][159], 9);
        Assert.Equal(94.58844851038282, r["DSS"][159], 9);
        Assert.Equal(85.82400532062633, r["MIT"][159], 9);
        // Pine's first EMA (xPreCalc), which is this MIT, on its first published bar.
        Assert.Equal(57.54296541888232, r["MIT"][19], 9);
        Assert.True(double.IsNaN(r["MIT"][18]), $"MIT should be warm-up at bar 18, reads {r["MIT"][18]}");
    }

    // ── 4. Warm-up and the edges ────────────────────────────────────────────────────────────

    /// <summary>
    /// 31 blank bars at the defaults (2P + E − 3), and not one more: a provider that blanked a
    /// full extra EMA period would be silent for a week of daily bars it could have spoken.
    /// </summary>
    [Fact]
    public void AtTheDefaults_TheFirst31BarsAreBlankAndNoneAfter()
    {
        var r = Run(Walk(200, seed: 3));
        var dss = r["DSS"];
        Assert.Equal(31, Enumerable.Range(0, dss.Length).TakeWhile(i => double.IsNaN(dss[i])).Count());
        Assert.DoesNotContain(dss.Skip(31), double.IsNaN);
        Assert.DoesNotContain(r["Trigger"].Skip(31), double.IsNaN);
    }

    /// <summary>
    /// A window with no range has no stochastic, and the line HOLDS. A halted market, here a run
    /// of 40 identical prints: once MIT's own 13-bar window has nothing but held values in it, the
    /// DSS stops moving and stays where it was — no NaN, no invented 50, and it resumes when the
    /// market does. Substituting 50 for the undefined stochastic (what a naive guard does) walks
    /// the line towards the middle on no information and fails this.
    /// </summary>
    [Fact]
    public void AFlatMarket_HoldsTheLine_AndNeverProducesANaN()
    {
        var walk = Walk(160, seed: 11);
        var hlc = walk.Select(b => (b.High, b.Low, b.Close)).ToList();
        double frozen = hlc[59].Item3;
        for (int i = 60; i < 100; i++) hlc[i] = (frozen, frozen, frozen);
        var r = Run(Bars(hlc));
        var dss = r["DSS"];

        Assert.DoesNotContain(dss.Skip(31), double.IsNaN);
        // Price windows are all flat from bar 72, so MIT holds from there; MIT's own 13-bar
        // windows hold nothing but that one value from bar 83, so the DSS holds from there.
        Assert.NotEqual(dss[81], dss[82]);
        for (int i = 83; i < 100; i++)
            Assert.True(dss[i] == dss[82], $"bar {i} reads {dss[i]}; the DSS should hold at {dss[82]} through the flat stretch");
        Assert.NotEqual(dss[100], dss[130]);   // and it moves again once the market does
    }

    /// <summary>
    /// A series that STARTS flat — the first weeks of an exchange's history often are — has
    /// nothing to seed from until the first real range, and a held bar is not an input. So the
    /// warm-up runs from the first real stochastic: with 20 identical opening prints the first
    /// window with a range ends on bar 20, and the DSS is blank through bar 38 and published from
    /// bar 39 (20 + 2P + E − 3 − (P − 1)).
    /// </summary>
    [Fact]
    public void AFlatOpening_CountsTheWarmUpFromTheFirstRealRange()
    {
        var walk = Walk(120, seed: 5);
        var hlc = walk.Select(b => (b.High, b.Low, b.Close)).ToList();
        double first = hlc[20].Item3;
        for (int i = 0; i < 20; i++) hlc[i] = (first, first, first);
        var dss = Run(Bars(hlc))["DSS"];

        Assert.True(double.IsNaN(dss[38]), $"bar 38 should still be warm-up, reads {dss[38]}");
        Assert.False(double.IsNaN(dss[39]), "bar 39 should be the first published bar");
        Assert.DoesNotContain(dss.Skip(39), double.IsNaN);
    }

    /// <summary>A bar with a missing price holds the line for the bars whose window it is in — it does not poison the rest.</summary>
    [Fact]
    public void AMissingPrice_DoesNotPropagate()
    {
        var walk = Walk(150, seed: 2);
        var hlc = walk.Select(b => (b.High, b.Low, b.Close)).ToList();
        hlc[70] = (double.NaN, double.NaN, double.NaN);
        var r = Run(Bars(hlc));
        Assert.DoesNotContain(r["DSS"].Skip(31), double.IsNaN);
        Assert.DoesNotContain(r["Trigger"].Skip(31), double.IsNaN);
    }

    // ── 5. What it is to the rest of the app ────────────────────────────────────────────────

    private static IndicatorMetadata Meta() => Provider.GetIndicators().Single();

    [Fact]
    public void TheDefaultsAreTheMetaTraderPairWithATradingViewTrigger()
    {
        var p = Meta().Parameters.ToDictionary(x => x.Name, x => Convert.ToInt32(x.DefaultValue));
        Assert.Equal(13, p[DssBressertProvider.ParamStoch]);
        Assert.Equal(8, p[DssBressertProvider.ParamEma]);
        Assert.Equal(5, p[DssBressertProvider.ParamTrigger]);
    }

    /// <summary>The Stochastic's levels, which every DSS implementation that draws levels draws at 80 and 20.</summary>
    [Fact]
    public void TheLevelsAre80And20WithAMidline_AndTheExtremesCarryZoneTexture()
    {
        var levels = Provider.GetDefaultLevels("DSS_BRESSERT");
        Assert.Equal(new[] { ("Overbought", 80.0), ("Midpoint", 50.0), ("Oversold", 20.0) },
            levels.Select(l => (l.Name, l.Value)));
        Assert.All(levels.Where(l => l.Name != "Midpoint"), l => Assert.True(l.ZoneNoiseAmount > 0));
        Assert.Empty(Provider.GetDefaultLevels("STOCH"));
    }

    /// <summary>
    /// The DSS is what is narrated and what clicks at its levels; the trigger is a second line;
    /// MIT is there for a MetaTrader user and hidden. Order matters: the narrator speaks for the
    /// FIRST oscillator line of a series.
    /// </summary>
    [Fact]
    public void TheDssIsTheFirstLineAndTheOnlyOneThatClicksAtItsLevels()
    {
        var comps = Meta().Components;
        Assert.Equal(new[] { "DSS", "Trigger", "MIT" }, comps.Select(c => c.Name));
        Assert.All(comps, c => Assert.Equal(ComponentDisplayType.Oscillator, c.DisplayType));
        Assert.True(comps[0].DefaultTriggerBoundaryClick);
        Assert.NotEqual(true, comps[1].DefaultTriggerBoundaryClick);
        Assert.True(comps[0].IsVisible && comps[1].IsVisible);
        Assert.False(comps[2].IsVisible);
        Assert.Equal("Smoothed %K", comps[2].DisplayName);
    }

    private static readonly IndicatorModelFactory Factory = new(
        new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
        new MockIndicatorPreferencesService());

    private static ChartSeries Built(IIndicatorProvider provider, IndicatorMetadata meta)
    {
        var series = Factory.CreateSeriesFromMetadata(meta, meta.Name, PaneAssignmentService.PaneFor(meta),
            new List<(string, string)>(), null, null);
        foreach (var d in provider.GetDefaultLevels(meta.Code.ToUpperInvariant()))
            series.Config.Levels.Add(new LevelConfig
            {
                Name = d.Name, Value = d.Value, ColorHex = d.ColorHex, DashStyle = d.Dash,
                IsVisible = true, PlayEarcon = d.PlayEarcon, EarconVolume = d.EarconVolume,
                ZoneNoiseAmount = d.ZoneNoiseAmount, ZoneNoiseType = d.ZoneNoiseType,
                AboveLabel = d.AboveLabel, BelowLabel = d.BelowLabel,
            });
        return series;
    }

    /// <summary>
    /// "Whatever Stochastic gets": the DSS line, built the way the Add Indicator dialog builds
    /// it, sounds like the Stochastic's %K — same voice, same pitch mapping, same neutral, same
    /// level click, same pane bounds — and the trigger sounds like %D. A sound setting that
    /// differs between two 0–100 stochastics is a difference the listener has to learn for no
    /// reason.
    /// </summary>
    [Fact]
    public void ItSoundsLikeTheStochastic()
    {
        var stochProvider = new SkenderBoundedOscillatorProvider();
        var stoch = Built(stochProvider, stochProvider.GetIndicators().Single(m => m.Code == "Stoch"));
        var dss = Built(Provider, Meta());

        void Same(ComponentConfig a, ComponentConfig b)
        {
            Assert.Equal(a.Waveform, b.Waveform);
            Assert.Equal(a.AboveReferenceWaveform, b.AboveReferenceWaveform);
            Assert.Equal(a.BelowReferenceWaveform, b.BelowReferenceWaveform);
            Assert.Equal(a.EnvelopeType, b.EnvelopeType);
            Assert.Equal(a.PitchMapping, b.PitchMapping);
            Assert.Equal(a.AmplitudeMapping, b.AmplitudeMapping);
            Assert.Equal(a.BaseFrequency, b.BaseFrequency);
            Assert.Equal(a.FreqMultiplier, b.FreqMultiplier);
            Assert.Equal(a.TriggerBoundaryClick, b.TriggerBoundaryClick);
            Assert.Equal(a.NoiseAmount, b.NoiseAmount);
            Assert.Equal(a.ReferenceLevel, b.ReferenceLevel);
            Assert.Equal(a.PlaybackLayer, b.PlaybackLayer);
        }
        Same(stoch.Components.First(c => c.Name == "Oscillator"), dss.Components.First(c => c.Name == "DSS"));
        Same(stoch.Components.First(c => c.Name == "Signal"), dss.Components.First(c => c.Name == "Trigger"));
        Assert.Equal((stoch.Config.RangeMin, stoch.Config.RangeMax), (dss.Config.RangeMin, dss.Config.RangeMax));
        Assert.Equal("Pane_DSS_BRESSERT", dss.Config.Pane);
        Assert.Equal(stoch.Config.Levels.Select(l => (l.Name, l.Value, l.ZoneNoiseAmount, l.PlayEarcon)),
                     dss.Config.Levels.Select(l => (l.Name, l.Value, l.ZoneNoiseAmount, l.PlayEarcon)));
    }

    /// <summary>The detail sentence: zone, direction, trigger, and the cross on the bar it happens.</summary>
    [Fact]
    public void TheDetailFactSaysTheZoneAndTheTriggerCross()
    {
        var results = new Dictionary<string, double[]>
        {
            ["DSS"] = new[] { 70.0, 85.0, 83.0 },
            ["Trigger"] = new[] { 80.0, 82.0, 84.0 },
        };
        string Fact(int i) => Provider.GetDetailFact("DSS_BRESSERT", ReadOnlySpan<Ohlcv>.Empty, results, i, new());

        Assert.Equal("Overbought at DSS 85.0, rising. Trigger 82.0. DSS crossed above trigger.", Fact(1));
        Assert.Equal("Overbought at DSS 83.0, falling. Trigger 84.0. DSS crossed below trigger.", Fact(2));
        results["DSS"][0] = double.NaN;
        Assert.Equal("Calculating...", Fact(0));
    }

    // ── 6. Narrated like the Stochastic ─────────────────────────────────────────────────────

    private sealed class CapturingSpeechRouter : ISpeechFeedbackRouter
    {
        public List<string> Spoken { get; } = new();
        public void Speak(string message, bool interrupt = false, SpeechChannel channel = SpeechChannel.Chart) => Spoken.Add(message);
        public void SpeakPoint(WorkspaceState s, WorkspaceState? p, ChartSeries ser, Ohlcv pt, string pfx = "") { }
        public void SpeakProfile(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int bin, string pfx = "") { }
        public void SpeakHeatmap(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int di, int bin, string pfx = "") { }
    }

    /// <summary>
    /// With narration on, the DSS crossing 80 is spoken on the bar close — once, for the DSS, not
    /// again when the trigger follows it. The series is built through the real factory with the
    /// provider's own levels and its own computed values; nothing here is a hand-made config.
    /// </summary>
    [Fact]
    public void CrossingOverbought_IsNarratedOnceForTheDss()
    {
        var bars = Walk(260, seed: 21);
        var r = Run(bars);
        var dssV = r["DSS"];
        var trgV = r["Trigger"];

        bool Up(double[] v, int i) => !double.IsNaN(v[i - 1]) && v[i - 1] < 80 && v[i] >= 80;
        // A DSS crossing at bar k that the trigger FOLLOWS two or three bars later, inside the
        // stretch narrated below: a narrator that spoke for both lines would say it twice, and
        // one that spoke for the trigger alone would say it late.
        int k = Enumerable.Range(40, bars.Count - 46)
            .FirstOrDefault(i => Up(dssV, i) && !Up(trgV, i) && !Up(trgV, i + 1)
                && !Enumerable.Range(i + 1, 5).Any(j => Up(dssV, j))
                && Enumerable.Range(i + 2, 2).Any(j => Up(trgV, j)));
        Assert.True(k > 0, "the synthetic walk has no DSS crossing of 80 followed by a trigger crossing");

        var built = Built(Provider, Meta());
        built.Config.IsAutoNarrated = true;
        built.Config.FriendlyName = "DSS Bressert";

        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        var router = new CapturingSpeechRouter();
        _ = new AutoNarrationService(store, bus, router, new IndicatorContextAnalyzer());

        WorkspaceState St(int count)
        {
            var buf = new SeriesDataBuffer { SeriesId = built.Config.Id };
            foreach (var (name, values) in r) buf.ComponentData[name] = values.Take(count).ToArray();
            return WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(bars.Take(count)),
                ActiveSeries = ImmutableList.Create(new ChartSeries(built.Config, buf)),
                FocusedSeriesId = built.Config.Id,
                CurrentDataIndex = count - 1,
                InitStatus = InitializationStatus.Ready,
                DataStatus = DataStatus.Ready,
                IsSpeechEnabled = true,
            };
        }

        // Which emit each crossing sentence arrived on: count = bars loaded at the time.
        var heardAt = new List<int>();
        for (int count = k - 1; count <= k + 5; count++)
        {
            int before = router.Spoken.Count;
            store.EmitState(St(count));
            bus.Publish(new RedrawEvent());
            heardAt.AddRange(router.Spoken.Skip(before)
                .Where(m => m.Contains("crossed above overbought, 80", StringComparison.OrdinalIgnoreCase))
                .Select(_ => count));
        }

        // Bar k is the newest bar when count = k + 1, and its close is when bar k + 1 arrives.
        Assert.True(heardAt.Count == 1 && heardAt[0] <= k + 2,
            $"expected the DSS crossing at bar {k} (trigger follows at bar {Enumerable.Range(k + 2, 2).First(j => Up(trgV, j))}) " +
            $"spoken once, on bar {k}'s close; heard it with these bar counts loaded: [{string.Join(", ", heardAt)}]:\n  " +
            string.Join("\n  ", router.Spoken));
    }
}
