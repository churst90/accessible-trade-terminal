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
    /// <see cref="WeeklyEveryTwoDaily"/> picks the weekly lows by
    /// <see cref="CipherCEngine.WeeklyFromTwoDaily"/>, the rule the original follows on the dollar,
    /// the Dow, the S&amp;P and the Nasdaq; <see cref="WeeklyNextDaily"/> by
    /// <see cref="CipherCEngine.WeeklyFromNextDaily"/>, its rule on Gold, Silver and the miners; the
    /// others keep <see cref="CipherCEngine.WeeklyFromDaily"/>.
    /// </summary>
    public sealed record CyclePreset(
        string Key, string Name,
        double DailyMin, double DailyMax,
        double WeeklyMin, double WeeklyMax,
        double YearlyMin, double YearlyMax,
        bool CalendarDays, string Source, double CountDailyMin = 0, bool WeeklyEveryTwoDaily = false,
        bool WeeklyNextDaily = false)
    {
        /// <summary>The shortest daily cycle the detector counts with.</summary>
        public double CountedDailyMin => CountDailyMin > 0 ? CountDailyMin : DailyMin;
    }

    /// <summary>
    /// The arithmetic behind <see cref="CipherCProvider"/>: the asset presets, how many bars a day
    /// is on this chart, and the cycle-low detector run once per tier.
    ///
    /// <para>
    /// <b>What the detector is.</b> Camel Finance's CF Cycle Trading Indicator is closed source. Its
    /// settings screen shows "overbought 75, oversold 10", "fast/slow EMA adjustment" and a "cycle
    /// length adjustment factor" — the inputs of a Schaff Trend Cycle (a MACD put through a double
    /// stochastic), with its lengths scaled from the asset's cycle range. This detector is that
    /// inference, fitted to the 25 SPX daily cycle lows CF itself labelled on Camel's videos
    /// (2020–2022 and 2024–2025): it finds 18 of the 25 within two bars (72%). Held out, on CF's
    /// own labels it was never fitted to, it finds 8 of 13 within four days (gold 3 of 7, Dow 3 of
    /// 4, Bitcoin 2 of 2); those labels were read off video frames and carry ±1–3 days themselves.
    /// The misses are mostly half-cycle lows CF marks and this does not.
    /// </para>
    ///
    /// <para>
    /// <b>The rule.</b> After the Schaff line has been at or under the oversold level, its first
    /// cross up through the overbought level CONFIRMS a cycle low: the lowest swing low since the
    /// last confirmed low's minimum gap (60% of the shortest cycle). The low must still be the
    /// lowest bar up to the confirming bar. A confirmed low is never taken back: the first close
    /// under it is a FAILED cycle, at any gap. A bar under a low still holding (pending), before
    /// the bar it is due, CANCELS that pending low (CF's "cancelled" state).
    /// </para>
    ///
    /// <para>
    /// <b>The hold.</b> The low the cross finds is not confirmed on that bar: it must hold for half
    /// the shortest cycle, rounded down (<see cref="HoldFraction"/>; 25 bars on a 51-bar cycle), counted from the low itself or from the last
    /// confirmed low plus the shortest cycle less two bars (<see cref="HoldFromBeforeMin"/>),
    /// whichever is later. A lower low while it holds takes its place and starts the hold again.
    /// This is the original's own timing, read bar by bar from it in TradingView's bar replay
    /// (2026-10-22): on the Dow (36-bar shortest cycle) every confirmation came 18 bars after the
    /// later of the low and the last low plus 34; on GDX (22) 11 bars after the later of the low and
    /// the last low plus 20, all 27 cycles from October 2023 to October 2026. Where the two agree
    /// on the low, Cipher C now confirms it on the same bar the original does: 13 of the original's
    /// 18 Dow lows from 2023 and 16 of its 27 GDX lows. The rest are lows the two pick differently.
    /// Confirmation therefore comes at least half a shortest cycle after the low: 18 bars on the
    /// Dow, where it used to be a median of eight. Read on daily charts only. The yearly tier, and
    /// the weekly tier on a weekly chart, use the same rule unverified.
    /// </para>
    ///
    /// <para>
    /// <b>The search, on the daily tier.</b> Once a confirmed low is there, the daily tier does not
    /// wait for the cross. From the last low plus the shortest cycle less two bars
    /// (<see cref="SearchFromBeforeMin"/>), any close above the bar before's high takes the
    /// lowest low since the cycle's highest high as the low to hold. Read from the same replays:
    /// the original's "unconfirmed" low first appears 21 to 43 bars after the last low on GDX
    /// (shortest 22), and it is seldom the lowest bar since the last low, but the low after the
    /// cycle's top. Simulated against every replayed bar, this rule names the original's candidate
    /// on the bar it appears more often than any other tried (GDX 25 of 38, Dow 21 of 35), but it is
    /// not the rule: the original sometimes names a low on a falling bar, and sometimes a day or two
    /// later than this. On the original's final labels, after a 300-bar warm-up, it lifted the daily
    /// F1 from 0.727 to 0.739 and the tops from 0.795 to 0.815 on eight assets held out from it
    /// (five better, two worse, Bitcoin level). The first low, the yearly tier, and the weekly tier
    /// on a weekly chart still wait for the cross.
    /// </para>
    ///
    /// <para>
    /// <b>The weekly tier.</b> On a daily chart the original does not find weekly lows itself: it
    /// picks them from its confirmed daily lows, each judged on the bar it is confirmed
    /// (<see cref="WeeklyFromDaily"/>). A new daily low is the weekly candidate; it is kept when the
    /// next daily low is at least the shortest weekly cycle less four weeks from the last kept low
    /// (<see cref="KeepBars"/>), dropped if a lower one comes sooner, and a kept low undercut too
    /// soon is dropped with it. After a weekly low lower than the one before, the next daily low
    /// decides a "double". The weekly low it calls confirmed is the kept one before the newest.
    /// Read from the original's labels in TradingView (2026-10-23): on its own daily lows, over
    /// sixteen assets, this rule draws 317 of its weekly lows, 60 it does not and misses 60; on the
    /// spans replayed bar by bar (GDX, Dow, Bitcoin) it draws exactly the original's. On Bitcoin
    /// since May 2022 Cipher C's weekly lows are the original's wherever the daily lows agree. The
    /// four-week margin is fitted (GDX allows 84 to 100 bars, Bitcoin any from 119 to 134), not
    /// read. The original has no weekly-chart mode ("Timeframe Not Optimised"), so on a weekly
    /// chart the weekly tier still finds its own lows with the cross and the hold.
    /// On the dollar, the Dow, the S&amp;P and the Nasdaq the replays logged every weekly label per
    /// bar, and they show a different rule, now used there (<see cref="CyclePreset.WeeklyEveryTwoDaily"/>,
    /// <see cref="WeeklyFromTwoDaily"/>): the first daily low after a new weekly low takes its place
    /// if lower; from the second, a new weekly low goes on the lowest daily low since that is two
    /// shortest daily cycles less two bars each after the last.
    /// On Gold, Silver and the miners (22-day tables) the original judges a candidate at the next
    /// daily low instead (<see cref="CyclePreset.WeeklyNextDaily"/>, <see cref="WeeklyFromNextDaily"/>).
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
        public const int    SearchFromBeforeMin = 2;  // the daily search opens at the last low + shortest cycle − this
        public const double KeepWeeksBelowMin = 4;    // a weekly low is kept this many weeks short of the shortest weekly cycle from the last

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
            new CyclePreset("SPX",    "S&P 500",     36, 44, 22, 31, 3.5, 4.5, false, FromCf, WeeklyEveryTwoDaily: true),
            new CyclePreset("NDQ",    "Nasdaq 100",  36, 44, 22, 31, 4,   4,   false, FromCf, WeeklyEveryTwoDaily: true),
            new CyclePreset("DJI",    "Dow Jones",   36, 44, 22, 31, 4,   4,   false, FromCf, WeeklyEveryTwoDaily: true),
            // Its table says 15-24, but the original counts the dollar from 18 (candle replay, 2021-26,
            // 52 lows): confirmed 9 bars after the low on 39 of 52, a short cycle no sooner than the
            // last low + 25, a first label no sooner than the last low + 17.
            new CyclePreset("DXY",    "Dollar index", 15, 24, 16, 20, 3,  3,   false, FromCf, CountDailyMin: 18, WeeklyEveryTwoDaily: true),
            new CyclePreset("GOLD",   "Gold",        22, 28, 20, 28, 8,   8,   false, FromCf, WeeklyNextDaily: true),
            // Silver's table says 22-28, as Gold's does, but the original counts it from 24 (bar replay
            // on candles, 2021-26, 40 lows): it confirms 12 bars after the low on 33 of 40, a short
            // cycle no sooner than the last low + 34, a first label no sooner than the last low + 23.
            // Gold, from the same table, confirms at 11 on 37 of 44. At 22 none of ours landed on its bar.
            new CyclePreset("SILVER", "Silver",      22, 28, 20, 28, 8,   8,   false, FromCf, CountDailyMin: 24, WeeklyNextDaily: true),
            new CyclePreset("USOIL",  "Crude oil",   36, 44, 16, 20, 3,   3,   false, FromCf),
            new CyclePreset("GDX",    "Gold miners", 22, 28, 22, 26, 8,   8,   false, FromCf, WeeklyNextDaily: true),
            new CyclePreset("GDXJ",   "Junior gold miners", 22, 28, 22, 26, 8, 8, false, FromCf, WeeklyNextDaily: true),
            new CyclePreset("SILJ",   "Junior silver miners", 22, 28, 22, 26, 8, 8, false, FromCf, WeeklyNextDaily: true),
            // Its table says 15-25, but the original counts Ethereum from 16 (candle replay, 2022-26, 75
            // lows): confirmed 8 bars after the low on 59 of 75 (15 and 16 both hold 8), a short cycle
            // no sooner than the last low + 22 (16 - 2 + 8; 15 would give 21), a first label no sooner
            // than the last low + 15.
            new CyclePreset("ETH",    "Ethereum",    15, 25, 7,  10, 4,   4,   true,  FromCf, CountDailyMin: 16),
            // CF's own BTC table (reviewer videos, 7-9 Aug 2025) reads 22-42 weeks; Camel says 28-32 on
            // his 2026 streams. The indicator is what Cipher C copies, so the table wins.
            // Its table says 54-66 days, but the original counts Bitcoin as if the shortest cycle were
            // 48 (bar replay on candles, 2022-26, 24 lows): its first label comes no sooner than the
            // last low + 47 (dMin - 1), it confirms 24 bars after the low on 19 of 24, and a short
            // cycle's confirmation waits until the last low + 70 (1.5 dMin - 2), on 4 of 5. At 54 every
            // one of ours came 3 bars late; at 48, 20 of 22 land on its bar and 22 of 25 lows match (19).
            new CyclePreset("BTC",    "Bitcoin",     54, 66, 22, 42, 4,   4,   true,  FromCf, CountDailyMin: 48),
            // TradingView counts stock bars, so these CF daily ranges are treated as trading days;
            // that interpretation is an inference from the stock charts, not an explicit CF note.
            // Its table says 40-90 for both, but the original counts Coinbase and MicroStrategy from 51
            // (candle replays, 2021-26 and 2019-26): confirmed 25 bars after the low (half of 51,
            // rounded down; 50 would hold 25 too), a short cycle no sooner than the last low + 74
            // (51 - 2 + 25; 50 would give 73), on both. At 40 every one of ours came 5 bars early.
            new CyclePreset("COIN",   "Coinbase",    40, 90, 24, 42, 4,   4,   false, FromCf, CountDailyMin: 51),
            new CyclePreset("MSTR",   "MicroStrategy", 40, 90, 24, 42, 4, 4, false, FromCf, CountDailyMin: 51),
            // Its table says 36-44, but the original counts MARA from 38 (candle replay, 2022-26, 23 lows):
            // confirmed 19 bars after the low on 19, a short cycle no sooner than the last low + 55
            // (38 - 2 + 19; 55, 55, 56), a first label no sooner than + 37. At 36 every one came a bar early.
            new CyclePreset("MARA",   "Marathon Digital", 36, 44, 22, 26, 4, 4, false, FromCf, CountDailyMin: 38),
            // RIOT shares MARA's table but is counted from 39 (candle replay, 2022-26, 23 lows): confirmed
            // 19 bars after the low on 19 (floor(39/2); 38 holds 19 too), a short cycle no sooner than the
            // last low + 56 (39 - 2 + 19; 56, 56, 56), a first label no sooner than + 38 (39 - 1).
            new CyclePreset("RIOT",   "Riot Platforms", 36, 44, 22, 26, 4, 4, false, FromCf, CountDailyMin: 39),
        };

        /// <summary>Other names each preset's asset trades under: index, ETF, future, CFD, spot pair.</summary>
        private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["SPX"] = "SPX", ["SPY"] = "SPX", ["ES"] = "SPX", ["US500"] = "SPX", ["VOO"] = "SPX", ["IVV"] = "SPX", ["GSPC"] = "SPX", ["SPX500"] = "SPX",
            ["NDQ"] = "NDQ", ["NDX"] = "NDQ", ["QQQ"] = "NDQ", ["NQ"] = "NDQ", ["US100"] = "NDQ", ["NAS100"] = "NDQ", ["IXIC"] = "NDQ",
            ["DJI"] = "DJI", ["DIA"] = "DJI", ["YM"] = "DJI", ["US30"] = "DJI", ["DJIA"] = "DJI",
            ["DXY"] = "DXY", ["UUP"] = "DXY", ["DX"] = "DXY",
            ["GOLD"] = "GOLD", ["XAU"] = "GOLD", ["GLD"] = "GOLD", ["GC"] = "GOLD", ["IAU"] = "GOLD",
            ["SILVER"] = "SILVER", ["XAG"] = "SILVER", ["SLV"] = "SILVER", ["SI"] = "SILVER",
            ["USOIL"] = "USOIL", ["WTI"] = "USOIL", ["CL"] = "USOIL", ["USO"] = "USOIL", ["OIL"] = "USOIL",
            ["GDX"] = "GDX", ["GDXJ"] = "GDXJ", ["SILJ"] = "SILJ",
            ["ETH"] = "ETH", ["ETHEREUM"] = "ETH", ["ETHA"] = "ETH", ["ETHE"] = "ETH",
            ["BTC"] = "BTC", ["XBT"] = "BTC", ["BITCOIN"] = "BTC", ["IBIT"] = "BTC", ["FBTC"] = "BTC", ["GBTC"] = "BTC",
            ["COIN"] = "COIN", ["MSTR"] = "MSTR", ["MARA"] = "MARA", ["RIOT"] = "RIOT",
        };

        private static readonly string[] QuoteSuffixes = { "USDT", "USDC", "USD", "EUR", "GBP" };

        public static CyclePreset Preset(string key) => Presets.First(p => p.Key == key);

        /// <summary>
        /// The preset for a chart symbol ("BTC/USD", "TVC:GOLD", "XAUUSD", "SPY", "^GSPC"), or null
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

            if (spacingDays < 20.0 / 24)
            {
                if (weekend) return new BarClock(86400.0 / spacing, true);
                // A stock's intraday bars: how many make one session, from the dates themselves.
                var perDate = new Dictionary<DateTime, int>();
                for (int i = from - 1; i < n; i++)
                {
                    var d = data[i].Date.Date;
                    perDate[d] = perDate.TryGetValue(d, out var c) ? c + 1 : 1;
                }
                var counts = perDate.Values.OrderBy(v => v).ToList();
                return new BarClock(Math.Max(1, counts[counts.Count / 2]), false);
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
            /// <summary>Per bar: the low found and holding at the end of that bar (the pending low), or -1.</summary>
            public int[] HeldAt = Array.Empty<int>();
            /// <summary>Per bar: the pending low a cancellation on that bar dropped, or -1.</summary>
            public int[] CancelledLow = Array.Empty<int>();
        }

        /// <summary>
        /// Runs the detector for one tier. Everything it publishes per bar is decided from that bar
        /// and earlier ones; only <see cref="TierRun.Lows"/> (the final list) knows the future.
        /// <paramref name="searchOnTurn"/>: the daily tier's search (see "The search" in the class
        /// remarks); off, every low waits for the cycle line's cross.
        /// </summary>
        public static TierRun Detect(ReadOnlySpan<Ohlcv> data, TierBars tier, double overbought, double oversold,
                                     bool searchOnTurn = false)
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
            int searchFrom = Math.Max(1, (int)Math.Round(tier.MinBars) - SearchFromBeforeMin);

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
                    // bar the held low is confirmed whatever the bar's low (the original, 8 of the
                    // 13 such bars in its replays on five daily charts). The original drops the
                    // undercut low and names the next one a bar or more later (328 of 332 pending
                    // lows it dropped, none replaced on the same bar), so the search drops it too
                    // and looks again from the next bar; the cycle line's cross, which may not come
                    // again, moves it to the undercutting bar.
                    if (low[t] < low[held] && t - (last >= 0 ? Math.Max(held, last + holdFrom) : held) < hold)
                    {
                        run.Cancelled[t] = replaced = low[held];
                        run.CancelledLow[t] = held;
                        cancelledSince = true;
                        held = searchOnTurn && last >= 0 ? -1 : t;
                    }
                }
                else if (searchOnTurn && last >= 0 && t - last >= searchFrom && close[t] > high[t - 1])
                {
                    // The lowest low since the cycle's highest high (the latest of equal bars), if
                    // the bar has turned up from it.
                    int top = last + 1;
                    for (int i = last + 2; i <= t; i++) if (high[i] >= high[top]) top = i;
                    int c = top;
                    for (int i = top + 1; i <= t; i++) if (low[i] <= low[c]) c = i;
                    if (c < t) { held = c; found = t; }
                }
                else if ((!searchOnTurn || last < 0) && armed && stc[t - 1] < overbought && stc[t] >= overbought)
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
        /// How far, in bars, a daily low must be from the newest kept weekly low to keep a weekly
        /// low: the shortest weekly cycle less <see cref="KeepWeeksBelowMin"/> weeks. Read from the
        /// original: GDX and the Dow 90 (22 weeks of 5 bars; GDX allows 84 to 100), Bitcoin 126 (22
        /// weeks of 7).
        /// </summary>
        public static double KeepBars(double weeklyMin, double barsPerWeek) =>
            Math.Max(0, weeklyMin - KeepWeeksBelowMin) * barsPerWeek;

        /// <summary>
        /// The weekly tier on a chart with a daily one: the original's rule (see "The weekly tier"
        /// in the class remarks), driven only by the daily run's confirmations, each acted on the
        /// bar it happens. <paramref name="keepBars"/> is how far from the newest kept weekly low a
        /// daily low must be to be kept as the next ((shortest weekly cycle − 4) weeks of bars);
        /// <paramref name="dailyMin"/> the daily shortest cycle in bars. <see cref="TierRun.Lows"/>
        /// are the weekly lows drawn at the end (the live candidate among them);
        /// <see cref="TierRun.ConfirmedAt"/> the bar each was kept, or -1 for one not kept yet;
        /// <see cref="TierRun.FoundAt"/> the bar each was drawn. <see cref="TierRun.LastLowAt"/> is
        /// the original's confirmed weekly low: the kept weekly low before the newest, which its
        /// week count runs from. Confirmed fires when that moves on, Cancelled on the bar a kept
        /// weekly low is dropped (carrying its price), and Updated on the next confirmation after
        /// one was (the price of the one dropped last).
        /// </summary>
        public static TierRun WeeklyFromDaily(TierRun daily, ReadOnlySpan<Ohlcv> data, double keepBars, double dailyMin)
        {
            int n = data.Length;
            var run = new TierRun
            {
                LastLowAt = Enumerable.Repeat(-1, n).ToArray(), HeldAt = Enumerable.Repeat(-1, n).ToArray(),
                ClosedUnder = Enumerable.Repeat(-1, n).ToArray(), CancelledLow = Enumerable.Repeat(-1, n).ToArray(),
                Confirmed = Nan(n), Cancelled = Nan(n), Updated = Nan(n), Pending = Nan(n), Failed = Nan(n),
            };
            if (daily.LastLowAt.Length != n) return run;

            var kept = new List<int>();                       // kept weekly lows, newest last
            var drawnOn = new SortedDictionary<int, int>();   // drawn weekly low -> bar drawn
            var keptOn = new Dictionary<int, int>();          // kept weekly low -> bar kept
            var lows = new double[n];
            for (int i = 0; i < n; i++) lows[i] = data[i].Low;
            double Low(int i) => lows[i];
            int Gap(int d) => kept.Count > 0 ? d - kept[^1] : int.MaxValue;
            char phase = 'L';
            int cand = -1, chk = -1, dprev = -1, anchor = -1;
            double replaced = double.NaN;
            for (int t = 0; t < n; t++)
            {
                if (!double.IsNaN(daily.Confirmed[t]) && daily.LastLowAt[t] >= 0)
                {
                    int d = daily.LastLowAt[t];
                    void Keep(int i) { kept.Add(i); keptOn[i] = t; drawnOn.TryAdd(i, t); }
                    void Drop()
                    {
                        int i = kept[^1];
                        kept.RemoveAt(kept.Count - 1); drawnOn.Remove(i); keptOn.Remove(i);
                        run.Cancelled[t] = replaced = Low(i);
                        run.CancelledLow[t] = i;
                    }
                    switch (phase)
                    {
                        case 'L':
                            // A new daily low is the weekly candidate, drawn at once.
                            cand = d; drawnOn[d] = t; phase = 'C';
                            break;
                        case 'C' when Low(d) < Low(cand):
                            // A lower one takes its place; a kept low it undercuts too soon was not
                            // the weekly low. Far enough from the kept low, it is kept; if not, it
                            // is not drawn at all.
                            drawnOn.Remove(cand); cand = d;
                            while (kept.Count > 1 && Low(d) < Low(kept[^1]) && Gap(d) < keepBars) Drop();
                            if (Gap(d) >= keepBars) Keep(d);
                            phase = 'S';
                            break;
                        case 'C':
                            // The checkpoint: far enough from the kept low, the candidate is kept;
                            // otherwise it is removed — unless it undercut a kept low too soon,
                            // which is dropped and the candidate judged again.
                            while (kept.Count > 1 && Gap(d) < keepBars && Low(cand) < Low(kept[^1])) Drop();
                            if (Gap(d) >= keepBars)
                            {
                                bool lower = kept.Count > 0 && Low(cand) < Low(kept[^1]);
                                dprev = kept.Count > 0 ? kept[^1] : -1;
                                Keep(cand); chk = d;
                                phase = lower ? 'D' : 'L';
                            }
                            else { drawnOn.Remove(cand); phase = 'L'; }
                            break;
                        case 'D':
                            // After a lower weekly low the next daily low decides the "double": a
                            // lower one is a new candidate; otherwise the checkpoint low is kept as
                            // a second weekly low if it is under the kept low before.
                            if (Low(d) < Low(chk)) { cand = d; drawnOn[d] = t; phase = 'C'; }
                            else
                            {
                                if (Low(chk) < Low(dprev)) Keep(chk);
                                phase = 'L';
                            }
                            break;
                        default:   // 'S', after a replacement
                            // A lower low a full daily cycle after the replacement is judged as a
                            // checkpoint after a new weekly low.
                            if (cand >= 0 && Low(d) < Low(cand) && d - cand >= dailyMin && kept.Count > 1)
                            {
                                chk = d; dprev = kept[^1] == cand ? kept[^2] : kept[^1];
                                phase = 'D';
                            }
                            else phase = 'L';
                            break;
                    }
                }
                int now = kept.Count > 1 ? kept[^2] : -1;
                if (now > anchor)
                {
                    run.Confirmed[t] = Low(now);
                    run.Updated[t] = replaced;
                    replaced = double.NaN;
                }
                anchor = now;
                run.LastLowAt[t] = anchor;
            }
            foreach (var (i, on) in drawnOn)
            {
                run.Lows.Add(i); run.FoundAt.Add(on);
                run.ConfirmedAt.Add(keptOn.TryGetValue(i, out int k) ? k : -1);
            }
            return run;
        }

        /// <summary>
        /// The weekly tier on a chart with a daily one, as the original picks it on the dollar, the
        /// Dow, the S&amp;P and the Nasdaq: by counting its confirmed daily lows after the newest
        /// weekly low, each acted on the bar it is confirmed. The first daily low after a new weekly
        /// low takes its place if it is lower (and is never moved itself); otherwise nothing happens.
        /// From the second on, a new weekly low goes on the lowest of the daily lows since the newest
        /// that is at least <paramref name="gapBars"/> after it (two shortest daily cycles less two
        /// bars each: 2 × (shortest − 2)); until one is, it waits for the next. The weekly low it
        /// calls confirmed (<see cref="TierRun.LastLowAt"/>, which its week count runs from) is the
        /// one before the newest, moved on at each daily confirmation except one that draws a new
        /// weekly low on its own bar. Read from the original's labels and table (weeks since the
        /// confirmed and the unconfirmed weekly low) on the dollar's candle replay, 2022-26. Fed the
        /// original's own daily lows it draws 22 of its 22 weekly lows there; with the same
        /// 2 × (shortest − 2), 10 of 11 on the Dow, 10 of 11 on the Nasdaq and 11 of 13 on the S&amp;P
        /// (at the original's default count there, 32). Oil, Gold, Silver, GDX and Bitcoin do not
        /// fit it at this gap and keep <see cref="WeeklyFromDaily"/>. <see cref="TierRun.ConfirmedAt"/>
        /// is the bar a weekly low can no longer move (the next daily confirmation, or at once for one
        /// that moved or was drawn on an earlier low), -1 while it still can.
        /// </summary>
        public static TierRun WeeklyFromTwoDaily(TierRun daily, ReadOnlySpan<Ohlcv> data, double gapBars)
        {
            int n = data.Length;
            var run = new TierRun
            {
                LastLowAt = Enumerable.Repeat(-1, n).ToArray(), HeldAt = Enumerable.Repeat(-1, n).ToArray(),
                ClosedUnder = Enumerable.Repeat(-1, n).ToArray(), CancelledLow = Enumerable.Repeat(-1, n).ToArray(),
                Confirmed = Nan(n), Cancelled = Nan(n), Updated = Nan(n), Pending = Nan(n), Failed = Nan(n),
            };
            if (daily.LastLowAt.Length != n) return run;

            var weekly = new List<int>();                     // weekly lows, newest last
            var drawnOn = new SortedDictionary<int, int>();   // weekly low -> bar drawn
            var fixedOn = new Dictionary<int, int>();         // weekly low -> bar it can no longer move
            var after = new List<int>();                      // daily lows confirmed after the newest
            bool movable = false;                             // the newest was drawn on its own bar
            int anchor = -1;
            for (int t = 0; t < n; t++)
            {
                if (!double.IsNaN(daily.Confirmed[t]) && daily.LastLowAt[t] >= 0)
                {
                    int d = daily.LastLowAt[t];
                    bool drewOwn = false;
                    if (weekly.Count == 0)
                    {
                        weekly.Add(d); drawnOn[d] = t; movable = drewOwn = true;
                    }
                    else if (d > weekly[^1])
                    {
                        int u = weekly[^1];
                        after.Add(d);
                        if (after.Count == 1 && movable)
                        {
                            // The first daily low after a new weekly low: lower, it takes its place.
                            movable = false;
                            if (data[d].Low < data[u].Low)
                            {
                                drawnOn.Remove(u); weekly[^1] = d; drawnOn[d] = t; fixedOn[d] = t;
                                after.Clear();
                            }
                            else fixedOn[u] = t;
                        }
                        else if (after.Count >= 2)
                        {
                            int best = -1;
                            foreach (int x in after)
                                if (x - u >= gapBars && (best < 0 || data[x].Low < data[best].Low)) best = x;
                            if (best >= 0)
                            {
                                weekly.Add(best); drawnOn[best] = t;
                                after.RemoveAll(x => x <= best);
                                movable = drewOwn = after.Count == 0;
                                if (!movable) fixedOn[best] = t;
                            }
                        }
                    }
                    // The confirmed weekly low is the one before the newest, except on the bar a
                    // new one is drawn on its own daily low.
                    int now = weekly.Count > 1 ? weekly[^2] : -1;
                    if (!drewOwn && now > anchor) { run.Confirmed[t] = data[now].Low; anchor = now; }
                }
                run.LastLowAt[t] = anchor;
            }
            foreach (var (i, on) in drawnOn)
            {
                run.Lows.Add(i); run.FoundAt.Add(on);
                run.ConfirmedAt.Add(fixedOn.TryGetValue(i, out int k) ? k : -1);
            }
            return run;
        }

        /// <summary>
        /// The weekly tier on a chart with a daily one, as the original picks it on Gold, Silver
        /// and the gold and silver miners (shortest daily cycle 22 in their tables), each daily
        /// confirmation acted on the bar it happens. A daily low confirmed after the newest weekly
        /// low U is drawn at once as a candidate F and judged at the next daily confirmation N. If
        /// N is lower than F, N is a new weekly low when at least <paramref name="lowerGapBars"/>
        /// after U, else it takes U's place if lower than U. If N is not lower, F is a new weekly
        /// low when at least <paramref name="gapBars"/> after U, else it takes U's place if lower.
        /// The confirmation after a judged N draws no candidate when N undercut F or F was more than
        /// <paramref name="farBars"/> after U; the one after that does. The weekly low it calls
        /// confirmed (<see cref="TierRun.LastLowAt"/>) is the one before the newest. Read from the
        /// candle replays of GDXJ (2022-26) and checked on the others, fed the original's own daily
        /// lows: 40 of its 47 weekly lows with 6 more (GDXJ 10/10, GDX 8/9, Gold 7/8, Silver 8/9,
        /// SILJ 7/11 held out), against 43 with 51 more for <see cref="WeeklyFromDaily"/>. The three
        /// gaps are fitted, as 4.5, 4 and 6 of the table's shortest daily cycle (99, 88, 132 bars at
        /// 22; the fit allows 100-104, 85-88 and 130-140; Silver, counted from 24, fits the table's 22).
        /// </summary>
        public static TierRun WeeklyFromNextDaily(TierRun daily, ReadOnlySpan<Ohlcv> data, double gapBars, double lowerGapBars, double farBars)
        {
            int n = data.Length;
            var run = new TierRun
            {
                LastLowAt = Enumerable.Repeat(-1, n).ToArray(), HeldAt = Enumerable.Repeat(-1, n).ToArray(),
                ClosedUnder = Enumerable.Repeat(-1, n).ToArray(), CancelledLow = Enumerable.Repeat(-1, n).ToArray(),
                Confirmed = Nan(n), Cancelled = Nan(n), Updated = Nan(n), Pending = Nan(n), Failed = Nan(n),
            };
            if (daily.LastLowAt.Length != n) return run;

            var weekly = new List<int>();                     // weekly lows, newest last
            var drawnOn = new SortedDictionary<int, int>();   // drawn weekly low -> bar drawn
            var fixedOn = new Dictionary<int, int>();         // weekly low -> bar it became one
            int cand = -1, anchor = -1;
            bool skip = true;
            double replaced = double.NaN;
            var lows = new double[n];
            for (int i = 0; i < n; i++) lows[i] = data[i].Low;
            double Low(int i) => lows[i];
            for (int t = 0; t < n; t++)
            {
                if (!double.IsNaN(daily.Confirmed[t]) && daily.LastLowAt[t] >= 0)
                {
                    int d = daily.LastLowAt[t];
                    void Add(int i) { weekly.Add(i); drawnOn.TryAdd(i, t); fixedOn[i] = t; }
                    void Replace(int i)
                    {
                        int u = weekly[^1];
                        drawnOn.Remove(u); fixedOn.Remove(u);
                        run.Cancelled[t] = replaced = Low(u); run.CancelledLow[t] = u;
                        weekly[^1] = i; drawnOn.TryAdd(i, t); fixedOn[i] = t;
                    }
                    if (weekly.Count == 0) Add(d);
                    else if (d > weekly[^1])
                    {
                        if (cand >= 0)
                        {
                            int u = weekly[^1];
                            bool under = Low(d) < Low(cand);
                            if (under)
                            {
                                drawnOn.Remove(cand);
                                if (d - u >= lowerGapBars) Add(d);
                                else if (Low(d) < Low(u)) Replace(d);
                            }
                            else
                            {
                                if (cand - u >= gapBars) Add(cand);
                                else { drawnOn.Remove(cand); if (Low(cand) < Low(u)) Replace(cand); }
                            }
                            skip = under || cand - u > farBars;
                            cand = -1;
                        }
                        else if (skip) skip = false;
                        else { cand = d; drawnOn[d] = t; }
                    }
                }
                int now = weekly.Count > 1 ? weekly[^2] : -1;
                if (now > anchor)
                {
                    run.Confirmed[t] = Low(now);
                    run.Updated[t] = replaced;
                    replaced = double.NaN;
                }
                anchor = now;
                run.LastLowAt[t] = anchor;
            }
            foreach (var (i, on) in drawnOn)
            {
                run.Lows.Add(i); run.FoundAt.Add(on);
                run.ConfirmedAt.Add(fixedOn.TryGetValue(i, out int k) ? k : -1);
            }
            return run;
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
