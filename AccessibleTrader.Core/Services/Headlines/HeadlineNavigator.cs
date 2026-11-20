using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Headlines
{
    /// <summary>
    /// Fills <see cref="HeadlineHistory"/> with the focused chart's own headlines back
    /// <see cref="Reach"/>, so they can be placed on the bars they arrived in: a stock's Finnhub
    /// company news (only stories whose headline names the company, as the live wire says them)
    /// and its SEC filings; a coin's stories in Finnhub's crypto list (only the newest hundred or
    /// so — Finnhub keeps no older crypto list on a free key).
    ///
    /// <para>
    /// One read per symbol per <see cref="Refresh"/>, started by the first key or arrow that asks
    /// for the chart's headlines, never while the Headlines level is Off. The Finnhub half needs a
    /// Finnhub key and the news switch; the filings need neither.
    /// </para>
    /// </summary>
    public sealed class HeadlineBackfill
    {
        public static readonly TimeSpan Refresh = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan Reach = TimeSpan.FromDays(30);
        public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(2);

        private readonly HeadlineHistory _history;
        private readonly IFinnhubNewsClient? _news;
        private readonly ICompanyFilingSource? _filings;
        private readonly ICompanyNameSource? _names;
        private readonly TimeProvider _time;
        private readonly ILogger<HeadlineBackfill>? _logger;
        private readonly object _lock = new();
        private readonly Dictionary<string, DateTime> _lastUtc = new(StringComparer.OrdinalIgnoreCase);

        public HeadlineBackfill(
            HeadlineHistory history,
            IFinnhubNewsClient? news = null,
            ICompanyFilingSource? filings = null,
            ICompanyNameSource? names = null,
            TimeProvider? timeProvider = null,
            ILogger<HeadlineBackfill>? logger = null)
        {
            _history = history;
            _news = news;
            _filings = filings;
            _names = names;
            _time = timeProvider ?? TimeProvider.System;
            _logger = logger;
        }

        /// <summary>The last read started, for the tests to await.</summary>
        internal Task LastFill { get; private set; } = Task.CompletedTask;

        public void RequestRefresh(WorkspaceState state, NewsTarget target)
        {
            if (MarketEventLevels.Parse(state.HeadlinesLevel) == MarketEventLevel.Off) return;
            var now = _time.GetUtcNow().UtcDateTime;
            string key = (target.Coin ? "coin:" : "stock:") + target.Label;
            lock (_lock)
            {
                if (_lastUtc.TryGetValue(key, out var last) && now - last < Refresh) return;
                _lastUtc[key] = now;
            }

            var first = state.Data.Count > 0 ? state.Data[0].Date : now;
            var from = first > now - Reach ? first : now - Reach;
            bool wire = state.HeadlinesNewsWire;
            LastFill = Task.Run(() => FillAsync(target, key, from, now, wire));
        }

        private async Task FillAsync(NewsTarget target, string key, DateTime fromUtc, DateTime now, bool wire)
        {
            bool failed = false;
            try
            {
                if (!target.Coin && _filings != null && await _filings.IsFilerAsync(target.Label).ConfigureAwait(false))
                {
                    var filings = await _filings.RecentFilingsAsync(target.Label).ConfigureAwait(false);
                    if (filings == null) failed = true;
                    else
                        foreach (var f in filings.Where(f => f.AcceptedUtc >= fromUtc))
                            _history.Add(target.Label, f);
                }

                if (wire && _news != null)
                {
                    NewsReadResult read;
                    string? name = null;
                    if (target.Coin)
                    {
                        read = await _news.CryptoNewsAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        if (_names != null)
                            name = await _names.CompanyNameAsync(target.Label).ConfigureAwait(false);
                        read = await _news.CompanyNewsAsync(target.Label,
                            FinnhubNewsLiveNotices.NewYorkDate(fromUtc), FinnhubNewsLiveNotices.NewYorkDate(now))
                            .ConfigureAwait(false);
                    }

                    if (read.Status is NewsReadStatus.Failed or NewsReadStatus.RateLimited) failed = true;
                    foreach (var story in read.Stories)
                    {
                        if (story.CreatedUtc < fromUtc) continue;
                        bool about = target.Coin
                            ? NewsStoryWords.NamesCoin(story.Headline, target.Label)
                            : NewsStoryWords.NamesCompany(story.Headline, target.Label, name);
                        if (about)
                            _history.Add(story, new[] { target }, NewsStoryWords.Importance(story));
                    }
                }
            }
            catch (Exception ex)
            {
                failed = true;
                _logger?.LogDebug("Headlines for a chart could not be read: {Type}.", ex.GetType().Name);
            }

            // A failed read is tried again soon rather than in half an hour.
            if (failed)
                lock (_lock) _lastUtc[key] = now - Refresh + RetryAfterFailure;
        }
    }

    /// <summary>
    /// Period / Shift+period: jump to the next / previous bar carrying a headline for the chart's
    /// stock or coin that passes the Headlines level — as apostrophe does for market events and
    /// comma for chart formations. Routed as ordinary navigation with <c>IsJump</c>, so the bar is
    /// read with the same composition the arrows use, headline clause included.
    /// </summary>
    public sealed class HeadlineNavigator
    {
        private readonly IWorkspaceStore _store;
        private readonly IEventBus _eventBus;
        private readonly IHeadlineBarMap _map;

        public HeadlineNavigator(IWorkspaceStore store, IEventBus eventBus, IHeadlineBarMap map)
        {
            _store = store;
            _eventBus = eventBus;
            _map = map;
        }

        public void Jump(bool forward)
        {
            var state = _store.State;
            var level = MarketEventLevels.Parse(state.HeadlinesLevel);
            if (level == MarketEventLevel.Off)
            {
                Say("Headlines are off. Turn them on in Settings, Narration.");
                return;
            }
            if (state.Data == null || state.Data.Count == 0)
            {
                Say(forward ? "No later headlines." : "No earlier headlines.");
                return;
            }
            if (HeadlineChart.TargetOf(state) == null)
            {
                Say("Headlines are for stocks and coins; this chart has none.");
                return;
            }

            int current = state.CurrentDataIndex;
            var bars = _map.For(state)
                .Where(p => MarketEventLevels.Includes(level, p.Record.Importance))
                .Select(p => p.BarIndex);
            int target = forward
                ? bars.Where(b => b > current).DefaultIfEmpty(-1).Min()
                : bars.Where(b => b < current).DefaultIfEmpty(-1).Max();
            if (target < 0)
            {
                Say(forward ? "No later headlines." : "No earlier headlines.");
                return;
            }

            _store.Dispatch(new NavigateAction(target));
            _store.Dispatch(new SetInteractionContextAction(InteractionContext.Component));
            _eventBus.Publish(new FeedbackRequestEvent(
                FeedbackType.Navigation, "", true, IsXMove: true, IsJump: true));
        }

        /// <summary>Enter on a NEWS list line: the chart to that headline's bar, when it is on
        /// the loaded bars of this chart. False otherwise, so the list can say why.</summary>
        public bool JumpTo(HeadlineRecord record)
        {
            var state = _store.State;
            var placement = _map.For(state).FirstOrDefault(p => p.Record.Key == record.Key
                && string.Equals(p.Record.Symbol, record.Symbol, StringComparison.OrdinalIgnoreCase));
            if (placement == null) return false;
            _store.Dispatch(new NavigateAction(placement.BarIndex));
            _store.Dispatch(new SetInteractionContextAction(InteractionContext.Component));
            _eventBus.Publish(new FeedbackRequestEvent(
                FeedbackType.Navigation, "", true, IsXMove: true, IsJump: true));
            return true;
        }

        private void Say(string text)
            => _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Boundary, text));
    }
}
