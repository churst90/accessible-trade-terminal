using System;
using System.Linq;
using AccessibleTrader.Plugins.Fred;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// The FRED macro provider.
///
/// <para>
/// One property matters more than every other test in this file: <b>observations are stamped by
/// their RELEASE date, never by the period they describe.</b> CPIAUCSL for 2020-01 was first
/// published 2020-02-13; stamping it at 2020-01-01 lets a backtest read January's inflation print
/// six weeks before anyone could. GDP is worse — the advance estimate lands a month after
/// quarter-end and is revised twice more, and the default FRED request returns the LATEST vintage,
/// a number that did not exist in any form until long after the bar it sat on.
/// </para>
///
/// <para>
/// Like the EDGAR filing-date rule this mirrors, the bias is invisible: the series looks entirely
/// reasonable either way, and the backtest it corrupts looks good rather than broken. Only a test
/// can hold the line.
/// </para>
/// </summary>
public class FredProviderTests
{
    /// <summary>
    /// A FRED `output_type=4` payload: two CPI observations, each describing a month and each
    /// released the following month.
    /// </summary>
    private const string CpiPayload = """
        {"observations":[
          {"realtime_start":"2020-02-13","realtime_end":"9999-12-31","date":"2020-01-01","value":"257.971"},
          {"realtime_start":"2020-03-11","realtime_end":"9999-12-31","date":"2020-02-01","value":"258.678"}
        ]}
        """;

    [Fact]
    public void ObservationsAreStampedAtTheirReleaseDateNotTheirPeriod()
    {
        var bars = FredProvider.ParseObservations(CpiPayload);

        Assert.Equal(2, bars.Count);

        // The January CPI print sits on the day it was published, not on January 1st.
        Assert.Equal(new DateTime(2020, 2, 13, 0, 0, 0, DateTimeKind.Utc), bars[0].Date);
        Assert.Equal(257.971, bars[0].Close, precision: 3);

        Assert.Equal(new DateTime(2020, 3, 11, 0, 0, 0, DateTimeKind.Utc), bars[1].Date);
        Assert.Equal(258.678, bars[1].Close, precision: 3);

        // The period dates must appear nowhere in the series — that is the whole bug.
        Assert.DoesNotContain(bars, b => b.Date == new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.DoesNotContain(bars, b => b.Date == new DateTime(2020, 2, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void EveryBarIsStampedUtc()
    {
        // A local-kind stamp would shift the bar across a day boundary for anyone east of UTC,
        // which on a monthly macro series silently moves a release into the prior period.
        Assert.All(FredProvider.ParseObservations(CpiPayload),
            b => Assert.Equal(DateTimeKind.Utc, b.Date.Kind));
    }

    [Fact]
    public void WhereSeveralObservationsShareAReleaseDateTheLatestPeriodWins()
    {
        // A backfill or a print that revises the prior month alongside the current one. The
        // current reading as of that release is the one covering the latest period.
        const string sameDay = """
            {"observations":[
              {"realtime_start":"2021-01-08","realtime_end":"9999-12-31","date":"2020-11-01","value":"10.0"},
              {"realtime_start":"2021-01-08","realtime_end":"9999-12-31","date":"2020-12-01","value":"20.0"}
            ]}
            """;

        var bar = Assert.Single(FredProvider.ParseObservations(sameDay));
        Assert.Equal(new DateTime(2021, 1, 8, 0, 0, 0, DateTimeKind.Utc), bar.Date);
        Assert.Equal(20.0, bar.Close);
    }

    [Fact]
    public void MissingValuesAreSkippedRatherThanReadAsZero()
    {
        // FRED writes "." for a period with no data. Parsing that as 0 would put a fabricated
        // zero into a macro series — a 100% drop in payrolls, as far as a strategy can tell.
        const string withHole = """
            {"observations":[
              {"realtime_start":"2020-02-13","realtime_end":"9999-12-31","date":"2020-01-01","value":"."},
              {"realtime_start":"2020-03-11","realtime_end":"9999-12-31","date":"2020-02-01","value":"258.678"}
            ]}
            """;

        var bar = Assert.Single(FredProvider.ParseObservations(withHole));
        Assert.Equal(258.678, bar.Close, precision: 3);
    }

    [Fact]
    public void APayloadWithoutRealtimeStartFallsBackToThePeriodRatherThanGoingBlank()
    {
        // Defensive: an older cached payload or a response-shape change should degrade to the
        // old behaviour for those rows, not empty the chart.
        const string legacy = """
            {"observations":[{"date":"2020-01-01","value":"257.971"}]}
            """;

        var bar = Assert.Single(FredProvider.ParseObservations(legacy));
        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), bar.Date);
    }

    [Fact]
    public void ResultsAreOrderedByReleaseDate()
    {
        // The payload arrives ordered by period, and re-stamping can reorder it. A series
        // handed to the chart out of order breaks every downstream index assumption.
        const string outOfOrder = """
            {"observations":[
              {"realtime_start":"2020-03-11","realtime_end":"9999-12-31","date":"2020-02-01","value":"2.0"},
              {"realtime_start":"2020-02-13","realtime_end":"9999-12-31","date":"2020-01-01","value":"1.0"}
            ]}
            """;

        var bars = FredProvider.ParseObservations(outOfOrder);
        Assert.Equal(bars.OrderBy(b => b.Date).Select(b => b.Date), bars.Select(b => b.Date));
    }

    [Fact]
    public void AnEmptyOrMalformedPayloadYieldsNoBarsRatherThanThrowing()
    {
        Assert.Empty(FredProvider.ParseObservations("""{"observations":[]}"""));
        Assert.Empty(FredProvider.ParseObservations("""{"error_code":400}"""));
    }

    /// <summary>
    /// The request half of the point-in-time fix. Without `output_type=4` and the realtime
    /// window, FRED returns the latest vintage with no `realtime_start` to stamp with, the
    /// parser falls back to the period date, and the look-ahead bias is silently restored —
    /// every parsing test above would still pass.
    /// </summary>
    [Fact]
    public void TheObservationsRequestAsksForInitialReleasesAcrossAllVintages()
    {
        var request = new MarketDataRequest("Economic", "CPIAUCSL", "1d");
        string url = FredProvider.BuildObservationsUrl(request, "key", frequency: "m");

        Assert.Contains("output_type=4", url);
        Assert.Contains("realtime_start=1776-07-04", url);
        Assert.Contains("realtime_end=9999-12-31", url);
    }

    [Fact]
    public void TheSymbolIsEscapedSoItCannotInjectQueryParameters()
    {
        var request = new MarketDataRequest("Economic", "GDP&api_key=attacker", "1d");
        string url = FredProvider.BuildObservationsUrl(request, "real-key", frequency: null);

        Assert.DoesNotContain("api_key=attacker", url);
        Assert.Contains("api_key=real-key", url);
    }
}
