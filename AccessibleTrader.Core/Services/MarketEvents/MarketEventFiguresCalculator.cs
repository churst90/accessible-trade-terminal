namespace AccessibleTrader.Core.Services.MarketEvents
{
    /// <summary>One observation row as ALFRED/FRED returns it: a period, its value and the vintage that carries it.</summary>
    public sealed record MarketEventVintageRow(
        DateOnly Period,
        double Value,
        DateOnly RealtimeStart,
        DateOnly RealtimeEnd);

    /// <summary>
    /// The release figures, computed once for both the seed script and the live feed.
    ///
    /// <para>
    /// This is the C# half of section A's rules: the vintage visible on a release day, the
    /// newest period and the two before it, the "never the future" rule that a figure exists
    /// only when its series published that day, and the same rounding (1 decimal for
    /// percentages, whole thousands for payrolls, half away from zero after a 6-decimal
    /// clean-up). The seed's Python builder and this class must agree; a fixture test runs
    /// both against the same rows and the table in the handoff.
    /// </para>
    /// </summary>
    public static class MarketEventFiguresCalculator
    {
        public const string Cpi = "cpi";
        public const string Jobs = "nfp";
        public const string Gdp = "gdp";
        public const string Ppi = "ppi";

        private static readonly DateOnly PpiHeadlineStart = new(2014, 2, 1);

        /// <summary>
        /// Figures for one release day, or null when no relevant series published that day.
        /// <paramref name="rows"/> is keyed by FRED series id (CPIAUCSL, CPILFESL, PAYEMS,
        /// UNRATE, GDPC1, PPIFIS).
        /// </summary>
        public static MarketEventFigures? Compute(
            string sourceId,
            DateOnly day,
            IReadOnlyDictionary<string, IReadOnlyList<MarketEventVintageRow>> rows)
        {
            IReadOnlyList<MarketEventVintageRow> Series(string id)
                => rows.TryGetValue(id, out var found) ? found : Array.Empty<MarketEventVintageRow>();

            switch (sourceId)
            {
                case Cpi:
                {
                    var headline = MonthChange(Series("CPIAUCSL"), day);
                    var core = MonthChange(Series("CPILFESL"), day);
                    if (headline == null && core == null) return null;
                    return new MarketEventFigures(new[]
                    {
                        new MarketEventFigure("CPI", headline?.Actual, headline?.Previous,
                            MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsHotter),
                        new MarketEventFigure("core CPI", core?.Actual, core?.Previous,
                            MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsHotter),
                    });
                }

                case Jobs:
                {
                    var payrolls = PayrollsChange(Series("PAYEMS"), day);
                    var unemployment = Unemployment(Series("UNRATE"), day);
                    if (payrolls == null && unemployment == null) return null;
                    return new MarketEventFigures(new[]
                    {
                        new MarketEventFigure("payrolls", payrolls?.Actual, payrolls?.Previous,
                            MarketEventFigureUnit.Thousands, MarketEventFigureDirection.HigherIsStronger),
                        new MarketEventFigure("unemployment", unemployment?.Actual, unemployment?.Previous,
                            MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsWeaker),
                    });
                }

                case Gdp:
                    return GdpFigures(Series("GDPC1"), day);

                case Ppi:
                {
                    if (day < PpiHeadlineStart) return null;
                    var month = MonthChange(Series("PPIFIS"), day);
                    if (month == null) return null;
                    return new MarketEventFigures(new[]
                    {
                        new MarketEventFigure("PPI", month.Value.Actual, month.Value.Previous,
                            MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsHotter),
                    });
                }

                default:
                    return null;
            }
        }

        /// <summary>
        /// The handoff's release rule for fetched dates. A date is a release when it is before
        /// the key series' first vintage (nothing to check), or a key-series vintage starts that
        /// day and, for CPI, jobs and PPI, the vintage's newest period is newer than the previous
        /// kept date's. GDP keeps any date a GDPC1 vintage starts: second and third estimates
        /// republish the same quarter and are real releases. Dates after
        /// <paramref name="fetchDay"/> are kept because nothing can be checked yet. Correction
        /// and seasonal-revision days fail the rule and are dropped.
        /// </summary>
        public static IReadOnlyList<DateOnly> KeepReleases(
            string sourceId,
            IEnumerable<DateOnly> dates,
            IReadOnlyDictionary<string, IReadOnlyList<MarketEventVintageRow>> rows,
            DateOnly fetchDay)
        {
            string series = sourceId switch
            {
                Cpi => "CPIAUCSL",
                Jobs => "PAYEMS",
                Gdp => "GDPC1",
                Ppi => "PPIFIS",
                _ => "",
            };
            if (series.Length == 0) return Array.Empty<DateOnly>();

            var seriesRows = rows.TryGetValue(series, out var found)
                ? found
                : Array.Empty<MarketEventVintageRow>();
            DateOnly? firstVintage = seriesRows.Count == 0
                ? null
                : seriesRows.Min(row => row.RealtimeStart);
            DateOnly? previousNewest = null;
            var kept = new List<DateOnly>();

            foreach (var day in dates.Distinct().OrderBy(d => d))
            {
                if (day > fetchDay)
                {
                    kept.Add(day);
                    continue;
                }
                if (firstVintage is not { } first || day < first)
                {
                    kept.Add(day);
                    continue;
                }
                if (!HasStart(seriesRows, day)) continue;
                if (sourceId == Gdp)
                {
                    kept.Add(day);
                    continue;
                }

                var visible = seriesRows
                    .Where(row => double.IsFinite(row.Value)
                        && row.RealtimeStart <= day && day <= row.RealtimeEnd)
                    .ToList();
                if (visible.Count == 0) continue;
                var newest = visible.Max(row => row.Period);
                if (previousNewest is { } previous && newest <= previous) continue;
                kept.Add(day);
                previousNewest = newest;
            }

            return kept;
        }

        private static (double Actual, double Previous)? MonthChange(
            IReadOnlyList<MarketEventVintageRow> rows,
            DateOnly day)
        {
            if (!HasStart(rows, day)) return null;
            var values = Vintage(rows, day);
            if (values.Count < 3) return null;
            var periods = values.Keys.OrderBy(p => p).ToList();
            var p = periods[^1];
            var p1 = periods[^2];
            var p2 = periods[^3];
            return (
                RoundPercent((values[p] / values[p1] - 1) * 100),
                RoundPercent((values[p1] / values[p2] - 1) * 100));
        }

        private static (double Actual, double Previous)? PayrollsChange(
            IReadOnlyList<MarketEventVintageRow> rows,
            DateOnly day)
        {
            if (!HasStart(rows, day)) return null;
            var values = Vintage(rows, day);
            if (values.Count < 3) return null;
            var periods = values.Keys.OrderBy(p => p).ToList();
            var p = periods[^1];
            var p1 = periods[^2];
            var p2 = periods[^3];
            return (
                RoundThousands(values[p] - values[p1]),
                RoundThousands(values[p1] - values[p2]));
        }

        private static (double Actual, double Previous)? Unemployment(
            IReadOnlyList<MarketEventVintageRow> rows,
            DateOnly day)
        {
            if (!HasStart(rows, day)) return null;
            var values = Vintage(rows, day);
            if (values.Count < 2) return null;
            var periods = values.Keys.OrderBy(p => p).ToList();
            return (
                RoundPercent(values[periods[^1]]),
                RoundPercent(values[periods[^2]]));
        }

        private static MarketEventFigures? GdpFigures(
            IReadOnlyList<MarketEventVintageRow> rows,
            DateOnly day)
        {
            if (!HasStart(rows, day)) return null;
            var values = Vintage(rows, day);
            if (values.Count < 2) return null;
            var periods = values.Keys.OrderBy(p => p).ToList();
            var p = periods[^1];
            var p1 = periods[^2];

            double growth = RoundPercent((Math.Pow(values[p] / values[p1], 4) - 1) * 100);
            int estimate = rows
                .Where(row => row.Period == p && row.RealtimeStart <= day)
                .Select(row => row.RealtimeStart)
                .Distinct()
                .Count();

            double? previous = null;
            if (estimate <= 1)
            {
                if (periods.Count >= 3)
                    previous = RoundPercent((Math.Pow(values[p1] / values[periods[^3]], 4) - 1) * 100);
            }
            else
            {
                // The estimate this one revises: the same period as yesterday's vintage had it.
                var before = Vintage(rows, day.AddDays(-1));
                if (before.TryGetValue(p, out double beforeP) && before.TryGetValue(p1, out double beforeP1))
                    previous = RoundPercent((Math.Pow(beforeP / beforeP1, 4) - 1) * 100);
            }

            return new MarketEventFigures(
                new[]
                {
                    new MarketEventFigure("GDP", growth, previous,
                        MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsStronger),
                },
                estimate,
                p);
        }

        /// <summary>The one value per period visible on <paramref name="day"/>, newest vintage winning.</summary>
        private static Dictionary<DateOnly, double> Vintage(
            IReadOnlyList<MarketEventVintageRow> rows,
            DateOnly day)
        {
            var starts = new Dictionary<DateOnly, DateOnly>();
            var values = new Dictionary<DateOnly, double>();
            foreach (var row in rows)
            {
                if (!double.IsFinite(row.Value)) continue;
                if (row.RealtimeStart > day || day > row.RealtimeEnd) continue;
                if (!starts.TryGetValue(row.Period, out var known) || row.RealtimeStart > known)
                {
                    starts[row.Period] = row.RealtimeStart;
                    values[row.Period] = row.Value;
                }
            }
            return values;
        }

        private static bool HasStart(IReadOnlyList<MarketEventVintageRow> rows, DateOnly day)
            => rows.Any(row => row.RealtimeStart == day && double.IsFinite(row.Value));

        private static double RoundPercent(double value)
            => RoundHalfAwayFromZero(value, 10);

        private static double RoundThousands(double value)
            => RoundHalfAwayFromZero(value, 1);

        /// <summary>
        /// Round to <paramref name="scale"/> decimal places, half away from zero, after first
        /// rounding to 6 decimals to remove float noise. A rounded negative zero becomes +0.0.
        /// </summary>
        private static double RoundHalfAwayFromZero(double value, int scale)
        {
            value = Math.Round(value, 6);
            double rounded = Math.CopySign(
                Math.Floor(Math.Abs(value) * scale + 0.5) / scale,
                value);
            return rounded == 0 ? 0.0 : rounded;
        }
    }
}
