using System.Globalization;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.Extensions.Logging;

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

    /// <summary>
    /// Which accounts there are: the trading dashboard's Positions tab and the trade status bar ask
    /// the same question and must get the same answer (moved here from the dashboard 2026-11-04).
    /// </summary>
    public static class TradingAccountDirectory
    {
        /// <summary>DOM-id safe form of a provider name.</summary>
        public static string Slugify(string s) =>
            new string(s.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-').ToArray());

        /// <summary>
        /// In paper mode, the one paper account. Otherwise every venue with an active, non-withdrawal
        /// key that can trade, live keys only where the venue has a practice environment (Cody,
        /// 2026-10-21: "sandbox ones should be omitted"). A venue with no sandbox signs a
        /// Paper-labelled key against the real account, so its balances are real money and it stays.
        /// </summary>
        public static async Task<List<TradingAccount>> EnumerateAsync(
            bool paperMode, IApiKeyService apiKeys, IOrderExecutionService orders,
            string? activeKeyNickname = null, ILogger? logger = null)
        {
            if (paperMode)
                return new List<TradingAccount> { new("Paper", "Paper", "paper", IsPaper: true) };

            List<ApiKeyConfig> keys;
            try { keys = await apiKeys.GetAllKeysAsync(); }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Could not enumerate trading accounts.");
                return new List<TradingAccount>();
            }

            var usable = keys.Where(k => k.IsActive && !k.AllowsWithdrawal).ToList();
            var accounts = new List<TradingAccount>();
            foreach (var provider in usable.Select(k => k.Provider)
                                           .Distinct(StringComparer.OrdinalIgnoreCase)
                                           .Where(p => !string.IsNullOrEmpty(p))
                                           .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                if (!await orders.SupportsTradingAsync(provider)) continue;

                var activeKey = usable.First(k => string.Equals(k.Provider, provider, StringComparison.OrdinalIgnoreCase));
                if (!ProviderConfigKeys.IsLive(activeKey.Environment)
                    && await orders.HasPracticeEnvironmentAsync(provider))
                    continue;

                // The nickname only earns its place when there is more than one key for
                // this venue; otherwise "Kraken (main)" is a longer way to say "Kraken"
                // in a column that is read on every row.
                var forProvider = usable.Where(k => string.Equals(k.Provider, provider, StringComparison.OrdinalIgnoreCase)).ToList();
                string display = forProvider.Count > 1
                    ? $"{provider} ({(forProvider.FirstOrDefault(k => k.Nickname == activeKeyNickname) ?? forProvider[0]).Nickname})"
                    : provider;
                accounts.Add(new TradingAccount(provider, display, Slugify(provider), IsPaper: false));
            }
            return accounts;
        }
    }

    /// <summary>
    /// One open trade as the trade status bar shows it (2026-11-04). Cody: "if I have an open position
    /// I could see at a glance at the bottom of the screen ... my trades listed something like
    /// 'btcusdt Kraken Futures isolated spot long, up 14.3%' and have the text green and red to
    /// indicate the direction of the p and l." <see cref="Sign"/> is +1 up, -1 down, 0 flat.
    /// </summary>
    public sealed record TradeStatusItem(string Key, string Text, int Sign);

    /// <summary>The words for a trade in the status bar; static so they can be tested without rendering.</summary>
    public static class TradeStatusLine
    {
        /// <summary>
        /// Profit or loss as a percent of the margin put up: the price move times the leverage, so a
        /// 10x long up 1% in price reads "up 10%". Null when there is no entry price to measure from.
        /// </summary>
        public static double? ReturnPercent(Position p)
        {
            double margin = Math.Abs(p.Quantity) * p.AveragePrice / Math.Max(1.0, p.Leverage);
            if (!(margin > 0) || double.IsNaN(p.UnrealizedPnL)) return null;
            return p.UnrealizedPnL / margin * 100;
        }

        /// <summary>
        /// "BTCUSDT Kraken Futures isolated 1x long, up 14.3%". <paramref name="figure"/> is "percent",
        /// "amount" or "both"; a position with no entry price falls back to the amount.
        /// </summary>
        public static TradeStatusItem Describe(AccountPosition row, string figure)
        {
            var p = row.Position;
            string instrument = PositionLabel.Instrument(p);
            string modeAndLeverage = instrument.Length > p.Symbol.Length ? instrument[p.Symbol.Length..] : "";
            string direction = PositionLabel.Direction(p).ToLowerInvariant();

            double? pct = ReturnPercent(p);
            double pnl = double.IsNaN(p.UnrealizedPnL) ? 0 : p.UnrealizedPnL;
            int sign = Math.Abs(pnl) < 0.005 ? 0 : Math.Sign(pnl);
            string pctText = pct is double v ? Math.Abs(v).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "";
            string amountText = Math.Abs(pnl).ToString("#,##0.00", CultureInfo.InvariantCulture);
            string amount = figure switch
            {
                "amount" => amountText,
                "both"   => pctText.Length > 0 ? pctText + ", " + amountText : amountText,
                _        => pctText.Length > 0 ? pctText : amountText,
            };
            string move = sign > 0 ? "up " + amount : sign < 0 ? "down " + amount : "flat";
            return new TradeStatusItem(row.Account.Slug + "|" + p.Symbol + "|" + p.MarginMode,
                $"{p.Symbol} {row.Account.DisplayName}{modeAndLeverage} {direction}, {move}", sign);
        }
    }
}
