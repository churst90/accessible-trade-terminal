using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    public interface IIndicatorEngine
    {
        Task<Dictionary<string, double[]>> CalculateAsync(string code, IReadOnlyList<Ohlcv> data, Dictionary<string, object> parameters, CancellationToken ct);
        Task<Dictionary<string, double>> CalculateIncrementalAsync(string code, IReadOnlyList<Ohlcv> data, Dictionary<string, object> parameters, Dictionary<string, double[]> previousResults, CancellationToken ct);
        /// <summary>
        /// Returns the registered IIndicatorProvider whose metadata contains <paramref name="indicatorCode"/>.
        /// Returns null when not found (e.g. core series like candles have no provider).
        /// </summary>
        IIndicatorProvider? GetProvider(string indicatorCode);

        /// <summary>
        /// Full calculation that also surfaces dynamic zone bands written by the provider via
        /// <see cref="IIndicatorResultBuffer.WriteZoneBands"/>. The second element of the tuple is
        /// empty when the provider wrote no zone bands (most providers).
        /// </summary>
        Task<(Dictionary<string, double[]> Results, IReadOnlyList<ZoneBandConfig> ZoneBands)>
            CalculateWithBandsAsync(string code, IReadOnlyList<Ohlcv> data,
                Dictionary<string, object> parameters, CancellationToken ct);
    }

    public class IndicatorEngine : IIndicatorEngine
    {
        private readonly IIndicatorService _indicatorService;
        private readonly ICustomIndicatorRegistry _customRegistry;
        // Keep direct provider list for GetProvider lookup — IIndicatorService wraps them but doesn't expose them.
        private readonly List<IIndicatorProvider> _providers;

        public IndicatorEngine(IIndicatorService indicatorService, ICustomIndicatorRegistry customRegistry,
            IEnumerable<IIndicatorProvider> providers)
        {
            _indicatorService = indicatorService;
            _customRegistry = customRegistry;
            _providers = providers.ToList();
        }

        public IIndicatorProvider? GetProvider(string indicatorCode)
        {
            if (string.IsNullOrEmpty(indicatorCode)) return null;
            return _providers.FirstOrDefault(p =>
                p.GetIndicators().Any(m => m.Code.Equals(indicatorCode, StringComparison.OrdinalIgnoreCase)));
        }

        public async Task<Dictionary<string, double[]>> CalculateAsync(string code, IReadOnlyList<Ohlcv> data, Dictionary<string, object> parameters, CancellationToken ct)
        {
            var (results, _) = await CalculateWithBandsAsync(code, data, parameters, ct);
            return results;
        }

        public async Task<(Dictionary<string, double[]> Results, IReadOnlyList<ZoneBandConfig> ZoneBands)>
            CalculateWithBandsAsync(string code, IReadOnlyList<Ohlcv> data,
                Dictionary<string, object> parameters, CancellationToken ct)
        {
            // ── Custom (Roslyn/Pine-compiled) indicators take priority ────────────
            if (_customRegistry.TryGet(code, out var custom) && custom != null)
            {
                var results = await Task.Run(() =>
                {
                    var span = data.ToArray().AsSpan();
                    var p = parameters.ToDictionary(
                        kv => kv.Key,
                        kv => kv.Value is double d ? d : double.TryParse(kv.Value?.ToString(), out double pv) ? pv : 0.0);
                    var arrays = custom.Calculate(span, p);
                    var result = new Dictionary<string, double[]>();
                    for (int i = 0; i < custom.ComponentNames.Length && i < arrays.Length; i++)
                        result[custom.ComponentNames[i]] = arrays[i];
                    return result;
                }, ct);
                return (results, Array.Empty<ZoneBandConfig>());
            }

            return await Task.Run(() =>
            {
                var dict = new Dictionary<string, double[]>();
                var buffer = new IndicatorResultBuffer(dict, data.Count);
                _indicatorService.CalculateIndicator(code, data.ToArray(), parameters, buffer);
                // Seal the buffer after full Calculate() so any subsequent GetComponentSpan/SetValue
                // calls with unrecognised names throw ArgumentException instead of silently producing
                // NaN arrays.  This catches typos in provider component name constants at runtime.
                buffer.Seal();
                var zoneBands = buffer.ReadZoneBands(code);
                return (buffer.GetResults(), zoneBands);
            }, ct);
        }

        public async Task<Dictionary<string, double>> CalculateIncrementalAsync(string code, IReadOnlyList<Ohlcv> data, Dictionary<string, object> parameters, Dictionary<string, double[]> previousResults, CancellationToken ct)
        {
            // Custom indicators: recalculate last bar via full Calculate and take final values.
            if (_customRegistry.TryGet(code, out var custom) && custom != null)
            {
                return await Task.Run(() =>
                {
                    var span = data.ToArray().AsSpan();
                    var p = parameters.ToDictionary(
                        kv => kv.Key,
                        kv => kv.Value is double d ? d : double.TryParse(kv.Value?.ToString(), out double pv) ? pv : 0.0);
                    var arrays = custom.Calculate(span, p);
                    var lastValues = new Dictionary<string, double>();
                    for (int i = 0; i < custom.ComponentNames.Length && i < arrays.Length; i++)
                    {
                        var arr = arrays[i];
                        if (arr.Length > 0) lastValues[custom.ComponentNames[i]] = arr[^1];
                    }
                    return lastValues;
                }, ct);
            }

            return await Task.Run(() =>
            {
                // We use a temp buffer for just the last value.
                var resultsDict = new Dictionary<string, double[]>();
                var buffer = new IndicatorResultBuffer(resultsDict, data.Count);
                
                // Copy previous data if possible for the provider's context.
                // All component names are registered here — seal the buffer afterwards so that
                // UpdateLast() calling SetValue with a typo'd name throws instead of silently
                // producing a disconnected NaN array.
                foreach (var kvp in previousResults)
                {
                    var span = buffer.GetComponentSpan(kvp.Key);
                    if (kvp.Value.Length < span.Length)
                        Array.Copy(kvp.Value, 0, resultsDict[kvp.Key], 0, kvp.Value.Length);
                }
                buffer.Seal();

                _indicatorService.UpdateLast(code, data.ToArray(), parameters, buffer);
                
                var lastValues = new Dictionary<string, double>();
                foreach (var kvp in resultsDict)
                {
                    if (kvp.Value.Length > 0)
                        lastValues[kvp.Key] = kvp.Value[^1];
                }
                return lastValues;
            }, ct);
        }
    }
}

