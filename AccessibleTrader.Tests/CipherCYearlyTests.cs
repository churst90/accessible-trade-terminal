using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Yearly lows found from the price (2026-10-28).</b> The original's yearly lows are a table of
/// dates that stops at its newest (December 2023 at the latest), and an asset it does not tune has
/// none. Cody: "it would be helpful to have the yearly lows be a switch to have the indicator pick
/// the low rather than hand entered". "Yearly lows found from price" finds the ones after the
/// newest date, and all of them on an asset with no dates (<see cref="CipherCEngine.YearlyFromPrice"/>).
/// </summary>
public class CipherCYearlyTests
{
    private static readonly double Cycle = 4 * 365.25;

    /// <summary>A bar a day from <paramref name="from"/> (1 January 2000) for <paramref name="years"/> years with its
    /// low on a 4-year wave, lowest on <paramref name="trough"/> and every 4 years from it; <paramref name="flat"/>
    /// holds the low for that many days at each trough.</summary>
    private static Ohlcv[] Wave(DateTime trough, int years = 13, int flat = 0, DateTime? from = null)
    {
        var start = from ?? new DateTime(2000, 1, 1);
        var bars = new Ohlcv[(int)(years * 365.25)];
        for (int i = 0; i < bars.Length; i++)
        {
            var t = start.AddDays(i);
            double phase = ((t - trough).TotalDays % Cycle + Cycle) % Cycle;
            if (phase > Cycle / 2) phase -= Cycle;
            if (Math.Abs(phase) < flat / 2.0) phase = 0;
            double low = 100 - 50 * Math.Cos(2 * Math.PI * phase / Cycle);
            bars[i] = new Ohlcv(t, low + 1, low + 2, low, low + 1, 1000);
        }
        return bars;
    }

    private static int Bar(Ohlcv[] bars, DateTime d) => CipherCEngine.FirstBarOnOrAfter(bars, d);

    /// <summary>Bars from the last to the first on or after <paramref name="d"/>, a bar a day past the chart's end.</summary>
    private static int Ahead(Ohlcv[] bars, DateTime d)
        => d <= bars[^1].Date ? Bar(bars, d) - (bars.Length - 1) : (int)Math.Ceiling((d - bars[^1].Date).TotalDays);

    /// <summary>Each trough of a 4-year wave is a yearly low, known 0.15 of a cycle (219 days) after it.</summary>
    [Fact]
    public void A_yearly_low_is_the_first_low_that_stays_lowest_for_a_while_known_that_long_after()
    {
        var bars = Wave(new DateTime(2001, 3, 1));
        var run = CipherCEngine.YearlyFromPrice(bars, 4);
        var troughs = new[] { new DateTime(2001, 3, 1), new DateTime(2005, 3, 1), new DateTime(2009, 3, 1) };
        Assert.Equal(troughs.Select(d => Bar(bars, d)), run.Lows);
        var known = troughs.Select(d => Bar(bars, d.AddDays(Cycle * CipherCEngine.YearlyPivotCycles))).ToList();
        Assert.Equal(known, run.ConfirmedAt);
        Assert.All(run.Lows.Zip(run.ConfirmedAt), p => Assert.Equal(bars[p.First].Low, run.Confirmed[p.Second]));
        // Known from its confirmation, not before.
        Assert.Equal(-1, run.LastLowAt[known[0] - 1]);
        Assert.Equal(run.Lows[0], run.LastLowAt[known[0]]);
        Assert.Equal(run.Lows[0], run.LastLowAt[known[1] - 1]);
        // The 2013 trough is past the chart's end.
        Assert.Equal(3, run.Lows.Count);
    }

    /// <summary>A trough too close to the chart's start is not a yearly low: the first must stay
    /// lowest for 0.15 of a cycle before it too. A low held flat for days is a yearly low on its first
    /// day; when the search starts partway through it, the low began too soon and is not one.</summary>
    [Fact]
    public void The_first_yearly_low_needs_a_full_look_back_and_equal_lows_count_the_first()
    {
        var early = Wave(new DateTime(2000, 3, 1));
        var run = CipherCEngine.YearlyFromPrice(early, 4);
        Assert.Equal(Bar(early, new DateTime(2004, 3, 1)), run.Lows[0]);

        var flat = Wave(new DateTime(2001, 3, 1), flat: 9);
        var f = CipherCEngine.YearlyFromPrice(flat, 4);
        Assert.Equal(Bar(flat, new DateTime(2001, 3, 1).AddDays(-4)), f.Lows[0]);
        var dated = new DateTime(2005, 3, 1).AddDays(-2 - Cycle * CipherCEngine.YearlyGapCycles).Date;
        var mid = CipherCEngine.YearlyFromPrice(flat, 4, new[] { dated });
        Assert.Equal(new[] { Bar(flat, dated), Bar(flat, new DateTime(2009, 3, 1).AddDays(-4)) }, mid.Lows);
    }

    /// <summary>With the original's dates the dated lows come first, on their own bars, and the search
    /// starts 0.8 of a cycle (3.2 years) after the newest: from a date in January 2003 the 2005
    /// trough is too soon (2.2 years), so the next yearly low is the 2009 one.</summary>
    [Fact]
    public void After_the_originals_dates_the_next_yearly_low_is_found_from_the_newest()
    {
        var bars = Wave(new DateTime(2001, 3, 1));
        var dates = new[] { new DateTime(2001, 2, 20), new DateTime(2003, 1, 1) };
        var run = CipherCEngine.YearlyFromPrice(bars, 4, dates);
        Assert.Equal(new[] { Bar(bars, dates[0]), Bar(bars, dates[1]), Bar(bars, new DateTime(2009, 3, 1)) }, run.Lows);
        Assert.Equal(run.Lows[0], run.ConfirmedAt[0]);
        Assert.Equal(run.Lows[1], run.ConfirmedAt[1]);
        Assert.Equal(CipherCEngine.YearlyFromDates(bars, dates).Lows, run.Lows.Take(2));
    }

    /// <summary>
    /// In the indicator: off (the default) a tuned asset's yearly lows are its dates and an untuned
    /// one has none, as the original draws; on, an untuned asset's are found from the price over its
    /// preset's yearly cycle (the S&amp;P's 3.5 to 4.5 years: 4), and a tuned asset keeps its dates
    /// first. Bitcoin's fixture ends in June 2026, before any yearly low after November 2022 could
    /// be known, so on it the switch adds none.
    /// </summary>
    [Fact]
    public void The_switch_finds_yearly_lows_from_price_and_keeps_the_originals_dates()
    {
        var h = CipherSrSpeechTests.BtcDaily().ToArray();
        static List<int> Y(Ohlcv[] bars, string? symbol, bool on)
        {
            var p = new Dictionary<string, object> { [CipherCProvider.ParamYearlyFromPrice] = on };
            if (symbol != null) p["__symbol"] = symbol;
            var r = CipherCProvider.Compute(bars, p)[CipherCProvider.CompYcl];
            return Enumerable.Range(0, r.Length).Where(i => !double.IsNaN(r[i])).ToList();
        }
        var stocky = h.Where(b => b.Date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToArray();
        Assert.Empty(Y(stocky, "XYZ", false));
        var found = Y(stocky, "XYZ", true);
        Assert.True(found.Count >= 2, $"{found.Count} yearly lows found");
        Assert.Equal(CipherCEngine.YearlyFromPrice(stocky, 4).Lows, found);

        var dates = CipherCEngine.Preset("BTC").YearlyLows!.Select(d => Bar(h, d)).ToList();
        Assert.Equal(dates, Y(h, "BTC/USD", false));
        Assert.Equal(dates, Y(h, "BTC/USD", true));
    }

    /// <summary>On a tuned asset a yearly low found after its dates moves the yellow band on, by the
    /// same days the original counts from its dates: on a wave with Bitcoin's 2018 and 2022 lows
    /// and the next in November 2026, Bitcoin's 1143 to 1508 days from that one.</summary>
    [Fact]
    public void A_yearly_low_found_after_the_dates_moves_the_band_on()
    {
        var bars = Wave(new DateTime(2014, 11, 28), years: 16, from: new DateTime(2012, 1, 1));
        var own = CipherCEngine.Preset("BTC");
        var p = new Dictionary<string, object> { ["__symbol"] = "BTC/USD", [CipherCProvider.ParamYearlyFromPrice] = true };
        var r = CipherCProvider.Compute(bars, p);
        var y = Enumerable.Range(0, bars.Length).Where(i => !double.IsNaN(r[CipherCProvider.CompYcl][i])).ToList();
        var expected = own.YearlyLows!.Select(d => Bar(bars, d)).Append(Bar(bars, new DateTime(2026, 11, 28))).ToList();
        Assert.Equal(expected, y);
        int last = bars.Length - 1;
        Assert.Equal(Ahead(bars, new DateTime(2026, 11, 28).AddDays(own.YearlyBandFromDays)),
                     r[CipherCProvider.CompYearlyWindow + ComponentCompanions.BoxStart][last]);
        Assert.Equal(Ahead(bars, new DateTime(2026, 11, 28).AddDays(own.YearlyBandToDays)),
                     r[CipherCProvider.CompYearlyWindow + ComponentCompanions.BoxEnd][last]);
        // Off, the band runs from the newest date.
        var off = CipherCProvider.Compute(bars, new Dictionary<string, object> { ["__symbol"] = "BTC/USD" });
        Assert.Equal(Ahead(bars, own.YearlyLows![^1].AddDays(own.YearlyBandFromDays)),
                     off[CipherCProvider.CompYearlyWindow + ComponentCompanions.BoxStart][last]);
    }
}
