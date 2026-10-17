using AccessibleTrader.Core.Services.Indicators;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// Cloud colours as the renderer reads them. <c>StandardRenderers</c> parses a cloud's hex with
/// <c>SKColor.TryParse</c>, which reads eight digits as #AARRGGBB, and uses that alpha. Ichimoku's
/// Kumo was written #RRGGBBAA ("#4CAF5060" / "#F4433660"), so the bull cloud drew a pinkish
/// #AF5060 and the bear cloud a near-opaque purple #433660. Found 2026-10-17 by the Market Cipher
/// comparison, which hit the same mistake in Cipher B's cross dots.
/// </summary>
public class CloudFillColourTests
{
    private static SKColor Parse(string hex)
    {
        Assert.True(SKColor.TryParse(hex, out var c), $"'{hex}' does not parse");
        return c;
    }

    [Fact]
    public void IchimokusKumoIsGreenAboveAndRedBelowAndTranslucent()
    {
        var fill = new IchimokuProvider().GetIndicators()[0].DefaultCloudFills[0];
        var bull = Parse(fill.BullishColorHex);
        var bear = Parse(fill.BearishColorHex);

        Assert.True(bull.Green > bull.Red && bull.Green > bull.Blue, $"bull cloud {bull} is not green");
        Assert.True(bear.Red > bear.Green && bear.Red > bear.Blue, $"bear cloud {bear} is not red");
        Assert.InRange(bull.Alpha, (byte)0x20, (byte)0xA0);   // a cloud under the candles, not a slab
        Assert.InRange(bear.Alpha, (byte)0x20, (byte)0xA0);
    }
}
