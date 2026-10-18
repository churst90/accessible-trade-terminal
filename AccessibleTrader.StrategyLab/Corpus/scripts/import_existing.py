"""Import subtitle tracks that an earlier session already downloaded, without re-downloading them.

Usage:
    python3 -I import_existing.py --map import.tsv --dl-root DIR --log fetch_log.jsonl
                                  [--yt-dlp-python PY] [--meta-only-fetch]

import.tsv columns: source_slug  video_id  existing_dir  tags  access  note

The existing .vtt (and .info.json if present) are COPIED into a new <dl-root>/<slug>_<id>/.
If the earlier download kept no .info.json and --meta-only-fetch is given, metadata alone is
fetched (yt-dlp --skip-download --write-info-json, no subtitles, no media).  Records are appended
to the same JSON-lines log yt_fetch.py writes, so build_corpus.py treats both alike.
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

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from yt_fetch import pick_subtitle, load_done  # noqa: E402

ap = argparse.ArgumentParser()
ap.add_argument("--map", required=True)
ap.add_argument("--dl-root", required=True)
ap.add_argument("--log", required=True)
ap.add_argument("--yt-dlp-python", default=sys.executable)
ap.add_argument("--meta-only-fetch", action="store_true")
a = ap.parse_args()
done = load_done(a.log)
for line in open(a.map, encoding="utf-8"):
    if not line.strip() or line.startswith("#"):
        continue
    p = line.rstrip("\n").split("\t") + [""] * 6
    slug, vid, src, tags, access, note = p[:6]
    if (slug, vid) in done and done[(slug, vid)]["status"] == "obtained":
        continue
    d = os.path.join(a.dl_root, "%s_%s" % (slug, vid))
    os.makedirs(d, exist_ok=True)
    for f in glob.glob(os.path.join(src, "*.vtt")) + glob.glob(os.path.join(src, "*.info.json")):
        name = os.path.basename(f)
        if name.startswith("sub."):
            name = vid + name[3:]
        shutil.copy2(f, os.path.join(d, name))
    url = "https://www.youtube.com/watch?v=" + vid
    rec = dict(source_slug=slug, video_id=vid, tags=tags, access=access, note=note, url=url, dir=d,
               imported_from=src, retrieved=time.strftime("%Y-%m-%d", time.gmtime(os.path.getmtime(src))))
    if not glob.glob(os.path.join(d, "*.info.json")) and a.meta_only_fetch:
        time.sleep(random.uniform(5, 12))
        subprocess.run([a.yt_dlp_python, "-I", "-m", "yt_dlp", "--skip-download", "--write-info-json",
                        "--no-playlist", "-o", os.path.join(d, "%(id)s.%(ext)s"), url],
                       capture_output=True, text=True)
    infos = glob.glob(os.path.join(d, "*.info.json"))
    info = json.load(open(infos[0], encoding="utf-8")) if infos else {}
    rec.update(title=info.get("title"), channel=info.get("channel"), channel_url=info.get("channel_url"),
               upload_date=info.get("upload_date"), duration=info.get("duration"),
               availability=info.get("availability"))
    sub, kind = pick_subtitle(d, info)
    if sub:
        rec.update(status="obtained", method="YouTube subtitles (%s)" % kind, subtitle=os.path.basename(sub))
    else:
        rec.update(status="not obtained", reason="earlier download kept no subtitles")
    with open(a.log, "a", encoding="utf-8") as f:
        f.write(json.dumps(rec) + "\n")
    print(rec["status"], slug, vid, rec.get("method") or rec.get("reason"), "|", (rec.get("title") or "")[:70])
