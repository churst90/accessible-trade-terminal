using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>With narration on, crossing into a band is spoken as the band you entered.</b>
///
/// <para>
/// Cody, 2026-09-12: <i>"when narration is on, should it speak the zones as price crosses into
/// them?"</i> It already spoke the LINE — <c>ScanLevelCrosses</c> has said "crossed above
/// overbought, 70" since the narration routes were built — and on RSI that is the right sentence,
/// because the number is what the reader has calibrated against.
/// </para>
///
/// <para>
/// It is the wrong sentence for a band edge. "ADX: crossed above strong, 25" names the line you
/// passed; "ADX: strong trend" names where you now are, which is the entire content of an
/// indicator that measures trend strength and nothing else. The declaration for that arrived the
/// same day (<c>AboveLabel</c>/<c>BelowLabel</c>); this is narration reading it.
/// </para>
/// </summary>
public sealed class BandZoneNarrationTests
{
    private static List<Ohlcv> Bars(int n) => Enumerable.Range(0, n).Select(i => new Ohlcv(
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i), 100, 101, 99, 100, 10)).ToList();

    private static ChartSeries Adx(double[] values)
    {
        var config = new SeriesConfig { Id = "adx", IndicatorCode = "Adx", Name = "ADX", FriendlyName = "ADX", Pane = "Pane_Adx", IsAutoNarrated = true };
        config.Components.Add(new ComponentConfig { Name = "Adx", DisplayType = ComponentDisplayType.Line, IsVisible = true });
        config.Levels.Add(new LevelConfig { Name = "Strong", Value = 25, IsVisible = true, AboveLabel = "strong trend", BelowLabel = "weak trend" });
        var buffer = new SeriesDataBuffer { SeriesId = "adx" };
        buffer.ComponentData["Adx"] = values;
        return new ChartSeries(config, buffer);
    }

    private static ChartSeries Rsi(double[] values)
    {
        var config = new SeriesConfig { Id = "rsi", IndicatorCode = "SomeOsc", Name = "RSI", FriendlyName = "RSI", Pane = "Pane_Rsi", IsAutoNarrated = true };
        config.Components.Add(new ComponentConfig { Name = "Value", DisplayType = ComponentDisplayType.Oscillator, IsVisible = true });
        config.Levels.Add(new LevelConfig { Name = "Overbought", Value = 70, IsVisible = true });
        var buffer = new SeriesDataBuffer { SeriesId = "rsi" };
        buffer.ComponentData["Value"] = values;
        return new ChartSeries(config, buffer);
    }

    private static WorkspaceState State(int bars, ChartSeries s) => WorkspaceState.Initial with
    {
        Data = new TimeSeriesBuffer<Ohlcv>(Bars(bars)),
        ActiveSeries = ImmutableList.Create(s),
        CurrentDataIndex = bars - 1,
    };

    /// <summary>Seeds on a bar below the line, then closes a bar above it.</summary>
    private static string? CrossUp(Func<double[], ChartSeries> build, double below, double above)
    {
        var scanner = new NarrationScanner(new IndicatorContextAnalyzer());
        var seedValues = Enumerable.Repeat(below, 30).ToArray();
        var seeded = build(seedValues);
        scanner.Seed(seeded, State(30, seeded));

        var values = Enumerable.Repeat(below, 31).ToArray();
        values[30] = above;
        var crossed = build(values);
        return scanner.ScanAll(new[] { crossed }, State(31, crossed), closedBound: 30, isBarClose: true);
    }

    /// <summary>ADX says where it now is, not which line it passed.</summary>
    [Fact]
    public void CrossingIntoABand_SpeaksTheBandYouEntered()
    {
        string? said = CrossUp(Adx, below: 18, above: 30);

        Assert.NotNull(said);
        Assert.Contains("strong trend", said!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("crossed above", said!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>And falling out of it says the other side, because both were declared.</summary>
    [Fact]
    public void FallingOutOfABand_SpeaksTheBandBelow()
    {
        string? said = CrossUp(Adx, below: 30, above: 18);   // seed high, close low

        Assert.NotNull(said);
        Assert.Contains("weak trend", said!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An extreme keeps the older wording, which carries the NUMBER. "Crossed above overbought,
    /// 70" is the useful sentence on an RSI and the change must not reach it.
    /// </summary>
    [Fact]
    public void AnUnlabelledExtreme_StillNamesTheLineAndItsValue()
    {
        string? said = CrossUp(Rsi, below: 60, above: 80);

        Assert.NotNull(said);
        Assert.Contains("crossed above", said!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("70", said!);
    }

    /// <summary>
    /// A level the component does not subscribe to is not a crossing it can have. Narration now
    /// honours the same subscription the audio layer and the spoken zone word do — on a pane like
    /// Aroon's, where two scales share an axis, the other scale's line is not an event.
    /// </summary>
    [Fact]
    public void ALevelTheComponentDoesNotSubscribeTo_IsNotNarrated()
    {
        var scanner = new NarrationScanner(new IndicatorContextAnalyzer());
        ChartSeries Build(double[] values)
        {
            var s = Adx(values);
            s.Components[0].SubscribedLevelNames = new[] { "SomeOtherLine" };
            return s;
        }

        var seeded = Build(Enumerable.Repeat(18.0, 30).ToArray());
        scanner.Seed(seeded, State(30, seeded));

        var values = Enumerable.Repeat(18.0, 31).ToArray();
        values[30] = 30.0;
        var crossed = Build(values);

        string? said = scanner.ScanAll(new[] { crossed }, State(31, crossed), closedBound: 30, isBarClose: true);
        Assert.True(said == null || !said.Contains("strong trend", StringComparison.OrdinalIgnoreCase),
            $"narrated a line this component does not answer to: {said}");
    }

    /// <summary>A hidden line is not a crossing either — the same rule the whole pass has applied.</summary>
    [Fact]
    public void AHiddenLine_IsNotNarrated()
    {
        var scanner = new NarrationScanner(new IndicatorContextAnalyzer());
        ChartSeries Build(double[] values)
        {
            var s = Adx(values);
            s.Config.Levels[0].IsVisible = false;
            return s;
        }

        var seeded = Build(Enumerable.Repeat(18.0, 30).ToArray());
        scanner.Seed(seeded, State(30, seeded));

        var values = Enumerable.Repeat(18.0, 31).ToArray();
        values[30] = 30.0;
        var crossed = Build(values);

        string? said = scanner.ScanAll(new[] { crossed }, State(31, crossed), closedBound: 30, isBarClose: true);
        Assert.True(said == null || !said.Contains("strong trend", StringComparison.OrdinalIgnoreCase),
            $"narrated a switched-off line: {said}");
    }

    /// <summary>
    /// A component that declares an EMPTY level subscription narrates none of the series' levels.
    ///
    /// <para>
    /// A2f's F13. <c>SubscribesToLevel</c> distinguishes three states and only two were pinned:
    /// null means "no declaration, so every level", a non-empty list means "these", and an empty
    /// list means "none of them". Flipping the empty case from <c>false</c> to <c>true</c> — so an
    /// explicit opt-OUT becomes an opt-in to everything — went unnoticed by the whole suite. This
    /// is the knob Aroon needs: three components on one pane, of which only some live on the scale
    /// the level describes.
    /// </para>
    /// </summary>
    [Fact]
    public void AComponentDeclaringAnEmptyLevelSubscription_NarratesNoLevelAtAll()
    {
        string? said = CrossUp(
            values =>
            {
                var s = Rsi(values);
                s.Components[0].SubscribedLevelNames = Array.Empty<string>();
                return s;
            },
            below: 60, above: 80);

        Assert.True(said == null || !said.Contains("Overbought", StringComparison.OrdinalIgnoreCase),
            $"An empty subscription is an explicit 'none', not 'all'. Said: {said}");
    }

    /// <summary>
    /// The control: the same crossing with NO declaration does narrate, so "never narrate" cannot
    /// satisfy the test above.
    /// </summary>
    [Fact]
    public void TheSameCrossingWithNoSubscriptionDeclared_DoesNarrate()
    {
        string? said = CrossUp(Rsi, below: 60, above: 80);
        Assert.NotNull(said);
        Assert.Contains("Overbought", said!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A level keeps its NUMBER unless the name already is the number.
    ///
    /// <para>
    /// A2f's F16. <c>LevelPhrase</c> drops the value only when the level sits at zero AND is named
    /// "zero" — because "zero, 0" is a stutter. Loosening that <c>&amp;&amp;</c> to <c>||</c> made
    /// it drop the number for EITHER condition, so any level parked at zero under its own name —
    /// a Midpoint at 0, MACD's signal line — announced a crossing with no price in it. Nothing
    /// noticed, because every fixture that reached this line had a level whose name and value
    /// agreed.
    /// </para>
    /// </summary>
    [Fact]
    public void ALevelAtZeroThatIsNotCalledZero_KeepsItsNumber()
    {
        string? said = CrossUp(
            values =>
            {
                var config = new SeriesConfig
                {
                    Id = "macd", IndicatorCode = "SomeOsc", Name = "MACD", FriendlyName = "MACD",
                    Pane = "Pane_Macd", IsAutoNarrated = true,
                };
                config.Components.Add(new ComponentConfig
                {
                    Name = "Value", DisplayType = ComponentDisplayType.Oscillator, IsVisible = true,
                });
                config.Levels.Add(new LevelConfig { Name = "Midpoint", Value = 0, IsVisible = true });
                var buffer = new SeriesDataBuffer { SeriesId = "macd" };
                buffer.ComponentData["Value"] = values;
                return new ChartSeries(config, buffer);
            },
            below: -5, above: 5);

        Assert.NotNull(said);
        Assert.Contains("midpoint", said!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0", said!);
    }
}
