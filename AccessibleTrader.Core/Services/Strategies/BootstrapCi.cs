using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

// Moved from AccessibleTrader.StrategyLab (2026-07-17): the in-app Lab tab
// needs the same survivor gate (95% bootstrap CI on trade R-multiples) the
// research harness uses — one implementation, one definition of "survivor".
namespace AccessibleTrader.Core.Services.Strategies;

/// <summary>
/// Bootstrap confidence interval helper for R-multiple expectancy.
///
/// Tier 1 step 1 of the validation roadmap: every reported expectancy needs a CI alongside it
/// so we can answer "is the mean reliably > 0" rather than "is the point estimate > 0". The
/// pass criterion for any candidate signal is that the lower bound of the 95% CI is positive
/// in BOTH halves of the walk-forward — that's a much stronger claim than a single positive
/// point estimate, and it's the only honest way to distinguish a real edge from sampling noise
/// on a couple of dozen trades.
///
/// Method: classical non-parametric bootstrap. Resample the trade R-list with replacement N
/// times (default 10,000), take the mean of each resample, then return the 2.5th and 97.5th
/// percentiles of those means. Deterministic seed so identical inputs reproduce identical CIs
/// across runs (important when comparing harness output session-to-session).
/// </summary>
public static class BootstrapCi
{
    /// <summary>
    /// Compute the per-trade R multiple from a closed BacktestTrade. Returns NaN if the trade
    /// is missing data needed for the calculation (open trades, no stop recorded, no exit, etc.)
    /// — callers should filter NaNs before bootstrapping.
    ///
    /// R = (exitPrice - entryPrice) / |entryPrice - stopPrice|, sign-flipped for shorts so a
    /// winning short gives a positive R.
    /// </summary>
    public static double TradeR(BacktestTrade t)
    {
        if (t.ExitPrice is not double exit) return double.NaN;
        if (t.StopPrice is not double stop) return double.NaN;
        var risk = Math.Abs(t.EntryPrice - stop);
        if (risk <= 0 || double.IsNaN(risk) || double.IsInfinity(risk)) return double.NaN;
        var raw = (exit - t.EntryPrice) / risk;
        return t.Side == OrderSide.Sell ? -raw : raw;
    }

    /// <summary>
    /// One R per POSITION, in the order positions first appear. Rows sharing a non-zero
    /// <see cref="BacktestTrade.PositionId"/> are the exits of one entry and are scored together by
    /// <see cref="PositionR"/>; a row with PositionId 0 (hand-built, or from before the field
    /// existed) is scored on its own by <see cref="TradeR"/>, exactly as it always was.
    ///
    /// <para>Until 2026-10-18 this returned one R per exit ROW. A take-profit ladder emits a row
    /// per rung, so a two-rung winner was two samples and a whole-position loser one, and the
    /// rungs after a breakeven move carried a zero-risk stop and fell out entirely.</para>
    /// </summary>
    public static List<double> ExtractRs(IReadOnlyList<BacktestTrade> trades)
    {
        var rs = new List<double>(trades.Count);
        var groups = new Dictionary<int, List<BacktestTrade>>();
        var order = new List<object>(trades.Count);   // a row (PositionId 0) or a group key
        foreach (var t in trades)
        {
            if (t.PositionId == 0) { order.Add(t); continue; }
            if (!groups.TryGetValue(t.PositionId, out var g))
            {
                groups[t.PositionId] = g = new List<BacktestTrade>();
                order.Add(t.PositionId);
            }
            g.Add(t);
        }
        foreach (var item in order)
        {
            var r = item is BacktestTrade row ? TradeR(row) : PositionR(groups[(int)item]);
            if (!double.IsNaN(r) && !double.IsInfinity(r)) rs.Add(r);
        }
        return rs;
    }

    /// <summary>
    /// The R of one position from its exit rows: Σ quantity × (exit − entry) ÷ (Σ quantity ×
    /// (entry − initial stop)), sign-flipped for shorts — i.e. Σ portion × (exit − entry) ÷ initial
    /// risk, the definition <c>docs/SETUP_GRADER_PREREG.md</c> §2.2 sets. The stop is the rows'
    /// <see cref="BacktestTrade.StopPrice"/>, which the backtester stamps with the stop the position
    /// was OPENED with. NaN when the position is still open, has no stop, or its stop is not on the
    /// losing side of the entry (an entry that filled through its own stop has no defined risk).
    /// </summary>
    public static double PositionR(IReadOnlyList<BacktestTrade> rows)
    {
        if (rows.Count == 0) return double.NaN;
        var first = rows[0];
        if (first.StopPrice is not double stop) return double.NaN;
        double risk = first.Side == OrderSide.Sell ? stop - first.EntryPrice : first.EntryPrice - stop;
        if (!(risk > 0) || double.IsInfinity(risk)) return double.NaN;

        double qty = 0, gain = 0;
        foreach (var t in rows)
        {
            if (t.ExitPrice is not double exit) return double.NaN;
            double perUnit = first.Side == OrderSide.Sell ? first.EntryPrice - exit : exit - first.EntryPrice;
            gain += t.Quantity * perUnit;
            qty += t.Quantity;
        }
        return qty > 0 ? gain / (qty * risk) : double.NaN;
    }

    /// <summary>
    /// Resample the R list with replacement <paramref name="iterations"/> times, return the
    /// 2.5th and 97.5th percentiles of the resample means alongside the point estimate.
    /// </summary>
    /// <param name="seed">Deterministic seed (default 42) so reruns reproduce.</param>
    public static (double Lo, double Mean, double Hi) Compute(
        IReadOnlyList<double> rs,
        int iterations = 10_000,
        int seed = 42)
    {
        if (rs.Count == 0) return (double.NaN, double.NaN, double.NaN);
        if (rs.Count == 1) return (rs[0], rs[0], rs[0]);

        double pointMean = 0;
        for (int i = 0; i < rs.Count; i++) pointMean += rs[i];
        pointMean /= rs.Count;

        var rng = new Random(seed);
        var means = new double[iterations];
        int n = rs.Count;
        for (int it = 0; it < iterations; it++)
        {
            double sum = 0;
            for (int j = 0; j < n; j++) sum += rs[rng.Next(n)];
            means[it] = sum / n;
        }
        Array.Sort(means);
        int loIdx = (int)Math.Floor(0.025 * (iterations - 1));
        int hiIdx = (int)Math.Ceiling(0.975 * (iterations - 1));
        return (means[loIdx], pointMean, means[hiIdx]);
    }

    /// <summary>
    /// Convenience: extract Rs from a BacktestResult and return the 95% CI in one call.
    /// </summary>
    public static (double Lo, double Mean, double Hi) FromResult(BacktestResult result)
    {
        var rs = ExtractRs(result.Trades);
        return Compute(rs);
    }
}
