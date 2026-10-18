"""Convert a subtitle track or a Whisper JSON to plain prose with [mm:ss] markers about every 30 s.

Usage:
    python3 -I to_text.py IN.vtt OUT.txt          (YouTube VTT, manual or ASR)
    python3 -I to_text.py IN.json OUT.txt         (openai-whisper JSON: uses segments[].start/text)

YouTube ASR VTT repeats each line as it scrolls; exact repeats and lines already contained in the
previous cue are dropped, in order.  Minutes are not wrapped at 60: [93:10] means 93 min 10 s.
"""
import json
import re
import sys

MARK_EVERY = 30


def mark(t):
    return "[%02d:%02d]" % (t // 60, t % 60)


def from_vtt(src):
    ts_re = re.compile(r"^(?:(\d+):)?(\d\d):(\d\d)\.\d+ --> ")
    tag_re = re.compile(r"<[^>]+>")
    out, prev, cur_t, last_mark = [], "", 0, -999
    with open(src, encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.rstrip("\n")
            m = ts_re.match(line)
            if m:
                cur_t = int(m.group(1) or 0) * 3600 + int(m.group(2)) * 60 + int(m.group(3))
                continue
            if not line.strip() or line.startswith(("WEBVTT", "Kind:", "Language:", "NOTE")) or line.strip().isdigit():
                continue
            text = tag_re.sub("", line).replace("&nbsp;", " ").replace("&amp;", "&").replace("&gt;", ">").replace("&lt;", "<").strip()
            if not text or text == prev:
                continue
            if out and text in out[-1]:
                continue
            prev = text
            if cur_t - last_mark >= MARK_EVERY:
                out.append("\n%s %s" % (mark(cur_t), text))
                last_mark = cur_t
            else:
                out.append(" " + text)
    return "".join(out).strip() + "\n"


def from_whisper(src):
    with open(src, encoding="utf-8") as f:
        segs = json.load(f)["segments"]
    out, last_mark = [], -999
    for s in segs:
        t = int(s["start"])
        text = s["text"].strip()
        if not text:
            continue
        if t - last_mark >= MARK_EVERY:
            out.append("\n%s %s" % (mark(t), text))
            last_mark = t
        else:
            out.append(" " + text)
    return "".join(out).strip() + "\n"


def convert(src, dst):
    text = from_whisper(src) if src.endswith(".json") else from_vtt(src)
    with open(dst, "w", encoding="utf-8") as g:
        g.write(text)
    return text


if __name__ == "__main__":
    convert(sys.argv[1], sys.argv[2])
