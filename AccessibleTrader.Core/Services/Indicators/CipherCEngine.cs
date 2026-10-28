using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// One asset's cycle lengths: the daily cycle in days, the weekly cycle in weeks, the yearly
    /// cycle in years. <see cref="CalendarDays"/> says which days the daily range counts — calendar
    /// days for an asset that trades every day (Bitcoin), trading days for one that does not (the
    /// S&amp;P). <see cref="Source"/> is said aloud, so it is plain words. <see cref="CountDailyMin"/>,
    /// when not 0, is the shortest daily cycle the original counts with where it differs from the
    /// range its table shows (Bitcoin: table 54–66 days, counted from 48; Coinbase and MicroStrategy:
    /// 40–90, counted from 51).
    /// <see cref="WeeklyByWeeks"/> picks the weekly lows by <see cref="CipherCEngine.WeeklyByWeeks"/>,
    /// the original's rule on Gold, Silver and the gold and silver miners; the others by
    /// <see cref="CipherCEngine.WeeklyFromDaily"/>, on Ether without its far "double"
    /// (<see cref="WeeklyDoubles"/> false).
    /// <see cref="DssLength"/> and <see cref="DssSmooth"/> are the stochastic length and EMA length
    /// of the double-smoothed stochastic that names a daily low (<see cref="CipherCEngine.DetectDaily"/>);
    /// <see cref="PivotBreak"/> adds the second way to name one, the break of the last swing high;
    /// <see cref="DropUntilDue"/> lets an undercut drop a pending daily low at any time before it
    /// is due, not only within d / 3 bars of naming it (the Dow, the dollar, silver and Ether);
    /// <see cref="DoubleUnderNewest"/> lets a weekly-by-weeks "double" stand only under the newest
    /// weekly low (the three miners).
    /// </summary>
    public sealed record CyclePreset(
        string Key, string Name,
        double DailyMin, double DailyMax,
        double WeeklyMin, double WeeklyMax,
        double YearlyMin, double YearlyMax,
        bool CalendarDays, string Source, double CountDailyMin = 0, bool WeeklyByWeeks = false,
        int DssLength = CipherCEngine.DefaultDssLength,
        int DssSmooth = CipherCEngine.DefaultDssSmooth, bool PivotBreak = false, bool WeeklyDoubles = true,
        bool DropUntilDue = false, bool DoubleUnderNewest = false)
    {
        /// <summary>The shortest daily cycle the detector counts with.</summary>
        public double CountedDailyMin => CountDailyMin > 0 ? CountDailyMin : DailyMin;
    }

    /// <summary>
    /// The arithmetic behind <see cref="CipherCProvider"/>: the asset presets, how many bars a day
    /// is on this chart, and the cycle-low detector run once per tier.
    ///
    /// <para>
    /// <b>The daily tier</b> (<see cref="DetectDaily"/>) is the original's own rule, read bar by bar
    /// from it in TradingView's bar replay on candles (sixteen tuned assets, 2026-10-22 to 27; the
    /// replays cannot be committed, so <c>CipherCDailyTests</c> hold the engine to the study's model
    /// instead). With d the shortest daily cycle in bars:
    /// <list type="bullet">
    ///   <item><b>Naming.</b> Once the last low is d − 1 bars back, and d − 1 bars from where its hold
    ///         began, a bar names a low (the original's "unconfirmed" low) when the double-smoothed
    ///         stochastic (Bressert's DSS, <see cref="DoubleSmoothedStochastic"/>; length and EMA per
    ///         preset, 10 and 9 by default) is above its 5-bar EMA and between the oversold and
    ///         overbought levels (10 and 75). The level the original needs to still name a low is the
    ///         DSS rounded up, on every date tried. On gold, silver, the miners and the dollar
    ///         (<see cref="CyclePreset.PivotBreak"/>) a bar also names one by breaking the last
    ///         one-bar swing high, the first break since it formed that no close has made.</item>
    ///   <item><b>The low.</b> The lowest swing low after the cycle's top (the highest one-bar swing
    ///         high since the last low, or since the bar before it was confirmed), at most
    ///         <see cref="CandidateBack"/> bars back, and after any pending low dropped since.</item>
    ///   <item><b>The hold.</b> Half of d, rounded down (<see cref="HoldFraction"/>), from the low if it
    ///         is at least d − 2 after the last (<see cref="HoldFromBeforeMin"/>), else from the bar
    ///         before the naming bar. Never confirmed on the naming bar.</item>
    ///   <item><b>The drop.</b> A bar under the pending low within d / 3 bars of naming it, rounded up
    ///         (<see cref="DropDivisor"/>), drops it, unless the low comes due on that bar; after
    ///         that the low stands whatever the price does. On the Dow, the dollar, silver and Ether
    ///         (<see cref="CyclePreset.DropUntilDue"/>) any bar under it before it comes due drops it.</item>
    ///   <item><b>The start.</b> The first cycle counts from the lowest bar of the first d bars, which
    ///         is not drawn. From any start the lows are the same as from the original's own within
    ///         about two cycles.</item>
    /// </list>
    /// On the original's final daily lows over the sixteen assets this draws 1,202 of its 1,244
    /// and 35 it does not (F1 0.969; 0.950 with the d / 3 drop everywhere). Over their whole history,
    /// cancelled lows shown (2026-10-27), 1,328 of 1,372 and 36 others (0.971, was 0.952). Held out,
    /// on charts it was never fitted to, on the default count: platinum 0.939, Apple 0.985 (since
    /// 2022, 30 of 30); over the whole history of platinum, Apple and the euro 445 of 466, 16 others (0.960).
    /// </para>
    ///
    /// <para>
    /// <b>The cycle line,</b> on the yearly tier and on a weekly chart's weekly tier, is an inference
    /// from the original's settings screen ("overbought 75, oversold 10", "fast/slow EMA
    /// adjustment", "cycle length adjustment factor"): a Schaff Trend Cycle with its lengths scaled
    /// from the cycle range, fitted to the 25 S&amp;P daily lows read off videos (18 of 25 within
    /// two bars). After the line has been at or under the oversold level, its first cross up through
    /// the overbought level confirms the lowest swing low since the last low's minimum gap (60% of
    /// the shortest cycle), if it is still the lowest bar. It then holds as the daily tier's does,
    /// counted from the later of the low and the last low plus the shortest cycle less two; a lower
    /// bar before it is due takes its place. Unverified there.
    /// </para>
    ///
    /// <para>
    /// A confirmed low is never taken back: the first close under it is a FAILED cycle, at any gap
    /// (the original removed none of the 379 lows it confirmed in its replays). A pending low dropped
    /// is CANCELLED (the original's "cancelled" state).
    /// </para>
    ///
    /// <para>
    /// <b>The weekly tier.</b> On a daily chart the original does not find weekly lows itself: it
    /// picks them from its confirmed daily lows, each judged on the bar it is confirmed. Read from
    /// its candle replays (the table's weeks since the confirmed and the unconfirmed weekly low,
    /// logged per bar, 2016-26) and its whole-history drawings (2026-10-27). The second daily low
    /// after the newest weekly low is drawn as the candidate and the next daily low judges it.
    /// Everywhere but the metals (<see cref="WeeklyFromDaily"/>): a lower one cancels it and is a
    /// weekly low itself; one not lower makes it a weekly low; a weekly low is never taken back; and
    /// the first daily low after a weekly low is a weekly low too in two "double" cases. On Gold,
    /// Silver and the miners (<see cref="CyclePreset.WeeklyByWeeks"/>, <see cref="WeeklyByWeeks"/>)
    /// the same candidate is judged by distance: a new weekly low only 8 weeks short of the table's
    /// longest weekly cycle (28 or 26) from the newest, nearer only in its place when lower. The
    /// weekly low it calls confirmed, which its week count runs from, is the one before the newest.
    /// On the miners a double also needs its daily low under the newest weekly low.
    /// On the original's own daily lows over its whole history (nineteen assets, weekly lows after
    /// its first) this draws 701 of its 713 weekly lows with 10 others (F1 0.985): metals 0.892, the
    /// rest 0.994 (643 of 646, 5 others: the Dow's first years and two live candidates; before 2022
    /// 0.993, from 2022 0.995). On this engine's daily lows, run from the chart's first bar, 0.931
    /// (metals 0.859, the rest 0.939). The deep double's rule (under the daily low before the weekly
    /// low) was read from the original's whole-history drawings: after a candidate made a weekly low,
    /// the next daily low was a weekly low too in 36 of 40 cases when under the daily low before the
    /// weekly low and in 0 of 174 when not; the four exceptions are all under 16 bars after it, and
    /// the 16 are fitted (15 and 18 score lower). The machine counts daily lows, so a later start can
    /// leave it a daily low out of step for a while (within 32 daily lows on the nineteen assets'
    /// candles). The machine was read from the 2022-26 replays. The metals' week margins were fitted
    /// on the whole history; fitted on either side of 2022 alone they score 0.82 and 0.69 on the
    /// other (the rule before, 0.81 and 0.68), so most of the metals' gain is in sample. After some
    /// candidates are made weekly lows the original moves its reference on by one daily low; nothing
    /// tried separates those cases (supplied from its replays they would add 2 weekly lows on gold).
    /// The original has no weekly-chart mode ("Timeframe Not Optimised"), so on a weekly chart the
    /// weekly tier still finds its own lows with the cross and the hold.
    /// </para>
    ///
    /// <para>
    /// <b>What it is not.</b> Not an edge. Camel's own checklist showed none against shuffled
    /// prices in this project's tests, and CF's own SPX counts fall inside its stated 36–44 day
    /// window only 5 times in 23 (22–64 trading days, median 43; 17 of 23 fall in 30–50). The indicator follows the method;
    /// it does not prove it.
    /// </para>
    /// </summary>
    public static class CipherCEngine
    {
        // ── Fitted on CF's SPX labels (scratchpad cfproto/det2.py; see the class remarks) ──
        public const double MinGapFraction   = 0.6;   // of the shortest cycle, in bars
        public const double StcLengthFactor  = 0.25;  // stochastic length = mean cycle × this
        public const double StcFastFactor    = 0.5;   // fast EMA = mean cycle × this
        public const double StcSlowFactor    = 1.0;   // slow EMA = mean cycle × this
        public const double SearchBackCycles = 1.5;   // the low is looked for at most this many longest cycles back

        // ── Read from the original in bar replay (see "The hold" in the class remarks) ──
        public const double HoldFraction     = 0.5;   // a found low holds this much of the shortest cycle (rounded down) before it is confirmed
        public const int    HoldFromBeforeMin = 2;    // the hold counts from no earlier than the last low + shortest cycle − this
        public const double FarDoubleCycles  = 1.25;  // a first daily low under the weekly low this many shortest daily cycles after it is a weekly low too
        public const int    DoubleMinBars    = 16;    // ... or one after a candidate made a weekly low, under the daily low before it, at least this many bars after it
        public const double WeeksShortOfMax  = 8;     // metals: a new weekly low this many weeks short of the longest weekly cycle from the newest
        public const double DoubleWeeksShortOfMax = 14; // metals: a double this many weeks short of it

        // ── The daily tier (see DetectDaily) ──
        public const int    DefaultDssLength = 10;   // the double-smoothed stochastic's length, where a preset names none
        public const int    DefaultDssSmooth = 9;    // its EMA
        public const int    DssTrigger       = 5;    // the EMA of the line it must be above
        public const int    CandidateBack    = 99;   // the low is looked for at most this many bars back
        public const double DropDivisor      = 3;    // an undercut drops the pending low only within the shortest cycle / this (rounded up) bars of naming it

        // ── Presets ────────────────────────────────────────────────────────────────────

        // Said aloud after the preset's name. The indicator names no vendor (Cody, 2026-10-21).
        private const string FromCf     = "tuned";

        /// <summary>
        /// The sixteen assets CF says it is fine-tuned for (its own watchlist on cftrading.co.uk:
        /// SPX, DXY, NDQ, DJI, GOLD, SILVER, USOIL, GDX, GDXJ, SILJ, ETHUSD, BTCUSD, COIN, MSTR,
        /// MARA, RIOT). All sixteen range tables are CF's own. Fifteen were read from the table the
        /// original draws on its own chart in TradingView (2026-10-22): that corrected DXY (15–24
        /// days, 16–20 weeks, 3 years), crude oil (36–44 days, 16–20 weeks, 3 years), GDXJ and SILJ
        /// (22–26 weeks), and the Nasdaq, Dow and Ethereum yearly cycle (4 years), which had been
        /// estimates. SPX's table does not draw there, so SPX keeps the ranges read off Camel's
        /// videos. Crude oil's real table matches the original's crude oil lows worse than the
        /// estimate did (0.68 against 0.76 on exact bars); it is used because it is the original's.
        /// </summary>
        public static readonly IReadOnlyList<CyclePreset> Presets = new[]
        {
            new CyclePreset("SPX",    "S&P 500",     36, 44, 22, 31, 3.5, 4.5, false, FromCf, CountDailyMin: 32),
            new CyclePreset("NDQ",    "Nasdaq 100",  36, 44, 22, 31, 4,   4,   false, FromCf),
            // The Dow, the dollar, silver and Ether drop a pending low on any undercut before it is due
            // (DropUntilDue): on the original's full-history drawings (cancelled lows shown, 2026-10-27)
            // every undercut later than d / 3 after naming was a cancel there (Dow 13, dollar 8, silver 3,
            // Ether 2) and a keep on every other asset (64, the Nasdaq's 6 among them on the Dow's table).
            new CyclePreset("DJI",    "Dow Jones",   36, 44, 22, 31, 4,   4,   false, FromCf, DropUntilDue: true),
            // Its table says 15-24, but the original counts the dollar from 18 (candle replay, 2021-26,
            // 52 lows): confirmed 9 bars after the low on 39 of 52, a short cycle no sooner than the
            // last low + 25, a first label no sooner than the last low + 17.
            new CyclePreset("DXY",    "Dollar index", 15, 24, 16, 20, 3,  3,   false, FromCf, CountDailyMin: 18,
                DssLength: 8, PivotBreak: true, DropUntilDue: true),
            new CyclePreset("GOLD",   "Gold",        22, 28, 20, 28, 8,   8,   false, FromCf, WeeklyByWeeks: true, DssLength: 8, PivotBreak: true),
            // Silver's table says 22-28, as Gold's does, but the original counts it from 24 (bar replay
            // on candles, 2021-26, 40 lows): it confirms 12 bars after the low on 33 of 40, a short
            // cycle no sooner than the last low + 34, a first label no sooner than the last low + 23.
            // Gold, from the same table, confirms at 11 on 37 of 44. At 22 none of ours landed on its bar.
            new CyclePreset("SILVER", "Silver",      22, 28, 20, 28, 8,   8,   false, FromCf, CountDailyMin: 24, WeeklyByWeeks: true, PivotBreak: true,
                DropUntilDue: true),
            new CyclePreset("USOIL",  "Crude oil",   36, 44, 16, 20, 3,   3,   false, FromCf),
            new CyclePreset("GDX",    "Gold miners", 22, 28, 22, 26, 8,   8,   false, FromCf, WeeklyByWeeks: true, DssLength: 8, PivotBreak: true, DoubleUnderNewest: true),
            new CyclePreset("GDXJ",   "Junior gold miners", 22, 28, 22, 26, 8, 8, false, FromCf, WeeklyByWeeks: true, PivotBreak: true, DoubleUnderNewest: true),
            new CyclePreset("SILJ",   "Junior silver miners", 22, 28, 22, 26, 8, 8, false, FromCf, WeeklyByWeeks: true, PivotBreak: true, DoubleUnderNewest: true),
            // Its table says 15-25, but the original counts Ethereum from 16 (candle replay, 2022-26, 75
            // lows): confirmed 8 bars after the low on 59 of 75 (15 and 16 both hold 8), a short cycle
            // no sooner than the last low + 22 (16 - 2 + 8; 15 would give 21), a first label no sooner
            // than the last low + 15.
            // Ether's weekly lows have no far "double": with it, on the original's own daily lows over
            // the whole history, its weekly lows score F1 0.82 against 0.88 without.
            new CyclePreset("ETH",    "Ethereum",    15, 25, 7,  10, 4,   4,   true,  FromCf, CountDailyMin: 16, DssLength: 9, WeeklyDoubles: false,
                DropUntilDue: true),
            // CF's own BTC table (reviewer videos, 7-9 Aug 2025) reads 22-42 weeks; Camel says 28-32 on
            // his 2026 streams. The indicator is what Cipher C copies, so the table wins.
            // Its table says 54-66 days, but the original counts Bitcoin as if the shortest cycle were
            // 48 (bar replay on candles, 2022-26, 24 lows): its first label comes no sooner than the
            // last low + 47 (dMin - 1), it confirms 24 bars after the low on 19 of 24, and a short
            // cycle's confirmation waits until the last low + 70 (1.5 dMin - 2), on 4 of 5. At 54 every
            // one of ours came 3 bars late; at 48, 20 of 22 land on its bar and 22 of 25 lows match (19).
            // Bitcoin drops a pending low on any undercut before it is due, as the Dow does: its daily
            // chart never undercuts a pending low late in the window, so only the 4-hour and weekly
            // charts (the original runs the daily count on their bars) can tell, and there the
            // original's confirmed lows match 0.989 against 0.944 (4-hour) and 0.800 against 0.667
            // (weekly) with it; on the daily chart it removes one extra low.
            new CyclePreset("BTC",    "Bitcoin",     54, 66, 22, 42, 4,   4,   true,  FromCf, CountDailyMin: 48, DssLength: 14, DssSmooth: 14,
                DropUntilDue: true),
            // TradingView counts stock bars, so these CF daily ranges are treated as trading days;
            // that interpretation is an inference from the stock charts, not an explicit CF note.
            // Its table says 40-90 for both, but the original counts Coinbase and MicroStrategy from 51
            // (candle replays, 2021-26 and 2019-26): confirmed 25 bars after the low (half of 51,
            // rounded down; 50 would hold 25 too), a short cycle no sooner than the last low + 74
            // (51 - 2 + 25; 50 would give 73), on both. At 40 every one of ours came 5 bars early.
            new CyclePreset("COIN",   "Coinbase",    40, 90, 24, 42, 4,   4,   false, FromCf, CountDailyMin: 51, DssLength: 14),
            new CyclePreset("MSTR",   "MicroStrategy", 40, 90, 24, 42, 4, 4, false, FromCf, CountDailyMin: 51, DssLength: 14),
            // Its table says 36-44, but the original counts MARA from 38 (candle replay, 2022-26, 23 lows):
            // confirmed 19 bars after the low on 19, a short cycle no sooner than the last low + 55
            // (38 - 2 + 19; 55, 55, 56), a first label no sooner than + 37. At 36 every one came a bar early.
            new CyclePreset("MARA",   "Marathon Digital", 36, 44, 22, 26, 4, 4, false, FromCf, CountDailyMin: 38),
            // RIOT shares MARA's table but is counted from 39 (candle replay, 2022-26, 23 lows): confirmed
            // 19 bars after the low on 19 (floor(39/2); 38 holds 19 too), a short cycle no sooner than the
            // last low + 56 (39 - 2 + 19; 56, 56, 56), a first label no sooner than + 38 (39 - 1).
            new CyclePreset("RIOT",   "Riot Platforms", 36, 44, 22, 26, 4, 4, false, FromCf, CountDailyMin: 39),
        };

        /// <summary>
        /// Other names for the very instrument each preset is for: the index, metal, oil or coin
        /// under another data source's spelling ("^GSPC", "US30", "XAU/USD", "BTC/USDT"). Not its
        /// ETFs or futures: the original tunes none of SPY, QQQ, DIA or the S&amp;P future (it says
        /// "Unoptimised Asset Detected" on each, read 2026-10-25) and its default count matches its
        /// own lows there better than the index's preset (SPY daily F1 .758 against .710, QQQ .706
        /// against .645). Nor the Nasdaq Composite (IXIC), which is not the Nasdaq 100.
        /// </summary>
        private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["SPX"] = "SPX", ["US500"] = "SPX", ["GSPC"] = "SPX", ["SPX500"] = "SPX",
            ["NDQ"] = "NDQ", ["NDX"] = "NDQ", ["US100"] = "NDQ", ["NAS100"] = "NDQ",
            ["DJI"] = "DJI", ["US30"] = "DJI", ["DJIA"] = "DJI",
            ["DXY"] = "DXY",
            ["GOLD"] = "GOLD", ["XAU"] = "GOLD",
            ["SILVER"] = "SILVER", ["XAG"] = "SILVER",
            ["USOIL"] = "USOIL", ["WTI"] = "USOIL", ["OIL"] = "USOIL",
            ["GDX"] = "GDX", ["GDXJ"] = "GDXJ", ["SILJ"] = "SILJ",
            ["ETH"] = "ETH", ["ETHEREUM"] = "ETH",
            ["BTC"] = "BTC", ["XBT"] = "BTC", ["BITCOIN"] = "BTC",
            ["COIN"] = "COIN", ["MSTR"] = "MSTR", ["MARA"] = "MARA", ["RIOT"] = "RIOT",
        };

        private static readonly string[] QuoteSuffixes = { "USDT", "USDC", "USD", "EUR", "GBP" };

        public static CyclePreset Preset(string key) => Presets.First(p => p.Key == key);

        /// <summary>
        /// The preset for a chart symbol ("BTC/USD", "TVC:GOLD", "XAUUSD", "^GSPC"), or null
        /// when the symbol is none of the sixteen.
        /// </summary>
        public static CyclePreset? PresetForSymbol(string? symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return null;
            string s = symbol.Trim();
            int colon = s.LastIndexOf(':');
            if (colon >= 0) s = s[(colon + 1)..];
            s = s.TrimStart('^', '$');
            string head = s.Split('/', '-', '_', ' ', '.')[0];
            if (Aliases.TryGetValue(head, out var key)) return Preset(key);
            foreach (var q in QuoteSuffixes)
                if (head.Length > q.Length && head.EndsWith(q, StringComparison.OrdinalIgnoreCase)
                    && Aliases.TryGetValue(head[..^q.Length], out key))
                    return Preset(key);
            return null;
        }

        /// <summary>
        /// The original's count on a symbol it does not tune ("Default Count Selected"): its lows
        /// behave as a shortest daily cycle of 32 bars on every chart it was read on, stocks, ETFs,
        /// the S&amp;P on SP:SPX and coins alike (best fits 29 to 33; its shortest gap 18 to 23 bars).
        /// </summary>
        public const double DefaultCountDailyMin = 32;

        /// <summary>
        /// What a symbol that is none of the sixteen gets: the S&amp;P's ranges and weekly rule,
        /// counted from <see cref="DefaultCountDailyMin"/> bars. Its days are the chart's own, so a
        /// chart that trades every day counts 32 bars too, not 32 × 7/5.
        /// </summary>
        public static CyclePreset Fallback(bool tradesEveryDay)
            => Preset("SPX") with { CountDailyMin = DefaultCountDailyMin, CalendarDays = tradesEveryDay };

        /// <summary>Whether a preset setting picks a count rather than leaving it to the symbol (Auto, empty, or no such count).</summary>
        public static bool IsChosen(string? choice)
        {
            string c = (choice ?? "").Trim();
            return c.Length > 0 && !c.Equals("Auto", StringComparison.OrdinalIgnoreCase) && PresetForSymbol(c) != null;
        }

        /// <summary>
        /// Which preset drives a chart, and whether it was chosen for this asset. "Auto" (or blank)
        /// reads the symbol; a symbol that is none of the sixteen gets <see cref="Fallback"/>, and
        /// the reading says the asset is not one CF tunes.
        /// </summary>
        public static (CyclePreset Preset, bool FineTuned) Resolve(string? choice, string? symbol, bool tradesEveryDay)
        {
            string c = (choice ?? "").Trim();
            if (c.Length > 0 && !c.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                var chosen = PresetForSymbol(c);
                if (chosen != null) return (chosen, true);
            }
            var bySymbol = PresetForSymbol(symbol);
            if (bySymbol != null) return (bySymbol, true);
            return (Fallback(tradesEveryDay), false);
        }

        /// <summary>The first day counted on a symbol with no preset of its own: the original starts
        /// those on the first bar on or after 1 January 2000 (UTC), read off its Start label on
        /// sixteen charts, 2026-10-28.</summary>
        public static readonly DateTime UntunedStart = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>The bar the daily count starts on (<see cref="DetectDaily"/>'s confirmed seed):
        /// the chart's first bar on a symbol with a preset of its own, otherwise the first bar on or
        /// after <see cref="UntunedStart"/> (the first bar when there is none). The original starts
        /// each tuned symbol on a date of its own; those are not carried here.</summary>
        public static int StartBar(ReadOnlySpan<Ohlcv> data, bool tunedSymbol)
        {
            if (tunedSymbol) return 0;
            for (int i = 0; i < data.Length; i++) if (data[i].Date >= UntunedStart) return i;
            return 0;
        }

        // ── How long a bar is ─────────────────────────────────────────────────────────

        /// <summary>
        /// What the chart's bars are, measured from the bars themselves (so a test, a background
        /// tab and the lab agree with the browser without being told the timeframe).
        /// </summary>
        /// <param name="BarsPerDay">Bars in one of the chart's days: a calendar day when the asset
        /// trades every day, a trading day when it does not. 1 on a daily chart, 1/5 on a weekly
        /// stock chart, 24 on an hourly crypto chart.</param>
        /// <param name="TradesEveryDay">The market trades on Saturdays and Sundays (see <see cref="WeekendTrades"/>).</param>
        /// <param name="BarDays">The bar's own length in those days (1 daily, 5 or 7 weekly).</param>
        public readonly record struct BarClock(double BarsPerDay, bool TradesEveryDay)
        {
            public double DaysPerWeek => TradesEveryDay ? 7 : 5;
            public double DaysPerYear => TradesEveryDay ? 365.25 : 252;
            public double BarDays => 1 / BarsPerDay;
        }

        /// <summary>Saturday's and Sunday's bars must each be at least this share of a weekday
        /// bar's typical range to count as trading, flat bars left out. Over every cut of the lab's
        /// ten coins (each judged on its last 500 bars, from bar 100) the quieter weekend day was
        /// never under 0.37 of a weekday on daily bars, 0.35 on four-hour bars (Bitcoin);
        /// twelvedata's gold, which does not trade then, prints Saturdays at 0.17 and Sundays at
        /// 0.02.</summary>
        internal const double WeekendRangeShare = 0.25;

        /// <summary>
        /// Whether the market trades on the weekend: Saturday bars and Sunday bars each turn up in
        /// at least half the weeks, and each is a real session, its median range at least
        /// <see cref="WeekendRangeShare"/> of a weekday bar's. A flat bar (high equals low) is a
        /// day nothing traded and says nothing either way, so the ranges leave flat bars out:
        /// Bitstamp's Bitcoin in 2011 had more flat weekend bars than not, and counting them made
        /// its first 300 bars a five-day market and the rest seven (Dogecoin and Solana on four-hour
        /// bars likewise), so a replay of the early bars disagreed with the whole run.
        ///
        /// <para>
        /// The original counts gold's and the dollar index's weeks as five bars although
        /// TradingView dates a fifth of their bars on a Sunday (UTC: Monday's session opens on
        /// Sunday evening), and only crypto's as seven, read from its own "weeks since" counts on
        /// sixteen assets, 2026-10-23. A bar on Saturday or Sunday once meant "trades every day",
        /// so one stray weekend bar turned gold's 20 to 28 weeks from 100 to 140 bars into 140 to
        /// 196, and on twelvedata's gold, which since 2024 prints short Saturday and Sunday bars,
        /// the clock flipped as the last 500 bars moved across them (a replay agreed with the full
        /// run on 42 of 150 confirmations).
        /// </para>
        /// </summary>
        private static bool WeekendTrades(ReadOnlySpan<Ohlcv> bars)
        {
            if (bars.Length < 2) return false;
            var sat = new List<double>(); var sun = new List<double>(); var week = new List<double>();
            foreach (var b in bars)
            {
                double r = b.High - b.Low;
                if (r <= 0) continue;
                (b.Date.DayOfWeek switch { DayOfWeek.Saturday => sat, DayOfWeek.Sunday => sun, _ => week }).Add(r);
            }
            double weeks = Math.Max(1, (bars[^1].Date - bars[0].Date).TotalDays / 7);
            int satDays = 0, sunDays = 0; DateTime lastSat = default, lastSun = default;
            foreach (var b in bars)
            {
                var d = b.Date.Date;
                if (b.Date.DayOfWeek == DayOfWeek.Saturday && d != lastSat) { satDays++; lastSat = d; }
                if (b.Date.DayOfWeek == DayOfWeek.Sunday && d != lastSun) { sunDays++; lastSun = d; }
            }
            if (satDays < weeks / 2 || sunDays < weeks / 2) return false;
            double typical = Median(week);
            if (typical <= 0) return true;   // no weekday ranges (a flat or close-only source): the dates decide
            return Median(sat) >= WeekendRangeShare * typical && Median(sun) >= WeekendRangeShare * typical;
        }

        private static double WeekendShare(ReadOnlySpan<Ohlcv> bars)
        {
            int k = 0;
            foreach (var b in bars) if (b.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) k++;
            return bars.Length == 0 ? 0 : (double)k / bars.Length;
        }

        private static double Median(List<double> v)
        {
            if (v.Count == 0) return 0;
            v.Sort();
            return v[v.Count / 2];
        }

        public static BarClock MeasureBars(ReadOnlySpan<Ohlcv> data)
        {
            int n = data.Length;
            if (n < 3) return new BarClock(1, false);
            int from = Math.Max(1, n - 500);
            var gaps = new List<double>(n - from);
            for (int i = from; i < n; i++)
            {
                double g = (data[i].Date - data[i - 1].Date).TotalSeconds;
                if (g > 0) gaps.Add(g);
            }
            bool anyWeekend = false;
            for (int i = from - 1; i < n; i++)
                if (data[i].Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) { anyWeekend = true; break; }
            if (gaps.Count == 0) return new BarClock(1, anyWeekend);
            gaps.Sort();
            double spacing = gaps[gaps.Count / 2];
            double spacingDays = spacing / 86400.0;
            // A daily or shorter bar is judged by what trades on the weekend. A bar of two or three
            // days spans the weekend either way, so it is judged by how many fall on one: a fifth
            // or more (Bitcoin's two-day bars 0.28, twelvedata's gold at most 0.13). A weekly or
            // longer bar is dated on whatever day the source starts its week, so only its date can tell.
            bool weekend = spacingDays < 1.5 ? WeekendTrades(data[(from - 1)..])
                         : spacingDays < 4 ? WeekendShare(data[(from - 1)..]) >= 0.2
                         : anyWeekend;

            if (spacingDays < 20.0 / 24 && weekend) return new BarClock(86400.0 / spacing, true);
            if (spacingDays < 1.5 && !weekend)
            {
                // A stock's intraday bars: how many make one session, from the dates themselves.
                // Not from the spacing: with two bars a session (the Dow's four-hour chart, 13:30
                // and 17:30 UTC) half the gaps are the 20-hour night, so the median gap is a night
                // and the chart once read as daily.
                var perDate = new Dictionary<DateTime, int>();
                for (int i = from - 1; i < n; i++)
                {
                    var d = data[i].Date.Date;
                    perDate[d] = perDate.TryGetValue(d, out var c) ? c + 1 : 1;
                }
                var counts = perDate.Values.OrderBy(v => v).ToList();
                int perSession = counts[counts.Count / 2];
                if (perSession > 1 || spacingDays < 20.0 / 24) return new BarClock(Math.Max(1, perSession), false);
            }
            if (spacingDays < 4)
                return new BarClock(weekend ? 1 / Math.Round(spacingDays) : 1 / Math.Max(1, Math.Round(spacingDays)), weekend);
            // Weekly and longer: a week is 7 calendar or 5 trading days, a month about 30.4 or 21.7.
            // A bar spanning S calendar days spans S × 5/7 trading days, so a stock's weekly bar is
            // a fifth of a bar per trading day. (The factor was once 5/7 instead of 7/5, which made
            // a weekly stock bar 9.8 trading days long and switched the weekly tier off there.)
            return new BarClock((weekend ? 1.0 : 7.0 / 5.0) / spacingDays, weekend);
        }

        // ── One tier ───────────────────────────────────────────────────────────────────

        /// <summary>A tier's range in BARS of this chart, and whether the tier runs here.</summary>
        public readonly record struct TierBars(double MinBars, double MaxBars, bool Active)
        {
            public double MeanBars => (MinBars + MaxBars) / 2;
            public int MinGap => Math.Max(1, (int)Math.Round(MinBars * MinGapFraction));
        }

        /// <summary>
        /// A tier runs only when the chart's bar is no longer than the tier's unit — a daily cycle
        /// on a daily or shorter chart, a weekly cycle on a weekly or shorter one, a yearly cycle on
        /// anything up to a year — and its shortest cycle spans at least six bars. That is what
        /// keeps "DCL" off a weekly chart: on a weekly chart a daily cycle is six to nine bars at
        /// most and is the weekly cycle's business.
        /// </summary>
        public static TierBars Tier(double minDays, double maxDays, double unitDays, BarClock clock)
        {
            double minBars = minDays * clock.BarsPerDay, maxBars = maxDays * clock.BarsPerDay;
            bool active = clock.BarDays <= unitDays * 1.05 && minBars >= 6 && maxBars >= minBars;
            return new TierBars(minBars, maxBars, active);
        }

        /// <summary>What one tier found.</summary>
        public sealed class TierRun
        {
            /// <summary>The cycle lows that stood at the end of the data, oldest first.</summary>
            public List<int> Lows { get; } = new();
            /// <summary>The bar each of <see cref="Lows"/> was confirmed on.</summary>
            public List<int> ConfirmedAt { get; } = new();
            /// <summary>The bar the search found each of <see cref="Lows"/> on (a lower low while it
            /// held may have taken its place since).</summary>
            public List<int> FoundAt { get; } = new();
            /// <summary>Per bar: the newest confirmed low known ON that bar, or -1.</summary>
            public int[] LastLowAt = Array.Empty<int>();
            /// <summary>The bar the count starts from (the daily tier's seed, not a low of its own), or -1.</summary>
            public int Start = -1;
            /// <summary>
            /// Events, by the bar they happen on. Confirmed, Cancelled and Failed carry the price of
            /// the low concerned (Cancelled: the pending low a lower bar undercut; Failed: the
            /// confirmed low a close went under); Pending the bar's close; Updated, on a confirmation
            /// that came after a cancellation, the price of the low that was cancelled (the one this
            /// low replaces).
            /// </summary>
            public double[] Confirmed = Array.Empty<double>(), Cancelled = Array.Empty<double>(),
                            Updated = Array.Empty<double>(), Pending = Array.Empty<double>(),
                            Failed = Array.Empty<double>();
            /// <summary>Per bar: the confirmed low a failure on that bar closed under, or -1.</summary>
            public int[] ClosedUnder = Array.Empty<int>();
            /// <summary>Per bar: the low found and holding at the end of that bar (the pending low), or -1.
            /// The weekly machines' is the low the original's unconfirmed weekly count runs from: the
            /// newest drawn, the candidate included (see <see cref="WeeklyFromDaily"/>'s Anchor).</summary>
            public int[] HeldAt = Array.Empty<int>();
            /// <summary>Per bar: the pending low a cancellation on that bar dropped, or -1.</summary>
            public int[] CancelledLow = Array.Empty<int>();
        }

        /// <summary>
        /// Runs the cycle line's detector for one tier: the weekly tier on a chart with no daily one,
        /// and the yearly tier (the daily tier is <see cref="DetectDaily"/>). Everything it publishes
        /// per bar is decided from that bar and earlier ones; only <see cref="TierRun.Lows"/> (the
        /// final list) knows the future.
        /// </summary>
        public static TierRun Detect(ReadOnlySpan<Ohlcv> data, TierBars tier, double overbought, double oversold)
        {
            int n = data.Length;
            var run = new TierRun
            {
                LastLowAt = Enumerable.Repeat(-1, n).ToArray(), HeldAt = Enumerable.Repeat(-1, n).ToArray(),
                ClosedUnder = Enumerable.Repeat(-1, n).ToArray(), CancelledLow = Enumerable.Repeat(-1, n).ToArray(),
                Confirmed = Nan(n), Cancelled = Nan(n), Updated = Nan(n), Pending = Nan(n), Failed = Nan(n),
            };
            if (!tier.Active || n == 0) return run;

            double mean = tier.MeanBars;
            int len  = Math.Max(3, (int)Math.Round(mean * StcLengthFactor));
            int fast = Math.Max(2, (int)Math.Round(mean * StcFastFactor));
            int slow = Math.Max(fast + 1, (int)Math.Round(mean * StcSlowFactor));
            int swing = Math.Clamp((int)Math.Round(tier.MinBars / 7), 2, 5);
            int minGap = tier.MinGap;
            int searchBack = (int)Math.Round(tier.MaxBars * SearchBackCycles);
            int hold = Math.Max(0, (int)Math.Floor(tier.MinBars * HoldFraction));
            int holdFrom = Math.Max(0, (int)Math.Round(tier.MinBars) - HoldFromBeforeMin);

            var close = new double[n]; var low = new double[n]; var high = new double[n];
            for (int i = 0; i < n; i++) { close[i] = data[i].Close; low[i] = data[i].Low; high[i] = data[i].High; }
            var stc = SchaffTrendCycle(close, len, fast, slow);

            bool armed = false, cancelledSince = false;
            double replaced = double.NaN;
            int last = -1, failedFor = -1, held = -1;   // held: a found low waiting out its hold
            int found = -1;                             // the bar the search found it on
            for (int t = slow * 2; t < n; t++)
            {
                if (stc[t] <= oversold && !armed)
                {
                    armed = true;
                    if (last < 0 || t - last >= minGap) run.Pending[t] = close[t];
                }
                else if (stc[t] <= oversold) armed = true;

                // A confirmed low is never taken back (the original: 0 of 379 on sixteen assets).
                // The first close under it fails the cycle.
                if (last >= 0 && close[t] < low[last] && failedFor != last)
                {
                    run.Failed[t] = low[last];
                    run.ClosedUnder[t] = last;
                    failedFor = last;
                }

                if (held >= 0)
                {
                    // A bar under the held low up to the bar before it is due cancels it; on that
                    // bar the held low is confirmed whatever the bar's low (the original's rule on
                    // daily charts). The cycle line's cross, which may not come again, moves it to
                    // the undercutting bar.
                    if (low[t] < low[held] && t - (last >= 0 ? Math.Max(held, last + holdFrom) : held) < hold)
                    {
                        run.Cancelled[t] = replaced = low[held];
                        run.CancelledLow[t] = held;
                        cancelledSince = true;
                        held = t;
                    }
                }
                else if (armed && stc[t - 1] < overbought && stc[t] >= overbought)
                {
                    int start = last >= 0 ? last + minGap : 0;
                    start = Math.Max(Math.Max(start, t - searchBack), swing);
                    int c = -1;
                    for (int i = start; i < t; i++)
                    {
                        if (c >= 0 && low[i] >= low[c]) continue;
                        bool swingLow = true;
                        for (int j = i - swing; j <= Math.Min(t, i + swing) && swingLow; j++)
                            if (low[j] < low[i]) swingLow = false;
                        if (swingLow) c = i;
                    }
                    if (c >= 0)
                    {
                        bool stillLowest = true;
                        for (int j = c + 1; j <= t && stillLowest; j++)
                            if (low[j] < low[c]) stillLowest = false;
                        if (stillLowest) { held = c; found = t; }
                    }
                }
                if (held >= 0 && t - (last >= 0 ? Math.Max(held, last + holdFrom) : held) >= hold)
                {
                    run.Confirmed[t] = low[held];
                    if (cancelledSince) run.Updated[t] = replaced;
                    run.Lows.Add(held); run.ConfirmedAt.Add(t); run.FoundAt.Add(found);
                    last = held; held = -1; armed = false; cancelledSince = false;
                }
                run.LastLowAt[t] = last;
                run.HeldAt[t] = held;
            }
            return run;
        }

        /// <summary>
        /// The double-smoothed stochastic (Bressert's DSS) the daily tier names a low with, and its
        /// trigger: s = EMA(stochastic of the close in the high-low range, <paramref name="smooth"/>),
        /// d = EMA(stochastic of s in its own range, <paramref name="smooth"/>), trigger = EMA(d, 5).
        /// A stochastic with no full window yet, or a flat one, reads 50; each EMA starts from its
        /// first input.
        /// </summary>
        public static (double[] Dss, double[] Trigger) DoubleSmoothedStochastic(double[] high, double[] low, double[] close,
                                                                                 int length, int smooth)
        {
            int n = close.Length;
            var s = Ema(Stochastic(close, high, low, length), smooth);
            var d = Ema(Stochastic(s, s, s, length), smooth);
            return (d, Ema(d, DssTrigger));
        }

        private static double[] Stochastic(double[] x, double[] high, double[] low, int length)
        {
            int n = x.Length;
            var o = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (i < length - 1) { o[i] = 50; continue; }
                double hh = high[i], ll = low[i];
                for (int j = i - length + 1; j < i; j++) { if (high[j] > hh) hh = high[j]; if (low[j] < ll) ll = low[j]; }
                double v = (x[i] - ll) / (hh - ll) * 100;
                o[i] = double.IsFinite(v) ? v : 50;
            }
            return o;
        }

        private static double[] Ema(double[] x, int length)
        {
            var o = new double[x.Length];
            if (x.Length == 0) return o;
            double a = 2.0 / (length + 1);
            o[0] = x[0];
            for (int i = 1; i < x.Length; i++) o[i] = o[i - 1] + a * (x[i] - o[i - 1]);
            return o;
        }

        /// <summary>
        /// The daily tier: the original's own rule, read bar by bar from it in TradingView's bar
        /// replay (sixteen tuned assets, 2026-10-27; see "The daily tier" in the class remarks).
        /// Everything it publishes per bar is decided from that bar and earlier ones. The low before
        /// the first cycle is the lowest bar of the first shortest cycle, or <paramref name="seed"/>;
        /// it is where the count starts, not a low of its own (not in <see cref="TierRun.Lows"/>).
        /// With <paramref name="seedConfirmed"/> the seed is the original's Start bar: the count
        /// starts there as if a low there had been confirmed one hold later, so the first top is
        /// looked for from the bar before that (the provider's start, <see cref="StartBar"/>).
        /// </summary>
        public static TierRun DetectDaily(ReadOnlySpan<Ohlcv> data, TierBars tier, int dssLength, int dssSmooth,
                                          double overbought, double oversold, bool pivotBreak, int seed = -1,
                                          bool dropUntilDue = false, bool seedConfirmed = false)
        {
            int n = data.Length;
            var run = new TierRun
            {
                LastLowAt = Enumerable.Repeat(-1, n).ToArray(), HeldAt = Enumerable.Repeat(-1, n).ToArray(),
                ClosedUnder = Enumerable.Repeat(-1, n).ToArray(), CancelledLow = Enumerable.Repeat(-1, n).ToArray(),
                Confirmed = Nan(n), Cancelled = Nan(n), Updated = Nan(n), Pending = Nan(n), Failed = Nan(n),
            };
            if (!tier.Active || n < 3) return run;

            double dmin = tier.MinBars;
            int hold = Math.Max(0, (int)Math.Floor(dmin * HoldFraction));
            int holdFrom = (int)Math.Round(dmin) - HoldFromBeforeMin;
            double searchFrom = dmin - 1;
            int originGap = (int)Math.Round(dmin) - 1;
            int dropWithin = (int)Math.Ceiling(dmin / DropDivisor);

            var close = new double[n]; var low = new double[n]; var high = new double[n];
            for (int i = 0; i < n; i++) { close[i] = data[i].Close; low[i] = data[i].Low; high[i] = data[i].High; }
            var (dss, trigger) = DoubleSmoothedStochastic(high, low, close, dssLength, dssSmooth);

            if (seed < 0)
            {
                seed = 0;
                for (int i = 1; i < Math.Min(n, (int)Math.Round(dmin)); i++) if (low[i] < low[seed]) seed = i;
            }
            run.Start = seed;

            bool cancelledSince = false;
            double replaced = double.NaN;
            int last = seed, held = -1, named = 0, origin = 0, dropBar = -1, failedFor = -1;
            int confirmedAt = seedConfirmed ? seed + hold : int.MinValue / 2, lastOrigin = int.MinValue / 2;
            bool real = false;   // last is a low of its own, not the seed
            for (int t = seed + 1; t < n; t++)
            {
                if (real && close[t] < low[last] && failedFor != last)
                {
                    run.Failed[t] = low[last];
                    run.ClosedUnder[t] = last;
                    failedFor = last;
                }

                bool due = held >= 0 && t - origin >= hold && t > named;
                if (held >= 0)
                {
                    // An undercut drops the pending low only soon after it was named (any time before
                    // it is due, with dropUntilDue); later the low stands whatever the price does. A
                    // low due to confirm on this bar confirms.
                    if (low[t] < low[held] && !due && (dropUntilDue || t - named < dropWithin))
                    {
                        run.Cancelled[t] = replaced = low[held];
                        run.CancelledLow[t] = held;
                        cancelledSince = true;
                        held = -1; dropBar = t;
                    }
                }
                else if (t - last >= searchFrom && t - lastOrigin >= originGap)
                {
                    bool dssUp = dss[t] > trigger[t] && dss[t] < overbought && dss[t] > oversold;
                    if (dssUp || pivotBreak)
                    {
                        int c = Candidate(high, low, t, last, confirmedAt, dropBar, out int top);
                        if (c < t && (dssUp || BreaksLastSwingHigh(high, close, t, c, top)))
                        {
                            held = c; named = t;
                            origin = held >= last + holdFrom ? held : t - 1;
                            run.Pending[t] = close[t];
                        }
                    }
                }

                if (held >= 0 && t - origin >= hold && t > named)
                {
                    run.Confirmed[t] = low[held];
                    if (cancelledSince) run.Updated[t] = replaced;
                    run.Lows.Add(held); run.ConfirmedAt.Add(t); run.FoundAt.Add(named);
                    last = held; held = -1; dropBar = -1; confirmedAt = t; lastOrigin = origin;
                    cancelledSince = false; real = true;
                }
                run.LastLowAt[t] = real ? last : -1;
                run.HeldAt[t] = held;
            }
            return run;
        }

        /// <summary>
        /// The low a naming bar <paramref name="t"/> names, or <paramref name="t"/> for none: the
        /// lowest swing low (no higher than the bar before, lower than the bar after; the first of
        /// equal ones) after the cycle's top and after any pending low dropped since, at most
        /// <see cref="CandidateBack"/> bars back. The top is the highest one-bar swing high (no lower
        /// than the bar before, higher than the bar after) from the last low, or from the bar before
        /// it was confirmed if later; with none, the highest high up to two bars back (one, if the
        /// bar is not a higher high).
        /// </summary>
        private static int Candidate(double[] high, double[] low, int t, int last, int confirmedAt, int dropBar, out int top)
        {
            int exclude = high[t] > high[t - 1] ? 2 : 1;
            int end = t - exclude > last + 1 ? t - exclude : t - 1;
            int from = confirmedAt - 1 <= end ? Math.Max(last + 1, confirmedAt - 1) : last + 1;
            top = -1;
            for (int q = Math.Max(from, 1); q <= t - 1; q++)
                if (high[q - 1] <= high[q] && high[q + 1] < high[q] && (top < 0 || high[q] >= high[top])) top = q;
            if (top < 0)
            {
                top = from;
                for (int q = from + 1; q <= end; q++) if (high[q] >= high[top]) top = q;
            }
            int s0 = Math.Max(Math.Max(top + 1, t - CandidateBack), Math.Max(last + 1, dropBar));
            int c = t;
            for (int k = s0; k < t; k++)
            {
                if (k - 1 >= last + 1 && low[k] > low[k - 1]) continue;
                if (!(low[k] < low[k + 1])) continue;
                if (c == t || low[k] < low[c]) c = k;
            }
            return c;
        }

        /// <summary>
        /// The second way to name a low, on the presets that have it (<see cref="CyclePreset.PivotBreak"/>):
        /// bar <paramref name="t"/>'s high is the first break of the last one-bar swing high (two or
        /// more bars back) since it formed, judged by closes: no bar after it closed above it.
        /// The low named must come after the cycle's top.
        /// </summary>
        private static bool BreaksLastSwingHigh(double[] high, double[] close, int t, int candidate, int top)
        {
            if (candidate <= top) return false;
            int j = -1;
            for (int q = t - 2; q >= 1; q--)
                if (high[q - 1] <= high[q] && high[q + 1] < high[q]) { j = q; break; }
            if (j < 0 || !(high[t] > high[j])) return false;
            for (int q = j + 1; q < t; q++) if (close[q] > high[j]) return false;
            return true;
        }

        /// <summary>
        /// The weekly tier on a chart with a daily one, as the original picks it everywhere but the
        /// metals (see "The weekly tier" in the class remarks): a machine driven only by the daily
        /// run's confirmations, each acted on the bar it happens. The second daily low after the
        /// newest weekly low U is drawn at once as the candidate P and judged by the next: a lower
        /// one cancels P and is itself a new weekly low, at any distance; one not lower makes P a
        /// weekly low and counts as the first daily low after it. A weekly low is never taken back.
        /// Before P is drawn, the first daily low F after U is a weekly low of its own (a "double")
        /// when the second is not under it and either F is under U at least
        /// <see cref="FarDoubleCycles"/> shortest daily cycles (<paramref name="dailyMin"/>) after it
        /// (off with <paramref name="farDoubles"/> false, Ether), or U was a candidate made a weekly
        /// low (so F, which judged it, is not under it) and F is under the daily low before U and at
        /// least <see cref="DoubleMinBars"/> bars after U.
        /// <see cref="TierRun.Lows"/> are the weekly lows at the end (the live candidate among them),
        /// <see cref="TierRun.FoundAt"/> the bar each was drawn, <see cref="TierRun.ConfirmedAt"/> the
        /// bar each became a weekly low (-1 for the candidate). <see cref="TierRun.LastLowAt"/> is the
        /// original's confirmed weekly low, the one before the newest, which its week count runs
        /// from. Confirmed fires when that moves on, Cancelled on the bar a candidate is undercut
        /// (carrying its price), and Updated on that same bar (the low the new one replaces).
        /// </summary>
        public static TierRun WeeklyFromDaily(TierRun daily, ReadOnlySpan<Ohlcv> data, double dailyMin, bool farDoubles = true)
        {
            int n = data.Length;
            var run = NewRun(n);
            if (daily.LastLowAt.Length != n) return run;

            var weekly = new List<int>();                     // weekly lows, newest last
            var drawnOn = new SortedDictionary<int, int>();   // drawn low -> bar drawn
            var fixedOn = new Dictionary<int, int>();         // weekly low -> bar it became one
            var after = new List<int>();                      // daily lows since the newest, before a candidate
            var lows = new double[n];
            for (int i = 0; i < n; i++) lows[i] = data[i].Low;
            double Low(int i) => lows[i];
            int cand = -1, anchor = -1, last = -1;
            bool fromCandidate = false;                       // the newest weekly low was a candidate made one
            int candPrev = -1, uPrev = -1;                    // the daily low before the candidate; before the newest weekly low, when fromCandidate
            double replaced = double.NaN;
            for (int t = 0; t < n; t++)
            {
                if (!double.IsNaN(daily.Confirmed[t]) && daily.LastLowAt[t] > last)
                {
                    int d = last = daily.LastLowAt[t];
                    void Add(int i) { weekly.Add(i); drawnOn.TryAdd(i, t); fixedOn[i] = t; }
                    if (weekly.Count == 0) Add(d);
                    else if (cand >= 0)
                    {
                        if (Low(d) < Low(cand))
                        {
                            // Undercut: the candidate is cancelled and the lower low is a weekly low.
                            drawnOn.Remove(cand);
                            run.Cancelled[t] = replaced = Low(cand); run.CancelledLow[t] = cand;
                            Add(d); after.Clear(); fromCandidate = false;
                        }
                        else
                        {
                            Add(cand); uPrev = candPrev; after.Clear(); after.Add(d); fromCandidate = true;
                        }
                        cand = -1;
                    }
                    else
                    {
                        after.Add(d);
                        if (after.Count == 2)
                        {
                            int u = weekly[^1], f = after[0], s = after[1];
                            bool doubled = Low(s) >= Low(f)
                                && ((farDoubles && Low(f) < Low(u) && f - u >= FarDoubleCycles * dailyMin)
                                    || (fromCandidate && uPrev >= 0 && Low(f) < Low(uPrev) && f - u >= DoubleMinBars));
                            if (doubled) { Add(f); after.RemoveAt(0); fromCandidate = false; }
                            else { cand = s; candPrev = f; drawnOn[s] = t; }
                        }
                    }
                }
                Anchor(run, t, weekly, lows, ref anchor, ref replaced, cand);
            }
            Publish(run, drawnOn, fixedOn);
            return run;
        }

        /// <summary>
        /// The weekly tier on Gold, Silver and the gold and silver miners, where the original's
        /// table ends the weekly cycle at 28 or 26 weeks: the same candidate as
        /// <see cref="WeeklyFromDaily"/> (the second daily low after the reference low), judged by
        /// distance from the newest weekly low U. <paramref name="newBars"/> is
        /// <see cref="WeeksShortOfMax"/> weeks short of the longest weekly cycle, in bars;
        /// <paramref name="doubleBars"/> <see cref="DoubleWeeksShortOfMax"/> short of it.
        /// <list type="bullet">
        ///   <item>A next daily low under the candidate cancels it. It is a new weekly low when
        ///         <paramref name="newBars"/> or more from U, else it takes U's place when under U
        ///         (U is cancelled), else it is only the new reference low, drawn nowhere.</item>
        ///   <item>One not under it makes the candidate a weekly low on the same terms (far enough:
        ///         a new one; nearer but under U: in U's place); else the candidate goes, not
        ///         cancelled. That daily low is then the first after the reference.</item>
        ///   <item>A "double": when the first daily low after the reference is under the reference
        ///         and the second is not under the first, the first is a weekly low if at least
        ///         <paramref name="doubleBars"/> from U and, with <paramref name="doubleUnderNewest"/>
        ///         (the miners), under U as well. Read from the original's whole-history drawings: of
        ///         the five miner doubles, four fit (two it skipped were not under U, two it drew were)
        ///         and one does not (junior gold miners, drawn above U). Most of the gain is what
        ///         follows: the machine stays in step with the original afterwards (GDX and GDXJ weekly
        ///         lows 19 → 21 of 22, others 5 → 1). On Gold the same condition loses 2, so Gold and
        ///         Silver keep the plain double. Level with U is untested.</item>
        /// </list>
        /// The original's first daily low after a weekly low is never judged against it. Its
        /// re-anchoring after some candidates are made weekly lows (the next candidate one daily low
        /// later) is not reproduced: nothing found separates those cases. The outputs are as
        /// <see cref="WeeklyFromDaily"/>'s; on a bar that cancels both the candidate and U,
        /// Cancelled carries U.
        /// </summary>
        public static TierRun WeeklyByWeeks(TierRun daily, ReadOnlySpan<Ohlcv> data, double newBars, double doubleBars,
            bool doubleUnderNewest = false)
        {
            int n = data.Length;
            var run = NewRun(n);
            if (daily.LastLowAt.Length != n) return run;

            var weekly = new List<int>();                     // weekly lows, newest last
            var drawnOn = new SortedDictionary<int, int>();   // drawn low -> bar drawn
            var fixedOn = new Dictionary<int, int>();         // weekly low -> bar it became one
            var lows = new double[n];
            for (int i = 0; i < n; i++) lows[i] = data[i].Low;
            double Low(int i) => lows[i];
            int cand = -1, reference = -1, first = -1, count = 0, anchor = -1, last = -1, gone = -1;
            double replaced = double.NaN;
            for (int t = 0; t < n; t++)
            {
                if (!double.IsNaN(daily.Confirmed[t]) && daily.LastLowAt[t] > last)
                {
                    int d = last = daily.LastLowAt[t];
                    void Add(int i) { weekly.Add(i); drawnOn.TryAdd(i, t); fixedOn[i] = t; }
                    // A new weekly low far enough from the newest, or (when allowed) one in its place
                    // when lower; either becomes the reference.
                    bool Make(int i, double gap, bool replace)
                    {
                        int u = weekly[^1];
                        if (i - u >= gap) Add(i);
                        else if (replace && Low(i) < Low(u))
                        {
                            drawnOn.Remove(u); fixedOn.Remove(u);
                            run.Cancelled[t] = replaced = Low(u); run.CancelledLow[t] = u;
                            weekly[^1] = i; drawnOn.TryAdd(i, t); fixedOn[i] = t;
                        }
                        else return false;
                        reference = i;
                        return true;
                    }
                    if (weekly.Count == 0) { Add(d); reference = d; }
                    else if (cand >= 0)
                    {
                        if (Low(d) < Low(cand))
                        {
                            drawnOn.Remove(cand);
                            run.Cancelled[t] = replaced = Low(cand); run.CancelledLow[t] = cand;
                            if (!Make(d, newBars, true)) reference = d;
                            count = 0;
                        }
                        else
                        {
                            // A candidate that goes undrawn is still the low the table's unconfirmed
                            // week count runs from, as a reference low drawn nowhere is.
                            if (!Make(cand, newBars, true)) { drawnOn.Remove(cand); gone = cand; }
                            count = 1; first = d;
                        }
                        cand = -1;
                    }
                    else if (++count == 1) first = d;
                    else if (Low(first) < Low(reference) && Low(d) >= Low(first)
                        && (!doubleUnderNewest || Low(first) < Low(weekly[^1])) && Make(first, doubleBars, false)) { first = d; count = 1; }
                    else { cand = d; drawnOn[d] = t; }
                }
                Anchor(run, t, weekly, lows, ref anchor, ref replaced, cand >= 0 ? cand : Math.Max(reference, gone));
            }
            Publish(run, drawnOn, fixedOn);
            return run;
        }

        private static TierRun NewRun(int n) => new()
        {
            LastLowAt = Enumerable.Repeat(-1, n).ToArray(), HeldAt = Enumerable.Repeat(-1, n).ToArray(),
            ClosedUnder = Enumerable.Repeat(-1, n).ToArray(), CancelledLow = Enumerable.Repeat(-1, n).ToArray(),
            Confirmed = Nan(n), Cancelled = Nan(n), Updated = Nan(n), Pending = Nan(n), Failed = Nan(n),
        };

        // The original's confirmed weekly low is the one before the newest: Confirmed fires when it
        // moves on, with Updated the price of the low cancelled since.
        private static void Anchor(TierRun run, int t, List<int> weekly, double[] lows, ref int anchor, ref double replaced, int drawn)
        {
            int now = weekly.Count > 1 ? weekly[^2] : -1;
            if (now > anchor)
            {
                run.Confirmed[t] = lows[now];
                run.Updated[t] = replaced;
                replaced = double.NaN;
            }
            anchor = now;
            run.LastLowAt[t] = anchor;
            // The original's unconfirmed WCL, which its table's unconfirmed week count runs from: the
            // newest low it has drawn, a candidate included, else the newest weekly low (on the metals
            // also a newer low it drew nowhere, <paramref name="drawn"/>). Read from the original's
            // table on the sixteen tuned daily replays (2026-10-28): 53 -> 92% of bars.
            run.HeldAt[t] = Math.Max(drawn, weekly.Count > 0 ? weekly[^1] : -1);
        }

        private static void Publish(TierRun run, SortedDictionary<int, int> drawnOn, Dictionary<int, int> fixedOn)
        {
            foreach (var (i, on) in drawnOn)
            {
                run.Lows.Add(i); run.FoundAt.Add(on);
                run.ConfirmedAt.Add(fixedOn.TryGetValue(i, out int k) ? k : -1);
            }
        }

        /// <summary>
        /// Moves each of a higher tier's lows onto the lowest lower-tier low within
        /// <paramref name="reach"/> bars of it, so a weekly cycle low is always one of the daily
        /// ones (as on CF's charts, where the blue W stack sits on a green D one). A low with no
        /// lower-tier low near it keeps its own bar.
        /// </summary>
        public static List<int> SnapTo(IReadOnlyList<int> lows, IReadOnlyList<int> lowerTier, int reach, ReadOnlySpan<Ohlcv> data)
        {
            var result = new List<int>();
            foreach (int w in lows)
            {
                int best = -1;
                foreach (int d in lowerTier)
                    if (Math.Abs(d - w) <= reach && (best < 0 || data[d].Low < data[best].Low))
                        best = d;
                if (best < 0) best = w;
                if (result.Count == 0 || result[^1] != best) result.Add(best);
            }
            return result;
        }

        /// <summary>
        /// The Schaff Trend Cycle: a MACD (fast EMA minus slow EMA of the close) turned into a
        /// 0–100 stochastic over <paramref name="length"/> bars, smoothed by half, put through the
        /// same stochastic again and smoothed by half again.
        /// </summary>
        public static double[] SchaffTrendCycle(double[] close, int length, int fast, int slow)
        {
            int n = close.Length;
            var macd = new double[n];
            double ef = double.NaN, es = double.NaN, af = 2.0 / (fast + 1), aS = 2.0 / (slow + 1);
            for (int i = 0; i < n; i++)
            {
                ef = double.IsNaN(ef) ? close[i] : ef + af * (close[i] - ef);
                es = double.IsNaN(es) ? close[i] : es + aS * (close[i] - es);
                macd[i] = ef - es;
            }
            var pf = new double[n];
            var outp = new double[n];
            double f1 = double.NaN, p = double.NaN, f2 = double.NaN, q = double.NaN;
            var w1 = new SlidingRange(length); var w2 = new SlidingRange(length);
            for (int i = 0; i < n; i++)
            {
                w1.Add(i, macd[i]);
                var (lo, hi) = w1.Range(i);
                f1 = hi > lo ? (macd[i] - lo) / (hi - lo) * 100 : (double.IsNaN(f1) ? 50 : f1);
                p = double.IsNaN(p) ? f1 : p + 0.5 * (f1 - p);
                pf[i] = p;
                w2.Add(i, p);
                var (lo2, hi2) = w2.Range(i);
                f2 = hi2 > lo2 ? (p - lo2) / (hi2 - lo2) * 100 : (double.IsNaN(f2) ? 50 : f2);
                q = double.IsNaN(q) ? f2 : q + 0.5 * (f2 - q);
                outp[i] = q;
            }
            return outp;
        }

        /// <summary>The lowest and highest of the last <c>length</c> values, kept in two monotonic deques.</summary>
        private sealed class SlidingRange
        {
            private readonly int _length;
            private readonly LinkedList<(int I, double V)> _min = new(), _max = new();
            public SlidingRange(int length) { _length = length; }
            public void Add(int i, double v)
            {
                while (_min.Count > 0 && _min.Last!.Value.V >= v) _min.RemoveLast();
                _min.AddLast((i, v));
                while (_max.Count > 0 && _max.Last!.Value.V <= v) _max.RemoveLast();
                _max.AddLast((i, v));
            }
            public (double Lo, double Hi) Range(int i)
            {
                while (_min.First!.Value.I <= i - _length) _min.RemoveFirst();
                while (_max.First!.Value.I <= i - _length) _max.RemoveFirst();
                return (_min.First.Value.V, _max.First.Value.V);
            }
        }

        // ── Bitcoin's four-year cycle ──────────────────────────────────────────────────

        /// <summary>
        /// Bitcoin halving dates (UTC), the anchors of Loukas's and Camel's four-year cycle. The
        /// 2028 date is an estimate from the average spacing.
        /// </summary>
        public static readonly DateTime[] BtcHalvings =
        {
            new DateTime(2012, 11, 28, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2016,  7,  9, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2020,  5, 11, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024,  4, 19, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2028,  4, 20, 0, 0, 0, DateTimeKind.Utc), // estimated
        };

        /// <summary>
        /// Where a date sits in Bitcoin's four-year cycle, in words, by Loukas's published bands
        /// (accumulation to day 180, markup to 540, distribution to 720, then bear), or null before
        /// the first halving.
        /// </summary>
        public static string? HalvingPhase(DateTime date)
        {
            DateTime? anchor = null;
            foreach (var h in BtcHalvings) if (h <= date) anchor = h;
            if (anchor == null) return null;
            int day = (int)(date - anchor.Value).TotalDays;
            string phase = day < 180 ? "accumulation" : day < 540 ? "markup" : day < 720 ? "distribution" : "bear";
            return $"Day {day} after the {anchor.Value.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture)} halving, the {phase} phase of the four-year cycle";
        }

        public static double[] Nan(int n)
        {
            var a = new double[n];
            Array.Fill(a, double.NaN);
            return a;
        }
    }
}
