using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Tests the published Crypto Face Market Cipher setup against the snapshot in a small,
/// curated battery rather than a brute-force sweep. The test grid is designed to answer
/// three specific questions:
///
///   1. Does Face's full 3-stage long setup (Anchor washed + Trigger > 0 + MFW NEGATIVE
///      + entry pulse) survive walk-forward with bootstrap CIs?
///
///   2. Does the contrarian Money Flow direction (NEGATIVE per Face) outperform the naive
///      "buy when MF is bullish" v1 reading (POSITIVE)?
///
///   3. Does the symmetric short setup (Anchor in OB + Trigger &lt; 0 + MFW POSITIVE +
///      bear pulse) work, or is the long-only edge an artifact of BTC's secular uptrend?
///
/// Each cell is reported with H1/H2 trade count, R-expectancy, 95% bootstrap CI lower
/// bound, and a SURVIVOR flag if both halves clear CI-lo &gt; 0 with at least 5 trades each
/// (relaxed from the sweep's 10-trade gate because Face setups are intentionally rare).
///
/// All cells use Cipher B Money Flow Wave's true zero (-80 raw) and Cipher B Anchor Wave's
/// natural zero (0 raw). The Face long stages are encoded as published in
/// BuiltInStrategySeeds.cs lines 287-330.
/// </summary>
public static class FaceBatteryCommand
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
            var spec = MakeSpec($"face.{idx}", label, root, side);
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

        // The "any bull entry pulse" group used inside Face long setups: blue dot OR gold OR
        // Cipher A buy, all FiredWithin 5 bars (Face teaches a small look-back so the entry
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

        // 3. Anchor washed (Face stage 1): Anchor < -53 AND bull pulse.
        cells.Add(("Face S1: Anchor Wave < -53 AND bull pulse",
            OrderSide.Buy,
            Group("c3", LogicOperator.And,
                Lt("c3-anc", "CIPHER_B.Anchor Wave", -53),
                BullEntryPulse("c3"))));

        // 4. Stages 1+2: Anchor washed AND Trigger > 0 AND bull pulse.
        cells.Add(("Face S1+S2: Anchor < -53 AND Trigger > 0 AND pulse",
            OrderSide.Buy,
            Group("c4", LogicOperator.And,
                Lt("c4-anc", "CIPHER_B.Anchor Wave", -53),
                Gt("c4-trg", "CIPHER_B.Trigger Wave", 0),
                BullEntryPulse("c4"))));

        // 5. FULL Face long (S1+S2+S3 contrarian MFW negative): Face's published setup.
        cells.Add(("Face FULL: Anc<-53 AND Trg>0 AND MFW<0(base) AND pulse",
            OrderSide.Buy,
            Group("c5", LogicOperator.And,
                Lt("c5-anc", "CIPHER_B.Anchor Wave", -53),
                Gt("c5-trg", "CIPHER_B.Trigger Wave", 0),
                Lt("c5-mfw", "CIPHER_B.Money Flow Wave", MfBaseline),  // raw < -80 = below visual zero
                BullEntryPulse("c5"))));

        // 6. v1 inverse (the "naive" reading Face explicitly rejects): same gates but MFW POSITIVE.
        cells.Add(("v1 NAIVE: Anc<-53 AND Trg>0 AND MFW>0(base) AND pulse",
            OrderSide.Buy,
            Group("c6", LogicOperator.And,
                Lt("c6-anc", "CIPHER_B.Anchor Wave", -53),
                Gt("c6-trg", "CIPHER_B.Trigger Wave", 0),
                Gt("c6-mfw", "CIPHER_B.Money Flow Wave", MfBaseline),
                BullEntryPulse("c6"))));

        // 7. Just MFW sign filter, no anchor: bull pulse AND MFW < 0(base) (the Face contrarian-MF idea on its own).
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

        // 8c. Face FULL + SMA200 — does the regime filter rescue Face's stages?
        cells.Add(("Face FULL + Close>SMA200",
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

        // 10. Symmetric Face short S1: Anchor > +53 AND bear pulse.
        cells.Add(("Face SHORT S1: Anchor Wave > +53 AND bear pulse",
            OrderSide.Sell,
            Group("c10", LogicOperator.And,
                Gt("c10-anc", "CIPHER_B.Anchor Wave", 53),
                BearEntryPulse("c10"))));

        // 11. Symmetric Face short FULL: Anchor > +53 AND Trigger < 0 AND MFW > 0(base) AND bear pulse.
        cells.Add(("Face SHORT FULL: Anc>+53 AND Trg<0 AND MFW>0(base) AND pulse",
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

        return cells;
    }

    private static void PrintSummary(List<(string Label, OrderSide Side, RunResult H1, RunResult H2)> results)
    {
        Console.WriteLine();
        Console.WriteLine("══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine("CRYPTO FACE BATTERY — published Market Cipher long + symmetric short setups, no-reverse, bootstrap CIs");
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
            Console.WriteLine("VERDICT: No Face setup has CI lower bound > 0 in BOTH halves on this snapshot.");
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
            Id: id, Name: name, Description: "Face battery cell.",
            Side: side, Conditions: root, Risk: risk,
            ExecutionMode: StrategyExecutionMode.Suggestion,
            CreatedUtc: DateTime.UtcNow, UpdatedUtc: DateTime.UtcNow,
            IsAutoActivate: false);
    }

    private static async Task<RunResult> Run(
        StrategySpec spec, IStrategyBacktester backtester, IConfigurableStrategyFactory factory,
        SnapshotFile snapshot, WorkspaceState state, DateTime start, DateTime end, int warmup)
    {
        // 2026-04-09: face battery now matches RunCommand's cost model. Previously the
        // face battery defaulted CommissionRate and SlippagePercent to 0, which made
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
