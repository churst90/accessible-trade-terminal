#!/usr/bin/env python3
"""Rebuild AccessibleTrader.Core/Resources/MarketEvents/us_events_seed.json.

Inputs (gitignored, copied into strategy-lab-data/events-seed-input/):
  events_cpi.json  FRED release dates, release_id 10
  events_nfp.json  FRED release dates, release_id 50
  events_ppi.json  FRED release dates, release_id 46
  events_gdp.json  FRED release dates, release_id 53
                   Each is the 2026-08-01 fetch, with the dates after its last
                   (30 July 2026) appended from a 2026-11-21 fetch made with
                   include_release_dates_with_no_data=true, which also lists the
                   dates already scheduled (to December 2026).
  events_fomc.json Federal Reserve FOMC decision dates 2000-2027, fetched 2026-07-31
  alfred/{CPIAUCSL,CPILFESL,PAYEMS,UNRATE,GDPC1}_vintages.csv
                   ALFRED vintage histories, fetched 2026-11-21 (FRED API
                   series/observations over all real-time periods). Columns
                   period,value,realtime_start,realtime_end; an empty realtime_end
                   means still current; a '.' value is missing and its row is skipped.

Release-day figures use ONLY the vintage that existed on that day (the "never the
future" rule): every row with realtime_start <= day <= realtime_end, newest
realtime_start per period. A source's figures for a day exist only when at least
one row of the relevant series starts on that day.

FRED's release/dates list also names correction and seasonal-revision days, so a
date is kept only when it is a release: before the key series' first vintage
(there is nothing to check), or a key-series vintage starts that day and, for
CPI, jobs and PPI, its newest period is newer than the previous kept date's. GDP
keeps any date a GDPC1 vintage starts because second and third estimates
republish the same quarter. PPI has no offline vintages, so two PPI dates in the
same February no more than 7 days apart drop the earlier one; that is inferred
from CPI's pattern, not checked against PPI data. A date after FETCHED is a
scheduled release nothing can check yet: it is kept, with no figures.

Figure columns, aligned by index with each source's date array:
  cpi: [cpi, previous, core, corePrevious]
  nfp: [payrolls, previous, unemployment, unemploymentPrevious]
  gdp: [growth, estimate, previous, period]
  ppi: none in the seed (ALFRED refused automated requests); live FRED supplies them.

Percentages are rounded to 1 decimal and payrolls to whole thousands, half away
from zero, after first rounding to 6 decimals. A rounded -0.0 is stored as 0.0.

Run with:  python3 -I scripts/build_market_events_seed.py
"""

from __future__ import annotations

import csv
import json
import math
import sys
from datetime import date, timedelta
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
        "alfred": ["CPIAUCSL", "CPILFESL"],
    },
    "nfp": {
        "file": "events_nfp.json",
        "release_id": 50,
        "spoken": "jobs report",
        "long": "Nonfarm payrolls",
        "importance": "High",
        "url": "https://api.stlouisfed.org/fred/release/dates?release_id=50&order_by=release_date&sort_order=asc&include_release_dates_with_no_data=true",
        "alfred": ["PAYEMS", "UNRATE"],
    },
    "ppi": {
        "file": "events_ppi.json",
        "release_id": 46,
        "spoken": "PPI",
        "long": "Producer Price Index",
        "importance": "Medium",
        "url": "https://api.stlouisfed.org/fred/release/dates?release_id=46&order_by=release_date&sort_order=asc&include_release_dates_with_no_data=true",
        "alfred": [],
    },
    "gdp": {
        "file": "events_gdp.json",
        "release_id": 53,
        "spoken": "GDP",
        "long": "Gross domestic product",
        "importance": "Medium",
        "url": "https://api.stlouisfed.org/fred/release/dates?release_id=53&order_by=release_date&sort_order=asc&include_release_dates_with_no_data=true",
        "alfred": ["GDPC1"],
    },
}

FIGURE_COLUMNS = {
    "cpi": ["CPI month change percent", "previous", "core CPI month change percent", "previous"],
    "nfp": ["payrolls change, thousands", "previous", "unemployment rate percent", "previous"],
    "gdp": ["growth percent annualized", "estimate number", "previous", "period yyyy-mm-dd"],
    "ppi": [],
}

ALFRED_URL = "https://alfred.stlouisfed.org/series/downloaddata?seid={series}"

KEY_SERIES = {"cpi": "CPIAUCSL", "nfp": "PAYEMS", "gdp": "GDPC1"}

END_OF_TIME = date(9999, 12, 31)

# When the release dates and vintages were last fetched. A date after this is scheduled.
FETCHED = date(2026, 11, 21)


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


def load_vintages(series: str) -> list[tuple[date, float, date, date]]:
    """period, value, realtime_start, realtime_end; missing values are dropped."""
    rows: list[tuple[date, float, date, date]] = []
    with (INPUT / "alfred" / f"{series}_vintages.csv").open(encoding="utf-8") as f:
        for row in csv.DictReader(f):
            value = row["value"]
            if value == "." or value == "":
                continue
            end = date.fromisoformat(row["realtime_end"]) if row["realtime_end"] else END_OF_TIME
            rows.append((
                date.fromisoformat(row["period"]),
                float(value),
                date.fromisoformat(row["realtime_start"]),
                end,
            ))
    return rows


def newest_realtime_start(rows: list[tuple[date, float, date, date]]) -> date:
    return max(start for _, _, start, _ in rows)


def vintage(rows: list[tuple[date, float, date, date]], day: date) -> dict[date, float]:
    """The one value per period visible on `day`, newest vintage winning."""
    by_period: dict[date, tuple[date, float]] = {}
    for period, value, start, end in rows:
        if start <= day <= end:
            if period not in by_period or start > by_period[period][0]:
                by_period[period] = (start, value)
    return {period: value for period, (_, value) in by_period.items()}


def has_start(rows: list[tuple[date, float, date, date]], day: date) -> bool:
    return any(start == day for _, _, start, _ in rows)


def newest_period(rows: list[tuple[date, float, date, date]], day: date) -> date | None:
    values = vintage(rows, day)
    return max(values) if values else None


def ppi_release_days(days: list[date]) -> tuple[list[date], list[date]]:
    """The earlier of two PPI dates in the same February within 7 days is a revision.

    PPIFIS has no offline vintages, so this is inferred from CPI's pattern and not
    checked against PPI data.
    """
    by_month: dict[tuple[int, int], list[date]] = {}
    for day in days:
        if day.month == 2:
            by_month.setdefault((day.year, day.month), []).append(day)
    dropped: set[date] = set()
    for february in by_month.values():
        ordered = sorted(february)
        for earlier, later in zip(ordered, ordered[1:]):
            if (later - earlier).days <= 7:
                dropped.add(earlier)
    return [day for day in days if day not in dropped], sorted(dropped)


def release_days(
    key: str,
    days: list[date],
    series: dict[str, list[tuple[date, float, date, date]]],
) -> tuple[list[date], list[date]]:
    """Split a source's FRED dates into releases and correction/revision days."""
    if key == "ppi":
        return ppi_release_days(days)
    rows = series[KEY_SERIES[key]]
    first_vintage = min(start for _, _, start, _ in rows)
    kept: list[date] = []
    dropped: list[date] = []
    previous_newest: date | None = None
    for day in days:
        if day < first_vintage or day > FETCHED:
            kept.append(day)
            continue
        if not has_start(rows, day):
            dropped.append(day)
            continue
        if key == "gdp":
            kept.append(day)
            continue
        newest = newest_period(rows, day)
        if previous_newest is None or (newest is not None and newest > previous_newest):
            kept.append(day)
            previous_newest = newest
        else:
            dropped.append(day)
    return kept, dropped


def round_half_away(value: float, digits: int) -> float:
    value = round(value, 6)
    scale = 10 ** digits
    rounded = math.copysign(math.floor(abs(value) * scale + 0.5) / scale, value)
    return 0.0 if rounded == 0 else rounded


def round_percent(value: float) -> float:
    return round_half_away(value, 1)


def round_thousands(value: float) -> float:
    rounded = round_half_away(value, 0)
    return float(int(rounded))


def month_change(rows: list[tuple[date, float, date, date]], day: date):
    """(change, previous) in percent, or None when the series did not publish on `day`."""
    if not has_start(rows, day):
        return None
    values = vintage(rows, day)
    periods = sorted(values)
    if len(periods) < 3:
        return None
    p, p1, p2 = periods[-1], periods[-2], periods[-3]
    return (
        round_percent((values[p] / values[p1] - 1) * 100),
        round_percent((values[p1] / values[p2] - 1) * 100),
    )


def cpi_figures(day: date, series: dict[str, list[tuple[date, float, date, date]]]):
    headline = month_change(series["CPIAUCSL"], day)
    core = month_change(series["CPILFESL"], day)
    if headline is None and core is None:
        return None
    return [
        headline[0] if headline else None,
        headline[1] if headline else None,
        core[0] if core else None,
        core[1] if core else None,
    ]


def payrolls_change(rows: list[tuple[date, float, date, date]], day: date):
    if not has_start(rows, day):
        return None
    values = vintage(rows, day)
    periods = sorted(values)
    if len(periods) < 3:
        return None
    p, p1, p2 = periods[-1], periods[-2], periods[-3]
    return (round_thousands(values[p] - values[p1]), round_thousands(values[p1] - values[p2]))


def unemployment(rows: list[tuple[date, float, date, date]], day: date):
    if not has_start(rows, day):
        return None
    values = vintage(rows, day)
    periods = sorted(values)
    if len(periods) < 2:
        return None
    return (round_percent(values[periods[-1]]), round_percent(values[periods[-2]]))


def nfp_figures(day: date, series: dict[str, list[tuple[date, float, date, date]]]):
    pay = payrolls_change(series["PAYEMS"], day)
    rate = unemployment(series["UNRATE"], day)
    if pay is None and rate is None:
        return None
    return [
        pay[0] if pay else None,
        pay[1] if pay else None,
        rate[0] if rate else None,
        rate[1] if rate else None,
    ]


def estimate_count(rows: list[tuple[date, float, date, date]], period: date, day: date) -> int:
    return len({start for p, _, start, _ in rows if p == period and start <= day})


def gdp_figures(day: date, rows: list[tuple[date, float, date, date]]):
    if not has_start(rows, day):
        return None
    values = vintage(rows, day)
    periods = sorted(values)
    if len(periods) < 2:
        return None
    p, p1 = periods[-1], periods[-2]
    growth = round_percent(((values[p] / values[p1]) ** 4 - 1) * 100)
    estimate = estimate_count(rows, p, day)

    if estimate <= 1:
        previous = None
        if len(periods) >= 3:
            previous = round_percent(((values[p1] / values[periods[-3]]) ** 4 - 1) * 100)
    else:
        # This estimate revises yesterday's: P's growth as the vintage on D-1 had it.
        before = vintage(rows, day - timedelta(days=1))
        previous = None
        if p in before and p1 in before:
            previous = round_percent(((before[p] / before[p1]) ** 4 - 1) * 100)

    return [growth, estimate, previous, p.isoformat()]


def build_figures(series: dict[str, list[tuple[date, float, date, date]]], dates: dict[str, list[date]]):
    figures: dict[str, list[object]] = {}
    figures["cpi"] = [cpi_figures(day, series) for day in dates["cpi"]]
    figures["nfp"] = [nfp_figures(day, series) for day in dates["nfp"]]
    figures["ppi"] = [None for _ in dates["ppi"]]
    figures["gdp"] = [gdp_figures(day, series["GDPC1"]) for day in dates["gdp"]]
    return figures


def main() -> int:
    series = {name: load_vintages(name) for name in
              ("CPIAUCSL", "CPILFESL", "PAYEMS", "UNRATE", "GDPC1")}

    header = {
        "generated": FETCHED.isoformat(),
        "note": (
            "US release dates and Fed decisions, with the source and fetch date of each. "
            "The kinds, names, importance, scope and release times live in MarketEventSeed.cs. "
            "A date is a release only: correction and seasonal-revision days are dropped. Before the "
            "key series' first vintage every date is kept; otherwise CPIAUCSL, PAYEMS and PPIFIS "
            "must start a vintage that day whose newest period is newer than the previous kept "
            "date's, and GDPC1 need only start one that day (second and third estimates are real). "
            "PPI has no offline vintages: the earlier of two PPI dates in the same February within "
            "7 days is dropped, inferred from CPI's pattern, not checked against PPI data. "
            "Figures are the values visible on the release day itself (never a later vintage). "
            "Figure row columns, aligned by index with each date array: "
            "cpi [CPI month change %, previous, core CPI month change %, previous]; "
            "nfp [payrolls change thousands, previous, unemployment %, previous]; "
            "gdp [growth % annualized, estimate number, previous, period]. "
            "Percentages are 1 decimal, payrolls whole thousands; ppi_figures is all null. "
            "A date after the fetch day is a scheduled release, kept with no figures."
        ),
        "sources": [],
    }

    for key, spec in RELEASES.items():
        entry = {
            "id": key,
            "name": f"FRED release dates: {spec['long']} (release {spec['release_id']})",
            "url": spec["url"],
            "fetched": FETCHED.isoformat(),
        }
        if spec["alfred"]:
            entry["alfred"] = [
                {
                    "series": series_id,
                    "url": ALFRED_URL.format(series=series_id),
                    "newest": newest_realtime_start(series[series_id]).isoformat(),
                }
                for series_id in spec["alfred"]
            ]
        header["sources"].append(entry)
    header["sources"].append({
        "id": "fomc",
        "name": "Federal Reserve FOMC decision calendar",
        "url": "https://www.federalreserve.gov/monetarypolicy/fomccalendars.htm",
        "fetched": "2026-07-31",
    })

    release_dates = {key: load_dates(spec["file"]) for key, spec in RELEASES.items()}
    kept_dates: dict[str, list[date]] = {}
    dropped_dates: dict[str, list[date]] = {}
    for key in RELEASES:
        kept_dates[key], dropped_dates[key] = release_days(key, release_dates[key], series)
    release_dates = kept_dates
    scheduled, unscheduled = fomc_dates()
    figures = build_figures(series, release_dates)

    lines = ["{", '"header": ' + json.dumps(header, separators=(",", ":"), ensure_ascii=False) + ","]
    for key in RELEASES:
        days = json.dumps([day.isoformat() for day in release_dates[key]], separators=(",", ":"))
        rows = json.dumps(figures[key], separators=(",", ":"))
        lines.append(f'"{key}": {days},')
        lines.append(f'"{key}_figures": {rows},')
    fomc = {
        "scheduled": [day.isoformat() for day in scheduled],
        "unscheduled": [day.isoformat() for day in unscheduled],
    }
    lines.append('"fomc": ' + json.dumps(fomc, separators=(",", ":")))
    lines.append("}")
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text("\n".join(lines) + "\n", encoding="utf-8")

    counts = {key: len(days) for key, days in release_dates.items()}
    counts["fomc"] = len(scheduled) + len(unscheduled)
    total = sum(counts.values())
    print(f"wrote {OUTPUT} with {total} events: {counts}")
    print(f"size {OUTPUT.stat().st_size} bytes")
    for key in RELEASES:
        print(f"dropped {key} {len(dropped_dates[key])}: "
              + ", ".join(day.isoformat() for day in dropped_dates[key]))
    for key in RELEASES:
        print(f"{key}_figures non-null {sum(1 for row in figures[key] if row is not None)} of {len(figures[key])}")
    print("core CPI non-null", sum(1 for row in figures["cpi"] if row and row[2] is not None))
    print("unemployment non-null", sum(1 for row in figures["nfp"] if row and row[2] is not None))
    return 0


if __name__ == "__main__":
    sys.exit(main())
