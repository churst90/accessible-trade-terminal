using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Core.Services.Leaderboard
{
    /// <summary>
    /// Lifetime trade statistics for one paper account, persisted inside
    /// <c>paper_account.json</c> under <c>Stats</c>.
    ///
    /// <para>
    /// <b>Why this exists rather than counting <c>History</c>.</b> History is capped at the newest
    /// 200 fills, so an active account's trade count and win rate cannot be recovered from it.
    /// The stats below are maintained on every fill and outlive the cap.
    /// </para>
    ///
    /// <para>
    /// A "closed trade" is one fill that reduced existing exposure — not one opening fill and not
    /// one <c>RealizedPnL != 0</c> (a break-even close realizes exactly zero). A fill that flips a
    /// position through flat counts once and can add to or subtract from <see cref="RealizedTotal"/>.
    /// </para>
    /// </summary>
    public sealed record PaperTradeStats(
        int ClosedTrades,
        int Wins,
        double RealizedTotal,
        DateTime? FirstFillAt,
        DateTime? LastFillAt)
    {
        public static readonly PaperTradeStats Empty = new(0, 0, 0.0, null, null);

        /// <summary>Wins over closed trades, or null when nothing has closed yet.</summary>
        public double? WinRate => ClosedTrades == 0 ? null : (double)Wins / ClosedTrades;

        /// <summary>
        /// Rebuild stats from a kept fill history, used only for accounts written before
        /// <c>Stats</c> existed.
        ///
        /// <para>
        /// <b>A seeded count only covers the kept fills.</b> History is capped at 200, so an
        /// account older than that cap has already lost the fills before its newest 200, and
        /// nothing on disk can recover them. New fills accumulate on top of the seed from then on.
        /// </para>
        ///
        /// <para>
        /// <paramref name="newestFirst"/> is the on-disk order. The replay walks it backwards with
        /// a per-symbol signed quantity: a fill moving that quantity toward zero closed
        /// <c>min(|fill|, |position|)</c>, which is exactly <c>Settle</c>'s closing definition.
        /// </para>
        /// </summary>
        public static PaperTradeStats SeedFromHistory(IReadOnlyList<PaperFillFacts> newestFirst)
        {
            if (newestFirst.Count == 0) return Empty;

            var positions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            int closed = 0, wins = 0;
            double realizedTotal = 0.0;
            DateTime? first = null, last = null;

            for (int i = newestFirst.Count - 1; i >= 0; i--)
            {
                var fill = newestFirst[i];
                if (string.IsNullOrEmpty(fill.Symbol) || fill.Quantity <= 0) continue;

                first ??= fill.FilledAt;
                last = fill.FilledAt;

                double signed = fill.Side == OrderSide.Buy ? fill.Quantity : -fill.Quantity;
                positions.TryGetValue(fill.Symbol, out double position);

                if (Math.Abs(position) > 1e-12 && Math.Sign(signed) != Math.Sign(position))
                {
                    closed++;
                    if (fill.RealizedPnL > 0) wins++;
                    realizedTotal += fill.RealizedPnL;
                }

                double next = position + signed;
                if (Math.Abs(next) < 1e-12) positions.Remove(fill.Symbol);
                else positions[fill.Symbol] = next;
            }

            return new PaperTradeStats(closed, wins, realizedTotal, first, last);
        }
    }

    /// <summary>The smallest fill shape the replay needs, shared by the provider and the calculator.</summary>
    public readonly record struct PaperFillFacts(
        string Symbol,
        OrderSide Side,
        double Quantity,
        DateTime FilledAt,
        double RealizedPnL);
}
