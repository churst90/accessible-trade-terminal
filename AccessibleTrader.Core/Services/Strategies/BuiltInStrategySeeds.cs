using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Hard-coded library of starter strategy specs that ship with the app. The
    /// <see cref="JsonStrategyLibrary"/> calls <see cref="EnsureSeeded"/> after every Reload —
    /// any spec whose stable ID is not already present in the user's library is inserted.
    ///
    /// IDs are intentionally hand-picked (e.g. <c>builtin.cryptoface.long.v1</c>) so the
    /// seeder is idempotent and a user who edits or deletes a built-in is not pestered by
    /// it reappearing on the next launch — once present, the seeder leaves it alone, even
    /// if the user has modified it. To force a reseed, bump the version suffix.
    ///
    /// Built-in specs default to <c>IsAutoActivate = false</c> so they show up in the
    /// library tab as templates the user can load, inspect, modify, and run on demand —
    /// nothing executes automatically without explicit user action.
    /// </summary>
    public static class BuiltInStrategySeeds
    {
        // v13 — Blue Dot + Faber regime filter (post MCB-rewrite survivor). AND-gates the
        // Cipher B blue dot with REGIME.AboveSma200 > 0. BTC daily walk-forward:
        // H1 +0.65R/70% WR, H2 +0.50R/67% WR. First post-rewrite spec to test as a real
        // candidate rather than a diagnostic.
        public const string LongV13BlueDotSma200Id = "builtin.long.v13-blue-dot-sma200";

        // v14 — Hidden Bull Continuation + SMA200 (the second post-rewrite survivor).
        // Hidden Bull Continuation passed strict bootstrap CI on BTC 4h H1 (20 trades,
        // +1.005R, CIlo +0.34) in the 2026-04-11 isolation diagnostic — the only
        // Cipher B trend-continuation signal to clear that bar. The SMA200 gate is
        // mechanical mirror of v13 for clean comparison; "trend continuation in an
        // uptrend" is the canonical use of the signal so the gate is well-motivated
        // rather than fitted.
        public const string LongV14HiddenBullSma200Id = "builtin.long.v14-hidden-bull-sma200";

        // v15 — Blue Dot AND Bullish Divergence (within 5 bars). The two highest-
        // R survivors as confluence: blue dot picks the moment of the WT cross at
        // OS, bullish divergence confirms that price made a lower low while WT
        // made a higher low (the structural reason the cross is meaningful, not
        // random noise). Should be much rarer than either alone but with much
        // higher per-trade R.
        public const string LongV15BlueDotBullDivId = "builtin.long.v15-blue-dot-bull-div";

        // v16 — Trilogy Long. The "real MCB methodology" confluence: Cipher A Buy
        // Signal + Cipher B Blue Dot + Cipher SR Support all firing within small
        // windows of each other. This is how Crypto Face teaches MCB — A provides
        // tape read, B provides oscillator read, SR provides structure. High-
        // conviction setups require all three orthogonal dimensions to agree.
        public const string LongV16TrilogyId = "builtin.long.v16-trilogy";

        // v16s — Trilogy Short. Symmetric mirror: Cipher A Sell + Cipher B Red +
        // Cipher SR Resistance. First real short setup that uses orthogonal
        // confluence instead of a single oscillator signal.
        public const string ShortV16TrilogyId = "builtin.short.v16-trilogy";

        // v17 — Gold Trilogy. Cipher A Buy Signal + Cipher B Gold Dot + Cipher SR
        // Support. Tests the speculation that real MCB's gold dot takes Cipher A's
        // state as a hidden input — if true, this spec should produce cleaner
        // entries than v16 (which uses the blue dot) at the cost of rarity.
        public const string LongV17GoldTrilogyId = "builtin.long.v17-gold-trilogy";

        // v18 — Refined Short. The asymmetric answer to "isn't short just the opposite
        // of long?" — no, because crypto has structural upward drift, bear moves are
        // shorter and sharper than bull moves, and perp funding charges shorts in bull
        // regimes. v18 uses Hidden Bear Continuation (NOT bearish divergence which
        // killed v13s) gated by a confirmed bear regime and crowded-long funding.
        // Tighter ATR stop (×1.5) and faster TP ladder (1R/2R) to match the shorter
        // rhythm of bear moves.
        public const string ShortV18RefinedShortId = "builtin.short.v18-refined";

        // v21 — MVRV Capitulation Trilogy. v16 Trilogy gated by COINMETRICS.MVRVRegime
        // in the capitulation band (regime = 1, MVRV < 1.0 — underwater holders).
        // Validates the "on-chain gates filter noise" thesis on an already-winning
        // base: if v16 works in general and MVRV capitulation marks the highest
        // quality bottom regime, the intersection should boost per-trade R.
        public const string LongV21MvrvCapitulationTrilogyId = "builtin.long.v21-mvrv-capitulation-trilogy";

        // Pulse Long V2 — the cleanest pure-Pulse long signal produced as of 2026-04-09.
        // GreenDotV2 from PulseProvider: slope-confirmed RSI(14) midline cross + Regime
        // (SMA200 + slope) == +1 + ADX(14) ≥ 20 (lookback). Cross-instrument validated:
        // point-positive expectancy in BOTH walk-forward halves on BOTH BTC and ETH daily
        // (v2 was the first Pulse signal ever to generalize across assets without retuning).
        // Still a hair short of CI-strict survival on a single instrument (BTC H2 CIlo
        // -0.01) — use as confluence not gospel. Layer BNVISION_FUNDING.Funding > 0 for
        // BTC to lift expectancy further (validated combo).
        public const string PulseLongV2Id = "builtin.long.pulse-v2";

        // Pulse Reversal Long — first cycle-aware Pulse strategy. Fires when CycleState
        // is in stage 1 (accumulation: price below SMA200, slope flat or rising) AND a
        // Cipher_C cycle bottom marker fired within the last 5 bars. ETH daily walk-forward:
        // H1 9 trades +0.25R CIlo -0.59 (point-positive, fails CI), H2 12 trades +1.03R
        // CIlo +0.32 — FIRST Pulse-related cell to pass strict CI on ETH H2 in any version.
        // Fundamentally different mechanic from PulseLongV2: PulseLongV2 is a stage-2
        // markup trend-follower; this is a stage-1 capitulation reversal. Use both, in
        // their respective stages, not one or the other.
        public const string PulseReversalLongId = "builtin.long.pulse-reversal";
        public const string FaberPulseLongId    = "builtin.long.faber-pulse";
        public const string BareBullPulseLongId = "builtin.long.bare-bull-pulse";
        public const string CapitulationBuyId   = "builtin.long.capitulation-buy";

        /// <summary>
        /// Walks the seed list and inserts any missing specs into the library. Idempotent:
        /// repeated calls add nothing once the user has the spec. Existing specs (even ones
        /// the user has edited) are never overwritten.
        /// </summary>
        public static void EnsureSeeded(IStrategyLibrary library)
        {
            foreach (var spec in GetAllSeeds())
            {
                if (library.GetById(spec.Id) == null)
                    library.Upsert(spec);
            }
        }

        public static IEnumerable<StrategySpec> GetAllSeeds()
        {
            // Library state 2026-04-09 (post-rolling-window stress-test):
            // The rolling-window walk-forward harness (StrategyLab `face-rolling`)
            // tested every face battery cell across 10 rolling 1500-bar windows on a
            // fresh 4000-bar BTC daily snapshot. Result:
            //
            //   • BULL pulse + Close > SMA200 (Faber filter):  70% windows positive,
            //     **40% windows pass strict bootstrap CI**, mean +0.43R, range -0.38
            //     to +1.36. The MOST robust cell across the full 89-cell battery.
            //   • BULL pulse + Close > EMA200:                 70% / 30% / +0.39R.
            //   • V3.BullCross + CMF > 0:                      90% / 30% / +0.29R.
            //
            // The Faber 200-MA filter (Mebane Faber 2007) outperformed every Pulse v1-v12
            // confluence stack we built across 13 strategy iterations. The empirical
            // lesson: filter restraint beats stacked confluence. Bare bull pulse alone
            // hit 90% rolling windows positive; stacking gates on top consistently
            // REDUCED robustness. The least-clever strategy in the battery is the most
            // robust thing we have.
            //
            // Library is now seeded with the Faber-pulse cell as the primary built-in.
            // PulseLongV2 and PulseReversalLong remain as supplementary cycle-aware
            // alternatives but Faber-pulse is the recommended starting point.
            yield return BuildCapitulationBuy();
            yield return BuildFaberPulseLong();
            yield return BuildBareBullPulseLong();
            yield return BuildPulseLongV2();
            yield return BuildPulseReversalLong();
            yield return BuildV13BlueDotSma200();
            yield return BuildV14HiddenBullSma200();
            yield return BuildV15BlueDotBullDiv();
            yield return BuildV16TrilogyLong();
            yield return BuildV16TrilogyShort();
            yield return BuildV17GoldTrilogy();
            yield return BuildV18RefinedShort();
            yield return BuildV21MvrvCapitulationTrilogy();
        }

        // ─────────────────────────────────────────────────────────────────────────────

        // ─────────────────────────────────────────────────────────────────────────────
        // Pulse Long V2 — fires on PULSE.GreenDotV2, the pre-filtered v2 long marker from
        // PulseProvider (slope-confirmed midline cross + Regime +1 + ADX bull gate). Requires
        // the Pulse indicator loaded on the chart. Cross-instrument validated on BTC + ETH
        // daily with point-positive expectancy in both walk-forward halves.
        private static StrategySpec BuildPulseLongV2()
        {
            var greenDotV2 = new ConditionLeaf(
                Id: "pulse-v2-gdv2",
                SignalDescriptorId: "PULSE.GreenDotV2",
                Operator: LeafOperator.FiredWithin,
                WithinNBars: 5,
                Score: 1.0);

            var root = new ConditionGroup(
                Id: "pulse-v2-root",
                Logic: LogicOperator.Or,
                Children: new List<ConditionNode> { greenDotV2 });

            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 2.0);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 1.5, ClosePortion: 0.50),
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 3.0, ClosePortion: 0.50),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);

            var risk = new RiskPlan(
                Stop: stop, TpLadder: tpLadder, Sizing: sizing, Entry: entry,
                MinRewardRiskRatio: 1.5,
                StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: PulseLongV2Id,
                Name: "Pulse Long V2 (cross-instrument)",
                Description:
                    "Fires on PULSE.GreenDotV2 within the last 5 bars. GreenDotV2 is the " +
                    "pre-filtered v2 long marker from PulseProvider, requiring: (1) slope-" +
                    "confirmed RSI(14) midline cross with 5-bar hold-down, (2) Regime = +1 " +
                    "(price > SMA200 and slope > +0.02% per bar), and (3) ADX(14) ≥ 20 " +
                    "within the last 3 bars. First Pulse signal to cross-instrument validate " +
                    "— point-positive both walk-forward halves on BOTH BTC daily (9/9 trades " +
                    "+0.24/+0.99R) and ETH daily (16/19 trades +0.13/+0.45R). Not a CI-strict " +
                    "survivor on a single instrument (BTC H2 CIlo -0.01) — use as confluence. " +
                    "For BTC specifically, layering BNVISION_FUNDING.Funding > 0 lifts " +
                    "expectancy to +0.49/+0.87R (validated combo). Parameters are daily-tuned; " +
                    "users on 4h/1h should load PulseProvider.Presets.CryptoFourHour. " +
                    "REQUIRES: Pulse indicator loaded on the chart.",
                Side: OrderSide.Buy,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }

        // Pulse Reversal Long — fires on (CycleState ∈ [0.5, 1.5] = stage 1 accumulation)
        // AND (any Cipher_C bottom marker fired within last 5 bars). The cycle-state filter
        // is the contextual gate that makes Cipher_C bottom signals actionable — Cipher_C
        // bottoms fire across all market conditions but are only true capitulation reversals
        // when price is below SMA200 with a flat-or-rising slope. The combination produced
        // ETH daily H2 12 trades +1.03R CIlo +0.32 (passes strict CI, first ever Pulse-
        // related cell to pass H2 on ETH). REQUIRES: Cipher_C and Pulse loaded on the chart.
        private static StrategySpec BuildPulseReversalLong()
        {
            // Stage 1 = CycleState in [0.5, 1.5]. Use two leaves bracketing the value.
            var stage1Lo = new ConditionLeaf(
                Id: "pulse-rev-cs-lo",
                SignalDescriptorId: "PULSE.CycleState",
                Operator: LeafOperator.GreaterThan,
                Value: 0.5,
                Score: 1.0);

            var stage1Hi = new ConditionLeaf(
                Id: "pulse-rev-cs-hi",
                SignalDescriptorId: "PULSE.CycleState",
                Operator: LeafOperator.LessThan,
                Value: 1.5,
                Score: 1.0);

            // OR group of Cipher_C bottom markers — single, double, or triple bottom
            // (triple is highest conviction; we accept any of the three within 5 bars).
            var cipherBot = new ConditionGroup(
                Id: "pulse-rev-cb",
                Logic: LogicOperator.Or,
                Children: new List<ConditionNode>
                {
                    new ConditionLeaf(
                        Id: "pulse-rev-cb-s",
                        SignalDescriptorId: "CIPHER_C.Bottom Single",
                        Operator: LeafOperator.FiredWithin,
                        WithinNBars: 5,
                        Score: 1.0),
                    new ConditionLeaf(
                        Id: "pulse-rev-cb-d",
                        SignalDescriptorId: "CIPHER_C.Bottom Double",
                        Operator: LeafOperator.FiredWithin,
                        WithinNBars: 5,
                        Score: 1.0),
                    new ConditionLeaf(
                        Id: "pulse-rev-cb-t",
                        SignalDescriptorId: "CIPHER_C.Bottom Triple",
                        Operator: LeafOperator.FiredWithin,
                        WithinNBars: 5,
                        Score: 1.0),
                });

            var root = new ConditionGroup(
                Id: "pulse-rev-root",
                Logic: LogicOperator.And,
                Children: new List<ConditionNode> { stage1Lo, stage1Hi, cipherBot });

            // Same risk plan as Pulse Long V2 / v11 / v12 family. ATR×2 stop fits both
            // trend-following and reversal contexts on daily crypto — drawdowns from a
            // missed reversal are similar in magnitude to drawdowns from a failed pullback.
            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 2.0);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 1.5, ClosePortion: 0.50),
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 3.0, ClosePortion: 0.50),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);

            var risk = new RiskPlan(
                Stop: stop, TpLadder: tpLadder, Sizing: sizing, Entry: entry,
                MinRewardRiskRatio: 1.5,
                StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: PulseReversalLongId,
                Name: "Pulse Reversal Long (cycle-aware)",
                Description:
                    "Cycle-aware capitulation reversal long. Fires when CycleState is in " +
                    "stage 1 (accumulation: price below SMA200, slope flat or rising) AND " +
                    "any Cipher_C bottom marker (Single, Double, or Triple) fired within " +
                    "the last 5 bars. Fundamentally different from PulseLongV2 — that's a " +
                    "stage-2 markup trend-follower, this is a stage-1 capitulation reversal. " +
                    "Walk-forward results (10bps commission + 5bps slippage included): " +
                    "ETH daily H1 9 trades +0.248R (Sharpe 4.16), H2 12 trades +1.030R " +
                    "(Sharpe 10.28, CIlo +0.32 — passes strict CI on H2, the first ever " +
                    "Pulse-related cell to do so on ETH H2). H1 fails CI (CIlo -0.59) so " +
                    "this is not a full strict-CI walk-forward survivor, but H2 is the " +
                    "harder/more recent regime, which is the one that matters most for " +
                    "live paper trading. BTC daily H1 10 trades +0.065R, H2 10 trades " +
                    "+0.228R — point-positive both halves, lower magnitude than ETH. " +
                    "ETH daily is the primary asset for this strategy; BTC daily is a " +
                    "secondary diversification target. Pairs well with PulseLongV2 — that " +
                    "runs in stage 2 markup, this runs in stage 1 accumulation, so they " +
                    "complement across the cycle rather than competing for setups. " +
                    "REQUIRES: Pulse AND Cipher_C loaded on the chart. Risk plan: ATR(14)×2 " +
                    "stop, 1.5R/3R TP ladder, BE after TP1, 0.5% risk per trade.",
                Side: OrderSide.Buy,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // v13 — Blue Dot + Faber regime filter (long).
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV13BlueDotSma200()
        {
            var blueDot = new ConditionLeaf(
                Id: "v13-blue-dot",
                SignalDescriptorId: "CIPHER_B.Oversold Crossover",
                Operator: LeafOperator.Fired,
                Score: 1.0);

            var aboveSma = new ConditionLeaf(
                Id: "v13-above-sma200",
                SignalDescriptorId: "REGIME.AboveSma200",
                Operator: LeafOperator.GreaterThan,
                Value: 0.0,
                Score: 1.0);

            var root = new ConditionGroup(
                Id: "v13-root",
                Logic: LogicOperator.And,
                Children: new List<ConditionNode> { blueDot, aboveSma });

            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 2.0);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 1.5, ClosePortion: 0.50),
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 3.0, ClosePortion: 0.50),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);

            var risk = new RiskPlan(
                Stop: stop, TpLadder: tpLadder, Sizing: sizing, Entry: entry,
                MinRewardRiskRatio: 1.5,
                StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: LongV13BlueDotSma200Id,
                Name: "v13 — Blue Dot + SMA200",
                Description:
                    "Cipher B Oversold Crossover (blue dot) AND price above SMA(200) at " +
                    "entry. The first survivor on the post-rewrite Cipher B: BTC daily H1 " +
                    "+0.65R/70% WR, H2 +0.50R/67% WR vs bare blue dot's +0.55/+0.35 with " +
                    "65/48% WR. The regime gate halves trade count (47→19 on a 4000-bar " +
                    "BTC daily snapshot) but the WR consistency is the real win — H2 jumps " +
                    "from coin-flip to 2-of-3. REQUIRES: Cipher B and Regime Filter " +
                    "indicators loaded on the chart. Risk: ATR(14)x2 stop, 1.5R/3R ladder, " +
                    "BE after TP1, 0.5% risk per trade.",
                Side: OrderSide.Buy,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // v14 — Hidden Bull Continuation + SMA200 (long).
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV14HiddenBullSma200()
        {
            var hidBull = new ConditionLeaf(
                Id: "v14-hid-bull",
                SignalDescriptorId: "CIPHER_B.Hidden Bull Continuation",
                Operator: LeafOperator.Fired,
                Score: 1.0);

            var aboveSma = new ConditionLeaf(
                Id: "v14-above-sma200",
                SignalDescriptorId: "REGIME.AboveSma200",
                Operator: LeafOperator.GreaterThan,
                Value: 0.0,
                Score: 1.0);

            var root = new ConditionGroup(
                Id: "v14-root",
                Logic: LogicOperator.And,
                Children: new List<ConditionNode> { hidBull, aboveSma });

            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 2.0);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 1.5, ClosePortion: 0.50),
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 3.0, ClosePortion: 0.50),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);

            var risk = new RiskPlan(
                Stop: stop, TpLadder: tpLadder, Sizing: sizing, Entry: entry,
                MinRewardRiskRatio: 1.5,
                StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: LongV14HiddenBullSma200Id,
                Name: "v14 — Hidden Bull Continuation + SMA200",
                Description:
                    "Cipher B Hidden Bull Continuation AND price above SMA(200). " +
                    "Hidden Bull Continuation passed strict bootstrap CI on BTC 4h " +
                    "H1 in the post-rewrite isolation diagnostic (20 trades, +1.005R, " +
                    "CIlo +0.34) — the only Cipher B trend-continuation signal to do " +
                    "so. The SMA200 gate aligns the signal's intent (continue an " +
                    "uptrend) with regime confirmation. REQUIRES: Cipher B and Regime " +
                    "Filter loaded. Risk: ATR(14)x2 stop, 1.5R/3R ladder, BE after " +
                    "TP1, 0.5% risk per trade.",
                Side: OrderSide.Buy,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // v15 — Blue Dot AND Bullish Divergence within 5 bars (long confluence).
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV15BlueDotBullDiv()
        {
            var blueDot = new ConditionLeaf(
                Id: "v15-blue-dot",
                SignalDescriptorId: "CIPHER_B.Oversold Crossover",
                Operator: LeafOperator.Fired,
                Score: 1.0);

            var bullDiv = new ConditionLeaf(
                Id: "v15-bull-div",
                SignalDescriptorId: "CIPHER_B.Bullish Divergence",
                Operator: LeafOperator.FiredWithin,
                WithinNBars: 5,
                Score: 1.0);

            var root = new ConditionGroup(
                Id: "v15-root",
                Logic: LogicOperator.And,
                Children: new List<ConditionNode> { blueDot, bullDiv });

            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 2.0);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 1.5, ClosePortion: 0.50),
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 3.0, ClosePortion: 0.50),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);

            var risk = new RiskPlan(
                Stop: stop, TpLadder: tpLadder, Sizing: sizing, Entry: entry,
                MinRewardRiskRatio: 1.5,
                StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: LongV15BlueDotBullDivId,
                Name: "v15 — Blue Dot + Bullish Divergence (confluence)",
                Description:
                    "Cipher B Oversold Crossover (blue dot) AND a Bullish Divergence " +
                    "fired within the last 5 bars. The two highest-R Cipher B long " +
                    "signals as a confluence stack: blue dot picks the entry moment, " +
                    "bullish divergence confirms the structural reason — price made a " +
                    "lower low while WT made a higher low. Expected to be MUCH rarer " +
                    "than either alone but with substantially higher per-trade R. " +
                    "REQUIRES: Cipher B loaded. Risk: ATR(14)x2 stop, 1.5R/3R ladder, " +
                    "BE after TP1, 0.5% risk per trade.",
                Side: OrderSide.Buy,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // v16 — Trilogy Long (A + B + SR confluence).
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV16TrilogyLong()
        {
            var aBuy = new ConditionLeaf(
                Id: "v16-a-buy",
                SignalDescriptorId: "CIPHER_A.Buy Signal",
                Operator: LeafOperator.FiredWithin,
                WithinNBars: 5,
                Score: 1.0);

            var bBlueDot = new ConditionLeaf(
                Id: "v16-b-blue",
                SignalDescriptorId: "CIPHER_B.Oversold Crossover",
                Operator: LeafOperator.Fired,
                Score: 1.0);

            var srSupport = new ConditionLeaf(
                Id: "v16-sr-support",
                SignalDescriptorId: "CIPHER_SR.Support",
                Operator: LeafOperator.FiredWithin,
                WithinNBars: 3,
                Score: 1.0);

            var root = new ConditionGroup(
                Id: "v16-root",
                Logic: LogicOperator.And,
                Children: new List<ConditionNode> { aBuy, bBlueDot, srSupport });

            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 2.0);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 1.5, ClosePortion: 0.50),
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 3.0, ClosePortion: 0.50),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);
            var risk = new RiskPlan(
                Stop: stop, TpLadder: tpLadder, Sizing: sizing, Entry: entry,
                MinRewardRiskRatio: 1.5,
                StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: LongV16TrilogyId,
                Name: "v16 — Trilogy Long (A + B + SR)",
                Description:
                    "Real MCB methodology confluence. Cipher A Buy Signal within 5 " +
                    "bars AND Cipher B Blue Dot AND Cipher SR Support within 3 bars. " +
                    "Three orthogonal confirmations: A reads the tape, B reads the " +
                    "oscillator cycle, SR reads structural price levels. Expected to " +
                    "be rare but high-conviction. REQUIRES: Cipher A, Cipher B, and " +
                    "Cipher SR loaded. Risk: ATR(14)x2 stop, 1.5R/3R ladder, BE " +
                    "after TP1, 0.5% risk per trade.",
                Side: OrderSide.Buy,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // v16s — Trilogy Short (A + B + SR confluence).
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV16TrilogyShort()
        {
            var aSell = new ConditionLeaf(
                Id: "v16s-a-sell",
                SignalDescriptorId: "CIPHER_A.Sell Signal",
                Operator: LeafOperator.FiredWithin,
                WithinNBars: 5,
                Score: 1.0);

            var bRedDot = new ConditionLeaf(
                Id: "v16s-b-red",
                SignalDescriptorId: "CIPHER_B.Overbought Crossover",
                Operator: LeafOperator.Fired,
                Score: 1.0);

            var srResistance = new ConditionLeaf(
                Id: "v16s-sr-resistance",
                SignalDescriptorId: "CIPHER_SR.Resistance",
                Operator: LeafOperator.FiredWithin,
                WithinNBars: 3,
                Score: 1.0);

            var root = new ConditionGroup(
                Id: "v16s-root",
                Logic: LogicOperator.And,
                Children: new List<ConditionNode> { aSell, bRedDot, srResistance });

            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 2.0);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 1.5, ClosePortion: 0.50),
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 3.0, ClosePortion: 0.50),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);
            var risk = new RiskPlan(
                Stop: stop, TpLadder: tpLadder, Sizing: sizing, Entry: entry,
                MinRewardRiskRatio: 1.5,
                StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: ShortV16TrilogyId,
                Name: "v16s — Trilogy Short (A + B + SR)",
                Description:
                    "Symmetric short mirror of v16. Cipher A Sell Signal within 5 " +
                    "bars AND Cipher B Red Dot AND Cipher SR Resistance within 3 " +
                    "bars. First short spec that uses orthogonal confluence instead " +
                    "of a single oscillator signal — v13s (bear-div + regime filter) " +
                    "failed walk-forward across all tested assets, so confluence is " +
                    "the path forward on shorts. REQUIRES: Cipher A, B, and SR loaded. " +
                    "Risk: ATR(14)x2 stop, 1.5R/3R ladder, BE after TP1, 0.5% risk.",
                Side: OrderSide.Sell,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // v17 — Gold Trilogy (A + B Gold + SR). Tests the speculation that real
        // MCB's gold dot takes Cipher A state as a hidden input. If true, the
        // A Buy Signal should correlate with the gold dot firings — this spec
        // just makes that correlation explicit at the strategy layer.
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV17GoldTrilogy()
        {
            var aBuy = new ConditionLeaf(
                Id: "v17-a-buy",
                SignalDescriptorId: "CIPHER_A.Buy Signal",
                Operator: LeafOperator.FiredWithin,
                WithinNBars: 5,
                Score: 1.0);

            var bGold = new ConditionLeaf(
                Id: "v17-b-gold",
                SignalDescriptorId: "CIPHER_B.Triple Confluence Buy",
                Operator: LeafOperator.Fired,
                Score: 1.0);

            var srSupport = new ConditionLeaf(
                Id: "v17-sr-support",
                SignalDescriptorId: "CIPHER_SR.Support",
                Operator: LeafOperator.FiredWithin,
                WithinNBars: 3,
                Score: 1.0);

            var root = new ConditionGroup(
                Id: "v17-root",
                Logic: LogicOperator.And,
                Children: new List<ConditionNode> { aBuy, bGold, srSupport });

            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 2.0);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 1.5, ClosePortion: 0.50),
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 3.0, ClosePortion: 0.50),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);
            var risk = new RiskPlan(
                Stop: stop, TpLadder: tpLadder, Sizing: sizing, Entry: entry,
                MinRewardRiskRatio: 1.5,
                StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: LongV17GoldTrilogyId,
                Name: "v17 — Gold Trilogy (A + B Gold + SR)",
                Description:
                    "Cipher A Buy Signal within 5 bars AND Cipher B Gold Dot AND " +
                    "Cipher SR Support within 3 bars. Tests the speculation that real " +
                    "MCB's gold dot takes Cipher A state as a hidden input. Expected " +
                    "to be VERY rare (gold dots + A Buy + SR Support all aligning is a " +
                    "4-variable conjunction) but the per-trade R should be the highest " +
                    "in the suite. REQUIRES: Cipher A, B, and SR loaded. Risk: " +
                    "ATR(14)x2 stop, 1.5R/3R ladder, BE after TP1, 0.5% risk.",
                Side: OrderSide.Buy,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // v18 — Refined Short. Hidden Bear Continuation + confirmed bear regime +
        // crowded-long funding. Uses a CONTINUATION signal (not a divergence) so it
        // rides bear moves rather than trying to call tops in uptrends like v13s did.
        // Tighter ATR stop and faster TP ladder to match bear-move rhythm.
        //
        // NOTE: Uses BNVISION_FUNDING (Binance deep-history, ~5.6 years). Walk-forward
        // testable in StrategyLab but not live-runnable until BinanceVision is promoted
        // from lab-only to a Core cross-series provider. Core's FUNDING_RATE indicator
        // is OKX-sourced with only ~11 days of history — unusable for backtesting.
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV18RefinedShort()
        {
            var hidBear = new ConditionLeaf(
                Id: "v18-hid-bear",
                SignalDescriptorId: "CIPHER_B.Hidden Bear Continuation",
                Operator: LeafOperator.Fired,
                Score: 1.0);

            var belowSma = new ConditionLeaf(
                Id: "v18-below-sma200",
                SignalDescriptorId: "REGIME.AboveSma200",
                Operator: LeafOperator.LessThan,
                Value: 0.0,
                Score: 1.0);

            // Funding > 0 means longs are still paying shorts → crowded-long posture.
            // Shorting into this gets the funding credit plus a contrarian edge over
            // remaining longs who haven't unwound yet.
            var fundingPositive = new ConditionLeaf(
                Id: "v18-funding-positive",
                SignalDescriptorId: "BNVISION_FUNDING.Funding",
                Operator: LeafOperator.GreaterThan,
                Value: 0.0,
                Score: 1.0);

            var root = new ConditionGroup(
                Id: "v18-root",
                Logic: LogicOperator.And,
                Children: new List<ConditionNode> { hidBear, belowSma, fundingPositive });

            // Tighter risk plan for shorts: ATR×1.5 stop (shorter hold), 1R/2R ladder
            // (smaller wins but higher hit rate), MinRR 1.0.
            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 1.5);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 1.0, ClosePortion: 0.50),
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 2.0, ClosePortion: 0.50),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);
            var risk = new RiskPlan(
                Stop: stop, TpLadder: tpLadder, Sizing: sizing, Entry: entry,
                MinRewardRiskRatio: 1.0,
                StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: ShortV18RefinedShortId,
                Name: "v18 — Refined Short (Hidden Bear + bear regime + funding)",
                Description:
                    "Cipher B Hidden Bear Continuation AND price below SMA(200) AND " +
                    "funding rate positive. Uses a continuation signal (NOT bearish " +
                    "divergence, which killed v13s) so we ride bear moves rather than " +
                    "try to call tops in an uptrend. Funding > 0 means longs are still " +
                    "paying shorts — the crowded-long posture that makes shorts contrarian. " +
                    "Tighter ATR×1.5 stop + 1R/2R ladder to match the faster rhythm of " +
                    "bear moves in crypto. REQUIRES: Cipher B, Regime Filter, and Funding " +
                    "Rate indicators loaded.",
                Side: OrderSide.Sell,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }



        // ─────────────────────────────────────────────────────────────────────────────
        // v21 — MVRV Capitulation Trilogy. v16 Trilogy gated by COINMETRICS.MVRVRegime
        // = 1 (capitulation: MVRV < 1.0, holders underwater). Tests whether an on-chain
        // regime gate boosts per-trade R on an already-winning base strategy.
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV21MvrvCapitulationTrilogy()
        {
            // MVRVRegime is a 4-band classifier: 1=capitulation (<1.0), 2=fair (1-2),
            // 3=markup (2-3), 4=distribution/euphoria (>3). < 2 = capitulation band.
            var mvrvCapit = new ConditionLeaf(
                Id: "v21-mvrv-capit",
                SignalDescriptorId: "COINMETRICS.MVRVRegime",
                Operator: LeafOperator.LessThan,
                Value: 2.0,
                Score: 1.0);

            var aBuy = new ConditionLeaf(
                Id: "v21-a-buy",
                SignalDescriptorId: "CIPHER_A.Buy Signal",
                Operator: LeafOperator.FiredWithin,
                WithinNBars: 5,
                Score: 1.0);

            var bBlueDot = new ConditionLeaf(
                Id: "v21-b-blue",
                SignalDescriptorId: "CIPHER_B.Oversold Crossover",
                Operator: LeafOperator.Fired,
                Score: 1.0);

            var srSupport = new ConditionLeaf(
                Id: "v21-sr-support",
                SignalDescriptorId: "CIPHER_SR.Support",
                Operator: LeafOperator.FiredWithin,
                WithinNBars: 3,
                Score: 1.0);

            var root = new ConditionGroup(
                Id: "v21-root",
                Logic: LogicOperator.And,
                Children: new List<ConditionNode> { mvrvCapit, aBuy, bBlueDot, srSupport });

            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 2.0);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 1.5, ClosePortion: 0.50),
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 3.0, ClosePortion: 0.50),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);
            var risk = new RiskPlan(
                Stop: stop, TpLadder: tpLadder, Sizing: sizing, Entry: entry,
                MinRewardRiskRatio: 1.5,
                StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: LongV21MvrvCapitulationTrilogyId,
                Name: "v21 — MVRV Capitulation Trilogy",
                Description:
                    "v16 Trilogy (A Buy + B Blue + SR Support) gated by COINMETRICS " +
                    "MVRV Regime < 2 (capitulation band: MVRV < 1.0, holders underwater). " +
                    "Tests the thesis that on-chain regime filters boost per-trade R on " +
                    "an already-winning strategy base. Expected to be very rare (MVRV " +
                    "capitulation is a small fraction of bars) but with highest-quality " +
                    "entries. REQUIRES: Cipher A, B, SR, and CoinMetrics loaded. Same " +
                    "risk as v16 (ATR×2 stop, 1.5R/3R ladder, BE after TP1, 0.5% risk).",
                Side: OrderSide.Buy,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // REMOVED 2026-04-09: BuildChopBullPulse / BuildChopBearPulse. The chop-gated
        // Pulse pair did not survive a fresh-snapshot rerun (single-walk-forward artifact
        // — CIlo=0.00 H2 collapsed to -0.05 when the snapshot extended back to 2015 and
        // the H2 split moved). The PULSE.RollSharpe / AbsRollSharpe / TrendState
        // *components* remain in PulseProvider as a real-time regime read-out for
        // chart speech and as raw signals available to user-built strategies, but no
        // built-in spec consumes them. See git history if you want to recover the spec
        // builders for further experimentation.
        // ─────────────────────────────────────────────────────────────────────────────

        // Faber-Pulse Long — the most empirically robust setup across rolling-window
        // walk-forward stress testing. Single regime gate (Close > SMA200) on top of
        // any bull entry pulse from the Cipher A/B/SR family. Mebane Faber 2007.
        //
        // Validation (BTC daily, fresh 4000-bar snapshot 2015-2026, 10 rolling 1500-bar
        // windows stepped 250 bars, BacktestConfig with 10bps commission + 5bps slippage):
        //   • 7/10 windows positive expectancy (70%)
        //   • 4/10 windows pass strict bootstrap 95% CI (40%)
        //   • mean +0.43R/trade, std 0.60, range -0.38R to +1.36R
        //   • avg 15.3 trades per window
        //   • highest CI-pass count of any cell in the 89-cell face battery
        //
        // Cross-asset rolling validation pending — this spec needs to be re-tested on
        // ETH/XRP/SOL/LTC daily before it can be called a portable survivor. As of
        // shipping it is BTC-validated only.
        //
        // Risk plan: ATR(14)×2 stop, 1.5R/3R TP ladder with 50/50 partial close, BE
        // after TP1, 0.5% risk per trade. Same as the entire Pulse family.
        //
        // REQUIRES: Pulse and RegimeProvider both loaded on the chart. AddIndicatorModal
        // → Oscillators → Pulse, then Overlays → Regime Filter (200 MA).
        private static StrategySpec BuildFaberPulseLong()
        {
            // The "any bull entry pulse" disjunction — same OR-of-bull-markers used by
            // every Pulse-family strategy. Cipher B blue dot (Oversold Crossover), Cipher
            // B gold cross (Triple Confluence Buy), Cipher A Buy Signal, or Cipher SR
            // Support touch — any one of those firing within the last 5 bars qualifies.
            var bullPulse = new ConditionGroup(
                Id: "faber-bull-pulse",
                Logic: LogicOperator.Or,
                Children: new List<ConditionNode>
                {
                    new ConditionLeaf(
                        Id: "faber-blue",
                        SignalDescriptorId: "CIPHER_B.Oversold Crossover",
                        Operator: LeafOperator.FiredWithin, WithinNBars: 5, Score: 1.0),
                    new ConditionLeaf(
                        Id: "faber-gold",
                        SignalDescriptorId: "CIPHER_B.Triple Confluence Buy",
                        Operator: LeafOperator.FiredWithin, WithinNBars: 5, Score: 1.0),
                    new ConditionLeaf(
                        Id: "faber-abuy",
                        SignalDescriptorId: "CIPHER_A.Buy Signal",
                        Operator: LeafOperator.FiredWithin, WithinNBars: 5, Score: 1.0),
                    new ConditionLeaf(
                        Id: "faber-srsup",
                        SignalDescriptorId: "CIPHER_SR.Support",
                        Operator: LeafOperator.FiredWithin, WithinNBars: 5, Score: 1.0),
                });

            // The Faber regime gate: Close > SMA(200). Implemented as
            // REGIME.AboveSma200 > 0 because that component is shaped Close - SMA200.
            var faberGate = new ConditionLeaf(
                Id: "faber-sma-gate",
                SignalDescriptorId: "REGIME.AboveSma200",
                Operator: LeafOperator.GreaterThan,
                Value: 0.0,
                Score: 1.0);

            var root = new ConditionGroup(
                Id: "faber-root",
                Logic: LogicOperator.And,
                Children: new List<ConditionNode> { faberGate, bullPulse });

            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 2.0);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 1.5, ClosePortion: 0.50),
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 3.0, ClosePortion: 0.50),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);
            var risk   = new RiskPlan(stop, tpLadder, sizing, entry,
                MinRewardRiskRatio: 1.5,
                StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: FaberPulseLongId,
                Name: "Faber-Pulse Long (200 SMA + bull pulse)",
                Description:
                    "The most empirically robust Pulse-family setup. Fires any bull entry " +
                    "pulse (Cipher B Oversold Crossover / Triple Confluence Buy / Cipher A " +
                    "Buy Signal / Cipher SR Support, FiredWithin 5 bars) ONLY when Close > " +
                    "SMA(200) — the textbook Mebane Faber 2007 regime filter. Validated " +
                    "via rolling-window walk-forward stress test (StrategyLab face-rolling, " +
                    "10 windows × 1500 bars × 250-bar step on fresh BTC daily 4000-bar " +
                    "snapshot 2015-2026): 70% windows positive expectancy, 40% windows pass " +
                    "strict bootstrap CI, mean +0.43R/trade, range -0.38R to +1.36R, ~15 " +
                    "trades per window. Highest CI-pass count of any cell in the 89-cell " +
                    "face battery — outperformed every Pulse v1-v12 confluence stack across " +
                    "13 prior iterations. The empirical lesson is filter restraint: stacking " +
                    "additional gates on top consistently REDUCED robustness in the rolling " +
                    "test. BTC-validated only as of shipping; cross-asset rolling test on " +
                    "ETH/XRP/SOL/LTC pending. Risk plan: ATR(14)×2 stop, 1.5R/3R TP ladder " +
                    "(50/50 partial), BE after TP1, 0.5% risk per trade. " +
                    "REQUIRES: Pulse + Regime Filter (200 MA) loaded on the chart.",
                Side: OrderSide.Buy,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }

        // Bare Bull Pulse Long — the simplest possible Pulse strategy. ANY bull entry
        // pulse from the Cipher A/B/SR family with NO confluence filter at all.
        //
        // Cross-asset rolling-window stress test (StrategyLab face-rolling, fresh
        // 4000-bar BTC + 3159-bar ETH + 3402-bar XRP + 3000-bar LTC daily snapshots,
        // 1500-bar windows × 250-bar step):
        //
        //   BTC: 90% windows positive, 20% windows pass strict CI, mean +0.32R, 24.5 tr/win
        //   ETH: 83% windows positive, 17% windows pass strict CI, mean +0.20R, 30.5 tr/win
        //   XRP: 71% windows positive,  0% windows pass strict CI, mean +0.12R, 26.6 tr/win
        //   LTC: 50% windows positive,  0% windows pass strict CI, mean +0.07R, 32.3 tr/win
        //
        // The MOST cross-asset-consistent cell in the entire 89-cell face battery. Three
        // out of four major crypto assets show ≥71% positive expectancy across rolling
        // windows. LTC is the lone exception (50% — coin-flip).
        //
        // Use this when trading anything other than LTC. Use Faber-Pulse for BTC
        // specifically (40% CI count vs Bare's 20% on BTC — Faber dominates BTC because
        // BTC has the strongest secular trend and the SMA200 filter is doing real work).
        // For ETH/XRP this Bare cell is the better choice because the SMA200 filter
        // helps less on assets with weaker drift.
        //
        // Risk plan: same family as Faber-Pulse — ATR(14)×2 stop, 1.5R/3R TP ladder,
        // BE after TP1, 0.5% risk per trade.
        //
        // REQUIRES: Cipher A + Cipher B + Cipher SR loaded on the chart. (Pulse and
        // RegimeProvider are NOT required for this strategy — it's intentionally minimal.)
        // Capitulation Buy — first non-OHLCV-gated strategy in the Library. Fires any
        // bull entry pulse from the Cipher A/B/SR family ONLY when on-chain MVRV ratio
        // is below 1.0 (the canonical "average holder underwater" capitulation band).
        //
        // MVRV (Market Value to Realized Value) is the field-standard on-chain cycle
        // measurement: market cap divided by realized cap (sum of UTXOs valued at their
        // last-moved price). MVRV < 1 means the average BTC was last moved at a higher
        // price than current spot — i.e. holders are sitting at unrealized losses, the
        // hallmark of capitulation phases. Historically rare: BTC's MVRV has been below
        // 1 in roughly 5-8% of trading days, mostly clustered in 2015, late 2018, mid
        // 2020 (Covid), and late 2022. ETH similar pattern.
        //
        // Validation (rolling-window walk-forward stress test, 2026-04-09):
        //   BTC daily 4000-bar snapshot, 10×1500-bar windows step 250:
        //     3 valid windows (cell rarely fires, MVRV<1 is concentrated in cycle bottoms)
        //     67% windows positive expectancy, mean +0.59R/trade — highest mean of any cell
        //     range -0.37R to +1.48R, avg 5.3 trades per window
        //   ETH daily 3159-bar snapshot, 6×1500-bar windows step 250:
        //     6 valid windows
        //     100% windows positive expectancy, mean +0.44R/trade
        //     range +0.16R to +0.99R, worst window still positive
        //     avg 20.3 trades per window — fires more often on ETH because ETH MVRV history
        //     spends more time below 1 than BTC's does
        //   XRP daily: 7 windows, 57% positive, mean +0.08R (marginal)
        //   LTC daily: 6 windows, 67% positive, mean +0.05R (marginal)
        //   COMBINED: 22 rolling windows tested across 4 assets, 16 positive (~73%),
        //   weighted-average expectancy ~+0.29R. ETH and BTC are the primary use cases.
        //
        // What this is: the strongest single-cell empirical finding in the project's
        // strategy work to date, validated by rolling-window stress test (the gate that
        // catches single-snapshot artifacts). The first time non-OHLCV data has produced
        // a measurably better cell than the bare baseline.
        //
        // What this is NOT: a strict-CI walk-forward survivor. No window combination passes
        // 95% bootstrap CI on every asset simultaneously. The cell is rare-fire (5-25 trades
        // per 1500-bar window) and the per-window CI is wide. Use this as a high-conviction
        // confluence signal for already-bullish setups, not as a primary entry trigger.
        //
        // Risk plan: same as the entire Pulse family. ATR(14)×2 stop, 1.5R/3R TP ladder
        // with 50/50 partial close, BE after TP1, 0.5% risk per trade.
        //
        // REQUIRES: CoinMetrics indicator + Cipher A + Cipher B + Cipher SR loaded on the
        // chart. The CoinMetrics provider needs cached on-chain data for your asset; run
        // `StrategyLab coinmetrics --assets btc,eth,xrp,ltc` once to populate the cache.
        // Currently supports: BTC, ETH, XRP, LTC. Add more by extending the asset detector
        // in CoinMetricsProvider and re-running the downloader.
        private static StrategySpec BuildCapitulationBuy()
        {
            var bullPulse = new ConditionGroup(
                Id: "cap-bull-pulse",
                Logic: LogicOperator.Or,
                Children: new List<ConditionNode>
                {
                    new ConditionLeaf(
                        Id: "cap-blue",
                        SignalDescriptorId: "CIPHER_B.Oversold Crossover",
                        Operator: LeafOperator.FiredWithin, WithinNBars: 5, Score: 1.0),
                    new ConditionLeaf(
                        Id: "cap-gold",
                        SignalDescriptorId: "CIPHER_B.Triple Confluence Buy",
                        Operator: LeafOperator.FiredWithin, WithinNBars: 5, Score: 1.0),
                    new ConditionLeaf(
                        Id: "cap-abuy",
                        SignalDescriptorId: "CIPHER_A.Buy Signal",
                        Operator: LeafOperator.FiredWithin, WithinNBars: 5, Score: 1.0),
                    new ConditionLeaf(
                        Id: "cap-srsup",
                        SignalDescriptorId: "CIPHER_SR.Support",
                        Operator: LeafOperator.FiredWithin, WithinNBars: 5, Score: 1.0),
                });

            // The on-chain capitulation gate: MVRV ratio below 1.0 means the average
            // holder is underwater at their cost basis. Implementation reads from
            // CoinMetricsProvider.CompMvrv which is forward-filled onto chart bars.
            var capitulationGate = new ConditionLeaf(
                Id: "cap-mvrv-gate",
                SignalDescriptorId: "COINMETRICS.MVRV",
                Operator: LeafOperator.LessThan,
                Value: 1.0,
                Score: 1.0);

            var root = new ConditionGroup(
                Id: "cap-root",
                Logic: LogicOperator.And,
                Children: new List<ConditionNode> { capitulationGate, bullPulse });

            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 2.0);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 1.5, ClosePortion: 0.50),
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 3.0, ClosePortion: 0.50),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);
            var risk   = new RiskPlan(stop, tpLadder, sizing, entry,
                MinRewardRiskRatio: 1.5,
                StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: CapitulationBuyId,
                Name: "Capitulation Buy (on-chain MVRV<1 + bull pulse)",
                Description:
                    "First non-OHLCV-gated Library strategy. Fires any bull entry pulse " +
                    "(Cipher B Oversold Crossover / Triple Confluence Buy / Cipher A Buy " +
                    "Signal / Cipher SR Support, FiredWithin 5 bars) ONLY when CoinMetrics " +
                    "MVRV ratio is below 1.0 — the canonical on-chain capitulation band " +
                    "where the average holder is underwater at cost basis. Rare-fire " +
                    "(5-25 trades per ~4-year window) but the strongest measured edge in " +
                    "the project's strategy work to date. Rolling-window walk-forward " +
                    "validation: BTC 67% windows positive +0.59R mean (highest of any cell), " +
                    "ETH 100% windows positive +0.44R (every window positive), combined " +
                    "73% windows positive across BTC/ETH/XRP/LTC. NOT a strict-CI multi-asset " +
                    "survivor — use as a high-conviction confluence signal during bear-market " +
                    "accumulation, not as a primary entry trigger. Risk plan: ATR(14)×2 stop, " +
                    "1.5R/3R TP ladder, BE after TP1, 0.5% risk per trade. " +
                    "REQUIRES: CoinMetrics + Cipher A + Cipher B + Cipher SR loaded on chart. " +
                    "REQUIRES: cached on-chain data for your asset (run `StrategyLab " +
                    "coinmetrics --assets btc,eth,xrp,ltc` once to populate). Supported " +
                    "assets: BTC, ETH, XRP, LTC.",
                Side: OrderSide.Buy,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }

        private static StrategySpec BuildBareBullPulseLong()
        {
            var bullPulse = new ConditionGroup(
                Id: "bare-bull-pulse",
                Logic: LogicOperator.Or,
                Children: new List<ConditionNode>
                {
                    new ConditionLeaf(
                        Id: "bare-blue",
                        SignalDescriptorId: "CIPHER_B.Oversold Crossover",
                        Operator: LeafOperator.FiredWithin, WithinNBars: 5, Score: 1.0),
                    new ConditionLeaf(
                        Id: "bare-gold",
                        SignalDescriptorId: "CIPHER_B.Triple Confluence Buy",
                        Operator: LeafOperator.FiredWithin, WithinNBars: 5, Score: 1.0),
                    new ConditionLeaf(
                        Id: "bare-abuy",
                        SignalDescriptorId: "CIPHER_A.Buy Signal",
                        Operator: LeafOperator.FiredWithin, WithinNBars: 5, Score: 1.0),
                    new ConditionLeaf(
                        Id: "bare-srsup",
                        SignalDescriptorId: "CIPHER_SR.Support",
                        Operator: LeafOperator.FiredWithin, WithinNBars: 5, Score: 1.0),
                });

            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 2.0);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 1.5, ClosePortion: 0.50),
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 3.0, ClosePortion: 0.50),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);
            var risk   = new RiskPlan(stop, tpLadder, sizing, entry,
                MinRewardRiskRatio: 1.5,
                StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: BareBullPulseLongId,
                Name: "Bare Bull Pulse Long (cross-asset)",
                Description:
                    "The simplest Pulse strategy and the most cross-asset-consistent cell " +
                    "in the entire face battery. Fires any bull entry pulse (Cipher B Oversold " +
                    "Crossover / Triple Confluence Buy / Cipher A Buy Signal / Cipher SR Support, " +
                    "FiredWithin 5 bars) with NO confluence filter at all. Rolling-window walk-" +
                    "forward stress test (10 windows × 1500 bars × 250-bar step on fresh daily " +
                    "snapshots): BTC 90% windows positive (20% CI pass, mean +0.32R, 24.5 trades/" +
                    "window), ETH 83% (17% CI, +0.20R), XRP 71% (0% CI, +0.12R), LTC 50% (0% CI, " +
                    "+0.07R). Use this on ETH/XRP/SOL or unknown crypto assets where the Faber " +
                    "200-SMA filter doesn't generalize. For BTC specifically, prefer Faber-Pulse " +
                    "(40% CI count vs 20% for Bare). The empirical lesson from rolling-window " +
                    "validation: filter restraint beats stacked confluence — every iteration of " +
                    "Pulse v1-v12 added gates that REDUCED rolling-window robustness vs this bare " +
                    "version. Risk plan: ATR(14)×2 stop, 1.5R/3R TP ladder (50/50 partial), BE " +
                    "after TP1, 0.5% risk per trade. " +
                    "REQUIRES: Cipher A + Cipher B + Cipher SR loaded on the chart.",
                Side: OrderSide.Buy,
                Conditions: bullPulse,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }
    }
}
