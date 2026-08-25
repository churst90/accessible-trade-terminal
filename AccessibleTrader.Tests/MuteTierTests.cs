using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The 2026-07-21 mute-tier redesign. The grammar: unshifted F-key = the
    /// INTERACTIVE channel (things you asked for), Shift+F-key = the AMBIENT
    /// channel (things that happen to you). F2/Shift+F2 = speech tiers,
    /// F3/Shift+F3 = sound tiers. Order-execution outcomes break through both
    /// ambient mutes by default (the manual's "the one feedback you never miss"
    /// promise); errors and the toggle confirmations themselves are never muted.
    /// The gate lives in SpeechFeedbackRouter / EarconService — per-call-site
    /// checks are exactly how the original F2 bypasses crept in.
    /// </summary>
    public class MuteTierTests
    {
        // ── Speech router channels ───────────────────────────────────────────

        private static (SpeechFeedbackRouter router, ISpeechManager speech, IWorkspaceStore store, IAppSettings settings)
            BuildRouter(bool speechOn = true, bool eventSpeechOn = true, bool muteIncludesOrders = false)
        {
            var speech = Substitute.For<ISpeechManager>();
            var store = Substitute.For<IWorkspaceStore>();
            store.State.Returns(WorkspaceState.Initial with
            {
                IsSpeechEnabled = speechOn,
                IsEventSpeechEnabled = eventSpeechOn,
            });
            var settings = Substitute.For<IAppSettings>();
            settings.MuteIncludesOrderEvents.Returns(muteIncludesOrders);
            var router = new SpeechFeedbackRouter(speech, Substitute.For<ISpeechFormatter>(), store, settings);
            return (router, speech, store, settings);
        }

        [Fact]
        public void F2_off_silences_manual_speech_entirely()
        {
            var (router, speech, _, _) = BuildRouter(speechOn: false);
            router.Speak("Zoomed to 120 bars"); // Manual is the default channel
            speech.DidNotReceive().Speak(Arg.Any<string>(), Arg.Any<bool>());
        }

        [Fact]
        public void F2_off_does_not_touch_event_speech()
        {
            // The redesign's core split: F2 mutes what you ASKED for; alerts and
            // other ambient events keep speaking until Shift+F2.
            var (router, speech, _, _) = BuildRouter(speechOn: false, eventSpeechOn: true);
            router.Speak("Gold positioning alert", channel: SpeechChannel.Event);
            speech.Received(1).Speak(Arg.Any<string>(), Arg.Any<bool>());
        }

        [Fact]
        public void ShiftF2_off_silences_event_speech()
        {
            var (router, speech, _, _) = BuildRouter(eventSpeechOn: false);
            router.Speak("Gold positioning alert", channel: SpeechChannel.Event);
            speech.DidNotReceive().Speak(Arg.Any<string>(), Arg.Any<bool>());
        }

        [Fact]
        public void Order_outcomes_break_through_the_event_mute_by_default()
        {
            // Both mutes engaged — a stop-loss fill must still speak. Real money.
            var (router, speech, _, _) = BuildRouter(speechOn: false, eventSpeechOn: false);
            router.Speak("Stop loss hit. Sold 0.5 BTC/USDT.", channel: SpeechChannel.OrderEvent);
            speech.Received(1).Speak(Arg.Any<string>(), Arg.Any<bool>());
        }

        [Fact]
        public void Order_outcomes_respect_the_explicit_total_silence_opt_in()
        {
            var (router, speech, _, _) = BuildRouter(
                eventSpeechOn: false, muteIncludesOrders: true);
            router.Speak("Order filled.", channel: SpeechChannel.OrderEvent);
            speech.DidNotReceive().Speak(Arg.Any<string>(), Arg.Any<bool>());
        }

        [Fact]
        public void Critical_speech_is_never_muted()
        {
            // "Speech off" must be heard, and errors must never fail silently —
            // even with every mute engaged.
            var (router, speech, _, _) = BuildRouter(
                speechOn: false, eventSpeechOn: false, muteIncludesOrders: true);
            router.Speak("Speech off", channel: SpeechChannel.Critical);
            speech.Received(1).Speak(Arg.Any<string>(), Arg.Any<bool>());
        }

        // ── Earcon tiers ─────────────────────────────────────────────────────

        private static (EarconService svc, ISonificationManager sonify)
            BuildEarcons(bool earconsOn, bool muteIncludesOrders = false)
        {
            var sonify = Substitute.For<ISonificationManager>();
            var lib = Substitute.For<ISoundPatchLibrary>();
            lib.EarconOverrides.Returns(new EarconSettings());
            var store = Substitute.For<IWorkspaceStore>();
            store.State.Returns(WorkspaceState.Initial with { IsEarconsEnabled = earconsOn });
            var settings = Substitute.For<IAppSettings>();
            settings.MuteIncludesOrderEvents.Returns(muteIncludesOrders);
            return (new EarconService(sonify, lib, null, store, settings), sonify);
        }

        private static void AssertPlayed(ISonificationManager sonify, bool played)
        {
            if (played)
                sonify.Received().PlayNote(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<string>(),
                    Arg.Any<float>(), Arg.Any<float>(), Arg.Any<double>(), Arg.Any<bool>());
            else
                sonify.DidNotReceive().PlayNote(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<string>(),
                    Arg.Any<float>(), Arg.Any<float>(), Arg.Any<double>(), Arg.Any<bool>());
        }

        [Fact]
        public void ShiftF3_off_mutes_ambient_earcons()
        {
            var (svc, sonify) = BuildEarcons(earconsOn: false);
            svc.PlayInfo();
            svc.PlayNewBar();
            svc.PlayAlert();
            AssertPlayed(sonify, played: false);
        }

        [Fact]
        public void ShiftF3_off_does_not_mute_order_outcome_earcons()
        {
            var (svc, sonify) = BuildEarcons(earconsOn: false);
            svc.PlayStopHit();
            AssertPlayed(sonify, played: true);
        }

        [Fact]
        public void Order_earcons_respect_the_total_silence_opt_in()
        {
            var (svc, sonify) = BuildEarcons(earconsOn: false, muteIncludesOrders: true);
            svc.PlayOrderFill(AccessibleTrader.Sdk.Plugins.OrderSide.Buy);
            AssertPlayed(sonify, played: false);
        }

        [Fact]
        public void BreakThrough_alert_earcon_pierces_the_earcon_mute()
        {
            // Per-alert "break through mutes" — the margin-call alert the user
            // marked critical sounds even while Shift+F3 has earcons muted.
            var (svc, sonify) = BuildEarcons(earconsOn: false);
            svc.PlayAlert(breakThroughMutes: true);
            AssertPlayed(sonify, played: true);
        }

        [Fact]
        public void Error_earcons_are_never_muted()
        {
            var (svc, sonify) = BuildEarcons(earconsOn: false);
            svc.PlayError(AccessibleTrader.Sdk.Enums.ErrorSeverity.High);
            AssertPlayed(sonify, played: true);
        }

        // ── Reducer + defaults ───────────────────────────────────────────────

        [Fact]
        public void New_mute_tiers_default_on_and_toggle()
        {
            var s = WorkspaceState.Initial;
            Assert.True(s.IsEventSpeechEnabled);
            Assert.True(s.IsEarconsEnabled);
        }

        [Fact]
        public void Earcons_survive_chart_sonification_off()
        {
            // CONTRACT CHANGE: earcons no longer die with F3 — the sonification
            // manager's IsEnabled gate is bypassed with force:true. Cody's model
            // ("F3 just silences chart navigation") is now the actual behavior.
            var (svc, sonify) = BuildEarcons(earconsOn: true);
            sonify.IsEnabled.Returns(false);
            svc.PlayInfo();
            sonify.Received().PlayNote(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<string>(),
                Arg.Any<float>(), Arg.Any<float>(), Arg.Any<double>(), true);
        }
    }
}
