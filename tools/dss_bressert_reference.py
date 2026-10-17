"""Independent reference ports of DSS Bressert, for cross-checking the C# provider.

Two ports, written from the published sources and NOT from the C# code:

  mt4  - a line-by-line port of MetaQuotes' "DSS Bressert.mq4" (2008), which indexes bars as a
         time series (0 = newest) and smooths with  buf[i] = k*(x - buf[i+1]) + buf[i+1],
         k = 2/(1+EMA_period).  The seed is the value buf[] holds one bar before the first
         computed bar: 0 in the MT4 file (uninitialised buffer), 50 in the MT5 file.
  pine - TradingView's form (Astral Vision FtRMJcRd, Roul_Charts v3qBH25e):
             xPreCalc = ta.ema(ta.stoch(close, high, low, PDS), EMAlen)
             xDSS     = ta.ema(ta.stoch(xPreCalc, xPreCalc, xPreCalc, PDS), EMAlen)
             trigger  = ta.ema(xDSS, TriggerLen)
         with ta.stoch = na until PDS bars exist, ta.ema seeded with its first non-na input
         (the pine_ema() definition in the Pine reference manual).

Usage:  python3 -I tools/dss_bressert_reference.py <snapshot.json> <out.csv> [P] [E] [T]

The DssBressertProviderTests reference numbers came from this, run on bars 3200-3359 (sliced out as a series of their own) of
strategy-lab-data/bitstamp_BTC_USDT_1d.json (2020-05-22 to 2020-10-28) at 13/8/5.
"""
import json
import math
import sys

NAN = float("nan")


def load(path):
    with open(path) as f:
        d = json.load(f)
    bars = d["Bars"] if isinstance(d, dict) else d
    return [(b["Date"], float(b["High"]), float(b["Low"]), float(b["Close"])) for b in bars]


def mt4(bars, P, E, seed=0.0, guard_zero=True):
    """Port of DSS Bressert.mq4 start(). Arrays are chronological here; series index s = n-1-t."""
    n = len(bars)
    k = 2.0 / (1.0 + E)
    H = [b[1] for b in bars]
    L = [b[2] for b in bars]
    C = [b[3] for b in bars]
    # limit = Bars - Stochastic_period  (series index) -> the oldest computed bar is
    # chronological t0 = n-1-limit = P-1.  buf[limit+1] (chronological t0-1) is the seed.
    t0 = P - 1
    mit = [NAN] * n
    dss = [NAN] * n
    mit[t0 - 1] = seed
    dss[t0 - 1] = seed
    for t in range(t0, n):
        hi = max(H[t - P + 1:t + 1])
        lo = min(L[t - P + 1:t + 1])
        if hi - lo == 0:
            if not guard_zero:
                raise ZeroDivisionError(t)
            mit[t] = mit[t - 1]
            continue
        x = (C[t] - lo) / (hi - lo) * 100.0
        mit[t] = k * (x - mit[t - 1]) + mit[t - 1]
    # ArrayMaximum(MitBuffer, P, i) over series i..i+P-1 = chronological t-P+1..t; on the oldest
    # bars that window reaches back past t0 into buffer slots that hold 0 (MT4 EMPTY init).
    def mitv(t):
        if t < 0:
            return 0.0
        v = mit[t]
        return 0.0 if math.isnan(v) else v
    for t in range(t0, n):
        w = [mitv(j) for j in range(t - P + 1, t + 1)]
        hi, lo = max(w), min(w)
        if hi - lo == 0:
            if not guard_zero:
                raise ZeroDivisionError(t)
            dss[t] = dss[t - 1]
            continue
        x = (mit[t] - lo) / (hi - lo) * 100.0
        dss[t] = k * (x - dss[t - 1]) + dss[t - 1]
    return mit, dss


def pine_ema(src, length):
    a = 2.0 / (length + 1.0)
    out = [NAN] * len(src)
    s = NAN
    for i, x in enumerate(src):
        if math.isnan(x):
            # Pine: alpha*na + ... = na.  The reference pine_ema then restarts from the next
            # non-na source.  Our provider instead HOLDS; record which one this port does.
            out[i] = NAN
            s = NAN
            continue
        s = x if math.isnan(s) else a * x + (1 - a) * s
        out[i] = s
    return out


def pine_stoch(src, hi_src, lo_src, length):
    n = len(src)
    out = [NAN] * n
    for i in range(n):
        if i < length - 1:
            continue
        hw = hi_src[i - length + 1:i + 1]
        lw = lo_src[i - length + 1:i + 1]
        if any(math.isnan(v) for v in hw) or any(math.isnan(v) for v in lw) or math.isnan(src[i]):
            continue
        hh, ll = max(hw), min(lw)
        if hh - ll == 0:
            continue  # Pine: division by zero -> na
        out[i] = 100.0 * (src[i] - ll) / (hh - ll)
    return out


def pine(bars, P, E, T):
    H = [b[1] for b in bars]
    L = [b[2] for b in bars]
    C = [b[3] for b in bars]
    pre = pine_ema(pine_stoch(C, H, L, P), E)
    d = pine_ema(pine_stoch(pre, pre, pre, P), E)
    trig = pine_ema(d, T)
    return pre, d, trig


def main():
    path, out = sys.argv[1], sys.argv[2]
    P = int(sys.argv[3]) if len(sys.argv) > 3 else 13
    E = int(sys.argv[4]) if len(sys.argv) > 4 else 8
    T = int(sys.argv[5]) if len(sys.argv) > 5 else 5
    bars = load(path)
    m_mit, m_dss = mt4(bars, P, E, seed=0.0)
    m5_mit, m5_dss = mt4(bars, P, E, seed=50.0)
    p_pre, p_dss, p_trig = pine(bars, P, E, T)
    with open(out, "w") as f:
        f.write("i,date,high,low,close,mt4_mit,mt4_dss,mt5_mit,mt5_dss,pine_pre,pine_dss,pine_trig\n")
        for i, b in enumerate(bars):
            vals = [m_mit[i], m_dss[i], m5_mit[i], m5_dss[i], p_pre[i], p_dss[i], p_trig[i]]
            f.write(f"{i},{b[0]},{b[1]!r},{b[2]!r},{b[3]!r}," + ",".join(repr(v) for v in vals) + "\n")
    print(f"wrote {len(bars)} rows to {out}")


if __name__ == "__main__":
    main()
