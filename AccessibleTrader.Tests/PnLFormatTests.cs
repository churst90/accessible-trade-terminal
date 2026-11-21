using AccessibleTrader.BlazorClient.Services;

namespace AccessibleTrader.Tests;

/// <summary>
/// A profit or loss is money, not a price. Formatted as a price it read "0.00250000" for a
/// quarter of a cent and "-120.0000" for a loss of 120 (the release review, 2026-11-21).
/// </summary>
public sealed class PnLFormatTests
{
    [Theory]
    [InlineData(-120, "-120.00")]
    [InlineData(1234.567, "1,234.57")]
    [InlineData(12.3456, "12.35")]
    [InlineData(0.5, "0.50")]
    [InlineData(0.0025, "0.0025")]
    [InlineData(-0.00000123, "-0.00000123")]
    [InlineData(0, "0.00")]
    [InlineData(double.NaN, "—")]
    public void A_profit_or_loss_reads_as_money(double pnl, string shown)
    {
        Assert.Equal(shown, PriceFormatter.FormatPnL(pnl));
    }

    [Fact]
    public void A_loss_under_a_cent_is_not_rounded_away()
    {
        Assert.NotEqual("0.00", PriceFormatter.FormatPnL(0.004));
        Assert.NotEqual("-0.00", PriceFormatter.FormatPnL(-0.004));
    }
}
