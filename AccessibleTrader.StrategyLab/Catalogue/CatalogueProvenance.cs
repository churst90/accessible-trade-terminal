using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.StrategyLab.Catalogue
{
    /// <summary>
    /// What we actually know about each spec in <see cref="StrategyCatalogue"/>.
    ///
    /// <para>
    /// This table is the point of the whole terminal/lab split. Thirty specs used to be written
    /// into every user's library with nothing to distinguish "survived a rolling-window stress
    /// test on two assets" from "seemed like a good idea in April". Every spec now carries its
    /// evidence, negative results included — a recorded "this was tested and it failed" is more
    /// valuable than a gap, because a gap invites someone to re-run it hopefully.
    /// </para>
    ///
    /// <para>Three caveats recur and are quoted into the individual verdicts they apply to:</para>
    /// <list type="number">
    ///   <item>
    ///     <b>Battery promotion is not out-of-sample.</b> Several v23 variants were promoted from
    ///     the 89-cell gate battery because they were the best cells. Choosing the best of N cells
    ///     is a decision made in-sample even when each individual cell was walked forward — the
    ///     rolling-window number for the winner is a maximum over 89 draws, not an estimate.
    ///   </item>
    ///   <item>
    ///     <b>Cipher SR repaints.</b> The SR-proximity edge was traced to a 15-bar lookahead. It
    ///     was corrected inside <c>ConfluenceCommand</c>, but the PROVIDER still repaints, so any
    ///     backtest of a spec whose entry stack contains a CIPHER_SR leaf is optimistic by an
    ///     unmeasured amount. Seven specs here are in that position.
    ///   </item>
    ///   <item>
    ///     <b>The Cipher confluence family as a whole walked forward to break-even</b> across
    ///     eight versions. A single variant that looks better than the rest is, by default, the
    ///     upper tail of that family rather than a discovery.
    ///   </item>
    /// </list>
    /// </summary>
    public static class CatalogueProvenance
    {
        private static readonly Dictionary<string, StrategyProvenance> _table = new(StringComparer.OrdinalIgnoreCase)
        {
            // ── Pulse family ────────────────────────────────────────────────────────────
            [StrategyCatalogue.CapitulationBuyId] = new(
                StrategyEvidenceLevel.Fragile,
                TestedOn: "BTC/ETH/XRP/LTC daily, 22 rolling 1500-bar windows (2026-04)",
                Controls: "rolling-window walk-forward; bootstrap CI; NO null arm at the time",
                Verdict: "16 of 22 windows positive (~+0.29R), ETH best. Downgraded since: the MVRV " +
                         "on-chain gate that makes this spec distinctive later failed an exposure-matched " +
                         "null 0 for 6 — its apparent edge was exposure to the bear-market bottom, not " +
                         "timing. Entry stack also contains the repainting Cipher SR.",
                Source: "StrategyLab rolling-window; later on-chain null study"),

            [StrategyCatalogue.FaberPulseLongId] = new(
                StrategyEvidenceLevel.InSampleOnly,
                TestedOn: "BTC daily only, 10 rolling 1500-bar windows (2026-04)",
                Controls: "rolling-window; bootstrap CI on each window; no null arm; no second asset",
                Verdict: "The highest CI-pass count of the 89-cell battery — which is a maximum over 89 " +
                         "draws, not an estimate. Never re-tested on a second asset. Entry stack contains " +
                         "the repainting Cipher SR. The Faber 200-MA gate itself is separately supported; " +
                         "see the trend baseline, which tests it without the Cipher layer.",
                Source: "StrategyLab battery + rolling-window"),

            [StrategyCatalogue.BareBullPulseLongId] = new(
                StrategyEvidenceLevel.InSampleOnly,
                TestedOn: "BTC daily, 10 rolling 1500-bar windows (2026-04)",
                Controls: "rolling-window; no null arm",
                Verdict: "90% of windows positive — the highest raw hit rate in the battery, and the " +
                         "least clever cell in it. Worth keeping as the thing fancier Cipher stacks must " +
                         "beat, but it is a battery cell on one asset and the entry stack repaints.",
                Source: "StrategyLab battery"),

            [StrategyCatalogue.PulseLongV2Id] = new(
                StrategyEvidenceLevel.WalkForward,
                TestedOn: "BTC and ETH daily, split-half walk-forward (2026-04)",
                Controls: "walk-forward on two assets; bootstrap CI (not passed)",
                Verdict: "Point-positive expectancy in BOTH halves on BOTH assets without retuning — the " +
                         "first Pulse signal to generalise across assets. Falls short of strict bootstrap " +
                         "CI (BTC H2 CI low -0.01) and was never run against a random-entry null.",
                Source: "StrategyLab walk"),

            [StrategyCatalogue.PulseReversalLongId] = new(
                StrategyEvidenceLevel.WalkForward,
                TestedOn: "ETH daily, split-half walk-forward (2026-04)",
                Controls: "walk-forward; bootstrap CI (H2 only)",
                Verdict: "H2 passed strict CI (+1.03R, 12 trades); H1 point-positive but wide. One asset, " +
                         "one timeframe, 21 trades in total — the sample is small enough that the H1/H2 " +
                         "difference is not distinguishable from noise.",
                Source: "StrategyLab walk"),

            // ── Cipher B + regime gates ─────────────────────────────────────────────────
            [StrategyCatalogue.LongV13BlueDotSma200Id] = new(
                StrategyEvidenceLevel.WalkForward,
                TestedOn: "BTC daily, split-half walk-forward (2026-04)",
                Controls: "walk-forward; no null arm",
                Verdict: "Positive in both halves (+0.65R / +0.50R). Read it as one draw from the Cipher " +
                         "confluence family, which walked forward to break-even overall across eight " +
                         "versions — the variant looking best is the family's upper tail by construction.",
                Source: "StrategyLab walk"),

            [StrategyCatalogue.LongV14HiddenBullSma200Id] = new(
                StrategyEvidenceLevel.InSampleOnly,
                TestedOn: "BTC 4h, first half only, isolation diagnostic (2026-04-11)",
                Controls: "bootstrap CI on the half it was selected from",
                Verdict: "Passed strict CI on H1 with 20 trades — but H1 is the half the signal was picked " +
                         "on, and the second half was never run. This is a candidate, not a result.",
                Source: "StrategyLab diagnostic"),

            [StrategyCatalogue.LongV15BlueDotBullDivId] = new(
                StrategyEvidenceLevel.Untested,
                TestedOn: "never run",
                Controls: "none",
                Verdict: "Built as the confluence of the two highest-R survivors and never evaluated. The " +
                         "stated rationale — divergence explains why the cross matters — is a story, not evidence.",
                Source: null),

            // ── Cipher SR trilogies: the structural premise was tested and failed ───────
            [StrategyCatalogue.LongV16TrilogyId] = new(
                StrategyEvidenceLevel.Falsified,
                TestedOn: "the SR component, via ConfluenceCommand (2026-06)",
                Controls: "lookahead audit; random-label control on structure",
                Verdict: "The structural third of the trilogy does not exist: Cipher SR proximity was a " +
                         "15-bar lookahead artifact, and structure labels tested indistinguishable from " +
                         "random. The spec has never been re-run with the artifact removed, and the " +
                         "provider still repaints, so it cannot be honestly backtested as written.",
                Source: "StrategyLab ConfluenceCommand"),

            [StrategyCatalogue.ShortV16TrilogyId] = new(
                StrategyEvidenceLevel.Falsified,
                TestedOn: "the SR component, via ConfluenceCommand (2026-06)",
                Controls: "lookahead audit; random-label control on structure",
                Verdict: "Same falsified structural premise as the long trilogy, on the side of the market " +
                         "where crypto's upward drift is already against you.",
                Source: "StrategyLab ConfluenceCommand"),

            [StrategyCatalogue.LongV17GoldTrilogyId] = new(
                StrategyEvidenceLevel.Falsified,
                TestedOn: "the SR component, via ConfluenceCommand (2026-06)",
                Controls: "lookahead audit",
                Verdict: "Built on the same repainting SR leaf, plus a speculation about what the gold dot " +
                         "reads internally that was never tested either.",
                Source: "StrategyLab ConfluenceCommand"),

            [StrategyCatalogue.LongV21MvrvCapitulationTrilogyId] = new(
                StrategyEvidenceLevel.Falsified,
                TestedOn: "both components separately (2026-06, 2026-07)",
                Controls: "lookahead audit on SR; exposure-matched null on MVRV",
                Verdict: "Both halves failed: the SR leaf repaints, and MVRV-regime gating failed the " +
                         "exposure-matched null 0 for 6 — being long during a low-MVRV period is exposure, " +
                         "not timing. The intersection of two failed components is not a strategy.",
                Source: "StrategyLab ConfluenceCommand; on-chain null study"),

            // ── Asymmetric short ────────────────────────────────────────────────────────
            [StrategyCatalogue.ShortV18RefinedShortId] = new(
                StrategyEvidenceLevel.WalkForward,
                TestedOn: "BTC 4h and daily, split-half walk-forward (2026-04)",
                Controls: "walk-forward; no null arm",
                Verdict: "The only short in the catalogue to survive walk-forward. Its shape matches the " +
                         "separate finding that BTC short signals only work in a confirmed bear regime with " +
                         "longs still paying funding; every short built on a reversal trigger alone is " +
                         "negative. Never compared against a random-entry null.",
                Source: "StrategyLab walk"),

            // ── Top/bottom detector ─────────────────────────────────────────────────────
            [StrategyCatalogue.LongV22CapitulationBottomId] = new(
                StrategyEvidenceLevel.Untested,
                TestedOn: "never run",
                Controls: "none",
                Verdict: "Operationalises the 'bottoms are events' half of the asymmetry thesis. The thesis " +
                         "is untested and so is the spec.",
                Source: null),

            [StrategyCatalogue.ShortV22DistributionTopId] = new(
                StrategyEvidenceLevel.Untested,
                TestedOn: "never run",
                Controls: "none",
                Verdict: "The 'tops are processes' mirror. Untested, and a multi-bar accumulator with five " +
                         "weighted inputs has a great deal of freedom to fit whatever it is shown.",
                Source: null),

            [StrategyCatalogue.LongV22rCapitulationFaberId] = new(
                StrategyEvidenceLevel.Untested,
                TestedOn: "never run",
                Controls: "none",
                Verdict: "v22 plus the Faber regime gate. The gate is the best-supported filter in the " +
                         "project; the detector underneath it has still never been evaluated.",
                Source: null),

            [StrategyCatalogue.ShortV22rDistributionBearFundedId] = new(
                StrategyEvidenceLevel.Untested,
                TestedOn: "never run",
                Controls: "none",
                Verdict: "Applies v18's surviving short shape to the distribution detector. The most " +
                         "plausible untested spec here, which is not the same as a promising one.",
                Source: null),

            // ── v23 weekly reversal family ──────────────────────────────────────────────
            [StrategyCatalogue.LongV23CipherBWeeklyId] = new(
                StrategyEvidenceLevel.InSampleOnly,
                TestedOn: "BTC 4h and daily (2026-04)",
                Controls: "none beyond a single backtest",
                Verdict: "Positive total P&L with marginal per-trade R — counter-trend entries in deep bear " +
                         "markets eat the bull-regime wins. Every later v23 variant is an attempt to fix " +
                         "that, which makes this the base case rather than a strategy to run.",
                Source: "StrategyLab run"),

            [StrategyCatalogue.ShortV23CipherBWeeklyId] = new(
                StrategyEvidenceLevel.Falsified,
                TestedOn: "BTC 4h and daily (2026-04)",
                Controls: "backtest across both timeframes",
                Verdict: "Negative expectancy on both timeframes. Recorded rather than deleted: symmetric " +
                         "shorts on a reversal trigger are the single most repeated failure in this catalogue.",
                Source: "StrategyLab run"),

            [StrategyCatalogue.LongV23rCipherBFaberId] = new(
                StrategyEvidenceLevel.Untested,
                TestedOn: "never run as a spec",
                Controls: "none",
                Verdict: "The Faber gate on v23 base. The gate is well supported elsewhere and the " +
                         "hypothesis is reasonable, but this combination was never actually run.",
                Source: null),

            [StrategyCatalogue.ShortV23rCipherBFaberId] = new(
                StrategyEvidenceLevel.Falsified,
                TestedOn: "BTC 4h and daily (2026-04)",
                Controls: "backtest across both timeframes",
                Verdict: "Negative expectancy on both timeframes — adding the regime gate did not rescue " +
                         "the short side.",
                Source: "StrategyLab run"),

            [StrategyCatalogue.ShortV23rfCipherBFundingId] = new(
                StrategyEvidenceLevel.Untested,
                TestedOn: "never run",
                Controls: "none",
                Verdict: "Transplants v18's funding-crowding gate onto v23's short after the two plain " +
                         "shorts failed. The reasoning is sound and the spec is still a hypothesis.",
                Source: null),

            [StrategyCatalogue.LongV23pCipherBPivotsId] = new(
                StrategyEvidenceLevel.InSampleOnly,
                TestedOn: "ETH daily 6 windows, BTC daily 15 windows (2026-04 battery round 4)",
                Controls: "rolling-window per cell — but the cell was CHOSEN for winning",
                Verdict: "ETH 100% of windows positive at +0.523R, the best cell anywhere in the battery. " +
                         "That is a maximum over 89 cells; the selection is in-sample even though each cell " +
                         "was walked forward. Needs a fresh out-of-sample asset before it means anything.",
                Source: "StrategyLab battery round 4"),

            [StrategyCatalogue.LongV23hCipherBHurstId] = new(
                StrategyEvidenceLevel.InSampleOnly,
                TestedOn: "BTC daily, rolling windows (2026-04 battery)",
                Controls: "rolling-window per cell; selection over cells not controlled",
                Verdict: "71% of windows positive at +0.411R, 65% better per-trade R than v23 base. The " +
                         "mechanism — only fire reversals when Hurst says the regime mean-reverts — is one " +
                         "of the few gates here with a reason to exist beyond the numbers, which is the " +
                         "argument for testing it properly rather than for trusting it now.",
                Source: "StrategyLab battery"),

            [StrategyCatalogue.ShortV23hCipherBHurstId] = new(
                StrategyEvidenceLevel.InSampleOnly,
                TestedOn: "KAS 4h, rolling windows (2026-04 battery)",
                Controls: "rolling-window per cell; selection over cells not controlled",
                Verdict: "62% of windows positive at +0.207R on a single low-cap altcoin — the weakest " +
                         "evidence base of any promoted cell, on the side of the market where every other " +
                         "short in this catalogue has failed.",
                Source: "StrategyLab battery"),

            [StrategyCatalogue.LongV23aCipherBAvwapId] = new(
                StrategyEvidenceLevel.InSampleOnly,
                TestedOn: "ETH daily, BTC daily, rolling windows (2026-04 battery round 6)",
                Controls: "rolling-window per cell; selection over cells not controlled",
                Verdict: "ETH 100% of windows positive at +0.277R; BTC 80% at +0.203R. Same battery-selection " +
                         "caveat, and the 'soft' variant was chosen over the strict one because it fired more " +
                         "often — a second in-sample choice on top of the first.",
                Source: "StrategyLab battery round 6"),

            [StrategyCatalogue.LongV23orCipherBOrConfId] = new(
                StrategyEvidenceLevel.InSampleOnly,
                TestedOn: "ETH daily, BTC daily, rolling windows (2026-04 battery round 8)",
                Controls: "rolling-window per cell; selection over cells not controlled",
                Verdict: "Broader coverage than v23a or v23p alone with per-trade R between them, which is " +
                         "what an OR of two gates should do arithmetically. Third-order battery selection.",
                Source: "StrategyLab battery round 8"),

            // ── The baseline everything else must beat ──────────────────────────────────
            [StrategyCatalogue.LongTrendBaselineId] = new(
                StrategyEvidenceLevel.ControlTested,
                TestedOn: "10 assets across crypto, indices, gold, FX; era-sliced monthly (2026-07-13), " +
                          "plus a BTC daily parameter walk-forward (2026-07)",
                Controls: "exposure-matched buy-and-hold; era slicing; RANDOM-PARAMETER arm",
                Verdict: "The best-supported thing in this catalogue, and the result is humbling: the trend " +
                         "FAMILY works — BTC vol-targeted Sharpe 1.19 vs 0.80 for holding, max drawdown 23% " +
                         "vs 83% — but randomly-drawn parameters beat optimised ones out of sample, so the " +
                         "specific numbers here carry no information. On indices and gold it matches hold's " +
                         "Sharpe with 2-3x smaller drawdowns (crash insurance, not alpha); on single " +
                         "secular-growth names holding wins; long-only FX has no edge at all.",
                Source: "StrategyLab cross-asset trend study; BTC walk-forward"),

            [StrategyCatalogue.LongV23cCipherBCotId] = new(
                StrategyEvidenceLevel.Fragile,
                TestedOn: "10 assets, era-sliced gate battery (2026-07-13); COT re-tested separately (2026-07)",
                Controls: "era slicing; per-asset validity checks; later a dedicated COT study",
                Verdict: "Best cell in the battery (QQQ Faber+COT: 94% hit, +5.01%/20d, t=5.65) from 10 " +
                         "assets times many gates — and the COT half later tested as carrying no forward " +
                         "information from either data source, leaving the Faber regime gate doing the work. " +
                         "Per-asset validity is real and narrow: do NOT use on BTC (every gate reduced the " +
                         "edge; CME positioning is basis-trade contaminated) or FX.",
                Source: "StrategyLab gate battery; COT positioning study"),

            [StrategyCatalogue.LongV24CycleLowReversalId] = new(
                StrategyEvidenceLevel.WalkForward,
                TestedOn: "BTC daily, both walk-forward halves (2026-07-17)",
                Controls: "walk-forward; variant-by-variant ablation of gates and stops",
                Verdict: "Positive in both halves after four rejected variants — but those four were " +
                         "rejected ON these same halves, so the halves are not fully out of sample. The " +
                         "ablation record is the useful part: the anchor-wave depth gate deleted the good " +
                         "half, the structural swing-low stop got run over by cycle-low retests, and the " +
                         "Cipher trigger improved the WEAKER half, which is why it stayed. Cycle detection " +
                         "generally is a place this project has found artifacts before — treat with care.",
                Source: "StrategyLab walk (Wave 4)"),
        };

        /// <summary>Provenance for a spec id, or null if the id has no entry.</summary>
        public static StrategyProvenance? For(string specId) =>
            _table.TryGetValue(specId, out var p) ? p : null;

        /// <summary>Every recorded id. Used by the tests that keep this table in step with the catalogue.</summary>
        public static IReadOnlyCollection<string> RecordedIds => _table.Keys;

        /// <summary>
        /// The catalogue with provenance attached to each spec, ready to export. A spec with no
        /// table entry would ship as an anonymous recommendation, so this throws rather than
        /// emitting one — <c>CatalogueProvenanceTests</c> catches it long before the CLI does.
        /// </summary>
        public static IEnumerable<StrategySpec> SpecsWithProvenance() =>
            StrategyCatalogue.AllSpecs().Select(s =>
            {
                var p = For(s.Id)
                    ?? throw new InvalidOperationException(
                        $"Spec '{s.Id}' has no CatalogueProvenance entry. Every catalogue spec must record " +
                        "what it was tested on, which controls ran, and the verdict — 'Untested' included.");
                return s with { Provenance = p };
            });
    }
}
