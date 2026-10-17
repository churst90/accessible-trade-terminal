using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// For each catalogue spec that still names a RETIRED leaf, backtests the spec as written, with the
/// dead leaves removed, and with each proposed replacement — so Cody can choose a fix with the
/// numbers beside it. Nothing here edits the catalogue.
///
/// <para>
/// WHY "AS WRITTEN" AND "REMOVED" ARE BOTH RUN. A retired leaf never fires, so inside an OR it is
/// already as good as removed — the two should print the same numbers. If they do not, a dead leaf
/// is doing something (a refusal inside an AND vetoing the whole group, say), which is exactly what
/// a proposal has to know before it is applied. The comparison is the check, not a formality.
/// </para>
///
/// <para>
/// Each variant runs on the assets the spec was recorded on, over the whole sample and over rolling
/// 1500-bar windows stepped 250 (the battery's walk-forward), with the battery's costs (10 bps
/// commission + 5 bps slippage, no reverse-on-signal) and the spec's OWN risk plan. Every variant is
/// a try: the count is printed so it can go into variantsTried.
/// </para>
/// </summary>
public static class SpecVariantsCommand
{
    private sealed record Variant(string Name, Func<ConditionLeaf, ConditionNode?> Map);

    private static readonly string[] Dead = { "CIPHER_A.Buy Signal", "CIPHER_SR.Support" };

    private static ConditionLeaf Leaf(ConditionLeaf like, string id, string sig) =>
        like with { Id = like.Id + "-" + id, SignalDescriptorId = sig };

    /// <summary>The proposals. Each maps a dead leaf to its replacement (null = drop it); live leaves pass through.</summary>
    private static readonly Variant[] Variants =
    {
        new("as written (dead leaves never fire)", l => l),
        new("dead leaves removed", l => Dead.Contains(l.SignalDescriptorId) ? null : l),
        new("A.Buy -> A.Green Dot; SR removed", l =>
            l.SignalDescriptorId == "CIPHER_A.Buy Signal" ? Leaf(l, "gd", "CIPHER_A.Green Dot")
            : l.SignalDescriptorId == "CIPHER_SR.Support" ? null : l),
        new("A.Buy -> A.Blue Triangle; SR removed", l =>
            l.SignalDescriptorId == "CIPHER_A.Buy Signal" ? Leaf(l, "bt", "CIPHER_A.Blue Triangle")
            : l.SignalDescriptorId == "CIPHER_SR.Support" ? null : l),
        new("A.Buy removed; SR -> close crosses above Support Zone", l =>
            l.SignalDescriptorId == "CIPHER_A.Buy Signal" ? null
            : l.SignalDescriptorId == "CIPHER_SR.Support"
                ? new ConditionLeaf(l.Id + "-srz", "PRICE.line", LeafOperator.CrossesAboveLine,
                      SecondSignalDescriptorId: "CIPHER_SR.Support Zone", Score: l.Score)
                : l),
    };

    private static readonly (string SpecId, string[] Files)[] Targets =
    {
        (StrategyCatalogue_Ids.BareBullPulse, new[] { "bitstamp_BTC_USDT_1d.json", "bitstamp_ETH_USDT_1d.json" }),
        (StrategyCatalogue_Ids.FaberPulse, new[] { "bitstamp_BTC_USDT_1d.json", "bitstamp_ETH_USDT_1d.json" }),
        (StrategyCatalogue_Ids.CapitulationBuy, new[] { "bitstamp_BTC_USDT_1d.json", "bitstamp_ETH_USDT_1d.json",
                                                        "bitstamp_XRP_USDT_1d.json", "bitstamp_LTC_USDT_1d.json" }),
    };

    private static class StrategyCatalogue_Ids
    {
        public const string BareBullPulse = "builtin.long.bare-bull-pulse";
        public const string FaberPulse = "builtin.long.faber-pulse";
        public const string CapitulationBuy = "builtin.long.capitulation-buy";
    }

    /// <summary>Where each Cipher-dependent catalogue spec was recorded (CatalogueProvenance), for
    /// <c>--all</c>; every other spec runs on BTC and ETH daily.</summary>
    private static readonly Dictionary<string, string[]> RecordedOn = new()
    {
        ["builtin.long.v14-hidden-bull-sma200"] = new[] { "bitstamp_BTC_USDT_4h.json" },
        ["builtin.short.v18-refined"] = new[] { "bitstamp_BTC_USDT_4h.json", "bitstamp_BTC_USDT_1d.json" },
        ["builtin.long.v23-cipherb-weekly"] = new[] { "bitstamp_BTC_USDT_4h.json", "bitstamp_BTC_USDT_1d.json" },
        ["builtin.long.v23r-cipherb-faber"] = new[] { "bitstamp_BTC_USDT_4h.json", "bitstamp_ETH_USDT_4h.json" },
        ["builtin.short.v23h-cipherb-hurst"] = new[] { "mexc_KAS_USDT_4h.json" },
        ["builtin.long.v23c-cipherb-cot"] = new[] { "twelvedata_XAU_USD_1d.json", "twelvedata_SPY_1d.json", "twelvedata_QQQ_1d.json" },
        ["builtin.long.capitulation-buy"] = new[] { "bitstamp_BTC_USDT_1d.json", "bitstamp_ETH_USDT_1d.json",
                                                    "bitstamp_XRP_USDT_1d.json", "bitstamp_LTC_USDT_1d.json" },
    };

    public static async Task<int> RunAsync(string snapshotDir, int windowBars = 1500, int stepBars = 250, int warmup = 200,
        bool all = false)
    {
        // --all: every catalogue spec with a Cipher leaf, as written only (the OLD-vs-NEW sweep of the
        // whole catalogue). Without it: the three specs with retired leaves and every proposal.
        var targets = all
            ? CatalogueProvenance_Specs()
                .Where(sp => Leaves(sp.Conditions).Any(l => l.SignalDescriptorId.StartsWith("CIPHER_", StringComparison.OrdinalIgnoreCase)))
                .Select(sp => (sp.Id, RecordedOn.TryGetValue(sp.Id, out var f) ? f
                    : new[] { "bitstamp_BTC_USDT_1d.json", "bitstamp_ETH_USDT_1d.json" }))
                .ToArray()
            : Targets;
        var variants = all ? Variants.Take(1).ToArray() : Variants;
        Console.WriteLine("SPEC VARIANTS — catalogue specs with retired leaves: as written / removed / replaced");
        Console.WriteLine(LabCipherProfile.Describe());
        var host = LabHost.Build();
        var factory = host.Services.GetRequiredService<IConfigurableStrategyFactory>();
        var backtester = host.Services.GetRequiredService<IStrategyBacktester>();
        var catalog = host.Services.GetRequiredService<ISignalCatalog>();

        int tries = 0;
        var states = new Dictionary<string, (SnapshotFile Snap, WorkspaceState State)>();
        foreach (var (specId, files) in targets)
        {
            var spec = LabHost.FindBuiltInSpec(specId);
            if (spec == null) { Console.WriteLine($"  ! {specId}: not in the catalogue"); continue; }
            Console.WriteLine();
            Console.WriteLine($"══ {spec.Name}  [{spec.Id}]");
            foreach (var v in variants)
            {
                var root = (ConditionGroup)MapTree(spec.Conditions, v.Map)!;
                var mapped = spec with { Id = spec.Id + ".variant", Conditions = root };
                Console.WriteLine($"  ── {v.Name}");
                Console.WriteLine($"     leaves: {string.Join(", ", Leaves(root).Select(l => Describe(l, catalog)))}");
                foreach (var file in files)
                {
                    if (!states.TryGetValue(file, out var st))
                    {
                        var snap = SnapshotCommand.Load(Path.Combine(snapshotDir, file));
                        var state = await WorkspaceFactory.BuildAsync(host.Services, snap);
                        states[file] = st = (snap, state);
                    }
                    tries++;
                    var whole = await Run(mapped, backtester, factory, st.Snap, st.State, st.Snap.FirstDate, st.Snap.LastDate, warmup);
                    var wins = new List<(int Trades, double Er)>();
                    var bars = st.Snap.Bars;
                    for (int from = warmup; from + windowBars <= bars.Count; from += stepBars)
                        wins.Add(await Run(mapped, backtester, factory, st.Snap, st.State, bars[from].Date, bars[from + windowBars - 1].Date, warmup) is var r
                            ? (r.Trades, r.Er) : default);
                    var valid = wins.Where(w => w.Trades >= 5).ToList();
                    string rw = valid.Count == 0 ? "no window with >=5 trades"
                        : $"{valid.Count}/{wins.Count} windows valid, ER>0 in {valid.Count(w => w.Er > 0)} ({100.0 * valid.Count(w => w.Er > 0) / valid.Count:0}%), mean ER {valid.Average(w => w.Er):+0.000;-0.000}";
                    Console.WriteLine($"     {st.Snap.Symbol,-9} whole: {whole.Trades,3} trades, ER {whole.Er:+0.000;-0.000}R, CI [{whole.Lo:+0.00;-0.00}, {whole.Hi:+0.00;-0.00}]   rolling: {rw}");
                }
            }
        }
        Console.WriteLine();
        Console.WriteLine($"variants x assets tried: {tries} (count every one toward variantsTried)");
        return 0;
    }

    private static IEnumerable<StrategySpec> CatalogueProvenance_Specs() =>
        AccessibleTrader.StrategyLab.Catalogue.CatalogueProvenance.SpecsWithProvenance();

    private static string Describe(ConditionLeaf l, ISignalCatalog catalog)
    {
        string id = l.SignalDescriptorId + (l.SecondSignalDescriptorId != null ? $" {l.Operator} {l.SecondSignalDescriptorId}" : "");
        string? why = catalog.GetById(l.SignalDescriptorId) == null ? "DEAD" : null;
        if (why == null && l.SecondSignalDescriptorId != null && catalog.GetById(l.SecondSignalDescriptorId) == null) why = "DEAD second";
        return why == null ? id : $"{id} [{why}]";
    }

    private static IEnumerable<ConditionLeaf> Leaves(ConditionNode n) => n switch
    {
        ConditionLeaf l => new[] { l },
        ConditionGroup g => g.Children.SelectMany(Leaves),
        _ => Array.Empty<ConditionLeaf>(),
    };

    private static ConditionNode? MapTree(ConditionNode n, Func<ConditionLeaf, ConditionNode?> map) => n switch
    {
        ConditionLeaf l => map(l),
        ConditionGroup g => g with { Children = g.Children.Select(c => MapTree(c, map)).Where(c => c != null).Select(c => c!).ToList() },
        _ => n,
    };

    private static async Task<(int Trades, double Er, double Lo, double Hi)> Run(
        StrategySpec spec, IStrategyBacktester backtester, IConfigurableStrategyFactory factory,
        SnapshotFile snapshot, WorkspaceState state, DateTime start, DateTime end, int warmup)
    {
        // The battery's cost model (StrategyBatteryCommand.Run), so these numbers sit beside its cells.
        var config = new BacktestConfig(
            StartingCapital: 10000.0, CommissionRate: 0.001, SlippagePercent: 0.0005,
            WarmupBars: warmup, ReplayProfiles: false, StartDate: start, EndDate: end, AllowReverseOnSignal: false);
        var result = await backtester.RunAsync(factory.Create(spec), snapshot.Bars, config, state);
        var (lo, _, hi) = BootstrapCi.FromResult(result);
        return (result.Metrics.TotalSignals, double.IsNaN(result.Expectancy) ? 0 : result.Expectancy, lo, hi);
    }
}
