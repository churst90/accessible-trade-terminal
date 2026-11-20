using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Core.Services.Notifications;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Headlines
{
    /// <summary>
    /// Headlines, stage 1: a US company's SEC filings, said the minute EDGAR accepts them, for
    /// the stocks on the open charts and in the held positions.
    ///
    /// <para>
    /// <b>What is pure and what is not.</b> <see cref="EdgarHeadlineTracker"/> decides what is new
    /// and <see cref="EdgarHeadlineWording"/> writes the sentence. This class owns the clock, the
    /// polling and the position reads, and publishes <see cref="HeadlineEvent"/>.
    /// </para>
    ///
    /// <para>
    /// <b>How often.</b> Each touched stock's Atom feed is read every
    /// <see cref="OpenInterval"/> while EDGAR accepts filings (weekdays 6 AM to 10 PM New York)
    /// and every <see cref="ClosedInterval"/> otherwise, at most <see cref="PollsPerTick"/> reads
    /// per five-second tick — far under the SEC's ten requests a second. A symbol that is not in
    /// EDGAR's ticker index (a crypto pair, most ETFs) is asked about again only hourly.
    /// </para>
    ///
    /// <para>
    /// <b>Off means no work.</b> At the default level (Off) nothing is fetched at all, and turning
    /// it off forgets every baseline so turning it back on does not read out the gap.
    /// </para>
    ///
    /// <para>
    /// <b>Known gap</b>, shared with the market-event notices: nothing is said while no window is
    /// open (<see cref="IUserPresence.CanReachUser"/>); the change log only records what was said.
    /// </para>
    /// </summary>
    public sealed class EdgarHeadlineLiveNotices : IDisposable
    {
        public static readonly TimeSpan OpenInterval = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan ClosedInterval = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan NotAFilerInterval = TimeSpan.FromHours(1);
        public static readonly TimeSpan HeldRefresh = TimeSpan.FromMinutes(5);
        public const int PollsPerTick = 2;

        private readonly IWorkspaceStore _store;
        private readonly IEventBus _bus;
        private readonly ICompanyFilingSource _source;
        private readonly IHeldPositionReader? _held;
        private readonly IUserPresence? _presence;
        private readonly ILogger<EdgarHeadlineLiveNotices>? _logger;
        private readonly TimeProvider _time;
        private readonly ITimer _timer;

        private EdgarHeadlineTracker _tracker = new();
        private readonly Dictionary<string, DateTime> _nextPoll = new(StringComparer.OrdinalIgnoreCase);
        private IReadOnlyList<MarketEventHeldPosition> _heldPositions = Array.Empty<MarketEventHeldPosition>();
        private DateTime _heldReadAtUtc = DateTime.MinValue;
        private int _ticking;
        private bool _disposed;

        public EdgarHeadlineLiveNotices(
            IWorkspaceStore store,
            IEventBus bus,
            ICompanyFilingSource source,
            IHeldPositionReader? held = null,
            TimeProvider? timeProvider = null,
            IUserPresence? presence = null,
            ILogger<EdgarHeadlineLiveNotices>? logger = null)
        {
            _store = store;
            _bus = bus;
            _source = source;
            _held = held;
            _presence = presence;
            _logger = logger;
            _time = timeProvider ?? TimeProvider.System;
            _timer = _time.CreateTimer(_ => _ = TickAsync(), null,
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        private DateTime NowUtc => _time.GetUtcNow().UtcDateTime;

        /// <summary>One pass of the clock. Internal to the test project; the timer calls it every
        /// five seconds and it never waits for a previous pass.</summary>
        internal async Task TickAsync()
        {
            if (_disposed) return;
            if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
            try
            {
                var state = _store.State;
                var level = MarketEventLevels.Parse(state.HeadlinesLevel);
                if (level == MarketEventLevel.Off)
                {
                    if (_nextPoll.Count > 0)
                    {
                        _tracker = new EdgarHeadlineTracker();
                        _nextPoll.Clear();
                    }
                    return;
                }
                if (_presence is { CanReachUser: false }) return;

                var now = NowUtc;
                if (_held != null && now - _heldReadAtUtc >= HeldRefresh)
                {
                    _heldReadAtUtc = now;
                    try { _heldPositions = await _held.ReadHeldPositionsAsync().ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug("Headline positions could not be read: {Type}.", ex.GetType().Name);
                    }
                }

                var touched = TouchedStocks(state, _heldPositions);
                _tracker.Keep(touched);
                foreach (var gone in _nextPoll.Keys.Where(t => !touched.Contains(t)).ToList())
                    _nextPoll.Remove(gone);

                int polls = 0;
                foreach (var ticker in touched)
                {
                    if (polls >= PollsPerTick) break;
                    if (_nextPoll.TryGetValue(ticker, out var due) && due > now) continue;
                    polls++;
                    await PollAsync(ticker, level, now).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // A background notice must never take the terminal down; the next tick tries again.
                _logger?.LogWarning("A headline tick failed: {Type}.", ex.GetType().Name);
            }
            finally
            {
                Volatile.Write(ref _ticking, 0);
            }
        }

        private async Task PollAsync(string ticker, MarketEventLevel level, DateTime now)
        {
            if (!await _source.IsFilerAsync(ticker).ConfigureAwait(false))
            {
                _nextPoll[ticker] = now + NotAFilerInterval;
                return;
            }

            _nextPoll[ticker] = now + Interval(now);
            var filings = await _source.RecentFilingsAsync(ticker).ConfigureAwait(false);
            // A failed read teaches the tracker nothing: the next read is compared with the last
            // good one, so a filing accepted during an outage is still said (within LateReach).
            if (filings == null) return;

            var fresh = _tracker.Observe(ticker, filings, now);
            if (fresh.Count == 0) return;
            if (EdgarHeadlineWording.Compose(ticker, fresh, level, now) is { } headline)
                _bus.Publish(new HeadlineEvent(headline.Text, headline.Importance, headline.Id, headline.Symbol));
        }

        /// <summary>How long until a filer is read again: a minute while EDGAR accepts filings
        /// (weekdays 06:00–22:00 New York), a quarter hour otherwise.</summary>
        public static TimeSpan Interval(DateTime nowUtc)
        {
            var ny = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
                ExchangeTime.UsEastern);
            bool weekday = ny.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
            bool open = weekday && ny.Hour >= 6 && ny.Hour < 22;
            return open ? OpenInterval : ClosedInterval;
        }

        /// <summary>
        /// The stock tickers the trader touches: every open chart whose market is Stock (the
        /// focused chart with no category of its own counts when the terminal's selected market
        /// is Stock), and every held position classified as Stock. In EDGAR's form ("AAPL"),
        /// focused chart first, without repeats.
        /// </summary>
        public static IReadOnlyList<string> TouchedStocks(
            WorkspaceState state, IEnumerable<MarketEventHeldPosition> held)
        {
            var tickers = new List<string>();
            void Add(string? symbol)
            {
                string t = EdgarCompanyProfileSource.Normalise(symbol ?? "");
                if (t.Length > 0 && !tickers.Contains(t, StringComparer.OrdinalIgnoreCase))
                    tickers.Add(t);
            }

            var focusedMarket = MarketOf(state.Identity);
            if (focusedMarket == MarketType.Stock
                || (focusedMarket == null && state.Data.Count > 0 && state.SelectedMarketType == MarketType.Stock))
                Add(state.Identity.Symbol);

            foreach (var tab in state.TabSnapshots ?? ImmutableList<TabSnapshot>.Empty)
                if (MarketOf(tab.Identity) == MarketType.Stock)
                    Add(tab.Identity.Symbol);

            foreach (var p in held)
                if (p.Market == MarketType.Stock)
                    Add(p.Symbol);

            return tickers;
        }

        private static MarketType? MarketOf(ChartIdentity identity)
        {
            string category = MarketKey.Category(identity.Market ?? "");
            return Enum.TryParse<MarketType>(category, ignoreCase: true, out var market)
                ? market
                : null;
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Dispose();
        }
    }
}
