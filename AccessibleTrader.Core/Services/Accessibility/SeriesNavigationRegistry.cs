using System;
using System.Linq;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Central registry for resolving the appropriate navigation strategy for a given series.
    /// </summary>
    public interface ISeriesNavigationRegistry
    {
        /// <summary>
        /// Resolves the strategy for the specified series.
        /// </summary>
        INavigationStrategy GetStrategy(ChartSeries? series);
    }

    /// <summary>
    /// Default implementation of the series navigation registry.
    /// </summary>
    public class SeriesNavigationRegistry : ISeriesNavigationRegistry
    {
        private readonly PointNavigationStrategy _pointStrategy;
        private readonly BinnedNavigationStrategy _binnedStrategy;

        public SeriesNavigationRegistry(PointNavigationStrategy pointStrategy, BinnedNavigationStrategy binnedStrategy)
        {
            _pointStrategy = pointStrategy;
            _binnedStrategy = binnedStrategy;
        }

        public INavigationStrategy GetStrategy(ChartSeries? series)
        {
            if (series == null) return _pointStrategy;

            bool isBinned = series.IsProfile || series.Components.Any(c => c.DisplayType == ComponentDisplayType.Heatmap);
            return isBinned ? _binnedStrategy : _pointStrategy;
        }
    }
}
