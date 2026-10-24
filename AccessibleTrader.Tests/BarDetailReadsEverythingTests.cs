using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Alt+Shift+D reads EVERYTHING at the bar, in one announcement — Cody, 2026-10-17: "alt shift
    /// d doesn't seem to read the entire ladder either, it reads the current chart formation, the
    /// current focused series and that's it".
    ///
    /// <para>
    /// The manual promised the candle's open, high, low, close and volume, every active
    /// indicator's reading and every signal event on the bar; <see cref="BarDetailService"/> read
    /// the focused series alone (for the candles not even the four prices) plus the formations.
    /// "The ladder" is the TradingView status line (ChartStatusLine): every visible indicator's
    /// reading in every pane, top to bottom — and Cody's rule is that nothing it prints may be
    /// reachable only by eye. These pin the order (candle, every series in the status line's
    /// order whichever has focus, formations), what is left out (hidden), what is not (muted), and
    /// that a Heikin-Ashi chart reads the candle as drawn.
    /// </para>
    /// </summary>
    public class BarDetailReadsEverythingTests
    {
        // ── The order ──────────────────────────────────────────────────────

        /// <summary>
        /// Cody, 2026-10-22: "It should read all series/components ... from top to bottom starting
        /// with candles otherwise the reading order is changed every time you navigate to a
        /// different series". Until then the focused series was read first, in a form of its own.
        /// </summary>
        [Fact]
        public void TheCandleComesFirst_ThenEverySeriesInPaneOrder_ThenTheFormations_WhicheverHasFocus()
        {
            // Declared OUT of pane order on purpose: the oscillator pane's series is declared
            // before the price pane's EMA, and RSI's pane after it. The renderer draws Main first
            // and the other panes in first-appearance order, so the ladder must read the EMA
            // (Main), then "Osc" (its pane is first among the others), then RSI.
            string? first = null;
            foreach (var focus in new[] { "rsi", "osc", "ema", CoreSeriesIds.Candles })
            {
                var state = Chart(focused: focus, Candles(), Volume(), Osc(), Price(), Ema(), Rsi());
                string said = Announce(state, withFormation: true);

                int candle    = IndexOf(said, "Open 110.00, high 121.00, low 107.00, close 119.00, volume 4,321.");
                int shape     = IndexOf(said, "Body ");
                int ema       = IndexOf(said, "EMA 20: 115.50.");
                int osc       = IndexOf(said, "Osc 9: Fast 12.50, Slow -3.25, Buy.");
                int rsi       = IndexOf(said, "RSI 14: 64.25.");
                int formation = IndexOf(said, "formation here");

                Assert.Equal(0, candle - AfterTheDateAt(said));
                Assert.True(candle < shape, $"the four prices before the shape: {said}");
                Assert.True(shape < ema, $"the candle before the price pane's overlay: {said}");
                Assert.True(ema < osc, $"the price pane before the indicator panes: {said}");
                Assert.True(osc < rsi, $"the panes top to bottom, focused or not ({focus}): {said}");
                Assert.True(rsi < formation, $"the formations last: {said}");

                // Not just the order: the same words, whichever series has focus.
                first ??= said;
                Assert.Equal(first, said);
            }
        }

        /// <summary>A hidden series the user walked to is still answered for, after the ladder.</summary>
        [Fact]
        public void AFocusedHiddenSeriesIsReadAfterTheLadder()
        {
            var rsi = Rsi();
            rsi.IsVisible = false;
            string said = Announce(Chart(focused: "rsi", Candles(), Ema(), rsi), withFormation: true);

            Assert.True(IndexOf(said, "EMA 20: 115.50.") < IndexOf(said, "RSI 64.25"), said);
            Assert.True(IndexOf(said, "RSI 64.25") < IndexOf(said, "formation here"), said);
            // Not focused, hidden is hidden: it is not on the chart.
            var hiddenRsi = Rsi();
            hiddenRsi.IsVisible = false;
            Assert.DoesNotContain("RSI", Announce(Chart(focused: "ema", Candles(), Ema(), hiddenRsi)));
        }

        [Fact]
        public void AMarkerIsNamedOnlyOnTheBarItFires()
        {
            // "Buy" fires at the bar, "Sell" does not: a mark is there or it is not, and naming a
            // mark that is not there would announce a signal that never happened.
            string said = Announce(Chart(focused: "rsi", Candles(), Volume(), Osc(), Rsi()));

            Assert.Contains("Buy", said);
            Assert.DoesNotContain("Sell", said);
        }

        [Fact]
        public void WithTheCandlesFocused_VolumeAndTheCloseLineAreNotReadTwice()
        {
            // Volume rides with the candle and the close line IS the close; the ladder must not
            // repeat either as a series of its own.
            string said = Announce(Chart(focused: CoreSeriesIds.Candles, Candles(), Volume(), Price(), Ema(), Rsi()));

            Assert.StartsWith("Open 110.00", AfterTheDate(said));
            Assert.Contains("volume 4,321", said);
            Assert.DoesNotContain("Volume:", said);
            Assert.DoesNotContain("Price:", said);
            // ...and every other series is still there.
            Assert.Contains("EMA 20: 115.50.", said);
            Assert.Contains("RSI 14: 64.25.", said);
        }

        // ── What is left out, and what is not ──────────────────────────────

        [Fact]
        public void AHiddenSeriesAndAHiddenComponentAreNotRead()
        {
            // A hidden series in the price pane AND one in a pane of its own: the price pane's
            // list is filtered by the status line's own rule as well, so a hidden overlay alone
            // would not notice the ladder losing its visibility check (the first draft of this
            // test hid only the EMA, and stayed green with the check deleted).
            var ema = Ema();
            ema.IsVisible = false;
            var stoch = Series("stoch", "Stoch 14", "STOCH", "Pane_STOCH",
                ("K", ComponentDisplayType.Oscillator, At3(81.75)));
            stoch.IsVisible = false;
            var osc = Osc();
            osc.Components.Single(c => c.Name == "Slow").IsVisible = false;

            string said = Announce(Chart(focused: "rsi", Candles(), Volume(), ema, stoch, osc, Rsi()));

            Assert.DoesNotContain("EMA", said);
            Assert.DoesNotContain("Stoch", said);
            Assert.DoesNotContain("81.75", said);
            Assert.DoesNotContain("Slow", said);
            // The rest of the series still reads — the hidden component does not take it with it.
            Assert.Contains("Osc 9: 12.50, Buy.", said);
        }

        [Fact]
        public void AMutedSeriesIsStillRead()
        {
            // Muted is about sound, not information: the user silenced its tones, not its value.
            var ema = Ema();
            ema.IsMuted = true;

            string said = Announce(Chart(focused: "rsi", Candles(), Volume(), ema, Rsi()));

            Assert.Contains("EMA 20: 115.50.", said);
        }

        // ── Heikin Ashi ────────────────────────────────────────────────────

        [Fact]
        public void AHeikinAshiChartReadsTheCandleAsDrawn_AndTheCloseLineAsTheRawClose()
        {
            // Two bars, hand-computed (not by ChartMath, which is the thing under test):
            //   bar 0 (100, 110, 90, 104): HA open (100+104)/2 = 102, HA close 404/4 = 101
            //   bar 1 (104, 120, 100, 116): HA open (102+101)/2 = 101.5, HA close 440/4 = 110,
            //                                HA high 120, HA low 100
            var bars = Bars((100, 110, 90, 104), (104, 120, 100, 116));
            var price = Price(new[] { 104d, 116d });
            var state = Chart(focused: CoreSeriesIds.Candles, Candles(2), Volume(new[] { 10d, 20d }), price) with
            {
                Data = bars,
                CurrentDataIndex = 1,
                IsHeikinAshi = true,
            };

            string said = Announce(state);

            Assert.Contains("Open 101.50, high 120.00, low 100.00, close 110.00, volume 20.", said);
            Assert.DoesNotContain("Open 104.00", said);
            Assert.DoesNotContain("close 116.00", said);
            // The close line is drawn from the RAW close, so on a Heikin-Ashi chart it is a
            // different number from the candle's close and is read, not skipped.
            Assert.Contains("Price: 116.00.", said);
        }

        // ── DSS gives its state; Cipher B reads only what is shown (Cody, 2026-10-17) ──

        /// <summary>
        /// "dss narration should give its state": after its two numbers, the DSS says where it is
        /// and what it just did, in words — zone at an extreme, direction, a cross of its trigger —
        /// on the ladder and when focused alike, against the chart's own overbought line.
        /// </summary>
        [Fact]
        public void DssSaysItsStateInWordsAfterItsValues()
        {
            var ladder = Announce(Chart(focused: CoreSeriesIds.Candles, Candles(), Dss()));
            Assert.Contains("DSS Bressert: DSS 85.00, Trigger 82.00, overbought, rising, crossed above trigger.", ladder);

            var focused = Announce(Chart(focused: "dss", Candles(), Dss()));
            Assert.Contains("overbought, rising, crossed above trigger", focused);

            // Its own lines, not a constant: moved to 90, 85 is not overbought.
            var moved = Dss();
            moved.Levels.Add(new LevelConfig { Name = "Overbought", Value = 90 });
            var said = Announce(Chart(focused: CoreSeriesIds.Candles, Candles(), moved));
            Assert.Contains("DSS Bressert: DSS 85.00, Trigger 82.00, rising, crossed above trigger.", said);
        }

        /// <summary>
        /// "cipher b, read only what is not hidden as with series": a real Cipher B, built by the
        /// factory with its own visibility, with a value in EVERY component at the bar — none of its
        /// hidden components is read, focused or on the ladder, and the shown ones are.
        /// </summary>
        [Fact]
        public void CipherBReadsOnlyItsShownComponents()
        {
            var meta = new AccessibleTrader.Core.Services.Indicators.CipherBProvider().GetIndicators()[0];
            var styling = new AccessibleTrader.Core.Services.StylingService(
                new AccessibleTrader.Core.Services.ComponentRoleMapper(),
                new AccessibleTrader.Core.Services.Audio.SonificationProfileProvider(),
                new AccessibleTrader.Core.Services.PaneAssignmentService());
            var made = new AccessibleTrader.Core.Services.IndicatorModelFactory(styling, new MockIndicatorPreferencesService())
                .CreateSeriesFromMetadata(meta, "Cipher B", "Pane_CIPHER_B", new List<(string, string)>(), null);
            var buf = new SeriesDataBuffer { SeriesId = made.Id };
            // The provider computes every part, left out or not.
            foreach (var c in made.Components.Concat(made.Config.ExcludedComponents)) buf.ComponentData[c.Name] = At3(-61.5);
            var cipher = made.WithData(buf);

            // Parts it leaves out by default (anchor waves, polarity, its own dots; hidden until
            // 2026-10-23), and one the user hid with H.
            var hidden = cipher.Config.ExcludedComponents.Select(c => c.DisplayName ?? c.Name).ToList();
            Assert.True(hidden.Count >= 2, "precondition: Cipher B leaves parts out by default (anchor waves, polarity)");
            var hiddenByUser = cipher.Components.Single(c => c.Name == AccessibleTrader.Core.Services.Indicators.CipherBProvider.CompMcbRsi);
            hiddenByUser.IsVisible = false;
            hidden.Add(hiddenByUser.DisplayName!);

            foreach (string said in new[]
                     {
                         Announce(Chart(focused: CoreSeriesIds.Candles, Candles(), cipher)),
                         Announce(Chart(focused: cipher.Id, Candles(), cipher)),
                     })
            {
                foreach (var h in hidden)
                    Assert.DoesNotContain(h, said);
                Assert.Contains("Wave Trend Fast", said);
            }
        }

        /// <summary>
        /// A plot with no value on any bar of the chart (Cipher SR's Range High/Low/Mid and VWAP on
        /// a daily chart, Cody's 2026-10-18 screenshot) is left out of the status line, and the
        /// ladder reads what the status line prints: the series is then a one-plot indicator in
        /// both, its reading unlabelled — "Band: 5.00.", not "Band: Upper 5.00.".
        /// </summary>
        [Fact]
        public void APlotWithNoValueAnywhereIsLeftOutOfTheLadderAsOfTheStatusLine()
        {
            var band = Series("band", "Band", "BAND", "Pane_BAND",
                ("Upper", ComponentDisplayType.Line, At3(5)),
                ("Session", ComponentDisplayType.Line, new[] { double.NaN, double.NaN, double.NaN, double.NaN }));

            string said = Announce(Chart(focused: "rsi", Candles(), band, Rsi()));
            Assert.Contains("Band: 5.00.", said);
            Assert.DoesNotContain("Session", said);

            var theme = DbsiChartHarness.Themes().Current;
            var line = AccessibleTrader.Core.Services.Rendering.ChartStatusLine.SeriesLine(band, 3, theme,
                AccessibleTrader.Core.Services.Rendering.ChartStatusLine.Backing(theme, 245))!;
            Assert.Equal(new[] { ("", "5.00") }, line.Values.Select(v => (v.Label, v.Text)));

            // With a value on one bar the plot is on the chart, and both keep it labelled; the
            // status line keeps its dash where it has none.
            band.Data.ComponentData["Session"] = new[] { 2d, double.NaN, double.NaN, double.NaN };
            Assert.Contains("Band: Upper 5.00.", Announce(Chart(focused: "rsi", Candles(), band, Rsi())));
            line = AccessibleTrader.Core.Services.Rendering.ChartStatusLine.SeriesLine(band, 3, theme,
                AccessibleTrader.Core.Services.Rendering.ChartStatusLine.Backing(theme, 245))!;
            Assert.Equal(new[] { ("Upper", "5.00"), ("Session", "—") }, line.Values.Select(v => (v.Label, v.Text)));
        }

        private static ChartSeries Dss() =>
            Series("dss", "DSS Bressert", AccessibleTrader.Core.Services.Indicators.DssBressertProvider.Code, "Pane_DSS_BRESSERT",
                ("DSS", ComponentDisplayType.Oscillator, new[] { 70d, 75d, 78d, 85d }),
                ("Trigger", ComponentDisplayType.Oscillator, new[] { 72d, 76d, 80d, 82d }));

        // ── Fixtures ───────────────────────────────────────────────────────

        private static int IndexOf(string said, string what)
        {
            int i = said.IndexOf(what, StringComparison.Ordinal);
            Assert.True(i >= 0, $"expected \"{what}\" in: {said}");
            return i;
        }

        private static int AfterTheDateAt(string said) => said.IndexOf(": ", StringComparison.Ordinal) + 2;
        private static string AfterTheDate(string said) => said[(said.IndexOf(": ", StringComparison.Ordinal) + 2)..];

        private static string Announce(WorkspaceState state, bool withFormation = false)
        {
            var bus = new SpyEventBus();
            var patterns = withFormation
                ? new FixedPatterns(new ChartPattern(
                    Kind: ChartPatternKind.Rectangle, State: ChartPatternState.Forming,
                    StartBarIndex: 0, EndBarIndex: 100, KnownAtIndex: 0,
                    TriggerLevel: 125, StartTime: default, EndTime: default,
                    ExpiresAtIndex: 300, SecondaryLevel: 95))
                : null;
            new BarDetailService(bus, patterns).AnnounceDetails(state);
            var ev = bus.Log.OfType<AnnouncementEvent>().LastOrDefault();
            Assert.NotNull(ev);
            Assert.True(ev!.Interrupt);
            return ev.Message;
        }

        private sealed class FixedPatterns : IChartPatternCache
        {
            private readonly IReadOnlyList<ChartPattern> _all;
            public FixedPatterns(params ChartPattern[] all) => _all = all;
            public IReadOnlyList<ChartPattern> For(ChartIdentity identity, IReadOnlyList<Ohlcv>? bars) => _all;
        }

        private static readonly (double O, double H, double L, double C)[] FourBars =
        {
            (100, 110, 95, 105), (105, 115, 100, 112), (112, 118, 108, 110), (110, 121, 107, 119),
        };
        private static readonly double[] FourVolumes = { 1000, 2000, 3000, 4321 };

        private static WorkspaceState Chart(string focused, params ChartSeries[] series) =>
            WorkspaceState.Initial with
            {
                Data = Bars(FourBars),
                CurrentDataIndex = 3,
                ActiveSeries = ImmutableList.Create(series),
                FocusedSeriesId = focused,
                PrimarySeriesId = CoreSeriesIds.Candles,
            };

        private static TimeSeriesBuffer<Ohlcv> Bars(params (double O, double H, double L, double C)[] bars)
        {
            var t0 = new DateTime(2026, 10, 14);
            var list = new List<Ohlcv>();
            for (int i = 0; i < bars.Length; i++)
            {
                double vol = bars.Length == FourVolumes.Length ? FourVolumes[i] : (i + 1) * 10;
                list.Add(new Ohlcv(t0.AddDays(i), bars[i].O, bars[i].H, bars[i].L, bars[i].C, vol));
            }
            return new TimeSeriesBuffer<Ohlcv>(list);
        }

        private static ChartSeries Series(string id, string name, string code, string pane,
            params (string Name, ComponentDisplayType Type, double[] Values)[] comps)
        {
            var cfg = new SeriesConfig { Id = id, Name = name, FriendlyName = name, IndicatorCode = code, Pane = pane };
            var buf = new SeriesDataBuffer { SeriesId = id };
            foreach (var (n, t, v) in comps)
            {
                cfg.Components.Add(new ComponentConfig
                {
                    Name = n, DisplayName = n, DisplayType = t, IsVisible = true,
                    Role = code == "VOLUME" ? ComponentRole.Volume : ComponentRole.None,
                });
                buf.ComponentData[n] = v;
            }
            return new ChartSeries(cfg, buf);
        }

        private static double[] At3(double v) => new[] { double.NaN, double.NaN, double.NaN, v };

        private static ChartSeries Candles(int bars = 4) =>
            Series(CoreSeriesIds.Candles, "Candles", "CANDLES", "Main",
                ("Body", ComponentDisplayType.Candle, new double[bars]));

        private static ChartSeries Volume(double[]? values = null) =>
            Series(CoreSeriesIds.Volume, "Volume", "VOLUME", "Volume",
                ("Volume", ComponentDisplayType.Histogram, values ?? FourVolumes));

        private static ChartSeries Price(double[]? values = null) =>
            Series(CoreSeriesIds.Price, "Price", "PRICE", "Main",
                ("Close", ComponentDisplayType.Line, values ?? new[] { 105d, 112d, 110d, 119d }));

        private static ChartSeries Ema() =>
            Series("ema", "EMA 20", "EMA", "Main", ("EMA", ComponentDisplayType.Line, At3(115.5)));

        private static ChartSeries Rsi() =>
            Series("rsi", "RSI 14", "RSI", "Pane_RSI", ("RSI", ComponentDisplayType.Oscillator, At3(64.25)));

        /// <summary>A two-line oscillator with two marks: "Buy" fires at bar 3, "Sell" does not.</summary>
        private static ChartSeries Osc() =>
            Series("osc", "Osc 9", "OSC", "Pane_OSC",
                ("Fast", ComponentDisplayType.Line, At3(12.5)),
                ("Slow", ComponentDisplayType.Line, At3(-3.25)),
                ("Buy",  ComponentDisplayType.TriangleUp,   At3(1)),
                ("Sell", ComponentDisplayType.TriangleDown, new[] { 1d, double.NaN, double.NaN, double.NaN }));
    }
}
