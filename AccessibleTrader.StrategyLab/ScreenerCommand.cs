using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Layer 1 of the crypto screener: the deterministic veto.
///
/// <para>
/// ── What this is, and the one thing it is not ───────────────────────────────
/// This <b>sorts by flags raised, never by expected return</b>, and that is not a hedge — it is the
/// design. Nothing in this project's ledger predicts returns for small or new tokens, and the
/// honest expected value of trading brand-new listings is negative before fees. What can be built
/// responsibly is a filter that removes the obviously disqualified before a chart is ever opened,
/// which makes the gamble informed rather than blind. A screener that ranked by upside would be
/// making a claim this project has never earned.
/// </para>
///
/// <para>
/// ── Every check is arithmetic ───────────────────────────────────────────────
/// No model, no LLM, no judgement, nothing that cannot be recomputed identically tomorrow. Each one
/// is a comparison over a number the universe recorder already captured. That matters for a reason
/// beyond taste: layer 2 is the forward test of whether these flags actually predicted death, and a
/// rule containing judgement cannot be replayed against a historical snapshot to find out.
/// </para>
///
/// <para>
/// ── The thresholds are conventions, not findings ────────────────────────────
/// Not one of them has been tested against forward outcomes. They are the machine-checkable half of
/// the 24-point vetting guide, and they are labelled in the output as conventional red flags. The
/// standing prediction, recorded before any of this was built, is that they work as a <b>veto and
/// not as a timing signal</b>: they should avoid losses and should not pick winners. Testing that
/// needs point-in-time universe snapshots taken FORWARD — dead tokens are absent from today's
/// listings, so any backtest on the surviving universe is poisoned — which is what
/// <c>record-universe</c> exists to accumulate.
/// </para>
///
/// <para>
/// ── It runs on the archive, not on a live sweep ─────────────────────────────
/// Deliberately. The daily snapshot already carries supply, dilution, turnover and drawdown for a
/// thousand assets, so the whole of layer 1 costs zero API calls and can be re-run against any past
/// day — which is exactly how layer 2 will eventually be run.
/// </para>
/// </summary>
public static class ScreenerCommand
{
    /// <summary>
    /// One check: a name, the reason it exists, and whether this asset trips it.
    /// The reason travels WITH the flag because a screen output that says only "FDV" teaches nothing
    /// and will be either over-trusted or ignored.
    /// </summary>
    internal sealed record Check(string Name, bool Tripped, string Detail);

    public static int Run(string archiveDir, string? date, int top, int maxFlags, bool showClean,
                          string? only = null)
    {
        archiveDir = UniverseRecorderCommand.Anchor(archiveDir);
        var snapshots = UniverseRecorderCommand.Snapshots(archiveDir);
        if (snapshots.Count == 0)
        {
            Console.Error.WriteLine($"No universe snapshots in '{archiveDir}'. Run: record-universe");
            return 1;
        }

        string path = date == null
            ? snapshots[^1]
            : snapshots.FirstOrDefault(s => UniverseRecorderCommand.DateOf(s) == date)
              ?? throw new FileNotFoundException($"No snapshot for {date}.");

        var universe = UniverseRecorderCommand.Load(path);
        string asOf = UniverseRecorderCommand.DateOf(path);

        var scored = universe.Values
            .Where(r => r.Rank.HasValue && r.Rank <= top)
            .Select(r => (Row: r, Checks: Screen(r)))
            .Select(x => (x.Row, x.Checks, Flags: x.Checks.Count(c => c.Tripped)))
            .OrderByDescending(x => x.Flags)
            .ThenBy(x => x.Row.Rank)
            .ToList();

        // Named lookup: the single most useful thing when calibrating thresholds is to check a
        // handful of assets you already have an opinion about, and see whether the screen agrees.
        if (only != null)
        {
            var wanted = only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Console.WriteLine();
            Console.WriteLine($"── NAMED ASSETS, as at {asOf} ──");
            Console.WriteLine();
            foreach (var w in wanted)
            {
                var hit = universe.Values.FirstOrDefault(v =>
                              string.Equals(v.Symbol, w, StringComparison.OrdinalIgnoreCase))
                       ?? universe.Values.FirstOrDefault(v =>
                              string.Equals(v.Id, w, StringComparison.OrdinalIgnoreCase));
                if (hit == null) { Console.WriteLine($"  {w,-8} not in the recorded universe"); continue; }

                var cs = Screen(hit);
                int n = cs.Count(c => c.Tripped);
                Console.WriteLine($"  {hit.Symbol?.ToUpperInvariant(),-8} #{hit.Rank,-5} {Truncate(hit.Name, 24),-24} "
                                + $"{n} flag{(n == 1 ? "" : "s")} of {cs.Count} checks");
                // Only a TRIPPED check may print its sentence. The Detail text is written as the
                // failing case ("no maximum supply — issuance is uncapped"), so printing it beside
                // "ok" asserts the opposite of the truth: Bitcoin listed as "ok  no maximum supply"
                // is a flat falsehood produced purely by formatting.
                foreach (var c in cs)
                    Console.WriteLine(c.Tripped ? $"        FLAG  {c.Detail}" : $"        ok    {c.Name}");
                Console.WriteLine();
            }
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("═════ CRYPTO SCREEN — LAYER 1, THE DETERMINISTIC VETO ═════");
        Console.WriteLine($"Universe as at {asOf}, top {top} by market cap. {scored.Count} assets screened.");
        Console.WriteLine("Sorted by FLAGS RAISED, not by return. Nothing here predicts price.");
        Console.WriteLine();

        // Distribution first: it says whether the thresholds discriminate at all. A screen that
        // flags everything and a screen that flags nothing are equally useless, and both look
        // perfectly reasonable when you only read the top of the list.
        Console.WriteLine("── HOW MANY FLAGS, ACROSS THE UNIVERSE ──");
        foreach (var g in scored.GroupBy(x => x.Flags).OrderBy(g => g.Key))
            Console.WriteLine($"  {g.Key} flag{(g.Key == 1 ? " " : "s")}  {g.Count(),5}  {Bar(g.Count(), scored.Count)}");
        Console.WriteLine();

        var worst = scored.Where(x => x.Flags >= maxFlags).ToList();
        Console.WriteLine($"── {worst.Count} ASSETS RAISING {maxFlags}+ FLAGS ──");
        Console.WriteLine();
        foreach (var (row, checks, flags) in worst.Take(40))
        {
            Console.WriteLine($"  {row.Symbol?.ToUpperInvariant(),-8} #{row.Rank,-5} {Truncate(row.Name, 26),-26} {flags} flags");
            foreach (var c in checks.Where(c => c.Tripped))
                Console.WriteLine($"        · {c.Detail}");
        }
        if (worst.Count > 40) Console.WriteLine($"  … and {worst.Count - 40} more.");
        Console.WriteLine();

        if (showClean)
        {
            var clean = scored.Where(x => x.Flags == 0).Take(40).ToList();
            Console.WriteLine($"── {scored.Count(x => x.Flags == 0)} ASSETS RAISING NO FLAGS ──");
            Console.WriteLine("  Passing a filter is not a recommendation. It means nothing cheap was wrong.");
            Console.WriteLine();
            foreach (var (row, _, _) in clean)
                Console.WriteLine($"  {row.Symbol?.ToUpperInvariant(),-8} #{row.Rank,-5} {Truncate(row.Name, 30)}");
            Console.WriteLine();
        }

        Console.WriteLine("── HOW TO READ THIS ──");
        Console.WriteLine("  A flag is a CONVENTIONAL red flag, not a tested one. None of these thresholds");
        Console.WriteLine("  has been checked against forward outcomes, because doing that honestly needs");
        Console.WriteLine("  point-in-time snapshots of a universe that includes the assets that died —");
        Console.WriteLine("  and today's listings, by construction, do not contain them.");
        Console.WriteLine();
        Console.WriteLine($"  `universe-status` reports how close the archive is to answering that.");
        Console.WriteLine("  Until it can, treat this as a filter that removes obvious garbage cheaply,");
        Console.WriteLine("  and expect it to avoid losses rather than to pick winners.");
        Console.WriteLine();
        return 0;
    }

    // ── The checks ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Beyond this an FDV-to-market-cap ratio is a broken field rather than extreme dilution.
    /// Even the most aggressively vesting real token is a two-digit multiple.
    /// </summary>
    private const double AbsurdRatio = 1000.0;

    /// <summary>
    /// Every machine-checkable disqualifier from the vetting guide that the daily snapshot supports.
    ///
    /// <para>
    /// A check whose input is missing returns <b>not tripped</b>, never tripped. Absent data is not
    /// evidence of a problem, and treating it as one would flag every asset the aggregator happens
    /// to report thinly — punishing obscurity rather than measuring quality. Where absence IS the
    /// finding, the check says so explicitly and tests for the absence itself, which is a different
    /// thing from failing to compute.
    /// </para>
    /// </summary>
    internal static List<Check> Screen(UniverseRecorderCommand.Row r)
    {
        var checks = new List<Check>();

        // ── Stablecoins are excluded from the price-shaped checks ────────────────
        //
        // Drawdown from an all-time high and 24-hour turnover mean something completely different
        // for an asset that is supposed to sit at a dollar: a stablecoin is SUPPOSED to be 0% above
        // its high and to turn over many times its market cap. Running the standard checks on one
        // produces flags that are definitionally true and carry no information, and the first run
        // duly flagged two dollar-pegged tokens as "100% below all-time high". Supply and dilution
        // checks still apply — those are about issuance, which matters for a stablecoin too.
        bool pegged = IsLikelyStablecoin(r);

        // ── Dilution ─────────────────────────────────────────────────────────────
        // Fully diluted value far above market cap means most of the supply has not been issued
        // yet. Every future unlock is a seller who paid less than you, and the guide is right that
        // unlock schedules are the single biggest driver of small-cap drawdowns.
        //
        // Above AbsurdRatio the number is not a dilution finding, it is a broken field — the first
        // run of this screen confidently reported an asset as having "FDV 999999995.3x market cap",
        // which is a sentinel value wearing a sentence. Stating a fabricated fact in the same voice
        // as a real one is worse than staying quiet, so an implausible ratio is reported as bad
        // data and trips nothing.
        if (r.MarketCap is > 0 && r.FullyDiluted is > 0)
        {
            double ratio = r.FullyDiluted.Value / r.MarketCap.Value;
            if (ratio > AbsurdRatio)
                checks.Add(new("fdv-bad", false,
                    "reported FDV is implausible — treated as missing, not as dilution"));
            else
                checks.Add(new("fdv", ratio > 3.0,
                    $"FDV is {ratio:F1}x market cap — most of the supply is not issued yet"));
        }

        // Same idea from the supply side, and it catches assets the FDV field misses.
        if (r.Circulating is > 0 && r.MaxSupply is > 0)
        {
            double share = r.Circulating.Value / r.MaxSupply.Value;
            checks.Add(new("float", share < 0.30,
                $"only {share * 100:F0}% of maximum supply is circulating"));
        }

        // No cap at all. Not automatically disqualifying — several large, credible chains are
        // uncapped by design — but it is a fact the buyer should have to know they are accepting.
        checks.Add(new("uncapped", r.MaxSupply is null or <= 0,
            "no maximum supply — issuance is uncapped"));

        // ── Liquidity, flagged at BOTH ends ──────────────────────────────────────
        // Too little and you cannot leave a position. Too much is the classic wash-trading tell:
        // daily volume several times the entire market cap is not organic interest.
        if (!pegged && r.MarketCap is > 0 && r.Volume24h is >= 0)
        {
            double turnover = r.Volume24h.Value / r.MarketCap.Value;
            checks.Add(new("illiquid", turnover < 0.005,
                $"turnover is {turnover * 100:F2}% of market cap — you may not be able to exit"));
            checks.Add(new("wash", turnover > 1.5,
                $"turnover is {turnover * 100:F0}% of market cap in a day — a wash-trading tell"));
        }

        // ── Size ─────────────────────────────────────────────────────────────────
        // Below this the market cap is small enough that a single holder can move it at will.
        checks.Add(new("microcap", r.MarketCap is > 0 and < 10_000_000,
            $"market cap {Money(r.MarketCap)} — one holder can move this at will"));

        // ── Drawdown from the all-time high ──────────────────────────────────────
        // Not a valuation claim. A token 95% below its high has already had a full cycle of buyers
        // who are now underwater, and every recovery has to pass through them.
        if (!pegged && r.Ath is > 0 && r.Price is > 0)
        {
            double dd = 1.0 - r.Price.Value / r.Ath.Value;
            checks.Add(new("drawdown", dd > 0.95,
                $"{dd * 100:F0}% below its all-time high — a full cycle of holders is underwater"));
        }

        // ── A peg that is not holding ────────────────────────────────────────────
        // The one check that exists only for stablecoins, and it replaces the two that were
        // suppressed. For an asset whose entire proposition is a dollar, being off it is the only
        // question worth asking mechanically.
        if (pegged && r.Price is > 0)
        {
            double off = Math.Abs(r.Price.Value - 1.0);
            checks.Add(new("depeg", off > 0.02,
                $"trading at ${r.Price.Value:F4} — {off * 100:F1}% off its dollar peg"));
        }

        return checks;
    }

    /// <summary>
    /// A price that has never been far from a dollar. Deliberately based on PRICE rather than on a
    /// name or a curated list: a list goes stale the moment a new stablecoin launches, and matching
    /// on "USD" in the ticker would catch wrapped assets and miss euro-pegged ones.
    /// </summary>
    internal static bool IsLikelyStablecoin(UniverseRecorderCommand.Row r)
        => r.Price is > 0.90 and < 1.10 && r.Ath is > 0.90 and < 1.60;

    // ── Formatting ──────────────────────────────────────────────────────────────

    private static string Bar(int n, int total)
    {
        int width = total == 0 ? 0 : (int)Math.Round(40.0 * n / total);
        return new string('#', Math.Max(n > 0 ? 1 : 0, width));
    }

    private static string Money(double? v) => v switch
    {
        null => "unknown",
        >= 1e9 => $"${v / 1e9:F1}B",
        >= 1e6 => $"${v / 1e6:F1}M",
        _ => $"${v:N0}"
    };

    private static string Truncate(string? s, int n)
        => string.IsNullOrEmpty(s) ? "" : s.Length <= n ? s : s[..(n - 1)] + "…";
}
