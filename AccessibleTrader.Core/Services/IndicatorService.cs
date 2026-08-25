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
                // PlatformPaths, not GetFolderPath: the latter returns an empty string on Unix
                // when the target does not exist, and the resulting RELATIVE path would scan
                // whatever directory the process happens to be running from for loadable DLLs.
                // Deliberately MACHINE-level, not per-user (IPlatformPathService): these are
                // executable plugins, and a hosted account must not be able to drop code into a
                // directory the server loads.
                Path.Combine(PlatformPaths.AppDataRoot(), "Plugins", "Indicators")
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
                all.AddRange(p.GetIndicators().Where(m => IsComputable(m, p)));
            }

            return all.OrderBy(x => x.Category).ThenBy(x => x.Name).ToList();
        }

        /// <summary>
        /// Whether an indicator can actually produce a value, so one that cannot is never offered.
        /// The rule itself lives in <see cref="Indicators.IndicatorComputability"/> because
        /// <c>SignalCatalog</c> has to apply exactly the same one to strategy leaves — for years it
        /// did not, and PPO, HV, TMA, ZLEMA and EOM were unusable on a chart while remaining
        /// pickable in the strategy builder. This method keeps only the logging.
        ///
        /// <para>
        /// Filtering here rather than deleting five metadata blocks keeps the answer tied to what
        /// the library actually exposes: upgrade Skender and anything it gained appears by itself,
        /// and nothing can quietly return to the menu without being able to compute. A menu entry
        /// that can never produce a value is worse than an absent one — the user spends their time
        /// on it and has nothing to report but "it does not work".
        /// </para>
        /// </summary>
        private bool IsComputable(IndicatorMetadata meta, IIndicatorProvider provider)
        {
            bool ok = Indicators.IndicatorComputability.IsComputable(provider, meta);
            if (!ok && _unresolvable.Add(meta.Code))
                _logger?.LogWarning(
                    "Indicator '{Code}' ({Name}) is not offered: Skender exposes no Get{Method}, so it " +
                    "could only ever render an empty line.",
                    meta.Code, meta.Name, Indicators.SkenderCalculationCore.SkenderMethodName(meta.Code));
            return ok;
        }

        // Warn once per code, not once per menu build.
        private readonly HashSet<string> _unresolvable = new(StringComparer.OrdinalIgnoreCase);

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
