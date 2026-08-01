using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// The transfer format for strategy specs — the documented route by which a strategy gets
    /// into a user's library.
    ///
    /// <para>
    /// Before 2026-08-01 the app wrote thirty research specs into every new library on first
    /// launch. That is gone: the terminal starts empty and specs arrive only when the user
    /// imports them (see <c>docs/STRATEGY_LIBRARY_POLICY.md</c>). A bundle is a plain JSON file —
    /// the research lab writes one with <c>StrategyLab catalogue export</c>, and a user can hand
    /// one to another user, or move their own strategies between machines.
    /// </para>
    ///
    /// <para>
    /// The format owns the compatibility contract, so it lives in Core next to the library that
    /// reads it; the CONTENT is the lab's. Core has no knowledge of any particular strategy.
    /// </para>
    /// </summary>
    public sealed record StrategyBundle(
        int FormatVersion,
        string Source,
        string? CatalogueVersion,
        DateTime ExportedUtc,
        IReadOnlyList<StrategySpec> Strategies)
    {
        /// <summary>
        /// Current writer version. Bump only for a breaking change; the reader accepts anything
        /// at or below <see cref="MaxSupportedFormatVersion"/> so older bundles keep importing.
        /// </summary>
        public const int CurrentFormatVersion = 1;

        /// <summary>Highest version this build can read. A newer file is refused with a clear message.</summary>
        public const int MaxSupportedFormatVersion = 1;
    }

    /// <summary>
    /// Outcome of an import, in enough detail to announce. Every count is separate on purpose:
    /// "imported 4" and "skipped 26 already in your library" are different facts and a
    /// screen-reader user should hear both rather than a single ambiguous "done".
    /// </summary>
    /// <param name="Imported">Names of specs added to the library.</param>
    /// <param name="SkippedExisting">Names skipped because that id is already present — never overwritten.</param>
    /// <param name="Rejected">Human-readable reasons individual specs were refused.</param>
    /// <param name="Error">Set when the file could not be read at all; nothing was imported.</param>
    /// <param name="AutoExecutionCount">
    /// How many imported specs are set to place orders rather than raise suggestions. Nothing runs
    /// until the user starts it, but this is worth saying out loud before they do.
    /// </param>
    public sealed record StrategyImportResult(
        IReadOnlyList<string> Imported,
        IReadOnlyList<string> SkippedExisting,
        IReadOnlyList<string> Rejected,
        string? Error,
        int AutoExecutionCount)
    {
        public bool Success => Error == null;

        public static StrategyImportResult Failed(string error) =>
            new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), error, 0);

        /// <summary>One sentence suitable for a live region or a CLI line.</summary>
        public string Describe()
        {
            if (Error != null) return $"Import failed: {Error}";

            var parts = new List<string> { $"{Imported.Count} strategy{(Imported.Count == 1 ? "" : "s")} imported" };
            if (SkippedExisting.Count > 0)
                parts.Add($"{SkippedExisting.Count} skipped — already in your library");
            if (Rejected.Count > 0)
                parts.Add($"{Rejected.Count} rejected");
            if (AutoExecutionCount > 0)
                parts.Add($"{AutoExecutionCount} set to place orders automatically once started — review before starting");
            return string.Join("; ", parts) + ".";
        }
    }

    /// <summary>
    /// Reads and writes <see cref="StrategyBundle"/> files and imports them into an
    /// <see cref="IStrategyLibrary"/>.
    ///
    /// <para>Import rules, all of them deliberate:</para>
    /// <list type="bullet">
    ///   <item>An id already in the library is never overwritten — the user's edits win, always.</item>
    ///   <item><c>IsAutoActivate</c> is forced false. Importing a file must not start anything.</item>
    ///   <item>
    ///     Roslyn-source specs are refused. Importing one means compiling and running C# that came
    ///     out of a file, and a strategy library is not a plausible place to accept that. The
    ///     research catalogue contains none; user code belongs in the Custom tab where the user
    ///     pastes it themselves.
    ///   </item>
    ///   <item>A malformed file returns an error. It never throws at the caller and never clears the library.</item>
    /// </list>
    /// </summary>
    public static class StrategyBundleService
    {
        private static readonly JsonSerializerOptions _options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>Serializes specs into bundle JSON. <paramref name="exportedUtc"/> is injected so exports are reproducible.</summary>
        public static string Write(
            IEnumerable<StrategySpec> specs,
            string source,
            string? catalogueVersion,
            DateTime exportedUtc)
        {
            var bundle = new StrategyBundle(
                StrategyBundle.CurrentFormatVersion,
                source,
                catalogueVersion,
                exportedUtc,
                specs.ToList());
            return JsonSerializer.Serialize(bundle, _options);
        }

        /// <summary>
        /// Parses bundle JSON. Returns null and an error message rather than throwing — an import
        /// is user-supplied input and a bad paste is an ordinary event, not an exception.
        /// </summary>
        public static StrategyBundle? Read(string json, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "the file is empty";
                return null;
            }

            StrategyBundle? bundle;
            try
            {
                bundle = JsonSerializer.Deserialize<StrategyBundle>(json, _options);
            }
            catch (JsonException ex)
            {
                error = $"this does not look like a strategy file ({ex.Message})";
                return null;
            }

            if (bundle == null)
            {
                error = "this does not look like a strategy file";
                return null;
            }
            if (bundle.FormatVersion > StrategyBundle.MaxSupportedFormatVersion)
            {
                error = $"the file uses strategy format version {bundle.FormatVersion}; this version of the app reads up to {StrategyBundle.MaxSupportedFormatVersion}. Update the app.";
                return null;
            }
            if (bundle.Strategies == null || bundle.Strategies.Count == 0)
            {
                error = "the file contains no strategies";
                return null;
            }
            return bundle;
        }

        /// <summary>Reads and imports in one step. See the class remarks for the rules.</summary>
        public static StrategyImportResult Import(string json, IStrategyLibrary library)
        {
            var bundle = Read(json, out string? error);
            if (bundle == null) return StrategyImportResult.Failed(error ?? "unreadable file");
            return Import(bundle, library);
        }

        public static StrategyImportResult Import(StrategyBundle bundle, IStrategyLibrary library)
        {
            var imported = new List<string>();
            var skipped = new List<string>();
            var rejected = new List<string>();
            int autoExec = 0;

            foreach (var spec in bundle.Strategies)
            {
                if (spec == null) { rejected.Add("an entry was empty"); continue; }

                if (string.IsNullOrWhiteSpace(spec.Id) || string.IsNullOrWhiteSpace(spec.Name))
                {
                    rejected.Add($"a strategy is missing an id or a name");
                    continue;
                }
                if (spec.Conditions == null || spec.Risk == null)
                {
                    rejected.Add($"{spec.Name}: no conditions or no risk plan");
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(spec.RoslynSource))
                {
                    rejected.Add($"{spec.Name}: contains program code — import it through the Custom tab instead");
                    continue;
                }
                if (library.GetById(spec.Id) != null)
                {
                    skipped.Add(spec.Name);
                    continue;
                }

                // Never auto-start an imported spec, whatever the file claims.
                var safe = spec with { IsAutoActivate = false, UpdatedUtc = DateTime.UtcNow };
                if (safe.ExecutionMode == StrategyExecutionMode.Auto) autoExec++;

                library.Upsert(safe);
                imported.Add(safe.Name);
            }

            return new StrategyImportResult(imported, skipped, rejected, null, autoExec);
        }
    }
}
