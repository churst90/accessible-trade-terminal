using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>One slice of an N-window walk-forward.</summary>
    public sealed record LabWindowResult(
        int Index, DateTime Start, DateTime End,
        int Trades, double AverageR, double WinRate, double ProfitFactor, double MaxDrawdown);

    /// <summary>One strategy's H1/H2 comparison row (the battery view).</summary>
    public sealed record LabComparisonRow(
        string SpecId, string Name,
        int TradesH1, double AvgRH1, double CiLoH1,
        int TradesH2, double AvgRH2, double CiLoH2,
        bool Survivor)
    {
        /// <summary>Rank key: era robustness first — the WORSE half's CI lower bound.
        /// A strategy is only as good as its weaker regime.</summary>
        public double RankKey => Math.Min(CiLoH1, CiLoH2);
    }

    public interface ILabRunner
    {
        /// <summary>
        /// N-window chronological walk-forward for one spec over the loaded data:
        /// slice the range into equal windows, backtest each independently, and
        /// report per-window stability — the in-app version of the research
        /// harness's walk-windows command.
        /// </summary>
        Task<IReadOnlyList<LabWindowResult>> RunWindowsAsync(
            string specId, IReadOnlyList<Ohlcv> data, WorkspaceState state,
            int windows, BacktestConfig baseConfig,
            IProgress<string>? progress = null, CancellationToken ct = default);

        /// <summary>
        /// Battery comparison: every provided spec backtested on the FIRST and
        /// SECOND half of the loaded data. SURVIVOR = the research harness's gate —
        /// the 95% bootstrap CI lower bound on trade R is positive in BOTH halves
        /// with at least <see cref="LabRunner.MinTradesPerHalf"/> trades each. Rows come back
        /// ranked by the weaker half's CI lower bound (era robustness first).
        /// </summary>
        Task<IReadOnlyList<LabComparisonRow>> CompareAsync(
            IReadOnlyList<(string Id, string Name)> specs,
            IReadOnlyList<Ohlcv> data, WorkspaceState state, BacktestConfig baseConfig,
            IProgress<string>? progress = null, CancellationToken ct = default);
    }

    public sealed class LabRunner : ILabRunner
    {
        /// <summary>The survivor gate's minimum sample per half — below this, any CI is noise.</summary>
        public const int MinTradesPerHalf = 5;

        private readonly IStrategyModalCoordinator _coordinator;

        public LabRunner(IStrategyModalCoordinator coordinator) => _coordinator = coordinator;

        public async Task<IReadOnlyList<LabWindowResult>> RunWindowsAsync(
            string specId, IReadOnlyList<Ohlcv> data, WorkspaceState state,
            int windows, BacktestConfig baseConfig,
            IProgress<string>? progress = null, CancellationToken ct = default)
        {
            if (data == null || data.Count < windows * 2)
                return Array.Empty<LabWindowResult>();
            windows = Math.Clamp(windows, 2, 12);

            var results = new List<LabWindowResult>(windows);
            var first = data[0].Date;
            var last = data[^1].Date;
            var step = TimeSpan.FromTicks((last - first).Ticks / windows);

            for (int i = 0; i < windows; i++)
            {
                ct.ThrowIfCancellationRequested();
                var start = first + TimeSpan.FromTicks(step.Ticks * i);
                var end = i == windows - 1 ? last : first + TimeSpan.FromTicks(step.Ticks * (i + 1));
                progress?.Report($"Window {i + 1} of {windows}: {start:MMM yyyy} to {end:MMM yyyy}…");

                var config = baseConfig with { StartDate = start, EndDate = end };
                var (result, _) = await _coordinator.RunBacktestAsync(specId, data, config, state)
                    .ConfigureAwait(false);
                if (result == null) continue;

                results.Add(new LabWindowResult(
                    i + 1, start, end,
                    result.Metrics.TotalSignals,
                    result.AverageR,
                    result.Metrics.WinRate,
                    result.ProfitFactor,
                    result.Metrics.MaxDrawdown));
            }
            return results;
        }

        public async Task<IReadOnlyList<LabComparisonRow>> CompareAsync(
            IReadOnlyList<(string Id, string Name)> specs,
            IReadOnlyList<Ohlcv> data, WorkspaceState state, BacktestConfig baseConfig,
            IProgress<string>? progress = null, CancellationToken ct = default)
        {
            if (data == null || data.Count < 4 || specs.Count == 0)
                return Array.Empty<LabComparisonRow>();

            int mid = (data.Count - 1) / 2;
            var h1 = (Start: data[0].Date, End: data[mid].Date);
            var h2 = (Start: data[mid].Date, End: data[^1].Date);

            var rows = new List<LabComparisonRow>(specs.Count);
            for (int i = 0; i < specs.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (id, name) = specs[i];
                progress?.Report($"Testing {i + 1} of {specs.Count}: {name}…");

                var (r1, _) = await _coordinator.RunBacktestAsync(id, data,
                    baseConfig with { StartDate = h1.Start, EndDate = h1.End }, state).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                var (r2, _) = await _coordinator.RunBacktestAsync(id, data,
                    baseConfig with { StartDate = h2.Start, EndDate = h2.End }, state).ConfigureAwait(false);
                if (r1 == null || r2 == null) continue;

                var (ciLo1, avgR1, n1) = HalfStats(r1);
                var (ciLo2, avgR2, n2) = HalfStats(r2);
                bool survivor = n1 >= MinTradesPerHalf && n2 >= MinTradesPerHalf
                                && ciLo1 > 0 && ciLo2 > 0;

                rows.Add(new LabComparisonRow(id, name, n1, avgR1, ciLo1, n2, avgR2, ciLo2, survivor));
            }

            return rows.OrderByDescending(r => r.Survivor)
                       .ThenByDescending(r => r.RankKey)
                       .ToList();
        }

        private static (double CiLo, double AvgR, int Count) HalfStats(BacktestResult result)
        {
            var rs = BootstrapCi.ExtractRs(result.Trades);
            if (rs.Count == 0) return (double.NegativeInfinity, double.NaN, 0);
            var (lo, mean, _) = BootstrapCi.Compute(rs);
            return (lo, mean, rs.Count);
        }
    }
}
