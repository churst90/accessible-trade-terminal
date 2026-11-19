using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Interfaces;

namespace AccessibleTrader.Core.Services.ChangeLog
{
    /// <summary>
    /// Alt+U: snapshot, plan, say it, then move the caught-up mark past exactly the entries that
    /// were spoken. Also offers the optional prompt when the user comes back.
    ///
    /// <para>
    /// Marking happens only AFTER the sentence is published, so an interruption or a crash
    /// between the two leaves the items unheard rather than silently skipped.
    /// </para>
    /// </summary>
    public sealed class ChangeLogCatchUpService
    {
        public static readonly TimeSpan ReturnPromptAfter = TimeSpan.FromMinutes(5);

        private readonly ChangeLogStore _store;
        private readonly IEventBus _bus;
        private readonly IWorkspaceStore? _workspace;
        private readonly IAppSettings? _settings;
        private readonly TimeProvider _time;

        private bool _lastPressEndedWithMore;
        private DateTime? _newestAtLastPress;

        public ChangeLogCatchUpService(
            ChangeLogStore store,
            IEventBus bus,
            IWorkspaceStore? workspace = null,
            TimeProvider? time = null,
            IAppSettings? settings = null)
        {
            _store = store;
            _bus = bus;
            _workspace = workspace;
            _time = time ?? TimeProvider.System;
            // The settings facade, when DI supplies it, is read straight from settings.json:
            // a circuit asks for the return prompt before AppStartupService has seeded the
            // workspace store from that same file, so the store alone can still hold the
            // default on the first offer of a session.
            _settings = settings;
        }

        private DateTime NowUtc => _time.GetUtcNow().UtcDateTime;

        public async Task SpeakAsync()
        {
            var doc = await _store.SnapshotAsync().ConfigureAwait(false);
            var plan = ChangeLogRules.Plan(doc, NowUtc);

            DateTime? newest = Newest(doc);
            bool followUp = _lastPressEndedWithMore
                            && plan.Items.Count > 0
                            && newest <= _newestAtLastPress;

            string sentence = ChangeLogWording.Press(doc, plan, NowUtc, followUp);
            _bus.Publish(new AnnouncementEvent(
                sentence, Interrupt: true, Channel: SpeechChannel.Interface));

            if (plan.Items.Count > 0)
                await _store.MarkHeardAsync(plan.Items.Select(e => e.Key)).ConfigureAwait(false);

            _lastPressEndedWithMore = plan.Remaining > 0;
            _newestAtLastPress = newest;
        }

        /// <summary>
        /// Offers the return prompt when the user was away at least five minutes, the setting is
        /// on, and something unheard was recorded after <c>LastPresentUtc</c>. It never marks
        /// anything heard.
        /// </summary>
        public async Task OfferOnReturnAsync(DateTime nowUtc)
        {
            var doc = await _store.SnapshotAsync().ConfigureAwait(false);
            if (doc.LastPresentUtc is not { } lastPresent) return;
            if (nowUtc - lastPresent < ReturnPromptAfter) return;
            bool offer;
            try
            {
                offer = _settings?.ChangeLogOfferOnReturn
                        ?? _workspace?.State.ChangeLogOfferOnReturn
                        ?? true;
            }
            catch
            {
                // An unreadable setting is not an instruction to stay silent; the prompt still
                // only offers and never reads anything.
                offer = true;
            }
            if (!offer) return;

            int changed = (doc.Entries ?? new List<ChangeLogEntry>())
                .Count(e => !e.Heard && e.TimeUtc > lastPresent);
            if (changed == 0) return;

            _bus.Publish(new AnnouncementEvent(
                ChangeLogWording.ReturnPrompt(lastPresent, changed, nowUtc),
                Interrupt: false,
                Channel: SpeechChannel.Interface));
        }

        public Task SetLastPresentAsync(DateTime timeUtc)
            => _store.SetLastPresentAsync(timeUtc);

        private static DateTime? Newest(ChangeLogDocument doc)
        {
            var entries = doc.Entries ?? new List<ChangeLogEntry>();
            return entries.Count == 0 ? null : entries.Max(e => e.TimeUtc);
        }
    }
}
