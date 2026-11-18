#!/usr/bin/env python3
"""Rebuild AccessibleTrader.Core/Resources/MarketEvents/us_events_seed.json.

Inputs (gitignored, copied into strategy-lab-data/events-seed-input/):
  events_cpi.json  FRED release dates, release_id 10, fetched 2026-08-01
  events_nfp.json  FRED release dates, release_id 50, fetched 2026-08-01
  events_ppi.json  FRED release dates, release_id 46, fetched 2026-08-01
  events_gdp.json  FRED release dates, release_id 53, fetched 2026-08-01
  events_fomc.json Federal Reserve FOMC decision dates 2000-2027, fetched 2026-07-31

Run with:  python3 -I scripts/build_market_events_seed.py
"""

from __future__ import annotations

import json
import sys
from datetime import date
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
INPUT = ROOT / "strategy-lab-data" / "events-seed-input"
OUTPUT = ROOT / "AccessibleTrader.Core" / "Resources" / "MarketEvents" / "us_events_seed.json"

RELEASES = {
    "cpi": {
        "file": "events_cpi.json",
        "release_id": 10,
        "spoken": "CPI",
        "long": "Consumer Price Index",
        "importance": "High",
        "url": "https://api.stlouisfed.org/fred/release/dates?release_id=10&order_by=release_date&sort_order=asc&include_release_dates_with_no_data=true",
    },
    "nfp": {
        "file": "events_nfp.json",
        "release_id": 50,
        "spoken": "jobs report",
        "long": "Nonfarm payrolls",
        "importance": "High",
        "url": "https://api.stlouisfed.org/fred/release/dates?release_id=50&order_by=release_date&sort_order=asc&include_release_dates_with_no_data=true",
    },
    "ppi": {
        "file": "events_ppi.json",
        "release_id": 46,
        "spoken": "PPI",
        "long": "Producer Price Index",
        "importance": "Medium",
        "url": "https://api.stlouisfed.org/fred/release/dates?release_id=46&order_by=release_date&sort_order=asc&include_release_dates_with_no_data=true",
    },
    "gdp": {
        "file": "events_gdp.json",
        "release_id": 53,
        "spoken": "GDP",
        "long": "Gross domestic product",
        "importance": "Medium",
        "url": "https://api.stlouisfed.org/fred/release/dates?release_id=53&order_by=release_date&sort_order=asc&include_release_dates_with_no_data=true",
    },
}

def load_dates(name: str) -> list[date]:
    rows = json.loads((INPUT / name).read_text(encoding="utf-8"))
    return sorted(date.fromisoformat(row["date"]) for row in rows)


def fomc_dates() -> tuple[list[date], list[date]]:
    scheduled: list[date] = []
    unscheduled: list[date] = []
    for row in json.loads((INPUT / "events_fomc.json").read_text(encoding="utf-8")):
        day = date.fromisoformat(row["date"])
        (scheduled if bool(row["scheduled"]) else unscheduled).append(day)
    return sorted(scheduled), sorted(unscheduled)


def main() -> int:
    header = {
        "generated": "2026-11-18",
        "note": (
            "US release dates and Fed decisions, and the source and fetch date of each. "
            "The kinds, names, importance, scope and release times live in MarketEventSeed.cs; "
            "only dates are stored here."
        ),
        "sources": [
            {
                "id": key,
                "name": f"FRED release dates: {spec['long']} (release {spec['release_id']})",
                "url": spec["url"],
                "fetched": "2026-08-01",
            }
            for key, spec in RELEASES.items()
        ]
        + [
            {
                "id": "fomc",
                "name": "Federal Reserve FOMC decision calendar",
                "url": "https://www.federalreserve.gov/monetarypolicy/fomccalendars.htm",
                "fetched": "2026-07-31",
            }
        ],
    }

    release_dates = {key: load_dates(spec["file"]) for key, spec in RELEASES.items()}
    scheduled, unscheduled = fomc_dates()

    lines = ["{", '"header": ' + json.dumps(header, separators=(",", ":"), ensure_ascii=False) + ","]
    for key in RELEASES:
        days = json.dumps([day.isoformat() for day in release_dates[key]], separators=(",", ":"))
        lines.append(f'"{key}": {days},')
    fomc = {
        "scheduled": [day.isoformat() for day in scheduled],
        "unscheduled": [day.isoformat() for day in unscheduled],
    }
    lines.append('"fomc": ' + json.dumps(fomc, separators=(",", ":")) + "")
    lines.append("}")
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text("\n".join(lines) + "\n", encoding="utf-8")

    counts = {key: len(days) for key, days in release_dates.items()}
    counts["fomc"] = len(scheduled) + len(unscheduled)
    total = sum(counts.values())
    print(f"wrote {OUTPUT} with {total} events: {counts}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
