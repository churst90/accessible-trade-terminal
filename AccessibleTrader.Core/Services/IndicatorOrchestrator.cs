using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Accessibility;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    public interface IIndicatorOrchestrator
    {
        Task RecalculateAllAsync(IReadOnlyList<Ohlcv> data, IEnumerable<ChartSeries> series, CancellationToken ct, List<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)>? historicalBooks = null);
        Task RecalculateLastAsync(IReadOnlyList<Ohlcv> data, IEnumerable<ChartSeries> series, CancellationToken ct, List<OrderBookEntry>? lastBids = null, List<OrderBookEntry>? lastAsks = null);
    }

    /// <summary>
    /// Computes technical indicators and specialized series (Heatmap, Profiles).
    /// SURGICAL: Dispatches UpdateSeriesDataAction to update data buffers without touching UI config.
    /// </summary>
    public class IndicatorOrchestrator : IIndicatorOrchestrator
    {
        private readonly IIndicatorEngine _engine;
        private readonly IIndicatorStateMapper _mapper;
        private readonly IDrawingService _drawingService;
        private readonly IProfileService _profileService;
        private readonly IHeatmapService _heatmapService;
        private readonly IWorkspaceStore _store;
        private readonly INotificationHub _notificationHub;
        private readonly ILogger<IndicatorOrchestrator> _logger;

        public IndicatorOrchestrator(
            IIndicatorEngine engine,
            IIndicatorStateMapper mapper,
            IDrawingService drawingService,
            IProfileService profileService,
            IHeatmapService heatmapService,
            IWorkspaceStore store,
            INotificationHub notificationHub,
            ILogger<IndicatorOrchestrator> logger)
        {
            _engine = engine;
            _mapper = mapper;
            _drawingService = drawingService;
            _profileService = profileService;
            _heatmapService = heatmapService;
            _store = store;
            _notificationHub = notificationHub;
            _logger = logger;
        }

        public async Task RecalculateAllAsync(IReadOnlyList<Ohlcv> data, IEnumerable<ChartSeries> series, CancellationToken ct, List<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)>? historicalBooks = null)
        {
            bool hasData = data != null && data.Any();
            var state = _store.State;

            foreach (var s in series)
            {
                if (ct.IsCancellationRequested) return;
                
                SeriesDataBuffer buffer = new SeriesDataBuffer { SeriesId = s.Id };
                string codeUpper = (s.IndicatorCode ?? "").ToUpperInvariant();

                if (!hasData || data == null)
                {
                    // Dispatch empty buffer
                    _store.Dispatch(new UpdateSeriesDataAction(s.Id, buffer));
                    continue;
                }

                // Identify indicator type by component display type or model flags
                bool isHeatmap = s.Components.Any(c => c.DisplayType == ComponentDisplayType.Heatmap);
                bool isProfile = s.IsProfile || s.Components.Any(c => c.DisplayType == ComponentDisplayType.Profile || c.DisplayType == ComponentDisplayType.Distribution);

                // 1. HEATMAP
                if (isHeatmap)
                {
                    // PHASE 4: Ensure historical order books are used if available
                    buffer.HeatmapData = _heatmapService.GenerateHeatmap(data, historicalBooks ?? new(), 50.0);
                }
                // 2. PROFILE (VPVR, VPFR, TPO)
                else if (isProfile)
                {
                    var profileData = data;
                    if (codeUpper == "VPVR" || codeUpper == "TPO" || !codeUpper.Contains("FIXED"))
                    {
                        int start = Math.Clamp(state.ViewportStartIndex, 0, data.Count - 1);
                        int length = Math.Clamp(state.ViewportLength, 1, data.Count - start);
                        profileData = data.Skip(start).Take(length).ToList();
                    }
                    
                    if (profileData.Any())
                    {
                        // Null-coalesce: some profile providers return null on edge-case inputs.
                        // An empty list is safe; the speech formatter and renderer both check Count.
                        buffer.ProfileBins = (codeUpper switch {
                            "TPO" => _profileService.CalculateMarketProfile(profileData),
                            _ => _profileService.CalculateVolumeProfile(profileData)
                        }) ?? new List<ProfileBin>();
                    }
                }
                // 3. DRAWINGS
                else if (s.IsDrawing && s.Drawing != null)
                {
                    var drawingData = _drawingService.CalculateDrawingData(s.Drawing, data);
                    buffer.ComponentData = drawingData;
                }
                // 4. INTERNAL MAPPING (CANDLES/VOLUME)
                else if (s.Components.Any(c => !string.IsNullOrEmpty(c.DataMapping)))
                {
                    buffer = _mapper.MapInternalDataToBuffer(s, data);
                }
                // 5. EXTERNAL INDICATORS (SKENDER)
                else if (!string.IsNullOrEmpty(s.IndicatorCode))
                {
                    try
                    {
                        var parameters = s.Parameters.ToDictionary(k => k.Key, v => (object)v.Value);

                        // ── Adaptive parameter detection ─────────────────────────────────────
                        // IAdaptiveIndicatorProvider (e.g. Cipher S) may inspect the loaded data
                        // and return better parameter values — e.g. auto-detected cycle window.
                        // Only fires on full historical recalculations (RecalculateAllAsync),
                        // never on tick updates (RecalculateLastAsync / RecalculateSeriesFullAsync).
                        var adaptiveProvider = _engine.GetProvider(s.IndicatorCode);
                        if (adaptiveProvider is IAdaptiveIndicatorProvider adaptive)
                        {
                            var suggested = adaptive.SuggestParameters(
                                data.ToArray().AsSpan(),
                                state.Identity.Symbol,
                                parameters,
                                out string? message);

                            if (suggested != null)
                            {
                                // Apply to local dict so this calculation uses the detected values.
                                foreach (var kv in suggested)
                                    parameters[kv.Key] = kv.Value;

                                // Persist to store so the parameter panel reflects the new values.
                                var doubleUpdates = suggested.ToDictionary(
                                    kv => kv.Key,
                                    kv => kv.Value is double d ? d : Convert.ToDouble(kv.Value));
                                _store.Dispatch(new UpdateSeriesParametersAction(s.Id, doubleUpdates));

                                // Announce only when the provider flagged it as a significant change.
                                if (message != null)
                                    _notificationHub.NotifyInfo(message, interrupt: false);
                            }
                        }

                        var (results, zoneBands) = await _engine.CalculateWithBandsAsync(s.IndicatorCode, data, parameters, ct);
                        buffer = _mapper.MapResultsToBuffer(s, results, data.Count);
                        // Apply dynamic zone bands if the provider wrote any via buffer.WriteZoneBands().
                        if (zoneBands.Count > 0)
                            _store.Dispatch(new UpdateSeriesZoneBandsAction(s.Id, zoneBands));
                        // GAP 2: Validate that every written buffer key matches a declared component Name.
                        // A mismatch means the provider used a typo key — its data will be silently ignored.
                        ValidateBufferKeys(s, results, _engine.GetProvider(s.IndicatorCode));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to calculate indicator {Code}", s.IndicatorCode);
                        buffer = _mapper.MapResultsToBuffer(s, null!, data.Count);
                    }
                }

                _store.Dispatch(new UpdateSeriesDataAction(s.Id, buffer));
            }
        }

        public async Task RecalculateLastAsync(IReadOnlyList<Ohlcv> data, IEnumerable<ChartSeries> series, CancellationToken ct, List<OrderBookEntry>? lastBids = null, List<OrderBookEntry>? lastAsks = null)
        {
            if (data == null || !data.Any()) return;

            foreach (var s in series)
            {
                if (ct.IsCancellationRequested) return;
                string codeUpper = (s.IndicatorCode ?? "").ToUpperInvariant();

                // Clone existing buffer for incremental update
                var buffer = s.Data.Clone();

                bool isHeatmap = s.Components.Any(c => c.DisplayType == ComponentDisplayType.Heatmap);
                bool isProfile = s.IsProfile || s.Components.Any(c => c.DisplayType == ComponentDisplayType.Profile || c.DisplayType == ComponentDisplayType.Distribution);

                if (isHeatmap)
                {
                    var lastBarBins = _heatmapService.GenerateBarHeatmap(data[^1], lastBids ?? new(), lastAsks ?? new(), 50.0);
                    if (data.Count > buffer.HeatmapData.Count)
                    {
                        // New bar added — always append (even if empty, as a placeholder).
                        buffer.HeatmapData.Add(lastBarBins);
                    }
                    else if (buffer.HeatmapData.Count > 0 && lastBarBins.Count > 0)
                    {
                        // Only overwrite the last bar if we actually have new order-book data.
                        // An empty lastBarBins means GetOrderBookAsync returned nothing this tick;
                        // keep the existing (non-empty) snapshot rather than resetting it to empty.
                        buffer.HeatmapData[^1] = lastBarBins;
                    }

                    _store.Dispatch(new UpdateSeriesDataAction(s.Id, buffer));
                    continue;
                }

                // Skip full profile/drawing recalculation on every tick for performance
                if (isProfile || s.IsDrawing) continue;

                // Pivot-based indicators that write to historical bar indices need a full
                // recalculation rather than the scalar incremental update.
                if (s.RequiresFullRecalcOnTick)
                {
                    await RecalculateSeriesFullAsync(s, data, ct);
                    continue;
                }

                if (!string.IsNullOrEmpty(s.IndicatorCode))
                {
                    try
                    {
                        var parameters = s.Parameters.ToDictionary(k => k.Key, v => (object)v.Value);
                        var results = await _engine.CalculateIncrementalAsync(s.IndicatorCode, data, parameters, buffer.ComponentData, ct);

                        foreach (var kvp in results)
                        {
                            if (buffer.ComponentData.TryGetValue(kvp.Key, out var arr))
                            {
                                if (data.Count > arr.Length)
                                {
                                    var newArr = new double[data.Count];
                                    Array.Fill(newArr, double.NaN);  // NaN = no data; prevents zero-valued slots from firing signals
                                    Array.Copy(arr, newArr, arr.Length);
                                    newArr[^1] = kvp.Value;
                                    buffer.ComponentData[kvp.Key] = newArr;
                                }
                                else
                                {
                                    arr[^1] = kvp.Value;
                                }
                            }
                        }
                        _store.Dispatch(new UpdateSeriesDataAction(s.Id, buffer));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Incremental calculation failed for {Code}", s.IndicatorCode);
                    }
                }
            }
        }

        /// <summary>
        /// Validates that every key written to the buffer by a provider has a matching component Name.
        /// Companion array keys (suffixed with "_color" or "_touches") are intentionally undeclared and
        /// are skipped. A warning is logged for any other unmatched key so plugin authors catch typos early.
        /// </summary>
        private void ValidateBufferKeys(ChartSeries series, Dictionary<string, double[]> results, IIndicatorProvider? provider)
        {
#if DEBUG
            if (provider == null || results == null) return;
            var knownNames = new HashSet<string>(series.Components.Select(c => c.Name), StringComparer.Ordinal);
            string providerName = provider.Name;
            foreach (var key in results.Keys)
            {
                // Skip intentional companion arrays: GradientDot color arrays and touch-count arrays.
                if (key.EndsWith("_color", StringComparison.Ordinal) ||
                    key.EndsWith("_touches", StringComparison.Ordinal) ||
                    key.StartsWith("__", StringComparison.Ordinal))
                    continue;
                if (!knownNames.Contains(key))
                {
                    _logger.LogWarning(
                        "[{Provider}] wrote buffer key '{Key}' for indicator '{Code}' but no component with that Name exists in the series. " +
                        "Data will be silently ignored. Check that buffer.Write() key matches the component Name in metadata.",
                        providerName, key, series.IndicatorCode);
                }
            }
#endif
        }

        private async Task RecalculateSeriesFullAsync(ChartSeries s, IReadOnlyList<Ohlcv> data, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(s.IndicatorCode)) return;
            try
            {
                var parameters = s.Parameters.ToDictionary(k => k.Key, v => (object)v.Value);
                var (results, zoneBands) = await _engine.CalculateWithBandsAsync(s.IndicatorCode, data, parameters, ct);
                var buffer = _mapper.MapResultsToBuffer(s, results, data.Count);
                _store.Dispatch(new UpdateSeriesDataAction(s.Id, buffer));
                // Apply dynamic zone bands if the provider wrote any via buffer.WriteZoneBands().
                if (zoneBands.Count > 0)
                    _store.Dispatch(new UpdateSeriesZoneBandsAction(s.Id, zoneBands));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Full recalculation failed for {Code}", s.IndicatorCode);
            }
        }
    }
}
