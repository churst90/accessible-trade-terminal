# A2e — the fourth mutant set, and the first targeted one

Run 2026-09-06 against `4702a00f`. 27 mutants, none in any file A2, A2b, A2c or A2d touched.

## The number

| | |
|---|---|
| Mutants applied | 27 |
| Equivalent (excluded from the denominator) | 2 — E14, E25 |
| Valid mutants | **25** |
| Caught | 18 |
| Survived | 7 |
| **Honest catch rate** | **72.0%** |

Naive rate before the corrections was 20/27 = 74.1%, and it was wrong twice over: two
mutants (E03, E04) came back falsely CAUGHT, and two (E14, E25) are equivalent mutants that
should never have been in the set.

**72.0% against A2d's 73.1% on a completely different area is corroboration, not repetition.**
The two sets share no files and were chosen a week apart, so the agreement is evidence that
the catch rate is a property of the suite rather than of the sampling.

## What went wrong in the harness, and it was self-inflicted

**Mid-run I bumped `<Version>` to 2.9.0 and rewrote `WHATSNEW.md` for the release.** This repo
has `AboutDialogHonestyTests.TheReleaseDocsNameTheVersionThatIsActuallyBuilt`, which reads
`Directory.Build.props` and diffs it against `CHANGES.md` and `WHATSNEW.md`. It went red, and
E03 and E04 were recorded as CAUGHT with that single unrelated test as their only failure.

That is the A2 trap exactly — five mutants falsely caught by one flaky test, naive 79% against
an honest 61% — arriving by a different route. Both were re-run on a restored baseline and
**both SURVIVED**, which is how two of the most interesting findings in the set were nearly lost.

**Rule 5, now in the harness docstring: a campaign's baseline includes the DOCS.** Not just "do
not edit code while a campaign runs" — this suite asserts on documentation, so the tree has to
be quiet in every sense from launch to control run.

The control run at the end was green (6,887 passed, 0 failed), so the tree was clean throughout
and no result was measured against a stale sabotage.

## The seven survivors

### E03 — F1 is unreachable from inside any dialog

`CommandDispatcher`'s modal gate allows seven commands through while a dialog is open. Removing
`OpenHelp` from that list passed all 6,887 tests.

**The most serious finding in the set.** For a screen-reader user who has arrived somewhere they
do not recognise, F1 is the key they reach for, and it would have gone dead in exactly the
situation it exists for. The gate's own comment says *"F1 — help is always reachable"*; nothing
checked that it was.

Killed by `CommandDispatcherGatingTests.F1ReachesHelpFromInsideAnOpenDialog`, with
`AChartCommandIsStillSwallowedWhileADialogIsOpen` as the control so "let everything through"
cannot satisfy it.

### E04 — the `0` key ignores the component under the cursor

Written the same day the campaign ran. Deleting the focused-component lookup, leaving only the
"first component that declares a neutral" fallback, passed everything — because every test had a
single-component series where the two answers agree.

**The pattern: a fallback masks the rule it is a fallback for.** A Cipher B pane holds components
with different neutrals, and the one under the cursor is the one being asked about.

The first draft of the killing test re-derived the dispatcher's two-step lookup in the test and
asserted on `ReferenceLevelPlacement` — which is the *test mirrors production logic* pathology,
and the mutant would have survived it. Rewritten to drive `CommandDispatcher.Dispatch` and read
the `AddLevelAction` that comes out.

### E09 — the crossing engine picks the farthest crossing, not the nearest

Inverting `jumpRight ? idx < found : idx > found` passed everything.

**A selection rule is only under test when at least two candidates compete.** With one candidate,
`found < 0` takes it regardless of the comparison — and every existing test had exactly one.
Killed by two tests, one per arm of the ternary.

### E10 — a continuous line is treated as a sparse marker

Weakening `hasNaN = compData.Any(double.IsNaN)` to `compData != null` sends every dense line to
the sparse-signal jump, so Ctrl+Left/Right lands on an arbitrary bar instead of saying there is
nothing to jump to. That is the "silently falling through surprised users" case the branch's own
comment describes, reintroduced.

### E17 — the alert cooldown is ignored on repeat

Replacing `DateTime.UtcNow - last >= alert.Cooldown` with `>= TimeSpan.Zero` passed everything.

**The test directly above it in the file looks like it covers this** — it is the
`RepeatIfStillActive` test, and its own summary sentence mentions the cooldown. It sets
`Cooldown = TimeSpan.Zero`, which makes the mutant and the original the same expression.
**A test that sets a value to zero is not testing what that value does.**

Cost if wrong: both background monitors poll every 60 seconds, so a held level would re-announce
on every poll regardless of what the user set — the unbounded duplicate delivery the per-bar
dedupe exists to stop, arriving through the one branch deliberately exempted from it.

### E24 — unconfigured alert channels are dispatched to

Inverting `if (!ch.IsConfigured) continue;` passed everything, for the simplest reason there is:
**`AlertDeliveryService` had no test file.** It was named once, in passing, as a constructor
argument in another class's tests.

A channel reports `IsConfigured` false precisely when it has no SMTP host, no bot token, no
webhook URL. Calling it anyway is an exception per alert per channel, and the configured channel
the user actually set up goes silent — with no symptom at all on an audio-first terminal.

New file, five tests, including the class's stated but untested contract that one channel
failing must not starve the others.

### E27 — a hostname resolving to one public and one private address is allowed

Weakening `resolved.Any(a => !IsPublic(a))` to `All(...)` passed everything. That is DNS
rebinding: an attacker controls their own DNS record, so an answer with one public address and
one private one is still a probe at the private one.

The cases *around* it were well covered — a private literal, a name resolving only to loopback,
eleven tests catching the loopback mutant (E26). This one sentence was not, because it sat inline
after a live `Dns.GetHostAddressesAsync` and could not be reached without controlling DNS.

**Fixed by extracting the rule** to `OutboundNetworkGuard.AllPublic`, which needs no network, plus
a test that the resolver actually calls it (a pure function tested in isolation says nothing about
whether the caller reads it — the *presence, not path* trap).

## The two equivalent mutants

Recorded rather than quietly dropped, because "equivalent" is the excuse a weak campaign uses.

- **E14** — removing the `IsNullOrWhiteSpace` guard from `KeyNormalizationService.NormalizeKey`.
  The very next line is `key.Trim().ToUpperInvariant()`, so a whitespace key becomes `""`, misses
  both map lookups, and is returned as `""` — the same value the guard would have returned. No
  observable difference. `InputRoutingTests.Normalize_BlankInput_YieldsEmpty` already covers
  `"   "` and passes either way, correctly.
- **E25** — flipping `return false; // unknown family` in `OutboundNetworkGuard.IsPublic`.
  `IPAddress.AddressFamily` is always `InterNetwork` or `InterNetworkV6`; the type cannot
  represent anything else. The line is unreachable defensive code, and the right response is to
  say so rather than to write a test that fakes reachability.

## What the survivors have in common

Six of the seven are the **A2d shape, one level deeper**: not an untested class, but *the one
sentence of a tested class that nobody asked about, sitting next to a test that looks like it
covers it*. E17 is the purest example — the test is named for the flag, mentions the cooldown,
and neutralises it.

The seventh (E24) is the other A2d shape: **code nobody has ever written a test about.** The
census predicted it — `Core/Services/Alerts` shows 1 of 15 types unnamed, and `AlertDeliveryService`
was the one.

**Two of the seven are security code** (E27 rebinding, and E25 which turned out equivalent), in a
file that already had 46 passing tests. A well-tested file is not a measured file.

## What this says about where to mutate next

The five remaining never-mutated areas, in the order I would take them:

1. `Core/Services/Workspace` (9 files) — the restore path, most-recently-broken, and the new
   completeness guards there have never been measured.
2. `Core/Services/Audio` (19) — a whole sensory channel; 9 of 32 types unnamed.
3. `Core/Services/Analysis` (17).
4. `Core/Services/Rendering` (12) — where the segfault lived.
5. `Core/Services/Feeds` + `Notifications` (5) — small, and **these two should be folded into
   the background-monitor work rather than done first**, because that work rewires them.
