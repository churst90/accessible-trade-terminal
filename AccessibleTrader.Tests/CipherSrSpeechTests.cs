using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>What Cipher SR SAYS, measured on the chart Cody narrates every day: Bitstamp BTC/USDT
/// daily, 2011-08-18 to 2026-06-15, with his saved settings.</b>
///
/// <para>
/// <b>"Broken" when the level was only replaced.</b> On the bar a new pivot level took over from
/// a live one, the provider left the zone line empty so the renderer would not draw a slanted
/// stroke from the old level to the new one. The narrator reads "had a value, now empty" as a
/// break, so it said "Cipher SR: Resistance at X broken." about a level price never went near,
/// and then said nothing about the level that replaced it. A Python port measured 81 of 136
/// break announcements over this history as false. The old break tests fed hand-written arrays
/// that never contained a replacement, which is why none of them noticed.
/// </para>
///
/// <para>
/// <b>The design that replaced it.</b> The zone line now carries the level on every bar it is
/// live, including the bar a new one takes over (a strategy or the proximity clause reading
/// that bar used to see no level at all). The provider says what happened in a companion array,
/// <c>"{zone}_replaced"</c>, holding the OLD level on the bar it was replaced and NaN elsewhere.
/// The narrator reads it: a replacement is "New resistance at Y, replacing X"; an empty line is
/// a break and only a break. The renderer reads the same array to lift the pen, so the picture
/// is unchanged.
/// </para>
///
/// <para>
/// Since the Market Cipher SR rebuild later the same day (CipherSrMarketCipherFidelityTests), a
/// level is held until replaced — never broken — and the pivot dots these tests once read are
/// retired; the zone lines and what the narrator says about them are what is measured here.
/// </para>
/// </summary>
public sealed class CipherSrSpeechTests
{
    private static readonly CipherSrProvider Provider = new();

    // ── The real chart ──────────────────────────────────────────────────────────────

    private static List<Ohlcv>? _btc;

    /// <summary>The snapshot, committed as a CSV beside the tests (Fixtures/).</summary>
    internal static List<Ohlcv> BtcDaily()
    {
        if (_btc != null) return _btc;
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "bitstamp_BTC_USDT_1d.csv");
        var bars = new List<Ohlcv>(5500);
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var f = line.Split(',');
            var d = DateTime.SpecifyKind(DateTime.ParseExact(f[0], "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeKind.Utc);
            double P(int k) => double.Parse(f[k], CultureInfo.InvariantCulture);
            bars.Add(new Ohlcv(d, P(1), P(2), P(3), P(4), P(5)));
        }
        Assert.True(bars.Count > 5000, $"the BTC daily fixture has {bars.Count} bars; expected the 5,416-bar snapshot");
        return _btc = bars;
    }

    /// <summary>Cody's saved Cipher SR settings (his __last-session__ workspace, 2026-10-17).</summary>
    private static Dictionary<string, object> CodysSettings() => new()
    {
        ["PivotBars"] = 5.0, ["VolumeLookback"] = 20.0, ["VolumeMultiplier"] = 1.2,
        ["BreakThreshold"] = 0.2, ["AdaptiveBreak"] = 1.0, ["AtrPeriod"] = 14.0, ["AutoScale"] = 1.0,
    };

    internal static Dictionary<string, double[]> Compute(List<Ohlcv> bars, Dictionary<string, object>? pars = null)
    {
        var results = new Dictionary<string, double[]>();
        var buffer = new IndicatorResultBuffer(results, bars.Count);
        Provider.Calculate("CIPHER_SR", System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
            pars ?? CodysSettings(), buffer);
        return results;
    }

    private static double[] Get(Dictionary<string, double[]> r, string key)
        => r.TryGetValue(key, out var a) ? a : Array.Empty<double>();

    // ── The narrator, driven bar by bar ─────────────────────────────────────────────

    private sealed class Capture : ISpeechFeedbackRouter
    {
        public List<string> Spoken { get; } = new();
        public void Speak(string message, bool interrupt = false, SpeechChannel channel = SpeechChannel.Chart) => Spoken.Add(message);
        public void SpeakPoint(WorkspaceState s, WorkspaceState? p, ChartSeries ser, Ohlcv pt, string pfx = "") { }
        public void SpeakProfile(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int bin, string pfx = "") { }
        public void SpeakHeatmap(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int di, int bin, string pfx = "") { }
    }

    private sealed class NoContext : IIndicatorContextAnalyzer
    {
        public void RegisterDefinition(IndicatorContextDefinition def) { }
        public bool HasZoneThresholds(string indicatorCode, string componentName) => false;
        public IndicatorContext? AnalyzeAt(ChartSeries series, string? componentName, int dataIndex) => null;
        public IndicatorContext? Analyze(ChartSeries series, WorkspaceState state) => null;
        public IEnumerable<IndicatorContext> AnalyzeAll(ChartSeries series, WorkspaceState state) => Enumerable.Empty<IndicatorContext>();
    }

    /// <summary>
    /// Every utterance the bar-close narrator made over the whole history, with the bar it was
    /// about. The two zone lines narrate, as on Cody's chart; every array the provider wrote is
    /// handed over, cut to the bars that exist at that moment. Cutting the full-history arrays is
    /// the same as recomputing, because the zone lines and their companions are causal
    /// (IndicatorCausalityTests); the pivot DOTS are not, so they are left out — they would be
    /// announced on bars before they could be known.
    /// </summary>
    private static List<(int Bar, string Text)> NarrateWholeHistory(List<Ohlcv> bars, Dictionary<string, double[]> r)
    {
        var cfg = new SeriesConfig
        {
            Name = "Cipher SR", FriendlyName = "Cipher SR", IndicatorCode = "CIPHER_SR",
            Pane = "Main", IsAutoNarrated = true,
        };
        foreach (var name in new[] { CipherSrProvider.CompResistanceLine, CipherSrProvider.CompSupportLine })
            cfg.Components.Add(new ComponentConfig
            {
                Name = name, DisplayName = name, DisplayType = ComponentDisplayType.Line,
                Role = ComponentRole.Level, IsVisible = true, IsZoneLine = true,
            });

        var keys = r.Keys.ToList();

        WorkspaceState StateAt(int count)
        {
            var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
            foreach (var k in keys) buf.ComponentData[k] = r[k][..count];
            var series = new ChartSeries(cfg, buf);
            return WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(bars.Take(count)),
                ActiveSeries = ImmutableList.Create(series),
                FocusedSeriesId = series.Id,
                CurrentDataIndex = count - 1,
                InitStatus = InitializationStatus.Ready,
                DataStatus = DataStatus.Ready,
                IsSpeechEnabled = true,
            };
        }

        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        var router = new Capture();
        using var svc = new AutoNarrationService(store, bus, router, new NoContext());

        var said = new List<(int, string)>();
        for (int count = 1; count <= bars.Count; count++)
        {
            int before = router.Spoken.Count;
            store.EmitState(StateAt(count));
            bus.Publish(new RedrawEvent());
            // A new bar arriving closes the one before it: count c scans bar c - 2.
            for (int m = before; m < router.Spoken.Count; m++) said.Add((count - 2, router.Spoken[m]));
        }
        return said;
    }

    private static List<(int Bar, string Text)>? _btcNarration;
    private static List<(int Bar, string Text)> BtcNarration()
        => _btcNarration ??= NarrateWholeHistory(BtcDaily(), Compute(BtcDaily()));

    private static readonly Regex BrokenClause = new(@"(Resistance|Support) at ([0-9]+(?:\.[0-9]+)?) broken", RegexOptions.Compiled);
    private static readonly Regex NewClause = new(@"New (resistance|support) at ([0-9]+(?:\.[0-9]+)?), replacing ([0-9]+(?:\.[0-9]+)?)", RegexOptions.Compiled);

    /// <summary>
    /// <b>Every "broken" on the real chart is a close through the level it names.</b>
    ///
    /// <para>
    /// Judged from the price, not from the provider's arrays: the clause names a level, the bar
    /// it was said on has a close, and a resistance is broken only by a close above it (a support
    /// by a close below it). A replaced level's bar has no such close, which is what made the
    /// false announcements measurable. Before the fix, most of this history's "broken"s failed it.
    /// </para>
    /// </summary>
    [Fact]
    public void OnRealBtcDaily_EveryBrokenAnnouncementIsACloseThroughTheLevelItNames()
    {
        var bars = BtcDaily();
        var r = Compute(bars);
        var res = Get(r, CipherSrProvider.CompResistanceLine);
        var sup = Get(r, CipherSrProvider.CompSupportLine);
        var said = BtcNarration();

        int broken = 0;
        var false_ = new List<string>();
        var misnamed = new List<string>();
        foreach (var (bar, text) in said)
            foreach (Match m in BrokenClause.Matches(text))
            {
                broken++;
                string price = m.Groups[2].Value;
                double close = bars[bar].Close;
                // Which line it was about: the one carrying that price on the bar before.
                bool wasRes = bar > 0 && !double.IsNaN(res[bar - 1]) && SpeechPriceFormatter.FormatPrice(res[bar - 1]) == price;
                bool wasSup = bar > 0 && !double.IsNaN(sup[bar - 1]) && SpeechPriceFormatter.FormatPrice(sup[bar - 1]) == price;
                bool through = (wasRes && close > res[bar - 1]) || (wasSup && close < sup[bar - 1]);
                if (!through)
                    false_.Add($"{bars[bar].Date:yyyy-MM-dd}: \"{m.Value}\" but that bar closed at " +
                               $"{SpeechPriceFormatter.FormatPrice(close)}");
                // And it is named for the side the level was on: a broken resistance is
                // "Resistance", even when price had already closed a little above it without
                // clearing the break threshold the bar before.
                else if ((m.Groups[1].Value == "Resistance") != wasRes)
                    misnamed.Add($"{bars[bar].Date:yyyy-MM-dd}: \"{m.Value}\" about the {(wasRes ? "resistance" : "support")} line");
            }

        Assert.True(misnamed.Count == 0,
            $"{misnamed.Count} of {broken} breaks are named for the wrong side:\n  " + string.Join("\n  ", misnamed.Take(8)));

        Assert.True(false_.Count == 0,
            $"{false_.Count} of {broken} \"broken\" announcements on BTC daily name a level price did not " +
            "close through on that bar — the level was replaced by a newer pivot, not broken. First few:\n  " +
            string.Join("\n  ", false_.Take(8)));

        // No vacuity floor on the count since 2026-10-17: Cipher SR now holds a level through closes
        // beyond it, as Market Cipher SR does, so it is never "broken" and this history says it 0
        // times; price crossing a level is announced as a cross. (Under the break rule it had, 55
        // real breaks were heard here.) The test it guards against still bites: a line that goes
        // empty when a level is replaced is announced as broken without a close through it — and
        // OnRealBtcDaily_EveryReplacementIsAnnouncedAsNewLevelReplacingOld proves the scan runs.
    }

    /// <summary>
    /// <b>A replaced level is announced as a replacement, naming both levels, on the bar the new
    /// one took over.</b> Every replacement the provider records must be heard, and each clause
    /// must name the level the line now carries and the level it carried the bar before.
    /// </summary>
    [Fact]
    public void OnRealBtcDaily_EveryReplacementIsAnnouncedAsNewLevelReplacingOld()
    {
        var bars = BtcDaily();
        var r = Compute(bars);
        var said = BtcNarration();

        int expected = 0;
        var missing = new List<string>();
        foreach (var (line, word) in new[] { (CipherSrProvider.CompResistanceLine, "resistance"), (CipherSrProvider.CompSupportLine, "support") })
        {
            var z = Get(r, line);
            for (int b = 1; b < z.Length - 1; b++)
            {
                // A replacement, read from the price line alone: live before, live after, different.
                if (double.IsNaN(z[b - 1]) || double.IsNaN(z[b]) || z[b] == z[b - 1]) continue;
                expected++;
                string newP = SpeechPriceFormatter.FormatPrice(z[b]);
                string oldP = SpeechPriceFormatter.FormatPrice(z[b - 1]);
                bool heard = said.Any(s => s.Bar == b && NewClause.Matches(s.Text).Any(m =>
                    m.Groups[2].Value == newP && m.Groups[3].Value == oldP));
                if (!heard) missing.Add($"{bars[b].Date:yyyy-MM-dd} {word} {oldP} -> {newP} said: " + string.Join(" / ", said.Where(s => Math.Abs(s.Bar - b) <= 1).Select(s => s.Bar + ":" + s.Text)));
                // And the provider RECORDS it, on that bar, as the old level: the renderer lifts the
                // pen there, and the narrator reads the record first (its fallback, a changed value,
                // only covers bars that closed between two scans).
                var rec = Get(r, line + CipherSrProvider.ReplacedSuffix);
                if (b >= rec.Length || rec[b] != z[b - 1])
                    missing.Add($"{bars[b].Date:yyyy-MM-dd} {word} {oldP} -> {newP}: not recorded in {line}{CipherSrProvider.ReplacedSuffix}");
            }
        }

        Assert.True(expected >= 40,
            $"only {expected} bars on which the zone line steps from one live level to another — on the " +
            "old provider the line went empty for a bar at every replacement, so this fixture would find " +
            "none, and the measured history has about 80.");
        Assert.True(missing.Count == 0,
            $"{missing.Count} of {expected} replacements were not announced as \"New … at Y, replacing X\":\n  " +
            string.Join("\n  ", missing.Take(8)));
    }

    /// <summary>
    /// <b>The zone line has a value on every bar a level is live.</b> At a replacement the new
    /// level is known on that bar, so an empty value there is a lie to anything that reads the
    /// line (a strategy comparing close to it, the proximity clause, the hover readout). An empty
    /// bar between two live ones is allowed only where the old level was broken by a close.
    /// </summary>
    [Fact]
    public void OnRealBtcDaily_TheZoneLineIsNeverEmptyOnTheBarALevelIsReplaced()
    {
        var bars = BtcDaily();
        var r = Compute(bars);
        var gaps = new List<string>();
        foreach (var (line, isRes) in new[] { (CipherSrProvider.CompResistanceLine, true), (CipherSrProvider.CompSupportLine, false) })
        {
            var z = Get(r, line);
            for (int b = 1; b < z.Length; b++)
            {
                if (double.IsNaN(z[b - 1]) || !double.IsNaN(z[b])) continue;
                double c = bars[b].Close;
                bool closedThrough = isRes ? c > z[b - 1] : c < z[b - 1];
                if (!closedThrough)
                    gaps.Add($"{line} empty on {bars[b].Date:yyyy-MM-dd} after {z[b - 1]:F2}, close {c:F2}");
            }
        }
        Assert.True(gaps.Count == 0,
            $"{gaps.Count} bars where a zone line went empty without a close through its level:\n  " +
            string.Join("\n  ", gaps.Take(8)));
    }

    /// <summary>
    /// <b>A break is named for the side the level was born on.</b> A support that price closed a
    /// little under (inside a break threshold) before it went is still a SUPPORT that broke: named
    /// from the close on the bar before the break, 14 of the 55 real breaks on Cody's BTC daily chart
    /// were called the wrong side. Cipher SR no longer breaks levels, so the real history no longer
    /// reaches the case; this is the shape, through the same narrator.
    /// </summary>
    [Fact]
    public void ABreakIsNamedForTheSideTheLevelWasBornOn()
    {
        var t = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        double[] closes = { 110, 110, 110, 104.9, 100, 100, 100 };
        var bars = closes.Select((c, i) => new Ohlcv(t.AddDays(i), c, c + 1, c - 1, c, 1000)).ToList();
        var r = new Dictionary<string, double[]>
        {
            [CipherSrProvider.CompSupportLine] = new[] { 105.0, 105, 105, 105, double.NaN, double.NaN, double.NaN },
            [CipherSrProvider.CompResistanceLine] = Enumerable.Repeat(double.NaN, closes.Length).ToArray(),
        };
        var said = NarrateWholeHistory(bars, r);

        Assert.Contains(said, s => s.Text.Contains("Support at 105.00 broken", StringComparison.Ordinal));
        Assert.DoesNotContain(said, s => s.Text.Contains("Resistance at 105.00 broken", StringComparison.Ordinal));
    }

    // ── Settings ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>The warm-up Cipher SR declares covers the warm-up its levels have.</b> The levels taken
    /// on the last bars of a long history are the levels taken from only the declared window of
    /// bars before them. (Until 2026-10-17 Auto-Scale made a pivot window depend on a bar's position
    /// in the history and the declared window was 40 bars against a real 375.)
    /// </summary>
    [Fact]
    public void TheDeclaredWarmUpReproducesTheLongHistorysLevels()
    {
        var all = BtcDaily().TakeLast(1500).ToList();
        var pars = CodysSettings();
        int window = Provider.GetStabilityWindow("CIPHER_SR", pars);
        const int Compare = 200;
        var full = Compute(all, pars);
        var tail = all.Skip(all.Count - (window + Compare)).ToList();
        var part = Compute(tail, pars);
        int offset = all.Count - tail.Count;

        var diffs = new List<string>();
        int levelsTaken = 0;
        foreach (var name in new[] { CipherSrProvider.CompResistanceLine, CipherSrProvider.CompSupportLine })
        {
            var a = Get(full, name); var b = Get(part, name);
            for (int i = tail.Count - Compare; i < tail.Count; i++)
            {
                // A level taken on this bar: a value different from the bar before.
                bool takenFull = !double.IsNaN(a[offset + i]) && a[offset + i] != a[offset + i - 1];
                bool takenPart = !double.IsNaN(b[i]) && b[i] != b[i - 1];
                if (takenFull) levelsTaken++;
                if (takenFull != takenPart || (takenFull && a[offset + i] != b[i]))
                    diffs.Add($"{name} {tail[i].Date:yyyy-MM-dd}: full history {a[offset + i]:F2}, last {tail.Count} bars {b[i]:F2}");
            }
        }
        Assert.True(levelsTaken > 5, $"only {levelsTaken} levels taken in the compared bars");
        Assert.True(diffs.Count == 0,
            $"GetStabilityWindow said {window} bars, but {diffs.Count} levels taken in the last {Compare} bars differ " +
            "when computed from only that many bars:\n  " + string.Join("\n  ", diffs.Take(6)));
    }

    /// <summary>
    /// <b>Every setting moves something.</b> A knob that moves nothing is a lie the dialog tells:
    /// Pivot Bars did nothing while Auto-Scale was on, which was Cody's setting. Each declared
    /// parameter, set away from its default, changes some component on an intraday chart.
    /// </summary>
    [Fact]
    public void EveryDeclaredSettingChangesTheOutput()
    {
        var bars = CipherSrMarketCipherFidelityTests.Csv("coinbase_BTC_USD_15m_2022-01-20_31.csv");
        var meta = Provider.GetIndicators().Single(m => m.Code == "CIPHER_SR");
        var baseline = Compute(bars, new Dictionary<string, object>());
        var dead = new List<string>();
        foreach (var p in meta.Parameters)
        {
            double def = Convert.ToDouble(p.DefaultValue, CultureInfo.InvariantCulture);
            double other = def + 2;
            var moved = Compute(bars, new Dictionary<string, object> { [p.Name] = other });
            bool differs = baseline.Keys.Any(k => moved.TryGetValue(k, out var m)
                && baseline[k].Zip(m).Any(z => !(double.IsNaN(z.First) && double.IsNaN(z.Second)) && z.First != z.Second));
            if (!differs) dead.Add($"{p.Name} ({def} -> {other})");
        }
        Assert.True(meta.Parameters.Count >= 3, "Cipher SR declares fewer settings than Market Cipher SR's three");
        Assert.True(dead.Count == 0, "settings that change nothing: " + string.Join(", ", dead));
    }

    /// <summary>
    /// <b>The detail sentence names the levels the zone lines carry, as the most recent ones, with
    /// their age</b> — not "nearest", which they are not: the nearest level above price is often an
    /// older one.
    /// </summary>
    [Fact]
    public void TheDetailSentenceNamesTheHeldLevelsAndTheirAge()
    {
        var bars = BtcDaily();
        var r = Compute(bars);
        int last = bars.Count - 1;
        string fact = Provider.GetDetailFact("CIPHER_SR",
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars), r, last, CodysSettings());

        Assert.Contains($"Most recent resistance: {SpeechPriceFormatter.FormatPrice(Get(r, CipherSrProvider.CompResistanceLine)[last])}, held", fact);
        Assert.Contains($"Most recent support: {SpeechPriceFormatter.FormatPrice(Get(r, CipherSrProvider.CompSupportLine)[last])}, held", fact);
        Assert.DoesNotContain("Nearest", fact, StringComparison.OrdinalIgnoreCase);
    }
}
