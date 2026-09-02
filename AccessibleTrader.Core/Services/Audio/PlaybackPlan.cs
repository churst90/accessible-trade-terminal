using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Audio
{
    /// <summary>
    /// What a playback request would play and from where — the ONE answer to that question.
    ///
    /// <para>
    /// Three callers need it and used to agree by coincidence. <see cref="PlaybackOrchestrator"/>
    /// selected the series and the start bar as it started the sequencer; the
    /// <c>CommandDispatcher</c> had no idea what would play and dispatched
    /// <c>SetPlaybackAction(true)</c> regardless; and nothing spoke, so nobody had to describe the
    /// plan in words. Now the dispatcher refuses (and says why) when the plan is empty, the
    /// coordinator speaks the plan when playback starts, and the orchestrator plays exactly the
    /// plan it was described from. If the selection rule ever changes, it changes here and all
    /// three follow — the alternative is the announcement naming a series the sequencer skipped.
    /// </para>
    /// </summary>
    /// <param name="Series">The series that will sound. Empty when there is nothing to play.</param>
    /// <param name="StartIndex">The first bar played. Chart scope starts at the viewport's left
    /// edge; series and component scope start at the cursor so the user hears from where they
    /// are standing forward.</param>
    /// <param name="ComponentFilter">-1 plays every visible component; otherwise the single
    /// component index that component scope pins.</param>
    /// <param name="RefusalReason">Spoken when <see cref="Series"/> is empty — the sentence that
    /// explains why the key did nothing. Null when the plan is playable.</param>
    public sealed record PlaybackPlan(
        IReadOnlyList<ChartSeries> Series,
        int StartIndex,
        int ComponentFilter,
        string? RefusalReason)
    {
        public bool IsPlayable => Series.Count > 0 && RefusalReason == null;

        /// <summary>
        /// The refusal for a workspace with no bars at all. The dispatcher speaks this one before
        /// any playback command reaches the plan, so it is only reached here through a caller
        /// that skipped that gate.
        /// </summary>
        public const string NoDataReason = "No chart loaded.";

        public const string EverySeriesMutedReason =
            "Nothing to play. Every series is muted or hidden.";

        public const string NoSeriesReason =
            "Nothing to play. No series is loaded.";

        public static PlaybackPlan Resolve(WorkspaceState state, PlaybackScope scope)
        {
            if (state.Data == null || state.Data.Count == 0)
                return new PlaybackPlan(Array.Empty<ChartSeries>(), 0, -1, NoDataReason);

            int last = state.Data.Count - 1;

            if (scope == PlaybackScope.Chart)
            {
                // Chart scope: every visible, unmuted, non-drawing, non-profile series plays
                // simultaneously, layered bar by bar. Muted series are excluded so the user's
                // mute actually silences them.
                var playList = state.ActiveSeries
                    .Where(s => s.IsVisible && !s.IsMuted && !s.IsDrawing && !s.IsProfile)
                    .ToList();

                if (playList.Count == 0)
                {
                    // Two different things the user can fix: nothing loaded at all, or
                    // everything loaded is muted or hidden.
                    bool anySeries = state.ActiveSeries.Any(s => !s.IsDrawing && !s.IsProfile);
                    return new PlaybackPlan(playList, 0, -1, anySeries ? EverySeriesMutedReason : NoSeriesReason);
                }

                return new PlaybackPlan(playList, Math.Clamp(state.ViewportStartIndex, 0, last), -1, null);
            }

            // Series scope: all components of the focused series play together.
            // Component scope: only the focused component of the focused series plays.
            string seriesId = state.FocusedSeriesId ?? state.PrimarySeriesId;
            var series = state.ActiveSeries.FirstOrDefault(s => s.Id == seriesId)
                      ?? state.ActiveSeries.FirstOrDefault();
            if (series == null)
                return new PlaybackPlan(Array.Empty<ChartSeries>(), 0, -1, NoSeriesReason);

            int start = Math.Clamp(Math.Max(0, state.CurrentDataIndex), 0, last);

            // -1 is already the sequencer's "no component filter" value, which is the right
            // answer for a series with nothing to filter to.
            int componentFilter = scope == PlaybackScope.Component
                ? series.ClampComponent(state.FocusedComponentIndex)
                : -1;

            return new PlaybackPlan(new[] { series }, start, componentFilter, null);
        }
    }
}
