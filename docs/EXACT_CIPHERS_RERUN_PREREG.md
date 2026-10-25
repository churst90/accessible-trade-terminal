# The Market Cipher tests re-run on the exact Ciphers: pre-registration

Written 2026-10-25, around 00:45 UTC, before any outcome of any test below had been computed on
the exact indicators. Cody asked for it on 2026-10-24: "write the plan and then proceed to
execute".

## 1. Why

Four pre-registered tests of the Market Cipher indicators ran on 2026-10-17 and 10-18. All four
said No edge:

| Test | Command | Plan | Findings | Ran on |
|---|---|---|---|---|
| Cipher A battery | `cipher-a-battery` | `reports/cipher_strategies_rerun_REPORT.md` (pre-registration section) | same file, Results | 61caa0d4, 2026-10-17 |
| Round 1, setup grader | `setup-grader` | `docs/SETUP_GRADER_PREREG.md` | `docs/SETUP_GRADER_FINDINGS.md` | 2026-10-18 |
| Round 2, Market Cipher school | `setup-grader-mc2` | `docs/SETUP_GRADER_ROUND2_MC_PREREG.md` | `docs/SETUP_GRADER_ROUND2_MC_FINDINGS.md` | bb39113e, 2026-10-18 |
| Round 3, with Cipher SR support | `setup-grader-mc3` | `docs/SETUP_GRADER_ROUND3_MC_SR_PREREG.md` | `docs/SETUP_GRADER_ROUND3_MC_SR_FINDINGS.md` | de36e78b, 2026-10-18 |

Since then, Cipher A, Cipher B and Cipher SR have been checked bar by bar against Market Cipher's
own plotted values, and corrected until they match:

- **Cipher B:**
  - c958dc2f (10-23): the Buy dot is a cross up with the FAST wave below −60. It was the slow wave.
  - c958dc2f: the "RSI" and "Stochastic" lines are 40- and 81-bar stochastics of the close,
    smoothed by SMA2. They were hlc3, 40/80, SMA3.
  - b43455cc, fa07e5ba, e3e3298a (10-23 and 10-24): the money flow is now Market Cipher B's
    exactly. The 10-18 money flow was a different formula, signed square root × 175.
  - The waves were already exact.
- **Cipher A:**
  - f4e96e76: Green Dot, Blue Triangle, Red Diamond and Yellow X corrected.
  - c300bff5, then e3e3298a: Yellow Diamond is exact.
  - a1d03702: the pivots are not used by any test here.
- **Cipher SR:** 72c9e8f6, the stepped 2-bar pivot, drawn three bars on, is Market Cipher SR's.
  Round 3's findings said our old replica matched the real one on 62.5% of bars.

Every 10-17 and 10-18 result was therefore computed on replicas that were partly wrong. Round 3's
findings warned about this itself. The question this re-run answers is narrow:

**With the indicators now exact, do any of the four verdicts change?**

## 2. What I had seen before writing this

- Every outcome of all four tests on the OLD indicators. All four findings files had been read.
- How the exact indicators differ from Market Cipher's values. That was fidelity work: no returns,
  no R, no forward outcome of any kind.
- Nothing about the outcome of any test on the exact indicators. Not a count, not a mean, not a p.

Because I know the old results, this is not a blind test of the method. It is a re-run of tests
whose rules were fixed before their first run. Every threshold, window, seed, asset, era, exit and
verdict sentence stays as those four plans wrote it. The only thing that changes is the indicator.

## 3. What runs, in order

The lab is built from the `cipher-c-cf-cycles` branch at the commit that adds this file, in the
`att-camel-fixes` worktree. It is never built in main, because the server runs from main's build.

The snapshots are the same `strategy-lab-data` files the earlier runs used, read-only, with no
fetches. Each command hashes its inputs before and after the run.

1. **The tests of each harness first:** `SetupGraderTests`, `SetupGraderMc2Tests`,
   `SetupGraderMc3Tests` and `LabCipherProfileTests`. A red test stops the plan until it is
   explained.
   - A test that pins a 10-18 count or value to the old indicator is not a defect. It may be
     re-pinned, and every re-pin is listed in the findings.
   - A test that checks a property, such as causality or the rule that a level is live before its
     bar, must stay green without edits.
2. **Round 1.** `setup-grader --counts-only`, recorded. Then the full run.
3. **Round 2.** `setup-grader-mc2 --counts-only`, recorded. Then the full run.
4. **Round 3** needs two things from rounds 1 and 2: round 2's development-era counts for its
   plumbing check, and every earlier p for its cross-round Holm. Both are hard-coded from the 10-18
   runs.
   - The only harness change in this plan: a switch, `--priors exact`, selects a second table,
     filled in from steps 2 and 3's printed output. The change is committed before round 3 runs.
   - Without the switch, round 3 keeps the 10-18 table. It should then stop with exit 4, because
     round 2's counts no longer reproduce, which is the correct behaviour.
   - Then round 3 with `--counts-only` and the switch, recorded; then the full run with the switch.
5. **The Cipher A battery,** the full run. It has no counts-only stage. Its permutation seeds
   (20261017, 20261018) and its 2,000 draws stay as they were.

## 4. How each verdict is read

Each test's own plan decides its own verdict, word for word, as on 10-17 and 10-18:

- the families;
- Holm, or the max-|t| family null;
- the held-out era (2023 on for the setup graders);
- breadth;
- noise retention;
- the placebo gate;
- the 15 bps a side cost;
- the matched random controls.

Nothing is added, dropped or re-weighted. The findings report, for every headline test, the old and
new statistic, p and verdict side by side, and how many setups or signals changed between the old
and exact indicators.

**What a changed verdict would mean.** A test that now passes would be a pass under a rule written
before the data were first used. But the data are the same, the held-out era has already been
seen, and I knew the old answers. So a new pass is reported as a pass with exactly those caveats,
and it is not called an edge until it holds on data this lab has not used. That means bars after
2026-10-18, or markets not in these snapshots, under its own pre-registration. A test that now
fails where it passed before is reported the same way.

## 5. Predictions, written before any result

- **All four verdicts stay No edge.** The Ciphers changed in detail, but they still read the same
  price momentum. The Buy dot moves from the slow wave to the fast wave, so dots move by a bar or two
  and a few appear or vanish. That should not turn a null into an edge.
- **The setup counts change.**
  - Round 1's and round 2's grades read the money flow and the RSI line, both of which changed, so
    many setups will change grade.
  - The money flow's formula and scale changed, but the tests read only its sign, its direction and
    a rank (round 2's median of |money flow|), none of which depends on scale. So I expect the grade
    counts to move by tens of percent, not to collapse.
- **Round 3's support will be different levels** (72c9e8f6). Its verdict should still be No edge.

These are recorded so that the findings can say which ones were wrong.

## 6. One exploratory part, after everything above

Market Cipher is often used on Heikin-Ashi charts. Every test above computes the Ciphers on real
candles. As an exploratory part, clearly labelled and outside every Holm family above:

- **The switch:** a lab-wide `--cipher-candles ha` gives Cipher A, Cipher B and Cipher SR the same
  Heikin-Ashi parameter a Heikin-Ashi chart gives them in the app. Each indicator then computes as
  it does on a Heikin-Ashi chart. Entries, stops, ATR and fills stay on the real bars.
- **The runs:** round 2 and the Cipher A battery, each run once with the switch.
- **What it can say:** only "worth its own pre-registration on new data" or "nothing". It cannot
  say "edge", whatever its p.
- Its runs are counted in the findings' total of tries.

## 7. Total tries, so far

Four confirmatory commands, run once each in full on the exact indicators, plus their counts-only
stages. Any rerun after a crash or a reporting fix is listed with the reason. The exploratory part
adds two runs.
