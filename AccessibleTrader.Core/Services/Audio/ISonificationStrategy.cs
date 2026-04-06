using System;
using System.Linq;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Audio
{
    public interface ISonificationStrategy
    {
        AudioPoint CreateAudioPoint(ChartSeries series, ComponentConfig comp, double val, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, float chartVolume, double? prevVal = null);
        AudioPoint MapToAudio(ChartSeries series, int dataIndex, List<Ohlcv> data, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, float chartVolume);
        /// <summary>
        /// Maps a specific component at dataIndex to an AudioPoint.
        /// Unlike MapToAudio (which always picks the first visible component), this maps
        /// the component at <paramref name="componentIndex"/> so every component —
        /// including wicks — is sonified independently during playback.
        /// </summary>
        AudioPoint MapComponentToAudio(ChartSeries series, int componentIndex, int dataIndex, List<Ohlcv> data, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, float chartVolume);
    }
    
    public class DefaultSonificationStrategy : ISonificationStrategy
    {
        private readonly ISoundPatchRegistry _patchRegistry;

        public DefaultSonificationStrategy(ISoundPatchRegistry patchRegistry)
        {
            _patchRegistry = patchRegistry;
        }

        public AudioPoint CreateAudioPoint(ChartSeries series, ComponentConfig comp, double val, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, float chartVolume, double? prevVal = null)
        {
            if (double.IsNaN(val)) return new AudioPoint(0, 0, "sine", 0);

            // 1. DYNAMIC VOLUME & PANNING
            float baseVolume = comp.Volume * (series.IsMuted || comp.IsMuted || !series.IsVisible || !comp.IsVisible ? 0 : series.Volume) * chartVolume;
            
            double pan = 0;
            if (viewportWidth > 1)
            {
                pan = Math.Clamp((2.0 * (double)relativeIndex / (viewportWidth - 1)) - 1.0, -1.0, 1.0);
            }
            
            // 2. RANGE NORMALIZATION
            double rangeSpan = Math.Max(0.01, viewportRange.Max - viewportRange.Min);
            double normalizedValue = Math.Clamp((val - viewportRange.Min) / rangeSpan, 0, 1);
            
            // 3. PITCH MAPPING
            double freq = comp.BaseFrequency;
            if (comp.Role == ComponentRole.Wick || comp.DisplayType == ComponentDisplayType.Wick)
            {
                // Wicks use fixed tones regardless of PitchMapping so upper and lower are always
                // distinguishable: upper wick = 880 Hz (bright), lower wick = 220 Hz (deep).
                bool isUpperWick = comp.Name.Contains("Upper") || comp.Name.Contains("High");
                freq = isUpperWick ? 880.0 : 220.0;
                // FreqMultiplier still applies so users can tune per-component in Properties dialog.
                freq *= comp.FreqMultiplier;
            }
            else if (comp.PitchMapping == PitchMapping.Value)
            {
                freq = 200 + (normalizedValue * 800);
                freq *= comp.FreqMultiplier;
            }
            else if (comp.PitchMapping == PitchMapping.Direction || comp.PitchMapping == PitchMapping.PriceDirection)
            {
                // PriceDirection always uses candle direction.
                // Direction uses ReferenceLevel when set (non-zero) so value-anchored components
                // (e.g. Cipher B MF Wave anchored at −80) get the correct bullish/bearish split.
                bool isBullish = (comp.PitchMapping == PitchMapping.Direction
                                  && comp.ReferenceLevel.HasValue
                                  && comp.ReferenceLevel.Value != 0.0)
                    ? val >= comp.ReferenceLevel.Value
                    : point.Close >= point.Open;
                freq = isBullish ? comp.BullishFrequency : comp.BearishFrequency;
                freq *= comp.FreqMultiplier;
            }
            else if (comp.PitchMapping == PitchMapping.Price)
            {
                freq = 200 + (normalizedValue * 800);
                freq *= comp.FreqMultiplier;
            }
            else
            {
                freq *= comp.FreqMultiplier;
            }

            // 4. AMPLITUDE MAPPING
            float vol = baseVolume;
            if (comp.AmplitudeMapping == AmplitudeMapping.Absolute)
            {
                // Amplitude scales with |value| relative to the pane's symmetric peak.
                // Quiet near zero, loud when far from zero — mirrors ZeroArea fill intensity.
                double absMax = Math.Max(Math.Abs(viewportRange.Max), Math.Abs(viewportRange.Min));
                vol = (float)Math.Clamp(absMax > 0 ? Math.Abs(val) / absMax : 0.1, 0.05, 1.0) * baseVolume;
            }
            else if (comp.AmplitudeMapping == AmplitudeMapping.ReferenceDeviation)
            {
                // Amplitude scales with deviation from ReferenceLevel, not from zero.
                // Mirrors physical bar height: a bar drawn from −80 to −65 (height 15) and
                // a bar from −80 to −95 (height 15) produce the same loudness. A bar near
                // the reference level is quiet; a bar far from it is loud.
                //
                // DeviationNorm pins the denominator to the component's declared value range.
                // Without it, components whose value range is much smaller than the pane range
                // are nearly silent: Money Flow at −80±20 inside a −100..+100 WT pane would
                // compute maxDev=180, giving only 11% volume at full deviation.
                // With DeviationNorm=20, the denominator is exactly 20 → 100% at full deviation.
                double refLev    = comp.ReferenceLevel ?? 0.0;
                double deviation = Math.Abs(val - refLev);
                double maxDev    = comp.DeviationNorm ?? Math.Max(
                    Math.Abs(viewportRange.Max - refLev),
                    Math.Abs(viewportRange.Min - refLev));
                vol = (float)Math.Clamp(maxDev > 0 ? deviation / maxDev : 0.1, 0.05, 1.0) * baseVolume;
            }
            else if (comp.AmplitudeMapping == AmplitudeMapping.Size)
            {
                double size = Math.Abs(val);
                if (comp.Role == ComponentRole.Body || comp.Role == ComponentRole.PriceAction) size = Math.Abs(point.Close - point.Open);
                
                double absMax = Math.Max(Math.Abs(viewportRange.Max), Math.Abs(viewportRange.Min));
                vol = (float)Math.Clamp((absMax > 0 ? (size / absMax) : 0) * 2.0, 0.05, 1.0) * baseVolume;
            }
            else if (comp.AmplitudeMapping == AmplitudeMapping.DeltaFromPrice)
            {
                if (comp.DisplayType == ComponentDisplayType.Candle || comp.Role == ComponentRole.Body)
                {
                    // Body volume = body size as fraction of full bar range.
                    // Doji → quiet; marubozu → loud. Viewport-normalized so works at any price level.
                    double bodySize = Math.Abs(point.Close - point.Open);
                    double barRange = Math.Max((double)(point.High - point.Low), 1e-10);
                    vol = (float)Math.Clamp((bodySize / barRange) * 1.5 + 0.1, 0.1, 1.0) * baseVolume;
                }
                else
                {
                    // Wick volume = wick length as fraction of viewport price range.
                    bool isUpper = comp.Name.Contains("Upper") || comp.Name.Contains("High");
                    double bodyMax = Math.Max(point.Open, point.Close);
                    double bodyMin = Math.Min(point.Open, point.Close);
                    double wickSize = isUpper ? (point.High - bodyMax) : (bodyMin - point.Low);
                    vol = (float)Math.Clamp((wickSize / rangeSpan) * 4.0, 0.05, 1.0) * baseVolume;
                }
            }

            // 5. WAVEFORM SELECTION
            string wave = series.IsProfile ? "sawtooth" : comp.Waveform;
            if (!series.IsProfile && comp.ReferenceLevel.HasValue)
            {
                wave = (val >= comp.ReferenceLevel.Value) ? comp.AboveReferenceWaveform : comp.BelowReferenceWaveform;
            }

            // 6. BOUNDARY CLICKS
            // ReferenceLevel crossing (e.g. zero line): fires the primary click when the zero-line
            // level has PlayEarcon enabled, or when no zero-line level exists (backward compat).
            // Visible LevelConfig crossings: only fire when the level has PlayEarcon enabled.
            bool triggerClick = false;
            if (comp.TriggerBoundaryClick && prevVal.HasValue)
            {
                if (comp.ReferenceLevel.HasValue &&
                    ((prevVal.Value < comp.ReferenceLevel.Value && val >= comp.ReferenceLevel.Value) ||
                     (prevVal.Value >= comp.ReferenceLevel.Value && val < comp.ReferenceLevel.Value)))
                {
                    // Fire the reference-level click only when the matching "Zero" level has PlayEarcon,
                    // or when no "Zero" level exists in Config.Levels (preserves old behaviour).
                    // Also gate on the component's level subscription filter.
                    var zeroLevel = series.Config.Levels.FirstOrDefault(
                        l => l.Name.Contains("Zero", StringComparison.OrdinalIgnoreCase) ||
                             l.Name.Contains("Midpoint", StringComparison.OrdinalIgnoreCase));
                    bool zeroSubscribed = zeroLevel == null ||
                                         AudioZoneHelper.ComponentSubscribesTo(comp, zeroLevel.Name);
                    if (zeroSubscribed && (zeroLevel == null || zeroLevel.PlayEarcon))
                        triggerClick = true;
                }

                if (!triggerClick)
                {
                    foreach (var lc in series.Config.Levels)
                    {
                        // Gate on PlayEarcon — providers opt in per level.
                        if (!lc.IsVisible || !lc.PlayEarcon) continue;
                        // Gate on component's level subscription filter.
                        if (!AudioZoneHelper.ComponentSubscribesTo(comp, lc.Name)) continue;
                        if ((prevVal.Value < lc.Value && val >= lc.Value) ||
                            (prevVal.Value >= lc.Value && val < lc.Value))
                        {
                            triggerClick = true;
                            // TODO: lc.EarconVolume is available for scaled click volume — requires
                            // AudioPoint to carry a click-volume field (deferred to future phase).
                            break;
                        }
                    }
                }
            }

            // ── Dynamic OB/OS zone noise texturing ────────────────────────────────────────
            // Add noise when the value enters an overbought or oversold zone.
            // Zone thresholds and noise parameters come from series.Config.Levels (injected by
            // InjectDefaultLevels via IIndicatorProvider.GetDefaultLevels()).
            // Components can restrict which levels they respond to via SubscribedLevelNames.
            float noiseAmt = comp.NoiseAmount;
            string noiseType = "pink";
            if (comp.DisplayType is ComponentDisplayType.Oscillator or ComponentDisplayType.ZeroArea
                or ComponentDisplayType.Histogram or ComponentDisplayType.Line)
            {
                var (zoneNoise, zoneType) = AudioZoneHelper.ComputeZoneNoise(series, comp, val);
                if (zoneNoise > 0f)
                {
                    noiseAmt  = zoneNoise;
                    noiseType = zoneType;
                }
            }

            // ── SoundPatch ID resolution ─────────────────────────────────────────
            // When comp.SoundPatchId is set and the registry has the patch, propagate the
            // PatchId into the AudioPoint so AudioSequencer can apply decay and detuning.
            string? resolvedPatchId = null;
            if (!string.IsNullOrEmpty(comp.SoundPatchId) &&
                _patchRegistry.TryGetPatch(comp.SoundPatchId, out _))
            {
                resolvedPatchId = comp.SoundPatchId;
            }

            return new AudioPoint(Frequency: freq, Volume: vol, Waveform: wave, Pan: pan,
                                  EnvelopeType: comp.EnvelopeType, TriggerClick: triggerClick, NoiseAmount: noiseAmt,
                                  PatchId: resolvedPatchId, NoiseType: noiseType);
        }

        public AudioPoint MapToAudio(ChartSeries series, int dataIndex, List<Ohlcv> data, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, float chartVolume)
        {
            if (dataIndex < 0 || dataIndex >= data.Count) return new AudioPoint(0, 0, "sine", 0);

            var point = data[dataIndex];
            var comp = series.Components.FirstOrDefault(c => c.IsVisible && !c.IsMuted) ?? series.Components.FirstOrDefault();
            if (comp == null) return new AudioPoint(0, 0, "sine", 0);

            var compData = series.GetComponentData(comp.Name);
            double val = (dataIndex < compData.Length) ? compData[dataIndex] : point.Close;
            double? prevVal = (dataIndex > 0 && dataIndex - 1 < compData.Length) ? compData[dataIndex - 1] : null;

            return CreateAudioPoint(series, comp, val, point, relativeIndex, viewportWidth, viewportRange, chartVolume, prevVal);
        }

        public AudioPoint MapComponentToAudio(ChartSeries series, int componentIndex, int dataIndex, List<Ohlcv> data, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, float chartVolume)
        {
            if (dataIndex < 0 || dataIndex >= data.Count) return new AudioPoint(0, 0, "sine", 0);
            if (componentIndex < 0 || componentIndex >= series.Components.Count) return new AudioPoint(0, 0, "sine", 0);

            var point = data[dataIndex];
            var comp = series.Components[componentIndex];

            var compData = series.GetComponentData(comp.Name);
            double val;
            double? prevVal = null;

            if (compData.Length > dataIndex)
            {
                val = compData[dataIndex];
                prevVal = dataIndex > 0 && compData.Length > dataIndex - 1 ? compData[dataIndex - 1] : null;
            }
            else
            {
                // Fallback: read directly from OHLCV for price-mapped series (candles, volume).
                // comp.DataMapping gives us the field name.
                bool isPriceSeries = series.Id == "price" || series.Id == "candles"
                    || series.Id == "volume" || series.Pane == "Volume";
                if (!string.IsNullOrEmpty(comp.DataMapping))
                {
                    val = comp.DataMapping.ToLower() switch
                    {
                        "open"   => (double)point.Open,
                        "high"   => (double)point.High,
                        "low"    => (double)point.Low,
                        "close"  => (double)point.Close,
                        "volume" => (double)point.Volume,
                        _        => double.NaN
                    };
                }
                else if (isPriceSeries)
                    val = (double)point.Close;
                else
                    val = double.NaN;
            }

            if (double.IsNaN(val)) return new AudioPoint(0, 0, "sine", 0);
            return CreateAudioPoint(series, comp, val, point, relativeIndex, viewportWidth, viewportRange, chartVolume, prevVal);
        }
    }
}
