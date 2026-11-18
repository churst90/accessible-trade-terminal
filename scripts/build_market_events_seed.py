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
from datetime import date, datetime, time, timezone
from pathlib import Path
from zoneinfo import ZoneInfo

ROOT = Path(__file__).resolve().parent.parent
INPUT = ROOT / "strategy-lab-data" / "events-seed-input"
OUTPUT = ROOT / "AccessibleTrader.Core" / "Resources" / "MarketEvents" / "us_events_seed.json"

EASTERN = ZoneInfo("America/New_York")
UTC = timezone.utc

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

RELEASE_TIME_RULE = date(1997, 1, 1)
FOMC_TIME_RULE = date(2013, 1, 1)


def load_dates(name: str) -> list[date]:
    rows = json.loads((INPUT / name).read_text(encoding="utf-8"))
    return sorted(date.fromisoformat(row["date"]) for row in rows)


def moment(day: date, at: time | None) -> datetime:
    """UTC instant for a New York wall-clock time (or midnight New York for a day)."""
    local = datetime.combine(day, at or time(0, 0), tzinfo=EASTERN)
    return local.astimezone(UTC)


def iso_utc(value: datetime) -> str:
    return value.astimezone(UTC).strftime("%Y-%m-%dT%H:%M:%SZ")


def release_events() -> list[dict]:
    events = []
    for key, spec in RELEASES.items():
        for day in load_dates(spec["file"]):
            exact = day >= RELEASE_TIME_RULE
            events.append(
                {
                    "id": f"{key}-{day.isoformat()}",
                    "kind": "Economy",
                    "spokenName": spec["spoken"],
                    "longName": spec["long"],
                    "importance": spec["importance"],
                    "moment": iso_utc(moment(day, time(8, 30) if exact else None)),
                    "precision": "Exact" if exact else "DayOnly",
                    "date": day.isoformat(),
                    "scope": "all",
                    "source": f"FRED release {spec['release_id']}, fetched 2026-08-01",
                }
            )
    return events


def fomc_events() -> list[dict]:
    events = []
    for row in json.loads((INPUT / "events_fomc.json").read_text(encoding="utf-8")):
        day = date.fromisoformat(row["date"])
        scheduled = bool(row["scheduled"])
        exact = scheduled and day >= FOMC_TIME_RULE
        events.append(
            {
                "id": f"fomc-{day.isoformat()}",
                "kind": "Fed",
                "spokenName": "Fed decision" if scheduled else "Fed decision, unscheduled",
                "longName": (
                    "Federal Open Market Committee decision"
                    if scheduled
                    else "Unscheduled Federal Open Market Committee decision"
                ),
                "importance": "High",
                "moment": iso_utc(moment(day, time(14, 0) if exact else None)),
                "precision": "Exact" if exact else "DayOnly",
                "date": day.isoformat(),
                "scope": "all",
                "source": "Federal Reserve FOMC calendar, fetched 2026-07-31",
            }
        )
    return events


def main() -> int:
    events = release_events() + fomc_events()
    events.sort(key=lambda e: (e["moment"], e["id"]))
    document = {
        "header": {
            "generated": "2026-11-18",
            "note": "US release dates, Fed decisions, and the source and fetch date of each. Times are UTC; day-only events carry their own date and midnight in their own zone.",
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
        },
        "events": events,
    }
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(
        json.dumps(document, indent=1, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    counts: dict[str, int] = {}
    for event in events:
        kind = event["id"].split("-", 1)[0]
        counts[kind] = counts.get(kind, 0) + 1
    print(f"wrote {OUTPUT} with {len(events)} events: {counts}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
