using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Classifies a Cipher MarkerFire signal as bullish (→ buy long) or bearish (→ sell short) by
/// keyword. Cipher providers don't expose this on the descriptor itself, but the component
/// names follow consistent conventions: anything with "Bull", "Buy", "Oversold", "Triple
/// Confluence Buy", "Hidden Bull", "Bullish Divergence", or "Manipulation" is a long entry;
/// anything with "Bear", "Sell", "Overbought", "Hidden Bear", "Bearish Divergence",
/// "Exhaustion", or the "Blood Diamond" is a short entry.
///
/// Why this matters: ComboSweep used to hardcode Side=Buy, which meant bear markers were being
/// tested as longs (always loses) and the user couldn't measure short-side edge at all. Routing
/// each marker to its natural side is the only way to get an honest read on whether bear
/// signals carry information.
/// </summary>
public static class MarkerSideHelper
{
    /// <summary>
    /// Returns Buy/Sell for the given marker descriptor, or null if it can't be classified
    /// (in which case the caller should skip it or default to Buy and warn).
    /// </summary>
    public static OrderSide? Classify(string componentName)
    {
        var n = componentName.ToLowerInvariant();

        // Bullish keywords first — "bullish divergence" must hit Buy before "bear" matches.
        if (n.Contains("bull") || n.Contains("buy") || n.Contains("oversold")
            || n.Contains("hidden bull") || n.Contains("triple confluence buy")
            || n.Contains("manipulation"))
            return OrderSide.Buy;

        if (n.Contains("bear") || n.Contains("sell") || n.Contains("overbought")
            || n.Contains("hidden bear") || n.Contains("exhaustion")
            || n.Contains("blood"))
            return OrderSide.Sell;

        return null;
    }
}
