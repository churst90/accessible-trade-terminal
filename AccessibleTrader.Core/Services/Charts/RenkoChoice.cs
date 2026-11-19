using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Charts
{
    /// <summary>
    /// One validated path for every control that chooses a chart built off the clock (Renko, range
    /// bars, line break): the toolbar, the Go bar and the settings dialog. The refusal is decided HERE, before the store sees an action; the reducer
    /// never refuses. The change sentence is spoken by the state-change announcer from what the
    /// reducer actually did.
    /// </summary>
    public static class RenkoChoice
    {
        /// <summary>
        /// Applies <paramref name="settings"/> to the chart in front. Returns false when nothing
        /// was dispatched; the refusal or "already" sentence has already been published on the
        /// Interface channel.
        /// </summary>
        public static bool TryApply(IWorkspaceStore store, IEventBus eventBus, RenkoSettings settings)
            => TryApply(store, eventBus, PriceChartStyle.Renko, settings);

        /// <summary>
        /// Shows <paramref name="style"/> with <paramref name="settings"/> (the whole record; only
        /// that type's fields are read). Settings first, then the type: a settings change on a
        /// chart of another type rebuilds nothing, and the type change builds once.
        /// </summary>
        public static bool TryApply(IWorkspaceStore store, IEventBus eventBus, PriceChartStyle style, RenkoSettings settings)
        {
            var state = store.State;

            if (state.ChartStyle == style && BuiltCharts.SameFor(style, settings, state.Renko))
            {
                eventBus.Publish(new FeedbackRequestEvent(
                    FeedbackType.Info, BrickChartSpeech.AlreadyFor(style), true,
                    Channel: SpeechChannel.Interface));
                return false;
            }

            if (!BrickChartSpeech.TryPlan(state, settings, out _, out string? refusal, style))
            {
                eventBus.Publish(new FeedbackRequestEvent(
                    FeedbackType.Error, refusal ?? $"That {BuiltCharts.Name(style)} chart cannot be drawn.", true,
                    Channel: SpeechChannel.Interface));
                return false;
            }

            store.Dispatch(new SetRenkoSettingsAction(settings));
            if (state.ChartStyle != style)
                store.Dispatch(new SetChartStyleAction(style, HeikinAshi: false));
            return true;
        }
    }
}
