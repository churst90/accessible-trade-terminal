using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Charts;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Workspace.Reducers
{
    /// <summary>
    /// Reduces playback + accessibility + chart-display toggles.
    /// All of these are simple flag flips or bounded-delta increments — no
    /// collaborator services needed. Grouped together because they all live
    /// on the accessibility/presentation axis.
    /// </summary>
    internal static class PlaybackReducer
    {
        public static WorkspaceState Reduce(WorkspaceState state, WorkspaceAction action) => action switch
        {
            // Playback
            AdjustPlaybackSpeedAction a => state with { PlaybackSpeed = Math.Clamp(state.PlaybackSpeed + a.Delta, 0.1f, 10.0f) },
            SetPlaybackSpeedAction a    => state with { PlaybackSpeed = Math.Clamp(a.Speed, 0.1f, 10.0f) },
            SetPlaybackAction a         => state with { IsPlaying = a.IsPlaying, PlaybackScope = a.Scope, IsPaused = false },
            TogglePauseAction           => state with { IsPaused = !state.IsPaused },

            // Accessibility toggles
            ToggleSpeechAction          => state with { IsSpeechEnabled = !state.IsSpeechEnabled },
            ToggleSonificationAction    => state with { IsSonificationEnabled = !state.IsSonificationEnabled },
            ToggleEventSpeechAction     => state with { IsEventSpeechEnabled = !state.IsEventSpeechEnabled },
            ToggleEarconsAction         => state with { IsEarconsEnabled = !state.IsEarconsEnabled },

            // Chart display toggles
            //
            // Turning Heikin-Ashi ON is a choice of the CANDLES chart under the transform, so
            // it puts the style back to Candles: the user sees one chart type, not a flag
            // crossed with one. Line and Area are raw-close displays and can never carry it,
            // and a brick chart is already a different construction: HA on from Renko restores
            // the real bars and lands on Candles + Heikin-Ashi. The focus follows the drawing
            // (FollowChartType) whichever way the chart type changes.
            ToggleHeikinAshiAction      => FollowChartType(state, ToggleHeikinAshi(state)),
            SetChartStyleAction a       => FollowChartType(state, ApplyChartStyle(state, a.Style, a.HeikinAshi)),
            SetRenkoSettingsAction a    => ApplyRenkoSettings(state, a.Settings, a.BrickBoxSize),
            // The price scale is ONE choice to the user — Regular, Logarithmic, Percent,
            // Indexed to 100 — so the two fields never disagree. Turning log ON is the
            // Logarithmic entry and puts the mode back to Regular; turning it off leaves the
            // mode alone (it can only have been Regular while log was on). A non-Regular mode
            // is an affine relabel of the regular scale and is never logarithmic.
            ToggleLogScaleAction        => state with
            {
                IsLogScale = !state.IsLogScale,
                ScaleMode = !state.IsLogScale ? PriceScaleMode.Regular : state.ScaleMode,
            },
            SetPriceScaleAction a       => state with
            {
                ScaleMode = a.Mode,
                IsLogScale = a.Mode == PriceScaleMode.Regular && a.Log,
            },

            _ => state
        };

        private static WorkspaceState ToggleHeikinAshi(WorkspaceState state)
        {
            if (state.ChartStyle.IsBrickStyle())
            {
                // A brick chart can never show Heikin-Ashi bricks. Turning the transform on is
                // the candles choice under HA, so the real bars come back first.
                var left = LeaveBricks(state);
                return left with { ChartStyle = PriceChartStyle.Candles, IsHeikinAshi = true };
            }

            return state with
            {
                IsHeikinAshi = !state.IsHeikinAshi,
                ChartStyle = !state.IsHeikinAshi ? PriceChartStyle.Candles : state.ChartStyle,
            };
        }

        private static WorkspaceState ApplyChartStyle(WorkspaceState state, PriceChartStyle style, bool heikinAshi)
        {
            if (style.IsBrickStyle())
                return EnterBricks(state, style, state.Renko);

            var baseState = state.ChartStyle.IsBrickStyle() ? LeaveBricks(state) : state;
            return baseState with
            {
                ChartStyle = style,
                // Line and Area always draw the raw close, so they can never carry the
                // transform; Heikin-Ashi only exists on the candles chart.
                IsHeikinAshi = style == PriceChartStyle.Candles && heikinAshi,
            };
        }

        private static WorkspaceState ApplyRenkoSettings(
            WorkspaceState state, RenkoSettings settings, double savedBox)
        {
            // Off a built chart, or a change that does not touch the shown type's own fields
            // (the range changed while Renko is on screen): keep the choice, rebuild nothing.
            if (!state.ChartStyle.IsBrickStyle()
                || (!(savedBox > 0) && BuiltCharts.SameFor(state.ChartStyle, settings, state.Renko)))
            {
                return state with
                {
                    Renko = settings,
                    BrickBoxSize = savedBox > 0 ? savedBox : state.BrickBoxSize,
                };
            }

            return EnterBricks(state, state.ChartStyle, settings, savedBox);
        }

        /// <summary>
        /// Chooses a built chart (<paramref name="style"/>: Renko, range bars or line break): the real bars become <see cref="WorkspaceState.TimeBars"/>,
        /// the box is resolved once and frozen, and every index-based field is put on the bricks.
        /// A source with no bars (a restore before its first load) keeps the choice with an
        /// empty brick set; the command path has already refused anything it could not plan.
        /// </summary>
        private static WorkspaceState EnterBricks(
            WorkspaceState state, PriceChartStyle style, RenkoSettings settings, double savedBox = 0)
        {
            var source = (state.ChartStyle.IsBrickStyle() ? state.TimeBars : state.Data) ?? state.Data;

            // A restore carries the box the tab was drawn at. A settings change does not, and
            // must resolve afresh even though the old box is still on the state. A direct style
            // choice that is already that type and unchanged keeps its frozen box; another built
            // type (Renko to range bars) resolves its own. Off a built chart the box on the state
            // is a restore's (LeaveBricks clears it), so it belongs to the type being entered.
            double hint = state.ChartStyle.IsBrickStyle()
                ? (savedBox > 0 ? savedBox
                    : state.ChartStyle == style && BuiltCharts.SameFor(style, settings, state.Renko) ? state.BrickBoxSize : 0)
                : savedBox > 0 ? savedBox : state.BrickBoxSize;

            // A workspace restore chooses the style BEFORE its data has loaded, so there is
            // nothing to resolve a box from yet. Keep the choice; the first UpdateData on the
            // built chart resolves and freezes the box. A command never reaches this with a bad box:
            // it validated against the loaded bars before dispatching.
            if (source.Count == 0
                || (!(hint > 0) && !BuiltCharts.TryResolveBox(style, source, settings, out hint, out _)))
            {
                return state with
                {
                    ChartStyle = style,
                    IsHeikinAshi = false,
                    Renko = settings,
                    TimeBars = source,
                    Data = TimeSeriesBuffer<Ohlcv>.Empty,
                    BrickSpans = ImmutableArray<BrickSpan>.Empty,
                    BrickBoxSize = hint > 0 ? hint : 0,
                    CurrentDataIndex = 0,
                    ViewportStartIndex = 0,
                };
            }

            double box = hint;
            var built = BuiltCharts.Build(
                style, source, settings, box, BrickChartBuilder.BarLength(state.Identity, source));
            if (!built.Ok)
            {
                return state with
                {
                    ChartStyle = style,
                    IsHeikinAshi = false,
                    Renko = settings,
                    TimeBars = source,
                    Data = TimeSeriesBuffer<Ohlcv>.Empty,
                    BrickSpans = ImmutableArray<BrickSpan>.Empty,
                    BrickBoxSize = hint > 0 ? hint : 0,
                    CurrentDataIndex = 0,
                    ViewportStartIndex = 0,
                };
            }

            int idx = Math.Max(0, built.Bricks.Count - 1);
            int timeLength = TimeBarsLength(state);
            var fitted = state with
            {
                RightMarginBars = BrickMargin(built.Bricks.Count),
                ViewportLength = FitBricks(timeLength, built.Bricks.Count),
            };
            return fitted with
            {
                ChartStyle = style,
                IsHeikinAshi = false,
                Renko = settings,
                BrickBoxSize = box,
                TimeBars = source,
                Data = built.Bricks,
                BrickSpans = built.Spans,
                TimeBarsViewportLength = timeLength,
                CurrentDataIndex = idx,
                ViewportStartIndex = LiveStart(fitted, built.Bricks.Count),
            };
        }

        /// <summary>Leaves a brick chart: the real bars return to <see cref="WorkspaceState.Data"/>,
        /// the brick-only fields clear, and the cursor and viewport go back to the live edge.</summary>
        private static WorkspaceState LeaveBricks(WorkspaceState state)
        {
            var time = state.TimeBars ?? state.Data;
            int idx = Math.Max(0, time.Count - 1);
            if (state.TimeBarsViewportLength > 0)
                state = state with { ViewportLength = state.TimeBarsViewportLength };
            state = state with { RightMarginBars = TimeBarsMargin };
            return state with
            {
                TimeBarsViewportLength = 0,
                Data = time,
                TimeBars = null,
                BrickSpans = ImmutableArray<BrickSpan>.Empty,
                // The frozen size is the type's own (a Renko box is no range bar's range), so it
                // goes with the chart; choosing a built type again resolves afresh.
                BrickBoxSize = 0,
                CurrentDataIndex = idx,
                ViewportStartIndex = LiveStart(state, time.Count),
            };
        }

        /// <summary>The zoom the time bars had: the one remembered on entering a built chart,
        /// or the current one when the chart is not built yet.</summary>
        internal static int TimeBarsLength(WorkspaceState state) =>
            state.ChartStyle.IsBrickStyle() && state.TimeBarsViewportLength > 0
                ? state.TimeBarsViewportLength
                : state.ViewportLength;

        /// <summary>The empty slots to the right of the newest candle: room for the bars to come.</summary>
        internal const int TimeBarsMargin = 10;

        /// <summary>
        /// The empty slots to the right of the newest brick. Ten slots beside twenty-three daily
        /// bricks left a third of the Renko chart empty (the website photographs, 2026-11-21),
        /// so a built chart keeps about a tenth of its bricks as margin: two at the least, the
        /// candles' ten at the most.
        /// </summary>
        internal static int BrickMargin(int brickCount) => Math.Clamp(brickCount / 10, 2, TimeBarsMargin);

        /// <summary>
        /// The zoom a built chart opens at. A view sized for a hundred time bars held seventeen
        /// bricks in its left sixth and nothing to the right (the website photographs,
        /// 2026-11-21), so when the bricks are fewer than the time bars' view holds, the view
        /// fits them and their <see cref="BrickMargin"/>; otherwise it keeps the time bars' zoom.
        /// Never under the zoom-in limit (the margin plus ten).
        /// </summary>
        internal static int FitBricks(int timeLength, int brickCount)
        {
            int margin = BrickMargin(brickCount);
            if (brickCount >= timeLength - margin) return timeLength;
            return Math.Max(margin + 10, brickCount + margin);
        }

        private static int LiveStart(WorkspaceState state, int count)
        {
            int effectiveWindow = Math.Max(1, state.ViewportLength - state.RightMarginBars);
            return Math.Max(0, count - effectiveWindow);
        }

        /// <summary>
        /// The focus follows the drawing. The price series takes the chart type's shape itself
        /// (PriceSeriesShape, applied by the store after every reduce), so focus on it stays
        /// put. Focus on the separate Price line when Line or Area is chosen goes to the price
        /// series: that line is not drawn there (ChartTypeFocus). Focus on anything else (an
        /// indicator, a drawing's series) is the user's own choice and is left where it is.
        /// </summary>
        private static WorkspaceState FollowChartType(WorkspaceState before, WorkspaceState after)
        {
            string focused = after.FocusedSeriesId ?? CoreSeriesIds.Candles;
            if (focused == CoreSeriesIds.Price && ChartTypeFocus.PriceLineOffScreen(after))
                return FocusSeries(after, CoreSeriesIds.Candles);
            return after;
        }

        private static WorkspaceState FocusSeries(WorkspaceState state, string seriesId) => state with
        {
            FocusedSeriesId = seriesId,
            FocusedComponentIndex = SeriesReducer.GetDefaultComponentIndex(state, seriesId),
            LastInteractionContext = InteractionContext.Series,
        };
    }
}
