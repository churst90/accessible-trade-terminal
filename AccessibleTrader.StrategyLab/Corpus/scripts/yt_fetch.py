"""Politely fetch YouTube subtitles (and metadata) for a list of videos, with a Whisper fallback.

Usage:
    python3 -I yt_fetch.py --items items.tsv --dl-root DIR --log fetch_log.jsonl
                           [--yt-dlp-python PY] [--whisper-fallback] [--whisper BIN]
                           [--model large-v3-turbo] [--min-sleep 8] [--max-sleep 20]
                           [--caption-cooldown 1800]

items.tsv columns (tab separated, '#' lines ignored):
    source_slug  video_id  tags(comma list)  access  note

For each item a NEW directory <dl-root>/<source_slug>_<video_id>/ is created.  Nothing is ever
executed from a download directory.

Order of attempts per item:
  1. Metadata only (--skip-download --write-info-json).  Members-only, private, removed and
     age-restricted videos stop here and are logged "not obtained" with the reason.
  2. English subtitle track (manual if the uploader provided one, YouTube ASR otherwise), unless
     the caption endpoint answered HTTP 429 within the last --caption-cooldown seconds.  One try
     only: a 429 starts the cooldown, it is never retried in a loop.
  3. With --whisper-fallback: the audio stream only, into a separate <dir>_audio/ directory,
     transcribed with openai-whisper (JSON + VTT written into the item directory); the audio
     directory is deleted straight after, whatever happened.

Politeness: yt-dlp's --sleep-requests / --sleep-subtitles, a random --min-sleep..--max-sleep pause
between videos, and a 10-minute pause after a 429 on metadata or audio (two in a row stop the run).
No cookies, no login, nothing members-only.

The log is JSON-lines and the script is resumable: items already logged as obtained, or as not
obtained for a permanent reason, are skipped on a re-run.
"""
import argparse
import glob
import json
import os
import random
import shutil
import subprocess
import sys
import time

PERMANENT = ("members-only", "private", "unavailable", "age-restricted", "removed")


def classify_error(text):
    t = text.lower()
    if "429" in t or "too many requests" in t:
        return "http-429"
    if "members-only" in t or "join this channel" in t or "available to this channel's members" in t:
        return "members-only"
    if "private video" in t:
        return "private"
    if "sign in to confirm your age" in t or "age-restricted" in t:
        return "age-restricted"
    if "video unavailable" in t or "has been removed" in t or "not available" in t:
        return "unavailable"
    if "sign in to confirm you" in t and "bot" in t:
        return "bot-check"
    return "error"


def load_done(log_path):
    done = {}
    if os.path.exists(log_path):
        with open(log_path, encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if line:
                    rec = json.loads(line)
                    done[(rec["source_slug"], rec["video_id"])] = rec
    return done


def pick_subtitle(item_dir, info):
    """Return (path, kind) where kind is 'manual' or 'ASR'."""
    vtts = sorted(glob.glob(os.path.join(item_dir, "*.vtt")))
    vtts = [v for v in vtts if not os.path.basename(v).startswith("audio.")]
    manual_langs = [k for k in (info.get("subtitles") or {}) if k.startswith("en") and k != "live_chat"]
    for lang in manual_langs:
        for v in vtts:
            if v.endswith(".%s.vtt" % lang):
                return v, "manual"
    for suffix in (".en-orig.vtt", ".en.vtt"):
        for v in vtts:
            if v.endswith(suffix):
                return v, "ASR"
    if vtts:
        return vtts[0], "ASR"
    return None, None


def run(cmd, timeout=900):
    try:
        r = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
        return r.returncode, r.stdout + "\n" + r.stderr
    except subprocess.TimeoutExpired:
        return 124, "timeout"


def has_english_captions(info):
    keys = list((info.get("subtitles") or {}).keys()) + list((info.get("automatic_captions") or {}).keys())
    return any(k == "en" or k.startswith("en-") for k in keys)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--items", required=True)
    ap.add_argument("--dl-root", required=True)
    ap.add_argument("--log", required=True)
    ap.add_argument("--yt-dlp-python", default=sys.executable,
                    help="python interpreter that has yt_dlp installed")
    ap.add_argument("--whisper-fallback", action="store_true")
    ap.add_argument("--whisper", default=os.path.expanduser("~/voice/venv/bin/whisper"))
    ap.add_argument("--model", default="large-v3-turbo")
    ap.add_argument("--min-sleep", type=float, default=8)
    ap.add_argument("--max-sleep", type=float, default=20)
    ap.add_argument("--caption-cooldown", type=float, default=1800)
    args = ap.parse_args()

    os.makedirs(args.dl_root, exist_ok=True)
    done = load_done(args.log)
    items = []
    with open(args.items, encoding="utf-8") as f:
        for line in f:
            if not line.strip() or line.startswith("#"):
                continue
            parts = line.rstrip("\n").split("\t")
            parts += [""] * (5 - len(parts))
            items.append(dict(source_slug=parts[0], video_id=parts[1], tags=parts[2],
                              access=parts[3], note=parts[4]))

    ytdlp = [args.yt_dlp_python, "-I", "-m", "yt_dlp", "--js-runtimes", "node",
             "--sleep-requests", "1.5", "--sleep-subtitles", "3", "--retries", "2", "--no-playlist"]
    captions_blocked_until = 0.0
    hard_429 = 0
    first = True
    for it in items:
        key = (it["source_slug"], it["video_id"])
        prev = done.get(key)
        if prev and (prev["status"] == "obtained" or prev.get("reason") in PERMANENT):
            continue
        if not first:
            time.sleep(random.uniform(args.min_sleep, args.max_sleep))
        first = False
        url = "https://www.youtube.com/watch?v=" + it["video_id"]
        item_dir = os.path.join(args.dl_root, "%s_%s" % key)
        os.makedirs(item_dir, exist_ok=True)
        rec = dict(it, url=url, dir=item_dir, retrieved=time.strftime("%Y-%m-%d", time.gmtime()))
        out_tpl = os.path.join(item_dir, "%(id)s.%(ext)s")

        # 1. metadata
        infos = glob.glob(os.path.join(item_dir, "*.info.json"))
        if not infos:
            code, out = run(ytdlp + ["--skip-download", "--write-info-json", "-o", out_tpl, url])
            infos = glob.glob(os.path.join(item_dir, "*.info.json"))
            if not infos:
                reason = classify_error(out)
                rec.update(status="not obtained", reason=reason, detail=out.strip()[-400:])
                with open(args.log, "a", encoding="utf-8") as f:
                    f.write(json.dumps(rec) + "\n")
                print("not obtained", key, reason, flush=True)
                if reason in ("http-429", "bot-check"):
                    hard_429 += 1
                    if hard_429 >= 2:
                        print("two metadata refusals (429 or bot check) in a row: stopping politely", flush=True)
                        return
                    time.sleep(600)
                continue
        hard_429 = 0
        with open(infos[0], encoding="utf-8") as f:
            info = json.load(f)
        rec.update(title=info.get("title"), channel=info.get("channel"),
                   channel_url=info.get("channel_url"), upload_date=info.get("upload_date"),
                   duration=info.get("duration"), availability=info.get("availability"))
        if info.get("availability") in ("subscriber_only", "premium_only", "needs_auth"):
            rec.update(status="not obtained", reason="members-only")
            with open(args.log, "a", encoding="utf-8") as f:
                f.write(json.dumps(rec) + "\n")
            print("not obtained", key, "members-only", flush=True)
            continue

        # 2. subtitles (one try, cooldown on 429)
        sub, kind = pick_subtitle(item_dir, info)
        caption_note = None
        if not sub and has_english_captions(info):
            if time.time() < captions_blocked_until:
                caption_note = "YouTube captions exist but the caption endpoint was rate-limiting (HTTP 429); not requested"
            else:
                time.sleep(random.uniform(3, 6))
                code, out = run(ytdlp + ["--skip-download", "--write-subs", "--write-auto-subs",
                                         "--sub-langs", "en.*,-live_chat", "--sub-format", "vtt",
                                         "-o", out_tpl, url])
                sub, kind = pick_subtitle(item_dir, info)
                if not sub and classify_error(out) == "http-429":
                    captions_blocked_until = time.time() + args.caption_cooldown
                    caption_note = "YouTube captions exist but the caption endpoint answered HTTP 429"
                    print("caption 429: cooling down captions for", int(args.caption_cooldown), "s", flush=True)
        elif not sub:
            caption_note = "no English captions on YouTube"
        if sub:
            rec.update(status="obtained", method="YouTube subtitles (%s)" % kind, subtitle=os.path.basename(sub))
        elif args.whisper_fallback:
            # 3. audio only -> Whisper -> delete audio
            audio_dir = item_dir + "_audio"
            os.makedirs(audio_dir, exist_ok=True)
            try:
                audio = []
                # One alternative format on a 403 (googlevideo refuses some format/client pairs).
                for fmt in ("bestaudio[ext=m4a]/bestaudio", "bestaudio[ext=webm]/worstaudio"):
                    code, aout = run(ytdlp + ["-f", fmt, "-o", os.path.join(audio_dir, "audio.%(ext)s"), url],
                                     timeout=3600)
                    audio = [a for a in glob.glob(os.path.join(audio_dir, "audio.*")) if not a.endswith(".part")]
                    if audio or "403" not in aout:
                        break
                    time.sleep(random.uniform(5, 10))
                if audio:
                    code, wout = run([args.whisper, audio[0], "--model", args.model, "--language", "en",
                                      "--output_format", "all", "--output_dir", item_dir,
                                      "--verbose", "False"], timeout=7200)
                    for ext in ("srt", "tsv", "txt"):
                        p = os.path.join(item_dir, "audio." + ext)
                        if os.path.exists(p):
                            os.remove(p)
                    if os.path.exists(os.path.join(item_dir, "audio.json")):
                        rec.update(status="obtained", method="Whisper %s" % args.model,
                                   whisper_json="audio.json", subtitle="audio.vtt", caption_note=caption_note)
                    else:
                        rec.update(status="not obtained", reason="whisper-failed", detail=wout[-400:])
                else:
                    reason = classify_error(aout)
                    rec.update(status="not obtained", reason="audio download failed: " + reason,
                               detail=aout[-400:], caption_note=caption_note)
                    if reason in ("http-429", "bot-check"):
                        time.sleep(600)
            finally:
                shutil.rmtree(audio_dir, ignore_errors=True)
        else:
            rec.update(status="not obtained", reason=caption_note or "no subtitles")
        with open(args.log, "a", encoding="utf-8") as f:
            f.write(json.dumps(rec) + "\n")
        print(rec["status"], it["source_slug"], it["video_id"], rec.get("reason") or rec.get("method"),
              "|", (rec.get("title") or "")[:70], flush=True)


if __name__ == "__main__":
    main()
