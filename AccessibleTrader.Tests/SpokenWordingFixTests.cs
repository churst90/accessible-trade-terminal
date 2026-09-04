using System.Collections.Immutable;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Four things the terminal said that were wrong as ENGLISH, not as arithmetic.
    ///
    /// <para>
    /// A number that is merely imprecise can be worked around; a sentence that asserts the
    /// opposite of the truth cannot. These four were each spoken confidently and each said
    /// something untrue: a volume of nothing on a bar that traded, another bar's liquidity
    /// presented as this bar's, a pane named twice under two names, and a doubled word.
    /// </para>
    /// </summary>
    public class SpokenWordingFixTests
    {
        // ── Fractional volume is not zero ──────────────────────────────────────

        [Theory]
        [InlineData(0.35)]
        [InlineData(0.4)]
        [InlineData(0.0034)]
        public void ASubUnitCryptoVolume_IsNotSpokenAsZero(double volume)
        {
            // FormatVolume's sub-1,000 branch was ToString("F0"), written for share and contract
            // counts. On a spot BTC pair a candle carrying 0.35 BTC spoke "Volume 0" — a bar that
            // plainly traded, reported as having no volume at all. Crypto is the common case in
            // this app, so the band that read worst was the one used most.
            string msg = CandleSummary(volume);

            // The old reading was the literal sentence "Volume 0." — a zero and a full stop.
            // "Volume 0.35." is not that, so the pattern has to anchor on what FOLLOWS the zero.
            Assert.DoesNotMatch(@"Volume 0\.(\s|$)", msg);
            // The significant figures survive: the reading must distinguish 0.35 from 0.0034.
            Assert.Matches(@"Volume 0\.[0-9]+", msg);
        }

        [Fact]
        public void AWholeShareCount_StillReadsWhole()
        {
            // Vacuity guard the other way: padding every equity volume with a fake ".00" is its
            // own noise, and a fix that did that would pass the theory above.
            Assert.Contains("Volume 350.", CandleSummary(350));
            Assert.DoesNotContain("350.00", CandleSummary(350));
        }

        [Fact]
        public void LargeVolumesAreUnchanged()
        {
            // The M-suffix and thousands-separator branches were correct and stay correct.
            Assert.Contains("24,350", CandleSummary(24_350));
            Assert.Contains("2.50M", CandleSummary(2_500_000));
        }

        // ── "Bearish Bearish Marubozu" ─────────────────────────────────────────

        [Fact]
        public void ABearishMarubozu_IsNotAnnouncedTwice()
        {
            // The caller prefixes the trend word, and ClassifyCandleType ALSO returned the
            // asymmetric label "Bearish Marubozu" on the down side, so the concatenation spoke
            // "Bearish Bearish Marubozu."
            //
            // Still guarded, now against the shared vocabulary: CandlePatternSpeech.DescribeShape
            // is the ONE place that decides who says the direction, and it declines to prefix a
            // name that already carries one. The lowercase 'm' is that vocabulary's spelling —
            // identical to the ear, and identical to what the live bar-close announcement says.
            string msg = CandleSummary(volume: 1000, open: 110, close: 100, high: 110.2, low: 99.8);

            Assert.Contains("Bearish marubozu", msg);
            Assert.DoesNotContain("Bearish Bearish", msg);
        }

        [Fact]
        public void ABullishMarubozu_StillNamesTheShape()
        {
            // Vacuity guard: deleting the Marubozu label entirely would satisfy the test above.
            string msg = CandleSummary(volume: 1000, open: 100, close: 110, high: 110.2, low: 99.8);

            Assert.Contains("Bullish marubozu", msg);
        }

        // ── The heatmap does not pass off another bar's book as this one's ─────

        [Fact]
        public void WhenTheBookComesFromAnotherBar_TheReadingSaysSo()
        {
            // Order-book snapshots are sparse, so the caller resolves the nearest bar that has
            // one. The time label used to be that RESOLVED bar's — so standing on a historical
            // bar the user heard the live snapshot's time and liquidity with nothing to indicate
            // the data was not from where they were standing.
            var (state, series) = HeatmapState();

            string msg = new SpeechFormatter().FormatHeatmapFeedback(
                state, isXMove: true, isYMove: false, series,
                dataIndex: 2, binIndex: -1, prefixMessage: "", cursorDataIndex: 0);

            // The cursor's own time leads — it is the one thing the user cannot cross-check.
            Assert.Contains(LocalTime(0), msg);
            // And the borrowed snapshot is named rather than impersonating the cursor's bar.
            Assert.Contains("no book here", msg);
            Assert.Contains(LocalTime(2), msg);
        }

        [Fact]
        public void WhenTheBookIsTheCursorsOwnBar_NoCaveatIsAdded()
        {
            // Vacuity guard: a caveat on every reading would be noise, and would make the test
            // above pass no matter what the code did.
            var (state, series) = HeatmapState();

            string msg = new SpeechFormatter().FormatHeatmapFeedback(
                state, isXMove: true, isYMove: false, series,
                dataIndex: 2, binIndex: -1, prefixMessage: "", cursorDataIndex: 2);

            Assert.Contains(LocalTime(2), msg);
            Assert.DoesNotContain("no book here", msg);
        }

        [Fact]
        public void TheCaveatSurvivesTimestampsBeingTurnedOff()
        {
            // SpeakTimestamps off asks for less chatter. It does not ask to be told about
            // another bar's liquidity as though it were this one's, so the caveat is not gated
            // on it — only the leading time label is.
            var (state, series) = HeatmapState();

            string msg = new SpeechFormatter().FormatHeatmapFeedback(
                state with { SpeakTimestamps = false }, isXMove: true, isYMove: false, series,
                dataIndex: 2, binIndex: -1, prefixMessage: "", cursorDataIndex: 0);

            Assert.Contains("no book here", msg);
        }

        // ── Fixtures ───────────────────────────────────────────────────────────

        /// <summary>
        /// Runs the candle-summary path (Series interaction context on the "Candles" series),
        /// which is the one that speaks trend, pattern and volume in one utterance.
        /// </summary>
        private static string CandleSummary(double volume, double open = 100, double close = 105,
                                            double high = 110, double low = 90)
        {
            var cfg = new SeriesConfig { Id = "Candles", Name = "Candles", IndicatorCode = "OHLCV", Pane = "Main" };
            cfg.Components.Add(new ComponentConfig { Name = "Close", DisplayName = "Close", IsVisible = true });
            var buf = new SeriesDataBuffer { SeriesId = "Candles" };
            buf.ComponentData["Close"] = new[] { close };
            var series = new ChartSeries(cfg, buf);

            var bar = new Ohlcv(new DateTime(2026, 4, 23, 9, 30, 0, DateTimeKind.Utc),
                                open, high, low, close, volume);

            var state = WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(new List<Ohlcv> { bar }),
                CurrentDataIndex = 0,
                ActiveSeries = ImmutableList.Create(series),
                FocusedSeriesId = series.Id,
                FocusedComponentIndex = 0,
                SpeakTimestamps = false,
                LastInteractionContext = InteractionContext.Series,
            };

            return new SpeechFormatter().FormatPointFeedback(
                state, isXMove: true, isYMove: false, series, bar, prefixMessage: "");
        }

        /// <summary>
        /// Three bars at 09:30, 09:31 and 09:32 UTC, with a book snapshot on the LAST one only —
        /// the shape that produces the borrowed-snapshot reading.
        /// </summary>
        private static (WorkspaceState State, ChartSeries Series) HeatmapState()
        {
            var start = new DateTime(2026, 4, 23, 9, 30, 0, DateTimeKind.Utc);
            var bars = Enumerable.Range(0, 3)
                .Select(i => new Ohlcv(start.AddMinutes(i), 100, 110, 90, 105, 1000))
                .ToList();

            var cfg = new SeriesConfig { Id = "book", Name = "Order Book", IndicatorCode = "HEATMAP", Pane = "Main" };
            cfg.Components.Add(new ComponentConfig { Name = "Liquidity", DisplayName = "Liquidity", IsVisible = true });
            var buf = new SeriesDataBuffer { SeriesId = "book" };
            buf.HeatmapData = new List<List<ProfileBin>?>
            {
                null,
                null,
                new() { Bin(100, 101, 500, isPoc: true) },
            };
            var series = new ChartSeries(cfg, buf);

            var state = WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(bars),
                CurrentDataIndex = 0,
                ActiveSeries = ImmutableList.Create(series),
                FocusedSeriesId = series.Id,
                FocusedBinIndex = -1,
                SpeakTimestamps = true,
            };

            return (state, series);
        }

        /// <summary>
        /// The expected spoken time for heatmap bar <paramref name="barIndex"/>, in the box's own
        /// zone. Derived through the BCL rather than through SpeechTimeFormatter: asking
        /// production what it would print and asserting it printed that is not a test.
        /// </summary>
        private static string LocalTime(int barIndex)
        {
            var utc = new DateTime(2026, 4, 23, 9, 30, 0, DateTimeKind.Utc).AddMinutes(barIndex);
            return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local)
                .ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static ProfileBin Bin(double lo, double hi, double volume, bool isPoc) => new()
        {
            PriceLow = lo,
            PriceHigh = hi,
            TotalVolume = volume,
            TpoPeriodCount = 1,
            IsPOC = isPoc,
            IsValueArea = true,
        };
    }
}
