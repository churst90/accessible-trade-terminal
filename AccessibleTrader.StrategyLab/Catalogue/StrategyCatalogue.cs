using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.StrategyLab.Catalogue
{
    /// <summary>
    /// The research catalogue: every strategy spec this project has built, owned by the LAB.
    ///
    /// <para>
    /// Until 2026-08-01 this file lived in <c>AccessibleTrader.Core</c> as
    /// <c>BuiltInStrategySeeds</c> and every spec was written into the user's library on first
    /// launch. That made research artifacts look like shipped product. The terminal now ships the
    /// engine, the editor and the backtester and starts with an EMPTY library; specs travel from
    /// here into a terminal instance through an explicit export/import step
    /// (<see cref="AccessibleTrader.Core.Services.Strategies.StrategyBundle"/>), carrying their
    /// provenance with them. See <c>docs/STRATEGY_CATALOGUE.md</c>.
    /// </para>
    ///
    /// <para>
    /// IDs are hand-picked and stable (e.g. <c>builtin.long.v16-trilogy</c>) — they are how a
    /// lab run, a note, a memory file and an imported spec refer to the same thing. Do not renumber
    /// them; add a new id instead. Every spec here must have an entry in
    /// <see cref="CatalogueProvenance"/> — <c>CatalogueProvenanceTests</c> fails the build
    /// otherwise, because a spec with no recorded verdict is exactly the thing this split removed.
    /// </para>
    ///
    /// <para>
    /// Specs default to <c>IsAutoActivate = false</c> and the importer forces it false regardless,
    /// so importing a catalogue spec never starts anything running.
    /// </para>
    /// </summary>
    public static class StrategyCatalogue
    {
        /// <summary>
        /// Bumped whenever a spec is added, removed, or its parameters change. Stamped into every
        /// exported bundle so an imported spec can be traced back to a known catalogue state.
        /// </summary>
        public const string Version = "2026-10-31.1";

        // v13 — Blue Dot + Faber regime filter (post MCB-rewrite survivor). AND-gates the
        // Cipher B blue dot with REGIME.AboveSma200 > 0. BTC daily walk-forward:
        // H1 +0.65R/70% WR, H2 +0.50R/67% WR. First post-rewrite spec to test as a real
        // candidate rather than a diagnostic.
        public const string LongV13BlueDotSma200Id = "builtin.long.v13-blue-dot-sma200";

        // v14, v15, v18 and the v23 family (v23, v23r, v23rf, v23p, v23h, v23hs, v23a, v23or, v23c):
        // RETIRED 2026-10-31 with Cipher B's parts Market Cipher B does not draw. Each needed one
        // of them (the hidden or regular divergences, or the Anchor Wave gate), so none could fire.
        // The verdicts are kept in CatalogueProvenance.Retired.

        // v16 / v16s / v17 — the Cipher SR trilogies: RETIRED 2026-08-01. The structural third
        // of the trilogy was tested and does not exist (SR proximity was a 15-bar lookahead;
        // structure labels tested indistinguishable from random), and the provider still
        // repaints, so they cannot be honestly backtested as written. The verdicts are kept in
        // CatalogueProvenance.Retired — the record is the part worth having.

        // v21 — MVRV Capitulation Trilogy: RETIRED 2026-08-01. Both halves failed — the SR leaf
        // repaints and MVRV-regime gating failed the exposure-matched null 0 for 6. See
        // CatalogueProvenance.Retired.

        // v22 — Capitulation Bottom (Long). Fires on the new TopBottomDetector
        // "Bottom Confirmed" marker — single-bar capitulation event (volume spike +
        // lower wick + below band + RSI oversold + momentum flip), gated to bottom
        // 20% of the trailing window. The first long-side seed that explicitly
        // operationalises the "bottoms are events" half of the asymmetry thesis.
        public const string LongV22CapitulationBottomId = "builtin.long.v22-capitulation-bottom";

        // v22r — Faber-gated capitulation long. v22 + REGIME.AboveSma200 > 0
        // (price above 200-period SMA). Tests the hypothesis that the raw
        // Capitulation Confirmed marker has real edge but is overwhelmed by
        // bear-regime instances; pairing it with the most empirically robust
        // single-gate filter in the suite (Faber MA, validated on v13/Faber-Pulse)
        // should lift expectancy by suppressing the bear-half false positives.
        public const string LongV22rCapitulationFaberId = "builtin.long.v22r-capitulation-faber";

        // v22 — Distribution Top (Short). Fires on the new TopBottomDetector
        // "Top Confirmed" marker — multi-bar distribution accumulator with
        // exponential decay (bearish divergence + drying volume + volatility
        // compression + upthrusts + sideways-at-highs). Gated to top 20% of the
        // window. Operationalises the "tops are processes" half of the same
        // thesis. Both v22 seeds use the same detector; the Side flips them.
        public const string ShortV22DistributionTopId   = "builtin.short.v22-distribution-top";

        // v22r — Bear-regime + crowded-funding gated distribution short. v22 +
        // REGIME.AboveSma200 < 0 AND FUNDING_RATE.Funding Rate > 0 (crowded long).
        // Mirrors the v18-refined-short pattern that's the only short in the suite
        // that walk-forward survived: don't try to call tops in an uptrend, only
        // ride distribution in confirmed bear regime where remaining longs are
        // still paying funding. Tightest single hypothesis test: if any short
        // built on v22's distribution marker can survive walk-forward, it's this one.
        public const string ShortV22rDistributionBearFundedId = "builtin.short.v22r-distribution-bear-funded";

        // Trend Baseline — the "boring institutional strategy" every fancier spec
        // must beat. Faber cross entry (price crosses above SMA200), wide ATR
        // stop, single distant target, ATR-trail after TP1 so the trend runs.
        // 2026-07-13 cross-asset study (TSMOM-12m / MA-10m / vol-target, monthly,
        // era-sliced): crypto is where it shines (BTC vol-targeted trend Sharpe
        // 1.19 vs 0.80 hold, maxDD 23% vs 83%); on indices/gold it matches
        // buy-and-hold Sharpe with 2-3x smaller drawdowns (crash insurance); on
        // single secular-growth names holding wins; FX long-only has no edge.
        public const string LongTrendBaselineId = "builtin.long.trend-baseline";

        // v24 — Cycle Low Reversal: RETIRED 2026-10-22 with Loukas Cycle Detection, whose DCL
        // Confirmed was its entry event. Not re-tested on Cipher C's DCL Confirmed. The verdict is
        // kept in CatalogueProvenance.Retired.

        // ── Per-asset "recommended" accessors: REMOVED 2026-08-01 ──────────────────
        // GetV23{Long,Short}PresetForAsset, the ForBars variants, and the one-shot
        // GetRecommendedV23{Long,Short}Spec composites all mapped a symbol — or a classified
        // profile — onto a specific seed. The terminal surfaced that as a highlighted library
        // row, a starred dropdown entry, and a "Use recommended" button.
        //
        // That is shipping an OPINION, automatically, in software a user relies on. And every
        // branch returned a Cipher-B variant, which is precisely the component this project's
        // own research falsified: eight versions of pure-Cipher confluence walked forward to
        // break-even, and structure labels tested indistinguishable from random.
        //
        // The seeds remain as a library the user browses and chooses from. Selection is the
        // user's decision. See docs/STRATEGY_LIBRARY_POLICY.md.

        // Pulse Long V2 — the cleanest pure-Pulse long signal produced as of 2026-04-09.
        // GreenDotV2 from PulseProvider: slope-confirmed RSI(14) midline cross + Regime
        // (SMA200 + slope) == +1 + ADX(14) ≥ 20 (lookback). Cross-instrument validated:
        // point-positive expectancy in BOTH walk-forward halves on BOTH BTC and ETH daily
        // (v2 was the first Pulse signal ever to generalize across assets without retuning).
        // Still a hair short of CI-strict survival on a single instrument (BTC H2 CIlo
        // -0.01) — use as confluence not gospel. Layer FUNDING_RATE.Funding Rate > 0 for
        // BTC to lift expectancy further (validated combo).
        public const string PulseLongV2Id = "builtin.long.pulse-v2";

        // Pulse Reversal Long: RETIRED 2026-10-21. Its trigger was the oscillator Cipher C's
        // Bottom Single/Double/Triple markers, which went when Cipher C was rebuilt as a cycle-low
        // indicator after Camel Finance's CF; the spec cannot fire as written. The ETH walk-forward
        // verdict is kept in CatalogueProvenance.Retired.
        public const string FaberPulseLongId    = "builtin.long.faber-pulse";
        public const string BareBullPulseLongId = "builtin.long.bare-bull-pulse";
        public const string CapitulationBuyId   = "builtin.long.capitulation-buy";

        /// <summary>Looks up one spec by its stable id, or null when the id is unknown.</summary>
        public static StrategySpec? FindById(string id) =>
            AllSpecs().FirstOrDefault(s => s.Id == id);

        /// <summary>Every spec in the catalogue, in listing order.</summary>
        public static IEnumerable<StrategySpec> AllSpecs()
        {
            // Library state 2026-04-09 (post-rolling-window stress-test):
            // The rolling-window walk-forward harness (StrategyLab `rolling-window`)
            // tested every gate battery cell across 10 rolling 1500-bar windows on a
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
            // That result is IN-SAMPLE across a single battery on one asset family, which is
            // why the Faber-pulse cell is recorded in CatalogueProvenance as InSampleOnly and
            // not as a recommendation. The 2026-07 walk-forward work (see the memory note
            // btc-trend-walkforward) later showed random parameters beating the optimised ones
            // out of sample on this same family: the family works, the fitted numbers do not.
            yield return BuildCapitulationBuy();
            yield return BuildFaberPulseLong();
            yield return BuildBareBullPulseLong();
            yield return BuildPulseLongV2();
            yield return BuildV13BlueDotSma200();
            yield return BuildV22CapitulationBottom();
            yield return BuildV22DistributionTop();
            yield return BuildV22rCapitulationFaber();
            yield return BuildV22rDistributionBearFunded();
            yield return BuildTrendBaselineLong();
        }


        // ─────────────────────────────────────────────────────────────────────────────
        // Trend Baseline — Faber cross entry, ATR-trailed exit. See LongTrendBaselineId
        // docs for the cross-asset evidence. Deliberately has NO oscillator trigger:
        // this is the benchmark, not a setup — if a cipher/cycle spec can't beat this
        // in walk-forward on the same asset, it isn't earning its complexity.
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildTrendBaselineLong()
        {
            var faberCross = new ConditionLeaf(
                Id: "trendbase-cross",
                SignalDescriptorId: "REGIME.AboveSma200",
                Operator: LeafOperator.CrossesAbove,
                Value: 0.0,
                Score: 1.0);

            var root = new ConditionGroup(
                Id: "trendbase-root", Logic: LogicOperator.And,
                Children: new List<ConditionNode> { faberCross });

            // Wide stop + one distant rung + ATR trail after TP1 = "ride the trend
            // until it bends" expressed in the RiskPlan vocabulary. The 10R rung is
            // intentionally far: the trail is the real exit.
            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 4.0);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 10.0, ClosePortion: 1.0),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);
            var risk = new RiskPlan(
                Stop: stop, TpLadder: tpLadder, Sizing: sizing, Entry: entry,
                MinRewardRiskRatio: 1.5,
                StopAdjust: StopAdjustOnTp1.TrailByAtr,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: LongTrendBaselineId,
                Name: "Trend Baseline — Faber Cross (Long, benchmark)",
                Description:
                    "THE BENCHMARK, not a setup: enters when price crosses above the 200-bar " +
                    "SMA and rides with a wide ATR(14)x4 stop that trails after TP1. Any " +
                    "cipher/cycle strategy should beat this on the same asset in walk-forward " +
                    "before being trusted. LAB WALK-FORWARD 2026-07 (H1/H2, costs included): " +
                    "POSITIVE IN BOTH HALVES ON ALL FOUR ASSETS TESTED — SPY +0.24/+0.28R, " +
                    "QQQ +0.16/+0.39R, gold +0.08/+0.72R, BTC +2.02/+0.87R (33% win rate, " +
                    "profit factor 8.0/2.9 — the classic trend profile of rare huge winners). " +
                    "Cross-asset study: strongest on crypto; crash insurance on indices/gold; " +
                    "plain holding wins on single growth stocks; long-only FX has no edge. " +
                    "REQUIRES: Regime Filter loaded. Daily bars recommended.",
                Side: OrderSide.Buy,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
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
                    "For BTC specifically, layering FUNDING_RATE.Funding Rate > 0 lifts " +
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
                Name: "Dip Buy in Uptrend — Blue Dot + SMA200 (Long) [v13]",
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
        // v22 — Capitulation Bottom Long.
        //
        // First seed built directly on the TopBottomDetector indicator. Fires the
        // long entry on its single-bar "Bottom Confirmed" marker. The detector
        // pre-gates to the bottom 20% of the trailing 100-bar window AND requires
        // a multi-component capitulation score (volume z + range z + lower-wick
        // rejection + below-band exhaustion + RSI oversold + momentum flip)
        // ≥ 0.6 — so the marker is itself already a confluence signal; no
        // additional gates needed at the strategy layer.
        //
        // Risk: ATR(14)×2 stop, 1.5R/3R TP ladder, BE-after-TP1, 0.5% risk —
        // same family as v16/v17/v21 for clean cross-comparison in walk-forward.
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV22CapitulationBottom()
        {
            var bottom = new ConditionLeaf(
                Id: "v22l-bottom",
                SignalDescriptorId: "TOP_BOTTOM_DETECTOR.Bottom Confirmed",
                Operator: LeafOperator.FiredWithin,
                WithinNBars: 2,
                Score: 1.0);

            var root = new ConditionGroup(
                Id: "v22l-root",
                Logic: LogicOperator.Or,
                Children: new List<ConditionNode> { bottom });

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
                Id: LongV22CapitulationBottomId,
                Name: "Capitulation Bottom — BTC Daily (Long) [v22]",
                Description:
                    "Fires on TOP_BOTTOM_DETECTOR.Bottom Confirmed within 2 bars. " +
                    "The detector signals a single-bar capitulation event: volume " +
                    "z-score spike + wide range + lower-wick rejection + low pierces " +
                    "lower Bollinger by ATR-fraction + RSI oversold + momentum reversal. " +
                    "Marker is gated to the bottom 20% of the trailing 100-bar window " +
                    "so it cannot fire on healthy pullbacks in uptrends. " +
                    "Operationalises the 'bottoms are events' half of the asymmetry " +
                    "thesis — single-bar event detector, no multi-bar accumulator. " +
                    "REQUIRES: TopBottomDetector loaded. Risk: ATR(14)×2 stop, " +
                    "1.5R/3R ladder, BE after TP1, 0.5% risk.",
                Side: OrderSide.Buy,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // v22 — Distribution Top Short.
        //
        // The asymmetric counterpart. Fires the short entry on the detector's
        // "Top Confirmed" marker, which is itself the output of a multi-bar
        // distribution accumulator with exponential decay. Evidence streams that
        // contribute to the accumulator: bearish divergence on confirmed pivots,
        // volume drying up at swing highs, volatility compression at price highs,
        // upthrusts above prior pivots, and sideways consolidation near highs.
        // Gated to the top 20% of the trailing window.
        //
        // Risk plan tracks v18 short conventions: ATR(14)×1.5 stop (tighter than
        // longs to match the faster rhythm of bear moves in crypto) and a 1R/2R
        // ladder. MinRR 1.0.
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV22DistributionTop()
        {
            var top = new ConditionLeaf(
                Id: "v22s-top",
                SignalDescriptorId: "TOP_BOTTOM_DETECTOR.Top Confirmed",
                Operator: LeafOperator.FiredWithin,
                WithinNBars: 2,
                Score: 1.0);

            var root = new ConditionGroup(
                Id: "v22s-root",
                Logic: LogicOperator.Or,
                Children: new List<ConditionNode> { top });

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
                Id: ShortV22DistributionTopId,
                Name: "Distribution Top — BTC 4h, robust (Short) [v22s]",
                Description:
                    "Fires on TOP_BOTTOM_DETECTOR.Top Confirmed within 2 bars. " +
                    "The detector accumulates distribution evidence over multiple " +
                    "bars (bearish divergence on confirmed pivots, volume drying up " +
                    "at swing highs, volatility compression at price highs, upthrusts " +
                    "above prior pivots, sideways regime near highs) with exponential " +
                    "decay; the marker fires when accumulated confidence crosses the " +
                    "confirm threshold while price is in the top 20% of the trailing " +
                    "window. Operationalises the 'tops are processes' half of the " +
                    "asymmetry thesis. REQUIRES: TopBottomDetector loaded. Risk: " +
                    "ATR(14)×1.5 stop, 1R/2R ladder (faster bear rhythm), 0.5% risk.",
                Side: OrderSide.Sell,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // v22r — Capitulation Bottom + Faber regime filter (Long).
        //
        // Tests whether the raw v22 capitulation marker — which has real edge on
        // ETH 1d, XRP 1d, XRP 4h but is mixed on BTC — can be lifted across the
        // board by gating with the most empirically robust single filter in the
        // suite (Close > SMA200, validated by v13 / Faber-Pulse rolling-window
        // walk-forward). The hypothesis: capitulation events in bear regimes are
        // dead-cat bounces that fail; capitulation events in bull regimes are
        // pullback-buys that work. The Faber gate operationalises that split.
        //
        // Risk plan unchanged from v22-long: ATR(14)×2 stop, 1.5R/3R ladder,
        // BE-after-TP1, 0.5% risk.
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV22rCapitulationFaber()
        {
            var bottom = new ConditionLeaf(
                Id: "v22rl-bottom",
                SignalDescriptorId: "TOP_BOTTOM_DETECTOR.Bottom Confirmed",
                Operator: LeafOperator.FiredWithin,
                WithinNBars: 2,
                Score: 1.0);

            var aboveSma = new ConditionLeaf(
                Id: "v22rl-above-sma200",
                SignalDescriptorId: "REGIME.AboveSma200",
                Operator: LeafOperator.GreaterThan,
                Value: 0.0,
                Score: 1.0);

            var root = new ConditionGroup(
                Id: "v22rl-root",
                Logic: LogicOperator.And,
                Children: new List<ConditionNode> { bottom, aboveSma });

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
                Id: LongV22rCapitulationFaberId,
                Name: "Capitulation Bottom + Trend Filter (Long) [v22r, deprecated]",
                Description:
                    "[DEPRECATED 2026-04-27 round 5] Walk-windows verdict on BTC 4h: " +
                    "high per-trade R (+1.03R) but trade count collapses to n=11 over " +
                    "9 years — quality without quantity. Faber gate is too restrictive " +
                    "on top of v22's existing bottom-20% gate. Prefer v23p (Pivots) for " +
                    "BTC/ETH 1d, v23r-Faber for BTC/ETH 4h, or bare v22 for BTC 1d. " +
                    "Kept in the seed library for reproducibility. " +
                    "Original spec: v22 (TOP_BOTTOM_DETECTOR.Bottom Confirmed within 2 " +
                    "bars) AND REGIME.AboveSma200 > 0 (price above 200-period SMA). " +
                    "REQUIRES: TopBottomDetector + Regime indicators loaded.",
                Side: OrderSide.Buy,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // v22r — Distribution Top + bear regime + positive funding (Short).
        //
        // The asymmetric counterpart, modelled exactly on v18-refined-short. v22's
        // raw Top Confirmed marker fails walk-forward across all 9 asset/TF combos
        // tested 2026-04-27 — no asset shows both halves positive at 1d. The v18
        // pattern (don't call tops in uptrends, only ride distribution in
        // confirmed bear regime where remaining longs still pay funding) is the
        // only short pattern in the suite that survives walk-forward. This spec
        // applies it to the v22 Top Confirmed marker.
        //
        // Risk plan: ATR(14)×1.5 stop + 1R/2R ladder, MinRR 1.0 — v18 conventions
        // for the faster bear rhythm.
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV22rDistributionBearFunded()
        {
            var top = new ConditionLeaf(
                Id: "v22rs-top",
                SignalDescriptorId: "TOP_BOTTOM_DETECTOR.Top Confirmed",
                Operator: LeafOperator.FiredWithin,
                WithinNBars: 2,
                Score: 1.0);

            var belowSma = new ConditionLeaf(
                Id: "v22rs-below-sma200",
                SignalDescriptorId: "REGIME.AboveSma200",
                Operator: LeafOperator.LessThan,
                Value: 0.0,
                Score: 1.0);

            var fundingPositive = new ConditionLeaf(
                Id: "v22rs-funding-positive",
                SignalDescriptorId: "FUNDING_RATE.Funding Rate",
                Operator: LeafOperator.GreaterThan,
                Value: 0.0,
                Score: 1.0);

            var root = new ConditionGroup(
                Id: "v22rs-root",
                Logic: LogicOperator.And,
                Children: new List<ConditionNode> { top, belowSma, fundingPositive });

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
                Id: ShortV22rDistributionBearFundedId,
                Name: "Distribution Top + Bear Regime + Funding (Short) [v22rs, deprecated]",
                Description:
                    "[DEPRECATED 2026-04-27 round 5] Walk-windows verdict: mechanism " +
                    "DEAD — fires zero times on BTC 1d/4h and ETH 1d/4h across all six " +
                    "windows. The conjunction 'top 20% of trailing 100-bar window' AND " +
                    "'price below SMA200' is logically rare by construction (if price is " +
                    "in a bear regime, the 100-bar high is from before the bear). Same " +
                    "negative result confirmed for v23rf SHORT (3 funding-gate variants, " +
                    "all 0 valid windows). Prefer v22-distribution-top for BTC 4h " +
                    "(only ROBUST short anywhere) or v23h-Hurst SHORT for everything else. " +
                    "Kept for reproducibility. " +
                    "Original spec: v22 + REGIME.AboveSma200 < 0 + FUNDING_RATE.Funding > 0. " +
                    "REQUIRES: TopBottomDetector + Regime + FundingRate indicators loaded.",
                Side: OrderSide.Sell,
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
        //   • highest CI-pass count of any cell in the 89-cell gate battery
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
                    "Fires any bull entry " +
                    "pulse (Cipher B Oversold Crossover / Triple Confluence Buy / Cipher A " +
                    "Buy Signal / Cipher SR Support, FiredWithin 5 bars) ONLY when Close > " +
                    "SMA(200) — the textbook Mebane Faber 2007 regime filter. Selected as the " +
                    "best-scoring cell of an 89-cell battery on ONE asset, so its rolling-window " +
                    "numbers are a maximum over 89 draws and not an out-of-sample estimate; it " +
                    "was never re-tested on a second asset, and its entry stack contains a " +
                    "Cipher SR leaf that still repaints. The Faber regime gate itself is " +
                    "separately supported — the trend baseline tests it without the Cipher " +
                    "layer. See the provenance record. Risk plan: ATR(14)×2 stop, 1.5R/3R TP ladder " +
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
        // Cross-asset rolling-window stress test (StrategyLab rolling-window, fresh
        // 4000-bar BTC + 3159-bar ETH + 3402-bar XRP + 3000-bar LTC daily snapshots,
        // 1500-bar windows × 250-bar step):
        //
        //   BTC: 90% windows positive, 20% windows pass strict CI, mean +0.32R, 24.5 tr/win
        //   ETH: 83% windows positive, 17% windows pass strict CI, mean +0.20R, 30.5 tr/win
        //   XRP: 71% windows positive,  0% windows pass strict CI, mean +0.12R, 26.6 tr/win
        //   LTC: 50% windows positive,  0% windows pass strict CI, mean +0.07R, 32.3 tr/win
        //
        // The MOST cross-asset-consistent cell in the entire 89-cell gate battery. Three
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
                    "The simplest strategy in the catalogue. Fires any bull entry pulse (Cipher B Oversold " +
                    "Crossover / Triple Confluence Buy / Cipher A Buy Signal / Cipher SR Support, " +
                    "FiredWithin 5 bars) with NO confluence filter at all. Its value is as the " +
                    "thing more elaborate stacks have to beat, and across thirteen iterations " +
                    "they did not — but the numbers behind that come from the same battery the " +
                    "cells were selected from, and the entry stack contains a Cipher SR leaf " +
                    "that still repaints. See the provenance record. Risk plan: ATR(14)×2 stop, 1.5R/3R TP ladder (50/50 partial), BE " +
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
