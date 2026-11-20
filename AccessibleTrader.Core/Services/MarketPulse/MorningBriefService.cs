using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.ChangeLog;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.MarketPulse
{
    /// <summary>The pulse as one sentence, for a caller that says it inside its own.</summary>
    public interface IMarketPulseReader
    {
        Task<string> SentenceAsync(WorkspaceState state);
    }

    /// <summary>
    /// The morning brief (Go bar BRIEF): what changed, the pulse, then today's market events, as
    /// one sentence. Each part is the one its own key says (Alt+U, Alt+P, the calendar); the
    /// brief only gathers them, so the words can never drift apart. What changed is marked heard
    /// only after the brief is said, as Alt+U does.
    /// </summary>
    public sealed class MorningBriefService
    {
        /// <summary>Today's events said in the brief; the rest are counted.</summary>
        public const int MostEvents = 6;

        private static readonly IReadOnlyList<MarketType> AllMarkets = Enum.GetValues<MarketType>();

        private readonly ChangeLogCatchUpService _catchUp;
        private readonly IMarketPulseReader _pulse;
        private readonly IWorkspaceStore _store;
        private readonly IEventBus _bus;
        private readonly MarketEventCalendar _calendar;
        private readonly MarketEventLiveNotices? _live;
        private readonly TimeProvider _time;
        private readonly ILogger<MorningBriefService>? _logger;
        private int _gathering;

        public MorningBriefService(
            ChangeLogCatchUpService catchUp,
            IMarketPulseReader pulse,
            IWorkspaceStore store,
            IEventBus bus,
            IMarketEventFigureFeed? feed = null,
            IMarketEventExpectedFeed? expectedFeed = null,
            IMarketEventEarningsFeed? earningsFeed = null,
            MarketEventLiveNotices? live = null,
            TimeProvider? time = null,
            ILogger<MorningBriefService>? logger = null,
            MarketEventCalendar? calendar = null)
        {
            _catchUp = catchUp;
            _pulse = pulse;
            _store = store;
            _bus = bus;
            _calendar = calendar
                ?? new MarketEventCalendar(feed: feed, expectedFeed: expectedFeed, earningsFeed: earningsFeed);
            _live = live;
            _time = time ?? TimeProvider.System;
            _logger = logger;
        }

        private DateTime NowUtc => _time.GetUtcNow().UtcDateTime;

        public async Task SpeakAsync()
        {
            if (Interlocked.CompareExchange(ref _gathering, 1, 0) != 0)
            {
                Publish("Still gathering the brief.");
                return;
            }
            try
            {
                Publish("Gathering the brief.");
                var state = _store.State;
                var changes = await _catchUp.PrepareAsync().ConfigureAwait(false);
                string pulse = await PulseAsync(state).ConfigureAwait(false);
                string events = EventsSentence(TodaysEvents(state), NowUtc);

                Publish($"{changes.Sentence} {pulse} {events}");
                await changes.MarkHeardAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Morning brief failed: {Type}.", ex.GetType().Name);
                Publish("The brief is unavailable.");
            }
            finally
            {
                Volatile.Write(ref _gathering, 0);
            }
        }

        private async Task<string> PulseAsync(WorkspaceState state)
        {
            try { return await _pulse.SentenceAsync(state).ConfigureAwait(false); }
            catch { return "The market pulse is unavailable."; }
        }

        /// <summary>
        /// Today, on the clock the terminal speaks (F12: local or UTC), at the market-events level
        /// you chose and only for the markets you have open or hold, as the live notices pick
        /// them. With market events off, the brief still says the important ones: you asked.
        /// </summary>
        internal IReadOnlyList<MarketEvent> TodaysEvents(WorkspaceState state)
        {
            var (from, to) = Today(NowUtc);
            var level = MarketEventSettings.Level(state);
            if (level == MarketEventLevel.Off) level = MarketEventLevel.ImportantOnly;
            var kinds = MarketEventSettings.Kinds(state);
            var open = MarketEventLiveNotices.OpenMarkets(state);
            var held = _live?.HeldMarkets ?? Array.Empty<MarketType>();

            return _calendar.Between(from, to, AllMarkets)
                .Where(e => MarketEventLevels.Includes(level, e.Importance))
                .Where(e => kinds.Includes(e.Kind))
                .Where(e => MarketEventNoticePlanner.Touches(e, open, held))
                .OrderBy(e => e.MomentUtc)
                .ThenBy(e => e.Importance)
                .ToList();
        }

        /// <summary>The day the spoken clock is on, as a UTC range.</summary>
        internal static (DateTime From, DateTime To) Today(DateTime nowUtc)
        {
            DateTime day = SpeechTimeFormatter.ToDisplay(nowUtc).Date;
            if (SpeechTimeFormatter.UseUtc)
            {
                var start = DateTime.SpecifyKind(day, DateTimeKind.Utc);
                return (start, start.AddDays(1));
            }
            return (TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day, DateTimeKind.Unspecified), TimeZoneInfo.Local),
                TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.AddDays(1), DateTimeKind.Unspecified), TimeZoneInfo.Local));
        }

        /// <summary>"Today: 8:30, CPI, important, .... 14:00, Fed decision, .... 2 more in the calendar."</summary>
        public static string EventsSentence(IReadOnlyList<MarketEvent> today, DateTime nowUtc)
        {
            if (today.Count == 0) return "No market events today.";
            var lines = today.Take(MostEvents).Select(e => MarketEventCalendarWording.EventLine(e, nowUtc));
            string said = "Today: " + string.Join(" ", lines);
            int more = today.Count - MostEvents;
            return more > 0 ? $"{said} {more} more in the calendar." : said;
        }

        private void Publish(string message)
            => _bus.Publish(new AnnouncementEvent(message, Interrupt: true, Channel: SpeechChannel.Interface));
    }
}
