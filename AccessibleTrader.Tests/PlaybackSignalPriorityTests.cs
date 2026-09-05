using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// WHICH signal playback speaks when more of them fire on one bar than an utterance can carry.
    ///
    /// <para>
    /// <b>The defect.</b> Cody, 2026-09-05: <i>"market structure, cipher sr all read fine and
    /// everything reads fine for cipher b like the wavetrend and bull/bear crosses just not the
    /// tripple"</i>. Cipher B's Triple Confluence Buy is computed INSIDE the Oversold Crossover
    /// branch, which is itself inside the WaveTrend cross branch (<c>CipherBProvider</c>, the
    /// <c>crossUp</c> loop) — so a gold bar is ALWAYS also a blue bar and a cross bar. The scan
    /// walked components in declaration order and stopped at the two-clause ceiling, and the
    /// provider declares the routine markers first. The rarest signal Cipher B can print was
    /// therefore the one signal playback could never say — not intermittently, but on every gold
    /// dot in every playback since the feature shipped.
    /// </para>
    ///
    /// <para>
    /// <b>The rule that replaces declaration order.</b> The rarest marker leads and the commonest
    /// is what gets dropped, measured by how often each actually fired across the loaded range
    /// (<c>PlaybackNarration.FireCount</c>). An indicator's consequential calls are its rare ones;
    /// the ones it prints every other cycle are its commentary.
    /// </para>
    /// </summary>
    public class PlaybackSignalPriorityTests
    {
        // ── Fixture: Cipher B's shape, and its declaration order ────────────────────

        private const int Bars = 400;

        /// <summary>
        /// A series whose components are declared commonest-first, exactly as
        /// <c>CipherBProvider</c> declares them, each firing on <paramref name="goldBar"/> plus
        /// however many other bars it takes to reach its own frequency.
        /// </summary>
        private static ChartSeries CipherBShaped(int goldBar, bool crossFires = true)
        {
            var cfg = new SeriesConfig
            {
                Id = "cipher_b",
                IndicatorCode = "CIPHER_B",
                Name = "Cipher B",
                FriendlyName = "Cipher B",
                IsAutoNarrated = true,
                IsVisible = true,
                IsMuted = false,
            };

            var buf = new SeriesDataBuffer { SeriesId = cfg.Id };

            void Marker(string name, string template, int every, bool firesOnGoldBar)
            {
                cfg.Components.Add(new ComponentConfig
                {
                    Name = name,
                    DisplayType = ComponentDisplayType.Dot,
                    IsVisible = true,
                    IsMuted = false,
                    SignalSpeechTemplate = template,
                });

                var data = new double[Bars];
                Array.Fill(data, double.NaN);
                for (int i = every; i < Bars; i += every) data[i] = 1.0;
                data[goldBar] = firesOnGoldBar ? 1.0 : double.NaN;
                buf.ComponentData[name] = data;
            }

            // Declaration order is the order the defect depended on: routine markers first.
            Marker("WaveTrend Cross Bull", "Wave cross up", every: 13, firesOnGoldBar: crossFires); // ~30 fires
            Marker("Oversold Crossover", "Oversold crossover, long signal", every: 50, firesOnGoldBar: true); // ~8
            Marker("Triple Confluence Buy", "Triple confluence buy, strong confirmation", every: 399, firesOnGoldBar: true); // 2

            return new ChartSeries(cfg, buf);
        }

        private static WorkspaceState ChartOf(ChartSeries series) => WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, Bars).Select(i =>
                new Ohlcv(new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddDays(i),
                          100 + i, 101 + i, 99 + i, 100 + i, 1000))),
            ActiveSeries = ImmutableList.Create(series),
            PrimarySeriesId = series.Id,
            FocusedSeriesId = series.Id,
            Identity = new ChartIdentity("Spot", "Test", "BTC/USD", "1d"),
            NarrateDuringPlayback = true,
        };

        // ── The report ──────────────────────────────────────────────────────────────

        [Fact]
        public void TheGoldDot_IsSpoken_OnTheBarWhereAllThreeMarkersFire()
        {
            // The whole report in one assertion. Bar 100 carries all three; before the fix the
            // two commonest filled the ceiling and this was null of any mention of the gold dot.
            string? spoken = PlaybackNarration.SignalsForStep(ChartOf(CipherBShaped(100)), 100);

            Assert.NotNull(spoken);
            Assert.Contains("Triple confluence buy", spoken, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TheCommonestMarker_IsWhatGetsDropped()
        {
            // The other half: the ceiling still holds at two clauses, and what falls off the end
            // is the WaveTrend cross — the thing Cipher B prints thirty times in four hundred
            // bars — not the thing it printed twice.
            string? spoken = PlaybackNarration.SignalsForStep(ChartOf(CipherBShaped(100)), 100);

            Assert.NotNull(spoken);
            Assert.DoesNotContain("Wave cross up", spoken, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Oversold crossover", spoken, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TheRarestMarkerLeads()
        {
            // Ordering is not cosmetic at ten bars a second: the first clause is the one that
            // lands before the next bar's tones start.
            string spoken = PlaybackNarration.SignalsForStep(ChartOf(CipherBShaped(100)), 100)!;

            int gold = spoken.IndexOf("Triple confluence", StringComparison.OrdinalIgnoreCase);
            int blue = spoken.IndexOf("Oversold crossover", StringComparison.OrdinalIgnoreCase);

            Assert.True(gold >= 0 && blue > gold,
                $"the rarest marker must lead the utterance — got \"{spoken}\"");
        }

        [Fact]
        public void ARoutineMarkerFiringAlone_IsStillSpoken()
        {
            // The vacuity partner for the two above. Ranking is not a filter: a build that had
            // simply stopped speaking WaveTrend crosses would satisfy "the commonest is dropped"
            // and be a worse terminal than the one with the bug. Bar 13 is a cross and nothing
            // else.
            string? spoken = PlaybackNarration.SignalsForStep(ChartOf(CipherBShaped(100)), 13);

            // The component leads because this template does not name it — SignalClauseSpeech.
            Assert.Equal("WaveTrend Cross Bull: Wave cross up.", spoken);
        }

        [Fact]
        public void TheRarityOfWhatWasSaid_TravelsWithIt()
        {
            // The rate limit reads this. It is the fire count of the RAREST clause spoken, not
            // of the whole series and not of the first component scanned.
            var step = PlaybackNarration.SignalStepFor(ChartOf(CipherBShaped(100)), 100);

            Assert.NotNull(step.Text);
            Assert.Equal(2, step.RarestFireCount);   // bars 100 and 399
        }

        [Fact]
        public void ABarWithNoMarker_ClaimsNoRarityAtAll()
        {
            // int.MaxValue, not 0: an utterance carrying no signal must not be able to silence
            // one. Bar 1 has nothing on it.
            var step = PlaybackNarration.SignalStepFor(ChartOf(CipherBShaped(100)), 1);

            Assert.Null(step.Text);
            Assert.Equal(int.MaxValue, step.RarestFireCount);
        }

        [Fact]
        public void FireCount_CountsBarsThatCarryAValue_NotBarsInTheArray()
        {
            // The measure the whole ranking rests on, stated on its own.
            Assert.Equal(0, PlaybackNarration.FireCount(new[] { double.NaN, double.NaN }));
            Assert.Equal(2, PlaybackNarration.FireCount(new[] { 1.0, double.NaN, 0.0 }));
        }

        [Fact]
        public void ASelectedComponent_IsSpokenEvenWhenItIsTheRarestOfMany()
        {
            // The second half of Cody's report: "I enabled narration for cipher b's series, then
            // arrowed down to triple confluence buy and hit n". That is a COMPONENT SELECTION —
            // the series narrates only what is flagged — and the gold dot is then the only thing
            // that may speak on a bar where all three fired.
            var series = CipherBShaped(100);
            series.Components.First(c => c.Name == "Triple Confluence Buy").IsAutoNarrated = true;

            string? spoken = PlaybackNarration.SignalsForStep(ChartOf(series), 100);

            Assert.Equal("Triple confluence buy, strong confirmation.", spoken);
        }
    }
}
