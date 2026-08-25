using AccessibleTrader.Sdk.Screening;

namespace AccessibleTrader.Core.Services.Screening
{
    /// <summary>Per-symbol progress so a long screen can be narrated as it runs.</summary>
    /// <param name="Completed">Symbols finished so far.</param>
    /// <param name="Total">Symbols in the run.</param>
    /// <param name="Symbol">The symbol that just completed.</param>
    public record ScreenerProgress(int Completed, int Total, string Symbol);

    /// <summary>
    /// Evaluates a <see cref="ScreenerSpec"/> across many symbols. Implementations fetch bars
    /// per symbol, compute exactly the indicators the spec references, and evaluate the spec's
    /// condition tree against each symbol's most recent closed bar.
    /// </summary>
    public interface IScreenerService
    {
        /// <summary>
        /// Runs the screen. Every entry produces exactly one row — symbols that failed to fetch
        /// or lack history are reported with a non-Evaluated status rather than dropped, so the
        /// caller can distinguish "nothing qualified" from "we never looked".
        /// </summary>
        Task<ScreenerRunResult> RunAsync(
            ScreenerSpec spec,
            IReadOnlyList<WatchlistEntry> entries,
            IProgress<ScreenerProgress>? progress = null,
            CancellationToken ct = default);
    }
}
