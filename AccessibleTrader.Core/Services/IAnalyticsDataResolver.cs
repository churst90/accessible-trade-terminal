using AccessibleTrader.Core.Services.Indicators;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Describes an analytics metric that the resolver can provide.
    /// </summary>
    public sealed record AnalyticsMetricDescriptor(
        string Metric,
        string DisplayName,
        string Category,
        string[] SupportedAssets,
        string[] AvailableProviders);

    /// <summary>
    /// Maps analytics metric requests to the best available provider based on
    /// API key availability and provider priority. Cross-series indicators and
    /// strategy conditions use this instead of hardcoding provider names, so
    /// adding a new provider automatically improves coverage without touching
    /// every consumer.
    /// </summary>
    public interface IAnalyticsDataResolver
    {
        /// <summary>
        /// Resolves the best <see cref="CrossSeriesRequest"/> for a given metric
        /// and optional asset. Returns null if no provider can serve the metric.
        /// Prefers free/no-key providers, then falls back to key-requiring providers
        /// that are configured.
        /// </summary>
        CrossSeriesRequest? Resolve(string metric, string? asset = null);

        /// <summary>
        /// Returns descriptors for all known metrics, including which providers
        /// can serve each one and which assets are supported.
        /// </summary>
        IReadOnlyList<AnalyticsMetricDescriptor> GetAvailableMetrics();

        /// <summary>
        /// Returns true if at least one provider can currently serve the metric
        /// (i.e. it's free or has a configured API key).
        /// </summary>
        bool IsMetricAvailable(string metric);
    }
}
