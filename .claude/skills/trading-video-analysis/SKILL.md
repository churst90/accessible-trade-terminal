---
name: trading-video-analysis
description: Use when asked to analyse a trading video, interview, or tutorial (YouTube URL) — how to pull the transcript without a browser, what to extract, and how to turn a claim into something the StrategyLab can falsify.
---

# Turning a trading video into a testable spec

## Pull the transcript

No browser or Whisper needed — YouTube's own captions are enough and far faster. Use the standalone
`yt-dlp` binary (download once to the scratchpad; no pip required):

```bash
curl -sL https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -o yt-dlp && chmod +x yt-dlp
./yt-dlp --skip-download --write-auto-subs --write-subs --sub-lang "en.*" \
         --sub-format vtt -o "%(id)s.%(ext)s" "https://www.youtube.com/watch?v=<id>"
```

The `.en.vtt` is the cleaner track. Strip it to prose — VTT repeats each line as it scrolls, so
dedupe while preserving order:

```python
import re
lines = open(f, encoding='utf-8').read().split('\n')
out, seen = [], set()
for L in lines:
    if '-->' in L or L.startswith(('WEBVTT','Kind:','Language:')) or not L.strip(): continue
    t = re.sub(r'<[^>]+>', '', L).strip()
    if t and t not in seen: seen.add(t); out.append(t)
open('transcript.txt','w').write(re.sub(r'\s+',' ', ' '.join(out)))
```

Split into ~6,000-word parts and read them in full. Do not grep-and-skim an interview — the
valuable material is usually an aside, not a headline. Grep is fine for locating a *named technique*
across a playlist.

Batch a playlist by looping over video IDs. Skim the first ~500 words of each to find which ones
actually contain the method; tutorial playlists are usually one substantive video plus filler.

## What to extract

- **The rule, stated as a number and a comparison.** "Buy when the z-score crosses above +1, sell
  below 0" is testable. "Buy when momentum shifts" is not. If the rule is a proprietary indicator
  changing colour, say so and stop — inferring it means fitting to a picture and then testing your
  own invention.
- **The claimed result and the benchmark used.** Beating DCA is mostly a statement about deployment
  schedules; insist on buy-and-hold.
- **Their robustness test, and whether it can fail.** Reshuffling a strategy's own returns cannot.
- **Calibration numbers from practitioners** — these are often more valuable than the strategy.
- **Falsifiable side-claims.** Often the most testable thing in the video is an aside.

## Convert to a spec before coding

Write the rule as: entry condition, exit condition, direction, timeframe, parameters, claimed
result, claimed benchmark. Then hand it to the `strategy-research` skill — which control would
reproduce this result *without* the claimed mechanism?

**Watch for retrospective selection.** Tutorials that identify features by looking back (cycle lows,
levels, patterns) will show near-perfect historical fit because the features were chosen knowing the
outcome. That claim needs an algorithmic definition plus surrogate comparison before it means
anything.

## Record it

Findings that survive go in `docs/`. Practitioner calibration and framings that change how work is
scoped go in memory as `type: reference` — they are worth more than any single strategy and they
compound. Existing notes: `narang-interview-notes`, `varma-interview-notes`, `camel-cycle-spec`.

## Sources analysed so far

- **Onchain Mind** — z-score momentum ("Trading Cross"). Tested: real, crypto-only.
- **Rishi Narang** (*Inside the Black Box*) — alpha taxonomy, detrending control, overfitting.
- **Samir Varma** — volume patterns, 200-day-MA robustness, noise injection, three momentum types.
- **Camel Finance / Bob Lucas / Charles Nana** — the cycle system. Spec captured, untested.
- **Cosasverdes** — equidistant line families. Tested: all four claims failed.
