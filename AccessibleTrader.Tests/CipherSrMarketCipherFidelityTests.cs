using System.Globalization;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cipher SR draws what Market Cipher SR draws.</b>
///
/// <para>
/// The evidence is Fixtures/mcsr_observations.json: Market Cipher SR v2.2.0 as drawn in two
/// tutorial videos (Coinbase BTC-USD, a 10-minute chart on 2022-01-28/29 and a 12-minute chart on
/// 2022-03-19/20), each purple (resistance) and yellow (support) dot run digitised as the price it
/// sits at and the first and last bar it covers, plus values MCSR printed in its legend. The
/// candles are Coinbase's own for the same minutes (Fixtures/coinbase_BTC_USD_*.csv).
/// </para>
///
/// <para>
/// MCSR computes its levels on 15-minute bars ("Resolution 15"), so the levels are computed here on
/// the 15-minute candles, and a run is matched on the 15-minute bars that closed inside it.
/// </para>
/// </summary>
public sealed class CipherSrMarketCipherFidelityTests
{
    private static readonly CipherSrProvider Provider = new();

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    internal static List<Ohlcv> Csv(string name)
    {
        var bars = new List<Ohlcv>();
        foreach (var line in File.ReadLines(Fixture(name)))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var f = line.Split(',');
            var t = DateTime.Parse(f[0], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            double P(int k) => double.Parse(f[k], CultureInfo.InvariantCulture);
            bars.Add(new Ohlcv(DateTime.SpecifyKind(t, DateTimeKind.Utc), P(1), P(2), P(3), P(4), P(5)));
        }
        return bars;
    }

    private static Dictionary<string, double[]> Compute(List<Ohlcv> bars, Dictionary<string, object>? pars = null)
    {
        var results = new Dictionary<string, double[]>();
        Provider.Calculate("CIPHER_SR", System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
            pars ?? new Dictionary<string, object>(), new IndicatorResultBuffer(results, bars.Count));
        return results;
    }

    private static double[] Get(Dictionary<string, double[]> r, string key)
        => r.TryGetValue(key, out var a) ? a : Array.Empty<double>();

    private static JObject Observations() => JObject.Parse(File.ReadAllText(Fixture("mcsr_observations.json")));

    /// <summary>
    /// Share of the observed dot bars on which our level is MCSR's, within 0.06% (a third of a
    /// typical 15-minute bar). <paramref name="shiftSeconds"/> moves the observed times, for the
    /// frame whose time axis is only readable to a bar.
    /// </summary>
    private static (double Share, List<string> Detail) Agreement(JToken set, long shiftSeconds)
    {
        var bars = Csv((string)set["bars"]!);
        var r = Compute(bars);
        int hit = 0, total = 0;
        var detail = new List<string>();
        foreach (var run in set["runs"]!)
        {
            string side = (string)run[0]!;
            double price = (double)run[1]!;
            var t0 = DateTime.Parse((string)run[2]!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal).AddSeconds(shiftSeconds);
            var t1 = DateTime.Parse((string)run[3]!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal).AddSeconds(shiftSeconds);
            var line = Get(r, side == "R" ? CipherSrProvider.CompResistanceLine : CipherSrProvider.CompSupportLine);
            int h = 0, n = 0;
            for (int k = 0; k < bars.Count; k++)
            {
                var close = bars[k].Date.AddMinutes(15);
                if (close < t0 || close > t1) continue;
                n++;
                if (k < line.Length && !double.IsNaN(line[k]) && Math.Abs(line[k] - price) / price < 0.0006) h++;
            }
            hit += h; total += n;
            detail.Add($"{side} {price:F2} {t0:MM-dd HH:mm}-{t1:HH:mm}: {h}/{n}");
        }
        return (total == 0 ? 0 : (double)hit / total, detail);
    }

    /// <summary>
    /// <b>The dot levels are Market Cipher SR's on most of the bars where MCSR is seen to draw one.</b>
    ///
    /// <para>
    /// The rule is no longer fitted to these frames: on 2026-10-23 it was read from Market Cipher SR
    /// 2.4.0's own plotted values on TradingView (a stepped two-bar pivot on volume above its
    /// VolumeMA average, drawn three bars later; every bar of a 5,076-bar BTCUSD 15-minute history
    /// matched — that export is not in the repository). These video frames are an independent check
    /// against an older version (v2.2.0) and against Coinbase's candles rather than TradingView's:
    /// 96.5% on the 10-minute frame and 69.8% on the 12-minute frame, floors just under. The rule
    /// fitted to these frames before reached 68.6% and 62.5%; the design before that, 13% and 0%.
    /// </para>
    /// </summary>
    [Fact]
    public void OnRealMarketCipherFrames_TheDotLevelsAreMarketCiphersOnMostObservedBars()
    {
        var sets = Observations()["sets"]!.ToList();
        var (fit, fitDetail) = Agreement(sets[0], 0);
        var holdout = new[] { -720L, 0L, 720L }.Select(s => Agreement(sets[1], s)).OrderByDescending(a => a.Share).First();

        Assert.True(fit >= 0.93,
            $"agreement with Market Cipher SR on the fitted frame is {fit:P0} (floor 93%):\n  " + string.Join("\n  ", fitDetail));
        Assert.True(holdout.Share >= 0.66,
            $"agreement with Market Cipher SR on the held-out frame is {holdout.Share:P0} (floor 66%):\n  " + string.Join("\n  ", holdout.Detail));
    }

    /// <summary>
    /// <b>The session range is the UTC day's high and low so far, and on the bar the video was
    /// recorded (2022-01-29, 20:15-20:30 UTC, the live day) it is the values MCSR printed:
    /// 38226.62, 37300.00 and their midpoint 37763.31.</b> Ours is the RUNNING range: on history MCSR
    /// draws the whole day's from the first bar, which only a look-ahead can; on the live day the two
    /// agree. The first bar of a day carries only that bar's own high and low.
    /// </summary>
    [Fact]
    public void TheRangeIsTheUtcDaysHighAndLow_MatchingMarketCiphersLegend()
    {
        var bars = Csv("coinbase_BTC_USD_15m_2022-01-20_31.csv");
        var r = Compute(bars);
        var legend = Observations()["legend"]!;
        int last = bars.FindIndex(b => b.Date == new DateTime(2022, 1, 29, 20, 15, 0, DateTimeKind.Utc));
        int first = bars.FindIndex(b => b.Date == new DateTime(2022, 1, 29, 0, 0, 0, DateTimeKind.Utc));
        Assert.True(last > 0 && first > 0, "fixture does not cover 2022-01-29");

        Assert.Equal((double)legend["rangeHigh_2022-01-29"]!, Get(r, CipherSrProvider.CompRangeHigh)[last], 2);
        Assert.Equal((double)legend["rangeLow_2022-01-29"]!, Get(r, CipherSrProvider.CompRangeLow)[last], 2);
        Assert.Equal((double)legend["rangeMid_2022-01-29"]!, Get(r, CipherSrProvider.CompRangeMid)[last], 2);

        Assert.Equal(bars[first].High, Get(r, CipherSrProvider.CompRangeHigh)[first]);
        Assert.Equal(bars[first].Low, Get(r, CipherSrProvider.CompRangeLow)[first]);
    }

    /// <summary>
    /// <b>VWAP is the day's volume-weighted hlc3, reset at 00:00 UTC, on the chart's own bars</b> — the
    /// two values MCSR printed on its 10-minute chart, reproduced to the cent from Coinbase's 10-minute
    /// candles.
    /// </summary>
    [Fact]
    public void VwapIsTheUtcSessionsVolumeWeightedTypicalPrice_MatchingMarketCiphersLegend()
    {
        var bars = Csv("coinbase_BTC_USD_10m_2022-01-28_29.csv");
        var vwap = Get(Compute(bars), CipherSrProvider.CompVwap);
        var legend = Observations()["legend"]!;
        int a = bars.FindIndex(b => b.Date == new DateTime(2022, 1, 28, 23, 50, 0, DateTimeKind.Utc));
        int b2 = bars.FindIndex(b => b.Date == new DateTime(2022, 1, 29, 20, 20, 0, DateTimeKind.Utc));
        // Within a cent and a half: TradingView prints two decimals of a value it computed from its own
        // copy of the volumes.
        Assert.InRange(vwap[a], (double)legend["vwap_2022-01-28T23:50Z"]! - 0.015, (double)legend["vwap_2022-01-28T23:50Z"]! + 0.015);
        Assert.InRange(vwap[b2], (double)legend["vwap_2022-01-29T20:20Z"]! - 0.015, (double)legend["vwap_2022-01-29T20:20Z"]! + 0.015);

        int midnight = bars.FindIndex(b => b.Date == new DateTime(2022, 1, 29, 0, 0, 0, DateTimeKind.Utc));
        var m = bars[midnight];
        Assert.Equal((m.High + m.Low + m.Close) / 3, vwap[midnight], 6);
    }

    /// <summary>
    /// <b>A level holds until a newer one replaces it — including through closes beyond it.</b> MCSR
    /// kept a 36614.02 resistance for 17 hours with price up to 4% above it (its legend printed the
    /// value at 2022-01-29 05:00 UTC). A run of dots is the level's age, so a close is not the end of
    /// it; the narrator speaks the close crossing it instead. On the real 15-minute history, levels
    /// are carried through closes beyond them, and the line is never empty once it has a level.
    /// </summary>
    [Fact]
    public void ALevelHoldsThroughClosesBeyondItUntilReplaced()
    {
        var bars = Csv("coinbase_BTC_USD_15m_2022-01-20_31.csv");
        var r = Compute(bars);
        var res = Get(r, CipherSrProvider.CompResistanceLine);
        var sup = Get(r, CipherSrProvider.CompSupportLine);

        int carriedBeyond = 0, emptied = 0;
        for (int k = 1; k < bars.Count; k++)
        {
            if (!double.IsNaN(res[k - 1]) && double.IsNaN(res[k])) emptied++;
            if (!double.IsNaN(sup[k - 1]) && double.IsNaN(sup[k])) emptied++;
            if (!double.IsNaN(res[k]) && res[k] == res[k - 1] && bars[k].Close > res[k]) carriedBeyond++;
            if (!double.IsNaN(sup[k]) && sup[k] == sup[k - 1] && bars[k].Close < sup[k]) carriedBeyond++;
        }
        Assert.Equal(0, emptied);
        Assert.True(carriedBeyond > 20, $"only {carriedBeyond} bars carried a level price had closed beyond");
    }

    /// <summary>
    /// <b>On a daily chart Range and VWAP are empty and the levels still come.</b> A UTC session on a
    /// daily chart is one bar: its range and VWAP would only restate the bar, and a VWAP that is
    /// every bar's own typical price would be crossed every day.
    /// </summary>
    [Fact]
    public void OnADailyChart_RangeAndVwapAreOffAndTheLevelsRemain()
    {
        var r = Compute(CipherSrSpeechTests.BtcDaily());
        foreach (var name in new[] { CipherSrProvider.CompRangeHigh, CipherSrProvider.CompRangeLow, CipherSrProvider.CompRangeMid, CipherSrProvider.CompVwap })
            Assert.True(Get(r, name).All(double.IsNaN), $"{name} has values on a daily chart");
        Assert.Contains(Get(r, CipherSrProvider.CompResistanceLine), v => !double.IsNaN(v));
        Assert.Contains(Get(r, CipherSrProvider.CompSupportLine), v => !double.IsNaN(v));
    }

    /// <summary>
    /// <b>A zone line speaks its age — the run of dots MCSR draws — and the side price is on.</b>
    /// "97939.00, price below, held 152 days": on Cody's BTC daily chart at 2026-06-15.
    /// </summary>
    [Fact]
    public void AZoneLineSpeaksTheLevelsAgeAndTheSidePriceIsOn()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var r = Compute(bars);
        int last = bars.Count - 1;
        var res = Get(r, CipherSrProvider.CompResistanceLine);
        int start = last;
        while (start > 0 && res[start - 1] == res[last]) start--;
        int days = (int)Math.Round((bars[last].Date - bars[start].Date).TotalDays);

        var data = new Dictionary<string, double[]>(r, StringComparer.OrdinalIgnoreCase) { ["__live_close"] = new[] { bars[last].Close } };
        string speech = Provider.GetComponentSpeech(CipherSrProvider.CompResistanceLine, res[last], bars[last], data, last) ?? "";

        Assert.True(days >= 2, $"the last resistance is only {days} days old — the fixture does not reach the case");
        Assert.Contains($"held {days} days", speech);
        Assert.Contains(bars[last].Close < res[last] ? "price below" : "price above", speech);
    }

    /// <summary>
    /// <b>The retired pivot dots say why they went.</b> A strategy or alert saved on "Resistance" or
    /// "Support" is told to use the zone line instead, and the dots are gone from the metadata.
    /// </summary>
    [Fact]
    public void TheRetiredPivotDotsAreGoneAndSayWhy()
    {
        var names = Provider.GetIndicators().Single().Components.Select(c => c.Name).ToList();
        Assert.DoesNotContain(CipherSrProvider.RetiredResistanceDot, names);
        Assert.DoesNotContain(CipherSrProvider.RetiredSupportDot, names);
        Assert.Contains("Resistance Zone", SignalCatalog.Retired["CIPHER_SR.Resistance"]);
        Assert.Contains("Support Zone", SignalCatalog.Retired["CIPHER_SR.Support"]);
    }
}
