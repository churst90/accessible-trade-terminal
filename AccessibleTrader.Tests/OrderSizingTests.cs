using AccessibleTrader.Core.Services.Trading;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Sizing an order by its value, and closing part of a position</b> (2026-11-04). Cody: "I should
/// be able to specify my position size in contracts, not in btc ... '1' that means '1 btc' not '1
/// dollar'" and "when i limit close a trade i should have the option of closing a portion/percentage
/// of the position". See <see cref="OrderSizing"/>.
/// </summary>
public sealed class OrderSizingTests
{
    [Theory]
    [InlineData(1000, 50000, 0.02)]
    [InlineData(1000, 30000, 0.03333333)]   // rounded down: never worth more than typed
    [InlineData(100, 3000, 0.03333333)]
    [InlineData(5, 0.5, 10)]
    public void A_value_is_turned_into_base_units_at_the_price_rounded_down(double value, double price, double qty)
    {
        double q = OrderSizing.QuantityFromValue(value, price);
        Assert.Equal(qty, q, 10);
        Assert.True(q * price <= value + 1e-9, $"{q} at {price} is worth more than {value}");
    }

    [Theory]
    [InlineData(0, 50000)]
    [InlineData(-5, 50000)]
    [InlineData(1000, 0)]
    [InlineData(1000, double.NaN)]
    [InlineData(double.PositiveInfinity, 50000)]
    public void No_quantity_without_a_positive_value_and_price(double value, double price)
        => Assert.Equal(0, OrderSizing.QuantityFromValue(value, price));

    [Fact]
    public void All_of_a_position_is_the_whole_position_exactly()
    {
        // A float that does not survive *100/100 must still close in full, not leave dust.
        double size = 0.1 + 0.2;
        Assert.Equal(size, OrderSizing.PartialCloseQuantity(size, 100));
        Assert.Equal(size, OrderSizing.PartialCloseQuantity(-size, 100));
    }

    [Theory]
    [InlineData(2.0, 50, 1.0)]
    [InlineData(-2.0, 25, 0.5)]           // a short's size is its absolute quantity
    [InlineData(1.0, 33.333, 0.33333)]
    [InlineData(0.3, 10, 0.03)]
    public void Part_of_a_position_is_that_share_rounded_down(double position, double percent, double qty)
        => Assert.Equal(qty, OrderSizing.PartialCloseQuantity(position, percent), 10);

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(100.5)]
    [InlineData(double.NaN)]
    public void A_percent_outside_0_to_100_closes_nothing(double percent)
        => Assert.Equal(0, OrderSizing.PartialCloseQuantity(2.0, percent));

    [Theory]
    [InlineData("50", 50)]
    [InlineData(" 12.5 % ", 12.5)]
    [InlineData("100", 100)]
    public void A_typed_percent_is_read(string text, double expected)
    {
        var r = OrderSizing.ParsePercent(text);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(expected, r.Value);
    }

    [Theory]
    [InlineData("", "Enter a percent")]
    [InlineData("half", "is not a percent")]
    [InlineData("0", "above zero")]
    [InlineData("150", "cannot be more than 100")]
    public void A_bad_percent_says_why(string text, string because)
    {
        var r = OrderSizing.ParsePercent(text);
        Assert.False(r.Ok);
        Assert.Contains(because, r.Message);
    }
}
