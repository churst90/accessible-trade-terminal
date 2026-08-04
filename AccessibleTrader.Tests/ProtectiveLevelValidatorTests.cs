using AccessibleTrader.Core.Services.Trading;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// A hand-typed stop or target is checked before it reaches a broker.
///
/// <para>
/// <b>Why the check cannot be left to the venue.</b> A stop on the wrong side of the market is not
/// universally rejected — several exchanges accept it and trigger it <b>immediately</b>, closing the
/// position at market the instant it is placed. So the expensive mistake is exactly the one the
/// broker does not catch: a long protected by a stop above the price is a position that liquidates
/// itself on submission.
/// </para>
///
/// <para>
/// The rule inverts with direction, which is the kind of thing that gets written correctly in one
/// place and backwards in another. Hence one function, tested in both directions for both levels.
/// </para>
/// </summary>
public class ProtectiveLevelValidatorTests
{
    private const double Price = 64_000;

    // ── The four direction cases ─────────────────────────────────────────────

    [Fact]
    public void ALongStopMustSitBelowThePrice()
    {
        Assert.True(ProtectiveLevelValidator.Validate("63000", ProtectiveLevel.StopLoss, isLong: true, Price).Ok);

        var bad = ProtectiveLevelValidator.Validate("65000", ProtectiveLevel.StopLoss, isLong: true, Price);
        Assert.False(bad.Ok);
        Assert.Contains("close the position immediately", bad.Message);
    }

    [Fact]
    public void AShortStopMustSitAboveThePrice()
    {
        Assert.True(ProtectiveLevelValidator.Validate("65000", ProtectiveLevel.StopLoss, isLong: false, Price).Ok);
        Assert.False(ProtectiveLevelValidator.Validate("63000", ProtectiveLevel.StopLoss, isLong: false, Price).Ok);
    }

    [Fact]
    public void ALongTargetMustSitAboveThePrice()
    {
        Assert.True(ProtectiveLevelValidator.Validate("70000", ProtectiveLevel.TakeProfit, isLong: true, Price).Ok);

        var bad = ProtectiveLevelValidator.Validate("60000", ProtectiveLevel.TakeProfit, isLong: true, Price);
        Assert.False(bad.Ok);
        Assert.Contains("already behind you", bad.Message);
    }

    [Fact]
    public void AShortTargetMustSitBelowThePrice()
    {
        Assert.True(ProtectiveLevelValidator.Validate("60000", ProtectiveLevel.TakeProfit, isLong: false, Price).Ok);
        Assert.False(ProtectiveLevelValidator.Validate("70000", ProtectiveLevel.TakeProfit, isLong: false, Price).Ok);
    }

    /// <summary>Exactly at the price is not a protective level, it is an immediate exit.</summary>
    [Fact]
    public void ALevelExactlyAtThePriceIsRefused()
    {
        Assert.False(ProtectiveLevelValidator.Validate("64000", ProtectiveLevel.StopLoss, true, Price).Ok);
        Assert.False(ProtectiveLevelValidator.Validate("64000", ProtectiveLevel.TakeProfit, true, Price).Ok);
    }

    // ── What people actually type ────────────────────────────────────────────

    [Theory]
    [InlineData("63,000")]
    [InlineData("$63000")]
    [InlineData("  63000  ")]
    [InlineData("63000.00")]
    public void CommonWaysOfTypingAPriceAreAccepted(string typed)
        => Assert.True(ProtectiveLevelValidator.Validate(typed, ProtectiveLevel.StopLoss, true, Price).Ok);

    [Theory]
    [InlineData("abc")]
    [InlineData("6-3000")]
    [InlineData("--")]
    public void NonsenseIsRefusedWithTheTextQuotedBack(string typed)
    {
        var r = ProtectiveLevelValidator.Validate(typed, ProtectiveLevel.StopLoss, true, Price);
        Assert.False(r.Ok);
        Assert.Contains("is not a price", r.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-100")]
    public void ANonPositivePriceIsRefused(string typed)
        => Assert.False(ProtectiveLevelValidator.Validate(typed, ProtectiveLevel.StopLoss, true, Price).Ok);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnEmptyFieldSaysHowToLeaveItAlone(string? typed)
    {
        var r = ProtectiveLevelValidator.Validate(typed, ProtectiveLevel.StopLoss, true, Price);
        Assert.False(r.Ok);
        Assert.Contains("Escape", r.Message);
    }

    /// <summary>Sub-cent instruments are ordinary here, not an edge case.</summary>
    [Fact]
    public void SubCentPricesWork()
    {
        var r = ProtectiveLevelValidator.Validate("0.0261", ProtectiveLevel.StopLoss, true, currentPrice: 0.0268);
        Assert.True(r.Ok);
        Assert.Equal(0.0261, r.Value, 8);
    }

    /// <summary>
    /// Every outcome speaks, including success. A silent text field is indistinguishable from one
    /// that does nothing.
    /// </summary>
    [Theory]
    [InlineData("63000")]
    [InlineData("65000")]
    [InlineData("rubbish")]
    [InlineData("")]
    public void EveryOutcomeHasAMessage(string typed)
        => Assert.False(string.IsNullOrWhiteSpace(
               ProtectiveLevelValidator.Validate(typed, ProtectiveLevel.StopLoss, true, Price).Message));

    [Fact]
    public void WithNoCurrentPriceNothingIsChanged()
    {
        var r = ProtectiveLevelValidator.Validate("63000", ProtectiveLevel.StopLoss, true, currentPrice: 0);
        Assert.False(r.Ok);
        Assert.Contains("no current price", r.Message);
    }

    /// <summary>The distance is what tells you whether a stop is prudent or a hair away.</summary>
    [Fact]
    public void DistanceIsReportedAsAPercentage()
        => Assert.Equal("1.56% away", ProtectiveLevelValidator.DistanceFromPrice(63_000, 64_000));
}
