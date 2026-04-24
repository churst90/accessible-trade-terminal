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

            public void SyncNavigationSlots(WorkspaceState state) { }
            public void SonifySeries(ChartSeries series, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, int dataIndex, float masterVolume = 1, double durationSeconds = 0.2, double delayMilliseconds = 0) { }
            public void SonifyComponent(ChartSeries series, int componentIndex, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, int dataIndex, float masterVolume = 1, double durationSeconds = 0.2, double delayMilliseconds = 0) { }
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
    }
}
