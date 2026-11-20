using System.Collections.Immutable;
using System.Globalization;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Headlines
{
    /// <summary>
    /// One headline about one symbol, said or not: a wire story whose headline names the symbol,
    /// or one of its SEC filings. <see cref="Text"/> is the headline without the symbol in front
    /// ("Apple Reports Q4 Results." / "Filed an 8-K: earnings results."), because on the chart and
    /// in the list the symbol is already known. <see cref="Key"/> is the story's or filing's own
    /// id ("finnhub|123", "edgar|0000320193-25-000071").
    /// </summary>
    public sealed record HeadlineRecord(
        string Key,
        string Symbol,
        DateTime PublishedUtc,
        string Text,
        string Source,
        MarketEventImportance Importance,
        bool Said = false);

    /// <summary>
    /// Every headline the terminal has seen for the symbols it touches (Go bar <c>NEWS</c>), and
    /// the chart's own stock or coin back a month (<see cref="HeadlineBackfill"/>) so headlines can
    /// be placed on the bar they arrived in. One per host scope, like <see cref="NewsHeadlineGate"/>.
    ///
    /// <list type="bullet">
    /// <item><b>Said or not.</b> A story below the Headlines level, on a poller's first look, or
    /// from the backfill is kept unsaid; the gate marks what it says (<see cref="MarkSaid"/>).</item>
    /// <item><b>One story once per symbol.</b> The same key is kept once, and so is the same news
    /// in other words from another wire or publisher within two hours
    /// (<see cref="NewsStoryWords.SameStory"/>) — the gate's own test, so the chart and the list
    /// agree with what was said.</item>
    /// <item><b>Bounded</b> at <see cref="Cap"/>, the oldest dropped first.</item>
    /// </list>
    /// Thread-safe: the wires add from their own loops while the keys read.
    /// </summary>
    public sealed class HeadlineHistory
    {
        public const int Cap = 3000;
        public static readonly TimeSpan SameStoryReach = TimeSpan.FromHours(2);

        private readonly object _lock = new();
        private readonly List<HeadlineRecord> _records = new();
        private readonly Dictionary<string, HashSet<string>> _words = new(StringComparer.Ordinal);
        private int _version;

        /// <summary>Bumps on every change, so a reader can tell a stale copy.</summary>
        public int Version => Volatile.Read(ref _version);

        public int Count { get { lock (_lock) return _records.Count; } }

        /// <summary>A news story, once for each symbol it names.</summary>
        public void Add(NewsStory story, IReadOnlyList<NewsTarget> targets, MarketEventImportance importance)
        {
            foreach (var t in targets)
                Add(new HeadlineRecord(story.Key, t.Label, story.CreatedUtc, Sentence(story.Headline),
                    story.Source, importance));
        }

        /// <summary>A filing that is a headline (its form has an importance).</summary>
        public void Add(string ticker, EdgarFiling filing)
        {
            if (EdgarFilingWords.Importance(filing.Form, filing.Items) is not { } importance) return;
            Add(new HeadlineRecord("edgar|" + filing.Accession, ticker, filing.AcceptedUtc,
                FilingText(filing), "SEC EDGAR", importance));
        }

        public void Add(HeadlineRecord record)
        {
            lock (_lock)
            {
                if (_records.Any(r => r.Key == record.Key
                        && string.Equals(r.Symbol, record.Symbol, StringComparison.OrdinalIgnoreCase)))
                    return;

                var words = NewsStoryWords.StoryWords(record.Text);
                foreach (var r in _records)
                {
                    if (!string.Equals(r.Symbol, record.Symbol, StringComparison.OrdinalIgnoreCase)) continue;
                    if ((r.PublishedUtc - record.PublishedUtc).Duration() > SameStoryReach) continue;
                    if (_words.TryGetValue(r.Key, out var w) && NewsStoryWords.SameStory(w, words)) return;
                }

                _records.Add(record);
                _words[record.Key] = words;
                if (_records.Count > Cap)
                {
                    var oldest = _records.OrderBy(r => r.PublishedUtc).Take(_records.Count - Cap).ToList();
                    foreach (var o in oldest)
                    {
                        _records.Remove(o);
                        if (!_records.Any(r => r.Key == o.Key)) _words.Remove(o.Key);
                    }
                }
                _version++;
            }
        }

        /// <summary>The gate said this story (in full or counted in a sentence).</summary>
        public void MarkSaid(string key)
        {
            lock (_lock)
            {
                bool changed = false;
                for (int i = 0; i < _records.Count; i++)
                {
                    if (_records[i].Key != key || _records[i].Said) continue;
                    _records[i] = _records[i] with { Said = true };
                    changed = true;
                }
                if (changed) _version++;
            }
        }

        /// <summary>One symbol's headlines, oldest first.</summary>
        public IReadOnlyList<HeadlineRecord> For(string symbol)
        {
            lock (_lock)
                return _records
                    .Where(r => string.Equals(r.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(r => r.PublishedUtc)
                    .ToList();
        }

        /// <summary>Every headline, newest first: the Go bar's NEWS list.</summary>
        public IReadOnlyList<HeadlineRecord> Newest()
        {
            lock (_lock) return _records.OrderByDescending(r => r.PublishedUtc).ToList();
        }

        internal static string Sentence(string headline)
        {
            string h = (headline ?? "").Trim();
            if (h.Length > 0 && !h.EndsWith('.') && !h.EndsWith('?') && !h.EndsWith('!')) h += ".";
            return h;
        }

        /// <summary>"Filed an 8-K: earnings results." — <see cref="EdgarHeadlineWording.Sentence"/>
        /// without the ticker in front.</summary>
        internal static string FilingText(EdgarFiling filing)
        {
            string about = EdgarFilingWords.About(filing.Form, filing.Items);
            string spoken = EdgarFilingWords.SpokenForm(filing.Form);
            return about.Length == 0 ? $"Filed {spoken}." : $"Filed {spoken}: {about}.";
        }
    }

    /// <summary>A headline on the bar it arrived in.</summary>
    public sealed record HeadlineBarPlacement(int BarIndex, HeadlineRecord Record);

    /// <summary>
    /// Which symbol a chart's headlines are for, and which bar each arrived in.
    /// </summary>
    public static class HeadlineChart
    {
        /// <summary>The focused chart's headline symbol: the ticker for a stock ("AAPL"), the coin for
        /// crypto ("BTC"); null for anything else (forex, futures, indexes have no headlines).</summary>
        public static NewsTarget? TargetOf(WorkspaceState state)
        {
            var market = EdgarHeadlineLiveNotices.MarketOf(state.Identity)
                ?? (state.Data.Count > 0 ? state.SelectedMarketType : (MarketType?)null);
            if (market == MarketType.Stock)
            {
                string t = Analysis.EdgarCompanyProfileSource.Normalise(state.Identity.Symbol ?? "");
                return t.Length > 0 ? new NewsTarget(t, false) : null;
            }
            if (market == MarketType.Crypto)
            {
                string coin = NewsStoryWords.CoinLabel(Trading.SymbolAssets.BaseOf(state.Identity.Symbol));
                return coin.Length > 0 ? new NewsTarget(coin, true) : null;
            }
            return null;
        }

        /// <summary>
        /// Each headline on the bar it arrived in: the first bar that had not yet ended when it was
        /// published. Published inside a bar, it is on that bar; between bars — overnight, over a
        /// weekend, before the open — it is on the next bar, the one where the market could first
        /// answer it. Published after the last bar ended, it is on the last bar (the chart has not
        /// caught up); before the first bar, it is not placed.
        /// </summary>
        public static IReadOnlyList<HeadlineBarPlacement> Place(
            IReadOnlyList<HeadlineRecord> records, IReadOnlyList<Ohlcv> data, int barSeconds)
        {
            if (data.Count == 0 || records.Count == 0) return Array.Empty<HeadlineBarPlacement>();
            var bar = TimeSpan.FromSeconds(Math.Max(1, barSeconds));
            var first = Utc(data[0].Date);
            var placed = new List<HeadlineBarPlacement>();
            foreach (var r in records)
            {
                if (r.PublishedUtc < first) continue;
                // Smallest i with data[i].Date + bar > published.
                int lo = 0, hi = data.Count - 1, found = data.Count - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (Utc(data[mid].Date) + bar > r.PublishedUtc) { found = mid; hi = mid - 1; }
                    else lo = mid + 1;
                }
                placed.Add(new HeadlineBarPlacement(found, r));
            }
            return placed;
        }

        private static DateTime Utc(DateTime d)
            => d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : DateTime.SpecifyKind(d, DateTimeKind.Utc);
    }

    /// <summary>
    /// The bar-to-headline map for the focused chart. Not cached: a chart's headlines are a few
    /// hundred at most and each is one binary search, so it is cheaper than keeping a cache right
    /// while the history grows under it. Asking also starts the chart's backfill.
    /// </summary>
    public interface IHeadlineBarMap
    {
        IReadOnlyList<HeadlineBarPlacement> For(WorkspaceState state);
    }

    public sealed class HeadlineBarMap : IHeadlineBarMap
    {
        private readonly HeadlineHistory _history;
        private readonly HeadlineBackfill? _backfill;

        public HeadlineBarMap(HeadlineHistory history, HeadlineBackfill? backfill = null)
        {
            _history = history;
            _backfill = backfill;
        }

        public IReadOnlyList<HeadlineBarPlacement> For(WorkspaceState state)
        {
            var data = state.Data;
            if (data == null || data.Count == 0) return Array.Empty<HeadlineBarPlacement>();
            if (HeadlineChart.TargetOf(state) is not { } target) return Array.Empty<HeadlineBarPlacement>();
            // A brick is not a span of the clock, so a headline has no bar on a built chart —
            // the same rule the market events and Alt+Shift+D's formations keep.
            if (state.ChartStyle.IsBrickStyle()) return Array.Empty<HeadlineBarPlacement>();
            _backfill?.RequestRefresh(state, target);
            return HeadlineChart.Place(_history.For(target.Label), data,
                Accessibility.PlaybackNarration.BarSeconds(state));
        }
    }

    /// <summary>
    /// The words for headlines on a bar: the arrow keys' clause, Alt+Shift+D's section and the
    /// NEWS list's lines.
    /// </summary>
    public static class HeadlineSpeech
    {
        /// <summary>The ones the level lets through, most important first, newest among equals.</summary>
        public static IReadOnlyList<HeadlineRecord> Passing(
            IEnumerable<HeadlineBarPlacement> onBar, MarketEventLevel level)
            => onBar.Select(p => p.Record)
                .Where(r => MarketEventLevels.Includes(level, r.Importance))
                .OrderBy(r => r.Importance)
                .ThenByDescending(r => r.PublishedUtc)
                .ToList();

        /// <summary>"Headline: Apple Reports Q4 Results. 2 more headlines." or "".</summary>
        public static string Clause(IReadOnlyList<HeadlineRecord> passing)
        {
            if (passing.Count == 0) return "";
            string text = "Headline: " + passing[0].Text;
            int more = passing.Count - 1;
            if (more > 0)
                text += more == 1 ? " 1 more headline." : $" {more.ToString(CultureInfo.InvariantCulture)} more headlines.";
            return text;
        }

        /// <summary>Alt+Shift+D: every headline on the bar, whatever the level, in time order.
        /// "" when there are none, so no empty heading is read.</summary>
        public static string Detail(IEnumerable<HeadlineBarPlacement> onBar)
        {
            var all = onBar.Select(p => p.Record).OrderBy(r => r.PublishedUtc).ToList();
            if (all.Count == 0) return "";
            var lines = all.Select(r => $"{Time(r.PublishedUtc)}, {r.Source}, {NewsStoryWords.Word(r.Importance)}: {r.Text}");
            string head = all.Count == 1 ? "Headline on this bar:" : $"{all.Count.ToString(CultureInfo.InvariantCulture)} headlines on this bar:";
            return head + " " + string.Join(" ", lines);
        }

        /// <summary>
        /// One NEWS line: "AAPL: Apple Reports Q4 Results. Important, Finnhub, Nov 20 14:02 UTC."
        /// The symbol and the words lead, so arrowing the list is quick; "not said" marks the ones
        /// the level kept quiet.
        /// </summary>
        public static string ListLine(HeadlineRecord r)
        {
            string text = r.Text.TrimEnd();
            if (!text.EndsWith('.') && !text.EndsWith('!') && !text.EndsWith('?')) text += ".";
            string said = r.Said ? "" : ", not said";
            return $"{r.Symbol}: {text} {NewsStoryWords.Word(r.Importance)}, {r.Source}, {Time(r.PublishedUtc)}{said}.";
        }

        /// <summary>"Nov 20 14:02 UTC".</summary>
        public static string Time(DateTime utc)
            => utc.ToString("MMM d HH:mm", CultureInfo.InvariantCulture) + " UTC";
    }
}
