using System.Globalization;
using AccessibleTrader.Core.Services.MarketEvents;

namespace AccessibleTrader.Tests;

/// <summary>
/// The C# figure calculator is the same rule as the seed's Python builder. These rows are a
/// fixture copy of part of the ALFRED vintages files, and the expectations are exactly the
/// handoff's table A.
/// </summary>
public class MarketEventFiguresCalculatorTests
{
    private static IReadOnlyDictionary<string, IReadOnlyList<MarketEventVintageRow>> Rows()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MarketEvents");
        var result = new Dictionary<string, IReadOnlyList<MarketEventVintageRow>>(StringComparer.Ordinal);
        foreach (var series in new[] { "CPIAUCSL", "CPILFESL", "PAYEMS", "UNRATE", "GDPC1" })
        {
            var rows = new List<MarketEventVintageRow>();
            foreach (var line in File.ReadLines(Path.Combine(dir, series + "_vintages.csv")).Skip(1))
            {
                var parts = line.Split(',');
                if (parts.Length != 4 || parts[1] == "." || parts[1].Length == 0) continue;
                rows.Add(new MarketEventVintageRow(
                    DateOnly.ParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    double.Parse(parts[1], CultureInfo.InvariantCulture),
                    DateOnly.ParseExact(parts[2], "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    parts[3].Length == 0
                        ? new DateOnly(9999, 12, 31)
                        : DateOnly.ParseExact(parts[3], "yyyy-MM-dd", CultureInfo.InvariantCulture)));
            }
            result[series] = rows;
        }
        return result;
    }

    private static MarketEventFigure Figure(MarketEventFigures figures, string name)
        => figures.Figures.Single(f => f.Name == name);

    private static IReadOnlyDictionary<string, IReadOnlyList<MarketEventVintageRow>> Series(
        string id,
        params MarketEventVintageRow[] rows)
        => new Dictionary<string, IReadOnlyList<MarketEventVintageRow>>(StringComparer.Ordinal)
        {
            [id] = rows,
        };

    private static MarketEventVintageRow Row(string period, double value, string start)
        => new(
            DateOnly.ParseExact(period, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            value,
            DateOnly.ParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            new DateOnly(9999, 12, 31));

    [Fact]
    public void KeepReleasesDropsAPastDateWithNoVintageAndOneWithNoNewerPeriod()
    {
        var rows = Series("CPIAUCSL",
            Row("2023-12-01", 99, "2024-01-11"),
            Row("2024-01-01", 100, "2024-02-13"),
            Row("2023-12-01", 99.5, "2024-03-12"));
        var kept = MarketEventFiguresCalculator.KeepReleases(
            "cpi",
            new[] { new DateOnly(2024, 2, 9), new DateOnly(2024, 2, 13), new DateOnly(2024, 3, 12) },
            rows,
            new DateOnly(2024, 4, 1));

        Assert.Equal(new[] { new DateOnly(2024, 2, 13) }, kept);
    }

    [Fact]
    public void KeepReleasesKeepsDatesBeforeTheFirstVintage()
    {
        var rows = Series("CPIAUCSL", Row("1991-10-01", 100, "1991-12-04"));

        var kept = MarketEventFiguresCalculator.KeepReleases(
            "cpi",
            new[] { new DateOnly(1947, 7, 20), new DateOnly(1991, 12, 4) },
            rows,
            new DateOnly(2024, 1, 1));

        Assert.Equal(new[] { new DateOnly(1947, 7, 20), new DateOnly(1991, 12, 4) }, kept);
    }

    [Fact]
    public void KeepReleasesKeepsFutureDatesEvenWithNoVintage()
    {
        var rows = Series("CPIAUCSL", Row("2024-01-01", 100, "2024-02-13"));

        var kept = MarketEventFiguresCalculator.KeepReleases(
            "cpi", new[] { new DateOnly(2026, 12, 11) }, rows, new DateOnly(2026, 11, 18));

        Assert.Equal(new[] { new DateOnly(2026, 12, 11) }, kept);
    }

    [Fact]
    public void KeepReleasesKeepsGdpSecondAndThirdEstimates()
    {
        var rows = Series("GDPC1",
            Row("2026-01-01", 99, "2026-07-30"),
            Row("2026-04-01", 100, "2026-07-30"),
            Row("2026-04-01", 101, "2026-08-27"),
            Row("2026-04-01", 102, "2026-09-24"));

        var kept = MarketEventFiguresCalculator.KeepReleases(
            "gdp",
            new[] { new DateOnly(2026, 7, 30), new DateOnly(2026, 8, 27), new DateOnly(2026, 9, 24) },
            rows,
            new DateOnly(2026, 10, 1));

        Assert.Equal(
            new[] { new DateOnly(2026, 7, 30), new DateOnly(2026, 8, 27), new DateOnly(2026, 9, 24) },
            kept);
    }

    [Fact]
    public void KeepReleasesUsesPpifisForPpi()
    {
        var rows = Series("PPIFIS",
            Row("2024-01-01", 100, "2024-02-14"),
            Row("2024-02-01", 101, "2024-02-16"));

        var kept = MarketEventFiguresCalculator.KeepReleases(
            "ppi",
            new[] { new DateOnly(2024, 2, 14), new DateOnly(2024, 2, 16) },
            rows,
            new DateOnly(2024, 3, 1));

        Assert.Equal(new[] { new DateOnly(2024, 2, 14), new DateOnly(2024, 2, 16) }, kept);
    }

    [Theory]
    [InlineData("cpi", "2022-07-13", "CPI", 1.3, 1.0)]
    [InlineData("cpi", "2026-07-14", "CPI", -0.4, 0.5)]
    [InlineData("cpi", "2026-07-14", "core CPI", 0.0, 0.2)]
    [InlineData("nfp", "2020-05-08", "payrolls", -20500.0, -870.0)]
    [InlineData("nfp", "2020-05-08", "unemployment", 14.7, 4.4)]
    [InlineData("nfp", "2024-12-06", "payrolls", 227.0, 36.0)]
    [InlineData("nfp", "2026-07-02", "payrolls", 57.0, 129.0)]
    [InlineData("nfp", "2026-07-02", "unemployment", 4.2, 4.3)]
    [InlineData("gdp", "2025-07-30", "GDP", 3.0, -0.5)]
    [InlineData("gdp", "2025-08-28", "GDP", 3.3, 3.0)]
    [InlineData("gdp", "2026-07-30", "GDP", 1.5, 2.1)]
    public void TableAComesOutOfTheFixtureRows(
        string source, string day, string figure, double actual, double previous)
    {
        var figures = MarketEventFiguresCalculator.Compute(
            source, DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture), Rows());

        Assert.NotNull(figures);
        var f = Figure(figures!, figure);
        Assert.Equal(actual, f.Actual);
        Assert.Equal(previous, f.Previous);
    }

    [Fact]
    public void GdpEstimateNumbersAndPeriodsMatchTheSeed()
    {
        var rows = Rows();
        var first = MarketEventFiguresCalculator.Compute("gdp", new DateOnly(2025, 7, 30), rows)!;
        var second = MarketEventFiguresCalculator.Compute("gdp", new DateOnly(2025, 8, 28), rows)!;
        var next = MarketEventFiguresCalculator.Compute("gdp", new DateOnly(2026, 7, 30), rows)!;

        Assert.Equal(1, first.EstimateNumber);
        Assert.Equal(new DateOnly(2025, 4, 1), first.Period);
        Assert.Equal(2, second.EstimateNumber);
        Assert.Equal(new DateOnly(2025, 4, 1), second.Period);
        Assert.Equal(1, next.EstimateNumber);
        Assert.Equal(new DateOnly(2026, 4, 1), next.Period);
    }

    [Fact]
    public void ADateWithNoVintageStartingThatDayHasNoFigures()
    {
        var rows = Rows();

        Assert.Null(MarketEventFiguresCalculator.Compute("cpi", new DateOnly(2026, 7, 15), rows));
        Assert.Null(MarketEventFiguresCalculator.Compute("gdp", new DateOnly(2025, 7, 31), rows));
    }

    [Fact]
    public void PpiBeforeTheHeadlineSeriesIsSilent()
    {
        var rows = new Dictionary<string, IReadOnlyList<MarketEventVintageRow>>
        {
            ["PPIFIS"] = new[]
            {
                new MarketEventVintageRow(new DateOnly(2014, 1, 1), 100, new DateOnly(2014, 2, 19), new DateOnly(9999, 12, 31)),
                new MarketEventVintageRow(new DateOnly(2013, 12, 1), 99, new DateOnly(2014, 1, 15), new DateOnly(9999, 12, 31)),
                new MarketEventVintageRow(new DateOnly(2013, 11, 1), 98, new DateOnly(2013, 12, 13), new DateOnly(9999, 12, 31)),
            },
        };

        Assert.Null(MarketEventFiguresCalculator.Compute("ppi", new DateOnly(2014, 1, 15), rows));
        Assert.NotNull(MarketEventFiguresCalculator.Compute("ppi", new DateOnly(2014, 2, 19), rows));
    }
}
