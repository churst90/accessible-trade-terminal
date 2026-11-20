using AccessibleTrader.Sdk.Plugins;
using Newtonsoft.Json;

namespace AccessibleTrader.Core.Services.Leaderboard
{
    /// <summary>
    /// The leaderboard's read model of <c>paper_account.json</c>: only the fields a published
    /// row needs. Parsed with Newtonsoft because that is the library the account file is
    /// written with.
    /// </summary>
    public sealed class PaperAccountFile
    {
        public double Cash { get; set; }
        public List<PaperAccountPosition> Positions { get; set; } = new();
        public List<PaperAccountCollateral> Collateral { get; set; } = new();
        public List<PaperAccountChart> Charts { get; set; } = new();
        public PaperTradeStats? Stats { get; set; }

        /// <summary>Kept only for the pre-Stats seed fallback; capped at the newest 200 fills.</summary>
        public List<PaperAccountFill> History { get; set; } = new();

        /// <summary>The persisted stats, or the same replay the broker uses when the file predates them.</summary>
        public PaperTradeStats EffectiveStats()
        {
            if (Stats != null) return Stats;

            var facts = new List<PaperFillFacts>();
            foreach (var fill in History ?? new List<PaperAccountFill>())
                if (fill != null) facts.Add(fill.ToFacts());
            var held = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var position in Positions ?? new List<PaperAccountPosition>())
                if (position != null) held[position.Symbol] = position.Qty;
            return PaperTradeStats.SeedFromHistory(facts, held);
        }

        /// <summary>
        /// Parse, never throw. A malformed file yields false so one broken account cannot stop
        /// the rest of the leaderboard being published.
        /// </summary>
        public static bool TryParse(string json, out PaperAccountFile? file)
        {
            file = null;
            if (string.IsNullOrWhiteSpace(json)) return false;

            try
            {
                file = JsonConvert.DeserializeObject<PaperAccountFile>(json);
                return file != null;
            }
            catch
            {
                file = null;
                return false;
            }
        }
    }

    public sealed class PaperAccountPosition
    {
        public string Symbol { get; set; } = "";
        public double Qty { get; set; }
        public double Avg { get; set; }
    }

    public sealed class PaperAccountCollateral
    {
        public string Symbol { get; set; } = "";
        public double Value { get; set; }
    }

    public sealed class PaperAccountChart
    {
        public string Symbol { get; set; } = "";
        public string Market { get; set; } = "";
        public string Provider { get; set; } = "";
        public string Timeframe { get; set; } = "";
    }

    public sealed class PaperAccountFill
    {
        public string Symbol { get; set; } = "";
        public OrderSide Side { get; set; }
        public double Quantity { get; set; }
        public DateTime FilledAt { get; set; }
        public double RealizedPnL { get; set; }

        public PaperFillFacts ToFacts() => new(Symbol, Side, Quantity, FilledAt, RealizedPnL);
    }

    /// <summary>
    /// One position the account holds now, as the leaderboard shows it (Cody, 2026-11-20: "if
    /// the user has an open position, make that visible in the leaderboard"). A short has a
    /// negative <see cref="PaperAccountPosition.Qty"/> in the file; here it is
    /// <see cref="IsShort"/> with a positive <see cref="Quantity"/>.
    /// </summary>
    /// <param name="Mark">The price it was valued at, or null when none could be fetched (then
    /// the position is counted at its entry price and its profit is unknown, not zero).</param>
    /// <param name="UnrealizedPnL">Money made or lost if it were closed at <paramref name="Mark"/>,
    /// before fees; null with no mark.</param>
    /// <param name="UnrealizedPct">The same as a percentage of the entry price, positive when
    /// the trade is winning on either side; null with no mark.</param>
    public sealed record OpenPositionStanding(
        string Symbol,
        bool IsShort,
        double Quantity,
        double Entry,
        double? Mark,
        double? UnrealizedPnL,
        double? UnrealizedPct);

    /// <summary>
    /// One account's place on the leaderboard, computed purely from its file and a price lookup.
    /// </summary>
    public sealed record PaperAccountStanding(
        double Equity,
        double ReturnPct,
        double RealizedPnL,
        int TradeCount,
        double? WinRate,
        DateTime? FirstTradeAt,
        DateTime? LastTradeAt,
        bool MarkToMarketStale,
        IReadOnlyList<OpenPositionStanding> OpenPositions)
    {
        /// <summary>
        /// Equity = cash + each position at its mark + locked collateral. That one formula is
        /// right for shorts too: their quantity is negative and their collateral holds the
        /// proceeds plus margin, exactly as the broker's <c>Settle</c> accounts for it.
        ///
        /// <para>
        /// A position with no usable price (null, zero, negative, or a lookup that throws) is
        /// valued at its entry price and the row is flagged stale. Never a fabricated mark and
        /// never a crash — a leaderboard that takes the site down is worse than a stale row.
        /// </para>
        /// </summary>
        public static PaperAccountStanding Compute(
            PaperAccountFile file, Func<string, double?> priceOf, double startingBalance)
        {
            ArgumentNullException.ThrowIfNull(file);

            double equity = file.Cash;
            bool stale = false;
            var open = new List<OpenPositionStanding>();

            foreach (var position in file.Positions ?? new List<PaperAccountPosition>())
            {
                // A zero-quantity row contributes nothing and must not make the row stale.
                if (position == null || Math.Abs(position.Qty) < 1e-12) continue;

                double? mark = null;
                try { mark = priceOf?.Invoke(position.Symbol); }
                catch { mark = null; }

                double price;
                if (mark is > 0)
                {
                    price = mark.Value;
                }
                else
                {
                    price = position.Avg;
                    stale = true;
                }

                equity += position.Qty * price;

                bool known = mark is > 0;
                double? pnl = known ? position.Qty * (mark!.Value - position.Avg) : null;
                double? pct = known && position.Avg > 0
                    ? (mark!.Value - position.Avg) / position.Avg * 100.0 * Math.Sign(position.Qty)
                    : null;
                open.Add(new OpenPositionStanding(
                    position.Symbol, position.Qty < 0, Math.Abs(position.Qty), position.Avg,
                    known ? mark : null, pnl, pct));
            }

            foreach (var collateral in file.Collateral ?? new List<PaperAccountCollateral>())
                if (collateral != null) equity += collateral.Value;

            double returnPct = startingBalance > 0
                ? (equity - startingBalance) / startingBalance * 100.0
                : 0.0;

            var stats = file.EffectiveStats();
            double? winRate = stats.ClosedTrades == 0
                ? null
                : (double)stats.Wins / stats.ClosedTrades;

            return new PaperAccountStanding(
                equity,
                returnPct,
                stats.RealizedTotal,
                stats.ClosedTrades,
                winRate,
                stats.FirstFillAt,
                stats.LastFillAt,
                stale,
                open.OrderBy(p => p.Symbol, StringComparer.OrdinalIgnoreCase).ToList());
        }
    }
}
