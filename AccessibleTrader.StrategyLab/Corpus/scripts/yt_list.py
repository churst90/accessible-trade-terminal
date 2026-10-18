"""List a YouTube channel tab, playlist or search without downloading anything.

Usage:
    python3 -I yt_list.py --yt-dlp-python PY URL_OR_SEARCH [--end N]
Examples:
    yt_list.py "https://www.youtube.com/@CamelFinance/videos" --end 150
    yt_list.py "ytsearch15:walter bressert cycle"

Prints: id|duration_s|title|availability|channel   (availability 'subscriber_only' = members-only;
those are never fetched).
"""
import argparse
import subprocess
import sys

ap = argparse.ArgumentParser()
ap.add_argument("target")
ap.add_argument("--end", type=int, default=100)
ap.add_argument("--yt-dlp-python", default=sys.executable)
a = ap.parse_args()
r = subprocess.run([a.yt_dlp_python, "-I", "-m", "yt_dlp", "--flat-playlist", "--playlist-end", str(a.end),
                    "--sleep-requests", "1", "--print",
                    "%(id)s|%(duration)s|%(title)s|%(availability)s|%(channel)s", a.target],
                   capture_output=True, text=True)
sys.stdout.write(r.stdout)
if r.returncode:
    sys.stderr.write(r.stderr[-1000:])
