using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Audio
{
    public interface INavigationSonifier
    {
        void SyncNavigationSlots(WorkspaceState state);
        void PlayNote(double freq, double dur, string wave, float vol, float pan, double delay = 0);
        /// <summary>
        /// Plays a whole <see cref="AccessibleTrader.Sdk.Models.SoundPatch"/> — every oscillator layer,
        /// with its envelope and noise blend/colour — as one-shot voices. Used by the Sound Designer
        /// preview and by earcon overrides. Unlike <see cref="PlayNote"/> the envelope and noise
        /// actually reach the engine, and multi-oscillator patches sound all their layers.
        /// </summary>
        void PlayPatch(AccessibleTrader.Sdk.Models.SoundPatch patch, float volumeScale = 1f, float pan = 0f);
        void SonifySeries(ChartSeries series, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, int dataIndex, float masterVolume = 1.0f, double durationSeconds = 0.2, double delayMilliseconds = 0);
        void SonifyComponent(ChartSeries series, int componentIndex, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, int dataIndex, float masterVolume = 1.0f, double durationSeconds = 0.2, double delayMilliseconds = 0);
        void SonifyProfile(ChartSeries series, int binIndex, float masterVolume = 1.0f);
        void SonifyHeatmap(ChartSeries series, int dataIndex, int binIndex, float masterVolume = 1.0f);
        AudioPoint CreateAudioPoint(ChartSeries series, int componentIndex, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, int dataIndex, float masterVolume = 1.0f, double? overrideValue = null);
        void StopNavigationVoice();
        void SetMasterGain(float gain);
        void Silence();
        /// <summary>
        /// Scans marker-type components with non-NaN values at <paramref name="dataIndex"/>.
        /// Fires each as a distinct audio tick on slots 3–7 with 100ms gaps, in significance order.
        /// The focused series/component (slot 0) is excluded from cluster re-firing.
        /// Zone line components (IsZoneLine=true) are excluded — zone proximity is handled via speech.
        /// Fire-and-forget: does not block main navigation response.
        /// When <paramref name="crossSeriesMode"/> is false (navigation): only the focused series is scanned.
        /// When <paramref name="crossSeriesMode"/> is true (playback): all visible series are scanned.
        /// </summary>
        Task FireClusterTicksAsync(WorkspaceState state, int dataIndex, string excludeSeriesId, int excludeComponentIndex, bool crossSeriesMode = false);
    }

    public class NavigationSonifier : INavigationSonifier
    {
        private readonly IAudioDriver _audioDriver;
        private readonly ISonificationStrategy _strategy;
        private readonly ISoundPatchRegistry _patchRegistry;

        // ── Voice Slot Layout (64-voice polyphonic engine) ──────────────────────
        // Slots  0– 7 : Navigation — current bar/component (SyncNavigationSlots uses slot 0)
        // Slots  8–15 : Reserved for future multi-component navigation layering
        // Slots 16–31 : UI earcons — round-robin via PlayNote (modulo 16)
        // Slots 32–63 : Playback sequencer (AudioSequencer, PlaybackSlotOffset = 32)
        // ────────────────────────────────────────────────────────────────────────
        private const int SLOT_NAV_START = 0;
        private const int SLOT_UI_START = 16;
        private int _uiSlotCounter;

        public NavigationSonifier(IAudioDriver audioDriver, ISonificationStrategy strategy, ISoundPatchRegistry patchRegistry,
            ISoundPatchLibrary? patchLibrary = null)
        {
            _audioDriver = audioDriver;
            _strategy = strategy;
            _patchRegistry = patchRegistry;
            _patchLibrary = patchLibrary;
        }

        // Optional: user patch library for level-cue earcon overrides (null in minimal tests).
        private readonly ISoundPatchLibrary? _patchLibrary;

        /// <summary>
        /// Resolves the navigation Ping duration for a component, applying patch DefaultDecayMs
        /// when a SoundPatchId is set and comp.DecayMs is not explicitly set.
        /// </summary>
        private double ResolveNavPingDuration(AccessibleTrader.Sdk.Models.ComponentConfig comp, AudioPoint audioPt)
        {
            // Component-level DecayMs wins.
            if (comp.DecayMs.HasValue)
                return comp.DecayMs.Value / 1000.0;

            // Patch default decay.
            if (!string.IsNullOrEmpty(audioPt.PatchId) &&
                _patchRegistry.TryGetPatch(audioPt.PatchId, out var patch))
                return patch.DefaultDecayMs / 1000.0;

            // Existing navigation default (0.15s).
            return 0.15;
        }

        public void SyncNavigationSlots(WorkspaceState state)
        {
            int idx = state.CurrentDataIndex;
            if (idx < 0 || idx >= state.Data.Count) return;

            string seriesId = state.FocusedSeriesId ?? "candles";
            var series = state.ActiveSeries.FirstOrDefault(s => s.Id == seriesId);
            if (series == null || !series.IsVisible || series.IsMuted)
            {
                MuteAllNavigationSlots();
                return;
            }

            // Exclusive Focus for Distributions: If we are navigating a Heatmap or Profile,
            // we inhibit all other series oscillators and use specialized mapping.
            bool isHeatmap = series.Components.Any(c => c.DisplayType == ComponentDisplayType.Heatmap);
            bool isProfile = series.IsProfile || series.Components.Any(c => c.DisplayType == ComponentDisplayType.Profile || c.DisplayType == ComponentDisplayType.Distribution);

            if (isHeatmap)
            {
                SonifyHeatmap(series, idx, state.FocusedBinIndex, state.ChartVolume);
                return;
            }
            if (isProfile)
            {
                SonifyProfile(series, state.FocusedBinIndex, state.ChartVolume);
                return;
            }

            int cIdx = Math.Clamp(state.FocusedComponentIndex, 0, series.Components.Count - 1);

            // REACTIVE PANNING: Pan must match the visual bar positions.
            //   • At live edge — canvas shows data in `effectiveWindow` slots plus empty
            //     right-margin slots. Pan denominator = effectiveWindow so audio +1.0 lands
            //     on the last real bar (matching its visual position).
            //   • Panned back into history — data fills all viewportLength slots, no margin.
            //     Pan denominator = viewportLength so audio tracks the full canvas.
            int effectivePanWidth = AudioConstants.ComputePanWidth(state);
            float pan = (float)AudioConstants.CalculatePan(state.CurrentDataIndex - state.ViewportStartIndex, effectivePanWidth);

            // ── Cloud component: width-mapped volume, bullish/bearish pitch ────────
            var cloudComp = (cIdx >= 0 && cIdx < series.Components.Count) ? series.Components[cIdx] : null;
            if (cloudComp != null && cloudComp.DisplayType == ComponentDisplayType.Cloud)
            {
                SonifyCloudNavigation(series, cloudComp, idx, state, pan);
                return;
            }

            // Use the sub-pane range when the focused component lives in a sub-pane (e.g. MF Wave
            // in "Pane_CIPHER_B/MF"). Without this, raw MF values get normalised against the main
            // pane's ±100 WT range and clamp to two tones instead of a continuous pitch sweep.
            var focusedComp = (cIdx >= 0 && cIdx < series.Components.Count) ? series.Components[cIdx] : null;
            string rangeKey = !string.IsNullOrEmpty(focusedComp?.SubPaneName)
                ? $"{series.Pane}/{focusedComp.SubPaneName}"
                : series.Pane;
            var range = state.PaneRanges.TryGetValue(rangeKey, out var r)
                ? r
                : (state.PaneRanges.TryGetValue(series.Pane, out var pr) ? pr : state.ViewportRange);

            // When Heikin-Ashi is active, transform the raw bar so that pitch/direction
            // reflect the HA close/open values (which match the visual candle colours).
            Ohlcv navPoint = state.Data[idx];
            if (state.IsHeikinAshi && state.Data.Count > 1)
            {
                var rawSlice = new List<Ohlcv>(idx + 1);
                for (int i = 0; i <= idx; i++) rawSlice.Add(state.Data[i]);
                var haData = ChartMath.CalculateHeikinAshi(rawSlice);
                if (haData.Count > 0) navPoint = haData[^1];
            }

            var audioPt = CreateAudioPoint(series, cIdx, navPoint, idx - state.ViewportStartIndex, effectivePanWidth, range, idx, state.ChartVolume);

            // ── NaN guard for marker components ─────────────────────────────────
            // When a Ping-envelope (marker) component has no signal on this bar (value is NaN),
            // CreateAudioPoint returns volume=0. Calling SetVoice with volume=0 on a Ping can still
            // produce a click artifact on some audio drivers. Silence the slot explicitly instead.
            // Line/oscillator components with NaN (before warmup) are left to play at near-zero
            // amplitude — silence there is expected and no click is audible.
            if (string.Equals(audioPt.EnvelopeType, "Ping", StringComparison.OrdinalIgnoreCase) &&
                audioPt.Volume <= 0 &&
                focusedComp != null && AudioConstants.MarkerDisplayTypes.Contains(focusedComp.DisplayType))
            {
                _audioDriver.StopVoice(SLOT_NAV_START);
                for (int i = 2; i < 8; i++) _audioDriver.StopVoice(SLOT_NAV_START + i);
                return;
            }

            // ── Dynamic noise texturing ──────────────────────────────────────────
            // NoiseAmount is computed once inside CreateAudioPoint via AudioZoneHelper
            // (called by ISonificationStrategy.CreateAudioPoint) and stored on audioPt.
            // Pass audioPt.NoiseAmount directly to every SetVoice call below — no
            // duplicate recomputation needed here.

            // Gradient timbre blend: gradient-ribbon dots (e.g. WT Momentum) represent color with
            // waveform blending rather than pitch, since these dots sit at a fixed price position —
            // varying pitch like an oscillator would be misleading.
            // Carrier (slot 0): sine at fixed frequency (the component's DefaultBaseFrequency, e.g. 440 Hz).
            // Blend (slot 1): triangle for bullish/teal side, sawtooth for bearish/red side.
            // Blend volume scales linearly with WT1 oscillator strength (0 = neutral → pure sine only;
            // 100 = max strength → 65% blend waveform on top of sine carrier).
            bool isGradient = focusedComp != null && focusedComp.UsesGradientSpeech;
            float gradientBlendFraction = 0f;
            string gradientBlendWave = "triangle";
            if (isGradient)
            {
                var colorData = series.GetComponentData(focusedComp!.Name + "_color");
                if (colorData != null && idx < colorData.Length && !double.IsNaN(colorData[idx]))
                {
                    double wt1Val = colorData[idx];
                    gradientBlendFraction = (float)(Math.Clamp(Math.Abs(wt1Val), 0, 100) / 100.0);
                    gradientBlendWave = wt1Val >= 0 ? "triangle" : "sawtooth";
                }
            }

            // Use Slot 0 for the focused point.
            // Ping (dots, wicks, signal markers): 0.15s self-terminating transient (or patch DecayMs) — crisp per bar.
            // Sustain (oscillators, ZeroArea waves, lines): 0.45s self-terminating note — long enough
            // to convey oscillator pitch and glide feel during arrow-key hold (each bar replaces the
            // previous note), but always self-terminates so Home/End/PageUp/PageDown never leave a
            // stuck drone. continuous=false for all navigation voices; continuous=true is for playback only.
            var focusedCompForNav = (cIdx >= 0 && cIdx < series.Components.Count) ? series.Components[cIdx] : null;
            // Volume reads as a short "tick"/ping under MANUAL navigation (it stays a continuous bed
            // only during playback), so arrow-stepping bars doesn't hold a sustained drone under the price.
            string navEnvelope = (focusedCompForNav?.Role == ComponentRole.Volume) ? "Ping" : audioPt.EnvelopeType;
            bool isPing = string.Equals(navEnvelope, "Ping", StringComparison.OrdinalIgnoreCase);
            double navDuration = isPing
                ? (focusedCompForNav != null ? ResolveNavPingDuration(focusedCompForNav, audioPt) : 0.15)
                : 0.45;
            // Grit-carrying pings need longer decays: the sub-octave sawtooth that
            // encodes size sits an octave below the fundamental, and a 0.15s ping is
            // gone before a low-frequency texture registers. Volume bars (brown noise
            // + grit) get 0.40s; wicks (grit ∝ length) get 0.25s. An explicit
            // component DecayMs still wins.
            if (isPing && focusedCompForNav != null && !focusedCompForNav.DecayMs.HasValue)
            {
                if (focusedCompForNav.Role == ComponentRole.Volume)
                    navDuration = 0.40;
                else if (focusedCompForNav.Role == ComponentRole.Wick
                         || focusedCompForNav.DisplayType == ComponentDisplayType.Wick)
                    navDuration = 0.25;
            }

            if (isGradient)
            {
                // Slot 0: sine carrier at fixed base frequency.
                _audioDriver.SetVoice(SLOT_NAV_START, audioPt.Frequency, audioPt.Volume, pan, "sine", false, navDuration, idx, audioPt.EnvelopeType, false, audioPt.NoiseAmount, audioPt.NoiseType);
                // Slot 1: blend waveform at strength-scaled volume (max 65% of carrier).
                float blendVol = audioPt.Volume * gradientBlendFraction * 0.65f;
                if (blendVol > 0.01f)
                    _audioDriver.SetVoice(SLOT_NAV_START + 1, audioPt.Frequency, blendVol, pan, gradientBlendWave, false, navDuration, idx, audioPt.EnvelopeType, false, 0f);
            }
            else if (audioPt.PatchLayers != null)
            {
                // Multi-oscillator user patch: layer 0 on the main nav slot, extra layers on the free
                // navigation-layering slots (8-15). Each self-terminates (continuous=false), so leftover
                // slots from a previous, deeper patch simply decay — no drone.
                var layers = audioPt.PatchLayers;
                for (int li = 0; li < layers.Count; li++)
                {
                    int navSlot = li == 0 ? SLOT_NAV_START : SLOT_NAV_START + 7 + li; // 8,9,10,...
                    if (navSlot > SLOT_UI_START - 1) break;                            // stay within 0-15
                    var L = layers[li];
                    // Layer 0 carries the stronger of the patch's own noise and the computed
                    // zone noise (OB/OS texturing) — a user patch must not silence the zone cue.
                    float layerNoise = li == 0
                        ? Math.Max(Math.Max(0f, L.NoiseAmount), audioPt.NoiseAmount)
                        : Math.Max(0f, L.NoiseAmount);
                    string layerNoiseType = li == 0 && audioPt.NoiseAmount > L.NoiseAmount
                        ? audioPt.NoiseType
                        : (string.IsNullOrEmpty(L.NoiseType) ? "pink" : L.NoiseType);
                    _audioDriver.SetVoice(navSlot, audioPt.Frequency * L.FreqRatio,
                        Math.Clamp(audioPt.Volume * L.Gain, 0f, 1f), pan, L.Waveform, false, navDuration,
                        li == 0 ? idx : -1, audioPt.EnvelopeType, li == 0 && audioPt.TriggerClick,
                        layerNoise, layerNoiseType);
                }
            }
            else
            {
                _audioDriver.SetVoice(SLOT_NAV_START, audioPt.Frequency, audioPt.Volume, pan, audioPt.Waveform, false, navDuration, idx, navEnvelope, audioPt.TriggerClick, audioPt.NoiseAmount, audioPt.NoiseType, audioPt.SquareMix, audioPt.SawMix, audioPt.TriangleMix, audioPt.SubSawMix);

                // Detuned pair bell: fire second voice on Slot 1 at patch offset.
                if (isPing && focusedCompForNav != null &&
                    !string.IsNullOrEmpty(audioPt.PatchId) &&
                    _patchRegistry.TryGetPatch(audioPt.PatchId, out var navPatch) &&
                    navPatch.IsDetuned)
                {
                    double detunedFreq = audioPt.Frequency + navPatch.DetuneIntervalHz;
                    if (navPatch.DetunedOffsetMs <= 0)
                    {
                        _audioDriver.SetVoice(SLOT_NAV_START + 1, detunedFreq, audioPt.Volume, pan, audioPt.Waveform, false, navDuration, idx, "Ping", false, audioPt.NoiseAmount, audioPt.NoiseType);
                    }
                    else
                    {
                        _ = Task.Delay(navPatch.DetunedOffsetMs).ContinueWith(_ =>
                            _audioDriver.SetVoice(SLOT_NAV_START + 1, detunedFreq, audioPt.Volume, pan, audioPt.Waveform, false, navDuration, idx, "Ping", false, audioPt.NoiseAmount, audioPt.NoiseType),
                            TaskScheduler.Default);
                    }
                }
            }

            // Directional cross earcon: fires when the focused component's value crossed a
            // reference / OB / OS level between the previous bar and this one — so it sounds whether
            // you arrowed forward or backward onto the cross bar.
            if (audioPt.CrossDirection != 0
                && !EarconPatchPlayer.TryPlayOverride(_patchLibrary, _audioDriver,
                    audioPt.CrossDirection > 0 ? EarconPatchPlayer.CrossUpKey : EarconPatchPlayer.CrossDownKey,
                    1f, pan))
                CrossEarcon.Fire(_audioDriver, audioPt.CrossDirection, 1f, pan);

            for (int i = 2; i < 8; i++) _audioDriver.StopVoice(SLOT_NAV_START + i);
        }

        /// <summary>
        /// Sonifies a Cloud component during manual navigation. Produces a soft two-voice
        /// tone: sine carrier (slot 0) + quiet triangle blend (slot 1). Volume is proportional
        /// to cloud width (|upper - lower|) normalized against the viewport maximum. Pitch
        /// switches between BullishFrequency and BearishFrequency based on cloud direction.
        /// </summary>
        private void SonifyCloudNavigation(ChartSeries series, ComponentConfig comp, int dataIndex, WorkspaceState state, float pan)
        {
            // Read the cloud width from the component's own data (signed: positive=bullish, negative=bearish).
            var widthData = series.GetComponentData(comp.Name);
            if (widthData == null || widthData.Length == 0 || dataIndex < 0 || dataIndex >= widthData.Length)
            {
                MuteAllNavigationSlots();
                return;
            }

            double signedWidth = widthData[dataIndex];
            if (double.IsNaN(signedWidth))
            {
                MuteAllNavigationSlots();
                return;
            }

            bool isBullish = signedWidth >= 0;
            double absWidth = Math.Abs(signedWidth);

            // Normalize width against viewport maximum for volume mapping.
            double maxWidth = 0;
            int vpStart = Math.Max(0, state.ViewportStartIndex);
            int vpEnd = Math.Min(widthData.Length, state.ViewportStartIndex + state.ViewportLength);
            for (int i = vpStart; i < vpEnd; i++)
            {
                if (!double.IsNaN(widthData[i]))
                    maxWidth = Math.Max(maxWidth, Math.Abs(widthData[i]));
            }

            float normalizedVol = maxWidth > 0 ? (float)(absWidth / maxWidth) : 0f;
            float volume = Math.Clamp(normalizedVol * comp.Volume * state.ChartVolume * series.Volume, 0.05f, 1f);

            // Select frequency based on cloud direction.
            double freq = isBullish ? comp.BullishFrequency : comp.BearishFrequency;

            // Slot 0: sine carrier — warm, soft fundamental.
            _audioDriver.SetVoice(SLOT_NAV_START, freq, volume, pan,
                "sine", false, 0.4, dataIndex, "Sustain", false, 0f);

            // Slot 1: triangle blend at 35% volume — adds warmth without harshness.
            float blendVol = volume * 0.35f;
            if (blendVol > 0.02f)
            {
                _audioDriver.SetVoice(SLOT_NAV_START + 1, freq, blendVol, pan,
                    "triangle", false, 0.4, dataIndex, "Sustain", false, 0f);
            }
            else
            {
                _audioDriver.StopVoice(SLOT_NAV_START + 1);
            }

            // Clear remaining navigation slots.
            for (int i = 2; i < 8; i++) _audioDriver.StopVoice(SLOT_NAV_START + i);
        }

        private void MuteAllNavigationSlots()
        {
            for (int i = 0; i < 8; i++) _audioDriver.StopVoice(SLOT_NAV_START + i);
        }

        public AudioPoint CreateAudioPoint(ChartSeries series, int componentIndex, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, int dataIndex, float masterVolume = 1.0f, double? overrideValue = null)
        {
            if (componentIndex < 0 || componentIndex >= series.Components.Count) return new AudioPoint(0, 0, "sine", 0, "Sustain");
            var comp = series.Components[componentIndex];
            var data = series.GetComponentData(comp.Name);
            double? dataVal = (dataIndex >= 0 && dataIndex < data.Length) ? data[dataIndex] : null;
            
            bool isPriceSeries = series.Id == "price" || series.Id == "candles" || series.Id == "volume" || series.Pane == "Volume";
            double val = overrideValue ?? dataVal ?? (isPriceSeries ? (point.GetValue(series.Id, comp.Name) ?? double.NaN) : double.NaN);
            
            if (double.IsNaN(val)) return new AudioPoint(0, 0, "sine", 0, "Sustain");
            
            double? prevVal = null;
            if (dataIndex > 0 && dataIndex <= data.Length)
            {
                prevVal = data[dataIndex - 1];
            }
            
            return _strategy.CreateAudioPoint(series, comp, val, point, relativeIndex, viewportWidth, viewportRange, masterVolume, prevVal);
        }

        public void StopNavigationVoice() => _audioDriver.StopVoice(SLOT_NAV_START);
        public void SetMasterGain(float gain) => _audioDriver.SetMasterGain(gain);
        public void Silence() => _audioDriver.Reset();

        public void PlayNote(double freq, double dur, string wave, float vol, float pan, double delay = 0)
        {
            int slot = SLOT_UI_START + (Interlocked.Increment(ref _uiSlotCounter) & 15);
            _audioDriver.SetVoice(slot, freq, vol, pan, wave, false, dur);
        }

        public void PlayPatch(AccessibleTrader.Sdk.Models.SoundPatch patch, float volumeScale = 1f, float pan = 0f)
        {
            if (patch == null) return;
            double baseFreq = patch.BaseFrequency * patch.FreqMultiplier;
            string env = string.IsNullOrEmpty(patch.EnvelopeType) ? "Sustain" : patch.EnvelopeType;
            // One engine voice per oscillator layer, on round-robin UI slots (16-31) so they
            // sound simultaneously. Envelope + per-layer noise flow through to the engine, which
            // is why the Sound Designer's Envelope/Noise controls now audition correctly.
            foreach (var layer in patch.EffectiveLayers())
            {
                int slot = SLOT_UI_START + (Interlocked.Increment(ref _uiSlotCounter) & 15);
                float vol = Math.Clamp(patch.Volume * layer.Gain * volumeScale, 0f, 1f);
                _audioDriver.SetVoice(slot, baseFreq * layer.FreqRatio, vol, pan, layer.Waveform, false,
                    patch.DurationSeconds, -1, env, false, Math.Max(0f, layer.NoiseAmount),
                    string.IsNullOrEmpty(layer.NoiseType) ? "pink" : layer.NoiseType);
            }
        }

        public void SonifySeries(ChartSeries series, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, int dataIndex, float masterVolume = 1.0f, double durationSeconds = 0.2, double delayMilliseconds = 0)
        {
            if (series == null || !series.IsVisible || series.IsMuted) return;
            foreach (var comp in series.Components)
            {
                if (!comp.IsVisible || comp.IsMuted) continue;
                int cIdx = series.Components.IndexOf(comp);
                SonifyComponent(series, cIdx, point, relativeIndex, viewportWidth, viewportRange, dataIndex, masterVolume, durationSeconds, delayMilliseconds);
            }
        }

        public void SonifyProfile(ChartSeries series, int binIndex, float masterVolume = 1.0f)
        {
            if (series == null || series.Data.ProfileBins == null || binIndex < 0 || binIndex >= series.Data.ProfileBins.Count) return;

            var allBins = series.Data.ProfileBins;
            var bin     = allBins[binIndex];

            // Node-type determines pitch and timbre. No Y-axis pitch shift — position is irrelevant
            // for profiles; structural role is what matters perceptually.
            var nodeType = ProfileBinClassifier.Classify(bin, allBins);
            double freq  = ProfileBinClassifier.GetBasePitch(nodeType);
            string wave  = ProfileBinClassifier.GetWaveform(nodeType);
            bool click   = ProfileBinClassifier.ShouldTriggerClick(nodeType);
            double dur   = ProfileBinClassifier.GetDuration(nodeType);

            // Amplitude: normalised against the session maximum so louder = more volume.
            double maxVol = allBins.Count > 0 ? allBins.Max(b => b.TotalVolume) : 1.0;
            float vol = (float)Math.Clamp(maxVol > 0 ? bin.TotalVolume / maxVol : 0.1, 0.15, 1.0)
                        * masterVolume * series.Volume;

            _audioDriver.StopVoice(SLOT_NAV_START);
            _audioDriver.SetVoice(SLOT_NAV_START, freq, vol, 0f, wave, false, dur, binIndex,
                click ? "Ping" : "Sustain", click); // "Ping" is the short percussive envelope; AudioEngine doesn't recognize "Percussive"
        }

        public void SonifyHeatmap(ChartSeries series, int dataIndex, int binIndex, float masterVolume = 1.0f)
        {
            if (series == null || series.Data.HeatmapData == null || dataIndex < 0 || dataIndex >= series.Data.HeatmapData.Count) return;
            var bar = series.Data.HeatmapData[dataIndex];
            if (bar == null || binIndex < 0 || binIndex >= bar.Count) return;

            var bin = bar[binIndex];

            // ── Intensity classification (node-type base pitch) ──────────────────
            // Uses the same profile node-type pitches so both series feel consistent,
            // but classifies against this bar's bin list (not the full history).
            // Mark the column's highest-volume bin as IsPOC so the classifier can
            // identify HVN/LVN relative to the column mean.
            double barMaxVol = bar.Count > 0 ? bar.Max(b => b.TotalVolume) : 1.0;
            var classifyBins = bar.Select(b => new ProfileBin
            {
                PriceLow       = b.PriceLow,
                PriceHigh      = b.PriceHigh,
                TotalVolume    = b.TotalVolume,
                TpoPeriodCount = 0,
                IsPOC          = Math.Abs(b.TotalVolume - barMaxVol) < 1e-9,
                IsValueArea    = false,
            }).ToList();

            var nodeType  = ProfileBinClassifier.Classify(classifyBins[binIndex], classifyBins);
            double basePitch = ProfileBinClassifier.GetBasePitch(nodeType);

            // ── Y-position pitch shift ───────────────────────────────────────────
            // Compute normalised Y from the SERIES-WIDE price range so pitch is consistent
            // across all time columns — navigating the same price always sounds the same.
            var allBins = series.Data.HeatmapData.Where(b => b != null).SelectMany(b => b).ToList();
            if (allBins.Count == 0) return;
            double globalMin = allBins.Min(b => b.PriceLow);
            double globalMax = allBins.Max(b => b.PriceHigh);
            double span      = Math.Max(1e-9, globalMax - globalMin);
            double normalizedY = (bin.PriceLow - globalMin) / span;

            double yMultiplier = ProfileBinClassifier.GetYMultiplier(normalizedY);
            double freq = basePitch * yMultiplier;

            // ── Amplitude: normalised against this column's maximum ──────────────
            float vol = (float)Math.Clamp(barMaxVol > 0 ? bin.TotalVolume / barMaxVol : 0.1, 0.15, 1.0)
                        * masterVolume * series.Volume;

            _audioDriver.StopVoice(SLOT_NAV_START);
            // Sawtooth waveform distinguishes heatmaps perceptually from all other series.
            _audioDriver.SetVoice(SLOT_NAV_START, freq, vol, 0f, "sawtooth", false, 0.1, binIndex);
        }

        public void SonifyComponent(ChartSeries series, int componentIndex, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, int dataIndex, float masterVolume = 1.0f, double durationSeconds = 0.2, double delayMilliseconds = 0)
        {
            if (series == null || !series.IsVisible || series.IsMuted) return;
            if (componentIndex >= 0 && componentIndex < series.Components.Count && series.Components[componentIndex].IsMuted) return;
            var audioPt = CreateAudioPoint(series, componentIndex, point, relativeIndex, viewportWidth, viewportRange, dataIndex, masterVolume);
            if (audioPt.Volume <= 0) return;

            // Always use the dedicated navigation slot (0) so a new navigation note
            // immediately interrupts any prior navigation note.  PlayNote (earcons) uses
            // slots 16-31 and never interferes with slot 0.
            _audioDriver.StopVoice(SLOT_NAV_START);
            _audioDriver.SetVoice(SLOT_NAV_START, audioPt.Frequency, audioPt.Volume, (float)audioPt.Pan, audioPt.Waveform, false, durationSeconds, dataIndex, audioPt.EnvelopeType, audioPt.TriggerClick, audioPt.NoiseAmount, audioPt.NoiseType, audioPt.SquareMix, audioPt.SawMix, audioPt.TriangleMix, audioPt.SubSawMix);
        }


        /// <summary>
        /// Returns true if the component is considered "positive/bullish" for within-tier ordering.
        /// Positive = DirectionPitch with BaseFrequency above 400 Hz, or a name suggesting bullish bias.
        /// </summary>
        private static bool IsPositiveSignal(ComponentConfig comp)
        {
            if (comp.PitchMapping == PitchMapping.Direction)
                return comp.BaseFrequency > 400.0;

            string dn = (comp.DisplayName ?? comp.Name);
            return dn.Contains("Bull", StringComparison.OrdinalIgnoreCase) ||
                   dn.Contains("Buy", StringComparison.OrdinalIgnoreCase) ||
                   dn.Contains("Up", StringComparison.OrdinalIgnoreCase);
        }

        public async Task FireClusterTicksAsync(WorkspaceState state, int dataIndex, string excludeSeriesId, int excludeComponentIndex, bool crossSeriesMode = false)
        {
            if (dataIndex < 0 || dataIndex >= state.Data.Count) return;

            // Collect active marker signals on this bar.
            // Navigation mode (crossSeriesMode=false): only the focused series (excludeSeriesId = focused series).
            // Playback mode (crossSeriesMode=true): all visible series.
            var signals = new List<(int tier, bool positive, double freq, float vol, string waveform, double decaySeconds, string seriesId, int compIdx)>();

            foreach (var series in state.ActiveSeries)
            {
                if (!series.IsVisible || series.IsMuted) continue;
                if (series.IsProfile || series.Components.Any(c => c.DisplayType == ComponentDisplayType.Heatmap || c.DisplayType == ComponentDisplayType.Profile)) continue;

                // In navigation mode, only scan the focused series for cluster ticks.
                // Cross-indicator audio only happens during playback.
                if (!crossSeriesMode && series.Id != excludeSeriesId) continue;

                for (int ci = 0; ci < series.Components.Count; ci++)
                {
                    var comp = series.Components[ci];
                    if (!comp.IsVisible || comp.IsMuted) continue;
                    if (!AudioConstants.MarkerDisplayTypes.Contains(comp.DisplayType)) continue;
                    if (comp.IsZoneLine) continue;

                    // Skip the already-fired focused component.
                    if (series.Id == excludeSeriesId && ci == excludeComponentIndex) continue;

                    var data = series.GetComponentData(comp.Name);
                    if (data == null || dataIndex >= data.Length) continue;
                    double val = data[dataIndex];
                    if (double.IsNaN(val)) continue;

                    // Determine audio parameters.
                    double freq = comp.BaseFrequency * comp.FreqMultiplier;
                    if (comp.PitchMapping == PitchMapping.Direction)
                    {
                        double dirRef = comp.ReferenceLevel ?? 0.0;
                        freq = val >= dirRef ? comp.BullishFrequency : comp.BearishFrequency;
                    }

                    // Decay: prefer DecayMs, then patch DefaultDecayMs, then 0.15s default.
                    double decaySec = 0.15;
                    if (comp.DecayMs.HasValue)
                        decaySec = comp.DecayMs.Value / 1000.0;
                    else if (!string.IsNullOrEmpty(comp.SoundPatchId) &&
                             _patchRegistry.TryGetPatch(comp.SoundPatchId, out var patch))
                        decaySec = patch.DefaultDecayMs / 1000.0;

                    float vol = Math.Clamp(comp.Volume * state.ChartVolume, 0f, 1f);
                    int tier = SignalTierClassifier.GetTier(comp, series);
                    bool positive = IsPositiveSignal(comp);

                    signals.Add((tier, positive, freq, vol, comp.Waveform, decaySec, series.Id, ci));
                }
            }

            if (signals.Count == 0) return;

            // Sort: tier ascending, then positive before negative within each tier.
            signals.Sort((a, b) =>
            {
                int tc = a.tier.CompareTo(b.tier);
                if (tc != 0) return tc;
                // positive first (positive=true < positive=false in ascending bool order is false, so invert)
                return b.positive.CompareTo(a.positive);
            });

            // Compute reactive pan matching SyncNavigationSlots — width depends on whether
            // the viewport is at the live edge (effective window) or panned back (full viewport).
            int clusterPanWidth = AudioConstants.ComputePanWidth(state);
            float clusterPan = (float)AudioConstants.CalculatePan(dataIndex - state.ViewportStartIndex, clusterPanWidth);

            // Fire on slots 3–7 (up to 5 cluster ticks).
            int maxTicks = Math.Min(signals.Count, 5);
            for (int i = 0; i < maxTicks; i++)
            {
                var (_, _, freq, vol, waveform, decaySec, _, _) = signals[i];
                int slot = SLOT_NAV_START + 3 + i;
                _audioDriver.SetVoice(slot, freq, vol, clusterPan, waveform, false, decaySec, dataIndex, "Ping", false, 0f);
                if (i < maxTicks - 1)
                    await Task.Delay(100).ConfigureAwait(false);
            }
        }
    }
}