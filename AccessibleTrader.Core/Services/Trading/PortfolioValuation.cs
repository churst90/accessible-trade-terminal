using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Core.Services.Trading
{
    /// <summary>One asset's contribution to the account, or why it has none we can state.</summary>
    /// <param name="Unpriced">
    /// Non-null when no price could be established. The row still appears with its
    /// quantity — a holding you cannot value is not a holding you do not have, and
    /// dropping it would quietly shrink the account.
    /// </param>
    public sealed record AssetValuation(
        string  Asset,
        double  Quantity,
        double? Price,
        double? Value,
        double? DayChangePct,
        string? Unpriced)
    {
        public bool IsPriced => Unpriced is null;
    }

    /// <summary>
    /// The account at a glance: what it is worth, how it is split, and how honest
    /// that total is.
    /// </summary>
    /// <param name="PricedCount">
    /// How many assets the total actually covers. Reported beside the total
    /// because a figure that silently omits two holdings is worse than no figure —
    /// it is a number the user will act on.
    /// </param>
    public sealed record PortfolioSnapshot(
        IReadOnlyList<AssetValuation> Assets,
        double TotalValue,
        string QuoteAsset,
        int PricedCount,
        int TotalCount)
    {
        public bool IsComplete => PricedCount == TotalCount;

        /// <summary>The largest priced holding, for the spoken summary.</summary>
        public AssetValuation? Largest =>
            Assets.Where(a => a.IsPriced).OrderByDescending(a => a.Value ?? 0).FirstOrDefault();
    }

    /// <summary>
    /// Where a price comes from. Separated so the valuation arithmetic — which is
    /// what decides the number a user reads — is testable without a network, a
    /// provider, or a symbol convention.
    /// </summary>
    public interface IAssetPriceSource
    {
        /// <summary>
        /// Latest close and the previous session's close for <paramref name="asset"/>
        /// priced in <paramref name="quote"/>, or null when the pair cannot be
        /// resolved or fetched. Both come from the same daily bars, so the day
        /// change costs nothing extra.
        /// </summary>
        Task<(double Price, double? PreviousClose)?> TryGetDailyAsync(
            string provider, string asset, string quote, CancellationToken ct = default);
    }

    /// <summary>
    /// Turns a list of balances into a portfolio.
    ///
    /// <para>
    /// The Balances tab showed asset, free and locked — quantities with no value,
    /// no total, no allocation and no day change. Those are the numbers that answer
    /// "how am I doing", and they were the ones missing.
    /// </para>
    ///
    /// <para>
    /// **The rule this is built around: an unpriceable asset is never counted as
    /// zero.** It keeps its row, it is marked, and the total says how many assets
    /// it covers. Silent omission is the failure mode that makes a portfolio total
    /// dangerous rather than merely wrong.
    /// </para>
    /// </summary>
    public sealed class PortfolioValuationService
    {
        private readonly IAssetPriceSource _prices;

        public PortfolioValuationService(IAssetPriceSource prices) => _prices = prices;

        public async Task<PortfolioSnapshot> ValueAsync(
            string provider,
            IReadOnlyList<Balance> balances,
            string quoteAsset = "USD",
            CancellationToken ct = default)
        {
            var held = balances
                .Where(b => Math.Abs(b.Free) + Math.Abs(b.Locked) > 1e-12)
                .ToList();

            var rows = new List<AssetValuation>(held.Count);

            foreach (var b in held)
            {
                double qty = b.Free + b.Locked;

                // Cash is worth its face value in its own currency. Pricing USD in
                // USD through a market lookup would fail and mark real money
                // unpriced.
                if (string.Equals(b.Asset, quoteAsset, StringComparison.OrdinalIgnoreCase))
                {
                    rows.Add(new AssetValuation(b.Asset, qty, 1.0, qty, null, null));
                    continue;
                }

                try
                {
                    var quote = await _prices.TryGetDailyAsync(provider, b.Asset, quoteAsset, ct)
                                             .ConfigureAwait(false);
                    if (quote is null)
                    {
                        rows.Add(new AssetValuation(b.Asset, qty, null, null, null,
                            $"no {b.Asset}/{quoteAsset} market on {provider}"));
                        continue;
                    }

                    double price = quote.Value.Price;
                    double? change = quote.Value.PreviousClose is > 0
                        ? (price - quote.Value.PreviousClose.Value) / quote.Value.PreviousClose.Value * 100.0
                        : null;

                    rows.Add(new AssetValuation(b.Asset, qty, price, qty * price, change, null));
                }
                catch (Exception ex)
                {
                    // A thrown lookup is not a zero holding either.
                    rows.Add(new AssetValuation(b.Asset, qty, null, null, null,
                        $"price lookup failed: {ex.Message}"));
                }
            }

            // Order by value so the spoken summary leads with what matters, with
            // unpriced rows last rather than interleaved at zero.
            var ordered = rows
                .OrderByDescending(r => r.IsPriced)
                .ThenByDescending(r => r.Value ?? 0)
                .ToList();

            return new PortfolioSnapshot(
                ordered,
                ordered.Where(r => r.IsPriced).Sum(r => r.Value ?? 0),
                quoteAsset,
                ordered.Count(r => r.IsPriced),
                ordered.Count);
        }

        /// <summary>
        /// This asset's share of the priced total, or null when it has no price —
        /// never 0%, which would read as "worth nothing" rather than "unknown".
        /// </summary>
        public static double? AllocationPercent(AssetValuation a, PortfolioSnapshot snap) =>
            a.IsPriced && snap.TotalValue > 0 ? (a.Value ?? 0) / snap.TotalValue * 100.0 : null;

        /// <summary>
        /// The one-sentence summary spoken when the Balances tab takes focus. Says
        /// the coverage out loud whenever the total is partial, because that is the
        /// moment it matters.
        /// </summary>
        public static string Summarise(PortfolioSnapshot snap)
        {
            if (snap.TotalCount == 0) return "No balances.";

            string total = $"Portfolio {snap.TotalValue.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} {snap.QuoteAsset}";
            string cover = snap.IsComplete
                ? $" across {snap.TotalCount} {(snap.TotalCount == 1 ? "asset" : "assets")}."
                : $" across {snap.PricedCount} of {snap.TotalCount} assets — "
                + string.Join(", ", snap.Assets.Where(a => !a.IsPriced).Select(a => a.Asset))
                + " could not be priced.";

            var largest = snap.Largest;
            string lead = "";
            if (largest is not null && snap.TotalValue > 0)
            {
                double pct = (largest.Value ?? 0) / snap.TotalValue * 100.0;
                lead = $" Largest {largest.Asset}, {pct.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} percent.";
            }

            return total + cover + lead;
        }
    }
}
