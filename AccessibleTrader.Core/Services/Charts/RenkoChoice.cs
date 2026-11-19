using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Charts
{
    /// <summary>
    /// One validated path for every control that chooses Renko: the toolbar, the Go bar and the
    /// settings dialog. The refusal is decided HERE, before the store sees an action; the reducer
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
        {
            var state = store.State;

            if (state.ChartStyle.IsBrickStyle() && settings == state.Renko)
            {
                eventBus.Publish(new FeedbackRequestEvent(
                    FeedbackType.Info, BrickChartSpeech.Already, true,
                    Channel: SpeechChannel.Interface));
                return false;
            }

            if (!BrickChartSpeech.TryPlan(state, settings, out _, out string? refusal))
            {
                eventBus.Publish(new FeedbackRequestEvent(
                    FeedbackType.Error, refusal ?? "That Renko box cannot be drawn.", true,
                    Channel: SpeechChannel.Interface));
                return false;
            }

            store.Dispatch(new SetRenkoSettingsAction(settings));
            if (!state.ChartStyle.IsBrickStyle())
                store.Dispatch(new SetChartStyleAction(PriceChartStyle.Renko, HeikinAshi: false));
            return true;
        }
    }
}
