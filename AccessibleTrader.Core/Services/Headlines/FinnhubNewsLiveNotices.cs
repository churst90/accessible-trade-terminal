using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Core.Services.Notifications;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Services;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Headlines
{
    /// <summary>
    /// Headlines, stage 3: Finnhub's news, for the stocks and coins the trader touches (the same
    /// set as the Alpaca wire, <see cref="AlpacaNewsLiveNotices.TouchedTargets"/>). Finnhub's
    /// news socket is for paid keys only (checked 2026-11-20: "You don't have access to this
    /// resource"), so this polls: each touched stock's company news in turn, and the crypto list
    /// for every coin at once.
    ///
    /// <list type="bullet">
    /// <item><b>Within the free key's limit.</b> One request every <see cref="CallSpacing"/> at
    /// most — 15 a minute, a quarter of Finnhub's 60, leaving room for the Finnhub chart provider
    /// on the same key and a second window of the web version. Each stock (and the crypto list)
    /// is read every <see cref="MinInterval"/>, or less often when there are more than fifteen to
    /// go round.</item>
    /// <item><b>About the symbol.</b> A stock's list holds many stories that only mention it, so
    /// a story is said only when its headline names the stock or company
    /// (<see cref="NewsStoryWords.NamesCompany"/>); a crypto story only when it names the coin
    /// (<see cref="NewsStoryWords.NamesCoin"/>).</item>
    /// <item><b>The first look is a baseline</b>, as with the filings: what a symbol's first read
    /// finds is marked seen, except stories published in the last
    /// <see cref="EdgarHeadlineTracker.FirstLookReach"/>.</item>
    /// <item><b>Said through the shared gate</b> (<see cref="NewsHeadlineGate"/>), so a story
    /// already heard from Alpaca, or from another publisher in other words, is not said again,
    /// and a symbol's stories from both wires are gathered into one sentence.</item>
    /// <item><b>Off means no requests</b>: the level Off, the news switch off, no Finnhub key, or
    /// nothing touched. A refused key waits <see cref="RefusedKeyWait"/>; "too many requests"
    /// (429) waits <see cref="RateLimitWait"/>.</item>
    /// </list>
    /// </summary>
    public sealed class FinnhubNewsLiveNotices : IDisposable
    {
        public static readonly TimeSpan CallSpacing = TimeSpan.FromSeconds(4);
        public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan NoKeyRecheck = TimeSpan.FromMinutes(1);
        public static readonly TimeSpan RefusedKeyWait = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan RateLimitWait = TimeSpan.FromMinutes(1);

        /// <summary>The crypto list's job name; stocks are their tickers.</summary>
        internal const string CryptoJob = "*crypto";

        private readonly IWorkspaceStore _store;
        private readonly IEventBus _bus;
        private readonly IFinnhubNewsClient _client;
        private readonly NewsHeadlineGate _gate;
        private readonly ICompanyNameSource? _names;
        private readonly IHeldPositionReader? _held;
        private readonly IUserPresence? _presence;
        private readonly ILogger<FinnhubNewsLiveNotices>? _logger;
        private readonly TimeProvider _time;
        private readonly ITimer _timer;

        private readonly Dictionary<string, DateTime> _nextReadUtc = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _baselined = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastCallUtc = DateTime.MinValue;
        private DateTime _pausedUntilUtc = DateTime.MinValue;
        private IReadOnlyList<MarketEventHeldPosition> _heldPositions = Array.Empty<MarketEventHeldPosition>();
        private DateTime _heldReadAtUtc = DateTime.MinValue;
        private int _ticking;
        private bool _disposed;

        public FinnhubNewsLiveNotices(
            IWorkspaceStore store,
            IEventBus bus,
            IFinnhubNewsClient client,
            NewsHeadlineGate gate,
            ICompanyNameSource? names = null,
            IHeldPositionReader? held = null,
            TimeProvider? timeProvider = null,
            IUserPresence? presence = null,
            ILogger<FinnhubNewsLiveNotices>? logger = null)
        {
            _store = store;
            _bus = bus;
            _client = client;
            _gate = gate;
            _names = names;
            _held = held;
            _presence = presence;
            _logger = logger;
            _time = timeProvider ?? TimeProvider.System;
            _timer = _time.CreateTimer(_ => _ = TickAsync(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        private DateTime NowUtc => _time.GetUtcNow().UtcDateTime;

        /// <summary>The gate this wire says through, for the tests.</summary>
        internal NewsHeadlineGate Gate => _gate;

        /// <summary>Requests sent, for the tests.</summary>
        internal int Calls { get; private set; }

        /// <summary>One pass of the clock: at most one request. Internal to the test project; the
        /// timer calls it every second and it never waits for a previous pass.</summary>
        internal async Task TickAsync()
        {
            if (_disposed) return;
            if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
            try
            {
                var state = _store.State;
                var level = MarketEventLevels.Parse(state.HeadlinesLevel);
                if (level == MarketEventLevel.Off || !state.HeadlinesNewsWire)
                {
                    _nextReadUtc.Clear();
                    _baselined.Clear();
                    _pausedUntilUtc = DateTime.MinValue;
                    _gate.Forget();
                    return;
                }

                var now = NowUtc;
                if (_held != null && now - _heldReadAtUtc >= EdgarHeadlineLiveNotices.HeldRefresh)
                {
                    _heldReadAtUtc = now;
                    try { _heldPositions = await _held.ReadHeldPositionsAsync().ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug("Finnhub news positions could not be read: {Type}.", ex.GetType().Name);
                    }
                }

                var targets = AlpacaNewsLiveNotices.TouchedTargets(state, _heldPositions);
                var jobs = Jobs(targets);
                // A symbol dropped and touched again starts with a fresh baseline.
                foreach (var gone in _nextReadUtc.Keys.Where(k => !jobs.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
                {
                    _nextReadUtc.Remove(gone);
                    _baselined.Remove(gone);
                }

                if (jobs.Count > 0 && now >= _pausedUntilUtc && now - _lastCallUtc >= CallSpacing)
                {
                    var job = jobs
                        .Select(j => (Job: j, Due: _nextReadUtc.TryGetValue(j, out var d) ? d : DateTime.MinValue))
                        .Where(x => x.Due <= now)
                        .OrderBy(x => x.Due)
                        .Select(x => x.Job)
                        .FirstOrDefault();
                    if (job != null)
                        await ReadAsync(job, targets, level, Interval(jobs.Count), now).ConfigureAwait(false);
                }

                if (_presence is { CanReachUser: false }) return;
                foreach (var h in _gate.Due(now, targets.Select(t => t.Label).ToList()))
                    _bus.Publish(new HeadlineEvent(h.Text, h.Importance, h.Id, h.Symbol));
            }
            catch (Exception ex)
            {
                // A background notice must never take the terminal down; the next tick tries again.
                _logger?.LogWarning("A Finnhub news tick failed: {Type}.", ex.GetType().Name);
            }
            finally
            {
                Volatile.Write(ref _ticking, 0);
            }
        }

        /// <summary>The stocks by ticker, then the crypto list if any coin is touched.</summary>
        internal static IReadOnlyList<string> Jobs(IReadOnlyList<NewsTarget> targets)
        {
            var jobs = targets.Where(t => !t.Coin).Select(t => t.Label).ToList();
            if (targets.Any(t => t.Coin)) jobs.Add(CryptoJob);
            return jobs;
        }

        /// <summary>How often each list is read: every <see cref="MinInterval"/>, or once per
        /// round of <see cref="CallSpacing"/> when there are more lists than fit in it.</summary>
        public static TimeSpan Interval(int jobs)
            => TimeSpan.FromTicks(Math.Max(MinInterval.Ticks, CallSpacing.Ticks * Math.Max(1, jobs)));

        private async Task ReadAsync(string job, IReadOnlyList<NewsTarget> targets, MarketEventLevel level,
                                     TimeSpan interval, DateTime now)
        {
            bool crypto = job == CryptoJob;
            _lastCallUtc = now;
            Calls++;
            NewsReadResult read;
            if (crypto)
            {
                read = await _client.CryptoNewsAsync().ConfigureAwait(false);
            }
            else
            {
                var today = NewYorkDate(now);
                read = await _client.CompanyNewsAsync(job, today.AddDays(-1), today).ConfigureAwait(false);
            }

            switch (read.Status)
            {
                case NewsReadStatus.NoKey:
                    // Nothing was sent; check again soon in case a key was added.
                    Calls--;
                    _pausedUntilUtc = now + NoKeyRecheck;
                    return;
                case NewsReadStatus.Refused:
                    _logger?.LogWarning("Finnhub refused the key in API Keys; news waits {Minutes} minutes.",
                        (int)RefusedKeyWait.TotalMinutes);
                    _pausedUntilUtc = now + RefusedKeyWait;
                    return;
                case NewsReadStatus.RateLimited:
                    _pausedUntilUtc = now + RateLimitWait;
                    return;
                case NewsReadStatus.Failed:
                    _nextReadUtc[job] = now + interval;
                    return;
            }
            _nextReadUtc[job] = now + interval;

            bool first = _baselined.Add(job);
            string? name = null;
            if (!crypto && _names != null)
            {
                try { name = await _names.CompanyNameAsync(job).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    _logger?.LogDebug("A company name could not be read: {Type}.", ex.GetType().Name);
                }
            }

            foreach (var story in read.Stories.OrderBy(s => s.CreatedUtc))
            {
                var hits = crypto
                    ? targets.Where(t => t.Coin && NewsStoryWords.NamesCoin(story.Headline, t.Label)).ToList()
                    : targets.Where(t => !t.Coin && NewsStoryWords.StockKey(t.Label) == NewsStoryWords.StockKey(job)
                                         && NewsStoryWords.NamesCompany(story.Headline, job, name)).ToList();
                if (hits.Count == 0) continue;
                if (first && story.CreatedUtc < now - EdgarHeadlineTracker.FirstLookReach)
                {
                    _gate.MarkSeen(story);
                    continue;
                }
                var importance = NewsStoryWords.Importance(story);
                if (!MarketEventLevels.Includes(level, importance))
                {
                    _gate.MarkSeen(story);
                    continue;
                }
                _gate.Offer(story, hits, importance, now);
            }
        }

        /// <summary>Today's date in New York, the day Finnhub's company-news dates mean.</summary>
        internal static DateOnly NewYorkDate(DateTime utc)
            => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), ExchangeTime.UsEastern));

        public void Dispose()
        {
            _disposed = true;
            _timer.Dispose();
        }
    }
}
