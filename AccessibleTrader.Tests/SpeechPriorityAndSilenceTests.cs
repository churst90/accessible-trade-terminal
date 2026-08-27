using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>An arrow key does not talk over an order rejection.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// There was no speech priority anywhere. <c>ISpeechManager</c> exposes only
    /// <c>Speak(string, bool interrupt)</c> — no priority, no queue, no politeness level — and
    /// <c>interrupt</c> defaults to <b>true</b> on <c>SpeechFeedbackRouter</c>, whose
    /// subscription implements it as <c>Silence()</c> then <c>Speak(msg, true)</c>.
    /// <c>SpeechChannel</c> is a <i>mute tier</i>, not a priority: <c>IsChannelAudible</c> only
    /// decides whether to emit at all.
    /// </para>
    ///
    /// <para>
    /// So <c>OrderRejectedEvent</c> speaks "Order rejected for BTCUSDT. Insufficient balance."
    /// on the OrderEvent channel with <c>interrupt: true</c>, and the user's next arrow key
    /// ~200 ms later calls <c>Speak(barReading, interrupt: true)</c> on the Manual channel —
    /// which calls <c>Silence()</c> and truncates the rejection mid-word. <b>The user hears
    /// "Order rejec—" and a price.</b> Key-repeat on the arrow keys is the normal way this
    /// terminal is read, so this is the common case rather than an edge one.
    /// </para>
    /// </summary>
    public class SpeechPriorityAndSilenceTests
    {
        private sealed class SpySpeech : ISpeechManager
        {
            public bool IsSpeechEnabled { get; set; } = true;
            public Action<string>? OnSpeak { get; set; }
            public readonly List<string> Calls = new();

            public void Speak(string text, bool interrupt = false)
                => Calls.Add($"{(interrupt ? "INTERRUPT" : "QUEUE")}:{text}");

            public void Silence() => Calls.Add("SILENCE");
        }

        private static (SpeechFeedbackRouter Router, SpySpeech Speech) Build()
        {
            var speech = new SpySpeech();
            var store = Substitute.For<IWorkspaceStore>();
            store.State.Returns(WorkspaceState.Initial with
            {
                IsSpeechEnabled = true,
                IsEventSpeechEnabled = true,
            });
            var router = new SpeechFeedbackRouter(
                speech, Substitute.For<ISpeechFormatter>(), store);
            return (router, speech);
        }

        [Fact]
        public void An_arrow_key_does_not_silence_an_order_rejection_still_in_flight()
        {
            var (router, speech) = Build();

            router.Speak("Order rejected for BTCUSDT. Insufficient balance.",
                interrupt: true, SpeechChannel.OrderEvent);
            speech.Calls.Clear();

            // The next keystroke, immediately.
            router.Speak("61,240. 14:05.", interrupt: true, SpeechChannel.Manual);

            Assert.DoesNotContain("SILENCE", speech.Calls);
            Assert.Contains(speech.Calls, c => c.StartsWith("QUEUE:", StringComparison.Ordinal));
        }

        [Fact]
        public void An_error_is_never_talked_over_by_navigation()
        {
            var (router, speech) = Build();

            router.Speak("Cannot delete the candlestick series.",
                interrupt: true, SpeechChannel.Critical);
            speech.Calls.Clear();

            router.Speak("61,240.", interrupt: true, SpeechChannel.Manual);

            Assert.DoesNotContain("SILENCE", speech.Calls);
        }

        [Fact]
        public void A_second_order_outcome_does_supersede_the_first()
        {
            // Equal priority still interrupts. A newer fill genuinely replaces an older one,
            // and a fix that queued everything would make the user wait through stale news.
            var (router, speech) = Build();

            router.Speak("Order placed.", interrupt: true, SpeechChannel.OrderEvent);
            speech.Calls.Clear();

            router.Speak("Order filled at 61,240.", interrupt: true, SpeechChannel.OrderEvent);

            Assert.Contains("SILENCE", speech.Calls);
        }

        [Fact]
        public void A_higher_priority_message_interrupts_a_lower_one()
        {
            // The direction that must keep working: an order rejection arriving during a bar
            // reading is exactly what interrupt is for.
            var (router, speech) = Build();

            router.Speak("61,240. 14:05.", interrupt: true, SpeechChannel.Manual);
            speech.Calls.Clear();

            router.Speak("Order rejected.", interrupt: true, SpeechChannel.OrderEvent);

            Assert.Contains("SILENCE", speech.Calls);
        }

        [Fact]
        public void Navigation_still_interrupts_navigation()
        {
            // Key-repeat on the arrow keys MUST stay responsive. A guard that made every
            // keystroke queue behind the last one would turn a fast scroll into a backlog the
            // user has to sit through, which is a worse bug than the one being fixed.
            var (router, speech) = Build();

            router.Speak("61,240.", interrupt: true, SpeechChannel.Manual);
            speech.Calls.Clear();

            router.Speak("61,250.", interrupt: true, SpeechChannel.Manual);

            Assert.Contains("SILENCE", speech.Calls);
        }

        [Fact]
        public void Protection_expires_so_a_stale_message_cannot_lock_out_navigation()
        {
            var (router, speech) = Build();

            router.Speak("Order rejected.", interrupt: true, SpeechChannel.OrderEvent);
            router.ResetSpeechPriorityForTests();   // as if its estimated duration had elapsed
            speech.Calls.Clear();

            router.Speak("61,240.", interrupt: true, SpeechChannel.Manual);

            Assert.Contains("SILENCE", speech.Calls);
        }

        [Fact]
        public void A_suppressed_interrupt_is_still_SPOKEN()
        {
            // Not interrupting must never mean not saying. The bar reading is still the
            // answer to a keypress and has to arrive, just behind the rejection.
            var (router, speech) = Build();

            router.Speak("Order rejected.", interrupt: true, SpeechChannel.OrderEvent);
            speech.Calls.Clear();

            router.Speak("61,240.", interrupt: true, SpeechChannel.Manual);

            Assert.Contains(speech.Calls, c => c.EndsWith("61,240.", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// <b>Support and resistance are decided by where the level is, not by how it sounds.</b>
    ///
    /// <para><c>NavigationFeedbackManager.DescribeZoneProximity</c> classified a zone with
    /// <c>if ((float)comp.BaseFrequency &gt;= 500f)</c> → resistance, else support.
    /// <c>BaseFrequency</c> is a SONIFICATION setting, so a zone line whose tone was chosen for
    /// audibility rather than semantics was announced as the <b>opposite structural level</b>.
    /// "Near resistance at X" versus "near support at X" is a directional claim a trader acts
    /// on, and the magic 500 was undocumented.</para>
    /// </summary>
    public class ZoneProximityClassificationTests
    {
        private static ChartSeries ZoneSeries(string id, double level, double baseFrequency)
        {
            var config = new SeriesConfig { Id = id, Name = id, IndicatorCode = "ZONES" };
            config.Components.Add(new ComponentConfig
            {
                Name = "Level",
                DisplayName = "Level",
                IsZoneLine = true,
                BaseFrequency = baseFrequency,
            });
            var buffer = new SeriesDataBuffer { SeriesId = id };
            buffer.ComponentData["Level"] = new[] { level };
            return new ChartSeries(config, buffer);
        }

        private static List<string> Describe(params ChartSeries[] series)
        {
            // Price bar centred at 100 with a wide enough range that both levels are "near".
            var bars = new TimeSeriesBuffer<Ohlcv>(new[]
            {
                new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100, 106, 94, 100, 10),
            });
            var state = WorkspaceState.Initial with
            {
                Data = bars,
                CurrentDataIndex = 0,
                ActiveSeries = ImmutableList.CreateRange(series),
            };

            var m = typeof(NavigationFeedbackManager).GetMethod(
                "DescribeZoneProximity",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            return (List<string>)m.Invoke(null, new object[] { state, 0 })!;
        }

        [Fact]
        public void A_level_above_price_is_resistance_however_it_is_voiced()
        {
            // BaseFrequency 100 is well under the old magic 500, so this level used to be
            // announced as SUPPORT while sitting above the price.
            var clauses = Describe(ZoneSeries("above", level: 105, baseFrequency: 100));

            Assert.Contains(clauses, c => c.Contains("resistance", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(clauses, c => c.Contains("support", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void A_level_below_price_is_support_however_it_is_voiced()
        {
            // BaseFrequency 900 is over the old threshold, so this used to be RESISTANCE
            // while sitting below the price.
            var clauses = Describe(ZoneSeries("below", level: 95, baseFrequency: 900));

            Assert.Contains(clauses, c => c.Contains("support", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(clauses, c => c.Contains("resistance", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Both_sides_are_reported_when_price_sits_between_two_levels()
        {
            var clauses = Describe(
                ZoneSeries("above", level: 105, baseFrequency: 100),
                ZoneSeries("below", level: 95, baseFrequency: 900));

            Assert.Contains(clauses, c => c.Contains("resistance", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(clauses, c => c.Contains("support", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void The_fixture_frequencies_straddle_the_old_threshold()
        {
            // Vacuity check. If both fixtures sat on the same side of 500, the old
            // frequency-based classifier and the new price-based one would agree and these
            // tests would prove nothing.
            const float oldThreshold = 500f;
            Assert.True(100f < oldThreshold);
            Assert.True(900f >= oldThreshold);
        }
    }
}
