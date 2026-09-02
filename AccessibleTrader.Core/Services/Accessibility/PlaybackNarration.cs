using System.Globalization;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// The words a user hears about playback — the text equivalent of a run of tones.
    ///
    /// <para>
    /// Until 2026-09-02 there were none. The coordinator's playback gate returned before any
    /// announcement "because the PlaybackOrchestrator handles its own sonification/speech", and
    /// the orchestrator has no speech router; nothing else spoke either. Space started a stream
    /// of tones with no word about what was playing or from where, Ctrl+Space parked it in
    /// silence, Shift+= changed the speed without saying so, and when the last bar sounded the
    /// tones simply stopped — the same sound as a crash, a dropped feed or a muted chart. Up to
    /// eight minutes of the terminal's richest output, and not one sentence (WCAG 1.1.1, 1.2.1).
    /// </para>
    ///
    /// <para>
    /// Everything here is a pure function of state, so the coordinator's tests can assert the
    /// exact sentence and the rules can be read in one place:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>Start</b> names the scope, the first bar and how many bars will play.</item>
    ///   <item><b>Pause</b> names where the cursor parked; <b>resume</b> is one word.</item>
    ///   <item><b>Finished</b> and <b>stopped</b> are different sentences. The sequencer reached
    ///         the last bar in one case and was interrupted in the other, and a user who hears
    ///         "finished" knows the whole range sounded.</item>
    ///   <item><b>Landmarks</b> while it runs: the tones carry price, so speech carries time. Each
    ///         time the bar's date crosses a calendar boundary one step coarser than the bar
    ///         spacing — hour, day, month or year, chosen so that at the current speed the
    ///         announcements are at least <see cref="MinSecondsBetweenLandmarks"/> apart — the new
    ///         period is spoken without interrupting. Nothing else is said per bar: a bar readout
    ///         every 100 ms would be noise, not an equivalent.</item>
    /// </list>
    /// </summary>
    public static class PlaybackNarration
    {
        /// <summary>Playback speaks ~10 bars a second at 1x; a landmark every two seconds is a
        /// cadence a person can follow without the words piling up.</summary>
        public const double MinSecondsBetweenLandmarks = 2.0;

        /// <summary>Bars per second at speed 1.0 — the sequencer's 100 ms base delay.</summary>
        private const double BarsPerSecondAtUnitSpeed = 10.0;

        public enum LandmarkUnit { Hour, Day, Month, Year }

        // ── Start / pause / resume / stop ──────────────────────────────────────────

        public static string StartText(WorkspaceState state, PlaybackPlan plan)
        {
            string what = state.PlaybackScope switch
            {
                PlaybackScope.Chart => "chart",
                PlaybackScope.Component => ComponentName(plan),
                _ => plan.Series.Count > 0 ? SeriesName(plan.Series[0]) : "series",
            };

            int barSeconds = BarSeconds(state);
            int count = state.Data!.Count - plan.StartIndex;
            string from = DateText(state.Data[plan.StartIndex].Date, barSeconds);
            return $"Playing {what} from {from}, {count} bar{(count == 1 ? "" : "s")}.";
        }

        /// <summary>The name the arrow keys use for a series, with the config name behind it for
        /// a series that was never given a friendly one — a blank would read "Playing  from".</summary>
        private static string SeriesName(ChartSeries series)
            => string.IsNullOrWhiteSpace(series.FriendlyName) ? series.Name : series.FriendlyName;

        private static string ComponentName(PlaybackPlan plan)
        {
            if (plan.Series.Count == 0) return "component";
            var series = plan.Series[0];
            if (plan.ComponentFilter < 0 || plan.ComponentFilter >= series.Components.Count)
                return SeriesName(series);
            // A one-component indicator would stutter — "RSI RSI", "VWAP VWAP" — and the
            // series name already says everything the component name would.
            if (series.Components.Count == 1) return SeriesName(series);
            var comp = series.Components[plan.ComponentFilter];
            string name = string.IsNullOrWhiteSpace(comp.DisplayName) ? comp.Name : comp.DisplayName;
            return $"{SeriesName(series)} {name}";
        }

        public static string PauseText(WorkspaceState state)
            => $"Paused at {CursorDateText(state)}.";

        public const string ResumeText = "Resumed.";

        /// <summary>
        /// "Finished" when the cursor is on the last bar, "stopped" otherwise. The sequencer
        /// walks the cursor to <c>Count - 1</c> before it ends, so that is what "the whole range
        /// sounded" looks like from the store; a user stop lands anywhere before it.
        /// </summary>
        public static string EndText(WorkspaceState state)
            => $"Playback {(ReachedEnd(state) ? "finished" : "stopped")} at {CursorDateText(state)}.";

        /// <summary>
        /// The sequencer iterates a snapshot of the data, so a bar appended by the live feed
        /// mid-playback makes a full run land one short of the new last bar and read as
        /// "stopped". Rare, and the honest reading of the store; noted rather than special-cased.
        /// </summary>
        public static bool ReachedEnd(WorkspaceState state)
            => state.Data != null && state.Data.Count > 0
            && state.CurrentDataIndex >= state.Data.Count - 1;

        public static string SpeedText(float speed)
            => $"Playback speed: {speed.ToString("F1", CultureInfo.InvariantCulture)}x";

        // ── Landmarks while playing ─────────────────────────────────────────────────

        /// <summary>
        /// The coarsest-necessary calendar unit for landmark speech: the smallest of hour, day,
        /// month, year that holds at least <see cref="MinSecondsBetweenLandmarks"/> worth of bars
        /// at <paramref name="speed"/>. Year is the ceiling — monthly bars at any speed land here.
        /// </summary>
        public static LandmarkUnit UnitFor(int barSeconds, float speed)
        {
            double barsPerSecond = BarsPerSecondAtUnitSpeed * Math.Max(0.1, speed);
            double minBars = MinSecondsBetweenLandmarks * barsPerSecond;
            int bar = Math.Max(1, barSeconds);

            if (3600.0 / bar >= minBars) return LandmarkUnit.Hour;
            if (86400.0 / bar >= minBars) return LandmarkUnit.Day;
            if (2592000.0 / bar >= minBars) return LandmarkUnit.Month;
            return LandmarkUnit.Year;
        }

        /// <summary>
        /// The landmark to speak when playback steps from <paramref name="previous"/> to
        /// <paramref name="current"/>, or null when no boundary of <paramref name="unit"/> was
        /// crossed. Compared in the user's zone, the same instant the arrow keys read.
        /// </summary>
        public static string? Landmark(DateTime previous, DateTime current, LandmarkUnit unit)
        {
            var p = SpeechTimeFormatter.ToDisplay(previous);
            var c = SpeechTimeFormatter.ToDisplay(current);

            switch (unit)
            {
                case LandmarkUnit.Hour:
                    if (p.Date == c.Date && p.Hour == c.Hour) return null;
                    return c.ToString("HH:mm", CultureInfo.InvariantCulture);
                case LandmarkUnit.Day:
                    if (p.Date == c.Date) return null;
                    return c.ToString("MMMM d", CultureInfo.InvariantCulture);
                case LandmarkUnit.Month:
                    if (p.Year == c.Year && p.Month == c.Month) return null;
                    return c.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
                default:
                    if (p.Year == c.Year) return null;
                    return c.ToString("yyyy", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// The landmark for the step the store just took, if playback is running and the step
        /// crossed a boundary. Null while paused and whenever the cursor did not move, and null
        /// when <paramref name="isFirstStep"/>: the sequencer's first NavigateAction jumps the
        /// cursor from wherever the user left it to the plan's start bar, which is not a step
        /// through time, and the start sentence has already named that bar.
        /// </summary>
        public static string? LandmarkForStep(WorkspaceState previous, WorkspaceState current, bool isFirstStep)
        {
            if (!current.IsPlaying || current.IsPaused || !previous.IsPlaying || isFirstStep) return null;
            var data = current.Data;
            if (data == null || data.Count == 0) return null;
            int from = previous.CurrentDataIndex, to = current.CurrentDataIndex;
            if (from == to || from < 0 || to < 0 || from >= data.Count || to >= data.Count) return null;

            var unit = UnitFor(BarSeconds(current), current.PlaybackSpeed);
            return Landmark(data[from].Date, data[to].Date, unit);
        }

        // ── Dates ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Seconds between bars: the chart's timeframe when it parses, the spacing of the first
        /// two bars when it does not, and a day when there is only one bar to look at.
        /// </summary>
        public static int BarSeconds(WorkspaceState state)
        {
            int fromTimeframe = TimeframeUtility.ToSeconds(state.Identity.Timeframe ?? "");
            if (fromTimeframe > 0) return fromTimeframe;

            var data = state.Data;
            if (data != null && data.Count >= 2)
            {
                double gap = (data[1].Date - data[0].Date).TotalSeconds;
                if (gap > 0) return (int)gap;
            }
            return 86400;
        }

        /// <summary>A bar's date the way the viewport description reads it, with the time of
        /// day added only when bars are closer together than a day.</summary>
        public static string DateText(DateTime stamp, int barSeconds)
            => barSeconds < 86400
                ? SpeechTimeFormatter.Format(stamp, SpeechTimeFormatter.DateTimeFormat)
                : SpeechTimeFormatter.FormatLongDate(stamp);

        private static string CursorDateText(WorkspaceState state)
        {
            var data = state.Data;
            if (data == null || data.Count == 0) return "no bar";
            int idx = Math.Clamp(state.CurrentDataIndex, 0, data.Count - 1);
            return DateText(data[idx].Date, BarSeconds(state));
        }
    }
}
