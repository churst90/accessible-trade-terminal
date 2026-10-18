"""Build Corpus/transcripts/ and Corpus/manifest.json from the fetch logs.

Usage:
    python3 -I build_corpus.py --corpus CORPUS_DIR --logs a.jsonl [b.jsonl ...]
                               [--local local_items.json] [--web web_log.jsonl]
                               [--mirror STRATEGY_LAB_DATA/corpus]

Inputs
  * JSON-lines logs written by yt_fetch.py / import_existing.py / web_fetch.py (last record per
    (source_slug, video_id) wins).
  * local_items.json: a list of records for local files transcribed by whisper_local.py, each with
    source_slug, video_id (a slug), title, channel, publish_date, whisper_json, whisper_vtt, file,
    tags, access, note.

Outputs
  * CORPUS_DIR/transcripts/<source_slug>/<YYYY-MM-DD>_<id>.txt (prose, [mm:ss] about every 30 s)
    and, where one exists, the .vtt next to it (YouTube's track, or Whisper's own VTT).
  * CORPUS_DIR/manifest.json: one record per item, obtained or not.
  * --mirror: the same .txt/.vtt plus the trimmed metadata JSON, copied to a second folder.
Publish dates unknown to the source are written as 0000-00-00 in file names.
"""
import argparse
import hashlib
import json
import os
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from to_text import convert  # noqa: E402

# Which school of trading each source belongs to. Sources are never blended across schools
# (rules/SCHOOLS.md): structure = trend structure, horizontals, level reactions; market-cipher =
# Market Cipher / WaveTrend traders; cycle = cycle traders and DSS Bressert material.
SCHOOLS = {
    "cosasverdes": "structure",
    "camel-finance": "cycle", "bob-loukas": "cycle", "bressert-dss": "cycle", "hurst-cycles": "cycle",
    "likesmoney": "cycle",
    "cryptoface": "market-cipher", "market-cipher": "market-cipher", "jason-casper": "market-cipher",
    "trader-geo": "market-cipher", "trade-pro": "market-cipher", "daviddtech": "market-cipher",
    "trading-bureau": "market-cipher", "dylan-waves": "market-cipher", "gunslinger-trades": "market-cipher",
    "frankie-candles": "market-cipher", "alexocrypto": "market-cipher", "chart-wis": "market-cipher",
    "dbsi-reviewers": "market-cipher", "gainium": "market-cipher", "market-cipher-educators": "market-cipher",
}

TRIM = ("id", "title", "channel", "channel_url", "uploader", "upload_date", "duration", "availability",
        "description", "categories", "tags", "webpage_url", "language", "was_live", "live_status")


def hms(s):
    if s is None:
        return None
    s = int(s)
    return "%d:%02d:%02d" % (s // 3600, s % 3600 // 60, s % 60)


def iso(d):
    if not d:
        return None
    d = str(d)
    if len(d) == 8 and d.isdigit():
        return "%s-%s-%s" % (d[:4], d[4:6], d[6:])
    return d


def sha(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        h.update(f.read())
    return h.hexdigest()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--corpus", required=True)
    ap.add_argument("--logs", nargs="+", default=[])
    ap.add_argument("--local")
    ap.add_argument("--web", nargs="*", default=[])
    ap.add_argument("--mirror")
    ap.add_argument("--channel-slugs", help="JSON {channel name: source slug} for items logged as 'auto'")
    ap.add_argument("--listings", nargs="*", default=[], help="yt_list.py outputs, to title refused items")
    a = ap.parse_args()

    recs = {}
    for log in a.logs + (a.web or []):
        with open(log, encoding="utf-8") as f:
            for line in f:
                if line.strip():
                    r = json.loads(line)
                    recs[(r["source_slug"], r["video_id"])] = r
    if a.local:
        for r in json.load(open(a.local, encoding="utf-8")):
            r.setdefault("status", "obtained")
            recs[(r["source_slug"], r["video_id"])] = r
    # Items fetched with source_slug "auto" take their slug from the channel name.
    if a.channel_slugs:
        cmap = json.load(open(a.channel_slugs, encoding="utf-8"))
        fixed = {}
        for (slug, vid), r in recs.items():
            if slug == "auto":
                slug = cmap.get((r.get("channel") or "").strip(), "other")
                r["source_slug"] = slug
            fixed.setdefault((slug, vid), r)
        recs = fixed
    # One video, one record: if two sources list the same id, keep an obtained one, then the first
    # by source name.
    seen = {}
    for key in sorted(recs, key=lambda k: (recs[k]["status"] != "obtained", k)):
        seen.setdefault(key[1], key)
    recs = {k: recs[k] for k in seen.values()}

    # Titles/durations for items YouTube refused: taken from yt_list.py listings (id|duration|title|...).
    listed = {}
    for path in a.listings:
        for line in open(path, encoding="utf-8"):
            p = line.rstrip("\n").split("|")
            if len(p) >= 3 and not line.startswith(("#", "WARNING", "ERROR")):
                if len(p) >= 6 and p[1] == "NA" and p[2] == "NA":   # id|date|ts|duration|title|availability
                    dur, title = p[3], p[4]
                else:                                               # id|duration|title|availability|channel
                    dur, title = p[1], p[2]
                listed.setdefault(p[0], (int(dur) if dur.isdigit() else None, title))
    for r in recs.values():
        if r["status"] != "obtained" and r["video_id"] in listed:
            dur, title = listed[r["video_id"]]
            r.setdefault("title", title)
            r["title"] = r.get("title") or title
            r["duration"] = r.get("duration") or dur

    manifest = []
    tdir = os.path.join(a.corpus, "transcripts")
    written = set()
    for (slug, vid), r in sorted(recs.items(), key=lambda kv: (kv[0][0], iso(kv[1].get("upload_date") or kv[1].get("publish_date")) or "", kv[0][1])):
        date = iso(r.get("upload_date") or r.get("publish_date"))
        m = dict(id=vid, source=slug, school=SCHOOLS.get(slug, "other"), channel=(r.get("channel") or "").strip() or None,
                 title=r.get("title"),
                 url=r.get("url") or "local file, no public URL", publish_date=date,
                 duration_s=r.get("duration"), duration=hms(r.get("duration")),
                 obtained=r["status"] == "obtained", method=r.get("method"),
                 transcript=None, vtt=None, sha256=None, words=None,
                 retrieved=r.get("retrieved"), tags=[t for t in (r.get("tags") or "").split(",") if t]
                 if isinstance(r.get("tags"), str) else r.get("tags") or [],
                 access=r.get("access") or "unknown")
        if r.get("availability") and r.get("availability") not in ("public",):
            m["youtube_availability"] = r.get("availability")
        if r["status"] != "obtained":
            m["not_obtained_reason"] = r.get("reason")
        if r.get("note"):
            m["note"] = r["note"]
        if r.get("caption_note"):
            m["why_whisper"] = r["caption_note"]
        if r["status"] == "obtained":
            name = "%s_%s" % (date or "0000-00-00", vid)
            os.makedirs(os.path.join(tdir, slug), exist_ok=True)
            txt = os.path.join(tdir, slug, name + ".txt")
            if r.get("whisper_json"):
                src = os.path.join(r["dir"], r["whisper_json"]) if r.get("dir") else r["whisper_json"]
                convert(src, txt + ".tmp")
                vsrc = os.path.join(r["dir"], r["subtitle"]) if r.get("dir") else r.get("whisper_vtt")
            elif r.get("text_file"):
                shutil.copyfile(r["text_file"], txt + ".tmp")
                vsrc = None
            else:
                vsrc = os.path.join(r["dir"], r["subtitle"])
                convert(vsrc, txt + ".tmp")
            os.replace(txt + ".tmp", txt)  # atomic: readers never see a half-written file
            written.add(txt)
            m["transcript"] = os.path.relpath(txt, a.corpus)
            if vsrc and os.path.exists(vsrc):
                vdst = os.path.join(tdir, slug, name + ".vtt")
                shutil.copyfile(vsrc, vdst + ".tmp")
                os.replace(vdst + ".tmp", vdst)
                written.add(vdst)
                m["vtt"] = os.path.relpath(vdst, a.corpus)
            m["sha256"] = sha(txt)
            with open(txt, encoding="utf-8") as f:
                m["words"] = len([w for w in f.read().split() if not (w.startswith("[") and w.endswith("]"))])
            if a.mirror:
                mdir = os.path.join(a.mirror, slug)
                os.makedirs(mdir, exist_ok=True)
                shutil.copyfile(txt, os.path.join(mdir, name + ".txt"))
                if m["vtt"]:
                    shutil.copyfile(os.path.join(a.corpus, m["vtt"]), os.path.join(mdir, name + ".vtt"))
                info = None
                if r.get("dir"):
                    for fn in os.listdir(r["dir"]):
                        if fn.endswith(".info.json"):
                            info = json.load(open(os.path.join(r["dir"], fn), encoding="utf-8"))
                if info:
                    with open(os.path.join(mdir, name + ".json"), "w", encoding="utf-8") as g:
                        json.dump({k: info.get(k) for k in TRIM}, g, indent=1)
        manifest.append(m)

    # transcripts/ is fully generated: drop files no record wrote (e.g. after a source was renamed).
    for root, _, files in os.walk(tdir):
        for fn in files:
            if os.path.join(root, fn) not in written:
                os.remove(os.path.join(root, fn))
    for d in os.listdir(tdir):
        if not os.listdir(os.path.join(tdir, d)):
            os.rmdir(os.path.join(tdir, d))

    with open(os.path.join(a.corpus, "manifest.json"), "w", encoding="utf-8") as g:
        json.dump(dict(generated_by="scripts/build_corpus.py", items=manifest), g, indent=1, ensure_ascii=False)
        g.write("\n")
    ok = [m for m in manifest if m["obtained"]]
    print("items", len(manifest), "obtained", len(ok), "words", sum(m["words"] for m in ok),
          "hours %.1f" % (sum(m["duration_s"] or 0 for m in ok) / 3600))


if __name__ == "__main__":
    main()
