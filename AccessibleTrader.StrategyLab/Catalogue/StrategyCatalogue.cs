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
    /// <see cref="CatalogueProvenance"/> — <see cref="CatalogueProvenanceTests"/> fails the build
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
        public const string Version = "2026-08-01.2";

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

        // v16 / v16s / v17 — the Cipher SR trilogies: RETIRED 2026-08-01. The structural third
        // of the trilogy was tested and does not exist (SR proximity was a 15-bar lookahead;
        // structure labels tested indistinguishable from random), and the provider still
        // repaints, so they cannot be honestly backtested as written. The verdicts are kept in
        // CatalogueProvenance.Retired — the record is the part worth having.

        // v18 — Refined Short. The asymmetric answer to "isn't short just the opposite
        // of long?" — no, because crypto has structural upward drift, bear moves are
        // shorter and sharper than bull moves, and perp funding charges shorts in bull
        // regimes. v18 uses Hidden Bear Continuation (NOT bearish divergence which
        // killed v13s) gated by a confirmed bear regime and crowded-long funding.
        // Tighter ATR stop (×1.5) and faster TP ladder (1R/2R) to match the shorter
        // rhythm of bear moves.
        public const string ShortV18RefinedShortId = "builtin.short.v18-refined";

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

        // v23 — Cipher B Weekly Reversal (long + short). Built on the structural
        // observation that v22's single-bar event detector cannot fire on weekly
        // bars because aggregation (168 hourly bars → 1 weekly bar) blurs the
        // event spike out of the bar's volume/RSI/range. Cipher B's WaveTrend
        // oscillator is itself a smoothing operation, so its semantic survives
        // aggregation: a weekly Blue dot means "smoothed weekly momentum just
        // crossed up out of weekly oversold," which is a coherent statement.
        // Pairs the Blue/Red dot with a Bullish/Bearish Divergence OR-gate and
        // an Anchor Wave regime gate. Risk plan tuned to weekly cadence —
        // ATR×3 stops because weekly ATR is ~7× daily and a 2× stop gets noise-
        // tagged; 2R/4R TP ladder because weekly trends span months when they work.
        // The SHORT half was RETIRED 2026-08-01: negative expectancy on BTC 4h and daily.
        public const string LongV23CipherBWeeklyId  = "builtin.long.v23-cipherb-weekly";

        // v23r — Cipher B Weekly Reversal + Faber regime gate. The deeper-validation
        // refinement of v23. Same trigger (WT Cross Bull / Blue / Bull Div within 2)
        // and same Anchor regime gate, PLUS REGIME.AboveSma200 > 0 (long) or < 0
        // (short) so we only fire reversals on the side of the long-term trend.
        // Hypothesis: v23 base shows positive total P&L but marginal per-trade R
        // because counter-trend entries in deep bear markets eat the bull-regime
        // wins. Faber gate is the most empirically robust filter in the suite
        // (validated on v13, Faber-Pulse, BareBullPulse) — should lift per-trade R.
        // The SHORT half was RETIRED 2026-08-01: the regime gate did not rescue it either.
        public const string LongV23rCipherBFaberId  = "builtin.long.v23r-cipherb-faber";

        // v23rf — Cipher B Weekly Reversal short + funding-crowded contrarian gate.
        // The asymmetric short variant. v23-SHORT and v23r-SHORT both produced
        // negative expectancy across BTC 4h/1d. The only short pattern that has
        // ever worked on BTC is v18-refined-short's "fade rallies in confirmed
        // bear regime when remaining longs are still paying funding." This applies
        // the same gate to v23: bear trigger AND price < SMA200 AND funding > 0.
        public const string ShortV23rfCipherBFundingId = "builtin.short.v23rf-cipherb-funding";

        // v23p — Cipher B Reversal + Pivot zone gate (LONG). Promoted from
        // StrategyBatteryCommand cell after round-4 rolling-window proved this is the
        // closest-to-ROBUST cell anywhere: ETH 1d 100% positive / 33% CI / +0.523R
        // across 6 windows, BTC 1d 73% / 13% CI / +0.294R. Adds the Pivot Zone
        // gate (price near classic S1/S2/S3/CamL3/CamL4) to v23 base. Pivots are
        // institutional reference levels — firing reversals AT support is
        // structurally meaningful, not a fitted heuristic.
        public const string LongV23pCipherBPivotsId = "builtin.long.v23p-cipherb-pivots";

        // v23h — Cipher B Reversal + Hurst regime gate (LONG and SHORT). Promoted
        // from rolling-window cells: BTC 1d v23+HURST LONG = 71% / 14% / +0.411R
        // (65% better per-trade R than v23 base on the same TF). KAS 4h
        // v23+HURST SHORT = 62% / +0.207R (240% better than base). The gate is
        // simple: fire reversals only when Hurst < 0.45 (mean-reverting regime
        // where reversals SHOULD outperform) and skip in trending regimes where
        // reversals get run over.
        public const string LongV23hCipherBHurstId  = "builtin.long.v23h-cipherb-hurst";
        public const string ShortV23hCipherBHurstId = "builtin.short.v23h-cipherb-hurst";

        // v23a — Cipher B Reversal + AVWAP soft-bias gate. Promoted from
        // StrategyBatteryCommand cell after round 6 rolling-window: ETH 1d 100% positive
        // / +0.277R / 22.7 trades; BTC 1d 80% / 7% CI / +0.203R. AVWAP soft bias
        // is the looser version (close above EITHER anchor) which surfaces more
        // signal than the strict version (close above BOTH).
        public const string LongV23aCipherBAvwapId = "builtin.long.v23a-cipherb-avwap";

        // v23or — Cipher B Reversal + (AVWAP Bias Soft OR Pivot Support) gate.
        // Promoted from rolling-window round 8 (2026-04-27 evening 11): ETH 1d 100%
        // positive / 0% CI / +0.335R / 25.3 trades; BTC 1d 73% / 7% CI / +0.188R /
        // 24.3 trades. Higher trade count than either v23a or v23p individually
        // (v23a: 22.7 / v23p ETH: 14). Per-trade R sits between the two pure
        // gates. Use when you want broader coverage on liquid majors at daily;
        // use v23p when you want peak conviction (per-trade R champion).
        public const string LongV23orCipherBOrConfId = "builtin.long.v23or-cipherb-orconf";

        // Trend Baseline — the "boring institutional strategy" every fancier spec
        // must beat. Faber cross entry (price crosses above SMA200), wide ATR
        // stop, single distant target, ATR-trail after TP1 so the trend runs.
        // 2026-07-13 cross-asset study (TSMOM-12m / MA-10m / vol-target, monthly,
        // era-sliced): crypto is where it shines (BTC vol-targeted trend Sharpe
        // 1.19 vs 0.80 hold, maxDD 23% vs 83%); on indices/gold it matches
        // buy-and-hold Sharpe with 2-3x smaller drawdowns (crash insurance); on
        // single secular-growth names holding wins; FX long-only has no edge.
        public const string LongTrendBaselineId = "builtin.long.trend-baseline";

        // v24 — Cycle Low Reversal: v23 trigger family gated by a CONFIRMED daily
        // cycle low (Loukas DCL) within 5 bars. The cycle-clock alternative (raw
        // "in timing window" gate) already tested NEUTRAL per the Loukas provider's
        // own research notes — confirmation is the differentiated hypothesis.
        public const string LongV24CycleLowReversalId = "builtin.long.v24-cycle-low-reversal";

        // v23c — Cipher B Reversal + Faber + COT not-crowded gates (LONG, metals /
        // equity indices, daily). From the 2026-07-13 gate battery (10 assets,
        // era-sliced): the Faber bull-regime gate was the strongest single filter
        // on this trigger for indices and metals (SPY 91% hit t=4.9, QQQ 90%,
        // gold 84% t=4.6, silver 86%), and stacking the COT not-crowded gate on
        // top produced the best cell in the battery (QQQ Faber+COT: 94% hit,
        // +5.01%/20d, t=5.65, n=16). NOTE the per-asset validity: do NOT use on
        // BTC (every gate REDUCED the edge there — ungated is best; CME
        // positioning is basis-trade contaminated) or FX (specs are informed
        // trend flow); on single stocks the Faber gate was neutral-to-negative.
        public const string LongV23cCipherBCotId = "builtin.long.v23c-cipherb-cot";

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
            yield return BuildPulseReversalLong();
            yield return BuildV13BlueDotSma200();
            yield return BuildV14HiddenBullSma200();
            yield return BuildV15BlueDotBullDiv();
            yield return BuildV18RefinedShort();
            yield return BuildV22CapitulationBottom();
            yield return BuildV22DistributionTop();
            yield return BuildV22rCapitulationFaber();
            yield return BuildV22rDistributionBearFunded();
            yield return BuildV23CipherBWeeklyLong();
            yield return BuildV23rCipherBFaberLong();
            yield return BuildV23rfCipherBFundingShort();
            yield return BuildV23pCipherBPivotsLong();
            yield return BuildV23hCipherBHurstLong();
            yield return BuildV23hCipherBHurstShort();
            yield return BuildV23aCipherBAvwapLong();
            yield return BuildV23orCipherBOrConfLong();
            yield return BuildTrendBaselineLong();
            yield return BuildV23cCipherBCotLong();
            yield return BuildV24CycleLowReversalLong();
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // v24 — Cycle Low Reversal (LONG, BTC daily). Wave 4 of the 2026-07 plan.
        // Entry EVENT = a confirmed daily cycle low (Loukas DCL within 2); momentum
        // EVIDENCE = any v23 trigger within 8 (wider than v23's 2 because DCL
        // confirmation lags the low by the swing lookback). Iteration record from
        // the 2026-07-17 lab session — kept here because rejected variants are as
        // informative as the survivor:
        //   • trigger-within-2 + DCL-within-5:  2 trades / 7 yrs (temporal mismatch)
        //   • + Anchor Wave < 0 depth gate:     H1 -0.85R — REMOVED (by confirmation
        //     time the anchor has often recovered; the gate deleted the good half)
        //   • swing-low(12) structural stop:    H2 -0.10R — cycle-low RETESTS stop it
        //     out at -1R; ATR(14)x3 fixed it (H2 -0.10 → +0.31)
        //   • DCL-only (no cipher trigger):     +0.33/+0.18 — trigger improves the
        //     WEAKER half (+0.18 → +0.31), so it stays per the era-robustness rule
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV24CycleLowReversalLong()
        {
            var wtCrossBull = new ConditionLeaf(
                Id: "v24-wtx", SignalDescriptorId: "CIPHER_B.WaveTrend Cross Bull",
                Operator: LeafOperator.FiredWithin, WithinNBars: 8, Score: 1.0);
            var blueDot = new ConditionLeaf(
                Id: "v24-blue", SignalDescriptorId: "CIPHER_B.Oversold Crossover",
                Operator: LeafOperator.FiredWithin, WithinNBars: 8, Score: 1.0);
            var bullDiv = new ConditionLeaf(
                Id: "v24-bulldiv", SignalDescriptorId: "CIPHER_B.Bullish Divergence",
                Operator: LeafOperator.FiredWithin, WithinNBars: 8, Score: 1.0);

            // Momentum evidence near the low: any v23 trigger within the last 8 bars.
            // The window is wider than v23's 2 because DCL confirmation LAGS the low
            // by the swing lookback — the Cipher event fires AT the low, the cycle
            // confirmation arrives days later, and the entry is on the confirmation.
            var trigger = new ConditionGroup(
                Id: "v24-trigger", Logic: LogicOperator.Or,
                Children: new List<ConditionNode> { wtCrossBull, blueDot, bullDiv });

            // THE entry event: a confirmed daily cycle low within the last 2 bars.
            var dclConfirmed = new ConditionLeaf(
                Id: "v24-dcl", SignalDescriptorId: "LOUKAS_CYCLES.DCL Confirmed",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);

            var root = new ConditionGroup(
                Id: "v24-root", Logic: LogicOperator.And,
                Children: new List<ConditionNode> { trigger, dclConfirmed });

            // ATR stop, NOT the structurally-appealing swing-low stop: confirmed
            // cycle lows get RETESTED, and a stop at the low converts every retest
            // into -1R (lab: swing stop H2 -0.10R vs ATR +0.31R on identical entries).
            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 3.0);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 2.0, ClosePortion: 0.40),
                // Distant rung: the ATR trail after TP1 is the real exit — ride the up-leg.
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 8.0, ClosePortion: 0.60),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);
            var risk = new RiskPlan(
                Stop: stop, TpLadder: tpLadder, Sizing: sizing, Entry: entry,
                MinRewardRiskRatio: 1.5,
                StopAdjust: StopAdjustOnTp1.TrailByAtr,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: LongV24CycleLowReversalId,
                Name: "Cycle Low Reversal — DCL + Cipher B (Long, crypto daily) [v24]",
                Description:
                    "Enters in the first days of a NEW daily cycle: a Loukas daily-cycle-low " +
                    "CONFIRMATION within 2 bars AND any v23 reversal trigger (WT Cross Bull / " +
                    "Blue dot / Bull Divergence) within 8. LAB WALK-FORWARD 2026-07-17, BTC " +
                    "daily 2011-2026, H1/H2: POSITIVE BOTH HALVES — H1 +0.25R (35 trades, " +
                    "62.9% WR, PF 2.1), H2 +0.31R (35 trades, 57.1% WR, PF 2.1). Six-window " +
                    "slice: 5 of 6 windows positive, mean +0.49R — the one weak window is the " +
                    "MOST RECENT (2024-2026: -0.17R on 9 trades), so treat the current regime " +
                    "with humility. CROSS-ASSET, honestly: ETH positive both halves but thin " +
                    "in H2 (+0.09R); LTC fails H2 (-0.35R); SOL has too little history to " +
                    "judge — this is a BTC-DAILY strategy, per the Bitcoin-native Loukas " +
                    "framework. Design negatives kept on record: the raw in-window clock gate " +
                    "tests neutral (provider notes), an Anchor-depth gate DELETED the good " +
                    "half, and the structurally-pretty swing-low stop lost to ATR because " +
                    "cycle lows get retested. Versus Trend Baseline on BTC (+2.02/+0.87R): " +
                    "the baseline wins per-trade — v24 is its frequent, high-win-rate " +
                    "COMPLEMENT (about one trade per cycle month vs a handful per decade), " +
                    "not its replacement. REQUIRES: Loukas Cycles + Cipher B loaded, DAILY " +
                    "bars. Risk: ATR(14)x3 stop, 2R banks 40%, ATR trail rides the up-leg, " +
                    "0.5% risk per trade.",
                Side: OrderSide.Buy,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
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
        // v23c — Cipher B Reversal + COT not-crowded gate (gold / S&P daily long).
        // See LongV23cCipherBCotId docs for the positioning-study evidence.
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV23cCipherBCotLong()
        {
            var blueDot = new ConditionLeaf(
                Id: "v23cl-blue", SignalDescriptorId: "CIPHER_B.Oversold Crossover",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);
            var bullDiv = new ConditionLeaf(
                Id: "v23cl-bulldiv", SignalDescriptorId: "CIPHER_B.Bullish Divergence",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);
            var wtCrossBull = new ConditionLeaf(
                Id: "v23cl-wtx", SignalDescriptorId: "CIPHER_B.WaveTrend Cross Bull",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);

            var trigger = new ConditionGroup(
                Id: "v23cl-trigger", Logic: LogicOperator.Or,
                Children: new List<ConditionNode> { wtCrossBull, blueDot, bullDiv });

            var anchorBear = new ConditionLeaf(
                Id: "v23cl-anchor", SignalDescriptorId: "CIPHER_B.Anchor Wave",
                Operator: LeafOperator.LessThan, Value: 0.0, Score: 1.0);

            // Bull-regime gate — the strongest single filter for this trigger on
            // indices and metals in the 2026-07 battery.
            var faberBull = new ConditionLeaf(
                Id: "v23cl-faber", SignalDescriptorId: "REGIME.AboveSma200",
                Operator: LeafOperator.GreaterThan, Value: 0.0, Score: 1.0);

            // Funds NOT at a crowded-long extreme — stacks with Faber for the best
            // cell in the battery (QQQ 94% hit). NaN (COT indicator absent,
            // unmapped symbol, or z warmup) evaluates false, so the spec simply
            // never fires without its data — same contract as the funding-gated
            // v23rf.
            var cotNotCrowded = new ConditionLeaf(
                Id: "v23cl-cot", SignalDescriptorId: "COT_POSITIONING.Positioning Z-Score",
                Operator: LeafOperator.LessThan, Value: 1.5, Score: 1.0);

            var root = new ConditionGroup(
                Id: "v23cl-root", Logic: LogicOperator.And,
                Children: new List<ConditionNode> { trigger, anchorBear, faberBull, cotNotCrowded });

            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 3.0);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 2.0, ClosePortion: 0.50),
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 4.0, ClosePortion: 0.50),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);
            var risk = new RiskPlan(
                Stop: stop, TpLadder: tpLadder, Sizing: sizing, Entry: entry,
                MinRewardRiskRatio: 2.0,
                StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: LongV23cCipherBCotId,
                Name: "Cipher Reversal + Trend + COT Gates — Metals/Indices Daily (Long) [v23c]",
                Description:
                    "v23 base trigger (WT Cross Bull / Blue dot / Bull Divergence within 2) " +
                    "AND Anchor Wave < 0 AND price > SMA200 AND fund positioning NOT crowded " +
                    "long (COT 26-week z < 1.5). Gate battery 2026-07 (10 assets, era-sliced): " +
                    "Selected as the best cell of a 10-asset gate battery, which makes its headline " +
                    "numbers a maximum over many draws rather than an estimate; the COT half " +
                    "later tested as carrying no forward information, leaving the regime gate " +
                    "doing the work. See its provenance record for the verdict. " +
                    "ASSET-SPECIFIC: metals and equity indices on DAILY bars — every " +
                    "gate HURT on BTC (fires 0 trades there by design), FX is inverted, single " +
                    "stocks neutral. REQUIRES: Cipher B + Regime Filter + COT Positioning " +
                    "loaded. Risk: ATR(14)x3 stop, 2R/4R ladder, BE after TP1, 0.5% risk.",
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
                Name: "Trend Continuation — Hidden Bull + SMA200 (Long) [v14]",
                Description:
                    "Cipher B Hidden Bull Continuation AND price above SMA(200). " +
                    "Evaluated on ONE half of one asset — the half the signal was " +
                    "selected on — and never run on the other. The SMA200 gate aligns the " +
                    "signal's intent (continue an uptrend) with regime confirmation. REQUIRES: Cipher B and Regime " +
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
                Name: "Reversal Confluence — Blue Dot + Divergence (Long) [v15]",
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
        // v18 — Refined Short. Hidden Bear Continuation + confirmed bear regime +
        // crowded-long funding. Uses a CONTINUATION signal (not a divergence) so it
        // rides bear moves rather than trying to call tops in uptrends like v13s did.
        // Tighter ATR stop and faster TP ladder to match bear-move rhythm.
        //
        // NOTE: Uses Core FUNDING_RATE, which was repointed from OKX (11-day depth) to
        // BinanceVision (6-year depth) in the 2026-04-11 Core-indicator sweep. Live and
        // lab paths now share the same deep-history source — the former StrategyLab-only
        // BNVISION_FUNDING alias has been folded back into FUNDING_RATE.Funding Rate.
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
                SignalDescriptorId: "FUNDING_RATE.Funding Rate",
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
                Name: "Bear-Rally Fade — Hidden Bear + Bear Regime + Crowded Funding (Short) [v18]",
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

        // ─────────────────────────────────────────────────────────────────────────────
        // v23 — Cipher B Weekly Reversal (long).
        //
        // The structural answer to v22's weekly-aggregation problem. v22 looks for a
        // single-bar event spike (volume z, range z, RSI extreme, momentum flip) —
        // weekly bars AVERAGE those spikes out, so v22 produces zero capitulation
        // hits on weekly without aggressive gate relaxation. Cipher B's WaveTrend is
        // itself a smoothing operation, so its semantic SURVIVES aggregation: a
        // weekly Blue dot means "smoothed weekly momentum just crossed up out of
        // weekly oversold," which is coherent at any TF.
        //
        // Trigger: Blue dot (Oversold Crossover) within 2 bars OR Bullish Divergence
        // within 2 bars. The OR-gate gives the indicator two ways to mark a weekly
        // capitulation — the cross is the moment, the divergence is the structural
        // confirmation that the cross isn't noise.
        //
        // Regime: Anchor Wave < 0 ensures the broader (5× period) WaveTrend is in
        // the bear half. Without this gate the strategy fires on every Blue dot,
        // including counter-trend pullbacks in confirmed uptrends — those have
        // edge but not weekly-magnitude edge.
        //
        // Risk plan: ATR(14)×3 stop because weekly ATR is ~7× daily ATR — a 2× stop
        // gets immediately noise-tagged on weekly. 2R/4R TP ladder because weekly
        // trends span months when they work; the 1.5R/3R ladder appropriate for daily
        // gives away too much of the move. BE-after-TP1 preserved.
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV23CipherBWeeklyLong()
        {
            // Diagnostic on BTC 1w (2026-04-27 evening): the strict OS-confirmed Blue
            // dot fires only 2× across 8 weekly years; Bull Divergence 0×; bare
            // WaveTrend Cross Bull fires 15× per half (much more frequent and the
            // primary directional signal at the weekly cadence). Including the bare
            // cross in the trigger group gives the strategy enough sample to evaluate
            // — quality is restored by the Anchor regime gate further down.
            var blueDot = new ConditionLeaf(
                Id: "v23l-blue",
                SignalDescriptorId: "CIPHER_B.Oversold Crossover",
                Operator: LeafOperator.FiredWithin,
                WithinNBars: 2,
                Score: 1.0);

            var bullDiv = new ConditionLeaf(
                Id: "v23l-bulldiv",
                SignalDescriptorId: "CIPHER_B.Bullish Divergence",
                Operator: LeafOperator.FiredWithin,
                WithinNBars: 2,
                Score: 1.0);

            var wtCrossBull = new ConditionLeaf(
                Id: "v23l-wtx",
                SignalDescriptorId: "CIPHER_B.WaveTrend Cross Bull",
                Operator: LeafOperator.FiredWithin,
                WithinNBars: 2,
                Score: 1.0);

            // Trigger group: bare WT cross OR the confirmed Blue dot OR Bull Divergence.
            var trigger = new ConditionGroup(
                Id: "v23l-trigger",
                Logic: LogicOperator.Or,
                Children: new List<ConditionNode> { wtCrossBull, blueDot, bullDiv });

            var anchorBear = new ConditionLeaf(
                Id: "v23l-anchor",
                SignalDescriptorId: "CIPHER_B.Anchor Wave",
                Operator: LeafOperator.LessThan,
                Value: 0.0,
                Score: 1.0);

            var root = new ConditionGroup(
                Id: "v23l-root",
                Logic: LogicOperator.And,
                Children: new List<ConditionNode> { trigger, anchorBear });

            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 3.0);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 2.0, ClosePortion: 0.50),
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 4.0, ClosePortion: 0.50),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);
            var risk = new RiskPlan(
                Stop: stop, TpLadder: tpLadder, Sizing: sizing, Entry: entry,
                MinRewardRiskRatio: 2.0,
                StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: LongV23CipherBWeeklyId,
                Name: "Cipher Reversal — Universal (Long) [v23]",
                Description:
                    "Weekly-targeted long built on Cipher B's oscillator-based capitulation " +
                    "rather than v22's single-bar event score. Fires when EITHER an Oversold " +
                    "Crossover (Blue dot) OR a Bullish Divergence happened within the last 2 " +
                    "bars AND the Anchor Wave (5× period WT) is bearish (< 0). The structural " +
                    "rationale: WaveTrend is itself a smoothing operation, so its OS/OB " +
                    "semantic survives aggregation from intra-week into weekly — unlike v22's " +
                    "event score which gets averaged out. REQUIRES: Cipher B loaded. Risk: " +
                    "ATR(14)×3 stop (weekly ATR ≈ 7× daily — tight stops noise-tag immediately), " +
                    "2R/4R TP ladder (weekly trends span months when they work), BE after TP1, " +
                    "0.5% risk per trade.",
                Side: OrderSide.Buy,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }


        // ─────────────────────────────────────────────────────────────────────────────
        // v23r — Cipher B Weekly Reversal + Faber regime (long).
        //
        // Same trigger as v23-LONG (WT Cross Bull OR Blue dot OR Bull Divergence
        // within 2 bars) AND Anchor Wave < 0 AND price > SMA200. The Faber gate
        // restricts entries to bull regime — we only buy reversals on the side of
        // the long-term trend. Tested via empirical validation across v13, Faber-
        // Pulse, and BareBullPulse — the most cross-asset-validated filter in the
        // suite. Hypothesis: v23 base shows positive total P&L but marginal per-
        // trade R because counter-trend entries in deep bear markets eat the
        // bull-regime wins. Filtering them out should lift expectancy.
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV23rCipherBFaberLong()
        {
            var blueDot = new ConditionLeaf(
                Id: "v23rl-blue", SignalDescriptorId: "CIPHER_B.Oversold Crossover",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);
            var bullDiv = new ConditionLeaf(
                Id: "v23rl-bulldiv", SignalDescriptorId: "CIPHER_B.Bullish Divergence",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);
            var wtCrossBull = new ConditionLeaf(
                Id: "v23rl-wtx", SignalDescriptorId: "CIPHER_B.WaveTrend Cross Bull",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);

            var trigger = new ConditionGroup(
                Id: "v23rl-trigger", Logic: LogicOperator.Or,
                Children: new List<ConditionNode> { wtCrossBull, blueDot, bullDiv });

            var anchorBear = new ConditionLeaf(
                Id: "v23rl-anchor", SignalDescriptorId: "CIPHER_B.Anchor Wave",
                Operator: LeafOperator.LessThan, Value: 0.0, Score: 1.0);

            var faberBull = new ConditionLeaf(
                Id: "v23rl-faber", SignalDescriptorId: "REGIME.AboveSma200",
                Operator: LeafOperator.GreaterThan, Value: 0.0, Score: 1.0);

            var root = new ConditionGroup(
                Id: "v23rl-root", Logic: LogicOperator.And,
                Children: new List<ConditionNode> { trigger, anchorBear, faberBull });

            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 3.0);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 2.0, ClosePortion: 0.50),
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 4.0, ClosePortion: 0.50),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);
            var risk = new RiskPlan(
                Stop: stop, TpLadder: tpLadder, Sizing: sizing, Entry: entry,
                MinRewardRiskRatio: 2.0,
                StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: LongV23rCipherBFaberId,
                Name: "Cipher Reversal + Trend Filter — BTC/ETH 4h (Long) [v23r]",
                Description:
                    "v23 base trigger (WT Cross Bull / Blue dot / Bull Divergence within 2) " +
                    "AND Anchor Wave < 0 AND price > SMA200. The Faber regime gate restricts " +
                    "entries to bull regime — same gate that validated on v13, Faber-Pulse, " +
                    "and BareBullPulse (most cross-asset-validated filter in the suite). " +
                    "REQUIRES: Cipher B + Regime Filter loaded. Risk: ATR(14)×3 stop, 2R/4R " +
                    "TP ladder, BE after TP1, 0.5% risk per trade.",
                Side: OrderSide.Buy,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }


        // ─────────────────────────────────────────────────────────────────────────────
        // v23rf — Cipher B Weekly Reversal short + funding-crowded contrarian gate.
        //
        // v23-SHORT and v23r-SHORT both produced negative expectancy across
        // BTC 4h/1d. The only short pattern that has ever worked on BTC is
        // v18-refined-short's "fade rallies in confirmed bear regime when
        // remaining longs are still paying funding." Apply the same gate to v23:
        // bear trigger AND price < SMA200 AND funding > 0 (longs still paying).
        // The funding gate is the contrarian flag — when most participants think
        // the rally has more legs, paying to be long, the asymmetric edge favors
        // the fade. Tighter ATR×1.5 stop because crypto bear moves are fast.
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV23rfCipherBFundingShort()
        {
            var redDot = new ConditionLeaf(
                Id: "v23rfs-red", SignalDescriptorId: "CIPHER_B.Overbought Crossover",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);
            var bearDiv = new ConditionLeaf(
                Id: "v23rfs-beardiv", SignalDescriptorId: "CIPHER_B.Bearish Divergence",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);
            var wtCrossBear = new ConditionLeaf(
                Id: "v23rfs-wtx", SignalDescriptorId: "CIPHER_B.WaveTrend Cross Bear",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);

            var trigger = new ConditionGroup(
                Id: "v23rfs-trigger", Logic: LogicOperator.Or,
                Children: new List<ConditionNode> { wtCrossBear, redDot, bearDiv });

            var anchorBull = new ConditionLeaf(
                Id: "v23rfs-anchor", SignalDescriptorId: "CIPHER_B.Anchor Wave",
                Operator: LeafOperator.GreaterThan, Value: 0.0, Score: 1.0);

            var faberBear = new ConditionLeaf(
                Id: "v23rfs-faber", SignalDescriptorId: "REGIME.AboveSma200",
                Operator: LeafOperator.LessThan, Value: 0.0, Score: 1.0);

            var fundingCrowded = new ConditionLeaf(
                Id: "v23rfs-fund", SignalDescriptorId: "BNVISION_FUNDING.Funding",
                Operator: LeafOperator.GreaterThan, Value: 0.0, Score: 1.0);

            var root = new ConditionGroup(
                Id: "v23rfs-root", Logic: LogicOperator.And,
                Children: new List<ConditionNode> { trigger, anchorBull, faberBear, fundingCrowded });

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
                Id: ShortV23rfCipherBFundingId,
                Name: "Cipher Reversal + Bear + Crowded Funding (Short) [v23rf, negative edge]",
                Description:
                    "v23 short trigger AND Anchor Wave > 0 AND price < SMA200 AND funding > 0. " +
                    "The only short setup that has historically worked on BTC: fade rallies in " +
                    "confirmed bear regime when remaining longs are still paying funding. " +
                    "Mirrors v18-refined-short pattern. Tight ATR×1.5 stop / 1R/2R ladder " +
                    "because crypto bear moves are fast. REQUIRES: Cipher B + Regime Filter + " +
                    "BNVISION_FUNDING loaded on the chart.",
                Side: OrderSide.Sell,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // v23p — Cipher B Reversal + Pivot zone (LONG).
        //
        // Promoted from StrategyBatteryCommand cell (2026-04-27 round 4). Best
        // single result anywhere: ETH 1d 100% positive / 33% CI / +0.523R / 6
        // windows. BTC 1d 73% / 13% CI / +0.294R. Pivot zone is the bar's
        // proximity (in ATR units) to classic floor-trader S1/S2/S3 or Camarilla
        // L3/L4 levels — institutional reference levels that show up across
        // equities, forex, commodities, and crypto. Gating reversals to fire AT
        // support is structurally meaningful, not a fitted heuristic.
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV23pCipherBPivotsLong()
        {
            var blueDot = new ConditionLeaf(
                Id: "v23pl-blue", SignalDescriptorId: "CIPHER_B.Oversold Crossover",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);
            var bullDiv = new ConditionLeaf(
                Id: "v23pl-bulldiv", SignalDescriptorId: "CIPHER_B.Bullish Divergence",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);
            var wtCrossBull = new ConditionLeaf(
                Id: "v23pl-wtx", SignalDescriptorId: "CIPHER_B.WaveTrend Cross Bull",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);

            var trigger = new ConditionGroup(
                Id: "v23pl-trigger", Logic: LogicOperator.Or,
                Children: new List<ConditionNode> { wtCrossBull, blueDot, bullDiv });

            var anchorBear = new ConditionLeaf(
                Id: "v23pl-anchor", SignalDescriptorId: "CIPHER_B.Anchor Wave",
                Operator: LeafOperator.LessThan, Value: 0.0, Score: 1.0);

            var atSupport = new ConditionLeaf(
                Id: "v23pl-pivot", SignalDescriptorId: "PIVOTS.Pivot Zone",
                Operator: LeafOperator.LessThan, Value: -0.5, Score: 1.0);

            var root = new ConditionGroup(
                Id: "v23pl-root", Logic: LogicOperator.And,
                Children: new List<ConditionNode> { trigger, anchorBear, atSupport });

            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 3.0);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 2.0, ClosePortion: 0.50),
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 4.0, ClosePortion: 0.50),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);
            var risk = new RiskPlan(
                Stop: stop, TpLadder: tpLadder, Sizing: sizing, Entry: entry,
                MinRewardRiskRatio: 2.0,
                StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: LongV23pCipherBPivotsId,
                // The ★ that used to end this name was the last surviving piece of the
                // per-asset recommender — a rank marker baked into the data itself.
                Name: "Cipher Reversal at Pivot Support — BTC/ETH Daily (Long) [v23p]",
                Description:
                    "v23 base trigger (WT Cross Bull / Blue / Bull Divergence within 2) " +
                    "AND Anchor Wave < 0 AND PIVOTS.Pivot Zone < -0.5 (price within ATR-" +
                    "tolerance of classic S1/S2/S3 or Camarilla L3/L4 support). Promoted for " +
                    "being the best-scoring cell of an 89-cell battery, so its headline numbers " +
                    "are a maximum over 89 draws rather than an estimate — it needs a fresh " +
                    "out-of-sample asset before it means anything. See the provenance record. REQUIRES: Cipher B + Pivot Levels indicators loaded. Risk: " +
                    "ATR(14)×3 stop, 2R/4R TP ladder, BE after TP1, 0.5% risk per trade. " +
                    "Best on liquid majors (BTC/ETH) at the daily timeframe.",
                Side: OrderSide.Buy,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // v23h — Cipher B Reversal + Hurst regime gate (LONG).
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV23hCipherBHurstLong()
        {
            var blueDot = new ConditionLeaf(
                Id: "v23hl-blue", SignalDescriptorId: "CIPHER_B.Oversold Crossover",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);
            var bullDiv = new ConditionLeaf(
                Id: "v23hl-bulldiv", SignalDescriptorId: "CIPHER_B.Bullish Divergence",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);
            var wtCrossBull = new ConditionLeaf(
                Id: "v23hl-wtx", SignalDescriptorId: "CIPHER_B.WaveTrend Cross Bull",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);

            var trigger = new ConditionGroup(
                Id: "v23hl-trigger", Logic: LogicOperator.Or,
                Children: new List<ConditionNode> { wtCrossBull, blueDot, bullDiv });

            var anchorBear = new ConditionLeaf(
                Id: "v23hl-anchor", SignalDescriptorId: "CIPHER_B.Anchor Wave",
                Operator: LeafOperator.LessThan, Value: 0.0, Score: 1.0);

            var meanReverting = new ConditionLeaf(
                Id: "v23hl-hurst", SignalDescriptorId: "HURST.Hurst",
                Operator: LeafOperator.LessThan, Value: 0.45, Score: 1.0);

            var root = new ConditionGroup(
                Id: "v23hl-root", Logic: LogicOperator.And,
                Children: new List<ConditionNode> { trigger, anchorBear, meanReverting });

            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 3.0);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 2.0, ClosePortion: 0.50),
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 4.0, ClosePortion: 0.50),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);
            var risk = new RiskPlan(
                Stop: stop, TpLadder: tpLadder, Sizing: sizing, Entry: entry,
                MinRewardRiskRatio: 2.0,
                StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: LongV23hCipherBHurstId,
                Name: "Cipher Reversal in Mean-Reverting Regime (Long) [v23h]",
                Description:
                    "v23 base trigger AND Anchor Wave < 0 AND HURST.Hurst < 0.45 " +
                    "(mean-reverting regime). The gate has a reason to exist beyond its numbers — " +
                    "only fire reversals where the regime mean-reverts — which is the argument " +
                    "for testing it properly rather than trusting the battery cell it was " +
                    "promoted from. Separately, Hurst tested useless as a cross-asset " +
                    "classifier (0.57-0.60 on all 17 combinations). See the provenance record. REQUIRES: Cipher B + Hurst Exponent indicators. " +
                    "Risk: ATR×3 stop, 2R/4R ladder.",
                Side: OrderSide.Buy,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // v23h — Cipher B Reversal + Hurst regime gate (SHORT).
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV23hCipherBHurstShort()
        {
            var redDot = new ConditionLeaf(
                Id: "v23hs-red", SignalDescriptorId: "CIPHER_B.Overbought Crossover",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);
            var bearDiv = new ConditionLeaf(
                Id: "v23hs-beardiv", SignalDescriptorId: "CIPHER_B.Bearish Divergence",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);
            var wtCrossBear = new ConditionLeaf(
                Id: "v23hs-wtx", SignalDescriptorId: "CIPHER_B.WaveTrend Cross Bear",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);

            var trigger = new ConditionGroup(
                Id: "v23hs-trigger", Logic: LogicOperator.Or,
                Children: new List<ConditionNode> { wtCrossBear, redDot, bearDiv });

            var anchorBull = new ConditionLeaf(
                Id: "v23hs-anchor", SignalDescriptorId: "CIPHER_B.Anchor Wave",
                Operator: LeafOperator.GreaterThan, Value: 0.0, Score: 1.0);

            var meanReverting = new ConditionLeaf(
                Id: "v23hs-hurst", SignalDescriptorId: "HURST.Hurst",
                Operator: LeafOperator.LessThan, Value: 0.45, Score: 1.0);

            var root = new ConditionGroup(
                Id: "v23hs-root", Logic: LogicOperator.And,
                Children: new List<ConditionNode> { trigger, anchorBull, meanReverting });

            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 2.5);
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
                Id: ShortV23hCipherBHurstId,
                Name: "Cipher Reversal in Mean-Reverting Regime (Short) [v23hs]",
                // (continued below — see v23a builder for the new AVWAP seed)
                Description:
                    "v23 short trigger AND Anchor Wave > 0 AND HURST.Hurst < 0.45. Promoted from " +
                    "a battery cell measured on a single low-cap altcoin — the weakest evidence " +
                    "base of anything here, on the side of the market where every other short " +
                    "in this catalogue has failed. The idea is that the Hurst gate skips " +
                    "trending bull regimes where bear signals get steamrolled. " +
                    "REQUIRES: Cipher B + Hurst Exponent indicators. See the provenance record.",
                Side: OrderSide.Sell,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // v23a — Cipher B Reversal + AVWAP soft-bias gate (LONG).
        //
        // Promoted from StrategyBatteryCommand cell after round 6: ETH 1d 100% positive
        // / +0.277R / 22.7 trades; BTC 1d 80% / 7% / +0.203R. AVWAP soft-bias is the
        // looser version of the strict bias — accepts close above EITHER anchor
        // (high-anchor or low-anchor) rather than requiring above BOTH. This
        // surfaces materially more sample (avgTr 22.7 on ETH 1d vs 17.0 strict)
        // while preserving 100% positive-window rate on the empirical champion TF.
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV23aCipherBAvwapLong()
        {
            var blueDot = new ConditionLeaf(
                Id: "v23al-blue", SignalDescriptorId: "CIPHER_B.Oversold Crossover",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);
            var bullDiv = new ConditionLeaf(
                Id: "v23al-bulldiv", SignalDescriptorId: "CIPHER_B.Bullish Divergence",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);
            var wtCrossBull = new ConditionLeaf(
                Id: "v23al-wtx", SignalDescriptorId: "CIPHER_B.WaveTrend Cross Bull",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);

            var trigger = new ConditionGroup(
                Id: "v23al-trigger", Logic: LogicOperator.Or,
                Children: new List<ConditionNode> { wtCrossBull, blueDot, bullDiv });

            var anchorBear = new ConditionLeaf(
                Id: "v23al-anchor", SignalDescriptorId: "CIPHER_B.Anchor Wave",
                Operator: LeafOperator.LessThan, Value: 0.0, Score: 1.0);

            var avwapBull = new ConditionLeaf(
                Id: "v23al-avwap", SignalDescriptorId: "ANCHORED_VWAP.AVWAP Bias Soft",
                Operator: LeafOperator.GreaterThan, Value: 0.5, Score: 1.0);

            var root = new ConditionGroup(
                Id: "v23al-root", Logic: LogicOperator.And,
                Children: new List<ConditionNode> { trigger, anchorBear, avwapBull });

            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 3.0);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 2.0, ClosePortion: 0.50),
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 4.0, ClosePortion: 0.50),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);
            var risk = new RiskPlan(
                Stop: stop, TpLadder: tpLadder, Sizing: sizing, Entry: entry,
                MinRewardRiskRatio: 2.0,
                StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: LongV23aCipherBAvwapId,
                Name: "Cipher Reversal + AVWAP Bias — BTC/ETH Daily (Long) [v23a]",
                Description:
                    "Cipher B reversal trigger (WT Cross Bull / Blue / Bull Divergence within " +
                    "2) AND Anchor Wave < 0 AND AVWAP Bias Soft > 0.5 (close above either " +
                    "anchored-VWAP — institutional bull bias). Promoted from a battery cell, and " +
                    "the 'soft' variant was itself chosen over the strict one for firing more " +
                    "often — a second in-sample choice on top of the first. See the provenance " +
                    "record. REQUIRES: Cipher B + " +
                    "Anchored VWAP indicators loaded. Risk: ATR(14)×3 stop, 2R/4R TP ladder, " +
                    "BE after TP1, 0.5% risk per trade. Best on liquid majors at daily.",
                Side: OrderSide.Buy,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // v23or — Cipher B Reversal + (AVWAP Bias Soft OR Pivot Support) gate (LONG).
        //
        // Promoted from StrategyBatteryCommand cell after round 8 (2026-04-27 evening 11):
        // ETH 1d 100% positive / 0% CI / +0.335R / 25.3 trades; BTC 1d 73% / 7% CI /
        // +0.188R / 24.3 trades. Trade count is the highest of the v23a / v23p / v23or
        // family (v23or 25.3 > v23a 22.7 > v23p ETH 14.0). Per-trade R sits between
        // the two — broader coverage at the cost of peak conviction. The OR-gate
        // bridges two structurally different gate types: AVWAP is a price-relative
        // bias level (institutional anchoring) and Pivot Zone is HLC-derived
        // (classical pivot levels). Their disjunction broadens fire frequency
        // without breaking edge.
        // ─────────────────────────────────────────────────────────────────────────────
        private static StrategySpec BuildV23orCipherBOrConfLong()
        {
            var blueDot = new ConditionLeaf(
                Id: "v23orl-blue", SignalDescriptorId: "CIPHER_B.Oversold Crossover",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);
            var bullDiv = new ConditionLeaf(
                Id: "v23orl-bulldiv", SignalDescriptorId: "CIPHER_B.Bullish Divergence",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);
            var wtCrossBull = new ConditionLeaf(
                Id: "v23orl-wtx", SignalDescriptorId: "CIPHER_B.WaveTrend Cross Bull",
                Operator: LeafOperator.FiredWithin, WithinNBars: 2, Score: 1.0);

            var trigger = new ConditionGroup(
                Id: "v23orl-trigger", Logic: LogicOperator.Or,
                Children: new List<ConditionNode> { wtCrossBull, blueDot, bullDiv });

            var anchorBear = new ConditionLeaf(
                Id: "v23orl-anchor", SignalDescriptorId: "CIPHER_B.Anchor Wave",
                Operator: LeafOperator.LessThan, Value: 0.0, Score: 1.0);

            var avwapBull = new ConditionLeaf(
                Id: "v23orl-avwap", SignalDescriptorId: "ANCHORED_VWAP.AVWAP Bias Soft",
                Operator: LeafOperator.GreaterThan, Value: 0.5, Score: 1.0);
            var pivotSupport = new ConditionLeaf(
                Id: "v23orl-piv", SignalDescriptorId: "PIVOTS.Pivot Zone",
                Operator: LeafOperator.LessThan, Value: -0.5, Score: 1.0);

            var orGate = new ConditionGroup(
                Id: "v23orl-orgate", Logic: LogicOperator.Or,
                Children: new List<ConditionNode> { avwapBull, pivotSupport });

            var root = new ConditionGroup(
                Id: "v23orl-root", Logic: LogicOperator.And,
                Children: new List<ConditionNode> { trigger, anchorBear, orGate });

            var stop = new StopSource(Kind: StopSourceKind.AtrMultiple, AtrPeriod: 14, AtrMultiple: 3.0);
            var tpLadder = new List<TpLadderRung>
            {
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 2.0, ClosePortion: 0.50),
                new TpLadderRung(Kind: TargetSourceKind.RiskRewardMultiple, Multiple: 4.0, ClosePortion: 0.50),
            };
            var sizing = new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005);
            var entry  = new EntryTrigger(EntryTriggerKind.Immediate);
            var risk = new RiskPlan(
                Stop: stop, TpLadder: tpLadder, Sizing: sizing, Entry: entry,
                MinRewardRiskRatio: 2.0,
                StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
                NotionalEquity: 10000.0);

            return new StrategySpec(
                Id: LongV23orCipherBOrConfId,
                Name: "Cipher Reversal + AVWAP-or-Pivot Confluence — BTC/ETH Daily (Long) [v23or]",
                Description:
                    "Cipher B reversal trigger (WT Cross Bull / Blue / Bull Divergence within " +
                    "2) AND Anchor Wave < 0 AND (AVWAP Bias Soft > 0.5 OR Pivot Zone < -0.5). " +
                    "An OR of two gates fires more often than either alone and lands between " +
                    "them on per-trade quality, which is arithmetic rather than a finding; it is " +
                    "third-order selection from the same battery. See the provenance record. REQUIRES: " +
                    "Cipher B + Anchored VWAP + Pivots indicators loaded. Risk: ATR(14)×3 stop, " +
                    "2R/4R TP ladder, BE after TP1, 0.5% risk per trade. Best on BTC/ETH at 1d.",
                Side: OrderSide.Buy,
                Conditions: root,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow,
                IsAutoActivate: false);
        }
    }
}
