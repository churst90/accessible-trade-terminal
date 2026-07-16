using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Audio
{
    public interface IAudioSequencer
    {
        bool IsPlaying { get; }
        IObservable<Unit> PlaybackFinished { get; }
        IObservable<int> PointReached { get; }
        /// <param name="componentFilter">
        /// -1 = play all visible components in the series.
        /// n  = play only component at index n (Component scope).
        /// </param>
        Task StartPlaybackAsync(ChartSeries series, List<Ohlcv> data, int startIndex, CancellationToken ct, int componentFilter = -1);
        /// <summary>
        /// Plays all provided series simultaneously, bar by bar. Every visible, unmuted component
        /// across every visible, unmuted series is layered on top of each other — same bar, same
        /// moment — packed into the 64-voice playback budget (slots 32-95) by the voice plan.
        /// </summary>
        Task StartMultiSeriesPlaybackAsync(IReadOnlyList<ChartSeries> seriesList, List<Ohlcv> data, int startIndex, CancellationToken ct);
        void Stop();
    }

    public class AudioSequencer : IAudioSequencer
    {


        private readonly IAudioDriver _audioDriver;
        private readonly ISonificationStrategy _strategy;
        private readonly IWorkspaceStore _store;
        private readonly ISoundPatchRegistry _patchRegistry;
        private readonly ILogger<AudioSequencer> _logger;
        private readonly Subject<Unit> _playbackFinished = new();
        private readonly Subject<int> _pointReached = new();
        private CancellationTokenSource? _cts;

        // Slot map over the engine's 128 voices: 0-15 navigation, 16-31 earcons,
        // 32-95 playback (64 voices), 96-127 cloud fills (32 voices).
        private const int PlaybackSlotOffset = 32;
        private const int PlaybackSlotEnd = 95;   // last slot usable by component playback voices

        // Cloud fill voices during multi-series (Chart scope) playback.
        private const int CloudSlotOffset = 96;
        private const int MaxCloudSlots = 32;

        public bool IsPlaying => _cts != null && !_cts.Token.IsCancellationRequested;
        public IObservable<Unit> PlaybackFinished => _playbackFinished;
        public IObservable<int> PointReached => _pointReached;

        public AudioSequencer(IAudioDriver audioDriver, ISonificationStrategy strategy, IWorkspaceStore store, ISoundPatchRegistry patchRegistry, ILogger<AudioSequencer> logger,
            ISoundPatchLibrary? patchLibrary = null)
        {
            _audioDriver = audioDriver;
            _strategy = strategy;
            _store = store;
            _patchRegistry = patchRegistry;
            _logger = logger;
            _patchLibrary = patchLibrary;
        }

        // Optional: user patch library for level-cue earcon overrides (null in minimal tests).
        private readonly ISoundPatchLibrary? _patchLibrary;

        /// <summary>
        /// Returns the volume multiplier for a PlaybackLayer:
        /// Background=60%, Midground=80%, Foreground=100%.
        /// </summary>
        private static float LayerVolume(AccessibleTrader.Sdk.Models.PlaybackLayer layer) => layer switch
        {
            AccessibleTrader.Sdk.Models.PlaybackLayer.Background => 0.60f,
            AccessibleTrader.Sdk.Models.PlaybackLayer.Foreground => 1.00f,
            _ => 0.80f  // Midground default
        };

        /// <summary>
        /// Resolves the Ping duration in seconds for a given component and audio point.
        /// Applies DecayMs > patch DefaultDecayMs > existing bar-proportional default.
        /// </summary>
        private double ResolvePingDuration(AccessibleTrader.Sdk.Models.ComponentConfig comp, AudioPoint audioPt, double msPerBar)
        {
            if (audioPt.EnvelopeType != "Ping") return 0.0;

            // Component-level DecayMs wins (user edit or metadata layer 1).
            if (comp.DecayMs.HasValue)
                return Math.Min(comp.DecayMs.Value / 1000.0, msPerBar * 0.8 / 1000.0);

            // Patch default decay if component has a registered patch.
            if (!string.IsNullOrEmpty(audioPt.PatchId) &&
                _patchRegistry.TryGetPatch(audioPt.PatchId, out var patch))
                return Math.Min(patch.DefaultDecayMs / 1000.0, msPerBar * 0.8 / 1000.0);

            // Existing bar-proportional default.
            return Math.Min(0.15, msPerBar * 0.8 / 1000.0);
        }

        public async Task StartPlaybackAsync(ChartSeries series, List<Ohlcv> data, int startIndex, CancellationToken ct, int componentFilter = -1)
        {
            Stop();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _cts.Token;

            try
            {
                int count = data.Count;
                var seriesList = new[] { series };
                // Series scope: all components; Component scope: just the pinned component.
                var plan = BuildVoicePlan(seriesList, componentFilter);

                for (int i = startIndex; i < count; i++)
                {
                    if (token.IsCancellationRequested) break;

                    _pointReached.OnNext(i);

                    var state = _store.State;
                    // Sync store with playback position so visuals and speech remain consistent
                    _store.Dispatch(new NavigateAction(i));

                    // Scale 100ms base by inverse of PlaybackSpeed (e.g. 2.0x = 50ms delay)
                    double msPerBar = 100.0 / Math.Max(0.1, state.PlaybackSpeed);
                    int effPanWidth = AudioConstants.ComputePanWidth(state);

                    foreach (var vp in plan)
                        RenderComponentVoices(vp, seriesList, data, i, state, msPerBar, effPanWidth, token);

                    // Cloud pass: fire cloud fill voices for the focused series.
                    // Enables series-scope playback (Shift+Space) to sonify cloud fills
                    // (e.g. EMA Fill) rather than only hearing the two component lines.
                    var state2 = _store.State;
                    int cloudSlot2 = 0;
                    int cloudEnd2 = state2.ViewportStartIndex + AudioConstants.ComputePanWidth(state2) - 1;
                    FireCloudVoices(seriesList, i, state2.ViewportStartIndex, cloudEnd2, msPerBar, ref cloudSlot2);

                    await Task.Delay((int)msPerBar, token).ConfigureAwait(false);

                    // While paused, wait without incrementing. Silence the continuous playback
                    // voices first so the current bar's chord doesn't drone on while parked.
                    if (_store.State.IsPaused)
                    {
                        SilencePlaybackVoices();
                        while (_store.State.IsPaused && !token.IsCancellationRequested)
                            await Task.Delay(50, token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sequencer failed");
            }
            finally
            {
                _playbackFinished.OnNext(Unit.Default);
                Stop();
            }
        }

        /// <summary>A component's contiguous playback voice slots. Slots[0] is the main voice; extra
        /// slots carry a multi-oscillator patch's additional layers, or a gradient/detune aux voice.</summary>
        private readonly record struct VoicePlan(int SeriesIdx, int CompIdx, int[] Slots);

        /// <summary>
        /// Assigns stable voice slots (32-95) to every visible, unmuted component across the given series
        /// (optionally filtered to one component for Component scope), packing them sequentially so nothing
        /// is silently dropped up to the 64-voice budget. Each component reserves
        /// <see cref="ISonificationStrategy.ResolveComponentVoiceCount"/> slots — enough for a
        /// multi-oscillator patch's layers or a gradient/detune aux. Excess is logged, not hidden. Slots
        /// are assigned once from config so continuous voices keep a stable slot and glide bar-to-bar.
        /// </summary>
        private List<VoicePlan> BuildVoicePlan(IReadOnlyList<ChartSeries> seriesList, int componentFilter = -1)
        {
            var plan = new List<VoicePlan>();
            int cursor = PlaybackSlotOffset;
            bool overflow = false;

            for (int s = 0; s < seriesList.Count && !overflow; s++)
            {
                var series = seriesList[s];
                if (series == null || !series.IsVisible || series.IsMuted || series.IsDrawing) continue;

                for (int c = 0; c < series.Components.Count; c++)
                {
                    if (componentFilter >= 0 && c != componentFilter) continue;
                    var comp = series.Components[c];
                    if (!comp.IsVisible || comp.IsMuted) continue;
                    if (cursor > PlaybackSlotEnd) { overflow = true; break; }

                    int need = Math.Max(1, _strategy.ResolveComponentVoiceCount(comp));
                    int room = PlaybackSlotEnd - cursor + 1;
                    if (need > room) { need = room; overflow = true; } // truncate this component's layers

                    var slots = new int[need];
                    for (int k = 0; k < need; k++) slots[k] = cursor++;
                    plan.Add(new VoicePlan(s, c, slots));

                    if (overflow) break;
                }
            }

            if (overflow)
                _logger.LogWarning(
                    "Chart playback needs more than {Budget} simultaneous voices; some components/layers were not sonified. Mute or hide series to stay within budget.",
                    PlaybackSlotEnd - PlaybackSlotOffset + 1);

            return plan;
        }

        /// <summary>
        /// Fires one bar of a single component's voices onto its reserved plan slots: a cloud fill, a
        /// multi-oscillator patch's layers, or the standard main-voice (+ gradient blend / detuned aux).
        /// </summary>
        private void RenderComponentVoices(VoicePlan vp, IReadOnlyList<ChartSeries> seriesList, List<Ohlcv> data,
            int i, WorkspaceState state, double msPerBar, int effPanWidth, CancellationToken token)
        {
            var series = seriesList[vp.SeriesIdx];
            var comp = series.Components[vp.CompIdx];
            var slots = vp.Slots;
            int mainSlot = slots[0];
            int auxSlot = slots.Length > 1 ? slots[1] : -1;

            if (comp.DisplayType == ComponentDisplayType.Cloud)
            {
                PlayCloudComponent(series, comp, i, state, msPerBar, mainSlot, auxSlot);
                return;
            }

            // Sub-pane-aware range so components in a sub-pane use their own Y-range.
            string compRangeKey = !string.IsNullOrEmpty(comp.SubPaneName)
                ? $"{series.Pane}/{comp.SubPaneName}"
                : (series.Pane ?? "");
            var range = state.PaneRanges.TryGetValue(compRangeKey, out var cr) ? cr
                : (state.PaneRanges.TryGetValue(series.Pane ?? "", out var pr) ? pr : state.ViewportRange);

            var audioPt = _strategy.MapComponentToAudio(series, vp.CompIdx, i, data, i - state.ViewportStartIndex, effPanWidth, range, state.ChartVolume);

            // NaN guard for Ping-envelope markers: only fire on actual signal bars.
            if (string.Equals(audioPt.EnvelopeType, "Ping", StringComparison.OrdinalIgnoreCase)
                && AudioConstants.MarkerDisplayTypes.Contains(comp.DisplayType)
                && audioPt.Volume <= 0)
            {
                for (int k = 0; k < slots.Length; k++) _audioDriver.StopVoice(slots[k]);
                return;
            }

            float layerScale = LayerVolume(comp.PlaybackLayer);
            float scaledVol = audioPt.Volume * layerScale;
            bool continuous = audioPt.EnvelopeType != "Ping";
            double durationSec = ResolvePingDuration(comp, audioPt, msPerBar);
            float pan = (float)audioPt.Pan;

            // Directional cross earcon — fires whenever this component crossed a level on this bar,
            // independent of how its voices render below. On the shared 30/31 slots, so simultaneous
            // crosses across components collapse to one chirp per bar rather than a cluster.
            if (audioPt.CrossDirection != 0
                && !EarconPatchPlayer.TryPlayOverride(_patchLibrary, _audioDriver,
                    audioPt.CrossDirection > 0 ? EarconPatchPlayer.CrossUpKey : EarconPatchPlayer.CrossDownKey,
                    1f, pan))
                CrossEarcon.Fire(_audioDriver, audioPt.CrossDirection, 1f, pan);

            // Multi-oscillator user patch: one voice per layer across the reserved slots.
            if (audioPt.PatchLayers != null)
            {
                RenderPatchLayers(slots, audioPt.PatchLayers, audioPt.Frequency, scaledVol, pan, continuous, durationSec, i, audioPt.EnvelopeType, audioPt.TriggerClick);
                return;
            }

            // Gradient blend: sine carrier on the main slot; blend waveform on the aux slot.
            if (comp.UsesGradientSpeech)
            {
                _audioDriver.SetVoice(mainSlot, new VoiceParams
                {
                    Frequency = audioPt.Frequency, Volume = scaledVol, Pan = pan,
                    Waveform = "sine", Continuous = continuous, DurationSeconds = durationSec,
                    DataIndex = i, Envelope = audioPt.EnvelopeType, Click = audioPt.TriggerClick,
                    NoiseAmount = audioPt.NoiseAmount, NoiseType = audioPt.NoiseType,
                    SquareMix = audioPt.SquareMix, SawMix = audioPt.SawMix,
                    TriangleMix = audioPt.TriangleMix, SubSawMix = audioPt.SubSawMix,
                });
                float blendVol = ComputeGradientBlend(series, comp, i, scaledVol, out string blendWave);
                if (auxSlot >= 0 && blendVol > 0.01f)
                    _audioDriver.SetVoice(auxSlot, audioPt.Frequency, blendVol, pan, blendWave, continuous, durationSec, i, audioPt.EnvelopeType, false, 0f);
            }
            else
            {
                _audioDriver.SetVoice(mainSlot, new VoiceParams
                {
                    Frequency = audioPt.Frequency, Volume = scaledVol, Pan = pan,
                    Waveform = audioPt.Waveform, Continuous = continuous, DurationSeconds = durationSec,
                    DataIndex = i, Envelope = audioPt.EnvelopeType, Click = audioPt.TriggerClick,
                    NoiseAmount = audioPt.NoiseAmount, NoiseType = audioPt.NoiseType,
                    SquareMix = audioPt.SquareMix, SawMix = audioPt.SawMix,
                    TriangleMix = audioPt.TriangleMix, SubSawMix = audioPt.SubSawMix,
                });
            }

            // Detuned pair bell (built-in patch): second voice on the aux slot.
            if (auxSlot >= 0 && !string.IsNullOrEmpty(audioPt.PatchId) &&
                _patchRegistry.TryGetPatch(audioPt.PatchId, out var patchDef) && patchDef.IsDetuned)
            {
                double detunedFreq = audioPt.Frequency + patchDef.DetuneIntervalHz;
                if (patchDef.DetunedOffsetMs <= 0)
                {
                    _audioDriver.SetVoice(auxSlot, detunedFreq, scaledVol, pan, audioPt.Waveform, false, durationSec, i, "Ping", false, audioPt.NoiseAmount, audioPt.NoiseType);
                }
                else
                {
                    int slotCopy = auxSlot;
                    _ = Task.Delay(patchDef.DetunedOffsetMs, token).ContinueWith(t =>
                    {
                        if (!t.IsCanceled)
                            _audioDriver.SetVoice(slotCopy, detunedFreq, scaledVol, pan, audioPt.Waveform, false, durationSec, i, "Ping", false, audioPt.NoiseAmount, audioPt.NoiseType);
                    }, TaskScheduler.Default);
                }
            }
        }

        /// <summary>
        /// Fires one voice per oscillator layer of a multi-oscillator patch across the reserved slots,
        /// scaling frequency by <c>FreqRatio</c> and volume by <c>Gain</c>. Any reserved-but-unused slots
        /// (e.g. a per-colour patch with fewer layers this bar) are silenced so they don't drone.
        /// </summary>
        private void RenderPatchLayers(int[] slots, IReadOnlyList<AccessibleTrader.Sdk.Models.OscillatorLayer> layers,
            double baseFreq, float baseVol, float pan, bool continuous, double durationSec, int dataIndex, string envelope, bool click)
        {
            int n = Math.Min(layers.Count, slots.Length);
            for (int k = 0; k < n; k++)
            {
                var L = layers[k];
                _audioDriver.SetVoice(slots[k], baseFreq * L.FreqRatio, Math.Clamp(baseVol * L.Gain, 0f, 1f), pan,
                    L.Waveform, continuous, durationSec, k == 0 ? dataIndex : -1, envelope, k == 0 && click,
                    Math.Max(0f, L.NoiseAmount), string.IsNullOrEmpty(L.NoiseType) ? "pink" : L.NoiseType);
            }
            for (int k = n; k < slots.Length; k++) _audioDriver.StopVoice(slots[k]);
        }

        public async Task StartMultiSeriesPlaybackAsync(IReadOnlyList<ChartSeries> seriesList, List<Ohlcv> data, int startIndex, CancellationToken ct)
        {
            Stop();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _cts.Token;

            try
            {
                int count = data.Count;

                // ── Stable voice-slot plan ──────────────────────────────────────────
                // Playback voices live in slots 32-63 (32 total). Rather than a fixed
                // 8-per-series / 4-series grid (which silently dropped the 5th series and
                // the 9th component of any series), pack every visible + unmuted component
                // across every visible + unmuted series into the budget sequentially, so
                // "play chart" really does sound them all at once. Slots are assigned ONCE
                // from component configuration (not per-bar data), so a continuous Sustain
                // voice keeps a stable slot and glides smoothly bar-to-bar.
                var plan = BuildVoicePlan(seriesList);

                for (int i = startIndex; i < count; i++)
                {
                    if (token.IsCancellationRequested) break;

                    _pointReached.OnNext(i);
                    _store.Dispatch(new NavigateAction(i));

                    var state = _store.State;
                    double msPerBar = 100.0 / Math.Max(0.1, state.PlaybackSpeed);
                    int effPanWidth = AudioConstants.ComputePanWidth(state);

                    foreach (var vp in plan)
                        RenderComponentVoices(vp, seriesList, data, i, state, msPerBar, effPanWidth, token);

                    // Cloud pass — fires after component voices for each bar (Chart scope only).
                    int cloudSlot = 0;
                    int cloudEndMulti = state.ViewportStartIndex + AudioConstants.ComputePanWidth(state) - 1;
                    FireCloudVoices(seriesList, i, state.ViewportStartIndex, cloudEndMulti, msPerBar, ref cloudSlot);

                    await Task.Delay((int)msPerBar, token).ConfigureAwait(false);

                    // Silence the continuous playback voices on pause so the last bar doesn't drone.
                    if (_store.State.IsPaused)
                    {
                        SilencePlaybackVoices();
                        while (_store.State.IsPaused && !token.IsCancellationRequested)
                            await Task.Delay(50, token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Multi-series sequencer failed");
            }
            finally
            {
                _playbackFinished.OnNext(Unit.Default);
                Stop();
            }
        }

        /// <summary>
        /// Computes the blend waveform and volume for a gradient_blend component at a given bar.
        /// Returns the blend volume (0 if no color data available) and sets <paramref name="blendWave"/>
        /// to "triangle" (bullish/positive) or "sawtooth" (bearish/negative).
        /// Mirrors the two-slot logic in NavigationSonifier.SyncNavigationSlots.
        /// </summary>
        private static float ComputeGradientBlend(ChartSeries series, ComponentConfig comp, int barIndex, float carrierVolume, out string blendWave)
        {
            blendWave = "triangle";
            var colorData = series.GetComponentData(comp.Name + "_color");
            if (colorData.Length == 0 || barIndex >= colorData.Length || double.IsNaN(colorData[barIndex]))
                return 0f;
            double colorVal = colorData[barIndex];
            blendWave = colorVal >= 0 ? "triangle" : "sawtooth";
            float fraction = (float)(Math.Clamp(Math.Abs(colorVal), 0, 100) / 100.0);
            return carrierVolume * fraction * 0.65f;
        }

        /// <summary>
        /// Plays a Cloud component during Series/Chart-scope playback. Uses the same
        /// width-mapped volume + bullish/bearish frequency approach as navigation, but
        /// with continuous sustain so the tone glides smoothly bar-to-bar.
        /// </summary>
        private void PlayCloudComponent(ChartSeries series, ComponentConfig comp, int barIndex, WorkspaceState state, double msPerBar, int slot, int blendSlot)
        {
            var widthData = series.GetComponentData(comp.Name);
            if (widthData.Length == 0 || barIndex < 0 || barIndex >= widthData.Length) return;

            double signedWidth = widthData[barIndex];
            if (double.IsNaN(signedWidth)) return;

            bool isBullish = signedWidth >= 0;
            double absWidth = Math.Abs(signedWidth);

            // Normalize width against viewport maximum for volume mapping.
            double maxWidth = 0;
            int vpStart = Math.Max(0, state.ViewportStartIndex);
            int vpEnd = Math.Min(widthData.Length, state.ViewportStartIndex + state.ViewportLength);
            for (int a = vpStart; a < vpEnd; a++)
            {
                if (!double.IsNaN(widthData[a]))
                    maxWidth = Math.Max(maxWidth, Math.Abs(widthData[a]));
            }

            float normalizedVol = maxWidth > 0 ? (float)(absWidth / maxWidth) : 0f;
            float volume = Math.Clamp(normalizedVol * comp.Volume * state.ChartVolume * series.Volume, 0.05f, 1f);
            float layerScale = LayerVolume(comp.PlaybackLayer);
            volume *= layerScale;

            double freq = isBullish ? comp.BullishFrequency : comp.BearishFrequency;
            // Pan tracks the renderer's visual layout (see AudioConstants.ComputePanWidth).
            int cloudPanWidth = AudioConstants.ComputePanWidth(state);
            float pan = (float)AudioConstants.CalculatePan(barIndex - state.ViewportStartIndex, cloudPanWidth);

            double durationSec = msPerBar / 1000.0;

            // Sine carrier — warm fundamental, continuous sustain for smooth glide.
            _audioDriver.SetVoice(slot, freq, volume, pan,
                "sine", true, durationSec, barIndex, "Sustain", false, 0f);

            // Triangle blend at 35% volume — adds warmth on the assigned aux slot (if one was reserved).
            float blendVol = volume * 0.35f;
            if (blendSlot >= 0 && blendVol > 0.02f)
            {
                _audioDriver.SetVoice(blendSlot, freq, blendVol, pan,
                    "triangle", true, durationSec, barIndex, "Sustain", false, 0f);
            }
        }

        /// <summary>
        /// Fires cloud fill voices for the current bar during Chart-scope playback.
        /// Each active <see cref="CloudFillConfig"/> with a non-null Sonification config emits one voice.
        /// Volume is proportional to the cloud's thickness relative to the maximum thickness in the viewport.
        /// Direction (upper &gt;= lower = bullish) selects BullishFrequency or BearishFrequency.
        /// </summary>
        private void FireCloudVoices(IReadOnlyList<ChartSeries> seriesList, int barIndex, int viewportStart, int viewportEnd, double msPerBar, ref int cloudSlotCounter)
        {
            foreach (var series in seriesList)
            {
                var fills = series.CloudFills;
                if (fills == null || fills.Count == 0) continue;

                foreach (var fill in fills)
                {
                    if (fill.Sonification == null) continue;
                    if (!fill.IsVisible) continue;

                    // Retrieve data arrays by component name via ChartSeries helper.
                    var upperData = series.GetComponentData(fill.UpperComponentName);
                    var lowerData = series.GetComponentData(fill.LowerComponentName);
                    if (upperData.Length == 0 || lowerData.Length == 0) continue;
                    if (barIndex < 0 || barIndex >= upperData.Length || barIndex >= lowerData.Length) continue;

                    double upperVal = upperData[barIndex];
                    double lowerVal = lowerData[barIndex];
                    if (double.IsNaN(upperVal) || double.IsNaN(lowerVal)) continue;

                    // Cloud thickness → volume: normalize against viewport maximum.
                    float maxThickness = ComputeMaxCloudThickness(upperData, lowerData, viewportStart, viewportEnd);
                    float thickness = (float)Math.Abs(upperVal - lowerVal);
                    float normalizedThickness = maxThickness > 0f ? thickness / maxThickness : 0f;
                    float volume = normalizedThickness * fill.Sonification.MaxVolume;

                    // Skip near-silent bars (consolidation / overlapping lines).
                    if (volume < 0.05f) continue;

                    // Direction → frequency.
                    bool isBullish = upperVal >= lowerVal;
                    double freq = isBullish ? fill.Sonification.BullishFrequency : fill.Sonification.BearishFrequency;

                    // Assign a cloud slot (96-127), cycling if more than MaxCloudSlots fills are active.
                    int slot = CloudSlotOffset + (cloudSlotCounter % MaxCloudSlots);
                    cloudSlotCounter++;

                    double durationSec = Math.Min(fill.Sonification.DecayMs / 1000.0, msPerBar * 0.8 / 1000.0);
                    _audioDriver.SetVoice(slot, freq, volume, 0f, "sine", false, durationSec, barIndex, "Ping", false, 0f);
                }
            }
        }

        /// <summary>
        /// Scans the upper and lower component data arrays over the viewport range and returns
        /// the maximum absolute distance (|upper - lower|) found. Returns 0 if no valid bars exist.
        /// </summary>
        private static float ComputeMaxCloudThickness(double[] upperData, double[] lowerData, int viewportStart, int viewportEnd)
        {
            int start = Math.Max(0, Math.Min(viewportStart, upperData.Length - 1));
            int end   = Math.Max(0, Math.Min(viewportEnd,   upperData.Length - 1));
            float max = 0f;
            for (int i = start; i <= end; i++)
            {
                if (i >= lowerData.Length) break;
                double u = upperData[i];
                double l = lowerData[i];
                if (double.IsNaN(u) || double.IsNaN(l)) continue;
                float t = (float)Math.Abs(u - l);
                if (t > max) max = t;
            }
            return max;
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            SilencePlaybackVoices();
        }

        /// <summary>
        /// Stops every voice this sequencer can drive — component slots 32-63 and cloud slots 64-79.
        /// Playback voices are continuous Sustain oscillators (see <c>continuous = EnvelopeType != "Ping"</c>),
        /// so they ring until explicitly stopped. Called both on <see cref="Stop"/> and on entering pause,
        /// where the loop parks without advancing: without this the last bar's chord drones indefinitely
        /// (very audible on the WebHost browser path, which keeps streaming the sustained samples).
        /// Navigation slots (0-15) are left untouched so arrow-key auditioning still works while paused.
        /// </summary>
        private void SilencePlaybackVoices()
        {
            // Playback (32-95) + cloud fills (96-127); navigation (0-15) and earcons (16-31) stay live.
            for (int i = PlaybackSlotOffset; i < AudioEngine.MaxVoices; i++) _audioDriver.StopVoice(i);
        }
    }
}
