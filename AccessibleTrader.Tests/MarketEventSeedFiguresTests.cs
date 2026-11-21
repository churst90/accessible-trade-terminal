using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Tests;

/// <summary>
/// Stage 2 part A: the seed's first-print figures. The vintages are the ones visible on the
/// release day itself (never a later revision), rounded the way they are spoken, aligned by
/// index with each source's dates, and documented in the header.
/// </summary>
public class MarketEventSeedFiguresTests
{
    private static readonly DateOnly CpiLastDate = new(2026, 7, 14);

    private static MarketEvent FiguresEvent(string id)
        => MarketEventSeed.Load().Events.Single(e => e.Id == id);

    private static MarketEventFigure Figure(MarketEvent e, string name)
        => e.Figures!.Figures.Single(f => f.Name == name);

    [Theory]
    [InlineData("cpi-2022-07-13", "CPI", 1.3, 1.0)]
    [InlineData("cpi-2026-07-14", "CPI", -0.4, 0.5)]
    [InlineData("cpi-2026-07-14", "core CPI", 0.0, 0.2)]
    [InlineData("nfp-2020-05-08", "payrolls", -20500.0, -870.0)]
    [InlineData("nfp-2020-05-08", "unemployment", 14.7, 4.4)]
    [InlineData("nfp-2024-12-06", "payrolls", 227.0, 36.0)]
    [InlineData("nfp-2026-07-02", "payrolls", 57.0, 129.0)]
    [InlineData("nfp-2026-07-02", "unemployment", 4.2, 4.3)]
    [InlineData("gdp-2025-07-30", "GDP", 3.0, -0.5)]
    [InlineData("gdp-2025-08-28", "GDP", 3.3, 3.0)]
    [InlineData("gdp-2026-07-30", "GDP", 1.5, 2.1)]
    // From the 2026-11-21 fetch.
    [InlineData("cpi-2026-11-10", "CPI", 0.4, 0.6)]
    [InlineData("nfp-2026-11-06", "payrolls", 165.0, 60.0)]
    [InlineData("nfp-2026-11-06", "unemployment", 4.1, 4.2)]
    [InlineData("gdp-2026-10-29", "GDP", 3.4, 2.2)]
    public void TableAValuesAreStoredExactly(string id, string figure, double actual, double previous)
    {
        var e = FiguresEvent(id);
        var f = Figure(e, figure);

        Assert.Equal(actual, f.Actual);
        Assert.Equal(previous, f.Previous);
    }

    [Theory]
    [InlineData("cpi-2026-12-10")]
    [InlineData("nfp-2026-12-04")]
    [InlineData("ppi-2026-12-15")]
    [InlineData("gdp-2026-11-25")]
    [InlineData("gdp-2026-12-23")]
    public void AScheduledReleaseIsStoredWithNoFigures(string id)
    {
        // Dates after the fetch day are FRED's schedule: the countdown has them, but nothing
        // has been published yet, so there is nothing to say about them.
        Assert.Null(FiguresEvent(id).Figures);
    }

    [Fact]
    public void GdpCarriesItsEstimateNumberAndQuarter()
    {
        var first = FiguresEvent("gdp-2025-07-30");
        var second = FiguresEvent("gdp-2025-08-28");
        var next = FiguresEvent("gdp-2026-07-30");

        Assert.Equal(1, first.Figures!.EstimateNumber);
        Assert.Equal(new DateOnly(2025, 4, 1), first.Figures.Period);
        Assert.Equal(2, second.Figures!.EstimateNumber);
        Assert.Equal(new DateOnly(2025, 4, 1), second.Figures.Period);
        Assert.Equal(1, next.Figures!.EstimateNumber);
        Assert.Equal(new DateOnly(2026, 4, 1), next.Figures.Period);
    }

    [Fact]
    public void EveryFigureStoresItsUnitAndWhichWayIsHot()
    {
        var cpi = FiguresEvent("cpi-2026-07-14");
        var jobs = FiguresEvent("nfp-2026-07-02");
        var gdp = FiguresEvent("gdp-2026-07-30");
        var ppi = MarketEventSeed.Load().Events.First(e => e.Id.StartsWith("ppi-", StringComparison.Ordinal));

        Assert.Equal(MarketEventFigureUnit.Percent, Figure(cpi, "CPI").Unit);
        Assert.Equal(MarketEventFigureDirection.HigherIsHotter, Figure(cpi, "CPI").Direction);
        Assert.Equal(MarketEventFigureDirection.HigherIsHotter, Figure(cpi, "core CPI").Direction);
        Assert.Equal(MarketEventFigureUnit.Thousands, Figure(jobs, "payrolls").Unit);
        Assert.Equal(MarketEventFigureDirection.HigherIsStronger, Figure(jobs, "payrolls").Direction);
        Assert.Equal(MarketEventFigureUnit.Percent, Figure(jobs, "unemployment").Unit);
        Assert.Equal(MarketEventFigureDirection.HigherIsWeaker, Figure(jobs, "unemployment").Direction);
        Assert.Equal(MarketEventFigureDirection.HigherIsStronger, Figure(gdp, "GDP").Direction);
        Assert.Null(ppi.Figures);
    }

    [Fact]
    public void EveryStoredPercentageHasOneDecimalAndEveryPayrollIsWhole()
    {
        foreach (var e in MarketEventSeed.Load().Events)
        {
            if (e.Figures == null) continue;
            foreach (var f in e.Figures.Figures)
            {
                foreach (double value in new[] { f.Actual, f.Previous }.Where(v => v.HasValue).Select(v => v!.Value))
                {
                    if (f.Unit == MarketEventFigureUnit.Percent)
                        Assert.Equal(Math.Round(value, 1), value, 10);
                    else
                        Assert.Equal(Math.Round(value, 0), value, 10);
                    Assert.False(
                        value == 0 && BitConverter.DoubleToInt64Bits(value) < 0,
                        $"{e.Id} stores -0.0, which is spoken and compared as a negative zero.");
                }
            }
        }
    }

    [Fact]
    public void TheFigureArraysAlignWithTheDateArrays()
    {
        string json = SeedText();
        var root = Newtonsoft.Json.Linq.JObject.Parse(json);

        foreach (var source in new[] { "cpi", "nfp", "gdp", "ppi" })
        {
            var dates = (Newtonsoft.Json.Linq.JArray)root[source]!;
            var figures = (Newtonsoft.Json.Linq.JArray)root[source + "_figures"]!;
            Assert.Equal(dates.Count, figures.Count);
        }

        Assert.All((Newtonsoft.Json.Linq.JArray)root["ppi_figures"]!,
            row => Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Null, row.Type));
    }

    [Theory]
    [InlineData("cpi", 652, 933)]
    [InlineData("nfp", 858, 859)]
    [InlineData("gdp", 420, 812)]
    public void CoverageCountsMatchTheInputFiles(string source, int withFigures, int dates)
    {
        var root = Newtonsoft.Json.Linq.JObject.Parse(SeedText());
        var figures = (Newtonsoft.Json.Linq.JArray)root[source + "_figures"]!;

        Assert.Equal(dates, ((Newtonsoft.Json.Linq.JArray)root[source]!).Count);
        Assert.Equal(withFigures, figures.Count(row =>
            row is Newtonsoft.Json.Linq.JArray values && values[0].Type != Newtonsoft.Json.Linq.JTokenType.Null));
    }

    [Fact]
    public void CoreCpiAndUnemploymentHaveTheirOwnCoverageCounts()
    {
        var root = Newtonsoft.Json.Linq.JObject.Parse(SeedText());
        var cpi = (Newtonsoft.Json.Linq.JArray)root["cpi_figures"]!;
        var nfp = (Newtonsoft.Json.Linq.JArray)root["nfp_figures"]!;

        Assert.Equal(359, cpi.Count(row =>
            row is Newtonsoft.Json.Linq.JArray values && values[2].Type != Newtonsoft.Json.Linq.JTokenType.Null));
        Assert.Equal(800, nfp.Count(row =>
            row is Newtonsoft.Json.Linq.JArray values && values[2].Type != Newtonsoft.Json.Linq.JTokenType.Null));
    }

    [Fact]
    public void TheHeaderNamesTheAlfredInputsWithTheirNewestVintage()
    {
        var sources = MarketEventSeed.Load().Sources;
        var cpi = sources.Single(s => s.Id == "cpi");
        var jobs = sources.Single(s => s.Id == "nfp");
        var gdp = sources.Single(s => s.Id == "gdp");

        Assert.Equal(5, sources.Count);
        Assert.NotNull(cpi.Alfred);
        Assert.Contains(cpi.Alfred!, a => a.Series == "CPIAUCSL" && a.Newest == "2026-11-10");
        Assert.Contains(cpi.Alfred!, a => a.Series == "CPILFESL" && a.Newest == "2026-11-10");
        Assert.Contains(jobs.Alfred!, a => a.Series == "PAYEMS" && a.Newest == "2026-11-06");
        Assert.Contains(jobs.Alfred!, a => a.Series == "UNRATE" && a.Newest == "2026-11-06");
        Assert.Contains(gdp.Alfred!, a => a.Series == "GDPC1" && a.Newest == "2026-10-29");
        Assert.All(cpi.Alfred!, a =>
        {
            Assert.StartsWith("https://alfred.stlouisfed.org/", a.Url, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(a.Newest));
        });
        Assert.Empty(sources.Single(s => s.Id == "ppi").Alfred!);
    }

    [Fact]
    public void TheSeedStaysUnder150Kilobytes()
    {
        using var stream = typeof(MarketEventSeed).Assembly
            .GetManifestResourceStream(MarketEventSeed.ResourceName);
        Assert.NotNull(stream);
        Assert.True(stream!.Length < 150 * 1024, $"seed is {stream.Length} bytes");
    }

    private static string SeedText()
    {
        using var stream = typeof(MarketEventSeed).Assembly
            .GetManifestResourceStream(MarketEventSeed.ResourceName)
            ?? throw new InvalidOperationException("seed resource missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
