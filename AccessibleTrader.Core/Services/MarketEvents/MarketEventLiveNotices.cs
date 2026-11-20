using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Notifications;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.MarketEvents
{
    /// <summary>
    /// The live half of market events: countdowns before an event, the release read-out one
    /// minute after it, and the figure when FRED posts it.
    ///
    /// <para>
    /// <b>What is pure and what is not.</b> <see cref="MarketEventNoticePlanner"/> decides what is
    /// due and <see cref="MarketEventNoticeWording"/> writes the sentence. This class owns the
    /// clock, the timer, the fired set, the focused chart's recent closes and the broker reads.
    /// The planner is where the tests go; this class is deliberately thin.
    /// </para>
    ///
    /// <para>
    /// <b>Reads are rare.</b> Positions and working orders are read on the first tick after the
    /// level is turned on and again when a countdown is actually due — never on every tick. The
    /// snapshot they produce supplies the held markets between countdowns. The price samples are
    /// in memory only and come from the focused chart's own live events.
    /// </para>
    ///
    /// <para>
    /// <b>Nothing is said twice.</b> Each (event id, lead), each read-out and each figure try is
    /// kept in <see cref="_fired"/> for the life of the service instance.
    /// </para>
    /// </summary>
    public sealed class MarketEventLiveNotices : IDisposable, IHeldPositionReader
    {
        private const int MaxSamples = 5000;
        private static readonly TimeSpan SampleRetention = TimeSpan.FromMinutes(70);
        private static readonly IReadOnlyCollection<MarketType> AllMarkets = Enum.GetValues<MarketType>();

        private readonly IWorkspaceStore _store;
        private readonly IEventBus _bus;
        private readonly IMarketEventFigureFeed _feed;
        private readonly MarketEventCalendar _calendar;
        private readonly IOrderExecutionService _orders;
        private readonly IPaperTradingProvider _paper;
        private readonly IApiKeyService _apiKeys;
        private readonly ISettingsManager _settings;
        private readonly DemoPolicy _demo;
        private readonly IDataService? _dataService;
        private readonly IUserPresence? _presence;
        private readonly ILogger<MarketEventLiveNotices>? _logger;
        private readonly TimeProvider _time;
        private readonly ITimer _timer;
        private readonly List<IDisposable> _subscriptions = new();
        private readonly HashSet<string> _fired = new(StringComparer.Ordinal);
        private readonly object _firedGate = new();
        private readonly object _samplesGate = new();
        private readonly LinkedList<(ChartIdentity Identity, MarketEventPriceSample Sample)> _samples = new();

        private HeldSnapshot? _held;
        private int _ticking;
        private bool _disposed;

        private sealed record HeldSnapshot(
            DateTime ReadAtUtc,
            IReadOnlyList<MarketEventHeldPosition> Positions,
            IReadOnlyCollection<MarketType> Markets);

        public MarketEventLiveNotices(
            IWorkspaceStore store,
            IEventBus bus,
            IMarketEventFigureFeed feed,
            IOrderExecutionService orders,
            IPaperTradingProvider paper,
            IApiKeyService apiKeys,
            ISettingsManager settings,
            DemoPolicy demo,
            TimeProvider? timeProvider = null,
            MarketEventCalendar? calendar = null,
            IDataService? dataService = null,
            IUserPresence? presence = null,
            ILogger<MarketEventLiveNotices>? logger = null,
            IMarketEventExpectedFeed? expectedFeed = null)
        {
            _store = store;
            _bus = bus;
            _feed = feed;
            _calendar = calendar
                ?? new MarketEventCalendar(feed: feed, expectedFeed: expectedFeed);
            _orders = orders;
            _paper = paper;
            _apiKeys = apiKeys;
            _settings = settings;
            _demo = demo;
            _dataService = dataService;
            _presence = presence;
            _logger = logger;
            _time = timeProvider ?? TimeProvider.System;

            _subscriptions.Add(bus.Subscribe<NewBarEvent>(e => RecordSample(e.ClosedBar.Close)));
            _subscriptions.Add(bus.Subscribe<IntraBarUpdateEvent>(e => RecordSample(e.CurrentBar.Close)));

            _timer = _time.CreateTimer(_ => _ = TickAsync(), null,
                TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }

        private DateTime NowUtc => _time.GetUtcNow().UtcDateTime;

        private bool PaperMode =>
            !_demo.AllowLiveTrading
            || (_settings.GetSetting(SettingsKeys.PaperTradingMode)?.ToObject<bool>() ?? false);

        /// <summary>
        /// One pass of the clock. Internal to the test project; the timer calls it once a
        /// second. It never waits for a previous pass.
        /// </summary>
        internal async Task TickAsync()
        {
            if (_disposed) return;
            if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
            try
            {
                var state = _store.State;
                var level = MarketEventSettings.Level(state);
                var watched = MarketEventSettings.Watched(state);
                // Off normally means no live work at all. A watched event is the exception the
                // user asked for by name, so the tick keeps going while one exists.
                if (level == MarketEventLevel.Off && watched.Count == 0) return;
                if (_presence is { CanReachUser: false }) return;

                var now = NowUtc;
                var kinds = MarketEventSettings.Kinds(state);
                var leads = MarketEventCountdowns.Parse(state.MarketEventsCountdowns);

                // Every event in the window for any market; the planner applies the real
                // open-or-held filter. One pass over the calendar, not one per market.
                var candidates = _calendar.Between(
                    now.AddMinutes(-61), now.AddMinutes(31), AllMarkets);

                var openMarkets = OpenMarkets(state);
                bool hasKey = await KeyNeededAndPresentAsync(now, candidates, level, kinds, watched)
                    .ConfigureAwait(false);

                var planned = MarketEventNoticePlanner.Due(
                    now, candidates, leads, openMarkets,
                    _held?.Markets ?? Array.Empty<MarketType>(), level, kinds,
                    FiredKeys(), hasKey, watched);

                // Positions are read only when a countdown is actually due, and once more
                // around a read-out so a position opened since the countdown is included.
                // Never on a plain tick.
                bool countdownWindow = AnyCountdownDue(now, candidates, leads, level, kinds, watched);
                bool readOutOrFigureDue = AnyReadOutOrFigureDue(now, candidates, level, kinds, watched);
                bool staleCountdown = countdownWindow
                    && (_held == null
                        || (planned.Any(n => n.Kind == MarketEventNoticeKind.Countdown)
                            && now - _held.ReadAtUtc > TimeSpan.FromSeconds(5)));
                bool staleReadOut = readOutOrFigureDue
                    && (_held == null || now - _held.ReadAtUtc > TimeSpan.FromMinutes(5));
                if (staleCountdown || staleReadOut)
                {
                    _held = await ReadHeldAsync(state).ConfigureAwait(false);
                    planned = MarketEventNoticePlanner.Due(
                        now, candidates, leads, openMarkets, _held.Markets, level, kinds,
                        FiredKeys(), hasKey, watched);
                }

                foreach (var notice in planned)
                    await PublishAsync(notice, state, hasKey).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A background notice must never take the terminal down, and there is no
                // error sound or speech for it: the next tick tries again.
                _logger?.LogWarning("A market-event notice tick failed: {Type}.", ex.GetType().Name);
            }
            finally
            {
                Volatile.Write(ref _ticking, 0);
            }
        }

        private async Task PublishAsync(MarketEventNotice notice, WorkspaceState state, bool hasKey)
        {
            switch (notice.Kind)
            {
                case MarketEventNoticeKind.Countdown:
                {
                    IReadOnlyList<MarketEventHeldPosition> positions = _held == null
                        ? Array.Empty<MarketEventHeldPosition>()
                        : _held.Positions
                            .Where(p => p.Market is { } market && notice.Event.Scope.Contains(market))
                            .ToList();
                    Publish(MarketEventNoticeWording.Countdown(notice.Event, notice.Minutes, positions),
                        notice);
                    break;
                }

                case MarketEventNoticeKind.ReadOut:
                {
                    var focused = FocusedMarket(state);
                    var samples = focused is { } market && notice.Event.Scope.Contains(market)
                        ? SamplesFor(state.Identity, notice.Event.MomentUtc)
                        : Array.Empty<MarketEventPriceSample>();
                    bool awaiting = hasKey
                        && MarketEventNoticePlanner.IsFredRelease(notice.Event)
                        && !MarketEventFigures.Published(notice.Event);
                    Publish(MarketEventNoticeWording.ReadOut(notice.Event, samples, awaiting), notice);
                    break;
                }

                case MarketEventNoticeKind.Figure:
                {
                    bool arrived = await _feed.RequestReleaseFigureAsync(
                        SourceOf(notice.Event), notice.Event.Date).ConfigureAwait(false);
                    if (arrived)
                    {
                        var updated = _feed.Events.FirstOrDefault(e => e.Id == notice.Event.Id)
                            ?? notice.Event;
                        updated = _calendar.ApplyExpected(updated);
                        string text = MarketEventNoticeWording.Figure(updated);
                        if (text.Length > 0) Publish(text, notice);
                    }
                    break;
                }
            }

            // Marked only after the attempt: a figure try that failed must not be retried, and
            // a notice that was published must never be repeated.
            lock (_firedGate) _fired.Add(notice.Key);
        }

        private void Publish(string text, MarketEventNotice notice)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _bus.Publish(new MarketEventNoticeEvent(
                text, notice.Event.Importance, notice.Event.Id, notice.Kind));
        }

        private IReadOnlyList<string> FiredKeys()
        {
            lock (_firedGate) return _fired.ToList();
        }

        /// <summary>
        /// The FRED key is only needed for releases whose read-out or figure window is open. A
        /// countdown never asks for it, so a tick outside those minutes costs no key lookup.
        /// </summary>
        private async Task<bool> KeyNeededAndPresentAsync(
            DateTime now, IReadOnlyList<MarketEvent> events,
            MarketEventLevel level, MarketEventKindFilter kinds,
            IReadOnlyCollection<string> watched)
        {
            var watchedSet = new HashSet<string>(watched, StringComparer.Ordinal);
            bool needed = events.Any(e =>
                e.Kind == MarketEventKind.Economy
                && MarketEventNoticePlanner.IsFredRelease(e)
                && !MarketEventFigures.Published(e)
                && e.Precision == MarketEventPrecision.Exact
                && (watchedSet.Contains(e.Id)
                    || (MarketEventLevels.Includes(level, e.Importance) && kinds.Includes(e.Kind)))
                && (InWindow(now, e.MomentUtc.AddSeconds(60), MarketEventNoticePlanner.ReadOutWindow)
                    || MarketEventNoticePlanner.FigureTries.Any(m =>
                        InWindow(now, e.MomentUtc.AddMinutes(m), MarketEventNoticePlanner.Window))));
            if (!needed) return false;
            return await _feed.HasKeyAsync().ConfigureAwait(false);
        }

        private static bool InWindow(DateTime now, DateTime start, TimeSpan length)
            => now >= AsUtc(start) && now < AsUtc(start) + length;

        /// <summary>Is any event's countdown window open, whatever market it touches?</summary>
        private static bool AnyCountdownDue(
            DateTime now, IReadOnlyList<MarketEvent> events, IReadOnlyList<int> leads,
            MarketEventLevel level, MarketEventKindFilter kinds,
            IReadOnlyCollection<string> watched)
        {
            var watchedSet = new HashSet<string>(watched, StringComparer.Ordinal);
            return events.Any(e => Passes(e, level, kinds, watchedSet)
                && EffectiveLeads(e, leads, watchedSet).Any(lead => lead > 0
                    && InWindow(now, e.MomentUtc.AddMinutes(-lead),
                        MarketEventNoticePlanner.Window)));
        }

        /// <summary>Is any event's read-out or figure window open, whatever market it touches?</summary>
        private static bool AnyReadOutOrFigureDue(
            DateTime now, IReadOnlyList<MarketEvent> events,
            MarketEventLevel level, MarketEventKindFilter kinds,
            IReadOnlyCollection<string> watched)
        {
            var watchedSet = new HashSet<string>(watched, StringComparer.Ordinal);
            return events.Any(e => Passes(e, level, kinds, watchedSet)
                && (InWindow(now, e.MomentUtc.AddSeconds(60),
                        MarketEventNoticePlanner.ReadOutWindow)
                    || MarketEventNoticePlanner.FigureTries.Any(m =>
                        InWindow(now, e.MomentUtc.AddMinutes(m),
                            MarketEventNoticePlanner.Window))));
        }

        private static IReadOnlyList<int> EffectiveLeads(
            MarketEvent e, IReadOnlyList<int> leads, HashSet<string> watched)
            => watched.Contains(e.Id) && leads.Count == 0
                ? MarketEventCountdowns.DefaultLeads
                : leads;

        private static bool Passes(
            MarketEvent e, MarketEventLevel level, MarketEventKindFilter kinds,
            HashSet<string> watched)
            => e.Precision == MarketEventPrecision.Exact
               && (watched.Contains(e.Id)
                   || (MarketEventLevels.Includes(level, e.Importance) && kinds.Includes(e.Kind)));

        // ── The focused chart's live closes ───────────────────────────────────

        private void RecordSample(double close)
        {
            if (!double.IsFinite(close) || close <= 0) return;
            var identity = _store.State.Identity;
            var now = NowUtc;
            lock (_samplesGate)
            {
                _samples.AddLast((identity,
                    new MarketEventPriceSample(now, identity.Symbol ?? "", close)));
                while (_samples.Count > MaxSamples) _samples.RemoveFirst();
                var cutoff = now - SampleRetention;
                while (_samples.First != null && _samples.First.Value.Sample.AtUtc < cutoff)
                    _samples.RemoveFirst();
            }
        }

        private IReadOnlyList<MarketEventPriceSample> SamplesFor(ChartIdentity identity, DateTime moment)
        {
            lock (_samplesGate)
            {
                return _samples
                    .Where(x => x.Identity.Equals(identity)
                        && x.Sample.AtUtc >= AsUtc(moment) - MarketEventNoticeWording.BaselineReach)
                    .Select(x => x.Sample)
                    .ToList();
            }
        }

        // ── Markets ──────────────────────────────────────────────────────────

        /// <summary>
        /// The markets a held position is in, as of the last position read. Empty before the
        /// first read (and when no position has been read). The calendar's "only touching"
        /// filter reads this rather than doing a broker read of its own.
        /// </summary>
        public IReadOnlyCollection<MarketType> HeldMarkets
            => _held?.Markets ?? Array.Empty<MarketType>();

        /// <summary>
        /// The markets on the open chart tabs: the focused chart, plus every tab snapshot. The
        /// live notice planner and the calendar's "only touching" filter share this one answer.
        /// </summary>
        public static HashSet<MarketType> OpenMarkets(WorkspaceState state)
        {
            var markets = new HashSet<MarketType>();
            if (MarketOf(state.Identity) is { } focused)
                markets.Add(focused);
            else if (state.Data.Count > 0)
                // A legacy identity with no category ("Spot") has no market of its own; the
                // terminal's selected market is the only honest clue and is what the bar
                // clause already uses for the focused chart.
                markets.Add(state.SelectedMarketType);

            foreach (var tab in state.TabSnapshots ?? ImmutableList<TabSnapshot>.Empty)
                if (MarketOf(tab.Identity) is { } market)
                    markets.Add(market);
            return markets;
        }

        /// <summary>The focused chart's market, or the terminal's selected market when the chart
        /// has no category of its own. The calendar's history price part asks this.</summary>
        public static MarketType? MarketOfChart(WorkspaceState state)
            => FocusedMarket(state);

        private static MarketType? FocusedMarket(WorkspaceState state)
            => MarketOf(state.Identity) ?? state.SelectedMarketType;

        private static MarketType? MarketOf(ChartIdentity identity)
        {
            string category = MarketKey.Category(identity.Market ?? "");
            return Enum.TryParse<MarketType>(category, ignoreCase: true, out var market)
                ? market
                : null;
        }

        // ── Positions ────────────────────────────────────────────────────────

        private async Task<HeldSnapshot> ReadHeldAsync(WorkspaceState state)
        {
            var positions = new List<MarketEventHeldPosition>();
            var markets = new HashSet<MarketType>();
            bool paper = PaperMode;
            var chartIdentities = (state.TabSnapshots ?? ImmutableList<TabSnapshot>.Empty)
                .Select(t => t.Identity)
                .Append(state.Identity)
                .ToList();

            try
            {
                if (paper)
                {
                    var rows = await _paper.GetPositionsAsync().ConfigureAwait(false);
                    var orders = await _paper.GetOpenOrdersAsync().ConfigureAwait(false);
                    var paperIdentities = chartIdentities.Concat(_paper.ExposedIdentities()).ToList();
                    foreach (var p in rows)
                        Add(p, "Paper", paperIdentities, null, orders);
                }
                else
                {
                    List<ApiKeyConfig> keys;
                    try { keys = await _apiKeys.GetAllKeysAsync().ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug("Could not enumerate market-event trading accounts: {Type}.",
                            ex.GetType().Name);
                        keys = new List<ApiKeyConfig>();
                    }

                    foreach (string provider in keys
                        .Where(k => k.IsActive && !k.AllowsWithdrawal)
                        .Select(k => k.Provider)
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        try
                        {
                            if (!await _orders.SupportsTradingAsync(provider).ConfigureAwait(false))
                                continue;
                            var read = await _orders.GetPositionsAsync(provider).ConfigureAwait(false);
                            if (!read.IsOk) continue;
                            var orders = await _orders.GetOpenOrdersAsync(provider).ConfigureAwait(false);
                            var open = orders.OrElse(new List<OpenOrder>());
                            var supported = await SupportedMarketsAsync(provider).ConfigureAwait(false);
                            foreach (var p in read.OrElse(new List<Position>()))
                                Add(p, provider, chartIdentities, supported, open, provider);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogDebug(
                                "Market-event positions for {Provider} failed: {Type}.",
                                provider, ex.GetType().Name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Market-event positions could not be read: {Type}.", ex.GetType().Name);
            }

            return new HeldSnapshot(NowUtc, positions, markets);

            void Add(
                Position p,
                string venue,
                IReadOnlyList<ChartIdentity> identities,
                IReadOnlyCollection<MarketType>? supportedMarkets,
                IReadOnlyList<OpenOrder> orders,
                string? provider = null)
            {
                if (p.Quantity == 0 || !double.IsFinite(p.Quantity)) return;
                var market = ResolveMarket(p.Symbol, identities, supportedMarkets);
                if (market is { } m) markets.Add(m);
                positions.Add(new MarketEventHeldPosition(
                    p.Symbol, p.Quantity, venue, StopPriceFor(p.Symbol, p.Quantity, orders), market));
            }
        }

        /// <summary>A fresh read of every held position, for the headline notices. Not cached:
        /// the caller decides how often (every few minutes).</summary>
        public async Task<IReadOnlyList<MarketEventHeldPosition>> ReadHeldPositionsAsync()
            => (await ReadHeldAsync(_store.State).ConfigureAwait(false)).Positions;

        private static double? StopPriceFor(
            string symbol, double quantity, IReadOnlyList<OpenOrder> orders)
        {
            // A stop-limit counts: it is still a working stop. A protective stop is on the
            // opposite side of the position (a long's stop is a sell). The order's Price is
            // the trigger the venue reports; a zero or missing price is no price to say.
            var side = quantity > 0 ? OrderSide.Sell : OrderSide.Buy;
            foreach (var order in orders)
                if (string.Equals(order.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
                    && order.Side == side
                    && order.Type is OrderType.StopMarket or OrderType.StopLimit
                    && order.Price > 0)
                    return order.Price;
            return null;
        }

        private async Task<IReadOnlyCollection<MarketType>?> SupportedMarketsAsync(string provider)
        {
            if (_dataService == null) return null;
            try
            {
                var markets = await _dataService.GetSupportedMarketsForProviderAsync(provider)
                    .ConfigureAwait(false);
                return markets.Count == 0 ? null : markets;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Which market a position belongs to. A chart on the same symbol is the strongest
        /// answer; otherwise a venue that declares exactly one market is unambiguous. A
        /// multi-market venue with no matching chart stays unknown rather than being guessed —
        /// an unknown position neither gates an event nor is named on one.
        /// </summary>
        private static MarketType? ResolveMarket(
            string symbol,
            IReadOnlyList<ChartIdentity> identities,
            IReadOnlyCollection<MarketType>? supportedMarkets)
        {
            foreach (var identity in identities)
            {
                if (string.Equals(identity.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
                    && MarketOf(identity) is { } market)
                    return market;
            }

            if (supportedMarkets is { Count: 1 }) return supportedMarkets.First();
            return null;
        }

        private static string SourceOf(MarketEvent e)
        {
            int dash = e.Id.IndexOf('-');
            return dash > 0 ? e.Id[..dash] : e.Id;
        }

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        public void Dispose()
        {
            _disposed = true;
            _timer.Dispose();
            foreach (var sub in _subscriptions) sub.Dispose();
        }
    }
}
