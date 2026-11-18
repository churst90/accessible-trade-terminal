using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Tests;

/// <summary>
/// Stage 2 part B: what the figures sound like. Exact wording on one-event bars, never on a
/// crowd or a weekly bar, and full dossier lines in Alt+Shift+D.
/// </summary>
public class MarketEventFigureSpeechTests
{
    private static readonly DateTime Moment = new(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc);

    private static MarketEventFigures CpiFigures(double actual, double previous, double? core = null, double? corePrevious = null)
        => new(new[]
        {
            new MarketEventFigure("CPI", actual, previous,
                MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsHotter),
            new MarketEventFigure("core CPI", core, corePrevious,
                MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsHotter),
        });

    private static MarketEventFigures JobFigures(double payrolls, double previous, double unemployment, double unemploymentPrevious)
        => new(new[]
        {
            new MarketEventFigure("payrolls", payrolls, previous,
                MarketEventFigureUnit.Thousands, MarketEventFigureDirection.HigherIsStronger),
            new MarketEventFigure("unemployment", unemployment, unemploymentPrevious,
                MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsWeaker),
        });

    private static MarketEventFigures GdpFigures(double growth, int estimate, double previous, DateOnly period)
        => new(new[]
        {
            new MarketEventFigure("GDP", growth, previous,
                MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsStronger),
        }, estimate, period);

    private static MarketEventFigures PpiFigures(double actual, double previous)
        => new(new[]
        {
            new MarketEventFigure("PPI", actual, previous,
                MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsHotter),
        });

    private static string Say(int barSeconds, params MarketEventBarPlacement[] placements)
        => MarketEventSpeech.Clause(
            placements, barSeconds, MarketEventLevel.All, MarketEventKindFilter.All, MarketType.Crypto);

    private static string Time(MarketEvent e)
        => SpeechTimeFormatter.FormatTime(e.MomentUtc);

    [Fact]
    public void ACpiFigureIsAfterTheTimeAndBeforeThePlacementWord()
    {
        var e = MarketEventTestData.Event(moment: Moment, figures: CpiFigures(1.3, 1.0, 0.7, 0.6));

        string said = Say(1800, MarketEventTestData.Placed(e, 0, MarketEventPlacement.BeforeTheOpen));

        Assert.Equal(
            $"CPI {Time(e)}, up 1.3% on the month, previous up 1.0%, before the open",
            said);
    }

    [Fact]
    public void ACpiOfZeroIsUnchangedOnTheMonth()
    {
        var e = MarketEventTestData.Event(moment: Moment, figures: CpiFigures(0.0, 0.2));

        string said = Say(1800, MarketEventTestData.Placed(e, 0));

        Assert.Equal($"CPI {Time(e)}, unchanged on the month, previous up 0.2%", said);
    }

    [Fact]
    public void AJobsFigureReadsPayrollsThenUnemployment()
    {
        var e = MarketEventTestData.Event(
            spoken: "jobs report", longName: "Nonfarm payrolls", moment: Moment,
            figures: JobFigures(227, 36, 4.2, 4.1));

        string said = Say(1800, MarketEventTestData.Placed(e, 0));

        Assert.Equal(
            $"jobs report {Time(e)}, payrolls up 227 thousand, previous up 36 thousand, unemployment 4.2%",
            said);
    }

    [Fact]
    public void TwentyThousandPayrollsIsDownTwentyPointFiveMillion()
    {
        var e = MarketEventTestData.Event(
            spoken: "jobs report", longName: "Nonfarm payrolls", moment: Moment,
            figures: JobFigures(-20500, -870, 14.7, 4.4));

        string said = Say(1800, MarketEventTestData.Placed(e, 0));

        Assert.Equal(
            $"jobs report {Time(e)}, payrolls down 20.5 million, previous down 870 thousand, unemployment 14.7%",
            said);
    }

    [Theory]
    [InlineData(1000, "up 1 million")]
    [InlineData(1250, "up 1.3 million")]
    [InlineData(999, "up 999 thousand")]
    public void MillionsTrimATrailingPointZero(int payrolls, string expected)
    {
        var e = MarketEventTestData.Event(
            spoken: "jobs report", longName: "Nonfarm payrolls", moment: Moment,
            figures: JobFigures(payrolls, 0, 4.0, 4.0));

        string said = MarketEventSpeech.FigureClause(e);

        Assert.StartsWith("payrolls " + expected + ", previous unchanged", said);
    }

    [Fact]
    public void AGdpFirstEstimateNamesThePreviousQuarter()
    {
        var e = MarketEventTestData.Event(
            spoken: "GDP", longName: "Gross domestic product",
            importance: MarketEventImportance.Medium, moment: Moment,
            figures: GdpFigures(3.0, 1, -0.5, new DateOnly(2025, 4, 1)));

        string said = Say(1800, MarketEventTestData.Placed(e, 0));

        Assert.Equal(
            $"GDP {Time(e)}, first estimate, up 3.0% annualized, previous quarter down 0.5%",
            said);
    }

    [Fact]
    public void AGdpSecondEstimateSaysWas()
    {
        var e = MarketEventTestData.Event(
            spoken: "GDP", longName: "Gross domestic product",
            importance: MarketEventImportance.Medium, moment: Moment,
            figures: GdpFigures(3.3, 2, 3.0, new DateOnly(2025, 4, 1)));

        string said = Say(1800, MarketEventTestData.Placed(e, 0));

        Assert.Equal($"GDP {Time(e)}, second estimate, up 3.3% annualized, was up 3.0%", said);
    }

    [Fact]
    public void AGdpEstimateAboveThreeIsARevisedEstimate()
    {
        var e = MarketEventTestData.Event(
            spoken: "GDP", longName: "Gross domestic product",
            importance: MarketEventImportance.Medium, moment: Moment,
            figures: GdpFigures(2.8, 4, 3.0, new DateOnly(2025, 4, 1)));

        Assert.Contains("revised estimate", MarketEventSpeech.FigureClause(e));
    }

    [Fact]
    public void APpiFigureReadsLikeCpi()
    {
        var e = MarketEventTestData.Event(
            spoken: "PPI", longName: "Producer Price Index",
            importance: MarketEventImportance.Medium, moment: Moment,
            figures: PpiFigures(0.2, 0.1));

        string said = Say(1800, MarketEventTestData.Placed(e, 0));

        Assert.Equal($"PPI {Time(e)}, up 0.2% on the month, previous up 0.1%", said);
    }

    [Fact]
    public void NoFiguresIsExactlyTheStageOneClause()
    {
        var withFigures = MarketEventTestData.Event(id: "a", moment: Moment, figures: CpiFigures(1.3, 1.0));
        var without = MarketEventTestData.Event(id: "b", moment: Moment);

        Assert.NotEqual(Say(1800, MarketEventTestData.Placed(withFigures, 0)),
            Say(1800, MarketEventTestData.Placed(without, 0)));
        Assert.Equal($"CPI {Time(without)}", Say(1800, MarketEventTestData.Placed(without, 0)));
    }

    [Fact]
    public void ATwoEventBarKeepsTheShortListAndAddsNoFigures()
    {
        var cpi = MarketEventTestData.Event(id: "cpi", moment: Moment, figures: CpiFigures(1.3, 1.0));
        var ppi = MarketEventTestData.Event(
            id: "ppi", spoken: "PPI", longName: "Producer Price Index",
            importance: MarketEventImportance.Medium, moment: Moment.AddMinutes(1),
            figures: PpiFigures(0.2, 0.1));

        string said = Say(1800, MarketEventTestData.Placed(cpi, 0), MarketEventTestData.Placed(ppi, 0));

        Assert.Contains("CPI", said);
        Assert.Contains("PPI", said);
        Assert.DoesNotContain("on the month", said);
        Assert.DoesNotContain("1.3", said);
    }

    [Fact]
    public void AWeeklyBarDoesNotReadFigures()
    {
        var e = MarketEventTestData.Event(moment: Moment, figures: CpiFigures(1.3, 1.0));

        string said = Say(604800, MarketEventTestData.Placed(e, 0));

        Assert.DoesNotContain("on the month", said);
        Assert.DoesNotContain("1.3", said);
        Assert.Contains("CPI", said);
    }

    [Fact]
    public void DetailReadsEveryFigureAsAFullLine()
    {
        var e = MarketEventTestData.Event(
            longName: "Consumer Price Index", moment: Moment,
            figures: CpiFigures(1.3, 1.0, 0.7, 0.6));
        var bar = MarketEventTestData.Bar(Moment, open: 100, close: 101);

        string said = MarketEventSpeech.Detail(
            new[] { MarketEventTestData.Placed(e, 0) }, 0, bar, MarketEventKindFilter.All, MarketType.Crypto);

        Assert.Contains(
            "Figure: CPI up 1.3% on the month. Previous: up 1.0%. "
            + "Core CPI: up 0.7% on the month, previous up 0.6%. "
            + "First print, from FRED/ALFRED.",
            said);
    }

    [Fact]
    public void JobsDetailAddsTheUnemploymentLine()
    {
        var e = MarketEventTestData.Event(
            spoken: "jobs report", longName: "Nonfarm payrolls", moment: Moment,
            figures: JobFigures(227, 36, 4.2, 4.1));
        var bar = MarketEventTestData.Bar(Moment);

        string said = MarketEventSpeech.Detail(
            new[] { MarketEventTestData.Placed(e, 0) }, 0, bar, MarketEventKindFilter.All, MarketType.Crypto);

        Assert.Contains("Figure: payrolls up 227 thousand. Previous: up 36 thousand.", said);
        Assert.Contains("Unemployment: 4.2%, previous 4.1%.", said);
    }

    [Fact]
    public void GdpDetailAddsTheEstimateAndTheQuarter()
    {
        var e = MarketEventTestData.Event(
            spoken: "GDP", longName: "Gross domestic product",
            importance: MarketEventImportance.Medium, moment: Moment,
            figures: GdpFigures(3.0, 1, -0.5, new DateOnly(2025, 4, 1)));
        var bar = MarketEventTestData.Bar(Moment);

        string said = MarketEventSpeech.Detail(
            new[] { MarketEventTestData.Placed(e, 0) }, 0, bar, MarketEventKindFilter.All, MarketType.Crypto);

        Assert.Contains(
            "Figure: first estimate, up 3.0% annualized for the second quarter of 2025. "
            + "Previous quarter: down 0.5%.",
            said);
    }

    [Fact]
    public void GdpLaterEstimateSaysWas()
    {
        var e = MarketEventTestData.Event(
            spoken: "GDP", longName: "Gross domestic product",
            importance: MarketEventImportance.Medium, moment: Moment,
            figures: GdpFigures(3.3, 2, 3.0, new DateOnly(2025, 4, 1)));
        var bar = MarketEventTestData.Bar(Moment);

        string said = MarketEventSpeech.Detail(
            new[] { MarketEventTestData.Placed(e, 0) }, 0, bar, MarketEventKindFilter.All, MarketType.Crypto);

        Assert.Contains("Figure: second estimate, up 3.3% annualized", said);
        Assert.Contains("Was up 3.0%.", said);
    }

    [Fact]
    public void NoFiguresSaysUnavailable()
    {
        var e = MarketEventTestData.Event(moment: Moment);
        var bar = MarketEventTestData.Bar(Moment);

        string said = MarketEventSpeech.Detail(
            new[] { MarketEventTestData.Placed(e, 0) }, 0, bar, MarketEventKindFilter.All, MarketType.Crypto);

        Assert.Contains("Figure: unavailable.", said);
    }

    [Fact]
    public void AMissingCoreSaysUnavailableRatherThanBlank()
    {
        var e = MarketEventTestData.Event(moment: Moment, figures: CpiFigures(1.3, 1.0));
        var bar = MarketEventTestData.Bar(Moment);

        string said = MarketEventSpeech.Detail(
            new[] { MarketEventTestData.Placed(e, 0) }, 0, bar, MarketEventKindFilter.All, MarketType.Crypto);

        Assert.Contains("Core CPI: unavailable.", said);
    }

    [Fact]
    public void TheSharedFilterIsTheOnlyFilter()
    {
        var passing = MarketEventTestData.Event(id: "pass", moment: Moment, importance: MarketEventImportance.High);
        var filtered = MarketEventTestData.Event(id: "filtered", moment: Moment, importance: MarketEventImportance.Low);

        var result = MarketEventSpeech.Passing(
            new[] { MarketEventTestData.Placed(filtered, 0), MarketEventTestData.Placed(passing, 0) },
            MarketEventLevel.ImportantOnly,
            MarketEventKindFilter.All,
            MarketType.Crypto);

        Assert.Single(result);
        Assert.Equal("pass", result[0].Event.Id);
    }
}
