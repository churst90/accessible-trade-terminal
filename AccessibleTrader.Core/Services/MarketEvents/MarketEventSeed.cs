using System.Globalization;
using System.Reflection;
using AccessibleTrader.Sdk.Enums;
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
    /// The seed ends where its inputs end — July 2026 for the releases, 2027 for the Fed. Stage 2
    /// fetches the rest. Nothing here is fetched at run time and nothing here is a figure: only
    /// dates, times and sources.
    /// </para>
    /// </summary>
    public static class MarketEventSeed
    {
        internal const string ResourceName = "AccessibleTrader.Core.Resources.MarketEvents.us_events_seed.json";

        private static readonly Lazy<MarketEventSeedDocument> Cached = new(LoadUncached);

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
            var events = (root["events"] as JArray ?? new JArray())
                .OfType<JObject>()
                .Select(e => new MarketEvent(
                    Id: (string?)e["id"] ?? throw new FormatException("A seed event has no id."),
                    Kind: ParseKind((string?)e["kind"]),
                    SpokenName: (string?)e["spokenName"] ?? "",
                    LongName: (string?)e["longName"] ?? "",
                    Scope: (string?)e["scope"] == "all"
                        ? allMarkets
                        : (e["scope"] as JArray ?? new JArray())
                            .Select(t => Enum.Parse<MarketType>((string)t!))
                            .ToArray(),
                    Importance: ParseImportance((string?)e["importance"]),
                    MomentUtc: ParseMoment((string?)e["moment"]),
                    Precision: (string?)e["precision"] == "Exact"
                        ? MarketEventPrecision.Exact
                        : MarketEventPrecision.DayOnly,
                    Date: DateOnly.ParseExact((string?)e["date"] ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Source: (string?)e["source"] ?? ""))
                .ToList();

            return new MarketEventSeedDocument(sources, events);
        }

        private static MarketEventKind ParseKind(string? value) => value switch
        {
            "Economy" => MarketEventKind.Economy,
            "Fed" => MarketEventKind.Fed,
            "Expiry" => MarketEventKind.Expiry,
            "Holiday" => MarketEventKind.Holiday,
            "Halving" => MarketEventKind.Halving,
            _ => throw new FormatException($"Unknown market-event kind '{value}'."),
        };

        private static MarketEventImportance ParseImportance(string? value) => value switch
        {
            "High" => MarketEventImportance.High,
            "Medium" => MarketEventImportance.Medium,
            "Low" => MarketEventImportance.Low,
            _ => throw new FormatException($"Unknown market-event importance '{value}'."),
        };

        private static DateTime ParseMoment(string? value)
            => DateTime.Parse(
                value ?? throw new FormatException("A seed event has no moment."),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }
}
