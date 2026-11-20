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

        /// <summary>
        /// How long the away mark waits on the venues for the open positions. Leaving never
        /// waits on it: the read runs after the mark is written.
        /// </summary>
        public static readonly TimeSpan PositionsReadAtLeaving = TimeSpan.FromSeconds(30);

        /// <summary>How long Alt+U waits on the venues before saying the positions could not be read.</summary>
        public static readonly TimeSpan PositionsReadAtPress = TimeSpan.FromSeconds(5);

        private readonly ChangeLogStore _store;
        private readonly IEventBus _bus;
        private readonly IWorkspaceStore? _workspace;
        private readonly IAppSettings? _settings;
        private readonly TimeProvider _time;
        private readonly IHeldPositionsReader? _positions;

        private bool _lastPressEndedWithMore;
        private DateTime? _newestAtLastPress;

        public ChangeLogCatchUpService(
            ChangeLogStore store,
            IEventBus bus,
            IWorkspaceStore? workspace = null,
            TimeProvider? time = null,
            IAppSettings? settings = null,
            IHeldPositionsReader? positions = null)
        {
            _positions = positions;
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

            string? positions = followUp ? null : await PositionsSinceAsync(doc).ConfigureAwait(false);
            string sentence = ChangeLogWording.Press(doc, plan, NowUtc, followUp, positions);
            _bus.Publish(new AnnouncementEvent(
                sentence, Interrupt: true, Channel: SpeechChannel.Interface));

            if (plan.Items.Count > 0)
                await _store.MarkHeardAsync(plan.Items.Select(e => e.Key)).ConfigureAwait(false);
            if (doc.AwayPositions != null && !followUp)
                await _store.ClearAwayPositionsAsync().ConfigureAwait(false);

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

        /// <summary>
        /// Writes the away mark, then (without the caller waiting) reads the open positions to
        /// compare at the next Alt+U. The desktop marks on its UI thread while closing, so the
        /// read never holds that up; an app that exits first keeps the mark and loses only the
        /// positions.
        /// </summary>
        public async Task SetLastPresentAsync(DateTime timeUtc)
        {
            await _store.SetLastPresentAsync(timeUtc).ConfigureAwait(false);
            if (_positions != null)
                PositionsSnapshot = Task.Run(() => KeepPositionsAsync(timeUtc));
        }

        /// <summary>The last leave's positions read, for tests to wait on.</summary>
        internal Task PositionsSnapshot { get; private set; } = Task.CompletedTask;

        private async Task KeepPositionsAsync(DateTime leftUtc)
        {
            try
            {
                if (await ReadPositionsAsync(PositionsReadAtLeaving).ConfigureAwait(false) is { } held)
                    await _store.SetAwayPositionsAsync(leftUtc, held).ConfigureAwait(false);
            }
            catch
            {
                // Nothing kept means nothing said: never a partial comparison.
            }
        }

        private async Task<string?> PositionsSinceAsync(ChangeLogDocument doc)
        {
            if (doc.AwayPositions is not { } away || _positions == null) return null;
            var now = await ReadPositionsAsync(PositionsReadAtPress).ConfigureAwait(false);
            return AwayPositionsWording.Sentence(away.Positions, now, away.TimeUtc);
        }

        private async Task<IReadOnlyList<HeldPositionMark>?> ReadPositionsAsync(TimeSpan limit)
        {
            try
            {
                var read = _positions!.ReadAsync();
                var done = await Task.WhenAny(read, Task.Delay(limit)).ConfigureAwait(false);
                return done == read ? await read.ConfigureAwait(false) : null;
            }
            catch
            {
                return null;
            }
        }

        private static DateTime? Newest(ChangeLogDocument doc)
        {
            var entries = doc.Entries ?? new List<ChangeLogEntry>();
            return entries.Count == 0 ? null : entries.Max(e => e.TimeUtc);
        }
    }
}
