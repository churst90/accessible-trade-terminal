using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Audio
{
    /// <summary>
    /// Three-tier approach / crossing / sustained earcon layer. Per-bar tracker that
    /// runs alongside the existing navigation sonification and the zone-noise path.
    ///
    /// Tier 1 (approach): value is within <see cref="ApproachBandFraction"/> of an
    ///   OB/OS level but has not yet crossed. Quiet sine chime whose volume scales
    ///   with proximity (louder when closer to the line). Fires once per approach
    ///   episode — re-arms after the value leaves the approach band.
    ///
    /// Tier 2 (crossing): handled by the existing per-level <c>PlayEarcon</c> path
    ///   in <see cref="AccessibleTrader.Core.Services.Accessibility.EarconService"/>
    ///   and the sonification strategy. Not re-implemented here.
    ///
    /// Tier 3 (sustained): once the value has been past the level for
    ///   <see cref="SustainedBarsThreshold"/> + 1 consecutive bars, fire a single
    ///   low-frequency confirmation tone. Passive zone noise from
    ///   <see cref="AudioZoneHelper"/> still plays every bar, but the confirmation
    ///   tone cleanly marks "held beyond" to the user without a perpetual loop.
    ///
    /// State is keyed by <c>(seriesId, levelName)</c> so each indicator / level pair
    /// is tracked independently. <see cref="Reset"/> is called on symbol / timeframe
    /// changes so the approach / sustained trackers don't carry stale history.
    /// </summary>
    public interface ILevelCrossingMonitor
    {
        void OnBarNavigated(WorkspaceState state);
        void Reset();
    }

    public sealed class LevelCrossingMonitor : ILevelCrossingMonitor
    {
        private readonly INavigationSonifier _sonifier;
        private readonly Dictionary<string, LevelTrackerState> _states = new();

        // Value is "approaching" when within 5% of the level (relative to |level|).
        // For levels at 0 the fraction is applied to 1.0 as an absolute band so the
        // approach ping still fires for oscillator zero crossings.
        internal const double ApproachBandFraction = 0.05;

        // After this many consecutive bars past the level, Tier 3 fires.
        internal const int SustainedBarsThreshold = 3;

        // Tier 1 chime: high, short, quiet. Amplitude scales with proximity.
        private const double Tier1Freq = 1400.0;
        private const double Tier1Dur = 0.08;
        private const float Tier1BaseVol = 0.32f;   // audible above the bed (was 0.15)

        // Tier 3 confirmation: low, slightly longer, steady.
        private const double Tier3Freq = 220.0;
        private const double Tier3Dur = 0.25;
        private const float Tier3Vol = 0.42f;   // audible above the bed (was 0.20)

        internal class LevelTrackerState
        {
            public int ConsecutiveBeyond;
            public bool SustainedFired;
            public bool ApproachFired;

            /// <summary>
            /// Which side of the level the value sat on last bar: +1 above, -1 below, 0 not yet seen.
            /// Only used for two-sided levels, where "beyond" has no fixed meaning and has to be
            /// discovered from the crossing itself.
            /// </summary>
            public int LastSide;

            /// <summary>
            /// Whether the value has actually crossed since this level was first observed.
            /// Without it, loading a chart with price already above a hand-placed level would fire
            /// the "held beyond" tone a few bars later, reporting a crossing that never happened.
            /// </summary>
            public bool HasCrossed;
        }

        public LevelCrossingMonitor(INavigationSonifier sonifier, ISoundPatchLibrary? patchLibrary = null)
        {
            _sonifier = sonifier;
            _patchLibrary = patchLibrary;
        }

        // Optional: user patch library for cue overrides (null in minimal tests).
        private readonly ISoundPatchLibrary? _patchLibrary;

        /// <summary>Plays the Sound Designer patch assigned to a cue key, if any.
        /// Returns false so callers fall back to the built-in tone.</summary>
        private bool TryPlayCuePatch(string earconKey, float volumeScale, float pan)
        {
            if (_patchLibrary == null) return false;
            if (!_patchLibrary.EarconOverrides.EarconPatchIds.TryGetValue(earconKey, out var pid)
                || string.IsNullOrEmpty(pid))
                return false;
            var patch = _patchLibrary.GetPatch(pid);
            if (patch == null) return false;
            _sonifier.PlayPatch(patch, volumeScale, pan);
            return true;
        }

        public void Reset() => _states.Clear();

        public void OnBarNavigated(WorkspaceState state)
        {
            int idx = state.CurrentDataIndex;
            if (idx < 0 || state.Data == null || idx >= state.Data.Count) return;

            foreach (var series in state.ActiveSeries)
            {
                if (!series.IsVisible) continue;
                if (series.Levels == null || series.Levels.Count == 0) continue;

                // Primary component = the non-level visible data line the indicator
                // uses for its main value (RSI %, Stoch %K, Cipher B WT, etc.).
                var primary = series.Components.FirstOrDefault(c =>
                    c.IsVisible && c.Role != ComponentRole.Level &&
                    c.DisplayType != ComponentDisplayType.Level);
                if (primary == null) continue;

                var compData = series.GetComponentData(primary.Name);
                if (compData == null || idx >= compData.Length) continue;

                double val = compData[idx];
                if (double.IsNaN(val)) continue;

                foreach (var lc in series.Levels)
                {
                    if (!lc.IsVisible || !lc.PlayEarcon) continue;

                    // Direction used to be sniffed from the level's NAME here, and any level that
                    // was not called "Overbought" or "Oversold" was skipped outright — so a level a
                    // person placed themselves, with "Play Earcon on Crossing" ticked, made no sound
                    // and gave no reason. LevelConfig.EffectiveCrossDirection keeps that inference
                    // for provider levels but resolves everything else to Both.
                    ProcessLevel(state, series, lc, val, lc.EffectiveCrossDirection);
                }
            }
        }

        private void ProcessLevel(WorkspaceState state, ChartSeries series, LevelConfig lc,
            double val, LevelCrossDirection direction)
        {
            // Keyed by value as well as name: two hand-placed levels can share a name, and sharing a
            // tracker would make each one cancel the other's approach and sustained state.
            string key = series.Id + "::" + lc.Name + "::" + lc.Value.ToString("R",
                System.Globalization.CultureInfo.InvariantCulture);
            if (!_states.TryGetValue(key, out var s))
            {
                s = new LevelTrackerState();
                _states[key] = s;
            }

            int side = val > lc.Value ? 1 : val < lc.Value ? -1 : 0;
            bool crossedThisBar = side != 0 && s.LastSide != 0 && side != s.LastSide;
            if (side != 0) s.LastSide = side;
            if (crossedThisBar) s.HasCrossed = true;

            if (direction == LevelCrossDirection.Both)
            {
                TwoSided(state, lc, val, s, crossedThisBar);
                return;
            }

            bool beyond = direction == LevelCrossDirection.Above ? val > lc.Value : val < lc.Value;

            if (beyond)
            {
                s.ConsecutiveBeyond++;
                s.ApproachFired = false; // re-arm approach for the next exit

                if (!s.SustainedFired && s.ConsecutiveBeyond > SustainedBarsThreshold)
                {
                    s.SustainedFired = true;
                    float pan = ComputePan(state);
                    if (!TryPlayCuePatch(EarconPatchPlayer.SustainedKey, lc.EarconVolume, pan))
                        _sonifier.PlayNote(Tier3Freq, Tier3Dur, "sine", Tier3Vol * lc.EarconVolume, pan);
                }
                return;
            }

            // Outside zone — reset sustained state so re-entry re-arms the Tier 3 trigger.
            s.ConsecutiveBeyond = 0;
            s.SustainedFired = false;

            Approach(state, lc, val, s, direction);
        }

        /// <summary>
        /// A level with no fixed "beyond" side — the kind a person places on a price chart.
        ///
        /// <para>
        /// The one-sided path can return early once the value is inside the zone, because for an
        /// overbought line "inside" is a lasting condition and approaching it again is meaningless
        /// until it leaves. A two-sided level has no inside: price is always on one side of it, so
        /// the two tiers have to run <b>together</b> rather than as alternatives.
        /// </para>
        ///
        /// <list type="bullet">
        ///   <item><b>Sustained</b> means "crossed your line and stayed past it", so the counter
        ///         restarts at every crossing and the tone fires once per crossing — not once per
        ///         chart.</item>
        ///   <item><b>Approach</b> fires from either direction, because someone who marked a price
        ///         wants to hear it coming whichever way it arrives.</item>
        /// </list>
        ///
        /// <para>
        /// Nothing fires before the first crossing. Loading a chart whose price already sits above a
        /// hand-placed level must not announce a crossing that never happened.
        /// </para>
        /// </summary>
        private void TwoSided(WorkspaceState state, LevelConfig lc, double val,
            LevelTrackerState s, bool crossedThisBar)
        {
            if (crossedThisBar)
            {
                s.ConsecutiveBeyond = 0;
                s.SustainedFired = false;
            }

            if (s.HasCrossed)
            {
                s.ConsecutiveBeyond++;
                if (!s.SustainedFired && s.ConsecutiveBeyond > SustainedBarsThreshold)
                {
                    s.SustainedFired = true;
                    float pan = ComputePan(state);
                    if (!TryPlayCuePatch(EarconPatchPlayer.SustainedKey, lc.EarconVolume, pan))
                        _sonifier.PlayNote(Tier3Freq, Tier3Dur, "sine", Tier3Vol * lc.EarconVolume, pan);
                }
            }

            Approach(state, lc, val, s, LevelCrossDirection.Both);
        }

        /// <summary>
        /// Tier 1: a short high ping when the value comes within <see cref="ApproachBandFraction"/> of
        /// the level, louder the closer it is. Fires once per approach episode and re-arms when the
        /// value leaves the band.
        /// </summary>
        private void Approach(WorkspaceState state, LevelConfig lc, double val,
            LevelTrackerState s, LevelCrossDirection direction)
        {
            double levelAbs = Math.Abs(lc.Value);
            double band = (levelAbs > 0 ? levelAbs : 1.0) * ApproachBandFraction;
            double distance = Math.Abs(val - lc.Value);

            bool approaching = distance > 0 && distance <= band;

            // Direction gate: only ping when the value is on the "outside" of the level
            // (approaching from below an OB line, or from above an OS line). Otherwise a value
            // drifting away from the level on the wrong side would still ping, which feels random.
            //
            // A two-sided level has no outside, so it pings from either direction — which is exactly
            // what marking a price and wanting to hear it approach should do.
            if (direction == LevelCrossDirection.Above && val >= lc.Value) approaching = false;
            if (direction == LevelCrossDirection.Below && val <= lc.Value) approaching = false;

            if (approaching && !s.ApproachFired)
            {
                double proximity = 1.0 - (distance / band); // 1.0 = at the line; 0.0 = band edge
                float vol = Tier1BaseVol * (float)Math.Clamp(proximity, 0.0, 1.0) * lc.EarconVolume;
                float pan = ComputePan(state);
                if (!TryPlayCuePatch(EarconPatchPlayer.ApproachKey,
                        (float)Math.Clamp(proximity, 0.0, 1.0) * lc.EarconVolume, pan))
                    _sonifier.PlayNote(Tier1Freq, Tier1Dur, "sine", vol, pan);
                s.ApproachFired = true;
            }
            else if (!approaching)
            {
                s.ApproachFired = false;
            }
        }

        /// <summary>
        /// Level cues pan to the bar they belong to, so this MUST agree with the pan the
        /// navigation path gives that same bar. It used to compute the centre-of-slot form
        /// inline while <see cref="AudioConstants.CalculatePan"/> computed an edge-to-edge one;
        /// delegating removes the possibility of a second formula rather than merely fixing it.
        /// Internal so a test can pin the agreement.
        /// </summary>
        internal static float ComputePan(WorkspaceState state)
        {
            int rel = state.CurrentDataIndex - state.ViewportStartIndex;
            return (float)AudioConstants.CalculatePan(rel, AudioConstants.ComputePanWidth(state));
        }
    }
}
