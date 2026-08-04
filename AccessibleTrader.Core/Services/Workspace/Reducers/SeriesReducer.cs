using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Core.Models;

namespace AccessibleTrader.Core.Services.Workspace.Reducers
{
    /// <summary>
    /// Reduces series-management + series-focus + visibility/audio actions.
    /// This is the broadest reducer: anything that mutates the ActiveSeries
    /// list, its per-series mute/visibility/narration flags, or the currently
    /// focused series/component/bin. Announcements for mute/hide/narration
    /// toggles are published through the injected event bus.
    /// </summary>
    internal static class SeriesReducer
    {
        public static WorkspaceState Reduce(
            WorkspaceState state,
            WorkspaceAction action,
            IEventBus eventBus) => action switch
        {
            // Series focus
            SelectSeriesAction a        => state with { FocusedSeriesId = a.SeriesId, FocusedComponentIndex = GetDefaultComponentIndex(state, a.SeriesId) },
            SetPrimarySeriesIdAction a  => state with { PrimarySeriesId = a.SeriesId },
            SelectComponentAction a     => state with { FocusedComponentIndex = a.ComponentIndex },
            SelectBinAction a           => state with { FocusedBinIndex = a.BinIndex },
            SetInteractionContextAction a => state with { LastInteractionContext = a.Context },

            // Visibility / audio / narration
            ToggleMuteAction a      => ToggleMute(state, a.SeriesId, GetEffectiveComponentName(state, a.SeriesId, a.ComponentName), eventBus),
            ToggleHideAction a      => ToggleHide(state, a.SeriesId, GetEffectiveComponentName(state, a.SeriesId, a.ComponentName), eventBus),
            RestoreAllComponentsAction a => RestoreAll(state, a.Unhide, eventBus),
            ToggleNarrationAction a => ToggleNarration(state, a.SeriesId, eventBus),

            // Series management
            AddSeriesAction a                => AddSeries(state, a.Series),
            RemoveSeriesAction a             => RemoveSeries(state, a.SeriesId),
            AddLevelAction a                 => AddLevel(state, a.SeriesId, a.Level),
            RemoveLevelAction a              => RemoveLevel(state, a.SeriesId, a.LevelName),
            UpdateSeriesAction a             => state with { ActiveSeries = a.Series },
            UpdateSeriesDataAction a         => state with {
                ActiveSeries = state.ActiveSeries.Select(s =>
                    s.Id == a.SeriesId ? s.WithData(a.Data) : s).ToImmutableList()
            },
            UpdateSeriesZoneBandsAction a    => UpdateSeriesZoneBands(state, a.SeriesId, a.ZoneBands),
            UpdateSeriesParametersAction a   => UpdateSeriesParameters(state, a.SeriesId, a.Updates),

            _ => state
        };

        /// <summary>
        /// Resolves the default focused component index when a user or code path selects a
        /// series. For an indicator with a Body-role component (e.g. Candles), the body is
        /// the natural landing spot — the wicks flanking it are secondary.
        /// </summary>
        private static int GetDefaultComponentIndex(WorkspaceState state, string seriesId)
        {
            var s = state.ActiveSeries.FirstOrDefault(x => x.Id == seriesId);
            if (s == null || s.Components.Count == 0) return 0;

            for (int i = 0; i < s.Components.Count; i++)
            {
                if (s.Components[i].Role == ComponentRole.Body)
                    return i;
            }
            return 0;
        }

        private static string? GetEffectiveComponentName(WorkspaceState state, string? actionSeriesId, string? actionCompName)
        {
            if (!string.IsNullOrEmpty(actionCompName)) return actionCompName;
            if (state.LastInteractionContext != InteractionContext.Component) return null;

            var targetId = actionSeriesId ?? state.FocusedSeriesId;
            var s = state.ActiveSeries.FirstOrDefault(x => x.Id == targetId);
            if (s == null) return null;

            int idx = Math.Clamp(state.FocusedComponentIndex, 0, s.Components.Count - 1);
            return s.Components[idx].Name;
        }

        private static WorkspaceState AddSeries(WorkspaceState state, ChartSeries series)
        {
            var newList = state.ActiveSeries.Add(series);
            return state with { ActiveSeries = newList, FocusedSeriesId = series.Id };
        }

        private static WorkspaceState AddLevel(WorkspaceState state, string seriesId, LevelConfig level)
        {
            var target = state.ActiveSeries.FirstOrDefault(s => s.Id == seriesId);
            if (target == null) return state;
            // Clone the target so the prior state snapshot retains its own Levels collection —
            // otherwise any subscriber holding an earlier state reference would observe the
            // post-mutation collection before the StateStream notification fires.
            var updated = target.Clone();
            updated.Levels.Add(level);
            return state with {
                ActiveSeries = state.ActiveSeries.Select(s => s.Id == seriesId ? updated : s).ToImmutableList()
            };
        }

        /// <summary>
        /// Removes a level by name, cloning the target for the same reason <see cref="AddLevel"/>
        /// does — a subscriber holding an earlier state reference must not observe the mutation
        /// before the StateStream notification fires.
        /// </summary>
        private static WorkspaceState RemoveLevel(WorkspaceState state, string seriesId, string levelName)
        {
            var target = state.ActiveSeries.FirstOrDefault(s => s.Id == seriesId);
            if (target == null) return state;

            var updated = target.Clone();
            var doomed = updated.Levels
                .Where(l => string.Equals(l.Name, levelName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (doomed.Count == 0) return state;
            foreach (var l in doomed) updated.Levels.Remove(l);

            return state with {
                ActiveSeries = state.ActiveSeries.Select(s => s.Id == seriesId ? updated : s).ToImmutableList()
            };
        }

        private static WorkspaceState UpdateSeriesZoneBands(WorkspaceState state, string seriesId, IReadOnlyList<ZoneBandConfig> zoneBands)
        {
            var target = state.ActiveSeries.FirstOrDefault(s => s.Id == seriesId);
            if (target == null) return state;
            var updated = target.Clone();
            updated.ZoneBands.Clear();
            foreach (var band in zoneBands)
                updated.ZoneBands.Add(band.Clone());
            return state with {
                ActiveSeries = state.ActiveSeries.Select(s => s.Id == seriesId ? updated : s).ToImmutableList()
            };
        }

        private static WorkspaceState UpdateSeriesParameters(WorkspaceState state, string seriesId, Dictionary<string, double> updates)
        {
            var target = state.ActiveSeries.FirstOrDefault(s => s.Id == seriesId);
            if (target == null) return state;
            // Merge only the supplied keys — leave unaffected parameters unchanged.
            var updated = target.Clone();
            foreach (var kv in updates)
                updated.Config.Parameters[kv.Key] = kv.Value;
            return state with {
                ActiveSeries = state.ActiveSeries.Select(s => s.Id == seriesId ? updated : s).ToImmutableList()
            };
        }

        private static WorkspaceState RemoveSeries(WorkspaceState state, string id)
        {
            var newList = state.ActiveSeries.RemoveAll(s => s.Id == id);
            string? newFocus = state.FocusedSeriesId == id ? (newList.FirstOrDefault()?.Id ?? "candles") : state.FocusedSeriesId;
            return state with {
                ActiveSeries = newList,
                FocusedSeriesId = newFocus,
                LastInteractionContext = InteractionContext.Series,
                FocusedComponentIndex = 0
            };
        }

        /// <summary>
        /// Shows every hidden component, or unmutes every muted one, and says how many changed.
        ///
        /// <para>
        /// The count matters more than the action. "Nothing was hidden" and "9 components shown"
        /// are completely different pieces of information, and a silent reset leaves the user
        /// unsure whether the shortcut did anything or whether there was nothing to do.
        /// </para>
        /// </summary>
        private static WorkspaceState RestoreAll(WorkspaceState state, bool unhide, IEventBus eventBus)
        {
            int changed = 0;

            var newList = state.ActiveSeries.Select(s =>
            {
                bool seriesNeedsChange = unhide ? !s.IsVisible : s.IsMuted;
                bool anyComponent = s.Components.Any(c => unhide ? !c.IsVisible : c.IsMuted);
                if (!seriesNeedsChange && !anyComponent) return s;

                var updated = s.Clone();
                if (unhide)
                {
                    if (!updated.IsVisible) { updated.IsVisible = true; changed++; }
                    foreach (var c in updated.Components.Where(c => !c.IsVisible)) { c.IsVisible = true; changed++; }
                }
                else
                {
                    if (updated.IsMuted) { updated.IsMuted = false; changed++; }
                    foreach (var c in updated.Components.Where(c => c.IsMuted)) { c.IsMuted = false; changed++; }
                }
                return updated;
            }).ToImmutableList();

            string what = unhide ? "shown" : "unmuted";
            eventBus.Publish(new AnnouncementEvent(
                changed == 0
                    ? (unhide ? "Nothing was hidden." : "Nothing was muted.")
                    : $"{changed} {(changed == 1 ? "item" : "items")} {what}.", true));

            return changed == 0 ? state : state with { ActiveSeries = newList };
        }

        private static WorkspaceState ToggleMute(WorkspaceState state, string? seriesId, string? compName, IEventBus eventBus)
        {
            var targetId = seriesId ?? state.FocusedSeriesId;
            string? changedName = null;
            bool? newMuteState = null;

            var newList = state.ActiveSeries.Select(s =>
            {
                if (s.Id == targetId)
                {
                    var updated = s.Clone();
                    if (string.IsNullOrEmpty(compName))
                    {
                        updated.IsMuted = !updated.IsMuted;
                        changedName = updated.Name;
                        newMuteState = updated.IsMuted;
                    }
                    else
                    {
                        var comp = updated.Components.FirstOrDefault(c => c.Name == compName);
                        if (comp != null)
                        {
                            comp.IsMuted = !comp.IsMuted;
                            changedName = $"{s.Name}: {(string.IsNullOrEmpty(comp.DisplayName) ? comp.Name : comp.DisplayName)}";
                            newMuteState = comp.IsMuted;
                        }
                    }
                    return updated;
                }
                return s;
            }).ToImmutableList();

            if (changedName != null && newMuteState.HasValue)
            {
                eventBus.Publish(new SeriesStateChangedEvent(changedName, true, newMuteState.Value));
            }

            return state with { ActiveSeries = newList };
        }

        private static WorkspaceState ToggleHide(WorkspaceState state, string? id, string? compName, IEventBus eventBus)
        {
            var targetId = id ?? state.FocusedSeriesId;
            string? changedName = null;
            bool? newVisibility = null;

            var newList = state.ActiveSeries.Select(s =>
            {
                if (s.Id == targetId)
                {
                    var updated = s.Clone();
                    if (string.IsNullOrEmpty(compName))
                    {
                        updated.IsVisible = !updated.IsVisible;
                        changedName = updated.Name;
                        newVisibility = updated.IsVisible;
                    }
                    else
                    {
                        var comp = updated.Components.FirstOrDefault(c => c.Name == compName);
                        if (comp != null)
                        {
                            comp.IsVisible = !comp.IsVisible;
                            changedName = $"{s.Name}: {(string.IsNullOrEmpty(comp.DisplayName) ? comp.Name : comp.DisplayName)}";
                            newVisibility = comp.IsVisible;
                        }
                    }
                    return updated;
                }
                return s;
            }).ToImmutableList();

            if (changedName != null && newVisibility.HasValue)
            {
                eventBus.Publish(new SeriesStateChangedEvent(changedName, newVisibility.Value, false));
            }

            return state with { ActiveSeries = newList };
        }

        private static WorkspaceState ToggleNarration(WorkspaceState state, string? seriesId, IEventBus eventBus)
        {
            var targetId = seriesId ?? state.FocusedSeriesId;
            string? changedName = null;
            bool? newNarrationState = null;

            var newList = state.ActiveSeries.Select(s =>
            {
                if (s.Id == targetId)
                {
                    var updated = s.Clone();
                    updated.IsAutoNarrated = !updated.IsAutoNarrated;
                    changedName = updated.FriendlyName;
                    newNarrationState = updated.IsAutoNarrated;
                    return updated;
                }
                return s;
            }).ToImmutableList();

            if (changedName != null && newNarrationState.HasValue)
            {
                string msg = newNarrationState.Value
                    ? $"{changedName}, narrating"
                    : $"{changedName}, narration off";
                eventBus.Publish(new AnnouncementEvent(msg, true));
            }

            return state with { ActiveSeries = newList };
        }
    }
}
