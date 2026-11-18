using System.Globalization;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.MarketEvents
{
    /// <summary>One named input behind the seed file, with where it came from and when.</summary>
    public sealed record MarketEventSeedSource(string Id, string Name, string Url, string Fetched);

    /// <summary>The seed document: its provenance and its events.</summary>
    public sealed record MarketEventSeedDocument(
        IReadOnlyList<MarketEventSeedSource> Sources,
        IReadOnlyList<MarketEvent> Events);

    /// <summary>
    /// The compact seed shipped inside the assembly: US release dates (CPI, jobs, PPI, GDP) and
    /// every Fed decision from 2000 to 2027. Rebuilt by <c>scripts/build_market_events_seed.py</c>.
    ///
    /// <para>
    /// The file holds only dates and provenance. Everything repeated for every date — the kind,
    /// both names, importance, market scope, source and the release-time rule — is code here,
    /// because that is where the rule that produces it belongs. The times are 8:30 New York for
    /// releases from 1997, 14:00 New York for scheduled Fed decisions from 2013, and day-only
    /// for everything earlier and for unscheduled Fed decisions.
    /// </para>
    ///
    /// <para>
    /// The seed ends where its inputs end — July 2026 for the releases, 2027 for the Fed. Stage 2
    /// fetches the rest. Nothing here is fetched at run time and nothing here is a figure: only
    /// dates, times and sources.
    /// </para>
    /// </summary>
    public static class MarketEventSeed
    {
        internal const string ResourceName = "AccessibleTrader.Core.Resources.MarketEvents.us_events_seed.json";

        private static readonly DateOnly ReleaseTimeRule = new(1997, 1, 1);
        private static readonly DateOnly FomcTimeRule = new(2013, 1, 1);
        private static readonly TimeOnly ReleaseTime = new(8, 30);
        private static readonly TimeOnly FomcTime = new(14, 0);

        private static readonly Lazy<MarketEventSeedDocument> Cached = new(LoadUncached);

        private sealed record ReleaseSpec(
            MarketEventKind Kind,
            string SpokenName,
            string LongName,
            MarketEventImportance Importance,
            int FredReleaseId,
            DateOnly ExactFrom,
            TimeOnly ExactTime);

        private static readonly IReadOnlyDictionary<string, ReleaseSpec> Releases =
            new Dictionary<string, ReleaseSpec>(StringComparer.Ordinal)
            {
                ["cpi"] = new(MarketEventKind.Economy, "CPI", "Consumer Price Index",
                    MarketEventImportance.High, 10, ReleaseTimeRule, ReleaseTime),
                ["nfp"] = new(MarketEventKind.Economy, "jobs report", "Nonfarm payrolls",
                    MarketEventImportance.High, 50, ReleaseTimeRule, ReleaseTime),
                ["ppi"] = new(MarketEventKind.Economy, "PPI", "Producer Price Index",
                    MarketEventImportance.Medium, 46, ReleaseTimeRule, ReleaseTime),
                ["gdp"] = new(MarketEventKind.Economy, "GDP", "Gross domestic product",
                    MarketEventImportance.Medium, 53, ReleaseTimeRule, ReleaseTime),
            };

        /// <summary>The shipped seed. Never null; throws only if the embedded file is missing.</summary>
        public static MarketEventSeedDocument Load() => Cached.Value;

        private static MarketEventSeedDocument LoadUncached()
        {
            var assembly = typeof(MarketEventSeed).Assembly;
            using var stream = assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException(
                    $"The market-events seed '{ResourceName}' is not embedded in {assembly.GetName().Name}.");
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }

        /// <summary>Parses the seed text. Public so a test can hold a fixture without the resource.</summary>
        public static MarketEventSeedDocument Parse(string json)
        {
            var root = JObject.Parse(json);
            var header = root["header"] as JObject
                ?? throw new FormatException("The market-events seed has no header.");
            var sources = (header["sources"] as JArray ?? new JArray())
                .OfType<JObject>()
                .Select(s => new MarketEventSeedSource(
                    (string?)s["id"] ?? "",
                    (string?)s["name"] ?? "",
                    (string?)s["url"] ?? "",
                    (string?)s["fetched"] ?? ""))
                .ToList();

            var allMarkets = Enum.GetValues<MarketType>().ToArray();
            var events = new List<MarketEvent>();

            foreach (var (id, spec) in Releases)
                foreach (var day in ParseDates(root[id], id))
                    events.Add(ReleaseEvent(id, day, spec, allMarkets));

            var fomc = root["fomc"] as JObject;
            foreach (var day in ParseDates(fomc?["scheduled"], "fomc.scheduled"))
                events.Add(FomcEvent(day, allMarkets, scheduled: true));
            foreach (var day in ParseDates(fomc?["unscheduled"], "fomc.unscheduled"))
                events.Add(FomcEvent(day, allMarkets, scheduled: false));

            // The old file was ordered by moment, then id; keep that so a reader sees the
            // calendar in the same order it has always had.
            events.Sort((a, b) =>
            {
                int byMoment = a.MomentUtc.CompareTo(b.MomentUtc);
                return byMoment != 0 ? byMoment : string.CompareOrdinal(a.Id, b.Id);
            });

            return new MarketEventSeedDocument(sources, events);
        }

        private static MarketEvent ReleaseEvent(
            string id,
            DateOnly day,
            ReleaseSpec spec,
            IReadOnlyList<MarketType> scope)
        {
            bool exact = day >= spec.ExactFrom;
            return new MarketEvent(
                Id: $"{id}-{day:yyyy-MM-dd}",
                Kind: spec.Kind,
                SpokenName: spec.SpokenName,
                LongName: spec.LongName,
                Scope: scope,
                Importance: spec.Importance,
                MomentUtc: Moment(day, exact ? spec.ExactTime : null),
                Precision: exact ? MarketEventPrecision.Exact : MarketEventPrecision.DayOnly,
                Date: day,
                Source: $"FRED release {spec.FredReleaseId}, fetched 2026-08-01");
        }

        private static MarketEvent FomcEvent(DateOnly day, IReadOnlyList<MarketType> scope, bool scheduled)
        {
            bool exact = scheduled && day >= FomcTimeRule;
            return new MarketEvent(
                Id: $"fomc-{day:yyyy-MM-dd}",
                Kind: MarketEventKind.Fed,
                SpokenName: scheduled ? "Fed decision" : "Fed decision, unscheduled",
                LongName: scheduled
                    ? "Federal Open Market Committee decision"
                    : "Unscheduled Federal Open Market Committee decision",
                Scope: scope,
                Importance: MarketEventImportance.High,
                MomentUtc: Moment(day, exact ? FomcTime : null),
                Precision: exact ? MarketEventPrecision.Exact : MarketEventPrecision.DayOnly,
                Date: day,
                Source: "Federal Reserve FOMC calendar, fetched 2026-07-31");
        }

        private static IEnumerable<DateOnly> ParseDates(JToken? token, string name)
        {
            if (token is not JArray dates)
                throw new FormatException($"The market-events seed has no '{name}' dates.");
            foreach (var date in dates)
                yield return DateOnly.ParseExact(
                    (string?)date ?? throw new FormatException($"A '{name}' date is not a string."),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture);
        }

        /// <summary>A New York wall-clock moment; a day-only event is midnight in that zone.</summary>
        private static DateTime Moment(DateOnly day, TimeOnly? at)
        {
            var local = DateTime.SpecifyKind(
                day.ToDateTime(at ?? TimeOnly.MinValue),
                DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(local, ExchangeTime.UsEastern);
        }
    }
}
