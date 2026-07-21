using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Core.Services.Accessibility
{
    public interface IEarconService
    {
        void PlayError(ErrorSeverity severity);
        void PlaySuccess();
        void PlayRetry();
        void PlayConnectionState(ConnectionState state);
        void PlayBoundary();
        void PlayInfo();
        /// <summary>Bell-like tone signalling a new live bar has opened.</summary>
        void PlayNewBar();

        /// <summary>
        /// User-alert earcon (price/indicator alert fired). Urgent rising double
        /// tone — distinct from Info (single blip) and Error (low buzz).
        /// <paramref name="breakThroughMutes"/> (per-alert setting) bypasses the
        /// Shift+F3 earcon mute for alerts the user marked critical.
        /// </summary>
        void PlayAlert(bool breakThroughMutes = false);

        /// <summary>
        /// Plays the quality-setup bell (long or short variant) when a composite strategy
        /// fires a new setup. <paramref name="reconfirmation"/> = true plays the same chord
        /// at reduced volume so ongoing confirmations don't fatigue the listener — used by
        /// <c>SetupSonifier</c> when a previously-fired setup re-confirms on the next bar.
        /// </summary>
        void PlaySetupBell(OrderSide side, bool reconfirmation);

        /// <summary>
        /// Lighter "setup armed" earcon — fired when conditions are met but the entry trigger
        /// (e.g. OnPullbackToLevel) has not yet fired. The user knows the setup is real but
        /// they aren't in a position yet. Two-tone rising fifth (long) or falling fifth (short).
        /// </summary>
        void PlaySetupArmed(OrderSide side);

        /// <summary>
        /// Slightly brighter "entry zone reached" earcon — fired when an Armed setup's
        /// entry trigger actually fires and an order is placed. Distinct from PlaySetupBell
        /// (the conditions-met bell) so the user can tell apart "setup forming" from "in trade".
        /// </summary>
        void PlaySetupEntryReached(OrderSide side);

        /// <summary>
        /// Order-execution confirmation — fired when an exchange reports a fill (full or
        /// partial). Two quick staccato notes then a sustained tone, so it reads as
        /// "transaction complete" and can't be confused with the setup bells (chords) or
        /// PlaySuccess (plain rising octave). Buy = rising sine, sell = falling triangle,
        /// matching the long/short sound language used across the setup earcons.
        /// </summary>
        void PlayOrderFill(OrderSide side);

        /// <summary>
        /// Protective stop executed. Urgent low descending figure — more serious than
        /// PlayOrderFill but distinct from PlayError: a stop firing is the system working,
        /// not a malfunction.
        /// </summary>
        void PlayStopHit();

        /// <summary>
        /// Take-profit executed. Bright ascending arpeggio — unambiguously positive.
        /// </summary>
        void PlayTakeProfitHit();
    }

    public class EarconService : IEarconService
    {
        private readonly ISonificationManager _sonificationManager;
        private readonly ISoundPatchLibrary _patchLibrary;
        // Monotonic (Environment.TickCount64) rather than wall-clock: the throttle
        // compares intervals, and a clock step (NTP, VM resume) under DateTime.Now
        // could mute earcons for the step duration or let a burst through.
        private readonly ConcurrentDictionary<string, long> _lastPlayedAtMs = new();
        private const long MinIntervalMs = 200;

        // Optional so existing two-arg construction (tests, manual composition)
        // keeps working; DI supplies the bus, enabling the visual earcon channel.
        private readonly IEventBus? _eventBus;

        // Optional (like _eventBus) so existing two/three-arg construction keeps
        // working; DI supplies the store + settings, enabling the Shift+F3 earcon
        // mute tier. Null store = always audible (tests, manual composition).
        private readonly IWorkspaceStore? _store;
        private readonly IAppSettings? _appSettings;

        public EarconService(ISonificationManager sonificationManager, ISoundPatchLibrary patchLibrary,
            IEventBus? eventBus = null, IWorkspaceStore? store = null, IAppSettings? appSettings = null)
        {
            _sonificationManager = sonificationManager;
            _patchLibrary = patchLibrary;
            _eventBus = eventBus;
            _store = store;
            _appSettings = appSettings;
        }

        // ── Shift+F3 earcon mute tier ───────────────────────────────────────
        // Ambient earcons gate on IsEarconsEnabled. Error earcons NEVER gate
        // (silent-failure rule), and order-outcome earcons break through unless
        // the user opted them into the mute (speech.muteIncludesOrderEvents) —
        // mirroring the speech router's channel rules exactly.
        private bool AmbientEarconsAudible() => _store?.State.IsEarconsEnabled ?? true;

        private bool OrderEarconsAudible() =>
            AmbientEarconsAudible() || !(_appSettings?.MuteIncludesOrderEvents ?? false);

        /// <summary>
        /// Mirrors a just-played earcon onto the visual channel (Phase D,
        /// deaf/hard-of-hearing support). Fired only after the same enable +
        /// throttle gates as the audio, so visual and audio cadence match; the
        /// overlay component decides visibility from the opt-in setting.
        /// </summary>
        private void SignalVisual(string label, string tone)
            => _eventBus?.Publish(new EarconVisualEvent(label, tone));

        /// <summary>
        /// Plays a note using the assigned SoundPatch for <paramref name="earconKey"/> if one
        /// is configured in <see cref="EarconSettings"/>; otherwise falls back to
        /// <paramref name="defaultFreq"/>, <paramref name="defaultDuration"/>,
        /// <paramref name="defaultWave"/>, and <paramref name="defaultVol"/>.
        /// </summary>
        private void PlayWithPatchFallback(string earconKey, double defaultFreq, double defaultDuration,
            string defaultWave, float defaultVol, float pan = 0f)
        {
            if (_patchLibrary.EarconOverrides.EarconPatchIds.TryGetValue(earconKey, out var patchId))
            {
                var patch = _patchLibrary.GetPatch(patchId);
                if (patch != null)
                {
                    _sonificationManager.PlayPatch(patch, 1f, pan, force: true);
                    return;
                }
            }
            _sonificationManager.PlayNote(defaultFreq, defaultDuration, defaultWave, defaultVol, pan, force: true);
        }

        public void PlayAlert(bool breakThroughMutes = false)
        {
            if (!breakThroughMutes && !AmbientEarconsAudible()) return;
            if (!CanPlay("alert")) return;
            SignalVisual("Alert", "warning");
            // Rising urgent pair; patch-overridable via the "Alert" earcon key.
            PlayWithPatchFallback("Alert", 880, 0.09, "square", 0.14f);
            _sonificationManager.PlayNote(1174.66, 0.12, "square", 0.14f, 0, 100, force: true);
        }

        public void PlayInfo()
        {
            if (!AmbientEarconsAudible()) return;
            if (!CanPlay("info")) return;
            SignalVisual("Info", "neutral");
            PlayWithPatchFallback("Info", 660, 0.1, "sine", 0.1f);
        }

        private bool CanPlay(string key)
        {
            // Deliberately NOT gated on _sonificationManager.IsEnabled: F3 owns
            // chart sonification, Shift+F3 owns earcons (Ambient/OrderEarconsAudible
            // above). Before 2026-07-21 earcons silently died with F3.
            long now = Environment.TickCount64;
            if (_lastPlayedAtMs.TryGetValue(key, out var last) && now - last < MinIntervalMs) return false;
            _lastPlayedAtMs[key] = now;
            return true;
        }

        public void PlayError(ErrorSeverity severity)
        {
            if (!CanPlay("error")) return;
            SignalVisual($"Error ({severity})", "alert");

            switch (severity)
            {
                case ErrorSeverity.Low:
                    _sonificationManager.PlayNote(150, 0.1, "sine", 0.1f, 0, force: true);
                    break;
                case ErrorSeverity.Medium:
                    _sonificationManager.PlayNote(100, 0.2, "sawtooth", 0.15f, 0, force: true);
                    break;
                case ErrorSeverity.High:
                case ErrorSeverity.Critical:
                    _sonificationManager.PlayNote(80, 0.4, "square", 0.2f, -0.5f, force: true);
                    _sonificationManager.PlayNote(85, 0.4, "square", 0.2f, 0.5f, force: true);
                    break;
            }
        }

        public void PlaySuccess()
        {
            if (!AmbientEarconsAudible()) return;
            if (!CanPlay("success")) return;
            SignalVisual("Success", "positive");
            _sonificationManager.PlayNote(440, 0.1, "sine", 0.1f, 0, force: true);
            _sonificationManager.PlayNote(880, 0.1, "sine", 0.1f, 0, force: true);
        }

        public void PlayRetry()
        {
            if (!AmbientEarconsAudible()) return;
            if (!CanPlay("retry")) return;
            SignalVisual("Retrying", "neutral");
            _sonificationManager.PlayNote(330, 0.1, "sine", 0.1f, 0, force: true);
            _sonificationManager.PlayNote(220, 0.1, "sine", 0.1f, 0, force: true);
        }

        public void PlayBoundary()
        {
            if (!AmbientEarconsAudible()) return;
            if (!CanPlay("boundary")) return;
            SignalVisual("Boundary", "neutral");
            PlayWithPatchFallback("Boundary", 150, 0.1, "square", 0.1f);
        }

        public void PlayNewBar()
        {
            if (!AmbientEarconsAudible()) return;
            if (!CanPlay("newbar")) return;
            SignalVisual("New bar", "neutral");
            if (_patchLibrary.EarconOverrides.EarconPatchIds.TryGetValue("NewBar", out var patchId))
            {
                var patch = _patchLibrary.GetPatch(patchId);
                if (patch != null)
                {
                    _sonificationManager.PlayPatch(patch, 1f, 0f, force: true);
                    return;
                }
            }
            // Bell: three sine partials at fundamental + octave + minor third above octave.
            _sonificationManager.PlayNote(880,  0.06, "sine", 0.12f, 0, force: true);
            _sonificationManager.PlayNote(1320, 0.06, "sine", 0.08f, 0, force: true);
            _sonificationManager.PlayNote(2200, 0.06, "sine", 0.05f, 0, force: true);
        }

        /// <summary>
        /// Renders the setup_long_bell / setup_short_bell character (defined in SoundPatchRegistry)
        /// as a small chord of <see cref="ISonificationManager.PlayNote"/> calls. Long = bright
        /// ascending sine + perfect-fifth above; short = heavy descending triangle + sub-octave.
        /// Reconfirmation halves the duration and drops volume so the user still gets audible
        /// confirmation without being battered by every confirming bar.
        /// </summary>
        public void PlaySetupBell(OrderSide side, bool reconfirmation)
        {
            if (!AmbientEarconsAudible()) return;
            string key = $"setup_{(side == OrderSide.Buy ? "long" : "short")}_{(reconfirmation ? "rc" : "new")}";
            if (!CanPlay(key)) return;
            SignalVisual(side == OrderSide.Buy
                ? (reconfirmation ? "Long setup re-confirmed" : "Long setup")
                : (reconfirmation ? "Short setup re-confirmed" : "Short setup"),
                side == OrderSide.Buy ? "positive" : "negative");

            float vol = reconfirmation ? 0.12f : 0.28f;   // setup signal sits above the bed (was 0.06/0.14)
            double dur = reconfirmation ? 0.30 : 0.70;

            if (side == OrderSide.Buy)
            {
                // Bright ascending chord — sine 440 + perfect-fifth 660 + octave 880 shimmer.
                _sonificationManager.PlayNote(440, dur, "sine", vol,        0f, force: true);
                _sonificationManager.PlayNote(660, dur, "sine", vol * 0.85f, 0f, force: true);
                _sonificationManager.PlayNote(880, dur * 0.6, "sine", vol * 0.5f, 0f, force: true);
            }
            else
            {
                // Heavy descending chord — triangle 220 + sub-fifth 165 + low octave 110.
                _sonificationManager.PlayNote(220, dur, "triangle", vol,        0f, force: true);
                _sonificationManager.PlayNote(165, dur, "triangle", vol * 0.85f, 0f, force: true);
                _sonificationManager.PlayNote(110, dur * 0.6, "sine",     vol * 0.6f,  0f, force: true);
            }
        }

        public void PlaySetupArmed(OrderSide side)
        {
            if (!AmbientEarconsAudible()) return;
            string key = $"setup_armed_{(side == OrderSide.Buy ? "long" : "short")}";
            if (!CanPlay(key)) return;
            SignalVisual(side == OrderSide.Buy ? "Long setup armed" : "Short setup armed",
                side == OrderSide.Buy ? "positive" : "negative");
            // Two-tone fifth: long = rising 660 → 990, short = falling 330 → 220.
            // Distinct from PlaySetupBell by being a clean two-note fifth instead of a chord
            // and by using moderate (~0.10) rather than full (~0.14) volume.
            if (side == OrderSide.Buy)
            {
                _sonificationManager.PlayNote(660, 0.40, "sine", 0.10f, 0f, force: true);
                _sonificationManager.PlayNote(990, 0.40, "sine", 0.08f, 0f, force: true);
            }
            else
            {
                _sonificationManager.PlayNote(330, 0.40, "triangle", 0.10f, 0f, force: true);
                _sonificationManager.PlayNote(220, 0.40, "triangle", 0.08f, 0f, force: true);
            }
        }

        public void PlaySetupEntryReached(OrderSide side)
        {
            if (!AmbientEarconsAudible()) return;
            string key = $"setup_entry_{(side == OrderSide.Buy ? "long" : "short")}";
            if (!CanPlay(key)) return;
            SignalVisual(side == OrderSide.Buy ? "Long entry reached" : "Short entry reached",
                side == OrderSide.Buy ? "positive" : "negative");
            // Brighter and slightly longer than PlaySetupArmed but lighter than PlaySetupBell.
            // Long = 550 + 825 + 1100 (close to setup_long but slightly elevated). Short = mirror.
            if (side == OrderSide.Buy)
            {
                _sonificationManager.PlayNote(550,  0.55, "sine", 0.13f,        0f, force: true);
                _sonificationManager.PlayNote(825,  0.55, "sine", 0.13f * 0.85f, 0f, force: true);
                _sonificationManager.PlayNote(1100, 0.30, "sine", 0.13f * 0.5f,  0f, force: true);
            }
            else
            {
                _sonificationManager.PlayNote(275,  0.55, "triangle", 0.13f,        0f, force: true);
                _sonificationManager.PlayNote(183,  0.55, "triangle", 0.13f * 0.85f, 0f, force: true);
                _sonificationManager.PlayNote(138,  0.30, "sine",     0.13f * 0.5f,  0f, force: true);
            }
        }

        public void PlayOrderFill(OrderSide side)
        {
            if (!OrderEarconsAudible()) return;
            string key = $"order_fill_{(side == OrderSide.Buy ? "buy" : "sell")}";
            if (!CanPlay(key)) return;
            SignalVisual(side == OrderSide.Buy ? "Buy order filled" : "Sell order filled",
                side == OrderSide.Buy ? "positive" : "negative");
            if (side == OrderSide.Buy)
            {
                // Two staccato pickups then a sustained resolve a fourth up.
                _sonificationManager.PlayNote(587, 0.08, "sine", 0.12f, 0f, force: true);   // D5
                _sonificationManager.PlayNote(587, 0.08, "sine", 0.12f, 0f, force: true);   // D5
                _sonificationManager.PlayNote(784, 0.45, "sine", 0.13f, 0f, force: true);   // G5 sustained
            }
            else
            {
                _sonificationManager.PlayNote(294, 0.08, "triangle", 0.12f, 0f, force: true); // D4
                _sonificationManager.PlayNote(294, 0.08, "triangle", 0.12f, 0f, force: true); // D4
                _sonificationManager.PlayNote(196, 0.45, "triangle", 0.13f, 0f, force: true); // G3 sustained
            }
        }

        public void PlayStopHit()
        {
            if (!OrderEarconsAudible()) return;
            if (!CanPlay("stop_hit")) return;
            SignalVisual("Stop loss hit", "alert");
            // Urgent but orderly: low minor-third descent, square for bite — deliberately
            // shorter and cleaner than PlayError's dissonant beating pair.
            _sonificationManager.PlayNote(220, 0.18, "square", 0.16f, 0f, force: true); // A3
            _sonificationManager.PlayNote(185, 0.18, "square", 0.16f, 0f, force: true); // F#3
            _sonificationManager.PlayNote(147, 0.40, "square", 0.14f, 0f, force: true); // D3 sustained
        }

        public void PlayTakeProfitHit()
        {
            if (!OrderEarconsAudible()) return;
            if (!CanPlay("tp_hit")) return;
            SignalVisual("Take profit hit", "positive");
            // Bright major arpeggio up — the "win" sound.
            _sonificationManager.PlayNote(523,  0.10, "sine", 0.12f, 0f, force: true); // C5
            _sonificationManager.PlayNote(659,  0.10, "sine", 0.12f, 0f, force: true); // E5
            _sonificationManager.PlayNote(784,  0.10, "sine", 0.12f, 0f, force: true); // G5
            _sonificationManager.PlayNote(1046, 0.40, "sine", 0.13f, 0f, force: true); // C6 sustained
        }

        public void PlayConnectionState(ConnectionState state)
        {
            if (!AmbientEarconsAudible()) return;
            if (!CanPlay($"conn_{state}")) return;
            SignalVisual($"Connection: {state}",
                state == ConnectionState.Error || state == ConnectionState.Disconnected ? "alert" : "neutral");

            switch (state)
            {
                case ConnectionState.Connecting:
                    _sonificationManager.PlayNote(440, 0.05, "sine", 0.05f, 0, force: true);
                    break;
                case ConnectionState.Connected:
                    _sonificationManager.PlayNote(800, 0.2, "sine", 0.1f, 0, force: true);
                    break;
                case ConnectionState.Disconnected:
                    _sonificationManager.PlayNote(150, 0.3, "square", 0.1f, 0, force: true);
                    break;
                case ConnectionState.Error:
                    _sonificationManager.PlayNote(100, 0.5, "sawtooth", 0.2f, 0, force: true);
                    break;
            }
        }
    }
}
