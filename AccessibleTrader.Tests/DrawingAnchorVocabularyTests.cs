using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Drawing.Calculators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A drawing asks for each point by what it IS, not by its number.</b>
///
/// <para>
/// Cody, 2026-09-13: <i>"make sure the measure tool specifically says which 3 points I'm setting as
/// I set them, 'move to the take profit and press the shortcut again' type deal."</i> The prompts
/// said "anchor 1", "anchor 2", "anchor 3" for almost everything — a position in a sequence rather
/// than a thing. On a tool whose three points are an entry, a stop and a target, "anchor 2" is the
/// one description that cannot help you decide where to put it.
/// </para>
///
/// <para>
/// Two tools had hand-written wording and <b>one of them was a step behind the state machine it
/// described</b>: risk/reward's second prompt said "entry at {price}. Navigate to stop loss" on the
/// press that set the STOP. Anyone following it put the stop where the target belongs and got an
/// inverted ratio with nothing to say so.
/// </para>
/// </summary>
public sealed class DrawingAnchorVocabularyTests
{
    /// <summary>
    /// The names and the calculator must agree, because the calculator is what the numbers mean.
    /// <c>RiskRewardCalculator</c> reads anchor 1 as entry, anchor 2 as stop and anchor 3 as
    /// target; the prompts have to ask for them in that order.
    /// </summary>
    [Fact]
    public void RiskRewardAsksForEntryThenStopThenTarget()
    {
        var names = DrawingInteractionManager.AnchorNames(DrawingType.RiskReward);

        Assert.Equal(3, names.Length);
        Assert.Equal("entry", names[0]);
        Assert.Equal("stop loss", names[1]);
        Assert.Equal("take profit", names[2]);
    }

    /// <summary>
    /// And the calculator agrees, asserted against the calculator rather than against a comment:
    /// a 100/90/130 placement is a risk of 10 and a reward of 30, so 1 to 3. If the prompts and
    /// the calculator ever disagree about which anchor is which, this pins which one is right.
    /// </summary>
    [Fact]
    public void TheAnchorOrderMatchesWhatTheCalculatorComputes()
    {
        var drawing = new DrawingData
        {
            Type = DrawingType.RiskReward,
            AnchorPrice1 = 100, AnchorPrice2 = 90, AnchorPrice3 = 130,
        };
        new RiskRewardCalculator().Calculate(drawing, Array.Empty<Ohlcv>());

        Assert.Equal(3.0, drawing.RiskRewardRatio, 3);
    }

    /// <summary>
    /// The measure tool is TWO points. Cody expected three, which is the confusion this vocabulary
    /// exists to end: the three-point entry/stop/target tool is Risk/Reward. Naming the points is
    /// what makes the two distinguishable while you are placing them.
    /// </summary>
    [Fact]
    public void TheMeasureToolIsTwoPointsAndSaysWhatTheyAre()
    {
        var names = DrawingInteractionManager.AnchorNames(DrawingType.MeasureTool);

        Assert.Equal(2, names.Length);
        Assert.Contains("start", names[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("end", names[1], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// No point is called "anchor". That word is the defect: it names a slot in an array, which is
    /// an implementation detail of the placement loop and not a thing on a chart.
    /// </summary>
    [Theory]
    [InlineData(DrawingType.RiskReward)]
    [InlineData(DrawingType.MeasureTool)]
    [InlineData(DrawingType.TrendLine)]
    [InlineData(DrawingType.FibRetracement)]
    [InlineData(DrawingType.FibExtension)]
    [InlineData(DrawingType.AndrewsPitchfork)]
    [InlineData(DrawingType.Channel)]
    [InlineData(DrawingType.Rectangle)]
    public void NoPointIsCalledAnAnchor(DrawingType type)
    {
        foreach (var name in DrawingInteractionManager.AnchorNames(type))
        {
            Assert.DoesNotContain("anchor", name, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(name));
        }
    }

    /// <summary>
    /// Every multi-point drawing names at least as many points as it takes presses to place.
    /// A three-point tool with two names would fall back to "point 3", which is the wording this
    /// replaced.
    /// </summary>
    [Theory]
    [InlineData(DrawingType.RiskReward, 3)]
    [InlineData(DrawingType.FibExtension, 3)]
    [InlineData(DrawingType.AndrewsPitchfork, 3)]
    [InlineData(DrawingType.MeasureTool, 2)]
    [InlineData(DrawingType.TrendLine, 2)]
    public void EveryToolNamesAsManyPointsAsItAsksFor(DrawingType type, int expected)
        => Assert.True(DrawingInteractionManager.AnchorNames(type).Length >= expected,
            $"{type} takes {expected} presses but names only {DrawingInteractionManager.AnchorNames(type).Length} points");

    /// <summary>
    /// The measure tool's own answer — distance, percentage and bar count — is computed and was
    /// only ever drawn. On an audio-first terminal the result of a measurement is the point of
    /// taking it, so it has to survive as a sentence.
    /// </summary>
    [Fact]
    public void TheMeasureToolComputesAnAnswerWorthSpeaking()
    {
        var bars = Enumerable.Range(0, 20).Select(i => new Ohlcv(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i), 100, 101, 99, 100, 1)).ToList();
        var drawing = new DrawingData
        {
            Type = DrawingType.MeasureTool,
            AnchorDate1 = bars[2].Date, AnchorPrice1 = 100,
            AnchorDate2 = bars[12].Date, AnchorPrice2 = 110,
        };

        new MeasureToolCalculator().Calculate(drawing, bars);

        Assert.False(string.IsNullOrWhiteSpace(drawing.MeasureResult));
        Assert.Contains("10", drawing.MeasureResult!);      // the distance
        Assert.Contains("bars", drawing.MeasureResult!);    // and the time
    }
}
