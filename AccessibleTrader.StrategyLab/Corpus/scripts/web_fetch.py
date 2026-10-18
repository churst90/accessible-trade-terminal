"""Fetch a public web page once and keep its readable text.

Usage:
    python3 -I web_fetch.py --dl-root DIR --log web_log.jsonl SLUG ID URL "TAGS" ACCESS [PUBLISH_DATE] [TITLE]

The raw HTML goes to a NEW directory <dl-root>/<slug>_<id>/page.html; the text (scripts, styles,
navigation, header and footer dropped) to page.txt.  One request, a browser-neutral User-Agent
naming this script, no cookies.  The record is appended to the log in the same shape yt_fetch.py
writes, with method "web page".
"""
import argparse
import html.parser
import json
import os
import time
import urllib.request

SKIP = {"script", "style", "noscript", "nav", "header", "footer", "svg", "form", "iframe"}
BLOCK = {"p", "div", "li", "h1", "h2", "h3", "h4", "h5", "h6", "br", "tr", "section", "article", "blockquote"}


class Text(html.parser.HTMLParser):
    def __init__(self):
        super().__init__()
        self.depth = 0
        self.parts = []
        self.title = ""
        self.in_title = False

    def handle_starttag(self, tag, attrs):
        if tag in SKIP:
            self.depth += 1
        if tag == "title":
            self.in_title = True
        if tag in BLOCK:
            self.parts.append("\n")

    def handle_endtag(self, tag):
        if tag in SKIP and self.depth:
            self.depth -= 1
        if tag == "title":
            self.in_title = False
        if tag in BLOCK:
            self.parts.append("\n")

    def handle_data(self, data):
        if self.in_title:
            self.title += data
        elif not self.depth:
            self.parts.append(data)


ap = argparse.ArgumentParser()
ap.add_argument("--dl-root", required=True)
ap.add_argument("--log", required=True)
ap.add_argument("slug")
ap.add_argument("id")
ap.add_argument("url")
ap.add_argument("tags")
ap.add_argument("access")
ap.add_argument("publish_date", nargs="?")
ap.add_argument("title", nargs="?")
ap.add_argument("--stop-at", action="append", default=[],
                help="drop everything from the first line equal to this text (site furniture); repeatable")
ap.add_argument("--from-html", help="re-parse a page.html saved earlier instead of requesting the URL")
a = ap.parse_args()
d = os.path.join(a.dl_root, "%s_%s" % (a.slug, a.id))
os.makedirs(d, exist_ok=True)
rec = dict(source_slug=a.slug, video_id=a.id, url=a.url, tags=a.tags, access=a.access, dir=d,
           retrieved=time.strftime("%Y-%m-%d", time.gmtime()), publish_date=a.publish_date, method="web page")
try:
    if a.from_html:
        raw = open(a.from_html, encoding="utf-8").read()
    else:
        req = urllib.request.Request(a.url, headers={"User-Agent": "Mozilla/5.0 (corpus web_fetch.py; research)"})
        raw = urllib.request.urlopen(req, timeout=60).read().decode("utf-8", errors="replace")
    if os.path.abspath(a.from_html or "") != os.path.abspath(os.path.join(d, "page.html")):
        open(os.path.join(d, "page.html"), "w", encoding="utf-8").write(raw)
    p = Text()
    p.feed(raw)
    lines = [" ".join(x.split()) for x in "".join(p.parts).split("\n")]
    lines = [x for x in lines if x]
    for stop in a.stop_at:
        if stop in lines:
            lines = lines[:lines.index(stop)]
    text = "\n".join(lines)
    open(os.path.join(d, "page.txt"), "w", encoding="utf-8").write(text + "\n")
    rec.update(status="obtained", title=a.title or p.title.strip(), text_file=os.path.join(d, "page.txt"))
except Exception as e:  # noqa: BLE001 - record the reason, never retry hard
    rec.update(status="not obtained", reason="http error: %s" % e)
with open(a.log, "a", encoding="utf-8") as f:
    f.write(json.dumps(rec) + "\n")
print(rec["status"], a.url, rec.get("reason", ""))
