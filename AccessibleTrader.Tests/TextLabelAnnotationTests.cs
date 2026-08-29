using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The Text Label tool, reported 2026-08-27: pressing the label chord opened the prompt,
    /// the wording was accepted — and the chart then read the CLOSE PRICE at the label's bar
    /// and played the price line at it. The wording went into the series NAME, which is spoken
    /// once on a series switch and nowhere else, so the one thing on the chart the terminal did
    /// not compute was the one thing it would not say.
    ///
    /// <para>
    /// These tests pin the three halves of the fix: the label reads its own wording wherever the
    /// cursor meets it (its own series and any other), it has a marker earcon instead of a tone,
    /// and it never speaks its anchor price. The anchor price is the trap — it is a real,
    /// plausible number, so a test that only asserts "the wording is spoken" stays green against
    /// the original defect if the price is spoken alongside it. Every speech assertion here also
    /// asserts the price is ABSENT.
    /// </para>
    /// </summary>
    public class TextLabelAnnotationTests
    {
        private const double AnchorClose = 43210.5;

        // ── Speech: the label's own series ───────────────────────────────────

        [Fact]
        public void LabelSeries_OnItsAnchorBar_ReadsTheWording_NotThePrice()
        {
            var label = LabelSeries("Sold half here", anchorIndex: 1);
            var msg = FormatFocused(label, currentIndex: 1);

            Assert.Equal("Label. Sold half here", msg);
            Assert.DoesNotContain("43,210", msg);
            Assert.DoesNotContain("43210", msg);
        }

        [Fact]
        public void LabelSeries_OffItsAnchorBar_SaysSoRatherThanNothing()
        {
            // A label is a single point, so most bars of its series are empty. "no data" on
            // every one of them is indistinguishable from a broken series; naming the label and
            // saying it is not here is what tells the user they have arrowed past it.
            var label = LabelSeries("Sold half here", anchorIndex: 1);
            var msg = FormatFocused(label, currentIndex: 0);

            Assert.Equal("Label. Sold half here, not on this bar", msg);
        }

        [Fact]
        public void LabelWithNoText_SaysSo_RatherThanReadingThePrice()
        {
            // Cancelling the prompt leaves the anchor in place with empty text — deliberate, so
            // a just-positioned label is not deleted by a cancel. The empty label must still be
            // identifiable by ear, and must still not read the price.
            var label = LabelSeries("", anchorIndex: 1);
            var msg = FormatFocused(label, currentIndex: 1);

            Assert.Equal("Label, no text", msg);
            Assert.DoesNotContain("43,210", msg);
        }

        [Fact]
        public void LabelStrategy_WinsOverEveryOtherStrategy()
        {
            // The label component is created through the same factory as any indicator
            // component, so it arrives as a Line with a speech template — the shape the
            // fallback StandardTemplateStrategy handles. TextLabelStrategy is registered first
            // for exactly this reason; if it is reordered below the fallback this fails with
            // the price back in the utterance.
            var label = LabelSeries("Break of structure", anchorIndex: 1);
            label.Components[0].SpeechTemplate = "{name} {value:F2}";
            label.Components[0].DisplayType = ComponentDisplayType.Line;

            var msg = FormatFocused(label, currentIndex: 1);
            Assert.Equal("Label. Break of structure", msg);
        }

        // ── Speech: crossing a label from another series ─────────────────────

        [Fact]
        public void ArrowingAcrossCandles_ReadsALabelPinnedToThatBar()
        {
            var label = LabelSeries("Sold half here", anchorIndex: 1);
            var (router, mgr) = BuildFeedback();
            var state = StateWith(CandleSeries(), label, focusedId: "candles", currentIndex: 1);

            mgr.HandleNavigationFeedback(state, isXMove: true, isYMove: false, prefixMessage: "");

            var spoken = CapturedSpeech(router);
            Assert.Contains("Label. Sold half here", spoken);
        }

        [Fact]
        public void ArrowingPastTheLabelledBar_SaysNothingAboutIt()
        {
            // The clause is per-BAR, not per-series: standing one bar away must be silent about
            // the label, or every bar of the chart carries every label on it.
            var label = LabelSeries("Sold half here", anchorIndex: 1);
            var (router, mgr) = BuildFeedback();
            var state = StateWith(CandleSeries(), label, focusedId: "candles", currentIndex: 0);

            mgr.HandleNavigationFeedback(state, isXMove: true, isYMove: false, prefixMessage: "");

            Assert.DoesNotContain("Sold half here", CapturedSpeech(router));
        }

        [Fact]
        public void AJumpOntoALabelledBar_StillReadsTheLabel()
        {
            // Ctrl+Left / Ctrl+Right is how a label off screen is found again. The zone and
            // marker clauses suppress themselves on a jump (the user is repositioning, not
            // reading); the label clause deliberately does not.
            var label = LabelSeries("Sold half here", anchorIndex: 1);
            var (router, mgr) = BuildFeedback();
            var state = StateWith(CandleSeries(), label, focusedId: "candles", currentIndex: 1);

            mgr.HandleNavigationFeedback(state, isXMove: true, isYMove: false, prefixMessage: "", isJump: true);

            Assert.Contains("Label. Sold half here", CapturedSpeech(router));
        }

        [Fact]
        public void TheLabelsOwnSeries_IsNotAnnouncedTwice()
        {
            // Focused on the label: the component strategy reads it. The cross-series clause
            // must exclude the focused series or the wording lands twice in one utterance.
            var label = LabelSeries("Sold half here", anchorIndex: 1);
            var (router, mgr) = BuildFeedback();
            var state = StateWith(CandleSeries(), label, focusedId: label.Id, currentIndex: 1);

            mgr.HandleNavigationFeedback(state, isXMove: true, isYMove: false, prefixMessage: "");

            var spoken = CapturedSpeech(router);
            // "Label. " and the price both matter here. The component's DisplayName is the
            // wording too, so a fallback template would also contain "Sold half here" — this
            // assertion has to be the strategy's exact phrase, or it passes against the defect.
            int first = spoken.IndexOf("Label. Sold half here", StringComparison.Ordinal);
            Assert.True(first >= 0, $"Label not spoken at all: '{spoken}'");
            Assert.Equal(first, spoken.LastIndexOf("Label. Sold half here", StringComparison.Ordinal));
            Assert.DoesNotContain("43,210", spoken);
        }

        [Fact]
        public void AMutedLabel_IsSilent()
        {
            var label = LabelSeries("Sold half here", anchorIndex: 1);
            label.IsMuted = true;
            var (router, mgr) = BuildFeedback();
            var state = StateWith(CandleSeries(), label, focusedId: "candles", currentIndex: 1);

            mgr.HandleNavigationFeedback(state, isXMove: true, isYMove: false, prefixMessage: "");

            Assert.DoesNotContain("Sold half here", CapturedSpeech(router));
        }

        // ── Audio: an earcon, and no tone ────────────────────────────────────

        [Fact]
        public void FocusedOnALabel_PlaysNoNavigationTone()
        {
            // The defect's audio half. The label's component array holds the anchor's close
            // price, so the generic path sonified it as a price line — a tone at the same pitch
            // the price series itself would make, at a bar where nothing was measured.
            var driver = new SpyDriver();
            var sonifier = new NavigationSonifier(driver, new NoOpSonification(), new SoundPatchRegistry());
            var label = LabelSeries("Sold half here", anchorIndex: 1);

            sonifier.SyncNavigationSlots(StateWith(CandleSeries(), label, focusedId: label.Id, currentIndex: 1));

            Assert.Empty(driver.NavNotes());
            Assert.Contains(0, driver.StoppedSlots);
        }

        [Fact]
        public void ArrivingOnALabelledBar_PlaysTheLabelEarcon()
        {
            var driver = new SpyDriver();
            var sonifier = new NavigationSonifier(driver, new NoOpSonification(), new SoundPatchRegistry());
            var label = LabelSeries("Sold half here", anchorIndex: 1);

            sonifier.SyncNavigationSlots(StateWith(CandleSeries(), label, focusedId: "candles", currentIndex: 1));

            // The leading note lands synchronously, on a UI slot, well above the price
            // register — the point of the sound is that it cannot be mistaken for the chart.
            var ui = driver.UiNotes();
            Assert.Single(ui);
            Assert.True(ui[0].Frequency > 1000, $"{ui[0].Frequency} Hz is inside the data register");
            Assert.True(ui[0].Slot >= 16, "earcons must not write a navigation slot");
        }

        [Fact]
        public async Task TheEarconIsATwoNoteFigure()
        {
            // The partner note is scheduled, not immediate — a two-note tick has to be two
            // notes in TIME or it is a chord. Awaited rather than measured: the assertion is
            // that the second note arrives at all, never how long it took.
            var driver = new SpyDriver();
            var sonifier = new NavigationSonifier(driver, new NoOpSonification(), new SoundPatchRegistry());
            var label = LabelSeries("Sold half here", anchorIndex: 1);

            sonifier.SyncNavigationSlots(StateWith(CandleSeries(), label, focusedId: "candles", currentIndex: 1));
            await WaitForUiNotes(driver, 2);

            var ui = driver.UiNotes();
            Assert.Equal(2, ui.Count);
            Assert.NotEqual(ui[0].Frequency, ui[1].Frequency);
            Assert.All(ui, c => Assert.True(c.Frequency > 1000));
        }

        [Fact]
        public void TheEarconDoesNotRepeatWhileStandingOnTheLabel()
        {
            // Navigation state is pushed on every change, not only on an X move — component
            // moves, a live tick, a visibility toggle. Without the arrival guard the earcon
            // machine-guns while the user reads the bar.
            var driver = new SpyDriver();
            var sonifier = new NavigationSonifier(driver, new NoOpSonification(), new SoundPatchRegistry());
            var label = LabelSeries("Sold half here", anchorIndex: 1);
            var onLabel = StateWith(CandleSeries(), label, focusedId: "candles", currentIndex: 1);

            sonifier.SyncNavigationSlots(onLabel);
            sonifier.SyncNavigationSlots(onLabel);
            sonifier.SyncNavigationSlots(onLabel);

            Assert.Single(Arrivals(driver.UiNotes()));
        }

        [Fact]
        public void LeavingAndReturningPlaysTheEarconAgain()
        {
            // The other half of the guard: an arrival is an arrival. A "fire once ever" guard
            // would make a label audible exactly once per session.
            var driver = new SpyDriver();
            var sonifier = new NavigationSonifier(driver, new NoOpSonification(), new SoundPatchRegistry());
            var label = LabelSeries("Sold half here", anchorIndex: 1);
            var candles = CandleSeries();

            sonifier.SyncNavigationSlots(StateWith(candles, label, focusedId: "candles", currentIndex: 1));
            sonifier.SyncNavigationSlots(StateWith(candles, label, focusedId: "candles", currentIndex: 0));
            sonifier.SyncNavigationSlots(StateWith(candles, label, focusedId: "candles", currentIndex: 1));

            Assert.Equal(2, Arrivals(driver.UiNotes()).Count);
        }

        [Fact]
        public void AnUnlabelledBar_PlaysNoEarcon()
        {
            var driver = new SpyDriver();
            var sonifier = new NavigationSonifier(driver, new NoOpSonification(), new SoundPatchRegistry());
            var label = LabelSeries("Sold half here", anchorIndex: 1);

            sonifier.SyncNavigationSlots(StateWith(CandleSeries(), label, focusedId: "candles", currentIndex: 0));

            Assert.Empty(driver.UiNotes());
        }

        [Fact]
        public void TheEarconObeysTheEarconMute()
        {
            // Shift+F3 mutes ambient earcons. The label earcon is ambient — it is a chart
            // annotation, not a failure — so it gates, exactly like every other one.
            var driver = new SpyDriver();
            var sonifier = new NavigationSonifier(driver, new NoOpSonification(), new SoundPatchRegistry());
            var label = LabelSeries("Sold half here", anchorIndex: 1);
            var state = StateWith(CandleSeries(), label, focusedId: "candles", currentIndex: 1)
                with { IsEarconsEnabled = false };

            sonifier.SyncNavigationSlots(state);

            Assert.Empty(driver.UiNotes());
        }

        // ── Harness ──────────────────────────────────────────────────────────

        /// <summary>
        /// A Text Label drawing series shaped exactly as <c>DrawingInteractionManager</c> builds
        /// it: one component named "Label" whose array holds the ANCHOR'S CLOSE PRICE at the
        /// anchor bar and NaN everywhere else. The price in the array is the whole point — it is
        /// what the old code read out.
        /// </summary>
        private static ChartSeries LabelSeries(string text, int anchorIndex)
        {
            var cfg = new SeriesConfig
            {
                Id = "label-1",
                Name = string.IsNullOrEmpty(text) ? "Label (1)" : $"Label: {text}",
                Pane = "Main",
            };
            cfg.Components.Add(new ComponentConfig
            {
                Name = "Label",
                DisplayName = string.IsNullOrEmpty(text) ? "Label" : text,
                DisplayType = ComponentDisplayType.Line,
                IsVisible = true,
                Volume = 1.0f,
                Waveform = "sine",
                BaseFrequency = 440,
                FreqMultiplier = 1.0,
            });

            var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
            var arr = new[] { double.NaN, double.NaN, double.NaN };
            arr[anchorIndex] = AnchorClose;
            buf.ComponentData["Label"] = arr;

            return new ChartSeries(cfg, buf)
            {
                Drawing = new DrawingData
                {
                    Type = DrawingType.TextLabel,
                    AnchorDate1 = BarAt(anchorIndex).Date,
                    AnchorPrice1 = AnchorClose,
                    Text = text,
                },
            };
        }

        private static ChartSeries CandleSeries()
        {
            var cfg = new SeriesConfig { Id = "candles", Name = "Candles", Pane = "Main" };
            cfg.Components.Add(new ComponentConfig
            {
                Name = "Close",
                DisplayName = "Close",
                DisplayType = ComponentDisplayType.Candle,
                IsVisible = true,
                Volume = 1.0f,
                Waveform = "sine",
                BaseFrequency = 440,
                FreqMultiplier = 1.0,
            });
            var buf = new SeriesDataBuffer { SeriesId = "candles" };
            buf.ComponentData["Close"] = new[] { AnchorClose, AnchorClose, AnchorClose };
            return new ChartSeries(cfg, buf);
        }

        private static Ohlcv BarAt(int i)
            => new Ohlcv(new DateTime(2026, 08, 27, 9, 30, 0, DateTimeKind.Utc).AddMinutes(i),
                         AnchorClose, AnchorClose, AnchorClose, AnchorClose, 1000);

        private static WorkspaceState StateWith(ChartSeries candles, ChartSeries label, string focusedId, int currentIndex)
        {
            var bars = new List<Ohlcv> { BarAt(0), BarAt(1), BarAt(2) };
            return WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(bars),
                CurrentDataIndex = currentIndex,
                ActiveSeries = ImmutableList.Create(candles, label),
                FocusedSeriesId = focusedId,
                FocusedComponentIndex = 0,
                LastInteractionContext = InteractionContext.Component,
                SpeakTimestamps = false,
                ViewportStartIndex = 0,
                ViewportLength = 3,
                ChartVolume = 1.0f,
                PaneRanges = ImmutableDictionary<string, (double Min, double Max)>.Empty.Add("Main", (0, 100000)),
                ViewportRange = (0, 100000),
            };
        }

        /// <summary>Component-context speech for the focused series, prefix-free.</summary>
        private static string FormatFocused(ChartSeries label, int currentIndex)
        {
            var state = StateWith(CandleSeries(), label, focusedId: label.Id, currentIndex: currentIndex);
            return new SpeechFormatter()
                .FormatPointFeedback(state, isXMove: true, isYMove: false, label, BarAt(currentIndex), prefixMessage: "");
        }

        private static (ISpeechFeedbackRouter Router, NavigationFeedbackManager Manager) BuildFeedback()
        {
            var router = Substitute.For<ISpeechFeedbackRouter>();
            return (router, new NavigationFeedbackManager(router, new SpeechFormatter()));
        }

        /// <summary>Everything the manager handed the router, joined — the utterance is
        /// composed and spoken once, so this is normally a single call.</summary>
        private static string CapturedSpeech(ISpeechFeedbackRouter router)
            => string.Join(" | ", router.ReceivedCalls()
                .Where(c => c.GetMethodInfo().Name == nameof(ISpeechFeedbackRouter.Speak))
                .Select(c => c.GetArguments()[0] as string ?? ""));

        /// <summary>
        /// Polls until the driver has seen <paramref name="count"/> UI notes, or gives up.
        /// Bounded by attempts rather than by a stopwatch: this asserts that a scheduled note
        /// ARRIVES, never how long the scheduler took — the repo rule is that audio is never
        /// timed against the wall clock.
        /// </summary>
        /// <summary>
        /// One entry per ARRIVAL, not per note — which is what the two guards either side of the
        /// figure are actually about.
        ///
        /// <para>
        /// The earcon is two notes and the second is scheduled 55 ms out, so a raw note count is
        /// a stopwatch: "one arrival" reads as 1 note before the partner lands and 2 after, and
        /// "two arrivals" as 2, 3 or 4. Both counted raw until 2026-08-29, when
        /// <c>LeavingAndReturningPlaysTheEarconAgain</c> caught the middle value on a loaded
        /// Debug run — expected 2, got 3 — and a 300 ms settle before the assertion turned it
        /// into a deterministic 4. **They were passing on the partner note being late, not on the
        /// guard being right.**
        /// </para>
        ///
        /// <para>
        /// The arrival note is the one played synchronously, so it is always the first recorded,
        /// and every arrival plays it at the same pitch: counting notes at that pitch counts
        /// arrivals no matter which partners have landed. Taken from the recording rather than
        /// from the sonifier's constants deliberately — reading the pitch back would make this
        /// agree with any value it takes. It relies on the two notes of a figure having different
        /// pitches, which is the property <c>TheEarconIsATwoNoteFigure</c> asserts
        /// directly, so a change that broke this assumption fails there first.
        /// </para>
        /// </summary>
        private static List<VoiceCall> Arrivals(List<VoiceCall> uiNotes) =>
            uiNotes.Count == 0
                ? uiNotes
                : uiNotes.Where(c => c.Frequency == uiNotes[0].Frequency).ToList();

        private static async Task WaitForUiNotes(SpyDriver driver, int count)
        {
            for (int i = 0; i < 200 && driver.UiNotes().Count < count; i++)
                await Task.Delay(25);
        }

        private sealed record VoiceCall(int Slot, double Frequency, float Volume, float Pan);

        private sealed class SpyDriver : IAudioDriver
        {
            public int SampleRate => 48000;
            public int Channels => 2;
#pragma warning disable CS0067
            public event Action<int>? PointReached;
#pragma warning restore CS0067

            private readonly object _gate = new();
            private readonly List<VoiceCall> _calls = new();
            public List<int> StoppedSlots { get; } = new();

            /// <summary>UI-slot notes (16+) — the earcon channel. Snapshotted under the lock
            /// because the second note of the figure arrives on a scheduler thread.</summary>
            public List<VoiceCall> UiNotes()
            {
                lock (_gate) return _calls.Where(c => c.Slot >= 16).ToList();
            }

            /// <summary>Navigation-slot notes (0-15) — the tone channel a label must not use.</summary>
            public List<VoiceCall> NavNotes()
            {
                lock (_gate) return _calls.Where(c => c.Slot < 16).ToList();
            }

            public void SetVoice(int slot, double frequency, float volume, float pan, string waveform,
                bool continuous, double durationSeconds = 0.2, int dataIndex = -1, string envelope = "Sustain",
                bool click = false, float noiseAmount = 0f, string noiseType = "pink", float squareMix = 0f,
                float sawMix = 0f, float triangleMix = 0f, float subSawMix = 0f)
            {
                lock (_gate) _calls.Add(new VoiceCall(slot, frequency, volume, pan));
            }

            public void StopVoice(int slot) { lock (_gate) StoppedSlots.Add(slot); }
            public void StopAll() { }
            public void Reset() { }
            public void SetMasterGain(float gain) { }
            public void Pause() { }
            public void Resume() { }
        }

        private sealed class NoOpSonification : ISonificationStrategy
        {
            public AudioPoint CreateAudioPoint(ChartSeries series, ComponentConfig comp, double val, Ohlcv point,
                int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, float chartVolume, double? prevVal = null)
                => new AudioPoint(comp.BaseFrequency, 1.0f, comp.Waveform, 0.2, "Sustain");

            public AudioPoint MapToAudio(ChartSeries series, int dataIndex, List<Ohlcv> data, int relativeIndex,
                int viewportWidth, (double Min, double Max) viewportRange, float chartVolume)
                => new AudioPoint(440, 1, "sine", 0.2, "Sustain");

            public AudioPoint MapComponentToAudio(ChartSeries series, int componentIndex, int dataIndex, List<Ohlcv> data,
                int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, float chartVolume)
                => new AudioPoint(440, 1, "sine", 0.2, "Sustain");

            public int ResolveComponentVoiceCount(ComponentConfig comp) => 1;
        }
    }
}
