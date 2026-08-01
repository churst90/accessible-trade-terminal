using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Tests the published original published cipher setup against the snapshot in a small,
/// curated battery rather than a brute-force sweep. The test grid is designed to answer
/// three specific questions:
///
///   1. Does the published cipher 3-stage long setup (Anchor washed + Trigger > 0 + MFW NEGATIVE
///      + entry pulse) survive walk-forward with bootstrap CIs?
///
///   2. Does the contrarian Money Flow direction (NEGATIVE per the published setup) outperform the naive
///      "buy when MF is bullish" v1 reading (POSITIVE)?
///
///   3. Does the symmetric short setup (Anchor in OB + Trigger &lt; 0 + MFW POSITIVE +
///      bear pulse) work, or is the long-only edge an artifact of BTC's secular uptrend?
///
/// Each cell is reported with H1/H2 trade count, R-expectancy, 95% bootstrap CI lower
/// bound, and a SURVIVOR flag if both halves clear CI-lo &gt; 0 with at least 5 trades each
/// (relaxed from the sweep's 10-trade gate because these setups are intentionally rare).
///
/// All cells use Cipher B Money Flow Wave's true zero (-80 raw) and Cipher B Anchor Wave's
/// natural zero (0 raw). The published cipher long stages are encoded as published in
/// Catalogue/StrategyCatalogue.cs (formerly Core's BuiltInStrategySeeds).
/// </summary>
public static class StrategyBatteryCommand
{
    private const double MfBaseline = -80.0;  // Money Flow Wave plotted baseline (CipherBProvider line 531)

    public static async Task<int> RunAsync(string snapshotPath, int warmupBars)
    {
        if (!File.Exists(snapshotPath))
        {
            Console.Error.WriteLine($"Snapshot not found: {snapshotPath}");
            return 1;
        }

        var snapshot = SnapshotCommand.Load(snapshotPath);
        var midIdx = snapshot.Bars.Count / 2;
        var midDate = snapshot.Bars[midIdx].Date;
        Console.WriteLine($"Snapshot: {snapshot.Provider} {snapshot.Symbol} {snapshot.Timeframe} ({snapshot.BarCount} bars)");
        Console.WriteLine($"H1: {snapshot.FirstDate:yyyy-MM-dd} → {midDate:yyyy-MM-dd}");
        Console.WriteLine($"H2: {midDate:yyyy-MM-dd} → {snapshot.LastDate:yyyy-MM-dd}");
        Console.WriteLine();

        Console.WriteLine("Building host + computing indicators (one-time)...");
        var host = LabHost.Build();
        var state = await WorkspaceFactory.BuildAsync(host.Services, snapshot);
        var factory = host.Services.GetRequiredService<IConfigurableStrategyFactory>();
        var backtester = host.Services.GetRequiredService<IStrategyBacktester>();

        var cells = BuildCells();
        Console.WriteLine($"Battery: {cells.Count} curated setups");
        Console.WriteLine();

        var results = new List<(string Label, OrderSide Side, RunResult H1, RunResult H2)>();
        int idx = 0;
        foreach (var (label, side, root) in cells)
        {
            idx++;
            Console.Write($"  [{idx,2}/{cells.Count}] {(side == OrderSide.Buy ? "L" : "S")} {label,-58} ");
            var spec = MakeSpec($"cell.{idx}", label, root, side);
            var h1 = await Run(spec, backtester, factory, snapshot, state, snapshot.FirstDate, midDate, warmupBars);
            var h2 = await Run(spec, backtester, factory, snapshot, state, midDate, snapshot.LastDate, warmupBars);
            Console.WriteLine($"H1 tr={h1.Trades,3} R={h1.ExpectancyR,+6:0.000} CIlo={h1.CiLo,+6:0.00}  H2 tr={h2.Trades,3} R={h2.ExpectancyR,+6:0.000} CIlo={h2.CiLo,+6:0.00}");
            results.Add((label, side, h1, h2));
        }

        PrintSummary(results);
        return 0;
    }

    private static List<(string Label, OrderSide Side, ConditionGroup Root)> BuildCells()
    {
        // Helper builders. Cipher B and Cipher A share signal-id space via the catalog
        // (e.g. "CIPHER_B.Oversold Crossover", "CIPHER_A.Buy Signal").
        ConditionLeaf Fired(string id, string sig, int? withinBars = null) => new(
            Id: id, SignalDescriptorId: sig,
            Operator: withinBars.HasValue ? LeafOperator.FiredWithin : LeafOperator.Fired,
            WithinNBars: withinBars ?? 0, Score: 1.0);

        ConditionLeaf Lt(string id, string sig, double v) => new(
            Id: id, SignalDescriptorId: sig, Operator: LeafOperator.LessThan, Value: v, Score: 1.0);

        ConditionLeaf Gt(string id, string sig, double v) => new(
            Id: id, SignalDescriptorId: sig, Operator: LeafOperator.GreaterThan, Value: v, Score: 1.0);

        ConditionGroup Group(string id, LogicOperator logic, params ConditionNode[] children) =>
            new(Id: id, Logic: logic, Children: children.ToList());

        // The "any bull entry pulse" group used inside the cipher long setups: blue dot OR gold OR
        // Cipher A buy, all FiredWithin 5 bars (the published method uses a small look-back so the entry
        // doesn't have to be on the exact stage-3 bar).
        ConditionGroup BullEntryPulse(string idPrefix) => Group($"{idPrefix}-pulse", LogicOperator.Or,
            Fired($"{idPrefix}-blue", "CIPHER_B.Oversold Crossover", withinBars: 5),
            Fired($"{idPrefix}-gold", "CIPHER_B.Triple Confluence Buy", withinBars: 5),
            Fired($"{idPrefix}-abuy", "CIPHER_A.Buy Signal", withinBars: 5),
            Fired($"{idPrefix}-srsup", "CIPHER_SR.Support", withinBars: 5));

        // Symmetric bear entry pulse for the short side battery.
        ConditionGroup BearEntryPulse(string idPrefix) => Group($"{idPrefix}-pulse", LogicOperator.Or,
            Fired($"{idPrefix}-red", "CIPHER_B.Overbought Crossover", withinBars: 5),
            Fired($"{idPrefix}-asell", "CIPHER_A.Sell Signal", withinBars: 5),
            Fired($"{idPrefix}-aexh", "CIPHER_A.Exhaustion", withinBars: 5),
            Fired($"{idPrefix}-srres", "CIPHER_SR.Resistance", withinBars: 5));

        var cells = new List<(string, OrderSide, ConditionGroup)>();

        // === LONGS ===

        // 1. Bare bull entry pulse (no gates) — control for what the gates buy us.
        cells.Add(("BARE bull pulse (blue/gold/Abuy within 5)",
            OrderSide.Buy,
            BullEntryPulse("c1")));

        // 2. Anchor sign only (v12 thesis): Anchor < 0 AND bull pulse.
        cells.Add(("v12: Anchor Wave < 0 AND bull pulse",
            OrderSide.Buy,
            Group("c2", LogicOperator.And,
                Lt("c2-anc", "CIPHER_B.Anchor Wave", 0),
                BullEntryPulse("c2"))));

        // 3. Anchor washed (cipher stage 1): Anchor < -53 AND bull pulse.
        cells.Add(("Cipher S1: Anchor Wave < -53 AND bull pulse",
            OrderSide.Buy,
            Group("c3", LogicOperator.And,
                Lt("c3-anc", "CIPHER_B.Anchor Wave", -53),
                BullEntryPulse("c3"))));

        // 4. Stages 1+2: Anchor washed AND Trigger > 0 AND bull pulse.
        cells.Add(("Cipher S1+S2: Anchor < -53 AND Trigger > 0 AND pulse",
            OrderSide.Buy,
            Group("c4", LogicOperator.And,
                Lt("c4-anc", "CIPHER_B.Anchor Wave", -53),
                Gt("c4-trg", "CIPHER_B.Trigger Wave", 0),
                BullEntryPulse("c4"))));

        // 5. FULL cipher long (S1+S2+S3 contrarian MFW negative): the published setup.
        cells.Add(("Cipher FULL: Anc<-53 AND Trg>0 AND MFW<0(base) AND pulse",
            OrderSide.Buy,
            Group("c5", LogicOperator.And,
                Lt("c5-anc", "CIPHER_B.Anchor Wave", -53),
                Gt("c5-trg", "CIPHER_B.Trigger Wave", 0),
                Lt("c5-mfw", "CIPHER_B.Money Flow Wave", MfBaseline),  // raw < -80 = below visual zero
                BullEntryPulse("c5"))));

        // 6. v1 inverse (the "naive" reading the published method explicitly rejects): same gates but MFW POSITIVE.
        cells.Add(("v1 NAIVE: Anc<-53 AND Trg>0 AND MFW>0(base) AND pulse",
            OrderSide.Buy,
            Group("c6", LogicOperator.And,
                Lt("c6-anc", "CIPHER_B.Anchor Wave", -53),
                Gt("c6-trg", "CIPHER_B.Trigger Wave", 0),
                Gt("c6-mfw", "CIPHER_B.Money Flow Wave", MfBaseline),
                BullEntryPulse("c6"))));

        // 7. Just MFW sign filter, no anchor: bull pulse AND MFW < 0(base) (the contrarian-MF idea on its own).
        cells.Add(("MFW negative only: bull pulse AND MFW < 0(base)",
            OrderSide.Buy,
            Group("c7", LogicOperator.And,
                Lt("c7-mfw", "CIPHER_B.Money Flow Wave", MfBaseline),
                BullEntryPulse("c7"))));

        // 8. CIPHER_A.Manipulation alone (the encapsulated A-buy + MF>0 marker per memory).
        cells.Add(("CIPHER_A.Manipulation alone",
            OrderSide.Buy,
            Group("c8", LogicOperator.Or,
                Fired("c8-manip", "CIPHER_A.Manipulation"))));

        // 8a. Bull pulse + SMA(200) regime filter — the textbook Faber overlay.
        cells.Add(("BULL pulse + Close>SMA200 (Faber filter)",
            OrderSide.Buy,
            Group("c8a", LogicOperator.And,
                Gt("c8a-sma", "REGIME.AboveSma200", 0),
                BullEntryPulse("c8a"))));

        // 8b. Bull pulse + EMA(200) regime filter — same idea, faster MA.
        cells.Add(("BULL pulse + Close>EMA200 (faster filter)",
            OrderSide.Buy,
            Group("c8b", LogicOperator.And,
                Gt("c8b-ema", "REGIME.AboveEma200", 0),
                BullEntryPulse("c8b"))));

        // 8c. Cipher FULL + SMA200 — does the regime filter rescue the published stages?
        cells.Add(("Cipher FULL + Close>SMA200",
            OrderSide.Buy,
            Group("c8c", LogicOperator.And,
                Gt("c8c-sma", "REGIME.AboveSma200", 0),
                Lt("c8c-anc", "CIPHER_B.Anchor Wave", -53),
                Gt("c8c-trg", "CIPHER_B.Trigger Wave", 0),
                Lt("c8c-mfw", "CIPHER_B.Money Flow Wave", MfBaseline),
                BullEntryPulse("c8c"))));

        // 8d. KITCHEN SINK bull pulse — original 4 markers PLUS Cipher C bottoms PLUS Loukas
        // DCL/ICL Confirmed. Tests whether the orthogonal cycle indicators (which read
        // completely different math from Cipher A/B) add edge or just dilute it.
        cells.Add(("BULL pulse KITCHEN SINK (+CipherC +Loukas)",
            OrderSide.Buy,
            Group("c8d", LogicOperator.Or,
                Fired("c8d-blue", "CIPHER_B.Oversold Crossover", 5),
                Fired("c8d-gold", "CIPHER_B.Triple Confluence Buy", 5),
                Fired("c8d-abuy", "CIPHER_A.Buy Signal", 5),
                Fired("c8d-srsup", "CIPHER_SR.Support", 5),
                Fired("c8d-cbots", "CIPHER_C.Bottom Single", 5),
                Fired("c8d-cbotd", "CIPHER_C.Bottom Double", 5),
                Fired("c8d-cbott", "CIPHER_C.Bottom Triple", 5),
                Fired("c8d-ldcl",  "LOUKAS_CYCLES.DCL Confirmed", 5),
                Fired("c8d-licl",  "LOUKAS_CYCLES.ICL Confirmed", 5))));

        // 8e. Cipher C bottoms ONLY — orthogonal-cycle test in isolation.
        cells.Add(("CIPHER_C bottoms only (S/D/T within 5)",
            OrderSide.Buy,
            Group("c8e", LogicOperator.Or,
                Fired("c8e-s", "CIPHER_C.Bottom Single", 5),
                Fired("c8e-d", "CIPHER_C.Bottom Double", 5),
                Fired("c8e-t", "CIPHER_C.Bottom Triple", 5))));

        // 8f. Loukas DCL+ICL Confirmed only — Walter Bressert cycle theory in isolation.
        cells.Add(("LOUKAS DCL/ICL Confirmed only",
            OrderSide.Buy,
            Group("c8f", LogicOperator.Or,
                Fired("c8f-dcl", "LOUKAS_CYCLES.DCL Confirmed", 5),
                Fired("c8f-icl", "LOUKAS_CYCLES.ICL Confirmed", 5))));

        // 8g. Loukas ICL Confirmed only — the strongest cycle low.
        cells.Add(("LOUKAS ICL Confirmed only (strongest cycle low)",
            OrderSide.Buy,
            Group("c8g", LogicOperator.Or,
                Fired("c8g-icl", "LOUKAS_CYCLES.ICL Confirmed", 5))));

        // 8h. Bull pulse + funding Z negative (contrarian fade — buy when retail is bearish-positioned).
        // Literature: extreme negative funding = shorts paying longs = retail short-crowded = mean-revert long.
        cells.Add(("BULL pulse + Funding Z < -1.0 (contrarian)",
            OrderSide.Buy,
            Group("c8h", LogicOperator.And,
                Lt("c8h-fz", "BNVISION_FUNDING.FundingZScore", -1.0),
                BullEntryPulse("c8h"))));

        // 8i. Same idea, deeper extreme.
        cells.Add(("BULL pulse + Funding Z < -1.5 (deep contrarian)",
            OrderSide.Buy,
            Group("c8i", LogicOperator.And,
                Lt("c8i-fz", "BNVISION_FUNDING.FundingZScore", -1.5),
                BullEntryPulse("c8i"))));

        // 8j. The OPPOSITE — bull pulse when funding is hot (top 16%). Hypothesis: this should
        // UNDERPERFORM. If true, the difference between cells 8h/8i and 8j is the funding edge.
        cells.Add(("BULL pulse + Funding Z > +1.0 (chasing crowd, expected loss)",
            OrderSide.Buy,
            Group("c8j", LogicOperator.And,
                Gt("c8j-fz", "BNVISION_FUNDING.FundingZScore", 1.0),
                BullEntryPulse("c8j"))));

        // 8k. The whole bull pulse, but only WHEN funding is positive (longs paying — bull regime).
        // Hypothesis: this should match or modestly improve the bare pulse.
        cells.Add(("BULL pulse + Funding > 0 (bull regime)",
            OrderSide.Buy,
            Group("c8k", LogicOperator.And,
                Gt("c8k-f", "BNVISION_FUNDING.Funding", 0.0),
                BullEntryPulse("c8k"))));

        // 8l. Bull pulse + funding negative (the contrarian idea on the raw value, not z-score).
        cells.Add(("BULL pulse + Funding < 0 (raw contrarian)",
            OrderSide.Buy,
            Group("c8l", LogicOperator.And,
                Lt("c8l-f", "BNVISION_FUNDING.Funding", 0.0),
                BullEntryPulse("c8l"))));

        // 8m. Bull pulse + ΔOI > 0 (OI rising — leverage piling in, often top of pump).
        cells.Add(("BULL pulse + ΔOI > 0 (leverage building)",
            OrderSide.Buy,
            Group("c8m", LogicOperator.And,
                Gt("c8m-doi", "BNVISION_OI.OiDeltaPct", 0.0),
                BullEntryPulse("c8m"))));

        // 8n. Bull pulse + ΔOI < 0 (OI falling — capitulation/short-cover, often bottoms).
        cells.Add(("BULL pulse + ΔOI < 0 (deleveraging)",
            OrderSide.Buy,
            Group("c8n", LogicOperator.And,
                Lt("c8n-doi", "BNVISION_OI.OiDeltaPct", 0.0),
                BullEntryPulse("c8n"))));

        // 8o. Bull pulse + healthy bull regime (price/OI both up == align +1).
        cells.Add(("BULL pulse + Price/OI Align = +1 (healthy bull)",
            OrderSide.Buy,
            Group("c8o", LogicOperator.And,
                Gt("c8o-al", "BNVISION_OI.PriceOiAlign", 0.5),  // matches +1 and +2
                Lt("c8o-al2", "BNVISION_OI.PriceOiAlign", 1.5),  // excludes +2
                BullEntryPulse("c8o"))));

        // 8p. The combined champion: bull pulse + Funding > 0 + ΔOI > 0 (full bull regime).
        cells.Add(("BULL pulse + Funding>0 AND ΔOI>0 (full bull regime)",
            OrderSide.Buy,
            Group("c8p", LogicOperator.And,
                Gt("c8p-f", "BNVISION_FUNDING.Funding", 0.0),
                Gt("c8p-doi", "BNVISION_OI.OiDeltaPct", 0.0),
                BullEntryPulse("c8p"))));

        // 8q. The contrarian combined: pulse + Funding<0 + ΔOI<0 (capitulation).
        cells.Add(("BULL pulse + Funding<0 AND ΔOI<0 (capitulation)",
            OrderSide.Buy,
            Group("c8q", LogicOperator.And,
                Lt("c8q-f", "BNVISION_FUNDING.Funding", 0.0),
                Lt("c8q-doi", "BNVISION_OI.OiDeltaPct", 0.0),
                BullEntryPulse("c8q"))));

        // 8r. ΔOI z-score extreme (deleveraging flush) — alone, no funding.
        cells.Add(("BULL pulse + ΔOI Z < -1.5 (deleveraging flush)",
            OrderSide.Buy,
            Group("c8r", LogicOperator.And,
                Lt("c8r-z", "BNVISION_OI.OiDeltaZScore", -1.5),
                BullEntryPulse("c8r"))));

        // === PULSE INDICATOR (2026-04-09) ===
        // Tests the new PulseProvider end-to-end. Each cell uses real PULSE.* signal IDs that
        // appear in the catalog after registration. The first three are progressive isolation
        // (raw cross → cross + ADX → cross + Anchor) so we can attribute any edge to a layer.
        // The last is the pre-filtered GreenDot the indicator emits internally.

        // P1. Bare PULSE bull cross — control. Should match BARE bull pulse roughly in spirit
        //     (it's just an RSI midline cross instead of WT-cross), zero filters.
        cells.Add(("PULSE bare BullCross",
            OrderSide.Buy,
            Group("p1", LogicOperator.Or,
                Fired("p1-bx", "PULSE.BullCross", withinBars: 5))));

        // P2. PULSE bull cross + ADX > 20 (the chop filter on its own).
        cells.Add(("PULSE BullCross + ADX > 20",
            OrderSide.Buy,
            Group("p2", LogicOperator.And,
                Gt("p2-adx", "PULSE.Adx", 20.0),
                Fired("p2-bx", "PULSE.BullCross", withinBars: 5))));

        // P3. PULSE bull cross + Anchor (slow RSI) ≥ 40 (regime filter on its own).
        cells.Add(("PULSE BullCross + Anchor ≥ 40",
            OrderSide.Buy,
            Group("p3", LogicOperator.And,
                Gt("p3-anc", "PULSE.AnchorSlow", 40.0),
                Fired("p3-bx", "PULSE.BullCross", withinBars: 5))));

        // P4. PULSE bull cross + MFI > 50 (the volume filter on its own — the new ingredient).
        cells.Add(("PULSE BullCross + MFI > 50",
            OrderSide.Buy,
            Group("p4", LogicOperator.And,
                Gt("p4-mfi", "PULSE.Mfi", 50.0),
                Fired("p4-bx", "PULSE.BullCross", withinBars: 5))));

        // P5. PULSE BullCross + ADX>20 + Anchor≥40 + MFI>50 (manual all-filters version).
        cells.Add(("PULSE BullCross + ADX>20 + Anc≥40 + MFI>50",
            OrderSide.Buy,
            Group("p5", LogicOperator.And,
                Gt("p5-adx", "PULSE.Adx", 20.0),
                Gt("p5-anc", "PULSE.AnchorSlow", 40.0),
                Gt("p5-mfi", "PULSE.Mfi", 50.0),
                Fired("p5-bx", "PULSE.BullCross", withinBars: 5))));

        // P6. PULSE.GreenDot — the indicator's own pre-filtered marker. Should match P5
        //     numerically (same logic, computed once internally).
        cells.Add(("PULSE.GreenDot (pre-filtered)",
            OrderSide.Buy,
            Group("p6", LogicOperator.Or,
                Fired("p6-gd", "PULSE.GreenDot", withinBars: 5))));

        // P7. PULSE.GreenDot + Funding > 0 (compose with the validated BTC-daily filter).
        cells.Add(("PULSE.GreenDot + Funding > 0",
            OrderSide.Buy,
            Group("p7", LogicOperator.And,
                Gt("p7-f", "BNVISION_FUNDING.Funding", 0.0),
                Fired("p7-gd", "PULSE.GreenDot", withinBars: 5))));

        // P8. PULSE.GreenDot + ADX > 25 (stricter trend strength).
        cells.Add(("PULSE.GreenDot + ADX > 25 (stricter)",
            OrderSide.Buy,
            Group("p8", LogicOperator.And,
                Gt("p8-adx", "PULSE.Adx", 25.0),
                Fired("p8-gd", "PULSE.GreenDot", withinBars: 5))));

        // === PULSE V2 (2026-04-09 second pass) ===
        // Tests the new components: Regime classifier, slope-confirmed crosses, hold-down,
        // asymmetric short trigger (Fast crosses Anchor + declining anchor), pre-filtered
        // GreenDotV2 and RedDotV2.

        // V1. Regime gate alone, applied to v1 BullCross (isolates Regime's contribution).
        cells.Add(("V2.Regime+1 alone (BullCross + Regime==+1)",
            OrderSide.Buy,
            Group("v1c", LogicOperator.And,
                Gt("v1c-r", "PULSE.Regime", 0.5),
                Fired("v1c-bx", "PULSE.BullCross", withinBars: 5))));

        // V2. Slope-confirmed cross alone (no other gates) — isolates A1.
        cells.Add(("V2.BullCrossV2 alone",
            OrderSide.Buy,
            Group("v2c", LogicOperator.Or,
                Fired("v2c-bxv2", "PULSE.BullCrossV2", withinBars: 5))));

        // V3. BullCrossV2 + Regime+1 (combine A1 + B1).
        cells.Add(("V2.BullCrossV2 + Regime+1",
            OrderSide.Buy,
            Group("v3c", LogicOperator.And,
                Gt("v3c-r", "PULSE.Regime", 0.5),
                Fired("v3c-bxv2", "PULSE.BullCrossV2", withinBars: 5))));

        // V4. PULSE.GreenDotV2 — the indicator's own pre-filtered v2 long. Should be the
        // tightest filter combo: slope-confirmed cross + Anchor + MFI + ADX bull + Regime+1.
        cells.Add(("V2.GreenDotV2 (pre-filtered, all gates)",
            OrderSide.Buy,
            Group("v4c", LogicOperator.Or,
                Fired("v4c-gdv2", "PULSE.GreenDotV2", withinBars: 5))));

        // V5. GreenDotV2 + Funding > 0 (compose with the validated BTC daily filter).
        cells.Add(("V2.GreenDotV2 + Funding > 0",
            OrderSide.Buy,
            Group("v5c", LogicOperator.And,
                Gt("v5c-f", "BNVISION_FUNDING.Funding", 0.0),
                Fired("v5c-gdv2", "PULSE.GreenDotV2", withinBars: 5))));

        // V6. Fast-crosses-Anchor (alternative trigger) — bull side, no other filters.
        cells.Add(("V2.BullCrossAnchor alone",
            OrderSide.Buy,
            Group("v6c", LogicOperator.Or,
                Fired("v6c-bxa", "PULSE.BullCrossAnchor", withinBars: 5))));

        // V7. BullCrossAnchor + Regime+1.
        cells.Add(("V2.BullCrossAnchor + Regime+1",
            OrderSide.Buy,
            Group("v7c", LogicOperator.And,
                Gt("v7c-r", "PULSE.Regime", 0.5),
                Fired("v7c-bxa", "PULSE.BullCrossAnchor", withinBars: 5))));

        // === V2 SHORTS ===

        // V8. RedDotV2 — the asymmetric short trigger with all v2 short gates.
        cells.Add(("V2.RedDotV2 (asymmetric, all gates)",
            OrderSide.Sell,
            Group("v8c", LogicOperator.Or,
                Fired("v8c-rdv2", "PULSE.RedDotV2", withinBars: 5))));

        // V9. BearCrossAnchor alone — Fast crosses Anchor down, no other filters.
        cells.Add(("V2.BearCrossAnchor alone",
            OrderSide.Sell,
            Group("v9c", LogicOperator.Or,
                Fired("v9c-bxa", "PULSE.BearCrossAnchor", withinBars: 5))));

        // V10. BearCrossAnchor + Regime-1.
        cells.Add(("V2.BearCrossAnchor + Regime-1",
            OrderSide.Sell,
            Group("v10c", LogicOperator.And,
                Lt("v10c-r", "PULSE.Regime", -0.5),
                Fired("v10c-bxa", "PULSE.BearCrossAnchor", withinBars: 5))));

        // === PULSE V3 (2026-04-09 third pass — CMF + Vol regime + Golden dot) ===

        // V3a. CMF positive alone (filter on bare BullCross to isolate CMF contribution).
        cells.Add(("V3.BullCross + CMF > 0",
            OrderSide.Buy,
            Group("v3a", LogicOperator.And,
                Gt("v3a-c", "PULSE.Cmf", 0.0),
                Fired("v3a-bx", "PULSE.BullCross", withinBars: 5))));

        // V3b. Vol regime not climax alone.
        cells.Add(("V3.BullCross + VolPctile < 90",
            OrderSide.Buy,
            Group("v3b", LogicOperator.And,
                Lt("v3b-v", "PULSE.VolPctile", 90.0),
                Fired("v3b-bx", "PULSE.BullCross", withinBars: 5))));

        // V3c. CMF + Vol both, on bare BullCross (the orthogonal pair without trigger discipline).
        cells.Add(("V3.BullCross + CMF>0 + Vol<90",
            OrderSide.Buy,
            Group("v3c", LogicOperator.And,
                Gt("v3c-c", "PULSE.Cmf", 0.0),
                Lt("v3c-v", "PULSE.VolPctile", 90.0),
                Fired("v3c-bx", "PULSE.BullCross", withinBars: 5))));

        // V3d. PULSE.GoldenDot — the indicator's own pre-filtered v3 long entry.
        // Five orthogonal axes: BullCrossV2 + Regime+1 + ADX(bull lookback) + CMF>0 + Vol<90.
        cells.Add(("V3.GoldenDot (5-axis pre-filtered)",
            OrderSide.Buy,
            Group("v3d", LogicOperator.Or,
                Fired("v3d-gold", "PULSE.GoldenDot", withinBars: 5))));

        // V3e. GoldenDot + Funding > 0 — compose with the validated BTC-daily filter.
        cells.Add(("V3.GoldenDot + Funding > 0",
            OrderSide.Buy,
            Group("v3e", LogicOperator.And,
                Gt("v3e-f", "BNVISION_FUNDING.Funding", 0.0),
                Fired("v3e-gold", "PULSE.GoldenDot", withinBars: 5))));

        // V3f. GoldenDot + tighter vol gate (top quartile excluded).
        cells.Add(("V3.GreenDotV2 + Vol<75 (no GoldenDot)",
            OrderSide.Buy,
            Group("v3f", LogicOperator.And,
                Lt("v3f-v", "PULSE.VolPctile", 75.0),
                Fired("v3f-gd", "PULSE.GreenDotV2", withinBars: 5))));

        // V3g. GoldenDotShort — the indicator's own v3 short.
        cells.Add(("V3.GoldenDotShort (5-axis pre-filtered)",
            OrderSide.Sell,
            Group("v3g", LogicOperator.Or,
                Fired("v3g-gold", "PULSE.GoldenDotShort", withinBars: 5))));

        // V3h. CMF cross-asset test: bull pulse + CMF strong positive.
        cells.Add(("V3.BullCross + CMF > 0.10 (strong accum)",
            OrderSide.Buy,
            Group("v3h", LogicOperator.And,
                Gt("v3h-c", "PULSE.Cmf", 0.10),
                Fired("v3h-bx", "PULSE.BullCross", withinBars: 5))));

        // === PULSE V4 (2026-04-09 fourth pass — MTF Anchor/Regime + Funding×CMF quadrants) ===

        // ── A1: MTF Anchor and Regime ──────────────────────────────────────────
        // Tests whether sub-sampling daily bars to a synthetic "weekly" cadence and
        // running the regime/anchor calc on that smoother subseries beats the daily
        // versions. Hypothesis: weekly is less noisy → fewer false bull crosses get
        // gated through, higher per-trade expectancy on the survivors.

        // V4a. RegimeMtf alone (the cleanest test of the MTF idea).
        cells.Add(("V4.BullCross + RegimeMtf == +1",
            OrderSide.Buy,
            Group("v4a", LogicOperator.And,
                Gt("v4a-r", "PULSE.RegimeMtf", 0.5),
                Fired("v4a-bx", "PULSE.BullCross", withinBars: 5))));

        // V4b. BullCrossV2 + RegimeMtf+1 — tightest A1 test (the "GreenDotV2 with MTF gate").
        cells.Add(("V4.BullCrossV2 + RegimeMtf == +1",
            OrderSide.Buy,
            Group("v4b", LogicOperator.And,
                Gt("v4b-r", "PULSE.RegimeMtf", 0.5),
                Fired("v4b-bxv2", "PULSE.BullCrossV2", withinBars: 5))));

        // V4c. GreenDotV2 + RegimeMtf+1 — composing the v2 winner with the MTF gate.
        // Note: GreenDotV2 internally uses Regime (daily). This cell adds RegimeMtf as
        // an additional outer gate, so it requires BOTH daily AND weekly regime bullish.
        cells.Add(("V4.GreenDotV2 + RegimeMtf == +1",
            OrderSide.Buy,
            Group("v4c", LogicOperator.And,
                Gt("v4c-rmtf", "PULSE.RegimeMtf", 0.5),
                Fired("v4c-gdv2", "PULSE.GreenDotV2", withinBars: 5))));

        // V4d. AnchorMtf > 50 + BullCross — does the MTF anchor add value beyond regime?
        cells.Add(("V4.BullCross + AnchorMtf > 50",
            OrderSide.Buy,
            Group("v4d", LogicOperator.And,
                Gt("v4d-amtf", "PULSE.AnchorMtf", 50.0),
                Fired("v4d-bx", "PULSE.BullCross", withinBars: 5))));

        // V4e. Symmetric short: BearCross + RegimeMtf == -1.
        cells.Add(("V4.BearCross + RegimeMtf == -1",
            OrderSide.Sell,
            Group("v4e", LogicOperator.And,
                Lt("v4e-r", "PULSE.RegimeMtf", -0.5),
                Fired("v4e-bx", "PULSE.BearCross", withinBars: 5))));

        // ── A2: Funding × CMF orthogonal quadrants ─────────────────────────────
        // The four-quadrant matrix the user proposed. Funding axis (retail positioning
        // bias) × CMF axis (institutional volume direction). Each quadrant has a thesis;
        // the squeeze quadrant in particular is the one we've never tested.

        // V4f. Quadrant ++ : Funding>0 AND CMF>0 — both retail AND institutional bullish.
        // Hypothesis: strongest long. Should beat Funding>0 alone (the existing CI survivor).
        cells.Add(("V4.Quad++ : Funding>0 AND CMF>0 + bull pulse",
            OrderSide.Buy,
            Group("v4f", LogicOperator.And,
                Gt("v4f-f",  "BNVISION_FUNDING.Funding", 0.0),
                Gt("v4f-c",  "PULSE.Cmf", 0.0),
                BullEntryPulse("v4f"))));

        // V4g. Quadrant -+ : Funding<0 AND CMF>0 — retail short, real money buying.
        // The SQUEEZE setup. Academic literature supports this; we've never tested it.
        cells.Add(("V4.Quad-+ SQUEEZE: Funding<0 AND CMF>0 + bull pulse",
            OrderSide.Buy,
            Group("v4g", LogicOperator.And,
                Lt("v4g-f",  "BNVISION_FUNDING.Funding", 0.0),
                Gt("v4g-c",  "PULSE.Cmf", 0.0),
                BullEntryPulse("v4g"))));

        // V4h. Quadrant +- : Funding>0 AND CMF<0 — retail long, real money selling.
        // The DISTRIBUTION TOP. Best short setup according to the orthogonal-axes thesis.
        cells.Add(("V4.Quad+- DIST TOP: Funding>0 AND CMF<0 + bear pulse",
            OrderSide.Sell,
            Group("v4h", LogicOperator.And,
                Gt("v4h-f",  "BNVISION_FUNDING.Funding", 0.0),
                Lt("v4h-c",  "PULSE.Cmf", 0.0),
                BearEntryPulse("v4h"))));

        // V4i. Quadrant -- : Funding<0 AND CMF<0 — both bearish, confirmed downtrend.
        cells.Add(("V4.Quad-- : Funding<0 AND CMF<0 + bear pulse",
            OrderSide.Sell,
            Group("v4i", LogicOperator.And,
                Lt("v4i-f",  "BNVISION_FUNDING.Funding", 0.0),
                Lt("v4i-c",  "PULSE.Cmf", 0.0),
                BearEntryPulse("v4i"))));

        // V4j. PULSE.GreenDotV2 + Funding>0 AND CMF>0 (the v2 winner + the strongest quadrant).
        cells.Add(("V4.GreenDotV2 + Quad++ (Funding>0 AND CMF>0)",
            OrderSide.Buy,
            Group("v4j", LogicOperator.And,
                Gt("v4j-f",  "BNVISION_FUNDING.Funding", 0.0),
                Gt("v4j-c",  "PULSE.Cmf", 0.0),
                Fired("v4j-gdv2", "PULSE.GreenDotV2", withinBars: 5))));

        // === V5 — CFTC COT cross-asset positioning (2026-04-09 fifth pass) ===
        // First test of cross-asset positioning data on Pulse. CFTC publishes weekly
        // Tuesday positions for every futures contract from BTC to gold to oil to SPX.
        // For BTC daily we use the BITCOIN CME contract (Managed Money / Leveraged Funds
        // net positioning as % of OI).

        // V5a. CFTC.NetPctOi positive — specs net long. Trend-aligned.
        cells.Add(("V5.BullCross + CFTC.NetPctOi > 0 (specs long)",
            OrderSide.Buy,
            Group("v5a", LogicOperator.And,
                Gt("v5a-cot", "CFTC_COT.NetPctOi", 0.0),
                Fired("v5a-bx", "PULSE.BullCross", withinBars: 5))));

        // V5b. CFTC extreme negative — capitulation contrarian long.
        // The classic COT signal: when specs are at multi-month extreme short, contrarian long.
        cells.Add(("V5.BullCross + CFTC.NetZ < -1.5 (extreme spec capitulation)",
            OrderSide.Buy,
            Group("v5b", LogicOperator.And,
                Lt("v5b-cot", "CFTC_COT.NetZScore", -1.5),
                Fired("v5b-bx", "PULSE.BullCross", withinBars: 5))));

        // V5c. CFTC moderate negative — softer contrarian (more samples).
        cells.Add(("V5.BullCross + CFTC.NetZ < -1.0",
            OrderSide.Buy,
            Group("v5c", LogicOperator.And,
                Lt("v5c-cot", "CFTC_COT.NetZScore", -1.0),
                Fired("v5c-bx", "PULSE.BullCross", withinBars: 5))));

        // V5d. CFTC extreme positive — euphoria contrarian short.
        cells.Add(("V5.BearCross + CFTC.NetZ > +1.5 (extreme spec euphoria)",
            OrderSide.Sell,
            Group("v5d", LogicOperator.And,
                Gt("v5d-cot", "CFTC_COT.NetZScore", 1.5),
                Fired("v5d-bx", "PULSE.BearCross", withinBars: 5))));

        // V5e. The combined champion: GreenDotV2 + CFTC trend-aligned.
        cells.Add(("V5.GreenDotV2 + CFTC.NetPctOi > 0",
            OrderSide.Buy,
            Group("v5e", LogicOperator.And,
                Gt("v5e-cot", "CFTC_COT.NetPctOi", 0.0),
                Fired("v5e-gdv2", "PULSE.GreenDotV2", withinBars: 5))));

        // V5f. CFTC contrarian + GreenDotV2 — does the contrarian extreme add edge?
        cells.Add(("V5.GreenDotV2 + CFTC.NetZ < -1.0 (contrarian)",
            OrderSide.Buy,
            Group("v5f", LogicOperator.And,
                Lt("v5f-cot", "CFTC_COT.NetZScore", -1.0),
                Fired("v5f-gdv2", "PULSE.GreenDotV2", withinBars: 5))));

        // V5g. The two-positioning-source combo: Funding (crypto-native) + COT (TradFi).
        // Both bull = strongest possible cross-source confirmation we currently have.
        cells.Add(("V5.BullCross + Funding>0 AND CFTC.NetPctOi>0 (dual positioning)",
            OrderSide.Buy,
            Group("v5g", LogicOperator.And,
                Gt("v5g-f",   "BNVISION_FUNDING.Funding", 0.0),
                Gt("v5g-cot", "CFTC_COT.NetPctOi", 0.0),
                Fired("v5g-bx", "PULSE.BullCross", withinBars: 5))));

        // V5h. Capitulation combo: Funding negative AND COT extreme negative — both
        // sources screaming "specs maxed short," classic contrarian setup.
        cells.Add(("V5.BullCross + Funding<0 AND CFTC.NetZ<-1.0 (dual capitulation)",
            OrderSide.Buy,
            Group("v5h", LogicOperator.And,
                Lt("v5h-f",   "BNVISION_FUNDING.Funding", 0.0),
                Lt("v5h-cot", "CFTC_COT.NetZScore", -1.0),
                Fired("v5h-bx", "PULSE.BullCross", withinBars: 5))));

        // === V6 — CycleState 4-stage classifier (2026-04-09 sixth pass) ===
        // Tests the user's "Camel Finance cycle awareness" intuition. CycleState classifies
        // each bar into one of {1, 2, 3, 4} = {accumulation, markup, distribution, markdown}.
        // The goal is to apply DIFFERENT entry mechanics in different cycle stages — Pulse v2's
        // GreenDotV2 is implicitly a Stage 2 (markup / trend-follow) signal, and the H1/H2 fade
        // pattern partly comes from H1/H2 covering different stage mixes. Stage-aware entries
        // should reduce that asymmetry.

        // V6a. CycleState == 1 (accumulation) — bull cross at extreme oversold.
        // The classic capitulation reversal long.
        cells.Add(("V6.BullCross + CycleState == 1 (accumulation)",
            OrderSide.Buy,
            Group("v6a", LogicOperator.And,
                Gt("v6a-cs1", "PULSE.CycleState", 0.5),
                Lt("v6a-cs2", "PULSE.CycleState", 1.5),
                Fired("v6a-bx", "PULSE.BullCross", withinBars: 5))));

        // V6b. CycleState == 2 (markup) — bull cross in trending bull regime.
        // Should approximate GreenDotV2's success cases.
        cells.Add(("V6.BullCross + CycleState == 2 (markup)",
            OrderSide.Buy,
            Group("v6b", LogicOperator.And,
                Gt("v6b-cs1", "PULSE.CycleState", 1.5),
                Lt("v6b-cs2", "PULSE.CycleState", 2.5),
                Fired("v6b-bx", "PULSE.BullCross", withinBars: 5))));

        // V6c. CycleState == 3 (distribution) — bear cross at strength.
        // Stage-3 short setup — fade rallies into a topping market.
        cells.Add(("V6.BearCross + CycleState == 3 (distribution)",
            OrderSide.Sell,
            Group("v6c", LogicOperator.And,
                Gt("v6c-cs1", "PULSE.CycleState", 2.5),
                Lt("v6c-cs2", "PULSE.CycleState", 3.5),
                Fired("v6c-bx", "PULSE.BearCross", withinBars: 5))));

        // V6d. CycleState == 4 (markdown) — bear cross in confirmed downtrend.
        // Trend-following short. THE missing logic for the BTC 130k → 60k rollover.
        cells.Add(("V6.BearCross + CycleState == 4 (markdown)",
            OrderSide.Sell,
            Group("v6d", LogicOperator.And,
                Gt("v6d-cs1", "PULSE.CycleState", 3.5),
                Lt("v6d-cs2", "PULSE.CycleState", 4.5),
                Fired("v6d-bx", "PULSE.BearCross", withinBars: 5))));

        // V6e. Stage 1 + Cipher_C bottom (high-conviction reversal long).
        // Combines pure-price cycle classification with the orthogonal Cipher_C cycle marker.
        cells.Add(("V6.CycleState==1 + Cipher_C bottom (high-conviction)",
            OrderSide.Buy,
            Group("v6e", LogicOperator.And,
                Gt("v6e-cs1", "PULSE.CycleState", 0.5),
                Lt("v6e-cs2", "PULSE.CycleState", 1.5),
                Group("v6e-cb", LogicOperator.Or,
                    Fired("v6e-cbs", "CIPHER_C.Bottom Single", withinBars: 5),
                    Fired("v6e-cbd", "CIPHER_C.Bottom Double", withinBars: 5),
                    Fired("v6e-cbt", "CIPHER_C.Bottom Triple", withinBars: 5)))));

        // V6f. Stage 1 + Loukas DCL/ICL Confirmed (the user's cycle detection setup).
        cells.Add(("V6.CycleState==1 + Loukas DCL/ICL Confirmed",
            OrderSide.Buy,
            Group("v6f", LogicOperator.And,
                Gt("v6f-cs1", "PULSE.CycleState", 0.5),
                Lt("v6f-cs2", "PULSE.CycleState", 1.5),
                Group("v6f-l", LogicOperator.Or,
                    Fired("v6f-dcl", "LOUKAS_CYCLES.DCL Confirmed", withinBars: 5),
                    Fired("v6f-icl", "LOUKAS_CYCLES.ICL Confirmed", withinBars: 5)))));

        // V6g. Stage 1 + Cipher_C bottom + Funding < 0 (the deepest possible long capitulation).
        cells.Add(("V6.CycleState==1 + Cipher_C bot + Funding<0",
            OrderSide.Buy,
            Group("v6g", LogicOperator.And,
                Gt("v6g-cs1", "PULSE.CycleState", 0.5),
                Lt("v6g-cs2", "PULSE.CycleState", 1.5),
                Lt("v6g-f",   "BNVISION_FUNDING.Funding", 0.0),
                Group("v6g-cb", LogicOperator.Or,
                    Fired("v6g-cbs", "CIPHER_C.Bottom Single", withinBars: 5),
                    Fired("v6g-cbd", "CIPHER_C.Bottom Double", withinBars: 5),
                    Fired("v6g-cbt", "CIPHER_C.Bottom Triple", withinBars: 5)))));

        // V6h. Stage 4 + Cipher_C top (high-conviction trend-following short).
        cells.Add(("V6.CycleState==4 + Cipher_C top (high-conviction short)",
            OrderSide.Sell,
            Group("v6h", LogicOperator.And,
                Gt("v6h-cs1", "PULSE.CycleState", 3.5),
                Lt("v6h-cs2", "PULSE.CycleState", 4.5),
                Group("v6h-ct", LogicOperator.Or,
                    Fired("v6h-cts", "CIPHER_C.Top Single", withinBars: 5),
                    Fired("v6h-ctd", "CIPHER_C.Top Double", withinBars: 5),
                    Fired("v6h-ctt", "CIPHER_C.Top Triple", withinBars: 5)))));

        // V6i. CycleState ∈ {1, 2} (bull half of cycle) — bare BullCross.
        // Tests if simply gating bull entries to the "rising half" of the cycle helps.
        cells.Add(("V6.BullCross + CycleState ∈ {1,2} (bull half)",
            OrderSide.Buy,
            Group("v6i", LogicOperator.And,
                Lt("v6i-cs", "PULSE.CycleState", 2.5),
                Fired("v6i-bx", "PULSE.BullCross", withinBars: 5))));

        // V6j. GreenDotV2 + CycleState ∈ {1, 2} — does cycle gating add to v2's logic?
        cells.Add(("V6.GreenDotV2 + CycleState ∈ {1,2}",
            OrderSide.Buy,
            Group("v6j", LogicOperator.And,
                Lt("v6j-cs", "PULSE.CycleState", 2.5),
                Fired("v6j-gd", "PULSE.GreenDotV2", withinBars: 5))));

        // === SHORTS ===

        // 9. Bare bear pulse (control).
        cells.Add(("BARE bear pulse (red/Asell/exh within 5)",
            OrderSide.Sell,
            BearEntryPulse("c9")));

        // 10. Symmetric cipher short S1: Anchor > +53 AND bear pulse.
        cells.Add(("Cipher SHORT S1: Anchor Wave > +53 AND bear pulse",
            OrderSide.Sell,
            Group("c10", LogicOperator.And,
                Gt("c10-anc", "CIPHER_B.Anchor Wave", 53),
                BearEntryPulse("c10"))));

        // 11. Symmetric cipher short FULL: Anchor > +53 AND Trigger < 0 AND MFW > 0(base) AND bear pulse.
        cells.Add(("Cipher SHORT FULL: Anc>+53 AND Trg<0 AND MFW>0(base) AND pulse",
            OrderSide.Sell,
            Group("c11", LogicOperator.And,
                Gt("c11-anc", "CIPHER_B.Anchor Wave", 53),
                Lt("c11-trg", "CIPHER_B.Trigger Wave", 0),
                Gt("c11-mfw", "CIPHER_B.Money Flow Wave", MfBaseline),
                BearEntryPulse("c11"))));

        // 12. v12-symmetric for shorts: Anchor > 0 AND bear pulse.
        cells.Add(("v12-sym SHORT: Anchor Wave > 0 AND bear pulse",
            OrderSide.Sell,
            Group("c12", LogicOperator.And,
                Gt("c12-anc", "CIPHER_B.Anchor Wave", 0),
                BearEntryPulse("c12"))));

        // 13. SHORT pulse + Close < SMA200 — only short when below the regime filter.
        cells.Add(("SHORT pulse + Close<SMA200 (regime-gated)",
            OrderSide.Sell,
            Group("c13", LogicOperator.And,
                Lt("c13-sma", "REGIME.AboveSma200", 0),
                BearEntryPulse("c13"))));

        // === On-chain MVRV-gated cells (CoinMetrics, 2026-04-09 evening) ============
        // First non-OHLCV regime test on the new rolling-window harness. The hypothesis:
        // bull pulse cells should be conditional on MVRV regime — fire only when MVRV is
        // in early-cycle bands (1-2) where holders are accumulating, refuse to fire in
        // late-cycle distribution (regime 3-4). If true, the gated cell should beat the
        // bare cell on rolling-window stability AND have a smaller standard deviation.
        cells.Add(("BULL pulse + MVRV < 2 (early cycle only)",
            OrderSide.Buy,
            Group("c-mvrv-early", LogicOperator.And,
                Lt("c-mvrv-e", "COINMETRICS.MVRV", 2.0),
                BullEntryPulse("c-mvrv-early"))));

        cells.Add(("BULL pulse + MVRV < 1 (capitulation only)",
            OrderSide.Buy,
            Group("c-mvrv-cap", LogicOperator.And,
                Lt("c-mvrv-c", "COINMETRICS.MVRV", 1.0),
                BullEntryPulse("c-mvrv-cap"))));

        cells.Add(("BULL pulse + MVRV < 3 (anything but euphoria)",
            OrderSide.Buy,
            Group("c-mvrv-notop", LogicOperator.And,
                Lt("c-mvrv-n", "COINMETRICS.MVRV", 3.0),
                BullEntryPulse("c-mvrv-notop"))));

        cells.Add(("BEAR pulse + MVRV > 3 (distribution top-fade)",
            OrderSide.Sell,
            Group("c-mvrv-fade", LogicOperator.And,
                Gt("c-mvrv-f", "COINMETRICS.MVRV", 3.0),
                BearEntryPulse("c-mvrv-fade"))));

        cells.Add(("BEAR pulse + MVRV > 3.5 (extreme top-fade)",
            OrderSide.Sell,
            Group("c-mvrv-extreme", LogicOperator.And,
                Gt("c-mvrv-x", "COINMETRICS.MVRV", 3.5),
                BearEntryPulse("c-mvrv-extreme"))));

        // === v22 — TopBottomDetector reversal markers ===
        // Walk-windows survivors (2026-04-27 analysis): v22-LONG on BTC 4h
        // (4/6 windows positive, mean +0.22R, n=50 over 9 years) and
        // v22-SHORT on ETH 4h (5/6 windows positive across regime types,
        // mean +0.18R, n=105). Single-leaf cells — the marker is itself a
        // confluence of capitulation / distribution evidence streams, no
        // additional gate needed at the cell layer.

        cells.Add(("v22 LONG: TBD Bottom Confirmed (within 2)",
            OrderSide.Buy,
            Group("c-tbd-long", LogicOperator.Or,
                Fired("c-tbd-bot", "TOP_BOTTOM_DETECTOR.Bottom Confirmed", withinBars: 2))));

        cells.Add(("v22 SHORT: TBD Top Confirmed (within 2)",
            OrderSide.Sell,
            Group("c-tbd-short", LogicOperator.Or,
                Fired("c-tbd-top", "TOP_BOTTOM_DETECTOR.Top Confirmed", withinBars: 2))));

        // === v23 — Cipher B Weekly Reversal (oscillator-based, survives aggregation) ===
        // Walk-windows said v23r-LONG ETH 1d cleared the visual "this works" bar at
        // +0.534R / 4-of-6 / n=15. Rolling-window testing subjects the same setup to the strict
        // bootstrap-CI gate the suite uses to flag "deployable" cells. Bare v23 is
        // the same trigger without the Faber filter — useful for distinguishing
        // "trigger has edge" from "Faber gate is providing the edge."

        // Bull entry pulse for v23 — bare WT cross OR Blue dot OR Bull Divergence,
        // all FiredWithin 2 bars (matching the seed's WithinNBars semantics).
        ConditionGroup V23BullTrigger(string idPrefix) => Group($"{idPrefix}-trig", LogicOperator.Or,
            Fired($"{idPrefix}-wtx", "CIPHER_B.WaveTrend Cross Bull", withinBars: 2),
            Fired($"{idPrefix}-blue", "CIPHER_B.Oversold Crossover", withinBars: 2),
            Fired($"{idPrefix}-bdiv", "CIPHER_B.Bullish Divergence", withinBars: 2));

        ConditionGroup V23BearTrigger(string idPrefix) => Group($"{idPrefix}-trig", LogicOperator.Or,
            Fired($"{idPrefix}-wtx", "CIPHER_B.WaveTrend Cross Bear", withinBars: 2),
            Fired($"{idPrefix}-red", "CIPHER_B.Overbought Crossover", withinBars: 2),
            Fired($"{idPrefix}-sdiv", "CIPHER_B.Bearish Divergence", withinBars: 2));

        // v23 base — trigger + Anchor regime gate.
        cells.Add(("v23 LONG: trigger + Anchor<0",
            OrderSide.Buy,
            Group("c-v23l", LogicOperator.And,
                V23BullTrigger("c-v23l"),
                Lt("c-v23l-anc", "CIPHER_B.Anchor Wave", 0))));

        cells.Add(("v23 SHORT: trigger + Anchor>0",
            OrderSide.Sell,
            Group("c-v23s", LogicOperator.And,
                V23BearTrigger("c-v23s"),
                Gt("c-v23s-anc", "CIPHER_B.Anchor Wave", 0))));

        // v23r — same plus Faber regime gate.
        cells.Add(("v23r LONG: trigger + Anchor<0 + SMA200>0",
            OrderSide.Buy,
            Group("c-v23rl", LogicOperator.And,
                V23BullTrigger("c-v23rl"),
                Lt("c-v23rl-anc", "CIPHER_B.Anchor Wave", 0),
                Gt("c-v23rl-sma", "REGIME.AboveSma200", 0))));

        cells.Add(("v23r SHORT: trigger + Anchor>0 + SMA200<0",
            OrderSide.Sell,
            Group("c-v23rs", LogicOperator.And,
                V23BearTrigger("c-v23rs"),
                Gt("c-v23rs-anc", "CIPHER_B.Anchor Wave", 0),
                Lt("c-v23rs-sma", "REGIME.AboveSma200", 0))));

        // v23rf — bear trigger + Anchor>0 + SMA200<0 + funding>0 (crowded long contrarian).
        cells.Add(("v23rf SHORT: trigger + Anchor>0 + SMA200<0 + Fund>0",
            OrderSide.Sell,
            Group("c-v23rfs", LogicOperator.And,
                V23BearTrigger("c-v23rfs"),
                Gt("c-v23rfs-anc", "CIPHER_B.Anchor Wave", 0),
                Lt("c-v23rfs-sma", "REGIME.AboveSma200", 0),
                Gt("c-v23rfs-fund", "BNVISION_FUNDING.Funding", 0))));

        // v23rf2 — same as v23rf but using FundingZScore > +0.5 (relatively-positive
        // vs rolling 14-period mean, not raw positive). Hypothesis: in bear regime
        // funding is usually negative, so raw>0 almost never fires. But FundingZ>0
        // captures the brief micro-bounces where funding has been deeply negative
        // and is now recovering toward zero — exactly when shorts should fade rallies.
        cells.Add(("v23rf2 SHORT: trigger + Anchor>0 + SMA200<0 + FundZ>0.5",
            OrderSide.Sell,
            Group("c-v23rf2s", LogicOperator.And,
                V23BearTrigger("c-v23rf2s"),
                Gt("c-v23rf2s-anc", "CIPHER_B.Anchor Wave", 0),
                Lt("c-v23rf2s-sma", "REGIME.AboveSma200", 0),
                Gt("c-v23rf2s-fz",  "BNVISION_FUNDING.FundingZScore", 0.5))));

        // v23rf3 — looser FundingZ gate (>0).
        cells.Add(("v23rf3 SHORT: trigger + Anchor>0 + SMA200<0 + FundZ>0",
            OrderSide.Sell,
            Group("c-v23rf3s", LogicOperator.And,
                V23BearTrigger("c-v23rf3s"),
                Gt("c-v23rf3s-anc", "CIPHER_B.Anchor Wave", 0),
                Lt("c-v23rf3s-sma", "REGIME.AboveSma200", 0),
                Gt("c-v23rf3s-fz",  "BNVISION_FUNDING.FundingZScore", 0.0))));

        // === v23+ — Cipher confluence experiments (KAS/TAO investigation) ===
        // Tests whether adding orthogonal Cipher signals to v23 LONG actually lifts
        // edge or just dilutes it. the original cipher methodology's "Trilogy" thesis says A+B+SR all
        // firing together = highest-conviction setup. Cipher C is independent cycle
        // detection; should add signal regardless of TF since it's bar-relative.

        // v23+A: v23 LONG trigger + CIPHER_A.Buy Signal within 5 bars (Trilogy A piece).
        cells.Add(("v23+A LONG: trigger + Anchor<0 + CipherA.Buy(5)",
            OrderSide.Buy,
            Group("c-v23a", LogicOperator.And,
                V23BullTrigger("c-v23a"),
                Lt("c-v23a-anc", "CIPHER_B.Anchor Wave", 0),
                Fired("c-v23a-abuy", "CIPHER_A.Buy Signal", withinBars: 5))));

        // v23+SR: v23 LONG trigger + CIPHER_SR.Support within 5 bars (Trilogy SR piece).
        cells.Add(("v23+SR LONG: trigger + Anchor<0 + CipherSR.Support(5)",
            OrderSide.Buy,
            Group("c-v23sr", LogicOperator.And,
                V23BullTrigger("c-v23sr"),
                Lt("c-v23sr-anc", "CIPHER_B.Anchor Wave", 0),
                Fired("c-v23sr-srs", "CIPHER_SR.Support", withinBars: 5))));

        // v23+ASR: v23 LONG trigger + Cipher A Buy + Cipher SR Support (full Trilogy).
        cells.Add(("v23+ASR LONG: trigger + Anchor<0 + A.Buy + SR.Support",
            OrderSide.Buy,
            Group("c-v23asr", LogicOperator.And,
                V23BullTrigger("c-v23asr"),
                Lt("c-v23asr-anc", "CIPHER_B.Anchor Wave", 0),
                Fired("c-v23asr-abuy", "CIPHER_A.Buy Signal", withinBars: 5),
                Fired("c-v23asr-srs",  "CIPHER_SR.Support", withinBars: 5))));

        // v23+C: v23 LONG trigger + any CIPHER_C bottom (S/D/T) within 5 bars.
        cells.Add(("v23+C LONG: trigger + Anchor<0 + CipherC.Bottom(any,5)",
            OrderSide.Buy,
            Group("c-v23c", LogicOperator.And,
                V23BullTrigger("c-v23c"),
                Lt("c-v23c-anc", "CIPHER_B.Anchor Wave", 0),
                Group("c-v23c-cb", LogicOperator.Or,
                    Fired("c-v23c-cbs", "CIPHER_C.Bottom Single", withinBars: 5),
                    Fired("c-v23c-cbd", "CIPHER_C.Bottom Double", withinBars: 5),
                    Fired("c-v23c-cbt", "CIPHER_C.Bottom Triple", withinBars: 5)))));

        // v23+EMA200: same as v23r-Faber but using EMA200 (faster to react than SMA200).
        cells.Add(("v23+EMA200 LONG: trigger + Anchor<0 + EMA200>0",
            OrderSide.Buy,
            Group("c-v23ema", LogicOperator.And,
                V23BullTrigger("c-v23ema"),
                Lt("c-v23ema-anc", "CIPHER_B.Anchor Wave", 0),
                Gt("c-v23ema-ema", "REGIME.AboveEma200", 0))));

        // v23+ALL: trigger + Anchor + SMA200 + Cipher A Buy + Cipher SR Support + Cipher C bottom.
        // Maximal-confluence stress test — does stacking ALL the orthogonal signals beat
        // bare v23 or does it dilute via over-restriction?
        cells.Add(("v23+ALL LONG: trigger + Anchor + SMA200 + A.Buy + SR + C.Bot",
            OrderSide.Buy,
            Group("c-v23all", LogicOperator.And,
                V23BullTrigger("c-v23all"),
                Lt("c-v23all-anc", "CIPHER_B.Anchor Wave", 0),
                Gt("c-v23all-sma", "REGIME.AboveSma200", 0),
                Fired("c-v23all-abuy", "CIPHER_A.Buy Signal", withinBars: 5),
                Fired("c-v23all-srs",  "CIPHER_SR.Support", withinBars: 5),
                Group("c-v23all-cb", LogicOperator.Or,
                    Fired("c-v23all-cbs", "CIPHER_C.Bottom Single", withinBars: 5),
                    Fired("c-v23all-cbd", "CIPHER_C.Bottom Double", withinBars: 5),
                    Fired("c-v23all-cbt", "CIPHER_C.Bottom Triple", withinBars: 5)))));

        // === v23 + new universal-price-action indicators (2026-04-27 e7) ===

        // v23+AVWAP: v23 LONG trigger + close above the AVWAP-from-low (institutional
        // bull bias). The AVWAP from a swing low rises slowly; price holding above it
        // means every bar since the low has accumulated at lower prices on average →
        // bullish positioning. Bias > 0 means close above BOTH high-anchor and low-anchor.
        cells.Add(("v23+AVWAP LONG: trigger + Anchor<0 + AVWAP.Bias>0",
            OrderSide.Buy,
            Group("c-v23avwap", LogicOperator.And,
                V23BullTrigger("c-v23avwap"),
                Lt("c-v23avwap-anc", "CIPHER_B.Anchor Wave", 0),
                Gt("c-v23avwap-bias", "ANCHORED_VWAP.AVWAP Bias", 0.5))));

        // v23+HURST: v23 LONG trigger + Hurst < 0.45 (mean-reverting regime).
        // Reversal strategies should outperform in mean-reverting regimes; this
        // gate explicitly filters out trending regimes where reversals get run over.
        cells.Add(("v23+HURST LONG: trigger + Anchor<0 + Hurst<0.45",
            OrderSide.Buy,
            Group("c-v23hurst", LogicOperator.And,
                V23BullTrigger("c-v23hurst"),
                Lt("c-v23hurst-anc", "CIPHER_B.Anchor Wave", 0),
                Lt("c-v23hurst-h",   "HURST.Hurst", 0.45))));

        // v23+PIVOTS: v23 LONG trigger + price near a support pivot zone.
        // PivotZone = -1 when close within ATR-tolerance of S1/S2/S3/CamL3/CamL4.
        cells.Add(("v23+PIVOTS LONG: trigger + Anchor<0 + Zone=-1 (at support)",
            OrderSide.Buy,
            Group("c-v23pivot", LogicOperator.And,
                V23BullTrigger("c-v23pivot"),
                Lt("c-v23pivot-anc",  "CIPHER_B.Anchor Wave", 0),
                Lt("c-v23pivot-zone", "PIVOTS.Pivot Zone", -0.5))));

        // v23+HURST SHORT: bear trigger + mean-reverting regime.
        cells.Add(("v23+HURST SHORT: trigger + Anchor>0 + Hurst<0.45",
            OrderSide.Sell,
            Group("c-v23hurst-s", LogicOperator.And,
                V23BearTrigger("c-v23hurst-s"),
                Gt("c-v23hurst-s-anc", "CIPHER_B.Anchor Wave", 0),
                Lt("c-v23hurst-s-h",   "HURST.Hurst", 0.45))));

        // v23+AVWAP SHORT: bear trigger + AVWAP bias < 0 (close below both anchors).
        cells.Add(("v23+AVWAP SHORT: trigger + Anchor>0 + AVWAP.Bias<0",
            OrderSide.Sell,
            Group("c-v23avwap-s", LogicOperator.And,
                V23BearTrigger("c-v23avwap-s"),
                Gt("c-v23avwap-s-anc",  "CIPHER_B.Anchor Wave", 0),
                Lt("c-v23avwap-s-bias", "ANCHORED_VWAP.AVWAP Bias", -0.5))));

        // v23+PIVOTS SHORT: bear trigger + at-resistance.
        cells.Add(("v23+PIVOTS SHORT: trigger + Anchor>0 + Zone=+1 (at resistance)",
            OrderSide.Sell,
            Group("c-v23pivot-s", LogicOperator.And,
                V23BearTrigger("c-v23pivot-s"),
                Gt("c-v23pivot-s-anc",  "CIPHER_B.Anchor Wave", 0),
                Gt("c-v23pivot-s-zone", "PIVOTS.Pivot Zone", 0.5))));

        // === Round 6: AVWAP soft + BTC strength gates ===

        // v23+AVWAPS LONG: same as v23+AVWAP but using the SOFT bias (close above
        // either anchor counts). More fires, lower per-fire conviction — the
        // softer/stricter tradeoff is itself the question.
        cells.Add(("v23+AVWAPS LONG: trigger + Anchor<0 + AVWAP.BiasSoft>0",
            OrderSide.Buy,
            Group("c-v23avwaps", LogicOperator.And,
                V23BullTrigger("c-v23avwaps"),
                Lt("c-v23avwaps-anc", "CIPHER_B.Anchor Wave", 0),
                Gt("c-v23avwaps-bias", "ANCHORED_VWAP.AVWAP Bias Soft", 0.5))));

        cells.Add(("v23+AVWAPS SHORT: trigger + Anchor>0 + AVWAP.BiasSoft<0",
            OrderSide.Sell,
            Group("c-v23avwaps-s", LogicOperator.And,
                V23BearTrigger("c-v23avwaps-s"),
                Gt("c-v23avwaps-s-anc",  "CIPHER_B.Anchor Wave", 0),
                Lt("c-v23avwaps-s-bias", "ANCHORED_VWAP.AVWAP Bias Soft", -0.5))));

        // v23+BTCD LONG: altcoin bull cipher trigger + Anchor<0 + BtcRatioMomentum > 0
        // (asset has been outperforming BTC over the last 14 bars). Hypothesis:
        // altcoins that are gaining on BTC at the moment of the cipher reversal are
        // the ones with the cleanest setups. NaN-on-BTC will skip cleanly.
        cells.Add(("v23+BTCD LONG: trigger + Anchor<0 + BtcRatioMomentum>0",
            OrderSide.Buy,
            Group("c-v23btcd", LogicOperator.And,
                V23BullTrigger("c-v23btcd"),
                Lt("c-v23btcd-anc",  "CIPHER_B.Anchor Wave", 0),
                Gt("c-v23btcd-mom", "BTC_STRENGTH.BtcRatioMomentum", 0.0))));

        // v23+BTCD SHORT: bear trigger + BtcRatioMomentum < 0 (asset losing to BTC).
        cells.Add(("v23+BTCD SHORT: trigger + Anchor>0 + BtcRatioMomentum<0",
            OrderSide.Sell,
            Group("c-v23btcd-s", LogicOperator.And,
                V23BearTrigger("c-v23btcd-s"),
                Gt("c-v23btcd-s-anc", "CIPHER_B.Anchor Wave", 0),
                Lt("c-v23btcd-s-mom", "BTC_STRENGTH.BtcRatioMomentum", 0.0))));

        // === Round 7: inverted BTC-strength + new hypotheses ===

        // INV-BTCD LONG: bull cipher fire + Anchor<0 + BtcRatioMomentum < -0.05.
        // Contrarian thesis — altcoin LONG when asset is oversold vs BTC. KAS/TAO
        // local-bottoms typically coincide with worst-vs-BTC moments; this gate
        // catches that pattern instead of the previous (failed) pro-trend one.
        cells.Add(("INV-BTCD LONG: trigger + Anchor<0 + BtcRatioMomentum<-0.05",
            OrderSide.Buy,
            Group("c-invbtcd", LogicOperator.And,
                V23BullTrigger("c-invbtcd"),
                Lt("c-invbtcd-anc", "CIPHER_B.Anchor Wave", 0),
                Lt("c-invbtcd-mom", "BTC_STRENGTH.BtcRatioMomentum", -0.05))));

        // INV-BTCD softer: < -0.02 to broaden the sample.
        cells.Add(("INV-BTCD2 LONG: trigger + Anchor<0 + BtcRatioMomentum<-0.02",
            OrderSide.Buy,
            Group("c-invbtcd2", LogicOperator.And,
                V23BullTrigger("c-invbtcd2"),
                Lt("c-invbtcd2-anc", "CIPHER_B.Anchor Wave", 0),
                Lt("c-invbtcd2-mom", "BTC_STRENGTH.BtcRatioMomentum", -0.02))));

        // BTCD-WIDE LONG: gate is `BtcRatioMomentum > -999` — should always be true
        // when data is non-NaN. Diagnostic to confirm whether the BTC_STRENGTH series
        // is being read at all by the leaf evaluator. If this cell fires same count
        // as bare v23 (~368 on KAS 4h), the series is fine and the prior 0-trade
        // results are about the gate threshold; if 0, the leaf isn't reading it.
        cells.Add(("BTCD-WIDE LONG: trigger + Anchor<0 + BtcRatioMomentum>-999",
            OrderSide.Buy,
            Group("c-btcd-wide", LogicOperator.And,
                V23BullTrigger("c-btcd-wide"),
                Lt("c-btcd-wide-anc", "CIPHER_B.Anchor Wave", 0),
                Gt("c-btcd-wide-mom", "BTC_STRENGTH.BtcRatioMomentum", -999.0))));

        // INV-BTCD SHORT: bear cipher + asset over-extended vs BTC (gained too fast).
        cells.Add(("INV-BTCD SHORT: trigger + Anchor>0 + BtcRatioMomentum>+0.05",
            OrderSide.Sell,
            Group("c-invbtcd-s", LogicOperator.And,
                V23BearTrigger("c-invbtcd-s"),
                Gt("c-invbtcd-s-anc", "CIPHER_B.Anchor Wave", 0),
                Gt("c-invbtcd-s-mom", "BTC_STRENGTH.BtcRatioMomentum", 0.05))));

        // RANGE-EXP LONG: cipher trigger + Anchor<0 + bar's ATR > 1.5× recent median.
        // Hypothesis: capitulation candles have outsized range. Gate on the bar
        // being a volatility expansion event filters out range-bound nothing-burgers.
        // Uses Cipher B's own WT range as a proxy (no direct ATR signal in catalog).
        // Skip — leaves this as documented future work; needs an ATR signal exposed.

        // MA-STACK LONG: cipher trigger + Anchor<0 + SMA200 + price above EMA200
        // (compound trend confirmation). The SMA + EMA being aligned is a stronger
        // bull regime signal than either alone.
        cells.Add(("MA-STACK LONG: trigger + Anchor<0 + SMA200>0 + EMA200>0",
            OrderSide.Buy,
            Group("c-mastack", LogicOperator.And,
                V23BullTrigger("c-mastack"),
                Lt("c-mastack-anc", "CIPHER_B.Anchor Wave", 0),
                Gt("c-mastack-sma", "REGIME.AboveSma200", 0),
                Gt("c-mastack-ema", "REGIME.AboveEma200", 0))));

        // CONFLUENCE LONG: cipher trigger + Anchor + Pivots support + Hurst mean-revert.
        // Stack the two best individual gates from round 4. Risk: over-restriction.
        cells.Add(("CONFLUENCE LONG: trigger + Anchor<0 + Pivots support + Hurst<0.45",
            OrderSide.Buy,
            Group("c-conf", LogicOperator.And,
                V23BullTrigger("c-conf"),
                Lt("c-conf-anc",  "CIPHER_B.Anchor Wave", 0),
                Lt("c-conf-piv",  "PIVOTS.Pivot Zone", -0.5),
                Lt("c-conf-h",    "HURST.Hurst", 0.45))));

        // OR-CONF LONG: cipher trigger + Anchor + (AVWAPS bull OR Pivots support).
        // The OR variant of CONFLUENCE — broaden the gate by accepting EITHER
        // institutional reference level. Tests if the union beats either alone.
        cells.Add(("OR-CONF LONG: trigger + Anchor<0 + (AVWAPS>0.5 OR Pivots<-0.5)",
            OrderSide.Buy,
            Group("c-orconf", LogicOperator.And,
                V23BullTrigger("c-orconf"),
                Lt("c-orconf-anc", "CIPHER_B.Anchor Wave", 0),
                Group("c-orconf-or", LogicOperator.Or,
                    Gt("c-orconf-avwap", "ANCHORED_VWAP.AVWAP Bias Soft", 0.5),
                    Lt("c-orconf-piv",   "PIVOTS.Pivot Zone", -0.5)))));

        // ── ETH 4h SHORT investigation cells (round 9, 2026-04-27) ────────────────
        // v23r SHORT works on ETH 1d (100%/2) and BTC 4h (81%/16/+0.459R) but fails
        // on ETH 4h (47%/70/-0.009R). Hypothesis: ETH intraday bear rallies are more
        // persistent than BTC's, so the bare bear-cipher trigger fires too early.
        // These cells layer additional confluence on top of v23r SHORT to test
        // whether a confirmation signal lifts ETH 4h into deployable territory.

        // v23r-ASELL SHORT: + Cipher A.Sell within 5 bars (multi-indicator agreement).
        cells.Add(("v23r-ASELL SHORT: v23r + Cipher A.Sell within 5",
            OrderSide.Sell,
            Group("c-v23rsasell", LogicOperator.And,
                V23BearTrigger("c-v23rsasell"),
                Gt("c-v23rsasell-anc", "CIPHER_B.Anchor Wave", 0),
                Lt("c-v23rsasell-sma", "REGIME.AboveSma200", 0),
                Fired("c-v23rsasell-asell", "CIPHER_A.Sell Signal", withinBars: 5))));

        // v23r-AEXH SHORT: + Cipher A.Exhaustion within 5 bars (rare exhaustion confluence).
        cells.Add(("v23r-AEXH SHORT: v23r + Cipher A.Exhaustion within 5",
            OrderSide.Sell,
            Group("c-v23rsaexh", LogicOperator.And,
                V23BearTrigger("c-v23rsaexh"),
                Gt("c-v23rsaexh-anc", "CIPHER_B.Anchor Wave", 0),
                Lt("c-v23rsaexh-sma", "REGIME.AboveSma200", 0),
                Fired("c-v23rsaexh-aexh", "CIPHER_A.Exhaustion", withinBars: 5))));

        // v23r-SRRES SHORT: + Cipher SR.Resistance within 5 bars (resistance-tagged short).
        cells.Add(("v23r-SRRES SHORT: v23r + Cipher SR.Resistance within 5",
            OrderSide.Sell,
            Group("c-v23rssrr", LogicOperator.And,
                V23BearTrigger("c-v23rssrr"),
                Gt("c-v23rssrr-anc", "CIPHER_B.Anchor Wave", 0),
                Lt("c-v23rssrr-sma", "REGIME.AboveSma200", 0),
                Fired("c-v23rssrr-sr", "CIPHER_SR.Resistance", withinBars: 5))));

        // ── v24 — VOL_REGIME gate cells (round 10, 2026-06-12) ────────────────────
        // Era-robustness hypothesis: the v2-v23 universal decay pattern (every cell
        // worse in recent windows) is partly CALIBRATION decay, not signal death —
        // reversal entries still work when volatility is elevated relative to the
        // asset's own era baseline, and the dead trades are the low-relative-vol
        // ones that barely existed in early-BTC conditions. VOL_REGIME.VolRatio
        // (30/365-bar realized-vol ratio) is stationary across eras by construction.
        // Combo evidence (BTC 1d halves): WT Cross Bull + Ratio>1.0 left H1 alone
        // and lifted H2 +0.046→+0.241 R/tr. ETH halves did not replicate at 0.9 —
        // rolling windows are the referee.
        //
        // ROUND-10 VERDICT (same day): FALSIFIED as a hard gate. BTC 1d: v24
        // (ratio>0.9) doubled per-trade R (+0.248→+0.445) but cut n 27→6/window,
        // zero CI-pass; the falsification cell (compression) was mediocre as
        // predicted. ETH 1d: the FALSIFICATION cell won (100% pos, +0.543R,
        // HIGH-CONV) while elevated-vol did nothing — the exact mirror of BTC.
        // Opposite-sign "best gates" per asset on 6-15 windows = fitted noise,
        // not signal. v23 base stayed the top cell on BOTH assets. Conclusion
        // matches the suite's standing lesson: filter restraint beats stacked
        // confluence; a binary vol gate destroys more (trade count → CI power)
        // than its conditioning gains. Cells retained as the documented negative
        // result. Next angle: era-adaptation INSIDE the trigger (Cipher B
        // ThresholdMode=Percentile) which moves entries instead of discarding
        // them, and vol-target SIZING which needs no gate at all.

        // v24 LONG — the headline candidate: v23 trigger + Anchor gate + elevated vol.
        cells.Add(("v24 LONG: trigger + Anchor<0 + VolRatio>0.9",
            OrderSide.Buy,
            Group("c-v24l", LogicOperator.And,
                V23BullTrigger("c-v24l"),
                Lt("c-v24l-anc", "CIPHER_B.Anchor Wave", 0),
                Gt("c-v24l-vr", "VOL_REGIME.VolRatio", 0.9))));

        // Stricter variant — washout-quality entries only.
        cells.Add(("v24s LONG: trigger + Anchor<0 + VolRatio>1.1",
            OrderSide.Buy,
            Group("c-v24sl", LogicOperator.And,
                V23BullTrigger("c-v24sl"),
                Lt("c-v24sl-anc", "CIPHER_B.Anchor Wave", 0),
                Gt("c-v24sl-vr", "VOL_REGIME.VolRatio", 1.1))));

        // Vol gate WITHOUT the Anchor gate — isolates whether the vol gate carries
        // edge on its own or only composes with the oscillator regime.
        cells.Add(("v24x LONG: trigger + VolRatio>0.9 (no Anchor)",
            OrderSide.Buy,
            Group("c-v24xl", LogicOperator.And,
                V23BullTrigger("c-v24xl"),
                Gt("c-v24xl-vr", "VOL_REGIME.VolRatio", 0.9))));

        // Percentile-rank form — even more era-stationary than the ratio (pure rank
        // within the trailing distribution; no threshold units at all).
        cells.Add(("v24p LONG: trigger + Anchor<0 + VolPctile>0.5",
            OrderSide.Buy,
            Group("c-v24pl", LogicOperator.And,
                V23BullTrigger("c-v24pl"),
                Lt("c-v24pl-anc", "CIPHER_B.Anchor Wave", 0),
                Gt("c-v24pl-pct", "VOL_REGIME.VolPercentile", 0.5))));

        // Short-side mirror — shorts have never survived without the bear+funding
        // gate, so this is exploratory rather than expected to pass.
        cells.Add(("v24 SHORT: trigger + Anchor>0 + VolRatio>1.0",
            OrderSide.Sell,
            Group("c-v24sh", LogicOperator.And,
                V23BearTrigger("c-v24sh"),
                Gt("c-v24sh-anc", "CIPHER_B.Anchor Wave", 0),
                Gt("c-v24sh-vr", "VOL_REGIME.VolRatio", 1.0))));

        // FALSIFICATION cell — compression-gated reversals. The combo run said this
        // destroys the edge (H1 +0.450→-0.449 on the blue dot). If rolling-window
        // disagrees and this cell ranks well, the elevated-vol interpretation above
        // is wrong and the v24 family should not be promoted.
        cells.Add(("v24c LONG (falsif.): trigger + Anchor<0 + VolState<0",
            OrderSide.Buy,
            Group("c-v24cl", LogicOperator.And,
                V23BullTrigger("c-v24cl"),
                Lt("c-v24cl-anc", "CIPHER_B.Anchor Wave", 0),
                Lt("c-v24cl-vs", "VOL_REGIME.VolState", 0))));

        // ── v25 — percentile-mode threshold isolation cells (round 11, 2026-06-12) ──
        // Round 10 vol-GATE was falsified (n-starvation). This is the inside-the-
        // trigger angle: run the SAME cell against a percentile-mode Cipher B via
        // `--set CIPHER_B.ThresholdMode=Percentile`, which replaces the fixed ±53
        // OB/OS levels with the rolling 5th/95th percentile of WaveTrend's OWN recent
        // distribution (era-adaptive by construction). It MOVES entry levels instead
        // of discarding trades, so the trade count survives.
        //
        // CRITICAL: the v23/v24 cells are threshold-INSENSITIVE because their trigger
        // OR's in the raw "WaveTrend Cross Bull" leg, which already captures every
        // blue-dot bar (a blue dot is just a WT cross that lands in OS). To actually
        // see the percentile effect the trigger must ISOLATE the threshold-dependent
        // signal — blue dot (Oversold Crossover) alone, divergence alone. These
        // cells do that. Run each twice: once bare (fixed mode) and once with
        // --set CIPHER_B.ThresholdMode=Percentile; the delta is the era-adaptation.

        // Blue dot ALONE + Anchor regime gate. The pure oversold-crossover entry.
        cells.Add(("v25 BLUE-ONLY LONG: OversoldCross within 2 + Anchor<0",
            OrderSide.Buy,
            Group("c-v25bl", LogicOperator.And,
                Fired("c-v25bl-blue", "CIPHER_B.Oversold Crossover", withinBars: 2),
                Lt("c-v25bl-anc", "CIPHER_B.Anchor Wave", 0))));

        // Blue dot ALONE, no anchor gate — maximum sensitivity to the threshold.
        cells.Add(("v25 BLUE-BARE LONG: OversoldCross within 2 (no gate)",
            OrderSide.Buy,
            Group("c-v25bbl", LogicOperator.Or,
                Fired("c-v25bbl-blue", "CIPHER_B.Oversold Crossover", withinBars: 2))));

        // Bullish divergence ALONE + Anchor gate. Divergence detection keys off the
        // adaptive OS band too (the "near OS" gate at line ~982 of the provider).
        cells.Add(("v25 DIV-ONLY LONG: BullDiv within 2 + Anchor<0",
            OrderSide.Buy,
            Group("c-v25dl", LogicOperator.And,
                Fired("c-v25dl-div", "CIPHER_B.Bullish Divergence", withinBars: 2),
                Lt("c-v25dl-anc", "CIPHER_B.Anchor Wave", 0))));

        // Short-side blue-dot mirror (overbought crossover alone + anchor>0).
        cells.Add(("v25 RED-ONLY SHORT: OverboughtCross within 2 + Anchor>0",
            OrderSide.Sell,
            Group("c-v25rs", LogicOperator.And,
                Fired("c-v25rs-red", "CIPHER_B.Overbought Crossover", withinBars: 2),
                Gt("c-v25rs-anc", "CIPHER_B.Anchor Wave", 0))));

        // ── v26 — Bullish-Divergence focus (round 11 follow-up) ───────────────────
        // The percentile experiment's real payoff: isolating signals surfaced
        // Bullish Divergence + Anchor<0 as the strongest cross-asset cell in the
        // suite (FIXED mode) — BTC +0.794R/100%/HIGH-CONV (9 win), ETH +0.480R/83%,
        // LTC +0.853R/100%/HIGH-CONV (3 win). Its only weakness is rarity: ~6
        // trades/window, fires 0 trades on XRP/BCH.
        //
        // ROUND-11 VERDICT (width-5 follow-up):
        //  • The anchor gate is REDUNDANT — on BTC DIV-W5 and DIV-BARE-W5 are
        //    byte-identical, because every bullish divergence already forms in
        //    oversold (Anchor<0). The divergence signal alone IS the edge.
        //  • Widening within-2 → within-5 does NOT lift trade count (divergence is
        //    just rare: ~5.6/window either way) and DEGRADES quality (BTC
        //    +0.794→+0.636, ETH +0.480→collapsed to 50% marginal). within-2 timing
        //    matters; keep it tight.
        //  • DIV-FABER = 0 trades — divergences never coincide with price>SMA200
        //    (they form at bottoms, below the 200MA). Structurally a bottom-fisher,
        //    incompatible with trend filters.
        //  DISPOSITION: a genuine high-quality, irreducibly LOW-FREQUENCY signal.
        //  Promote as a peak-conviction overlay for liquid majors (BTC/ETH/LTC),
        //  cleanest form = "Bullish Divergence within 2" alone (no gate needed).
        //  NOT a standalone strategy — too few trades to stand on its own CI.
        //  Same category as v23p pivots: conviction over coverage.

        cells.Add(("v26 DIV-W5 LONG: BullDiv within 5 + Anchor<0",
            OrderSide.Buy,
            Group("c-v26d5", LogicOperator.And,
                Fired("c-v26d5-div", "CIPHER_B.Bullish Divergence", withinBars: 5),
                Lt("c-v26d5-anc", "CIPHER_B.Anchor Wave", 0))));

        // Divergence with NO anchor gate at width 5 — isolates the divergence
        // signal's own standalone edge at a usable trade count.
        cells.Add(("v26 DIV-BARE-W5 LONG: BullDiv within 5 (no gate)",
            OrderSide.Buy,
            Group("c-v26d5b", LogicOperator.Or,
                Fired("c-v26d5b-div", "CIPHER_B.Bullish Divergence", withinBars: 5))));

        // Divergence + Faber regime (the most empirically robust gate in the suite)
        // instead of the Anchor oscillator gate — tests whether the trend filter
        // composes better than the oscillator-state filter on the divergence entry.
        cells.Add(("v26 DIV-FABER LONG: BullDiv within 5 + SMA200>0",
            OrderSide.Buy,
            Group("c-v26df", LogicOperator.And,
                Fired("c-v26df-div", "CIPHER_B.Bullish Divergence", withinBars: 5),
                Gt("c-v26df-sma", "REGIME.AboveSma200", 0))));

        // ── v27 — CONFLUENCE LADDER (round 13, 2026-06-12, post look-ahead fix) ───
        // Tests the user's core buy thesis directly and measures the MARGINAL value of
        // each confluence layer on signal quality (per-trade R) vs frequency:
        //   "buy when a cycle indicator is at the bottom AND a Cipher B buy fires AND
        //    we're at Cipher SR support."
        // Plus the open question: does SENTIMENT (Fear&Greed, funding) add anything?
        // All run with honest divergence (DivergenceConfirmLag default ON). The v23
        // base cell above is the control; these layer onto the same trigger + Anchor.
        ConditionGroup CipherCBottom(string id) => Group(id, LogicOperator.Or,
            Fired($"{id}-s", "CIPHER_C.Bottom Single", withinBars: 5),
            Fired($"{id}-d", "CIPHER_C.Bottom Double", withinBars: 5),
            Fired($"{id}-t", "CIPHER_C.Bottom Triple", withinBars: 5));

        // v27 +SR — price-structure layer alone (Cipher B buy at support).
        cells.Add(("v27 +SR LONG: v23 + SR.Support within 5",
            OrderSide.Buy,
            Group("c-v27sr", LogicOperator.And,
                V23BullTrigger("c-v27sr"),
                Lt("c-v27sr-anc", "CIPHER_B.Anchor Wave", 0),
                Fired("c-v27sr-sup", "CIPHER_SR.Support", withinBars: 5))));

        // v27 FULL — THE THESIS: Cipher B buy + cycle bottom + at support.
        cells.Add(("v27 FULL LONG: v23 + CipherC.Bottom + SR.Support",
            OrderSide.Buy,
            Group("c-v27full", LogicOperator.And,
                V23BullTrigger("c-v27full"),
                Lt("c-v27full-anc", "CIPHER_B.Anchor Wave", 0),
                CipherCBottom("c-v27full-cb"),
                Fired("c-v27full-sup", "CIPHER_SR.Support", withinBars: 7))));

        // Sentiment layers (the open question).
        cells.Add(("v27 +FEAR LONG: v23 + FearGreed.Sentiment<30",
            OrderSide.Buy,
            Group("c-v27fg", LogicOperator.And,
                V23BullTrigger("c-v27fg"),
                Lt("c-v27fg-anc", "CIPHER_B.Anchor Wave", 0),
                Lt("c-v27fg-s", "FEAR_GREED.Sentiment", 30.0))));

        cells.Add(("v27 +NEGFUND LONG: v23 + Funding<0",
            OrderSide.Buy,
            Group("c-v27nf", LogicOperator.And,
                V23BullTrigger("c-v27nf"),
                Lt("c-v27nf-anc", "CIPHER_B.Anchor Wave", 0),
                Lt("c-v27nf-f", "BNVISION_FUNDING.Funding", 0.0))));

        // v27 FULL+FEAR — does sentiment ADD to the price confluence, or just thin it?
        cells.Add(("v27 FULL+FEAR LONG: v23 + CipherC + SR + Sentiment<40",
            OrderSide.Buy,
            Group("c-v27ff", LogicOperator.And,
                V23BullTrigger("c-v27ff"),
                Lt("c-v27ff-anc", "CIPHER_B.Anchor Wave", 0),
                CipherCBottom("c-v27ff-cb"),
                Fired("c-v27ff-sup", "CIPHER_SR.Support", withinBars: 7),
                Lt("c-v27ff-s", "FEAR_GREED.Sentiment", 40.0))));

        return cells;
    }

    private static void PrintSummary(List<(string Label, OrderSide Side, RunResult H1, RunResult H2)> results)
    {
        Console.WriteLine();
        Console.WriteLine("══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine("STRATEGY GATE BATTERY — published Market Cipher long + symmetric short setups, no-reverse, bootstrap CIs");
        Console.WriteLine("══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine($"{"Setup",-62} {"Sd",2} {"H1 tr",6} {"H1 R",8} {"H1 CIlo",8} {"H2 tr",6} {"H2 R",8} {"H2 CIlo",8} flags");
        Console.WriteLine(new string('─', 122));
        var ranked = results
            .OrderByDescending(r => Math.Min(SafeNum(r.H1.CiLo), SafeNum(r.H2.CiLo)))
            .ToList();
        foreach (var (label, side, h1, h2) in ranked)
        {
            bool ciOk = h1.CiLo > 0 && h2.CiLo > 0 && h1.Trades >= 5 && h2.Trades >= 5;
            bool ptPos = h1.ExpectancyR > 0 && h2.ExpectancyR > 0 && h1.Trades >= 5 && h2.Trades >= 5;
            string flag = ciOk ? "★ CI-SURVIVOR" : (ptPos ? "pt-positive" : "");
            Console.WriteLine(
                $"{Trim(label, 62),-62} {(side == OrderSide.Buy ? "L" : "S"),2} " +
                $"{h1.Trades,6} {h1.ExpectancyR,8:+0.000;-0.000; 0.000} {h1.CiLo,8:+0.00;-0.00; 0.00} " +
                $"{h2.Trades,6} {h2.ExpectancyR,8:+0.000;-0.000; 0.000} {h2.CiLo,8:+0.00;-0.00; 0.00} {flag}");
        }

        Console.WriteLine();
        var survivors = ranked.Where(r => r.H1.CiLo > 0 && r.H2.CiLo > 0 && r.H1.Trades >= 5 && r.H2.Trades >= 5).ToList();
        if (survivors.Count == 0)
        {
            Console.WriteLine("VERDICT: No battery cell has CI lower bound > 0 in BOTH halves on this snapshot.");
        }
        else
        {
            Console.WriteLine($"VERDICT: {survivors.Count} setup(s) survive (CI lo > 0 both halves, ≥5 trades each):");
            foreach (var s in survivors)
                Console.WriteLine($"  ★ {(s.Side == OrderSide.Buy ? "LONG" : "SHORT")}: {s.Label}");
        }
    }

    private static double SafeNum(double d) => double.IsNaN(d) ? double.NegativeInfinity : d;

    private static StrategySpec MakeSpec(string id, string name, ConditionGroup root, OrderSide side)
    {
        var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 2.0);
        var tpLadder = new List<TpLadderRung>
        {
            new(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 1.5, ClosePortion: 0.50),
            new(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 3.0, ClosePortion: 0.50),
        };
        var risk = new RiskPlan(
            Stop: stop, TpLadder: tpLadder,
            Sizing: new PositionSizing(SizingMode.FixedRiskPercent, RiskPercent: 0.005),
            Entry: new EntryTrigger(EntryTriggerKind.Immediate),
            MinRewardRiskRatio: 1.5,
            StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
            NotionalEquity: 10000.0);
        return new StrategySpec(
            Id: id, Name: name, Description: "Gate battery cell.",
            Side: side, Conditions: root, Risk: risk,
            ExecutionMode: StrategyExecutionMode.Suggestion,
            CreatedUtc: DateTime.UtcNow, UpdatedUtc: DateTime.UtcNow,
            IsAutoActivate: false);
    }

    private static async Task<RunResult> Run(
        StrategySpec spec, IStrategyBacktester backtester, IConfigurableStrategyFactory factory,
        SnapshotFile snapshot, WorkspaceState state, DateTime start, DateTime end, int warmup)
    {
        // 2026-04-09: gate battery now matches RunCommand's cost model. Previously the
        // gate battery defaulted CommissionRate and SlippagePercent to 0, which made
        // every reported expectancy GROSS of execution costs. Several "CI survivors"
        // turned out to be cost-blind artifacts (see project_pulse_reversal_long memory).
        // 10 bps commission + 5 bps slippage matches both legs of a typical centralized-
        // exchange crypto trade and the run command's defaults.
        var config = new BacktestConfig(
            StartingCapital: 10000.0,
            CommissionRate: 0.001,
            SlippagePercent: 0.0005,
            WarmupBars: warmup, ReplayProfiles: false,
            StartDate: start, EndDate: end, AllowReverseOnSignal: false);
        var strat = factory.Create(spec);
        var result = await backtester.RunAsync(strat, snapshot.Bars, config, state);
        var (lo, _, hi) = BootstrapCi.FromResult(result);
        return new RunResult(
            Trades: result.Metrics.TotalSignals,
            ExpectancyR: double.IsNaN(result.Expectancy) ? 0 : result.Expectancy,
            CiLo: lo, CiHi: hi);
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "…";

    private record RunResult(int Trades, double ExpectancyR, double CiLo, double CiHi);
}
