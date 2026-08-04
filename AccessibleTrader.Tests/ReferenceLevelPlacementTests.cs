using AccessibleTrader.Core.Services.Input;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// The <c>0</c> shortcut must place a level in the units of the pane it lands on.
///
/// <para>
/// <b>Provenance.</b> A maintainer screenshot showed a BTC 4h chart with the y-axis running
/// 0 → 70,000 and every candle squashed into the top tenth of the pane. It was not an indicator: the
/// saved workspace held <c>{"Name":"Zero","Value":0.0,"IsVisible":true}</c> attached to the
/// <c>CANDLES</c> series, put there by this command, which added a level at literal zero to whatever
/// series had focus. <c>ViewportRangeCalculator</c> then dutifully expanded the price range to reach
/// it — at every launch, because levels persist.
/// </para>
///
/// <para>
/// The command was written for oscillators, where zero is a real and useful constant. The bug is
/// that a price pane has <i>no</i> meaningful constant, so the same key had to mean something
/// different there.
/// </para>
/// </summary>
public class ReferenceLevelPlacementTests
{
    /// <summary>The exact reported case.</summary>
    [Fact]
    public void ThePriceSeriesNeverGetsALevelAtZero()
    {
        var level = ReferenceLevelPlacement.For("Main", cursorPrice: 63_920.11, out string reason);

        Assert.NotNull(level);
        Assert.NotEqual(0, level!.Value);
        Assert.Equal(63_920.11, level.Value);
        Assert.Contains("63920", reason.Replace(",", ""));
    }

    /// <summary>
    /// An empty pane string is treated as the price pane everywhere else in the renderer, so it must
    /// be treated as one here too — otherwise the guard has a hole in exactly the case that produced
    /// the defect.
    /// </summary>
    [Theory]
    [InlineData("Main")]
    [InlineData("main")]
    [InlineData("")]
    [InlineData(null)]
    public void PricePanesAreRecognisedIncludingTheUnsetCase(string? pane)
    {
        Assert.True(ReferenceLevelPlacement.IsPricePane(pane));
        var level = ReferenceLevelPlacement.For(pane, 100, out _);
        Assert.NotNull(level);
        Assert.Equal(100, level!.Value);
    }

    /// <summary>Oscillators keep the behaviour the command was written for.</summary>
    [Theory]
    [InlineData("Pane_CIPHER_B")]
    [InlineData("Pane_LOUKAS_CYCLES")]
    [InlineData("Volume")]
    public void AnOscillatorPaneStillGetsItsZeroLine(string pane)
    {
        var level = ReferenceLevelPlacement.For(pane, cursorPrice: 63_920.11, out string reason);

        Assert.NotNull(level);
        Assert.Equal(0, level!.Value);
        Assert.Equal("Zero", level.Name);
        Assert.Contains("Zero line", reason);
    }

    /// <summary>
    /// With no data there is no price to place at, and the command must say so rather than fall back
    /// to zero — falling back is precisely how the original defect happened.
    /// </summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(0)]
    [InlineData(-5)]
    public void APricePaneWithNoUsablePriceRefusesAndExplains(double cursorPrice)
    {
        var level = ReferenceLevelPlacement.For("Main", cursorPrice, out string reason);

        Assert.Null(level);
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.Contains("cursor", reason, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every outcome speaks. A key that declines silently is indistinguishable from one that is not
    /// bound — the standing rule for this codebase.
    /// </summary>
    [Theory]
    [InlineData("Main", 100.0)]
    [InlineData("Main", double.NaN)]
    [InlineData("Pane_RSI", 100.0)]
    public void EveryOutcomeHasSomethingToSay(string pane, double price)
    {
        ReferenceLevelPlacement.For(pane, price, out string reason);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    /// <summary>
    /// A sub-cent asset is the case a naive "is it big enough to be a price" check would break on,
    /// and the terminal trades those.
    /// </summary>
    [Fact]
    public void ASubCentPriceIsStillAPrice()
    {
        var level = ReferenceLevelPlacement.For("Main", 0.00004312, out _);
        Assert.NotNull(level);
        Assert.Equal(0.00004312, level!.Value);
    }
}
