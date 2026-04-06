using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Core.Services.Indicators;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    public interface IIndicatorService
    {
        List<IndicatorMetadata> GetAvailableIndicators();
        void CalculateIndicator(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer);
        void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer);
        int GetStabilityWindow(string code, Dictionary<string, object> parameters);
        string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters);
    }

    public class IndicatorService : IIndicatorService
    {
        private readonly List<IIndicatorProvider> _providers;

        public IndicatorService(IEnumerable<IIndicatorProvider> providers)
        {
            _providers = providers.ToList();
        }

        public List<IndicatorMetadata> GetAvailableIndicators()
        {
            var all = new List<IndicatorMetadata>();
            foreach (var p in _providers)
            {
                all.AddRange(p.GetIndicators());
            }

            return all.OrderBy(x => x.Category).ThenBy(x => x.Name).ToList();
        }

        public void CalculateIndicator(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            foreach (var provider in _providers)
            {
                var indicators = provider.GetIndicators();
                if (indicators.Any(i => i.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                {
                    provider.Calculate(code, data, parameters, buffer);
                    return;
                }
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            foreach (var provider in _providers)
            {
                var indicators = provider.GetIndicators();
                if (indicators.Any(i => i.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                {
                    provider.UpdateLast(code, data, parameters, buffer);
                    return;
                }
            }
        }

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            foreach (var provider in _providers)
            {
                var indicators = provider.GetIndicators();
                if (indicators.Any(i => i.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                {
                    return provider.GetStabilityWindow(code, parameters);
                }
            }
            return 200;
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters)
        {
            foreach (var provider in _providers)
            {
                var indicators = provider.GetIndicators();
                if (indicators.Any(i => i.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                {
                    return provider.GetDetailFact(code, data, calculatedResults, index, parameters);
                }
            }
            return string.Empty;
        }
    }
}
