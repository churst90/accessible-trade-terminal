using System.Text.Json;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// The built-in strategies the research catalogue has retired, taken out of a saved library
    /// (Cody, 2026-11-16: "Delete retired strategies"). Until 2026-08-01 every new library was seeded
    /// with thirty catalogue specs; twenty have since been retired because a part they need was
    /// removed or failed its test, so they sit in older libraries and can never fire.
    ///
    /// <para>
    /// They are moved, not destroyed: <see cref="MoveOut"/> writes them to
    /// <see cref="BackupFileName"/> beside <c>strategies.json</c> before the library drops them, and
    /// keeps the library as it was if that write fails. Only these exact <c>builtin.*</c> ids move;
    /// a user's own strategy never has one. The list is the catalogue's own retired list
    /// (<c>CatalogueProvenance.Retired</c> in the research lab, which the app does not reference);
    /// a test holds the two equal.
    /// </para>
    /// </summary>
    public static class RetiredBuiltInStrategies
    {
        public const string BackupFileName = "strategies.retired.json";

        public static readonly IReadOnlySet<string> Ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // 2026-08-01: the Cipher SR trilogies, the MVRV trilogy and the two symmetric shorts.
            "builtin.long.v16-trilogy",
            "builtin.short.v16-trilogy",
            "builtin.long.v17-gold-trilogy",
            "builtin.long.v21-mvrv-capitulation-trilogy",
            "builtin.short.v23-cipherb-weekly",
            "builtin.short.v23r-cipherb-faber",
            // 2026-10-21 and 10-22: the old Cipher C's pulse and Loukas Cycle Detection removed.
            "builtin.long.pulse-reversal",
            "builtin.long.v24-cycle-low-reversal",
            // 2026-10-31: Cipher B's parts Market Cipher B does not draw removed.
            "builtin.long.v14-hidden-bull-sma200",
            "builtin.long.v15-blue-dot-bull-div",
            "builtin.short.v18-refined",
            "builtin.long.v23-cipherb-weekly",
            "builtin.long.v23r-cipherb-faber",
            "builtin.short.v23rf-cipherb-funding",
            "builtin.long.v23p-cipherb-pivots",
            "builtin.long.v23h-cipherb-hurst",
            "builtin.short.v23h-cipherb-hurst",
            "builtin.long.v23a-cipherb-avwap",
            "builtin.long.v23or-cipherb-orconf",
            "builtin.long.v23c-cipherb-cot",
        };

        /// <summary>
        /// Splits the retired built-ins off <paramref name="specs"/> into the backup file in
        /// <paramref name="directory"/>, added to any already there (a later copy of an id replaces
        /// the earlier one). Returns the specs to keep: all of them when none is retired or the
        /// backup cannot be written.
        /// </summary>
        public static List<StrategySpec> MoveOut(List<StrategySpec> specs, string directory, JsonSerializerOptions options)
        {
            var retired = specs.Where(s => Ids.Contains(s.Id)).ToList();
            if (retired.Count == 0) return specs;
            try
            {
                string path = Path.Combine(directory, BackupFileName);
                var backup = File.Exists(path)
                    ? JsonSerializer.Deserialize<List<StrategySpec>>(File.ReadAllText(path), options) ?? new List<StrategySpec>()
                    : new List<StrategySpec>();
                foreach (var spec in retired)
                {
                    backup.RemoveAll(b => string.Equals(b.Id, spec.Id, StringComparison.OrdinalIgnoreCase));
                    backup.Add(spec);
                }
                AtomicFile.WriteAllText(path, JsonSerializer.Serialize(backup, options));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Retired strategies kept in the library, the backup failed: {ex.Message}");
                return specs;
            }
            return specs.Where(s => !Ids.Contains(s.Id)).ToList();
        }
    }
}
