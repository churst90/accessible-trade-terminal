using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.BlazorClient.Components
{
    /// <summary>
    /// One tradeable account the trading dashboard can show.
    ///
    /// <para>
    /// This type exists because the dashboard used to ask <em>"what chart am I
    /// looking at?"</em> and answer every question from that: the venue for a cancel,
    /// the symbol to filter orders by, whether to render at all. A user with three
    /// resting <c>BTC/USD</c> orders and a <c>BTCUSDT</c> chart therefore saw an empty
    /// Orders tab — indistinguishable from "you have no orders" — and could not cancel
    /// them. The dashboard now asks <em>"what accounts do I have?"</em> instead, and
    /// this is the answer.
    /// </para>
    ///
    /// <para>
    /// In paper mode there is exactly one, and it is not tied to any venue: the paper
    /// broker discards the provider name (<c>GeneralOrderService.GetTradingProviderAsync</c>
    /// returns it for any name at all). It is still named <c>"Paper"</c> rather than
    /// left empty, because <c>SupportsTradingAsync</c> refuses an empty string before
    /// it ever reaches that routing.
    /// </para>
    /// </summary>
    /// <param name="ProviderName">What to pass to <c>IOrderExecutionService</c> for this account.</param>
    /// <param name="DisplayName">What to show in the Exchange column — short, because it is a table cell.</param>
    /// <param name="Slug">DOM-id safe, for headings and keys.</param>
    /// <param name="IsPaper">Whether this is the simulated broker.</param>
    public sealed record TradingAccount(string ProviderName, string DisplayName, string Slug, bool IsPaper);

    /// <summary>A position and the account it is held in. The row owns its venue, so
    /// closing it never has to guess from the chart.</summary>
    public sealed record AccountPosition(TradingAccount Account, Position Position);

    /// <summary>An open order and the account it rests on.</summary>
    public sealed record AccountOrder(TradingAccount Account, OpenOrder Order);

    /// <summary>A balance line and the account holding it.</summary>
    public sealed record AccountBalance(TradingAccount Account, Balance Balance);

    /// <summary>A fill and the account that executed it.</summary>
    public sealed record AccountFill(TradingAccount Account, TradeFill Fill);

    /// <summary>
    /// How a position is described in the Positions table's leading column.
    ///
    /// <para>
    /// Static and separate from the component so it can be tested without rendering
    /// anything, and so the visible cell and the spoken button label are built from
    /// the same words.
    /// </para>
    /// </summary>
    public static class PositionLabel
    {
        /// <summary>
        /// Symbol, margin mode and leverage as one phrase — "BTCUSDT isolated 1x".
        ///
        /// <para>
        /// These three belong together because none of them means anything alone: the
        /// same symbol held cross and held isolated are two different trades with two
        /// different liquidation prices, and leverage sets the scale of both. They were
        /// previously spread across the first and eighth columns with the mode not shown
        /// at all, so the one number a trader checks first — how far this can go wrong —
        /// could not be read without crossing the table.
        /// </para>
        ///
        /// <para>
        /// Plain lowercase <c>x</c>, not <c>×</c>: the multiplication sign is announced
        /// inconsistently across screen readers (the same reason the Cancel button stopped
        /// being <c>✕</c>), and "one x" is how a trader says it aloud anyway. A spot
        /// holding gets neither word — nothing is borrowed, there is no collateral held
        /// either way, and printing "cross 1x" over it would describe a liquidation that
        /// cannot happen.
        /// </para>
        /// </summary>
        public static string Instrument(Position p)
        {
            string mode = p.MarginMode switch
            {
                MarginMode.Cross    => " cross",
                MarginMode.Isolated => " isolated",
                _                   => "",
            };
            // Leverage rides with the mode: on a margin position 1x is a fact worth
            // stating, on a spot holding it is noise on every single row.
            string lev = mode.Length > 0 || p.Leverage > 1
                ? " " + p.Leverage.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x"
                : "";
            return p.Symbol + mode + lev;
        }

        /// <summary>
        /// Which way the position is pointing. A signed quantity states this already,
        /// but only to someone who can see the minus sign — and a leading "-" is exactly
        /// the character screen readers are most likely to drop at default punctuation
        /// settings, which turns a short into a long silently.
        /// </summary>
        public static string Direction(Position p) =>
            p.Quantity > 0 ? "Long" : p.Quantity < 0 ? "Short" : "Flat";
    }
}
