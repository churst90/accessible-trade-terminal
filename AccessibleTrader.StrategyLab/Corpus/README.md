# StrategyLab research corpus: trader transcripts and extracted rules

This folder is data for the StrategyLab, not code. It holds what a set of traders and educators
actually said, so that their rules can be turned into pre-registered tests and their dated calls
can be scored against prices later. Nothing in here is an endorsement. Most of these methods have
not been tested yet, and several of the ideas behind them have already failed in this repo (see
`docs/CYCLE_FINDINGS.md`, `docs/PLATFORM_AND_SIGNAL_SERVICE.md`, `docs/SETUP_GRADER_PREREG.md`).

## What is here

| Path | What it is |
|---|---|
| `manifest.json` | One record per item, obtained or not: source, school, channel, title, URL, publish date, duration, how it was obtained, transcript path, sha256 of the transcript, word count, retrieval date, tags, access. |
| `transcripts/<source>/<YYYY-MM-DD>_<id>.txt` | The full transcript as plain prose, with a `[mm:ss]` marker about every 30 seconds. Minutes do not wrap: `[93:10]` is 93 minutes 10 seconds. |
| `transcripts/<source>/<YYYY-MM-DD>_<id>.vtt` | The timed track the text was made from: YouTube's own subtitle file, or Whisper's VTT. Never audio or video. |
| `rules/<source>.md` | The decision rules extracted from that source (context, location, triggers, confirmations, vetoes, stops, exits, sizing, timeframes, macro), each with a short quote and a timestamped citation, plus every dated call for later scoring, plus the candidate rules the pre-registered setup grader does not use. |
| `scripts/` | The scripts that list, fetch, transcribe, convert and build all of the above. |

`0000-00-00` in a file name means the source gives no publish date (the local tutorials).

## Schools: never blended

The sources are different schools of trading, and each rules file describes one trader's own
method in that trader's terms (Cody, 2026-10-18). `rules/SCHOOLS.md` consolidates what the traders
of one school share and where they disagree, with no cross-school combination.

| School | Sources |
|---|---|
| `structure` | Cosasverdes (Tim): trend structure from candle bodies, horizontals, level reactions, weekly moving-average phase |
| `market-cipher` | Crypto Face, Market Cipher official channels, Jason (Jayson) Casper, and the Market Cipher educators (Trader Geo, Trade Pro, DaviddTech, Trading Bureau, Dylan Waves, Gunslinger Trades, Frankie Candles, AlexOCrypto, Chart Wis, the DBSI reviewers, Gainium) |
| `cycle` | Camel Finance, Bob Loukas, and the DSS Bressert / Walter Bressert cycle material |

The `school` field in the manifest carries the same split.

## Reading the manifest honestly

- **`method`** says where the words came from:
  - `YouTube subtitles (manual)`: a track the uploader provided.
  - `YouTube subtitles (ASR)`: YouTube's automatic speech recognition. Tickers, numbers and
    indicator names are often wrong ("brelet" for Bressert, "market cypher" for Market Cipher).
  - `Whisper large-v3-turbo`: transcribed here from the audio with openai-whisper. Used for the
    local tutorial videos, and for YouTube videos whose caption track YouTube would not serve
    (HTTP 429); `why_whisper` says which.
  - `web page`: readable text of a public page.
- **`access`**: `public` (anyone can watch it), `community or paid` (the content says it came from
  a paid library or members' tier), `unknown`. Members-only videos were never requested; they
  appear with `obtained: false` and the reason so the gaps are visible.
- **`sha256`** is of the `.txt` file. A rebuild from a fresh download will not match exactly when
  YouTube regenerates ASR or Whisper is re-run; the hash pins the copy the rules were read from.

Full transcripts are committed here by Cody's decision (2026-10-18), with the access field kept
honest per item. A copy of the raw downloads (VTT plus trimmed metadata JSON) is also kept in the
gitignored `strategy-lab-data/corpus/`.

## Rebuilding

Needs Python 3, `yt-dlp` (any recent release; `curl_cffi` installed beside it), `node` (yt-dlp's
JavaScript runtime), `ffmpeg`, and openai-whisper with the `large-v3-turbo` model for the audio
fallback. No cookies, no login.

```bash
S=AccessibleTrader.StrategyLab/Corpus/scripts
# 1. list a channel, playlist or search (prints id|duration|title|availability|channel)
python3 -I $S/yt_list.py --yt-dlp-python PY "https://www.youtube.com/@CamelFinance/videos" --end 150
# 2. fetch: items.tsv = slug<TAB>video_id<TAB>tags<TAB>access<TAB>note
python3 -I $S/yt_fetch.py --items items.tsv --dl-root DL --log fetch.jsonl --yt-dlp-python PY --whisper-fallback
#    local video files (the Cosasverdes tutorial library is not on YouTube):
python3 -I $S/whisper_local.py --out LOCAL "video content/"*.mp4
#    public web pages:
python3 -I $S/web_fetch.py --dl-root DL --log web.jsonl SLUG ID URL "tags" public [DATE] [TITLE]
# 3. build transcripts/ and manifest.json from the logs
python3 -I $S/build_corpus.py --corpus AccessibleTrader.StrategyLab/Corpus --logs fetch.jsonl \
    --web web.jsonl --local local_items.json --channel-slugs channel_slugs.json \
    --mirror strategy-lab-data/corpus
```

To rebuild from the manifest alone, turn each record with a YouTube `url` into an `items.tsv` row
(`source`, `id`, `tags` joined by commas, `access`) and run steps 2 and 3. `yt_fetch.py` is
resumable and polite: one caption request per video, a 30-minute caption cooldown after any HTTP
429, 6 to 20 seconds between videos, a 10-minute pause after a 429 or a "confirm you're not a bot"
answer on metadata or audio and a stop after two in a row; audio goes to its own directory and is
deleted as soon as Whisper is done. On 2026-10-18 YouTube's caption endpoint answered 429 for most
of the run, so most YouTube items here are Whisper transcripts; late in the run it asked for a bot
check, the fetch was stopped, paused 40 minutes and resumed. Items it still refused are in the
manifest as not obtained with the reason. Whisper sometimes writes filler such as "We'll be right
back." over intro music; that is Whisper, not the speaker.

## How to use it

1. Read `rules/<source>.md`, pick a candidate rule, and write it as a pre-registration before
   computing anything (the `docs/SETUP_GRADER_PREREG.md` pattern). The candidate lists say which
   rules need data the lab does not have and which need a hand-drawn line.
2. The dated calls in each rules file are for scoring against prices later. Scoring has not been
   done; see `docs/TODO.md`.
3. Quote sparingly when writing findings, and cite the timestamp.
