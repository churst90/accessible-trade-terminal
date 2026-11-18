namespace AccessibleTrader.Core.Services.MarketPulse
{
    /// <summary>The four answers one pulse item can give.</summary>
    public enum MarketPulseKind
    {
        Change,
        Yield,
        Closed,
        Unavailable,
    }

    /// <summary>
    /// One item's answer. <see cref="Change"/> is a percent for a price and basis points for a
    /// yield (when <see cref="IsYield"/>); <see cref="YieldLevel"/> carries the quoted level;
    /// <see cref="MinutesOld"/> is null when the quote is fresh; and <see cref="SessionDate"/> is
    /// the session a closed market describes.
    /// </summary>
    public sealed record MarketPulseQuote(
        string Name,
        MarketPulseKind Kind,
        double Change = 0,
        double? YieldLevel = null,
        int? MinutesOld = null,
        DateOnly? SessionDate = null,
        bool IsYield = false);
}
