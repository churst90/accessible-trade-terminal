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
        /// <paramref name="newestFirst"/> is the on-disk order. The replay walks it from the
        /// NEWEST fill back, starting from the positions the account holds now
        /// (<paramref name="heldNow"/>): the position before a fill is the position after it
        /// less the fill. A fill whose position-before was non-zero and on the other side
        /// closed exposure — exactly <c>Settle</c>'s closing definition. Walking forward from
        /// flat instead was wrong for any capped history: the oldest kept fill can close a
        /// position whose opening fill was dropped, a forward replay reads it as an opening,
        /// and every later fill on that symbol is then classified against a phantom position.
        /// </para>
        /// </summary>
        public static PaperTradeStats SeedFromHistory(
            IReadOnlyList<PaperFillFacts> newestFirst,
            IReadOnlyDictionary<string, double>? heldNow = null)
        {
            if (newestFirst.Count == 0) return Empty;

            var positions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (heldNow != null)
                foreach (var kv in heldNow) positions[kv.Key] = kv.Value;

            int closed = 0, wins = 0;
            double realizedTotal = 0.0;
            DateTime? first = null, last = null;

            for (int i = 0; i < newestFirst.Count; i++)
            {
                var fill = newestFirst[i];
                if (string.IsNullOrEmpty(fill.Symbol) || fill.Quantity <= 0) continue;

                last ??= fill.FilledAt;
                first = fill.FilledAt;

                double signed = fill.Side == OrderSide.Buy ? fill.Quantity : -fill.Quantity;
                positions.TryGetValue(fill.Symbol, out double after);
                double before = after - signed;
                // Subtraction leaves dust where the broker had exactly zero.
                if (Math.Abs(before) < 1e-9 * Math.Max(1.0, fill.Quantity)) before = 0;

                if (before != 0 && Math.Sign(signed) != Math.Sign(before))
                {
                    closed++;
                    if (fill.RealizedPnL > 0) wins++;
                    realizedTotal += fill.RealizedPnL;
                }

                positions[fill.Symbol] = before;
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
