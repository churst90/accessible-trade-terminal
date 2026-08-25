using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// Pins TimeframeUtility.IsValid — the choke-point companion to
/// SymbolValidator (Phase A, 2026-07): timeframe tokens are interpolated into
/// provider URLs and drive period math, so malformed values must be rejected
/// in every mode before any request is built.
/// </summary>
public class TimeframeValidatorTests
{
    [Theory]
    [InlineData("1m")]
    [InlineData("15m")]
    [InlineData("1h")]
    [InlineData("4h")]
    [InlineData("1d")]
    [InlineData("1w")]
    [InlineData("1M")] // month — capital M must stay distinct from minutes
    public void EveryStandardTimeframe_IsValid(string tf)
    {
        Assert.True(TimeframeUtility.IsValid(tf));
    }

    [Fact]
    public void AllTimeframesList_IsFullyValid()
    {
        foreach (var tf in TimeframeUtility.AllTimeframes)
            Assert.True(TimeframeUtility.IsValid(tf), $"'{tf}' should be valid");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("1")]           // unit missing
    [InlineData("m")]           // count missing
    [InlineData("1x")]          // unknown unit
    [InlineData("-1m")]         // negative
    [InlineData("0m")]          // zero duration
    [InlineData("1m/../etc")]   // path/query injection shape
    [InlineData("1m&x=1")]
    [InlineData("999999999m")]  // absurd magnitude / over length cap
    [InlineData("1 m")]
    public void MalformedOrInjectionShapedTimeframes_AreRejected(string? tf)
    {
        Assert.False(TimeframeUtility.IsValid(tf));
    }
}
