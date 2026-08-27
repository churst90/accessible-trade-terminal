using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>A price is never formatted at fixed low precision.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// <c>F0</c>/<c>F1</c>/<c>F2</c> on a quote-currency value collapses every sub-dollar asset to
    /// the same number. KAS at 0.0363 becomes "0.04"; SHIB at 0.00003 and PEPE at 0.0000009 both
    /// become "0.00"; on <c>F0</c> the whole asset class becomes "0". The repo has shipped
    /// <c>SpeechPriceFormatter</c> (precision scales with magnitude, ~3 significant digits) since
    /// the first time this was found — and then found it again, and again. <b>Three separate
    /// commits each fixed some of these and missed others</b>, which is what this scanner is for:
    /// the defect is not any one call site, it is that nothing was ever looking at all of them.
    /// </para>
    ///
    /// <para>
    /// The 2026-08-21 sweep that added this test found the class living somewhere much worse than
    /// speech. <c>BitstampProvider.PlaceOrderAsync</c> formatted the LIMIT PRICE with
    /// <c>ToString("F2")</c> — not a description of an order, the order itself. A limit at 0.0363
    /// went to the exchange as "0.04", and anything under half a cent as "0.00". Same three
    /// characters; the victim is money rather than a sentence.
    /// </para>
    ///
    /// <para>
    /// ── What is enforced ───────────────────────────────────────────────────────
    /// A fixed-precision format hole (or <c>ToString("Fn")</c>) is banned when the text
    /// immediately before it names a quantity that lives in quote currency. The word list is
    /// deliberately narrow — it excludes indicator-shaped words like "zone", "band" and "anchor",
    /// which appear all over bounded oscillators where F1/F2 is the right and intended format.
    /// A scanner that cries wolf gets an allowlist entry per line and stops being read.
    /// </para>
    ///
    /// <para>
    /// This cannot catch every price-space value — no source scanner can know that MACD is in
    /// price units or that a Bollinger band is. Those were fixed by reading, in the same sweep.
    /// What it catches is the class that recurs: someone writes "price" or "support" in a string
    /// and formats the number next to it at two decimals.
    /// </para>
    /// </summary>
    public class PriceFormatScanTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static readonly string[] ScannedProjects =
        {
            "AccessibleTrader.Core",
            "AccessibleTrader.Sdk",
            "AccessibleTrader.BlazorClient",
            "AccessibleTrader.BlazorClient.Components",
            "AccessibleTrader.WebHost",
            "Plugins",
        };

        /// <summary>
        /// Words that name a value in quote currency. Kept narrow on purpose: every addition here
        /// has to be a word that is essentially never used for a bounded oscillator reading.
        /// </summary>
        private static readonly Regex PriceWord = new(
            @"\b(price|support|resistance|vwap|entry|stop|take[- ]?profit|strike|bid|ask|"
            + @"neckline|pivot|liquidation|breakeven)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex FixedFormat = new(
            @"\{[^{}]*:F[012]\}|\.ToString\(""F[012]""",
            RegexOptions.Compiled);

        /// <summary>
        /// A unit spoken straight after the number means it is not a price — a percentage, a
        /// multiplier, a z-score, a bar count. These are the honest F2s and they stay.
        /// </summary>
        private static readonly Regex UnitSuffix = new(
            @"^\s*(%|percent|x\b|sigma|bars?\b|times?\b|dollars?\b|billion|million)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Sites where a price word sits next to a fixed format and that is CORRECT. Each entry
        /// is the file and the reason — an allowlist without a reason is just a mute button.
        /// </summary>
        private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
        {
            ["Core/Services/Strategies/RiskPlanResolver.cs"] =
                "\"Stop below entry at 1.5 × ATR(14)\" — the F1 is on the ATR MULTIPLIER, a "
                + "dimensionless count of ATRs, not on a price. The price words belong to the "
                + "surrounding sentence.",
        };

        [Fact]
        public void NoPriceIsSpokenAtFixedLowPrecision()
        {
            var offenders = new List<string>();

            foreach (var file in SourceFiles())
            {
                string rel = Path.GetRelativePath(RepoRoot(), file).Replace('\\', '/');
                if (Allowed.Keys.Any(k => rel.EndsWith(k, StringComparison.Ordinal))) continue;

                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string trimmed = line.TrimStart();

                    // A comment describing the bug is not the bug. Without this, documenting the
                    // fix would trip the guard that enforces it.
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("*")) continue;

                    foreach (Match m in FixedFormat.Matches(line))
                    {
                        if (UnitSuffix.IsMatch(line[m.Index..].Substring(m.Length))) continue;

                        // Look only at the words immediately before the hole. Scanning the whole
                        // line matches any sentence that happens to mention price somewhere.
                        string before = line[Math.Max(0, m.Index - 45)..m.Index];
                        if (!PriceWord.IsMatch(before)) continue;

                        offenders.Add($"{rel}:{i + 1}: {line.Trim()}");
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                "A price-space value is formatted at fixed low precision. Sub-dollar assets "
                + "(KAS, SHIB, PEPE) collapse to \"0\" / \"0.00\" here. Use "
                + "SpeechPriceFormatter.FormatPrice for spoken values, the {value:price} template "
                + "token for indicator metadata, and ToString(CultureInfo.InvariantCulture) for "
                + "anything going on the wire to an exchange.\n  "
                + string.Join("\n  ", offenders));
        }

        /// <summary>
        /// The wire format for an order is a separate rule with a separate failure: full precision
        /// AND invariant culture. Every provider but one already did this; Bitstamp used F2 with
        /// ambient culture, so a comma-decimal machine posted "0,04" for a price of 0.0363.
        /// </summary>
        [Fact]
        public void OrderPricesGoOnTheWireAtFullPrecisionAndInvariantCulture()
        {
            var offenders = new List<string>();
            var suspicious = new Regex(
                @"(signal\.(Price|StopLoss|TakeProfit|TriggerPrice|Quantity)(\.Value)?)\.ToString\(([^)]*)\)",
                RegexOptions.Compiled);

            // The 2026-08-23 culture scoping found this guard's reach was narrower than its
            // name: it only matched `signal.X.ToString(...)` receivers, so interpolating the
            // field bare (`{signal.Price}` — CurrentCulture) or with a hole format
            // (`{signal.Price:F8}` — a hole cannot carry a culture), or copying it to a local
            // first, all walked around it. The two interpolated spellings are offenders unless
            // the line carries an Invariant wrap; a bare direct copy of a money field is
            // refused outright because the scan cannot follow the local — use the field
            // directly, or transform it through a named method the scan's rules cover.
            var interpolated = new Regex(
                @"\{signal\.(Price|StopLoss|TakeProfit|TriggerPrice|Quantity)(\.Value)?(:[^}]*)?\}",
                RegexOptions.Compiled);
            var bareCopy = new Regex(
                @"\b(var|double|decimal)\s+\w+\s*=\s*signal\.(Price|StopLoss|TakeProfit|TriggerPrice|Quantity)(\.Value)?\s*;",
                RegexOptions.Compiled);

            // The 2026-08-26 HIGH pass found the guard's OTHER half missing. The rule is
            // "full precision AND invariant culture", and only the culture half was ever
            // checked — so `signal.Price.Value.ToString("0.##", CultureInfo.InvariantCulture)`
            // was green on all four SchwabProvider call sites while rounding every order price
            // to two decimals. Schwab lists sub-dollar equities quoting in $0.0001 increments
            // under Reg NMS 612, so a limit at 0.4567 went to the venue at 0.46 — 2% away from
            // the level chosen — and anything under half a cent became "0.00".
            //
            // The line is drawn at eight fractional digits, not at "any format string". 1e-8 is
            // the smallest unit any venue in this fleet quotes (one satoshi), so Kraken's and
            // Coinbase's deliberate "F8" is lossless for every price and size those APIs accept
            // and is left alone. Anything coarser than that rounds away a level the user
            // actually chose. Rounding an order price is the venue's job regardless: it knows
            // the instrument's tick size and we do not.
            const int LosslessFractionDigits = 8;

            static int? FractionDigitsOf(string formatArgs)
            {
                // Standard numeric format: F8, N2, f4 — digits default to 2 when omitted.
                var std = Regex.Match(formatArgs, @"""[FfNn](\d*)""");
                if (std.Success)
                    return std.Groups[1].Value.Length == 0 ? 2 : int.Parse(std.Groups[1].Value);

                // Custom numeric format: "0.##", "0.0000", "#,##0.00" — count the placeholders
                // after the decimal point.
                var custom = Regex.Match(formatArgs, @"""[0#][0#,]*\.([0#]+)""");
                if (custom.Success) return custom.Groups[1].Value.Length;

                // Custom format with no decimal point at all ("0", "#,##0") is zero digits.
                if (Regex.IsMatch(formatArgs, @"""[0#][0#,]*""")) return 0;

                return null; // no format string — full precision
            }

            foreach (var file in SourceFiles().Where(f => f.Contains("Plugins", StringComparison.Ordinal)))
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    bool offends = false;
                    foreach (Match m in suspicious.Matches(lines[i]))
                    {
                        var args = m.Groups[4].Value;
                        if (!args.Contains("InvariantCulture", StringComparison.Ordinal))
                            offends = true;
                        // ...and the culture being right does not make the precision right.
                        if (FractionDigitsOf(args) is int digits && digits < LosslessFractionDigits)
                            offends = true;
                    }
                    if (interpolated.IsMatch(lines[i])
                        && !lines[i].Contains("Invariant", StringComparison.Ordinal))
                        offends = true;
                    if (bareCopy.IsMatch(lines[i]))
                        offends = true;
                    if (offends)
                        offenders.Add(
                            $"{Path.GetRelativePath(RepoRoot(), file).Replace('\\', '/')}:{i + 1}: {lines[i].Trim()}");
                }
            }

            Assert.True(offenders.Count == 0,
                "An order field is serialised without InvariantCulture. On a comma-decimal machine "
                + "this sends \"0,04\" to the exchange; with a fixed Fn it also rounds the price the "
                + "user chose.\n  " + string.Join("\n  ", offenders));
        }

        private static IEnumerable<string> SourceFiles()
        {
            foreach (var proj in ScannedProjects)
            {
                string dir = Path.Combine(RepoRoot(), proj);
                if (!Directory.Exists(dir)) continue;

                foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    if (!file.EndsWith(".cs", StringComparison.Ordinal) &&
                        !file.EndsWith(".razor", StringComparison.Ordinal)) continue;
                    if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                        file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                    yield return file;
                }
            }
        }
    }
}
