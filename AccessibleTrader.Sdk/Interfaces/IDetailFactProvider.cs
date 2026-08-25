using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Interfaces
{
    /// <summary>
    /// Provides high-detail contextual speech for a specific indicator at a given bar.
    /// Implement this interface alongside any indicator library adapter to supply
    /// rich narrative facts without coupling the logic to a specific provider class.
    /// Multiple implementations may be registered; the first non-empty result wins.
    /// </summary>
    public interface IDetailFactProvider
    {
        /// <summary>
        /// Returns a human-readable narrative describing the indicator's state at
        /// <paramref name="index"/>, or <c>null</c> / empty to fall through to the next provider.
        /// </summary>
        string? GetDetailFact(
            string code,
            ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults,
            int index,
            Dictionary<string, object> parameters);
    }
}
