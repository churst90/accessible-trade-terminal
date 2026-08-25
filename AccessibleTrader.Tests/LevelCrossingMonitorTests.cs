using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests
{
    public class LevelCrossingMonitorTests
    {
        // Spy sonifier records every PlayNote call so tests can assert on tiers.
        private sealed class SpySonifier : INavigationSonifier
        {
            public readonly List<(double Freq, double Dur, string Wave, float Vol, float Pan)> Notes = new();

            public void PlayNote(double freq, double dur, string wave, float vol, float pan, double delay = 0)
                => Notes.Add((freq, dur, wave, vol, pan));

            public void PlayPatch(AccessibleTrader.Sdk.Models.SoundPatch patch, float volumeScale = 1f, float pan = 0f) { }

            public void SyncNavigationSlots(WorkspaceState state) { }
            public void SonifyProfile(ChartSeries series, int binIndex, float masterVolume = 1) { }
            public void SonifyHeatmap(ChartSeries series, int dataIndex, int binIndex, float masterVolume = 1) { }
            public AudioPoint CreateAudioPoint(ChartSeries series, int componentIndex, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, int dataIndex, float masterVolume = 1, double? overrideValue = null)
                => new AudioPoint(0, 0, "sine", 0);
            public void StopNavigationVoice() { }
            public void SetMasterGain(float gain) { }
            public void Silence() { }
            public System.Threading.Tasks.Task FireClusterTicksAsync(WorkspaceState state, int dataIndex, string excludeSeriesId, int excludeComponentIndex, bool crossSeriesMode = false)
                => System.Threading.Tasks.Task.CompletedTask;
        }

        private static ChartSeries BuildRsi(double[] values)
        {
            var cfg = new SeriesConfig { Id = "rsi", Name = "rsi", IndicatorCode = "RSI", Pane = "RSI" };
            cfg.Components.Add(new ComponentConfig
            {
                Name = "Value",
                DisplayName = "RSI",
                IsVisible = true,
                Role = ComponentRole.Signal,
                DisplayType = ComponentDisplayType.Line,
            });
            cfg.Levels.Add(new LevelConfig
            {
                Name = "Overbought",
                Value = 70.0,
                PlayEarcon = true,
                EarconVolume = 1.0f,
                IsVisible = true,
            });
            cfg.Levels.Add(new LevelConfig
            {
                Name = "Oversold",
                Value = 30.0,
                PlayEarcon = true,
                EarconVolume = 1.0f,
                IsVisible = true,
            });

            var buf = new SeriesDataBuffer { SeriesId = "rsi" };
            buf.ComponentData["Value"] = values;
            return new ChartSeries(cfg, buf);
        }

        private static WorkspaceState StateAt(ChartSeries series, int currentIndex, int barCount)
        {
            var list = new List<Ohlcv>();
            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < barCount; i++)
                list.Add(new Ohlcv(start.AddMinutes(i), 100, 100, 100, 100, 0));
            var data = new TimeSeriesBuffer<Ohlcv>(list);

            return WorkspaceState.Initial with
            {
                Data = data,
                ActiveSeries = ImmutableList.Create(series),
                CurrentDataIndex = currentIndex,
                ViewportStartIndex = 0,
                ViewportLength = barCount,
            };
        }

        [Fact]
        public void ApproachPing_FiresWithinFivePercentBand()
        {
            var series = BuildRsi(new double[] { 50, 67 });
            var spy = new SpySonifier();
            var mon = new LevelCrossingMonitor(spy);

            mon.OnBarNavigated(StateAt(series, 1, 2));

            Assert.Single(spy.Notes);
            Assert.Equal(1400.0, spy.Notes[0].Freq, 1);
            Assert.InRange(spy.Notes[0].Vol, 0.0001f, 0.16f);
        }

        [Fact]
        public void ApproachPing_DoesNotRearmUntilOutOfBand()
        {
            var series = BuildRsi(new double[] { 67, 68 });
            var spy = new SpySonifier();
            var mon = new LevelCrossingMonitor(spy);

            mon.OnBarNavigated(StateAt(series, 0, 2));
            mon.OnBarNavigated(StateAt(series, 1, 2));

            Assert.Single(spy.Notes);
        }

        [Fact]
        public void ApproachPing_Rearms_AfterValueLeavesBand()
        {
            var series = BuildRsi(new double[] { 67, 50, 68 });
            var spy = new SpySonifier();
            var mon = new LevelCrossingMonitor(spy);

            mon.OnBarNavigated(StateAt(series, 0, 3));
            mon.OnBarNavigated(StateAt(series, 1, 3));
            mon.OnBarNavigated(StateAt(series, 2, 3));

            Assert.Equal(2, spy.Notes.Count);
        }

        [Fact]
        public void SustainedConfirmation_Fires_OnFourthConsecutiveBarBeyondLevel()
        {
            var series = BuildRsi(new double[] { 75, 76, 77, 78 });
            var spy = new SpySonifier();
            var mon = new LevelCrossingMonitor(spy);

            mon.OnBarNavigated(StateAt(series, 0, 4));
            mon.OnBarNavigated(StateAt(series, 1, 4));
            mon.OnBarNavigated(StateAt(series, 2, 4));
            Assert.Empty(spy.Notes);

            mon.OnBarNavigated(StateAt(series, 3, 4));
            Assert.Single(spy.Notes);
            Assert.Equal(220.0, spy.Notes[0].Freq, 1);
        }

        [Fact]
        public void SustainedConfirmation_FiresOnce_ThenRequiresReEntry()
        {
            var series = BuildRsi(new double[] { 75, 76, 77, 78, 79 });
            var spy = new SpySonifier();
            var mon = new LevelCrossingMonitor(spy);

            for (int i = 0; i < 5; i++)
                mon.OnBarNavigated(StateAt(series, i, 5));

            Assert.Single(spy.Notes);
        }

        [Fact]
        public void Reset_ClearsSustainedAndApproachState()
        {
            var series = BuildRsi(new double[] { 75, 76, 77, 78 });
            var spy = new SpySonifier();
            var mon = new LevelCrossingMonitor(spy);

            for (int i = 0; i < 4; i++) mon.OnBarNavigated(StateAt(series, i, 4));
            Assert.Single(spy.Notes);

            mon.Reset();
            for (int i = 0; i < 4; i++) mon.OnBarNavigated(StateAt(series, i, 4));
            Assert.Equal(2, spy.Notes.Count);
        }

        [Fact]
        public void DoesNotFire_WhenValueIsBeyondLevel_InApproachPath()
        {
            var series = BuildRsi(new double[] { 75 });
            var spy = new SpySonifier();
            var mon = new LevelCrossingMonitor(spy);

            mon.OnBarNavigated(StateAt(series, 0, 1));

            Assert.Empty(spy.Notes);
        }

        [Fact]
        public void IgnoresLevelsWithPlayEarconFalse()
        {
            var series = BuildRsi(new double[] { 67 });
            series.Levels[0].PlayEarcon = false;
            series.Levels[1].PlayEarcon = false;

            var spy = new SpySonifier();
            var mon = new LevelCrossingMonitor(spy);
            mon.OnBarNavigated(StateAt(series, 0, 1));

            Assert.Empty(spy.Notes);
        }

        [Fact]
        public void ProximityAffectsApproachVolume()
        {
            var spy1 = new SpySonifier();
            var mon1 = new LevelCrossingMonitor(spy1);
            mon1.OnBarNavigated(StateAt(BuildRsi(new double[] { 69 }), 0, 1));

            var spy2 = new SpySonifier();
            var mon2 = new LevelCrossingMonitor(spy2);
            mon2.OnBarNavigated(StateAt(BuildRsi(new double[] { 67 }), 0, 1));

            Assert.True(spy1.Notes[0].Vol > spy2.Notes[0].Vol);
        }

        /// <summary>
        /// A price series carrying one hand-placed level — the case the monitor used to ignore
        /// entirely, because it chose which levels to watch by matching their NAME against
        /// "Overbought" / "Oversold" and skipped everything else.
        /// </summary>
        private static ChartSeries BuildPriceWithUserLevel(double[] closes, double levelValue)
        {
            var cfg = new SeriesConfig { Id = "px", Name = "px", IndicatorCode = "CANDLES", Pane = "Main" };
            cfg.Components.Add(new ComponentConfig
            {
                Name = "Close", DisplayName = "Close", IsVisible = true,
                Role = ComponentRole.Signal, DisplayType = ComponentDisplayType.Line,
            });
            cfg.Levels.Add(new LevelConfig
            {
                Name = "Level", Value = levelValue, PlayEarcon = true, EarconVolume = 1.0f,
                IsVisible = true, IsUserDefined = true, CrossDirection = LevelCrossDirection.Both,
            });

            var buf = new SeriesDataBuffer { SeriesId = "px" };
            buf.ComponentData["Close"] = closes;
            return new ChartSeries(cfg, buf);
        }

        /// <summary>
        /// The headline regression: a level named "Level" produced no sound at all, however the
        /// "Play Earcon on Crossing" checkbox was set.
        /// </summary>
        [Fact]
        public void AUserLevelIsWatchedEvenThoughItIsNotCalledOverboughtOrOversold()
        {
            // 100 → approaches 99 from below, within the 5% band.
            var series = BuildPriceWithUserLevel(new double[] { 90, 99 }, 100);
            var spy = new SpySonifier();
            var mon = new LevelCrossingMonitor(spy);

            mon.OnBarNavigated(StateAt(series, 0, 2));
            mon.OnBarNavigated(StateAt(series, 1, 2));

            Assert.NotEmpty(spy.Notes);
        }

        /// <summary>
        /// A two-sided level has no fixed "outside", so it must ping on an approach from ABOVE as
        /// well. The one-sided gate would have suppressed this.
        /// </summary>
        [Fact]
        public void ATwoSidedLevelPingsOnApproachFromEitherSide()
        {
            var fromAbove = BuildPriceWithUserLevel(new double[] { 120, 101 }, 100);
            var spy = new SpySonifier();
            var mon = new LevelCrossingMonitor(spy);

            mon.OnBarNavigated(StateAt(fromAbove, 0, 2));
            mon.OnBarNavigated(StateAt(fromAbove, 1, 2));

            Assert.NotEmpty(spy.Notes);
        }

        /// <summary>
        /// Nothing may be announced before a crossing actually happens. Opening a chart whose price
        /// already sits above a level must not report holding past it a few bars later — that would
        /// be announcing an event that never occurred.
        /// </summary>
        [Fact]
        public void NoSustainedToneWithoutAnActualCrossing()
        {
            // Well above the level and far outside the approach band for every bar.
            var series = BuildPriceWithUserLevel(new double[] { 200, 201, 202, 203, 204, 205 }, 100);
            var spy = new SpySonifier();
            var mon = new LevelCrossingMonitor(spy);

            for (int i = 0; i < 6; i++) mon.OnBarNavigated(StateAt(series, i, 6));

            Assert.Empty(spy.Notes);
        }

        /// <summary>
        /// Once price does cross and stays past the level, the confirmation tone fires — once.
        /// </summary>
        [Fact]
        public void CrossingThenHoldingFiresTheSustainedToneExactlyOnce()
        {
            // Starts below, crosses at bar 1, then holds well clear of the approach band.
            var series = BuildPriceWithUserLevel(new double[] { 50, 200, 205, 210, 215, 220, 225 }, 100);
            var spy = new SpySonifier();
            var mon = new LevelCrossingMonitor(spy);

            for (int i = 0; i < 7; i++) mon.OnBarNavigated(StateAt(series, i, 7));

            Assert.Single(spy.Notes);
            Assert.Equal(220.0, spy.Notes[0].Freq, 1);   // Tier 3, not the 1400 Hz approach ping
        }

        /// <summary>
        /// Provider levels must behave exactly as before — the name inference is preserved for them,
        /// so this whole change is invisible to an RSI.
        /// </summary>
        [Fact]
        public void OverboughtStillOnlyWatchesAbove()
        {
            // Drifting away below an overbought line must stay silent.
            var series = BuildRsi(new double[] { 40, 35 });
            var spy = new SpySonifier();
            var mon = new LevelCrossingMonitor(spy);

            mon.OnBarNavigated(StateAt(series, 0, 2));
            mon.OnBarNavigated(StateAt(series, 1, 2));

            Assert.Empty(spy.Notes);
        }
    }
}
