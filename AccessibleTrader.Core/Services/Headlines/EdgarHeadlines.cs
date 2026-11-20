using System.Globalization;
using AccessibleTrader.Core.Services.MarketEvents;

namespace AccessibleTrader.Core.Services.Headlines
{
    /// <summary>One headline ready to say: the sentence, how much it matters (the sound and the
    /// level use it), a stable id for "already said", and the symbol it is about.</summary>
    public sealed record Headline(string Text, MarketEventImportance Importance, string Id, string Symbol);

    /// <summary>
    /// Which filings are NEW. Pure: the live service feeds it each poll's list and speaks what it
    /// returns.
    ///
    /// <para>
    /// <b>The first look is a baseline.</b> Opening a chart must not read out the company's last
    /// twenty filings, so the first list seen for a symbol is all marked as said — except a
    /// filing accepted in the last <see cref="FirstLookReach"/>, which is still news (a chart
    /// opened the minute results came out).
    /// </para>
    ///
    /// <para>
    /// <b>Late is dropped, not queued.</b> An unseen filing older than <see cref="LateReach"/>
    /// (the machine slept, the network was down) is marked as said without a word: it is in the
    /// dossier's list, and a stale headline spoken as if it were new misleads.
    /// </para>
    ///
    /// <para>
    /// <b>A symbol no longer touched is forgotten</b> (<see cref="Keep"/>), so closing a chart and
    /// opening it again later starts from a fresh baseline instead of reading out the gap.
    /// </para>
    /// </summary>
    public sealed class EdgarHeadlineTracker
    {
        public static readonly TimeSpan FirstLookReach = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan LateReach = TimeSpan.FromHours(2);

        private readonly Dictionary<string, HashSet<string>> _seen = new(StringComparer.OrdinalIgnoreCase);

        public bool Knows(string ticker) => _seen.ContainsKey(ticker);

        /// <summary>The filings in <paramref name="filings"/> not seen before for this symbol and
        /// recent enough to say, oldest first. Every filing in the list is then remembered.</summary>
        public IReadOnlyList<EdgarFiling> Observe(string ticker, IReadOnlyList<EdgarFiling> filings, DateTime nowUtc)
        {
            bool first = !_seen.TryGetValue(ticker, out var seen);
            if (first) _seen[ticker] = seen = new HashSet<string>(StringComparer.Ordinal);

            var reach = first ? FirstLookReach : LateReach;
            var fresh = new List<EdgarFiling>();
            foreach (var f in filings)
            {
                if (!seen!.Add(f.Accession)) continue;
                if (f.AcceptedUtc >= nowUtc - reach) fresh.Add(f);
            }
            return fresh.OrderBy(f => f.AcceptedUtc).ToList();
        }

        /// <summary>Forget every symbol not in <paramref name="touched"/>.</summary>
        public void Keep(IReadOnlyCollection<string> touched)
        {
            foreach (var ticker in _seen.Keys.ToList())
                if (!touched.Contains(ticker, StringComparer.OrdinalIgnoreCase))
                    _seen.Remove(ticker);
        }
    }

    /// <summary>
    /// The sentence for one symbol's new filings in one poll. One sentence, never a list: the
    /// most important filing is said in full and the rest are counted, because six Form 4s in a
    /// minute is one piece of news.
    /// </summary>
    public static class EdgarHeadlineWording
    {
        /// <summary>A filing said this long after its acceptance gets its age at the end.</summary>
        public static readonly TimeSpan SayAgeAfter = TimeSpan.FromMinutes(10);

        /// <summary>
        /// The headline for <paramref name="fresh"/> (one symbol's new filings), keeping only those
        /// the level lets through. Null when none pass.
        /// </summary>
        public static Headline? Compose(
            string ticker, IReadOnlyList<EdgarFiling> fresh, MarketEventLevel level, DateTime nowUtc)
        {
            var passing = fresh
                .Select(f => (Filing: f, Importance: EdgarFilingWords.Importance(f.Form, f.Items)))
                .Where(x => x.Importance is { } imp && MarketEventLevels.Includes(level, imp))
                .Select(x => (x.Filing, Importance: x.Importance!.Value))
                .ToList();
            if (passing.Count == 0) return null;

            var lead = passing
                .OrderBy(x => x.Importance)
                .ThenByDescending(x => x.Filing.AcceptedUtc)
                .First();

            string text = Sentence(ticker, lead.Filing);
            int more = passing.Count - 1;
            if (more > 0)
                text += more == 1 ? " 1 more filing." : $" {more.ToString(CultureInfo.InvariantCulture)} more filings.";

            var age = nowUtc - lead.Filing.AcceptedUtc;
            if (age >= SayAgeAfter)
                text += " " + Age(age);

            return new Headline(text, lead.Importance, "edgar|" + lead.Filing.Accession, ticker);
        }

        /// <summary>"AAPL filed an 8-K: earnings results." — also the dossier list's wording,
        /// without the symbol.</summary>
        public static string Sentence(string ticker, EdgarFiling filing)
        {
            string about = EdgarFilingWords.About(filing.Form, filing.Items);
            string spoken = EdgarFilingWords.SpokenForm(filing.Form);
            return about.Length == 0
                ? $"{ticker} filed {spoken}."
                : $"{ticker} filed {spoken}: {about}.";
        }

        private static string Age(TimeSpan age)
        {
            int minutes = (int)Math.Floor(age.TotalMinutes);
            if (minutes < 60) return $"Accepted {minutes.ToString(CultureInfo.InvariantCulture)} minutes ago.";
            int hours = minutes / 60;
            return hours == 1 ? "Accepted 1 hour ago."
                : $"Accepted {hours.ToString(CultureInfo.InvariantCulture)} hours ago.";
        }
    }
}
