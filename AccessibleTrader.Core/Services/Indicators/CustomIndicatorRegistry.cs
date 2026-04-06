using System.Collections.Concurrent;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Stores ICustomIndicator instances compiled at runtime (via Roslyn or Pine transpiler).
    /// Keyed by indicator ID so IndicatorEngine can route CalculateAsync to the compiled instance
    /// instead of falling through to IIndicatorService.
    /// Thread-safe: ConcurrentDictionary handles concurrent compiles.
    /// </summary>
    public interface ICustomIndicatorRegistry
    {
        void Register(ICustomIndicator indicator);
        bool TryGet(string indicatorId, out ICustomIndicator? indicator);
        void Unregister(string indicatorId);
        IReadOnlyCollection<ICustomIndicator> GetAll();
    }

    public class CustomIndicatorRegistry : ICustomIndicatorRegistry
    {
        private readonly ConcurrentDictionary<string, ICustomIndicator> _indicators =
            new(System.StringComparer.OrdinalIgnoreCase);

        public void Register(ICustomIndicator indicator)
            => _indicators[indicator.Id] = indicator;

        public bool TryGet(string indicatorId, out ICustomIndicator? indicator)
            => _indicators.TryGetValue(indicatorId, out indicator);

        public void Unregister(string indicatorId)
            => _indicators.TryRemove(indicatorId, out _);

        public IReadOnlyCollection<ICustomIndicator> GetAll()
            => (IReadOnlyCollection<ICustomIndicator>)_indicators.Values;
    }
}
