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

    /// <summary>What an item's change is measured from, for the detail read.</summary>
    public enum MarketPulseStart
    {
        Unknown,
        PreviousClose,
        MidnightUtc,
        Last24Hours,
        SessionBefore,
    }

    /// <summary>
    /// One item's answer. <see cref="Change"/> is a percent for a price and basis points for a
    /// yield (when <see cref="IsYield"/>); <see cref="YieldLevel"/> carries the quoted level;
    /// <see cref="MinutesOld"/> is null when the quote is fresh; and <see cref="SessionDate"/> is
    /// the session a closed market describes. The rest is for the detail read and the braille
    /// line: the short name, where the quote came from, the last price and the reference it is
    /// measured from (yields as percentages), and when the last minute bar ended.
    /// </summary>
    public sealed record MarketPulseQuote(
        string Name,
        MarketPulseKind Kind,
        double Change = 0,
        double? YieldLevel = null,
        int? MinutesOld = null,
        DateOnly? SessionDate = null,
        bool IsYield = false,
        string? Short = null,
        string? Provider = null,
        string? Symbol = null,
        double? Last = null,
        double? Reference = null,
        MarketPulseStart Start = MarketPulseStart.Unknown,
        DateTime? QuotedUtc = null);
}
