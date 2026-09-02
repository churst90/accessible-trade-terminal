#!/usr/bin/env python3
"""WCAG 2.x contrast for the bc52e652 review.
Parses AccessibleTrader.Core/Services/ThemeService.cs so every theme value comes from source."""
import re, sys

SRC = "/home/cody/external-rescue/Github/accessible-trade-terminal/AccessibleTrader.Core/Services/ThemeService.cs"

def lin(c):
    v = c / 255.0
    return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4

def lum(rgb):
    r, g, b = rgb
    return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b)

def ratio(a, b):
    la, lb = lum(a), lum(b)
    hi, lo = max(la, lb), min(la, lb)
    return (hi + 0.05) / (lo + 0.05)

def hexs(rgb):
    return "#%02x%02x%02x" % tuple(rgb)

def parse_hex(h):
    h = h.lstrip('#')
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16))

def composite(fg, alpha, bg):
    """sRGB-space alpha blend, as a browser composites an rgba() over an opaque background."""
    return tuple(round(alpha * f + (1 - alpha) * b) for f, b in zip(fg, bg))

NAMED = {"Black": (0, 0, 0), "White": (255, 255, 255), "Yellow": (255, 255, 0)}
DEFAULTS = {  # ChartTheme.cs init defaults for chrome fields a theme may omit
    "SurfaceRaised": (30, 30, 30), "ChromeBottom": (24, 24, 24), "TextMuted": (170, 170, 170),
    "BackgroundGradientEnd": None, "ChromeBottomEnd": None, "ChromeTopEnd": None,
}
FIELDS = ["Background", "BackgroundGradientEnd", "AxisText", "Crosshair", "SurfaceRaised",
          "ChromeTopEnd", "ChromeBottom", "ChromeBottomEnd", "TextMuted"]

def parse_color(expr):
    expr = expr.strip()
    if expr == "null":
        return None
    m = re.match(r"SKColors\.(\w+)", expr)
    if m:
        return NAMED[m.group(1)]
    m = re.match(r"new SKColor\(\s*([^,]+),\s*([^,]+),\s*([^,\)]+)(?:,\s*([^\)]+))?\)", expr)
    if m:
        vals = [int(x.strip(), 0) for x in m.groups()[:3]]
        if m.group(4) is not None:
            raise SystemExit(f"translucent theme colour not expected here: {expr}")
        return tuple(vals)
    raise SystemExit(f"unparsed colour expr: {expr}")

def parse_themes():
    text = open(SRC).read()
    blocks = re.split(r"private static ChartTheme (\w+)\(\) => new\(\)", text)
    themes = []
    for i in range(1, len(blocks), 2):
        name, body = blocks[i], blocks[i + 1]
        body = body.split("};")[0]
        t = {"name": name}
        for f in FIELDS:
            m = re.search(rf"^\s*{f}\s*=\s*(.+)$", body, re.M)
            if m:
                expr = m.group(1).split("//")[0].strip().rstrip(",").strip()
                t[f] = parse_color(expr)
            elif f in DEFAULTS:
                t[f] = DEFAULTS[f]
            else:
                raise SystemExit(f"{name}: required field {f} not found")
        themes.append(t)
    return themes

def naive_lum(rgb):  # ThemeCssBridge.Luminance: sRGB coefficients, NO gamma
    r, g, b = rgb
    return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0

def focus_ring_for(t):  # ThemeCssBridge.FocusRingFor
    return (0, 32, 176) if naive_lum(t["SurfaceRaised"]) > 0.5 else (255, 255, 0)

def pf(r, th):
    return "PASS" if r >= th else "FAIL"

def main():
    themes = parse_themes()
    print(f"Parsed {len(themes)} themes from ThemeService.cs: {', '.join(t['name'] for t in themes)}\n")

    # Self-check against the commit's own claimed numbers
    print("== Self-check vs numbers claimed in commit bc52e652 / audit 3.8 ==")
    for t in themes:
        if t["name"] in ("HighContrastLight", "Paper", "SteelGray"):
            bg = t["Background"]
            old_x = composite((255, 255, 255), 0.45, bg)
            print(f"  {t['name']:18} old #fff header on bg {hexs(bg)}: {ratio((255,255,255), bg):.2f}:1 | "
                  f"old crosshair rgba(255,255,255,.45)->{hexs(old_x)}: {ratio(old_x, bg):.2f}:1 | "
                  f"new AxisText: {ratio(t['AxisText'], bg):.2f}:1 | new Crosshair: {ratio(t['Crosshair'], bg):.2f}:1")
    print()

    # Q1: status header (AxisText) vs chart background (theme.Background) — the overlay is a flat fill
    print("== Q1: status header colour (theme.AxisText, via GetThemeTextHex) vs chart background (theme.Background) — 4.5:1 ==")
    print(f"  {'theme':18} {'text':8} {'bg':8} {'ratio':>7}  4.5:1   | sibling: TextMuted(--text-muted) vs bg  4.5:1")
    for t in themes:
        r = ratio(t["AxisText"], t["Background"])
        rm = ratio(t["TextMuted"], t["Background"])
        print(f"  {t['name']:18} {hexs(t['AxisText'])} {hexs(t['Background'])} {r:7.2f}  {pf(r,4.5):5}  | {hexs(t['TextMuted'])} {rm:7.2f}  {pf(rm,4.5)}")
    print()

    # Q2: crosshair vs chart background (top AND gradient end where the theme has one) — 3:1
    print("== Q2a: theme.Crosshair (--crosshair-color) vs chart background — 3:1 (1.4.11) ==")
    print(f"  {'theme':18} {'xhair':8} {'bg-top':8} {'ratio':>7}  3:1    | {'bg-end':8} {'ratio':>7}  3:1")
    for t in themes:
        r = ratio(t["Crosshair"], t["Background"])
        end = t["BackgroundGradientEnd"]
        e = f"{hexs(end)} {ratio(t['Crosshair'], end):7.2f}  {pf(ratio(t['Crosshair'], end),3)}" if end else "(flat)"
        print(f"  {t['name']:18} {hexs(t['Crosshair'])} {hexs(t['Background'])} {r:7.2f}  {pf(r,3):5}  | {e}")
    print()
    print("== Q2b: :root fallback #ffd65c vs every theme's chart background — 3:1 ==")
    fb = parse_hex("#ffd65c")
    for t in themes:
        r = ratio(fb, t["Background"])
        end = t["BackgroundGradientEnd"]
        e = f"| bg-end {hexs(end)} {ratio(fb, end):7.2f}  {pf(ratio(fb, end),3)}" if end else ""
        print(f"  {t['name']:18} #ffd65c  {hexs(t['Background'])} {r:7.2f}  {pf(r,3):5}  {e}")
    print()

    # Q3: focus ring
    print("== Q3: focus ring (ThemeCssBridge.FocusRingFor) vs chart bg, chart bg-end, page bg (--bg-primary = ChromeBottomEnd ?? ChromeBottom), toolbar (--bg-toolbar = SurfaceRaised) — 3:1 ==")
    print(f"  {'theme':18} {'ring':8} {'chart':8} {'ratio':>6} {'':5} {'chart-end':9} {'ratio':>6} {'':5} {'page':8} {'ratio':>6} {'':5} {'toolbar':8} {'ratio':>6}")
    for t in themes:
        ring = focus_ring_for(t)
        page = t["ChromeBottomEnd"] or t["ChromeBottom"]
        end = t["BackgroundGradientEnd"] or t["Background"]
        r1, r2, r3, r4 = ratio(ring, t["Background"]), ratio(ring, end), ratio(ring, page), ratio(ring, t["SurfaceRaised"])
        print(f"  {t['name']:18} {hexs(ring)} {hexs(t['Background'])} {r1:6.2f} {pf(r1,3):5} {hexs(end):9} {r2:6.2f} {pf(r2,3):5} {hexs(page)} {r3:6.2f} {pf(r3,3):5} {hexs(t['SurfaceRaised'])} {r4:6.2f} {pf(r4,3)}")
    print()
    print("== Q3b: :root fallback ring #ffff00 (what renders before the bridge publishes) vs chart bg / page bg — 3:1 ==")
    yl = (255, 255, 0)
    for t in themes:
        page = t["ChromeBottomEnd"] or t["ChromeBottom"]
        r1, r3 = ratio(yl, t["Background"]), ratio(yl, page)
        print(f"  {t['name']:18} #ffff00  chart {hexs(t['Background'])} {r1:6.2f} {pf(r1,3):5}  page {hexs(page)} {r3:6.2f} {pf(r3,3)}")
    print()

    # Adjacent, same overlay: hover readout box and progress bar literals still in ChartArea
    print("== Adjacent literals still in ChartArea.razor (not this commit's fix, reported for completeness) ==")
    for t in themes:
        bg = t["Background"]
        box = composite((18, 18, 18), 0.92, bg)
        print(f"  {t['name']:18} readout #fff on rgba(18,18,18,.92)->{hexs(box)}: {ratio((255,255,255), box):5.2f}:1 {pf(ratio((255,255,255), box),4.5)} | "
              f"#bbb on box: {ratio((0xbb,0xbb,0xbb), box):5.2f}:1 {pf(ratio((0xbb,0xbb,0xbb), box),4.5)} | "
              f"progress #0078d4 on #222 track: {ratio((0,0x78,0xd4),(0x22,0x22,0x22)):5.2f}:1 | track #222 on bg: {ratio((0x22,0x22,0x22), bg):5.2f}:1")

if __name__ == "__main__":
    main()
