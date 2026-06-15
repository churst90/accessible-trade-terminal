#!/usr/bin/env python
"""
Confidence-model falsification experiment (round 14, 2026-06-14).

The one question: do the causal features RANK trade outcomes out-of-sample?
If OOS AUC > ~0.55 and predictions calibrate, the whole "asset-tuned ML confidence
indicator" vision is alive. If it's a coin flip OOS, the edge isn't ML-extractable
from these features and we've learned that in an afternoon instead of weeks.

Discipline: strictly chronological walk-forward (train only on the past), no shuffling,
asset NOT used as a feature (testing for a UNIVERSAL signal), per-asset AUC reported
separately to check generalization.
"""
import sys
import numpy as np
import pandas as pd
from sklearn.metrics import roc_auc_score
import lightgbm as lgb

CSV = sys.argv[1] if len(sys.argv) > 1 else "../strategy-lab-data/ml/ml_1d.csv"
N_FOLDS = 5

FEATURES = ["ret1", "ret5", "atr_pct", "dist_sma_pct", "range_pct",
            "wt1", "wt2", "wt_hist", "mfw", "anchor", "anchor2",
            "c_sine", "c_lead", "hurst", "vol_ratio", "vol_pct", "vol_state", "regime",
            "sig_wtx", "sig_blue", "sig_gold"]

def main():
    df = pd.read_csv(CSV)
    df = df.sort_values("date").reset_index(drop=True)
    df = df.dropna(subset=["win"])
    n = len(df)
    base = df["win"].mean()
    print(f"\n=== {CSV} ===")
    print(f"rows={n}  assets={df['asset'].nunique()}  date {df['date'].min()}..{df['date'].max()}")
    print(f"base win rate (target-before-stop, 1.5R/1.0R/20bar) = {base:.3f}")

    # Chronological walk-forward: fold k tests on its time slice, trains on everything before.
    bounds = [int(n * k / N_FOLDS) for k in range(N_FOLDS + 1)]
    oof_pred = np.full(n, np.nan)
    fold_aucs = []
    for k in range(1, N_FOLDS):
        tr0, tr1 = 0, bounds[k]
        te0, te1 = bounds[k], bounds[k + 1]
        Xtr, ytr = df.iloc[tr0:tr1][FEATURES], df.iloc[tr0:tr1]["win"]
        Xte, yte = df.iloc[te0:te1][FEATURES], df.iloc[te0:te1]["win"]
        if yte.nunique() < 2 or ytr.nunique() < 2:
            continue
        model = lgb.LGBMClassifier(
            n_estimators=300, learning_rate=0.03, num_leaves=31,
            min_child_samples=80, subsample=0.8, colsample_bytree=0.8,
            reg_lambda=1.0, verbose=-1)
        model.fit(Xtr, ytr)
        p = model.predict_proba(Xte)[:, 1]
        oof_pred[te0:te1] = p
        auc = roc_auc_score(yte, p)
        fold_aucs.append(auc)
        d0, d1 = df.iloc[te0]["date"], df.iloc[te1 - 1]["date"]
        print(f"  fold {k}: train {tr1:6d}  test {te1-te0:5d} [{d0}..{d1}]  OOS AUC={auc:.4f}")

    mask = ~np.isnan(oof_pred)
    y = df["win"].values[mask]
    p = oof_pred[mask]
    pooled_auc = roc_auc_score(y, p)
    print(f"\nPOOLED OOS AUC = {pooled_auc:.4f}   (0.50 = coin flip; >0.55 = real signal)")
    print(f"mean fold AUC  = {np.mean(fold_aucs):.4f} ± {np.std(fold_aucs):.4f}")

    # Calibration: decile reliability — does predicted P(win) track actual win rate?
    print("\nCalibration (OOS, by predicted-probability decile):")
    print("  decile  pred_mean  actual_win  n")
    dec = pd.qcut(p, 10, labels=False, duplicates="drop")
    for dd in sorted(set(dec)):
        m = dec == dd
        print(f"   {dd:2d}      {p[m].mean():.3f}      {y[m].mean():.3f}     {m.sum()}")

    # Confidence lift: top-20% vs bottom-20% vs base.
    order = np.argsort(p)
    k20 = max(1, len(p) // 5)
    bot = y[order[:k20]].mean()
    top = y[order[-k20:]].mean()
    print(f"\nConfidence lift (OOS): bottom-20%={bot:.3f}  base={y.mean():.3f}  top-20%={top:.3f}")
    print(f"  top-vs-bottom spread = {top-bot:+.3f}")

    # Per-asset OOS AUC (does the universal model generalize across assets?)
    print("\nPer-asset OOS AUC:")
    da = df[mask].copy(); da["p"] = p; da["y"] = y
    for asset, g in da.groupby("asset"):
        if g["y"].nunique() < 2 or len(g) < 50:
            print(f"  {asset:12s}  n={len(g):5d}  (too few/one-class)"); continue
        print(f"  {asset:12s}  n={len(g):5d}  AUC={roc_auc_score(g['y'], g['p']):.4f}  win={g['y'].mean():.3f}")

    # ── META-MODEL TEST: confidence WHEN a buy signal fires ───────────────────
    # The user's real use case. Filter OOS rows to bars where a Cipher B buy fired
    # (WT cross OR blue dot OR gold) and ask: does the model rank THOSE outcomes,
    # and does it beat the raw signal's unconditional win rate?
    dm = df[mask].copy(); dm["p"] = p; dm["y"] = y
    sig = dm[(dm["sig_wtx"] == 1) | (dm["sig_blue"] == 1) | (dm["sig_gold"] == 1)]
    print(f"\n=== META-MODEL: confidence on buy-signal bars (n={len(sig)}) ===")
    if len(sig) > 100 and sig["y"].nunique() == 2:
        print(f"  raw signal win rate (no model) = {sig['y'].mean():.3f}")
        print(f"  model OOS AUC on signal bars   = {roc_auc_score(sig['y'], sig['p']):.4f}")
        order = np.argsort(sig["p"].values); k = max(1, len(sig)//4)
        botq = sig["y"].values[order[:k]].mean(); topq = sig["y"].values[order[-k:]].mean()
        print(f"  signal+top-25%-confidence win  = {topq:.3f}   (vs raw {sig['y'].mean():.3f}, bottom-25% {botq:.3f})")
        print(f"  → confidence tiers add {topq - sig['y'].mean():+.3f} to a fired signal's win rate")

    # ── ASSET-TUNED test: train a model PER ASSET (the user's core hypothesis) ──
    # "a model tuned to the asset being viewed." Walk-forward within each asset's own
    # history. If asset-tuned beats the pooled per-asset AUCs, the per-asset idea has legs.
    print("\n=== ASSET-TUNED: per-asset walk-forward (train on that asset only) ===")
    for asset, g in df.groupby("asset"):
        g = g.sort_values("date").reset_index(drop=True)
        m = len(g)
        if m < 2000:
            print(f"  {asset:12s}  n={m:5d}  (too few for per-asset training)"); continue
        cut = int(m * 0.6)
        Xtr, ytr = g.iloc[:cut][FEATURES], g.iloc[:cut]["win"]
        Xte, yte = g.iloc[cut:][FEATURES], g.iloc[cut:]["win"]
        if ytr.nunique() < 2 or yte.nunique() < 2:
            continue
        mdl = lgb.LGBMClassifier(n_estimators=200, learning_rate=0.03, num_leaves=31,
                                 min_child_samples=60, reg_lambda=1.0, verbose=-1)
        mdl.fit(Xtr, ytr)
        pa = mdl.predict_proba(Xte)[:, 1]
        print(f"  {asset:12s}  n={m:5d}  asset-tuned OOS AUC={roc_auc_score(yte, pa):.4f}")

    # Feature importance from a final full-history model (gain).
    full = lgb.LGBMClassifier(n_estimators=300, learning_rate=0.03, num_leaves=31,
                              min_child_samples=80, reg_lambda=1.0, verbose=-1)
    full.fit(df[FEATURES], df["win"])
    imp = sorted(zip(FEATURES, full.booster_.feature_importance(importance_type="gain")),
                 key=lambda t: -t[1])
    print("\nFeature importance (gain, full-history fit):")
    for f, v in imp:
        print(f"  {f:14s} {v:10.0f}")

if __name__ == "__main__":
    main()
