using System.Globalization;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.MarketEvents
{
    /// <summary>One ALFRED input behind a seed source: the series, where it came from, and its newest vintage.</summary>
    public sealed record MarketEventSeedAlfredInput(string Series, string Url, string Newest);

    /// <summary>One named input behind the seed file, with where it came from and when.</summary>
    public sealed record MarketEventSeedSource(
        string Id,
        string Name,
        string Url,
        string Fetched,
        IReadOnlyList<MarketEventSeedAlfredInput>? Alfred = null);

    /// <summary>The seed document: its provenance and its events.</summary>
    public sealed record MarketEventSeedDocument(
        IReadOnlyList<MarketEventSeedSource> Sources,
        IReadOnlyList<MarketEvent> Events);

    /// <summary>
    /// The compact seed shipped inside the assembly: US release dates (CPI, jobs, PPI, GDP) and
    /// every Fed decision from 2000 to 2027. Rebuilt by <c>scripts/build_market_events_seed.py</c>.
    ///
    /// <para>
    /// The file holds only dates, figures and provenance. Everything repeated for every date —
    /// the kind, both names, importance, market scope, source and the release-time rule — is
    /// code here, because that is where the rule that produces it belongs. The times are 8:30
    /// New York for releases from 1997, 14:00 New York for scheduled Fed decisions from 2013,
    /// and day-only for everything earlier and for unscheduled Fed decisions.
    /// </para>
    ///
    /// <para>
    /// Figure rows are aligned by index with each source's date array, and their columns are:
    /// <c>cpi</c> [CPI month change %, previous, core CPI month change %, previous];
    /// <c>nfp</c> [payrolls change thousands, previous, unemployment %, previous];
    /// <c>gdp</c> [growth % annualized, estimate number, previous, period];
    /// <c>ppi</c> none. The values are the first print visible on the release day, never a later
    /// revision. Percentages are 1 decimal and payrolls whole thousands; a null column is a
    /// figure that source did not publish that day.
    /// </para>
    ///
    /// <para>
    /// The seed ends where its inputs end — July 2026 for the releases, 2027 for the Fed. Stage 2
    /// fetches the rest. Nothing here is fetched at run time.
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
                    (string?)s["fetched"] ?? "",
                    (s["alfred"] as JArray ?? new JArray())
                        .OfType<JObject>()
                        .Select(a => new MarketEventSeedAlfredInput(
                            (string?)a["series"] ?? "",
                            (string?)a["url"] ?? "",
                            (string?)a["newest"] ?? ""))
                        .ToList()))
                .ToList();

            var allMarkets = Enum.GetValues<MarketType>().ToArray();
            var events = new List<MarketEvent>();

            foreach (var (id, spec) in Releases)
            {
                var dates = ParseDates(root[id], id).ToList();
                var figures = ParseFigures(root[id + "_figures"], id, dates.Count);
                for (int i = 0; i < dates.Count; i++)
                    events.Add(ReleaseEvent(id, dates[i], spec, allMarkets, figures[i]));
            }

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
            IReadOnlyList<MarketType> scope,
            MarketEventFigures? figures,
            string? fetched = null)
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
                Source: $"FRED release {spec.FredReleaseId}, fetched {fetched ?? "2026-08-01"}",
                Figures: figures);
        }

        /// <summary>
        /// One release event built by the same code as the seed, for the live feed. Unknown
        /// source ids throw: a feed answer must never invent a kind or an importance.
        /// </summary>
        public static MarketEvent CreateRelease(
            string sourceId,
            DateOnly day,
            MarketEventFigures? figures,
            string fetched)
        {
            if (!Releases.TryGetValue(sourceId, out var spec))
                throw new ArgumentOutOfRangeException(nameof(sourceId), sourceId,
                    "Not a seed release source.");
            return ReleaseEvent(sourceId, day, spec, Enum.GetValues<MarketType>().ToArray(), figures, fetched);
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

        /// <summary>
        /// Parses one source's figure rows. Each row is aligned by index with that source's
        /// dates; a null row means no figures for that release. See the class comment for the
        /// column order of each source.
        /// </summary>
        private static IReadOnlyList<MarketEventFigures?> ParseFigures(
            JToken? token,
            string sourceId,
            int expectedCount)
        {
            var result = new List<MarketEventFigures?>(expectedCount);
            if (token is not JArray rows)
                throw new FormatException($"The market-events seed has no '{sourceId}_figures' rows.");
            if (rows.Count != expectedCount)
                throw new FormatException(
                    $"'{sourceId}_figures' has {rows.Count} rows for {expectedCount} dates.");

            foreach (var row in rows)
            {
                if (row is not JArray values)
                {
                    result.Add(null);
                    continue;
                }

                static double? Number(JToken? value)
                    => value == null || value.Type == JTokenType.Null ? null : value.Value<double>();

                static int? Whole(JToken? value)
                    => value == null || value.Type == JTokenType.Null ? null : value.Value<int>();

                switch (sourceId)
                {
                    case "cpi":
                    {
                        double? cpi = Number(values.ElementAtOrDefault(0));
                        double? core = Number(values.ElementAtOrDefault(2));
                        if (cpi == null && core == null)
                        {
                            result.Add(null);
                            break;
                        }
                        result.Add(new MarketEventFigures(new[]
                        {
                            new MarketEventFigure("CPI", cpi,
                                Number(values.ElementAtOrDefault(1)),
                                MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsHotter),
                            new MarketEventFigure("core CPI", core,
                                Number(values.ElementAtOrDefault(3)),
                                MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsHotter),
                        }));
                        break;
                    }
                    case "nfp":
                    {
                        double? payrolls = Number(values.ElementAtOrDefault(0));
                        double? unemployment = Number(values.ElementAtOrDefault(2));
                        if (payrolls == null && unemployment == null)
                        {
                            result.Add(null);
                            break;
                        }
                        result.Add(new MarketEventFigures(new[]
                        {
                            new MarketEventFigure("payrolls", payrolls,
                                Number(values.ElementAtOrDefault(1)),
                                MarketEventFigureUnit.Thousands, MarketEventFigureDirection.HigherIsStronger),
                            new MarketEventFigure("unemployment", unemployment,
                                Number(values.ElementAtOrDefault(3)),
                                MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsWeaker),
                        }));
                        break;
                    }
                    case "gdp":
                    {
                        double? growth = Number(values.ElementAtOrDefault(0));
                        if (growth == null)
                        {
                            result.Add(null);
                            break;
                        }
                        DateOnly? period = values.ElementAtOrDefault(3) is { Type: JTokenType.String } day
                            ? DateOnly.ParseExact((string)day!, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : null;
                        result.Add(new MarketEventFigures(
                            new[]
                            {
                                new MarketEventFigure("GDP", growth,
                                    Number(values.ElementAtOrDefault(2)),
                                    MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsStronger),
                            },
                            Whole(values.ElementAtOrDefault(1)),
                            period));
                        break;
                    }
                    default:
                        result.Add(null);
                        break;
                }
            }
            return result;
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
