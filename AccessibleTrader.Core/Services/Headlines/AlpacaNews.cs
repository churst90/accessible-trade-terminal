using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Core.Services.Trading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.Headlines
{
    /// <summary>
    /// One news-wire story as Alpaca's news API sends it (Benzinga's feed). <see cref="Id"/> is
    /// Alpaca's id and is what "already said" is keyed on: an edited story is sent again with the
    /// same id and a newer <see cref="UpdatedUtc"/>, and is not said twice.
    /// <see cref="CreatedUtc"/> is when it was published.
    /// </summary>
    public sealed record NewsStory(
        long Id,
        string Headline,
        string Author,
        string Source,
        DateTime CreatedUtc,
        DateTime UpdatedUtc,
        IReadOnlyList<string> Symbols,
        string Url);

    /// <summary>A control message on the news stream: <c>success</c>, <c>subscription</c> or
    /// <c>error</c>, with Alpaca's message and (for an error) its code.</summary>
    public sealed record AlpacaStreamControl(string Type, string Message, int Code);

    /// <summary>
    /// Alpaca's two news shapes. The stream sends a JSON array of messages, each with a
    /// <c>"T"</c>: <c>"n"</c> for a story, <c>"success"</c>, <c>"subscription"</c> or
    /// <c>"error"</c>. The REST endpoint (<c>/v1beta1/news</c>) sends
    /// <c>{"news":[...],"next_page_token":...}</c> with the same story fields and no <c>"T"</c>.
    ///
    /// <para>
    /// <b>Written from Alpaca's documentation, not from a captured response</b> (2026-11-20: both
    /// key pairs in Cody's patches folder were refused, so nothing could be captured). The fixtures
    /// are the documented shapes; capture real ones when a working key is at hand.
    /// </para>
    /// </summary>
    public static class AlpacaNewsParser
    {
        /// <summary>The stories and control messages in one stream frame. A frame that is not
        /// JSON yields nothing rather than throwing.</summary>
        public static (IReadOnlyList<NewsStory> Stories, IReadOnlyList<AlpacaStreamControl> Controls)
            ParseFrame(string json)
        {
            var stories = new List<NewsStory>();
            var controls = new List<AlpacaStreamControl>();
            JToken? root = Load(json);
            IEnumerable<JToken> messages = root switch
            {
                JArray a => a,
                JObject o => new[] { o },
                _ => Array.Empty<JToken>(),
            };
            foreach (var m in messages.OfType<JObject>())
            {
                string type = (string?)m["T"] ?? "";
                if (type == "n")
                {
                    if (Story(m) is { } s) stories.Add(s);
                }
                else if (type is "success" or "subscription" or "error")
                {
                    int code = m["code"]?.Type == JTokenType.Integer ? (int)m["code"]! : 0;
                    controls.Add(new AlpacaStreamControl(type, (string?)m["msg"] ?? "", code));
                }
            }
            return (stories, controls);
        }

        /// <summary>The stories in a REST response, as sent (Alpaca sends newest first).</summary>
        public static IReadOnlyList<NewsStory> ParseRest(string json)
        {
            var list = new List<NewsStory>();
            if (Load(json) is JObject o && o["news"] is JArray news)
                foreach (var m in news.OfType<JObject>())
                    if (Story(m) is { } s) list.Add(s);
            return list;
        }

        private static JToken? Load(string json)
        {
            try
            {
                // Dates are read as text and parsed below: Newtonsoft's own date handling turns
                // "...Z" into local time on some paths.
                using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
                return JToken.ReadFrom(reader);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static NewsStory? Story(JObject m)
        {
            if (m["id"]?.Type != JTokenType.Integer) return null;
            string headline = Clean((string?)m["headline"]);
            if (headline.Length == 0) return null;
            if (!TryUtc((string?)m["created_at"], out var created)) return null;
            if (!TryUtc((string?)m["updated_at"], out var updated)) updated = created;
            var symbols = (m["symbols"] as JArray)?.Select(t => ((string?)t ?? "").Trim().ToUpperInvariant())
                .Where(t => t.Length > 0).Distinct().ToList() ?? new List<string>();
            return new NewsStory((long)m["id"]!, headline, Clean((string?)m["author"]),
                Clean((string?)m["source"]), created, updated, symbols, (string?)m["url"] ?? "");
        }

        /// <summary>Benzinga headlines carry HTML entities ("&amp;amp;", "&amp;#39;") and the odd
        /// run of spaces; a screen reader would say them.</summary>
        internal static string Clean(string? text)
            => Regex.Replace(WebUtility.HtmlDecode(text ?? ""), @"\s+", " ").Trim();

        private static bool TryUtc(string? text, out DateTime utc)
            => DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);
    }

    /// <summary>
    /// What a symbol on a chart or in a position is called in Alpaca's news: a stock by its
    /// ticker, a coin by its base asset (Alpaca tags crypto stories "BTCUSD").
    /// <see cref="Label"/> is what is said: "AAPL", "BTC".
    /// </summary>
    public sealed record NewsTarget(string Label, bool Coin)
    {
        /// <summary>The symbol the REST endpoint is asked about: "AAPL", "BTCUSD".</summary>
        public string RestSymbol => Coin ? Label + "USD" : Label;
    }

    /// <summary>
    /// How much a wire story matters. Alpaca sends no importance, so this is a rule on the
    /// headline's words — <b>Claude's judgement, 2026-11-20, for Cody to correct</b>:
    ///
    /// <list type="number">
    /// <item>A story tagged with more than <see cref="RoundupSymbols"/> symbols is a roundup
    /// ("12 stocks moving in Tuesday's session"): Low.</item>
    /// <item>Benzinga's generated filler — options-activity notes, "why shares are trading
    /// higher", movers lists, earnings previews, "if you invested $1000": Low.</item>
    /// <item>Results, guidance, takeovers, bankruptcy, delisting, regulator decisions, halts,
    /// offerings, a CEO leaving, a hack: High.</item>
    /// <item>Upgrades and downgrades, dividends, buybacks, deals and contracts, lawsuits, job
    /// cuts, officer appointments: Medium.</item>
    /// <item>Everything else, including a price-target change without a rating change: Low.</item>
    /// </list>
    /// </summary>
    public static class NewsStoryWords
    {
        public const int RoundupSymbols = 4;

        private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private static readonly Regex Filler = new(string.Join("|", new[]
        {
            @"\boptions? (activity|trades?|alert)\b",
            @"\bwhales?\b",
            @"\bsmart money\b",
            @"\bwhat'?s going on with\b",
            @"^why\b.*\b(shares?|stocks?)\b",
            @"\b(shares|stock) (is|are) (trading|moving|falling|rising|sliding|jumping|soaring|plunging|down|up|higher|lower)\b",
            @"\b(stocks|shares) (moving|to watch)\b",
            @"\bmovers?\b",
            @"\bmid-?day\b",
            @"\bpre-?market\b",
            @"\bafter-?hours\b",
            @"\bif you (had )?invested\b",
            @"\bhere'?s how much\b",
            @"\bearnings (outlook|preview|scheduled)\b",
            @"\bpreview\b",
            @"\bahead of (its |the )?(earnings|q[1-4]|results)\b",
            @"\bwhat to expect\b",
            @"\banalyst ratings?\b",
        }), Opt);

        private static readonly Regex High = new(string.Join("|", new[]
        {
            @"\breports? (q[1-4]|fy|first|second|third|fourth|full[- ]year|quarterly|annual)\b",
            @"\b(q[1-4]|fy)( \d{4})? (adj(usted)?\.? )?(eps|earnings|results|revenue|sales)\b",
            @"\b(beats?|miss(es)?)\b.*\bestimates?\b",
            @"\b(raises|lowers|cuts|boosts|reaffirms|withdraws|suspends|issues)\b.*\b(guidance|outlook|forecast)\b",
            @"\bsees (q[1-4]|fy)\b",
            @"\b(to acquire|acquires|acquired by|to be acquired|acquisition of|merger|to merge|merges|buyout|takeover|tender offer|go(ing)? private|definitive agreement)\b",
            @"\b(bankruptcy|chapter (11|7)|going concern|delist(s|ed|ing)?)\b",
            @"\bfda\b.*\b(approv\w*|reject\w*|clear\w*|complete response|crl)\b",
            @"\bcomplete response letter\b",
            @"\b(sec|doj|ftc) (charges|sues|probe|investigation)\b",
            @"\bsubpoena\b",
            @"\b(trading )?halt(ed|s)?\b",
            @"\bresumes trading\b",
            @"\b(hack(ed|s)?|exploit(ed)?|stolen|drained)\b",
            @"\betfs?\b.*\b(approv\w*|reject\w*|den(y|ies|ied))\b",
            @"\b(approv\w*|reject\w*|den(y|ies|ied))\b.*\betfs?\b",
            @"\b(public|secondary|stock|share|equity) offering\b",
            @"\bprices? .*\boffering\b",
            @"\bceo\b.*\b(resigns?|steps? down|to step down|departs?|ousted|fired)\b",
            @"\brecalls?\b",
            @"\b(cuts|suspends|eliminates|slashes) (its )?(quarterly )?dividend\b",
            @"\bstock split\b",
        }), Opt);

        private static readonly Regex Medium = new(string.Join("|", new[]
        {
            @"\b(upgrades?|upgraded|downgrades?|downgraded|initiates? coverage)\b",
            @"\bdividend\b",
            @"\b(buyback|repurchase)\b",
            @"\b(partnership|partners with|collaboration|contract|awarded|deal with|agreement with)\b",
            @"\b(lawsuit|sues|sued|settles?|settlement|investigation|probe)\b",
            @"\b(layoffs?|job cuts|restructuring)\b",
            @"\b(appoints|names)\b.*\b(ceo|cfo|president|chair\w*)\b",
        }), Opt);

        public static MarketEventImportance Importance(NewsStory story)
        {
            if (story.Symbols.Count > RoundupSymbols) return MarketEventImportance.Low;
            string h = story.Headline;
            if (Filler.IsMatch(h)) return MarketEventImportance.Low;
            if (High.IsMatch(h)) return MarketEventImportance.High;
            if (Medium.IsMatch(h)) return MarketEventImportance.Medium;
            return MarketEventImportance.Low;
        }

        /// <summary>The importance as the dossier writes it.</summary>
        public static string Word(MarketEventImportance importance) => importance switch
        {
            MarketEventImportance.High => "Important",
            MarketEventImportance.Medium => "Medium",
            _ => "Low",
        };

        /// <summary>Which of <paramref name="targets"/> the story is about, in the targets'
        /// order (the focused chart first).</summary>
        public static IReadOnlyList<NewsTarget> Matches(NewsStory story, IReadOnlyList<NewsTarget> targets)
        {
            var hits = new List<NewsTarget>();
            foreach (var t in targets)
            {
                bool hit = t.Coin
                    ? story.Symbols.Any(s => CoinOf(s) == t.Label)
                    : story.Symbols.Any(s => StockKey(s) == StockKey(t.Label));
                if (hit) hits.Add(t);
            }
            return hits;
        }

        /// <summary>"BRK.B", "BRK-B" and "BRK/B" are one ticker.</summary>
        internal static string StockKey(string symbol)
            => symbol.Trim().ToUpperInvariant().Replace(".", "").Replace("-", "").Replace("/", "");

        /// <summary>
        /// The coin a news symbol names ("BTCUSD" is BTC), or "" when it is not a coin pair.
        /// Alpaca tags crypto stories with a USD, USDT or USDC pair, so only those are cut off —
        /// not <see cref="SymbolAssets.Split"/>, which tries the longest quote first and reads
        /// "XBTUSD" as XB priced in TUSD. Kraken's XBT is BTC.
        /// </summary>
        internal static string CoinOf(string symbol)
        {
            string s = symbol.Trim().ToUpperInvariant().Replace("/", "").Replace("-", "");
            foreach (var quote in NewsQuotes)
                if (s.Length > quote.Length && s.EndsWith(quote, StringComparison.Ordinal))
                    return CoinLabel(s[..^quote.Length]);
            return "";
        }

        private static readonly string[] NewsQuotes = { "USDT", "USDC", "USD" };

        internal static string CoinLabel(string coin)
        {
            string c = coin.Trim().ToUpperInvariant();
            return c == "XBT" ? "BTC" : c;
        }
    }

    /// <summary>
    /// Which wire stories to say, and when. Pure: the stream hands it each story it matched, and
    /// the clock asks what is due.
    ///
    /// <list type="bullet">
    /// <item><b>Said once.</b> A story id already offered is ignored, so Benzinga's edits (same id,
    /// newer time) are not said again.</item>
    /// <item><b>Gathered.</b> A symbol's first new story waits <see cref="Gather"/> for the others
    /// that wire desks send in a burst ("Q4 EPS beats", "Q4 sales beat", "sees Q1"), so they are
    /// one sentence.</item>
    /// <item><b>Capped.</b> After a symbol's sentence nothing more is said for it for
    /// <see cref="Cooldown"/>; stories arriving meanwhile are gathered into the next sentence.</item>
    /// <item><b>Not late.</b> A story published more than <see cref="LateReach"/> ago is not said,
    /// as with the filings.</item>
    /// </list>
    /// </summary>
    public sealed class NewsHeadlineGate
    {
        public static readonly TimeSpan Gather = TimeSpan.FromSeconds(3);
        public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan LateReach = EdgarHeadlineTracker.LateReach;
        public const int SeenCap = 5000;

        private sealed record Item(NewsStory Story, IReadOnlyList<NewsTarget> Targets, MarketEventImportance Importance);

        private readonly HashSet<long> _seen = new();
        private readonly Queue<long> _seenOrder = new();
        private readonly Dictionary<string, (DateTime FirstUtc, List<Item> Items)> _pending = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _lastSaidUtc = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Queues a matched story that passes the level. False when it was already
        /// seen or is too old.</summary>
        public bool Offer(NewsStory story, IReadOnlyList<NewsTarget> targets, MarketEventImportance importance, DateTime nowUtc)
        {
            if (targets.Count == 0) return false;
            if (!_seen.Add(story.Id)) return false;
            _seenOrder.Enqueue(story.Id);
            while (_seenOrder.Count > SeenCap) _seen.Remove(_seenOrder.Dequeue());
            if (story.CreatedUtc < nowUtc - LateReach) return false;

            string key = targets[0].Label;
            if (!_pending.TryGetValue(key, out var p))
                _pending[key] = p = (nowUtc, new List<Item>());
            p.Items.Add(new Item(story, targets, importance));
            return true;
        }

        /// <summary>The sentences due now, one per symbol. A symbol no longer in
        /// <paramref name="touched"/> is dropped unsaid.</summary>
        public IReadOnlyList<Headline> Due(DateTime nowUtc, IReadOnlyCollection<string> touched)
        {
            var due = new List<Headline>();
            foreach (var key in _pending.Keys.ToList())
            {
                var (first, items) = _pending[key];
                if (!touched.Contains(key, StringComparer.OrdinalIgnoreCase)) { _pending.Remove(key); continue; }
                if (nowUtc - first < Gather) continue;
                if (_lastSaidUtc.TryGetValue(key, out var last) && nowUtc - last < Cooldown) continue;

                _pending.Remove(key);
                var live = items.Where(i => i.Story.CreatedUtc >= nowUtc - LateReach).ToList();
                if (live.Count == 0) continue;
                due.Add(NewsHeadlineWording.Compose(
                    live.Select(i => (i.Story, i.Targets, i.Importance)).ToList(), nowUtc));
                _lastSaidUtc[key] = nowUtc;
            }
            return due;
        }

        /// <summary>Forgets everything: the level was turned off, or the wire switched off.</summary>
        public void Forget()
        {
            _pending.Clear();
            _lastSaidUtc.Clear();
            _seen.Clear();
            _seenOrder.Clear();
        }

        public int PendingCount => _pending.Values.Sum(p => p.Items.Count);
    }

    /// <summary>
    /// The sentence for one symbol's stories: "AAPL: Apple Reports Q4 Results, Beats Estimates."
    /// The most important (newest among equals) is said in full and the rest are counted, as with
    /// the filings. A story about two watched symbols names both: "AAPL, MSFT: ...".
    /// </summary>
    public static class NewsHeadlineWording
    {
        public static Headline Compose(
            IReadOnlyList<(NewsStory Story, IReadOnlyList<NewsTarget> Targets, MarketEventImportance Importance)> items,
            DateTime nowUtc)
        {
            var lead = items
                .OrderBy(x => x.Importance)
                .ThenByDescending(x => x.Story.CreatedUtc)
                .First();

            string text = Sentence(lead.Story, lead.Targets);
            int more = items.Count - 1;
            if (more > 0)
                text += more == 1 ? " 1 more headline." : $" {more.ToString(CultureInfo.InvariantCulture)} more headlines.";

            var age = nowUtc - lead.Story.CreatedUtc;
            if (age >= EdgarHeadlineWording.SayAgeAfter)
                text += " " + Age(age);

            return new Headline(text, lead.Importance, "alpaca|" + lead.Story.Id.ToString(CultureInfo.InvariantCulture),
                lead.Targets[0].Label);
        }

        public static string Sentence(NewsStory story, IReadOnlyList<NewsTarget> targets)
        {
            string who = string.Join(", ", targets.Take(2).Select(t => t.Label));
            string h = story.Headline.TrimEnd();
            if (!h.EndsWith('.') && !h.EndsWith('?') && !h.EndsWith('!')) h += ".";
            return $"{who}: {h}";
        }

        private static string Age(TimeSpan age)
        {
            int minutes = (int)Math.Floor(age.TotalMinutes);
            if (minutes < 60) return $"Published {minutes.ToString(CultureInfo.InvariantCulture)} minutes ago.";
            int hours = minutes / 60;
            return hours == 1 ? "Published 1 hour ago."
                : $"Published {hours.ToString(CultureInfo.InvariantCulture)} hours ago.";
        }
    }
}
