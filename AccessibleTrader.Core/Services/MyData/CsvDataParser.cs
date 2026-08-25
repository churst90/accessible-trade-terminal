using System.Globalization;

namespace AccessibleTrader.Core.Services.MyData
{
    /// <summary>Detected shape of an imported dataset.</summary>
    public enum MyDataShape
    {
        /// <summary>date,open,high,low,close[,volume] — charts as candles.</summary>
        Ohlcv,
        /// <summary>date + one or more named numeric columns — each column loads as a line chart.</summary>
        Values,
        /// <summary>date,label[,value] — dated events, rendered as chart markers.</summary>
        Events,
    }

    public sealed record MyDataEvent(DateTime Date, string Label, double? Value);

    /// <summary>
    /// One parsed dataset. For <see cref="MyDataShape.Ohlcv"/>, <see cref="Bars"/>
    /// is populated; for Values, <see cref="Dates"/> + <see cref="Columns"/> /
    /// <see cref="ColumnData"/>; for Events, <see cref="Events"/>.
    /// </summary>
    public sealed class ParsedDataset
    {
        public MyDataShape Shape { get; init; }
        public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();
        public IReadOnlyList<Sdk.Models.Ohlcv> Bars { get; init; } = Array.Empty<Sdk.Models.Ohlcv>();
        public IReadOnlyList<DateTime> Dates { get; init; } = Array.Empty<DateTime>();
        /// <summary>Per-column values, parallel to <see cref="Dates"/>. Keyed by column name.</summary>
        public IReadOnlyDictionary<string, double[]> ColumnData { get; init; } =
            new Dictionary<string, double[]>();
        public IReadOnlyList<MyDataEvent> Events { get; init; } = Array.Empty<MyDataEvent>();
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
        /// <summary>Inferred timeframe token ("1d", "1w", "1M", "1h") from median row spacing.</summary>
        public string InferredTimeframe { get; init; } = "1d";
        public int RowCount => Shape switch
        {
            MyDataShape.Ohlcv => Bars.Count,
            MyDataShape.Events => Events.Count,
            _ => Dates.Count,
        };
        public DateTime FirstDate => Shape switch
        {
            MyDataShape.Ohlcv => Bars.Count > 0 ? Bars[0].Date : default,
            MyDataShape.Events => Events.Count > 0 ? Events[0].Date : default,
            _ => Dates.Count > 0 ? Dates[0] : default,
        };
        public DateTime LastDate => Shape switch
        {
            MyDataShape.Ohlcv => Bars.Count > 0 ? Bars[^1].Date : default,
            MyDataShape.Events => Events.Count > 0 ? Events[^1].Date : default,
            _ => Dates.Count > 0 ? Dates[^1] : default,
        };
    }

    /// <summary>
    /// CSV/TSV parser for user-imported data. Deliberately strict about STRUCTURE
    /// (a date column first, a header row naming the rest) and tolerant about
    /// SURFACE (delimiter auto-detect, several date formats, blank lines, BOM,
    /// quoted fields) — the failure mode to avoid is a silently-wrong chart, so
    /// anything ambiguous becomes a thrown, human-readable error or a Warning
    /// the import dialog speaks.
    /// </summary>
    public static class CsvDataParser
    {
        public const int MaxRows = 200_000;

        private static readonly string[] DateFormats =
        {
            // ISO first (the templates use it), then the common spreadsheet exports.
            "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm",
            "MM/dd/yyyy", "M/d/yyyy", "MM/dd/yyyy HH:mm", "dd.MM.yyyy", "yyyy/MM/dd",
        };

        public static ParsedDataset Parse(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new FormatException("The file is empty.");

            var lines = content.Replace("﻿", "").Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();
            if (lines.Count < 2)
                throw new FormatException(
                    "Need a header row plus at least one data row. First line: column names (date first); following lines: the data.");
            if (lines.Count - 1 > MaxRows)
                throw new FormatException($"Too many rows ({lines.Count - 1:N0}). The limit is {MaxRows:N0}.");

            char delimiter = DetectDelimiter(lines[0]);
            var header = SplitLine(lines[0], delimiter);
            if (header.Count < 2)
                throw new FormatException(
                    $"Only one column detected (delimiter '{delimiter}'). Expected a date column plus at least one more.");

            // The header row must not itself be data — that's the #1 authoring
            // mistake and it silently shifts every row by one.
            if (TryParseDate(header[0], out _))
                throw new FormatException(
                    "The first line looks like data, not a header. Add a header row — for example: date,value");

            var warnings = new List<string>();
            var columnNames = header.Skip(1)
                .Select((h, i) => string.IsNullOrWhiteSpace(h) ? $"Column {i + 2}" : h.Trim())
                .ToList();

            // Shape detection from the header + first data row.
            var lower = columnNames.Select(c => c.ToLowerInvariant()).ToList();
            bool looksOhlcv = lower.Contains("open") && lower.Contains("high")
                           && lower.Contains("low") && lower.Contains("close");
            var firstData = SplitLine(lines[1], delimiter);
            bool secondColumnNumeric = firstData.Count > 1 && TryParseNumber(firstData[1], out _);

            if (looksOhlcv) return ParseOhlcv(lines, delimiter, columnNames, warnings);
            if (!secondColumnNumeric) return ParseEvents(lines, delimiter, columnNames, warnings);
            return ParseValues(lines, delimiter, columnNames, warnings);
        }

        // ── Shape parsers ────────────────────────────────────────────────────

        private static ParsedDataset ParseOhlcv(List<string> lines, char d, List<string> columns, List<string> warnings)
        {
            var lower = columns.Select(c => c.ToLowerInvariant()).ToList();
            int io = lower.IndexOf("open"), ih = lower.IndexOf("high"),
                il = lower.IndexOf("low"), ic = lower.IndexOf("close"),
                iv = lower.IndexOf("volume");

            var rows = new SortedDictionary<DateTime, Sdk.Models.Ohlcv>();
            foreach (var (fields, date, lineNo) in DataRows(lines, d, columns.Count, warnings))
            {
                double open = Num(fields, io, lineNo), high = Num(fields, ih, lineNo),
                       low = Num(fields, il, lineNo), close = Num(fields, ic, lineNo);
                double volume = iv >= 0 && iv < fields.Count - 1 && TryParseNumber(fields[iv + 1], out var v) ? v : 0;
                if (high < low)
                    throw new FormatException($"Line {lineNo}: high ({high}) is below low ({low}).");
                if (rows.ContainsKey(date))
                    warnings.Add($"Duplicate date {date:yyyy-MM-dd} — the later row wins.");
                rows[date] = new Sdk.Models.Ohlcv(date, open, high, low, close, volume);
            }
            var bars = rows.Values.ToList();
            return new ParsedDataset
            {
                Shape = MyDataShape.Ohlcv,
                Columns = columns,
                Bars = bars,
                Warnings = warnings,
                InferredTimeframe = InferTimeframe(bars.Select(b => b.Date).ToList()),
            };
        }

        private static ParsedDataset ParseValues(List<string> lines, char d, List<string> columns, List<string> warnings)
        {
            var rows = new SortedDictionary<DateTime, double[]>();
            foreach (var (fields, date, lineNo) in DataRows(lines, d, columns.Count, warnings))
            {
                var values = new double[columns.Count];
                for (int c = 0; c < columns.Count; c++)
                {
                    // A blank/unparseable cell becomes NaN (a gap), not an error —
                    // real personal data has holes. It's reported once, not per cell.
                    if (c + 1 < fields.Count && TryParseNumber(fields[c + 1], out var v)) values[c] = v;
                    else values[c] = double.NaN;
                }
                if (rows.ContainsKey(date))
                    warnings.Add($"Duplicate date {date:yyyy-MM-dd} — the later row wins.");
                rows[date] = values;
            }
            if (rows.Values.Any(vals => vals.Any(double.IsNaN)))
                warnings.Add("Some cells were blank or not numbers — they chart as gaps.");

            var dates = rows.Keys.ToList();
            var columnData = new Dictionary<string, double[]>();
            for (int c = 0; c < columns.Count; c++)
                columnData[columns[c]] = rows.Values.Select(vals => vals[c]).ToArray();

            return new ParsedDataset
            {
                Shape = MyDataShape.Values,
                Columns = columns,
                Dates = dates,
                ColumnData = columnData,
                Warnings = warnings,
                InferredTimeframe = InferTimeframe(dates),
            };
        }

        private static ParsedDataset ParseEvents(List<string> lines, char d, List<string> columns, List<string> warnings)
        {
            var events = new List<MyDataEvent>();
            foreach (var (fields, date, lineNo) in DataRows(lines, d, columns.Count, warnings))
            {
                string label = fields.Count > 1 ? fields[1].Trim() : "";
                if (label.Length == 0)
                    throw new FormatException($"Line {lineNo}: the event label is empty.");
                double? value = fields.Count > 2 && TryParseNumber(fields[2], out var v) ? v : null;
                events.Add(new MyDataEvent(date, label, value));
            }
            events.Sort((a, b) => a.Date.CompareTo(b.Date));
            return new ParsedDataset
            {
                Shape = MyDataShape.Events,
                Columns = columns,
                Events = events,
                Warnings = warnings,
                InferredTimeframe = InferTimeframe(events.Select(e => e.Date).ToList()),
            };
        }

        // ── Row iteration + primitives ───────────────────────────────────────

        private static IEnumerable<(List<string> Fields, DateTime Date, int LineNo)> DataRows(
            List<string> lines, char d, int expectedValueColumns, List<string> warnings)
        {
            for (int i = 1; i < lines.Count; i++)
            {
                var fields = SplitLine(lines[i], d);
                if (!TryParseDate(fields[0], out var date))
                    throw new FormatException(
                        $"Line {i + 1}: could not read \"{fields[0]}\" as a date. Use ISO format like 2026-07-22.");
                if (fields.Count - 1 < expectedValueColumns && fields.Count < 2)
                    throw new FormatException($"Line {i + 1}: no values after the date.");
                yield return (fields, date, i + 1);
            }
        }

        private static double Num(List<string> fields, int columnIndex, int lineNo)
        {
            // columnIndex is into the VALUE columns (header minus the date column).
            int fieldIndex = columnIndex + 1;
            if (columnIndex < 0 || fieldIndex >= fields.Count || !TryParseNumber(fields[fieldIndex], out var v))
                throw new FormatException($"Line {lineNo}: missing or non-numeric OHLC value.");
            return v;
        }

        internal static char DetectDelimiter(string headerLine)
        {
            int commas = headerLine.Count(c => c == ',');
            int semis = headerLine.Count(c => c == ';');
            int tabs = headerLine.Count(c => c == '\t');
            if (tabs >= commas && tabs >= semis && tabs > 0) return '\t';
            if (semis > commas) return ';';
            return ',';
        }

        /// <summary>Minimal quoted-field splitter — enough for spreadsheet exports
        /// ("a,b",c). No embedded-newline support (rows are lines by contract).</summary>
        internal static List<string> SplitLine(string line, char delimiter)
        {
            var fields = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else inQuotes = !inQuotes;
                }
                else if (ch == delimiter && !inQuotes)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else current.Append(ch);
            }
            fields.Add(current.ToString());
            return fields;
        }

        internal static bool TryParseDate(string s, out DateTime date)
        {
            s = s.Trim();
            if (DateTime.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date))
                return true;
            // Unix timestamps (seconds or milliseconds) — exchanges export these.
            if (long.TryParse(s, out var unix) && unix > 100_000_000)
            {
                date = unix > 100_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(unix).UtcDateTime
                    : DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
                return true;
            }
            date = default;
            return false;
        }

        internal static bool TryParseNumber(string s, out double value)
        {
            s = s.Trim().Replace("$", "").Replace("%", "");
            // Thousands separators from spreadsheet exports: "1,234.56" arrives
            // quoted, so the comma survives the split — strip it when a dot is
            // also present (invariant decimal point contract, documented).
            if (s.Contains(',') && s.Contains('.')) s = s.Replace(",", "");
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                   && !double.IsInfinity(value);
        }

        internal static string InferTimeframe(IReadOnlyList<DateTime> dates)
        {
            if (dates.Count < 2) return "1d";
            var deltas = new List<double>(dates.Count - 1);
            for (int i = 1; i < dates.Count; i++)
                deltas.Add((dates[i] - dates[i - 1]).TotalDays);
            deltas.Sort();
            double median = deltas[deltas.Count / 2];
            return median switch
            {
                < 0.05 => "1m",
                < 0.5 => "1h",
                < 3 => "1d",
                < 10 => "1w",
                < 45 => "1M",
                _ => "1y",
            };
        }
    }
}
