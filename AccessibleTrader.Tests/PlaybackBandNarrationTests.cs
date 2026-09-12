using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Playback speaks a level crossing, because a level crossing is a discrete signal.</b>
///
/// <para>
/// Cody, 2026-09-12: <i>"adx on playback narration says nothing. I'm sure every bar close when on
/// it is narrating in the ladder, but should it narrate during playback?"</i> It said nothing by
/// construction: the playback scan only ever looked at MARKER components carrying a signal
/// template, and ADX has four plain lines and no markers.
/// </para>
///
/// <para>
/// The rule playback was built on is <i>"a line has a value on every bar, and playback speaks
/// discrete signals only"</i> — and that rule is about DISCRETENESS. A level crossing is as
/// discrete as a marker gets: ADX crosses 25 a handful of times in five hundred bars. The rule did
/// not exclude it; the implementation did, by reading component TYPE where the rule is about event
/// shape. What keeps this from becoming the per-bar readout the rule forbids is the machinery that
/// was already there — the rarity ranking, the two-clause ceiling and the rate limit — which
/// crossings now go through like everything else.
/// </para>
/// </summary>
public sealed class PlaybackBandNarrationTests
{
    private static List<Ohlcv> Bars(int n) => Enumerable.Range(0, n).Select(i => new Ohlcv(
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i), 100, 101, 99, 100, 10)).ToList();

    /// <summary>ADX as production builds it: a warmup, then a line walking up through its bands.</summary>
    private static ChartSeries Adx(double[] values, bool narrating = true)
    {
        var config = new SeriesConfig
        {
            Id = "adx", IndicatorCode = "Adx", Name = "ADX", FriendlyName = "ADX",
            Pane = "Pane_Adx", IsAutoNarrated = narrating,
        };
        config.Components.Add(new ComponentConfig { Name = "Adx", DisplayName = "ADX", DisplayType = ComponentDisplayType.Line, IsVisible = true });
        config.Levels.Add(new LevelConfig { Name = "Strong", Value = 25, IsVisible = true, AboveLabel = "strong trend", BelowLabel = "weak trend" });
        var buffer = new SeriesDataBuffer { SeriesId = "adx" };
        buffer.ComponentData["Adx"] = values;
        return new ChartSeries(config, buffer);
    }

    private static WorkspaceState State(ChartSeries s, int bars) => WorkspaceState.Initial with
    {
        Data = new TimeSeriesBuffer<Ohlcv>(Bars(bars)),
        ActiveSeries = ImmutableList.Create(s),
        PrimarySeriesId = s.Id,
    };

    /// <summary>A ramp that crosses 25 exactly once, at bar 30.</summary>
    private static double[] CrossesAt30(int bars = 60)
        => Enumerable.Range(0, bars).Select(i => i < 30 ? 18.0 : 32.0).ToArray();

    [Fact]
    public void PlaybackSpeaksTheBandEnteredOnTheCrossingBar()
    {
        var series = Adx(CrossesAt30());
        string? said = PlaybackNarration.SignalsForStep(State(series, 60), barIndex: 30);

        Assert.NotNull(said);
        Assert.Contains("strong trend", said!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>And says nothing on the bars either side — this is a signal, not a readout.</summary>
    [Theory]
    [InlineData(29)]
    [InlineData(31)]
    [InlineData(45)]
    public void PlaybackSaysNothingOnANonCrossingBar(int barIndex)
    {
        var series = Adx(CrossesAt30());
        Assert.Null(PlaybackNarration.SignalsForStep(State(series, 60), barIndex));
    }

    /// <summary>A series the user has not flagged for narration stays silent, as always.</summary>
    [Fact]
    public void ASeriesNotFlaggedForNarration_StaysSilent()
    {
        var series = Adx(CrossesAt30(), narrating: false);
        Assert.Null(PlaybackNarration.SignalsForStep(State(series, 60), barIndex: 30));
    }

    /// <summary>A switched-off line is not an event here either.</summary>
    [Fact]
    public void AHiddenLineIsNotSpoken()
    {
        var series = Adx(CrossesAt30());
        series.Config.Levels[0].IsVisible = false;
        Assert.Null(PlaybackNarration.SignalsForStep(State(series, 60), barIndex: 30));
    }

    /// <summary>A level the component does not subscribe to is not its crossing.</summary>
    [Fact]
    public void ALevelTheComponentDoesNotSubscribeTo_IsNotSpoken()
    {
        var series = Adx(CrossesAt30());
        series.Components[0].SubscribedLevelNames = new[] { "SomethingElse" };
        Assert.Null(PlaybackNarration.SignalsForStep(State(series, 60), barIndex: 30));
    }

    /// <summary>An undeclared line names what it crossed rather than inventing a zone.</summary>
    [Fact]
    public void AnUnlabelledLineNamesTheLineItCrossed()
    {
        var series = Adx(CrossesAt30());
        series.Config.Levels[0].AboveLabel = null;
        series.Config.Levels[0].BelowLabel = null;

        string? said = PlaybackNarration.SignalsForStep(State(series, 60), barIndex: 30);

        Assert.NotNull(said);
        Assert.Contains("crossed above", said!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("strong", said!, StringComparison.OrdinalIgnoreCase);
    }

    // ── The rarity ranking, which is what keeps this quiet ───────────────────

    /// <summary>
    /// A line that whipsaws across its level on most bars is ranked as the routine thing it is.
    /// That ranking is what stops a chatty crossing from displacing a rare marker on a bar where
    /// both fire, and it is the reason this can be added without breaking the "not a per-bar
    /// readout" rule.
    /// </summary>
    [Fact]
    public void ACrossingIsRankedByHowOftenItHappens()
    {
        var rare = CrossesAt30();
        var chatty = Enumerable.Range(0, 60).Select(i => i % 2 == 0 ? 18.0 : 32.0).ToArray();

        Assert.Equal(1, PlaybackNarration.CrossCount(rare, 25));
        Assert.True(PlaybackNarration.CrossCount(chatty, 25) > 50);
    }

    /// <summary>
    /// A warmup prefix is not a crossing: NaN is not a side of the line.
    ///
    /// <para>
    /// The fixture starts ABOVE the level on its first real bar, because that is the only shape
    /// that tells the two rules apart. Treating NaN as "not above" makes the warmup the below
    /// side, so the first real value manufactures a crossing that never happened — and on ADX,
    /// whose first fourteen bars are always NaN, that would rank every band as one cross busier
    /// than it is and put a phantom "strong trend" on bar fifteen of every chart.
    /// </para>
    /// </summary>
    [Fact]
    public void AWarmupPrefixIsNotACrossing()
    {
        // NaN × 14, then straight in above 25 and never leaving: zero crossings.
        var warmupThenAbove = Enumerable.Range(0, 60)
            .Select(i => i < 14 ? double.NaN : 32.0).ToArray();
        Assert.Equal(0, PlaybackNarration.CrossCount(warmupThenAbove, 25));

        // And a real crossing after a warmup is still exactly one.
        var warmupThenCross = Enumerable.Range(0, 60)
            .Select(i => i < 14 ? double.NaN : (i < 30 ? 18.0 : 32.0)).ToArray();
        Assert.Equal(1, PlaybackNarration.CrossCount(warmupThenCross, 25));
    }

    /// <summary>
    /// And the same at the speaking end: the first real bar after a warmup does not announce a
    /// band it has not crossed into.
    /// </summary>
    [Fact]
    public void TheFirstBarAfterAWarmupDoesNotAnnounceAPhantomCrossing()
    {
        var series = Adx(Enumerable.Range(0, 60).Select(i => i < 14 ? double.NaN : 32.0).ToArray());
        Assert.Null(PlaybackNarration.SignalsForStep(State(series, 60), barIndex: 14));
    }

    /// <summary>
    /// The rarest thing on the bar leads. A crossing that happens constantly must not push a
    /// once-in-the-chart marker out of the two-clause ceiling.
    /// </summary>
    [Fact]
    public void TheRarerEventLeadsWhenBothFireOnOneBar()
    {
        var series = Adx(Enumerable.Range(0, 60).Select(i => i % 2 == 0 ? 18.0 : 32.0).ToArray());
        series.Config.Components.Add(new ComponentConfig
        {
            Name = "Rare", DisplayName = "Rare Signal", DisplayType = ComponentDisplayType.Dot,
            IsVisible = true, SignalSpeechTemplate = "Rare signal",
        });
        var rare = Enumerable.Repeat(double.NaN, 60).ToArray();
        rare[31] = 1.0;
        series.Data.ComponentData["Rare"] = rare;

        string? said = PlaybackNarration.SignalsForStep(State(series, 60), barIndex: 31);

        Assert.NotNull(said);
        Assert.StartsWith("Rare", said!.TrimStart(), StringComparison.OrdinalIgnoreCase);
    }
}
