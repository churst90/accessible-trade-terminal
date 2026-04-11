using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Core.Services.Indicators;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    public interface IIndicatorService
    {
        /// <summary>
        /// Scans the Plugins/Indicators/ directory for dynamically-loaded indicator
        /// providers and merges them with the DI-registered providers. Safe to call
        /// multiple times — duplicate codes are skipped.
        /// </summary>
        void LoadIndicatorPlugins(IPluginLoaderService pluginLoader);

        List<IndicatorMetadata> GetAvailableIndicators();
        void CalculateIndicator(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer);
        void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer);
        int GetStabilityWindow(string code, Dictionary<string, object> parameters);
        string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters);
    }

    public class IndicatorService : IIndicatorService
    {
        private readonly List<IIndicatorProvider> _providers;
        private readonly ILogger<IndicatorService> _logger;
        private bool _pluginsLoaded;

        public IndicatorService(IEnumerable<IIndicatorProvider> providers, ILogger<IndicatorService> logger)
        {
            _providers = providers.ToList();
            _logger = logger;
        }

        public void LoadIndicatorPlugins(IPluginLoaderService pluginLoader)
        {
            if (_pluginsLoaded) return;
            _pluginsLoaded = true;

            // Collect existing codes so we skip duplicates.
            var existingCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in _providers)
                foreach (var meta in p.GetIndicators())
                    existingCodes.Add(meta.Code);

            int loaded = 0;

            // Scan the built-in Plugins/Indicators/ directory.
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var dirs = new[]
            {
                Path.Combine(baseDir, "Plugins", "Indicators"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AccessibleTrader", "Plugins", "Indicators")
            };

            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;

                try
                {
                    var discovered = pluginLoader.LoadPlugins<IIndicatorProvider>(dir).ToList();
                    foreach (var provider in discovered)
                    {
                        // Skip if any of this provider's codes conflict with an already-registered provider.
                        var codes = provider.GetIndicators().Select(m => m.Code).ToList();
                        if (codes.Any(c => existingCodes.Contains(c)))
                        {
                            _logger.LogWarning("Skipping indicator plugin '{Name}' — duplicate code(s): {Codes}.",
                                provider.Name, string.Join(", ", codes.Where(existingCodes.Contains)));
                            continue;
                        }

                        _providers.Add(provider);
                        foreach (var c in codes) existingCodes.Add(c);
                        loaded++;
                        _logger.LogInformation("Loaded indicator plugin '{Name}' with {Count} indicator(s).",
                            provider.Name, codes.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading indicator plugins from {Directory}.", dir);
                }
            }

            if (loaded > 0)
                _logger.LogInformation("Loaded {Count} indicator plugin(s) from disk.", loaded);
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
