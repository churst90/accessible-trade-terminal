using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.StrategyLab.Catalogue
{
    /// <summary>
    /// The registry of tested <b>edges</b> — measured relationships — as opposed to the catalogue of
    /// <b>strategies</b>, which are bundles of them.
    ///
    /// <para>
    /// The distinction is the point. A strategy bundles an entry, gates, a stop and a ladder: when it
    /// fails you cannot tell which part failed, and when it works you cannot reuse the part that
    /// worked. Every genuine finding this project has produced is an edge; every expensive failure
    /// was a strategy stacking four correlated ones and calling it confluence.
    /// </para>
    ///
    /// <para>Three things the registry buys that prose findings documents cannot:</para>
    /// <list type="number">
    ///   <item>
    ///     <b>Decay is tracked rather than discovered.</b> FOMC drift lost 70% of its effect after
    ///     2015 and that was noticed by accident. Every edge carries a <c>decay</c> series and a
    ///     <c>lastMeasured</c> date, so an edge that fades is visible before it is expensive.
    ///   </item>
    ///   <item>
    ///     <b>Orthogonality is expressible.</b> <c>correlatesWith</c> records which edges overlap, so
    ///     a scoring engine can refuse to count the same information twice.
    ///   </item>
    ///   <item>
    ///     <b>Nulls are first-class.</b> Ten of the twenty records are falsified. A recorded negative
    ///     stops the next hopeful re-run, which is worth more hours than any positive here.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// The store is <c>edges.json</c>, copied beside the binary at build time. It is data rather than
    /// code so other tools — and a future terminal-side reader — can consume it without referencing
    /// the lab.
    /// </para>
    /// </summary>
    public sealed class EdgeRegistry
    {
        public const string FileName = "edges.json";

        /// <summary>Highest schema this build understands. A newer file is refused, not guessed at.</summary>
        public const int MaxSupportedSchemaVersion = 1;

        private static readonly JsonSerializerOptions _options = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
        };

        public int SchemaVersion { get; init; }
        public string RegistryVersion { get; init; } = "";
        public string? Note { get; init; }
        public IReadOnlyList<Edge> Edges { get; init; } = Array.Empty<Edge>();

        /// <summary>Loads the registry shipped beside the executable.</summary>
        public static EdgeRegistry Load() => LoadFrom(DefaultPath());

        public static string DefaultPath() =>
            Path.Combine(AppContext.BaseDirectory, "Catalogue", FileName);

        public static EdgeRegistry LoadFrom(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"Edge registry not found at {path}. It ships beside the binary as Catalogue/{FileName}.", path);

            var reg = JsonSerializer.Deserialize<EdgeRegistry>(File.ReadAllText(path), _options)
                      ?? throw new InvalidDataException($"{path} did not deserialize into a registry.");

            if (reg.SchemaVersion > MaxSupportedSchemaVersion)
                throw new InvalidDataException(
                    $"{path} uses edge schema {reg.SchemaVersion}; this build reads up to {MaxSupportedSchemaVersion}.");

            return reg;
        }

        public Edge? this[string id] =>
            Edges.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Edges that may contribute to a score. Only <see cref="StrategyEvidenceLevel.ControlTested"/>
        /// qualifies — the same rule the terminal applies to strategies, for the same reason: an
        /// unbeaten null is not evidence, and a fragile or falsified result is evidence against.
        /// </summary>
        public IEnumerable<Edge> Scorable => Edges.Where(e => e.CanScore);

        /// <summary>Edges whose scope covers this asset class, regardless of evidence level.</summary>
        public IEnumerable<Edge> InScopeFor(string assetClass) =>
            Edges.Where(e => e.AppliesTo(assetClass));

        /// <summary>
        /// Pairs of edges recorded as overlapping. A scoring engine must not count both at full
        /// weight; a study that stacks both is measuring one thing twice.
        /// </summary>
        public IEnumerable<(Edge A, Edge B, string Note)> KnownOverlaps()
        {
            // Overlap is symmetric, and both ends often record it in their own words. Emit each
            // pair once, keeping the first note — two entries for one relationship reads as two
            // problems.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in Edges)
                foreach (var link in e.CorrelatesWith)
                {
                    var other = this[link.Id];
                    if (other == null) continue;

                    string[] pair = { e.Id, other.Id };
                    Array.Sort(pair, StringComparer.OrdinalIgnoreCase);
                    if (!seen.Add(string.Join("|", pair))) continue;

                    yield return (e, other, link.Note ?? "");
                }
        }

        /// <summary>
        /// Structural problems, as human-readable lines. Empty means the registry is internally
        /// consistent — it says nothing about whether the science is right.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();

            if (SchemaVersion <= 0) problems.Add("schemaVersion is missing or not positive.");
            if (string.IsNullOrWhiteSpace(RegistryVersion)) problems.Add("registryVersion is missing.");

            foreach (var dupe in Edges.GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                problems.Add($"duplicate edge id '{dupe.Key}'.");

            foreach (var e in Edges)
            {
                string where = $"edge '{e.Id}'";
                if (string.IsNullOrWhiteSpace(e.Id)) problems.Add("an edge has no id.");
                if (string.IsNullOrWhiteSpace(e.Title)) problems.Add($"{where}: no title.");
                if (e.Claim.Length < 30) problems.Add($"{where}: the claim is too short to be falsifiable.");
                if (e.Scope == null || e.Scope.AssetClasses.Count == 0)
                    problems.Add($"{where}: no asset-class scope — an edge that applies everywhere has not been scoped.");
                // Two kinds of record live here and the rules differ. A MEASURED edge must say what
                // it was tested against, where the verdict is written up, and when — including a
                // measured null, which is a result and not an absence. A QUEUED claim (Untested)
                // has none of those yet by definition, and instead must be traceable to its origin.
                bool measured = e.Evidence != StrategyEvidenceLevel.Untested;
                if (measured)
                {
                    if (e.Controls.Count == 0)
                        problems.Add($"{where}: no controls recorded. Even a null needs to say what it was tested against.");
                    if (string.IsNullOrWhiteSpace(e.Source))
                        problems.Add($"{where}: no source document.");
                    if (!IsIsoDate(e.LastMeasured))
                        problems.Add($"{where}: lastMeasured '{e.LastMeasured}' is not an ISO date.");
                    if (!IsIsoDate(e.FirstMeasured))
                        problems.Add($"{where}: firstMeasured '{e.FirstMeasured}' is not an ISO date.");
                }
                else
                {
                    if (!IsIsoDate(e.Origin?.CapturedOn))
                        problems.Add($"{where}: queued claim with no capture date on its origin.");
                    if (e.ReMeasure?.Command == null)
                        problems.Add($"{where}: queued claim with no proposed test — an idea, not a plan.");
                }

                // The rule that makes the registry mean something: a scorable edge must name the
                // control it beat. "ControlTested" with an empty controls list is a claim, not a test.
                if (e.Evidence == StrategyEvidenceLevel.ControlTested && e.Controls.Count < 2)
                    problems.Add($"{where}: marked ControlTested with fewer than two named controls.");

                // Breadth is required of a SCORABLE edge and optional everywhere else. A claim the
                // application is willing to act on has to say how many independent instruments it
                // actually held on — a pooled significance with a breadth of one is one symbol's
                // behaviour wearing a statistic, and that is a shape this project has been caught
                // by before.
                if (e.Evidence == StrategyEvidenceLevel.ControlTested)
                {
                    if (e.Breadth == null)
                        problems.Add($"{where}: ControlTested with no breadth recorded — on how many instruments did it hold?");
                    else if (e.Breadth.Tested <= 0)
                        problems.Add($"{where}: breadth records {e.Breadth.Held} held but nothing tested.");
                    else if (e.Breadth.Held > e.Breadth.Tested)
                        problems.Add($"{where}: breadth held ({e.Breadth.Held}) exceeds tested ({e.Breadth.Tested}).");
                }

                if (e.Breadth != null && e.Breadth.Held < 0)
                    problems.Add($"{where}: negative breadth.");

                // An untested edge is a QUEUE ENTRY. Without an origin nobody can re-read the
                // claim in its original words, and an ambiguous result becomes unresolvable.
                if (e.Evidence == StrategyEvidenceLevel.Untested && e.Origin == null)
                    problems.Add($"{where}: Untested with no origin — an untraceable idea, not a queued claim.");

                foreach (var link in e.CorrelatesWith)
                    if (this[link.Id] == null)
                        problems.Add($"{where}: correlatesWith points at unknown edge '{link.Id}'.");

                foreach (var d in e.Decay)
                    if (!IsIsoDate(d.AsOf))
                        problems.Add($"{where}: decay entry has a non-ISO asOf '{d.AsOf}'.");
            }

            return problems;
        }

        private static bool IsIsoDate(string? s) =>
            !string.IsNullOrWhiteSpace(s) && DateTime.TryParseExact(
                s, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _);
    }

    /// <summary>One tested claim. See <see cref="EdgeRegistry"/> for why this is the unit of work.</summary>
    public sealed record Edge
    {
        public string Id { get; init; } = "";
        public string Title { get; init; } = "";

        /// <summary>The claim as something that could be refuted, not as a description of a technique.</summary>
        public string Claim { get; init; } = "";

        /// <summary>Loose grouping — momentum, trend, volume, exit, event, regime, positioning, cycle, ml.</summary>
        public string Family { get; init; } = "";

        public EdgeScope Scope { get; init; } = new();
        public EdgeEffect Effect { get; init; } = new();

        /// <summary>The controls this claim was measured against. The most important field here.</summary>
        public IReadOnlyList<string> Controls { get; init; } = Array.Empty<string>();

        /// <summary>
        /// On how many independent instruments the claim held. See <see cref="EdgeBreadth"/> for why
        /// this sits beside the p-value rather than inside the notes.
        /// </summary>
        public EdgeBreadth? Breadth { get; init; }

        public StrategyEvidenceLevel Evidence { get; init; } = StrategyEvidenceLevel.Untested;

        public string FirstMeasured { get; init; } = "";
        public string LastMeasured { get; init; } = "";

        /// <summary>Effect size re-measured over time. An edge that fades shows it here first.</summary>
        public IReadOnlyList<EdgeDecayPoint> Decay { get; init; } = Array.Empty<EdgeDecayPoint>();

        /// <summary>Edges this one overlaps with — the raw material for not counting information twice.</summary>
        public IReadOnlyList<EdgeLink> CorrelatesWith { get; init; } = Array.Empty<EdgeLink>();

        public string Source { get; init; } = "";
        public EdgeReMeasure? ReMeasure { get; init; }

        /// <summary>
        /// Where the CLAIM came from, when it came from outside this project — a video, a book, a
        /// paper. Distinct from <see cref="Source"/>, which is where our own verdict is written up.
        /// An untested edge without an origin is an idea nobody can trace; with one, the claim can
        /// be re-read in its original words when the test comes out ambiguous.
        /// </summary>
        public EdgeOrigin? Origin { get; init; }

        /// <summary>See <see cref="EdgeRegistry.Scorable"/>.</summary>
        [JsonIgnore]
        public bool CanScore => Evidence == StrategyEvidenceLevel.ControlTested;

        public bool AppliesTo(string assetClass) =>
            Scope.AssetClasses.Any(c => string.Equals(c, assetClass, StringComparison.OrdinalIgnoreCase));

        /// <summary>A one-line summary for a list or an announcement.</summary>
        [JsonIgnore]
        public string Summary =>
            $"{Title} [{Evidence}] — {string.Join("/", Scope.AssetClasses)}, last measured {LastMeasured}";
    }

    public sealed record EdgeScope
    {
        public IReadOnlyList<string> AssetClasses { get; init; } = Array.Empty<string>();
        public int HorizonBars { get; init; }
        public string? Timeframe { get; init; }
        public int? UniverseMin { get; init; }
        public string? Notes { get; init; }
    }

    public sealed record EdgeEffect
    {
        public string? Measure { get; init; }
        public double? Value { get; init; }
        public string? Unit { get; init; }
        public double? P { get; init; }
        public double? N { get; init; }
        public string? Notes { get; init; }
    }

    /// <summary>
    /// On how many independent instruments did this claim actually hold?
    ///
    /// <para>
    /// A p-value answers "could this pooled number have arisen by chance". Breadth answers a
    /// different and often more useful question: <b>does it show up in more than one place</b>. A
    /// result that is significant across a pooled sample but present on two symbols out of thirty is
    /// one symbol's behaviour wearing a statistic — and this project has been caught by exactly that
    /// shape before, which is why "per-symbol and per-era breakdown" is already a standing control.
    /// </para>
    ///
    /// <para>
    /// Recorded as a field because a breakdown that lives only in prose cannot be queried, sorted or
    /// compared between edges. Prompted by an outside analysis whose single best idea was treating
    /// "survived on twenty unrelated tickers" as a first-class criterion rather than a footnote.
    /// </para>
    ///
    /// <para>
    /// <b>Instruments must be independent for the count to mean anything.</b> The same asset from two
    /// providers is one instrument; SPY and VOO are very nearly one instrument. <see cref="Notes"/>
    /// is where that judgement is written down, because it cannot be inferred from the number.
    /// </para>
    /// </summary>
    public sealed record EdgeBreadth
    {
        /// <summary>How many instruments the claim held on.</summary>
        public int Held { get; init; }

        /// <summary>How many were tested. <c>Held</c> out of <c>Tested</c> is the whole point.</summary>
        public int Tested { get; init; }

        /// <summary>
        /// Which ones held, when the list is short enough to be worth naming. Optional — for a
        /// forty-symbol sweep the count is the finding and the list is noise.
        /// </summary>
        public IReadOnlyList<string> Instruments { get; init; } = Array.Empty<string>();

        /// <summary>
        /// How independence was judged, and anything that qualifies the count — deduplication,
        /// correlated pairs, an arm that was underpowered rather than negative.
        /// </summary>
        public string? Notes { get; init; }

        /// <summary>Share of tested instruments on which the claim held, or null when nothing was tested.</summary>
        [JsonIgnore]
        public double? Share => Tested > 0 ? (double)Held / Tested : null;

        [JsonIgnore]
        public string Summary => Tested > 0 ? $"{Held}/{Tested}" : "not recorded";
    }

    public sealed record EdgeDecayPoint
    {
        public string AsOf { get; init; } = "";
        public double? Value { get; init; }
        public string? Note { get; init; }
    }

    /// <summary>Provenance of an external claim — who said it, where, and when we captured it.</summary>
    public sealed record EdgeOrigin
    {
        public string? Kind { get; init; }      // video, book, paper, practitioner
        public string? Who { get; init; }
        public string? Url { get; init; }
        public string? CapturedOn { get; init; }
        public string? Quote { get; init; }     // the claim in their words, so a null result can be checked against what was actually said
    }

    public sealed record EdgeLink
    {
        public string Id { get; init; } = "";
        public string? Note { get; init; }
        public double? Correlation { get; init; }
    }

    /// <summary>
    /// How this edge gets re-measured. <c>Implemented</c> is honest about the current state: the
    /// registry records nineteen verdicts and only a few of their tests are one command away. A
    /// false here is a to-do, not a hidden failure.
    /// </summary>
    public sealed record EdgeReMeasure
    {
        public string? Command { get; init; }
        public bool Implemented { get; init; }
    }
}
