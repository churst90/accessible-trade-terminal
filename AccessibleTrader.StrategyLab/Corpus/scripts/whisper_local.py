"""Transcribe local video files with openai-whisper (JSON + VTT, so timestamps survive).

Usage:
    python3 -I whisper_local.py --out DIR [--whisper BIN] [--model large-v3-turbo] FILE [FILE ...]

Each FILE is converted to a temporary 16 kHz mono WAV in DIR, transcribed, and the WAV is deleted.
Output: DIR/<stem>.json and DIR/<stem>.vtt (whisper's own names), where <stem> is t1..t8 for files
named "Tutorial N - ...", otherwise the file stem.  Used for the eight Cosasverdes tutorial videos
in the repo's "video content/" folder (not in git; Cody's local copies).
"""
import argparse
import os
import re
import subprocess

ap = argparse.ArgumentParser()
ap.add_argument("--out", required=True)
ap.add_argument("--whisper", default=os.path.expanduser("~/voice/venv/bin/whisper"))
ap.add_argument("--model", default="large-v3-turbo")
ap.add_argument("files", nargs="+")
a = ap.parse_args()
os.makedirs(a.out, exist_ok=True)
for f in a.files:
    stem = os.path.splitext(os.path.basename(f))[0]
    m = re.match(r"Tutorial (\d+)", stem)
    if m:
        stem = "t" + m.group(1)
    if os.path.exists(os.path.join(a.out, stem + ".json")):
        print("skip", stem)
        continue
    wav = os.path.join(a.out, stem + ".wav")
    subprocess.run(["ffmpeg", "-nostdin", "-loglevel", "error", "-y", "-i", f, "-ac", "1", "-ar", "16000", wav],
                   check=True)
    try:
        subprocess.run([a.whisper, wav, "--model", a.model, "--language", "en", "--output_format", "all",
                        "--output_dir", a.out, "--verbose", "False"], check=True)
    finally:
        os.remove(wav)
    for ext in (".srt", ".tsv"):
        p = os.path.join(a.out, stem + ext)
        if os.path.exists(p):
            os.remove(p)
    print("done", stem, f, flush=True)
