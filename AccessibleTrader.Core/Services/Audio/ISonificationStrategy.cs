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

        /// <summary>
        /// How many playback voice slots a component needs: the layer count of the largest multi-oscillator
        /// user patch assigned to it (base/bullish/bearish), or 1 plus an aux slot when it uses a gradient
        /// blend or a detuned built-in patch. Used to size the playback voice plan.
        /// </summary>
        int ResolveComponentVoiceCount(ComponentConfig comp);
    }
    
    public class DefaultSonificationStrategy : ISonificationStrategy
    {
        private readonly ISoundPatchRegistry _patchRegistry;
        private readonly ISoundPatchLibrary? _patchLibrary;

        public DefaultSonificationStrategy(ISoundPatchRegistry patchRegistry, ISoundPatchLibrary? patchLibrary = null)
        {
            _patchRegistry = patchRegistry;
            _patchLibrary = patchLibrary;
        }

        public AudioPoint CreateAudioPoint(ChartSeries series, ComponentConfig comp, double val, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, float chartVolume, double? prevVal = null)
        {
            if (double.IsNaN(val)) return new AudioPoint(0, 0, "sine", 0);

            // 1. DYNAMIC VOLUME & PANNING
            float baseVolume = comp.Volume * (series.IsMuted || comp.IsMuted || !series.IsVisible || !comp.IsVisible ? 0 : series.Volume) * chartVolume;
            
            double pan = AudioConstants.CalculatePan(relativeIndex, viewportWidth);
            
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

            // A cross fires a directional earcon: the value's movement between prevVal and val (which
            // sit on opposite sides of the crossed level) IS the cross direction — up if it rose, down
            // if it fell. Inherits triggerClick's PlayEarcon / subscription gating.
            int crossDir = (triggerClick && prevVal.HasValue) ? Math.Sign(val - prevVal.Value) : 0;

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
                // Take the stronger of the component's base texture and the zone texture. Replacing
                // (the old behaviour) could REDUCE roughness on entering a zone when the component's
                // base noise was higher than the zone's — the opposite of the intent, which is that
                // overbought/oversold zones sound rougher than the clean mid-range.
                if (zoneNoise > noiseAmt)
                {
                    noiseAmt  = zoneNoise;
                    noiseType = zoneType;
                }
            }

            // ── SoundPatch resolution (per-colour, live-linked) ──────────────────
            // The bar's colour selects the bullish (green: close >= open) or bearish (red) patch
            // when one is assigned; otherwise the component's single SoundPatchId applies. A
            // registry (built-in bell) patch propagates its ID so AudioSequencer applies decay /
            // detune / gradient. A user Sound Designer patch is resolved live from the library and
            // overrides the timbre here (waveform + noise + envelope) — so editing that patch in the
            // Sound Designer changes every component referencing it, with no snapshot to re-sync.
            // Sign-coloured components (histograms, zero-line dots/areas, or any polarity-coloured
            // component) are green/red by their own value vs ColorBaseline; candles/bars are green/red
            // by the price bar's direction (close vs open).
            bool barBullish = (comp.UsePolarityColoring
                    || comp.DisplayType is ComponentDisplayType.Histogram
                        or ComponentDisplayType.ZeroDot or ComponentDisplayType.ZeroArea)
                ? val >= comp.ColorBaseline
                : point.Close >= point.Open;
            string? effPatchId = barBullish ? comp.BullishSoundPatchId : comp.BearishSoundPatchId;
            if (string.IsNullOrEmpty(effPatchId)) effPatchId = comp.SoundPatchId;

            string? resolvedPatchId = null;
            string effEnvelope = comp.EnvelopeType;
            IReadOnlyList<OscillatorLayer>? patchLayers = null;
            if (!string.IsNullOrEmpty(effPatchId))
            {
                if (_patchRegistry.TryGetPatch(effPatchId, out _))
                {
                    resolvedPatchId = effPatchId;
                }
                else
                {
                    var libPatch = _patchLibrary?.GetPatch(effPatchId);
                    if (libPatch != null)
                    {
                        // Primary layer drives the single-voice fields (waveform + noise + envelope).
                        var layers = libPatch.EffectiveLayers();
                        var layer0 = layers[0];
                        wave = layer0.Waveform;
                        if (!string.IsNullOrEmpty(libPatch.EnvelopeType)) effEnvelope = libPatch.EnvelopeType;
                        if (layer0.NoiseAmount > 0f)
                        {
                            noiseAmt  = layer0.NoiseAmount;
                            noiseType = string.IsNullOrEmpty(layer0.NoiseType) ? noiseType : layer0.NoiseType;
                        }
                        // Multi-oscillator patch: carry the full layer list so the playback/nav paths
                        // render one voice per layer. Single-layer patches leave this null (the fields
                        // above already carry them), avoiding a per-bar allocation on the hot path.
                        if (layers.Count > 1) patchLayers = layers;
                    }
                }
            }

            return new AudioPoint(Frequency: freq, Volume: vol, Waveform: wave, Pan: pan,
                                  EnvelopeType: effEnvelope, TriggerClick: triggerClick, NoiseAmount: noiseAmt,
                                  PatchId: resolvedPatchId, NoiseType: noiseType, PatchLayers: patchLayers,
                                  CrossDirection: crossDir);
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

        public int ResolveComponentVoiceCount(ComponentConfig comp)
        {
            int layers = Math.Max(UserPatchLayerCount(comp.SoundPatchId),
                Math.Max(UserPatchLayerCount(comp.BullishSoundPatchId), UserPatchLayerCount(comp.BearishSoundPatchId)));
            if (layers > 1) return layers;   // multi-oscillator user patch: one voice per layer
            return 1 + (comp.UsesGradientSpeech || IsDetunedBuiltin(comp) ? 1 : 0);
        }

        // A user-library patch's layer count; 1 for a built-in (registry) patch, empty id, or unknown.
        private int UserPatchLayerCount(string? id)
        {
            if (string.IsNullOrEmpty(id) || _patchRegistry.TryGetPatch(id, out _)) return 1;
            var p = _patchLibrary?.GetPatch(id);
            return p != null ? Math.Max(1, p.EffectiveLayers().Count) : 1;
        }

        private bool IsDetunedBuiltin(ComponentConfig comp)
        {
            return Detuned(comp.SoundPatchId) || Detuned(comp.BullishSoundPatchId) || Detuned(comp.BearishSoundPatchId);
            bool Detuned(string? id) => !string.IsNullOrEmpty(id) && _patchRegistry.TryGetPatch(id, out var p) && p.IsDetuned;
        }
    }
}
