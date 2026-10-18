using System.Collections.Immutable;
using System.Globalization;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;
using Xunit.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cody's BTCUSDT 1D chart, restored from his own session and drawn by the real renderer
/// (2026-10-18).</b> Market Structure, Cipher SR, Cipher B and Cipher A exactly as he saved them
/// (<c>Fixtures/codys_btc_1d_series_2026-10-18.json</c>, cut read-only from his
/// <c>__last-session__</c> file), through <see cref="WorkspaceInitializer.MigrateSeriesConfig"/>
/// and <see cref="SeriesManagementService.MaterializeSaved"/>, computed on the Bitstamp BTC daily
/// fixture and painted on a Heikin-Ashi chart as his was.
///
/// <para>Two of his screenshot's complaints are pinned here: about 13 value tags stacked on the
/// price axis, some ~100 px from their own lines, because every one of Cipher A's eight ribbon
/// lines put a tag there (now only EMA 5 does, by default); and Cipher SR's status line reading
/// "Range High — Range Low — Range Mid — VWAP —" on a daily chart, where those four have no value
/// on any bar (now left out, and back on an intraday chart).</para>
///
/// <para>Set <c>AT_CODY_PNG_DIR</c> to write the frames as PNGs; set <c>AT_BTC_CSV</c> to draw them
/// on a longer history in the fixture's format (the committed fixture ends 2026-06-15).</para>
/// </summary>
public sealed class CodysBtcChartTests
{
    private readonly ITestOutputHelper _out;
    public CodysBtcChartTests(ITestOutputHelper output) => _out = output;

    private static readonly IIndicatorProvider[] Providers =
        { new SwingStructureProvider(), new CipherSrProvider(), new CipherBProvider(), new CipherAProvider() };

    private static List<IndicatorMetadata> AllMeta() => Providers.SelectMany(p => p.GetIndicators()).ToList();

    private static IndicatorEngine Engine() => new(
        new IndicatorService(Providers.ToList(), NullLogger<IndicatorService>.Instance), new CustomIndicatorRegistry(), Providers.ToList());

    private static readonly IndicatorModelFactory Factory = new(DbsiChartHarness.Styling(), new MockIndicatorPreferencesService());

    /// <summary>His four saved series, as on disk.</summary>
    internal static List<SeriesConfig> Saved()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "codys_btc_1d_series_2026-10-18.json");
        var root = JObject.Parse(File.ReadAllText(path));
        return root["Series"]!.Select(t => t.ToObject<SeriesConfig>()!).ToList();
    }

    /// <summary>One saved series through the restore path a resumed session takes.</summary>
    internal static ChartSeries Restore(SeriesConfig saved)
    {
        var meta = AllMeta();
        WorkspaceInitializer.MigrateSeriesConfig(saved, meta);
        return SeriesManagementService.MaterializeSaved(saved, meta.Single(m => m.Code == saved.IndicatorCode), Factory,
            Array.Empty<IReadOnlyDictionary<string, object>>(), Engine(), new MockIndicatorPreferencesService());
    }

    /// <summary>The series' data as its provider computes it on <paramref name="bars"/>.</summary>
    internal static ChartSeries Computed(ChartSeries s, List<Ohlcv> bars)
    {
        var provider = Providers.Single(p => p.GetIndicators().Any(m => m.Code == s.IndicatorCode));
        var results = new Dictionary<string, double[]>();
        provider.Calculate(s.IndicatorCode, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
            s.BuildParameterMap(), new IndicatorResultBuffer(results, bars.Count));
        s.Data.ComponentData.Clear();
        foreach (var kv in results) s.Data.ComponentData[kv.Key] = kv.Value;
        return s;
    }

    private static List<ChartSeries> HisChart(List<Ohlcv> bars, Action<ChartSeries>? edit = null)
    {
        var list = new List<ChartSeries> { DbsiChartHarness.Candles() };
        foreach (var saved in Saved())
        {
            var s = Restore(saved);
            edit?.Invoke(s);
            list.Add(Computed(s, bars));
        }
        return list;
    }

    private static List<Ohlcv> Bars()
    {
        string? path = Environment.GetEnvironmentVariable("AT_BTC_CSV");
        if (string.IsNullOrEmpty(path)) return CipherSrSpeechTests.BtcDaily();
        var bars = new List<Ohlcv>();
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var f = line.Split(',');
            var d = DateTime.SpecifyKind(DateTime.ParseExact(f[0], "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeKind.Utc);
            double P(int k) => double.Parse(f[k], CultureInfo.InvariantCulture);
            bars.Add(new Ohlcv(d, P(1), P(2), P(3), P(4), P(5)));
        }
        return bars;
    }

    private const int W = 1400, H = 900, Shown = 135;

    private sealed record Drawn(SKBitmap Bmp, ChartRenderer Renderer);

    /// <summary>His chart's last <see cref="Shown"/> bars, as his viewport, Heikin-Ashi, through the real renderer.</summary>
    private static Drawn Render(List<Ohlcv> bars, IReadOnlyList<ChartSeries> series, string? dump,
        IReadOnlyList<ChartPattern>? formations = null)
    {
        int start = bars.Count - Shown;
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = series.ToImmutableList(),
            ViewportStartIndex = start,
            ViewportLength = Shown,
            IsHeikinAshi = true,
        };
        var ranges = new ViewportRangeCalculator().Calculate(state).PaneRanges;
        var layout = new PaneLayoutService();
        var renderer = new ChartRenderer(DbsiChartHarness.Themes(), DbsiChartHarness.Styling(), layout,
            NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>());
        var bmp = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(bmp))
            renderer.Render(canvas, W, H, bars, series, cursorIndex: bars.Count - 1, viewportStart: start, viewportLength: Shown,
                viewportRange: ranges["Main"], paneRanges: ranges, isHeikinAshi: true, density: 1f, rightMarginBars: 0,
                formations: formations, identity: new ChartIdentity("Spot", "Bitstamp", "BTCUSDT", "1d"));
        if (dump != null && Environment.GetEnvironmentVariable("AT_CODY_PNG_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            using var img = SKImage.FromBitmap(bmp);
            using var png = img.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(dir, dump + ".png"), png.ToArray());
        }
        return new Drawn(bmp, renderer);
    }

    /// <summary>How far each placed price-axis tag sits from its own value, in pixels.</summary>
    private static List<(string Name, double Value, float Off)> Drift(ChartRenderer r)
    {
        var (axis, min, max, log, labels) = r.LastMainScale;
        return labels.Select(l => ($"{l.Series}/{l.Component}", l.Value,
            Math.Abs(l.Y - ChartMath.MapY(l.Value, axis.Top, axis.Bottom, min, max, log)))).ToList();
    }

    private static bool IsRibbon(string component) => component.StartsWith("EMA ", StringComparison.Ordinal);

    // ── Fix 1: the ribbon's tags ────────────────────────────────────────────────────

    [Fact]
    public void Only_EMA_5_of_Cipher_A_s_ribbon_puts_a_tag_on_his_price_axis()
    {
        var bars = Bars();
        var after = Render(bars, HisChart(bars), dump: "codys_btc_1d_after");
        // The same chart with every ribbon line's label chosen on — what his chart drew before.
        var before = Render(bars, HisChart(bars, s =>
        {
            if (s.IndicatorCode == "CIPHER_A")
                foreach (var c in s.Components.Where(c => IsRibbon(c.Name))) c.UserShowAxisLabel = true;
        }), dump: "codys_btc_1d_before");

        var b = Drift(before.Renderer);
        var a = Drift(after.Renderer);
        foreach (var (name, v, off) in b) _out.WriteLine($"before {name,-40} {v,12:F2} {off,6:F1} px");
        foreach (var (name, v, off) in a) _out.WriteLine($"after  {name,-40} {v,12:F2} {off,6:F1} px");

        var ribbonBefore = b.Where(t => IsRibbon(t.Name.Split('/')[1])).Select(t => t.Name.Split('/')[1]).ToList();
        var ribbonAfter = a.Where(t => IsRibbon(t.Name.Split('/')[1])).Select(t => t.Name.Split('/')[1]).ToList();
        // The "before" frame really carried the crowd, or the comparison proves nothing.
        Assert.True(ribbonBefore.Count >= 6, $"the old frame placed only {ribbonBefore.Count} ribbon tags: {string.Join(", ", ribbonBefore)}");
        Assert.Equal(new[] { "EMA 5" }, ribbonAfter);
        // Everything else he had on the axis is still there.
        Assert.Equal(b.Where(t => !IsRibbon(t.Name.Split('/')[1])).Select(t => t.Name).OrderBy(n => n),
            a.Where(t => !IsRibbon(t.Name.Split('/')[1])).Select(t => t.Name).OrderBy(n => n));
    }

    [Fact]
    public void His_restored_Cipher_A_takes_the_new_default_and_a_users_choice_is_kept()
    {
        var saved = Saved().Single(s => s.IndicatorCode == "CIPHER_A");
        // The fixture is the old shape: no choice recorded on any line.
        Assert.All(saved.Components, c => Assert.Null(c.UserShowAxisLabel));

        var restored = Restore(saved);
        foreach (var c in restored.Components.Where(c => IsRibbon(c.Name)))
            Assert.True(c.ShowAxisLabelInEffect() == (c.Name == "EMA 5"), $"{c.Name}: {c.ShowAxisLabelInEffect()}");

        // A user who turned EMA 21's label on and EMA 5's off keeps both, across a save and a
        // second restore (the session file is what the next launch reads).
        restored.Components.Single(c => c.Name == "EMA 21").UserShowAxisLabel = true;
        restored.Components.Single(c => c.Name == "EMA 5").UserShowAxisLabel = false;
        var onDisk = Newtonsoft.Json.JsonConvert.DeserializeObject<SeriesConfig>(Newtonsoft.Json.JsonConvert.SerializeObject(restored.Config))!;
        var again = Restore(onDisk);
        Assert.True(again.Components.Single(c => c.Name == "EMA 21").ShowAxisLabelInEffect());
        Assert.False(again.Components.Single(c => c.Name == "EMA 5").ShowAxisLabelInEffect());
        Assert.False(again.Components.Single(c => c.Name == "EMA 11").ShowAxisLabelInEffect());
        // The indicator's own default is rebuilt from metadata, never taken from the file.
        Assert.False(again.Components.Single(c => c.Name == "EMA 21").ShowAxisLabel);
        Assert.True(again.Components.Single(c => c.Name == "EMA 5").ShowAxisLabel);

        var bars = CipherSrSpeechTests.BtcDaily();
        var labels = ChartRenderer.ScaleLabelsFor(new[] { Computed(again, bars) }, bars.Count, DbsiChartHarness.Themes().Current);
        Assert.Equal(new[] { "EMA 21" }, labels.Select(l => l.Component));
    }

    [Fact]
    public void A_new_Cipher_A_labels_only_EMA_5_and_every_other_built_in_line_still_labels()
    {
        var meta = new CipherAProvider().GetIndicators()[0];
        var fresh = Factory.CreateSeriesFromMetadata(meta, "Cipher A", "Main", new List<(string, string)>(), null);
        Assert.Equal(new[] { "EMA 5" }, fresh.Components.Where(c => ChartRenderer.CanHaveAxisLabel(c) && c.ShowAxisLabel).Select(c => c.Name));
        Assert.Equal(8, fresh.Components.Count(ChartRenderer.CanHaveAxisLabel));
        // Nothing else in the fleet opted out: the switch is Cipher A's ribbon's alone today.
        var others = IndicatorProviderFixture.AllProviders().Where(p => p is not CipherAProvider)
            .SelectMany(p => p.GetIndicators()).SelectMany(m => m.Components)
            .Where(c => c.DefaultShowAxisLabel == false).Select(c => c.Name).ToList();
        Assert.True(others.Count == 0, $"unexpected opt-outs: {string.Join(", ", others)}");
    }

    // ── Fix 2: plots with no value on this timeframe ────────────────────────────────

    private static readonly string[] SessionPlots = { "Range High", "Range Low", "Range Mid", "VWAP" };

    private static ChartSeries HisCipherSr(List<Ohlcv> bars) =>
        Computed(Restore(Saved().Single(s => s.IndicatorCode == "CIPHER_SR")), bars);

    private static StatusLine Line(ChartSeries s, int bar)
    {
        var theme = DbsiChartHarness.Themes().Current;
        return ChartStatusLine.SeriesLine(s, bar, theme, ChartStatusLine.Backing(theme, ChartStatusLine.DefaultBackingAlpha))!;
    }

    [Fact]
    public void On_his_daily_chart_Cipher_SR_s_status_line_leaves_out_the_four_session_plots()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var sr = HisCipherSr(bars);
        // Vacuity: they really are empty on a daily chart, and shown.
        foreach (var n in SessionPlots)
        {
            Assert.True(sr.Components.Single(c => c.Name == n).IsVisible, $"{n} is hidden in his save — the test would prove nothing");
            Assert.All(sr.GetComponentData(n), v => Assert.True(double.IsNaN(v)));
        }

        var line = Line(sr, bars.Count - 1);
        Assert.Equal(new[] { "Resistance Zone", "Support Zone" }, line.Values.Select(v => v.Label));
        Assert.All(line.Values, v => Assert.NotEqual("—", v.Text));

        // A plot that has values elsewhere keeps its dash at a bar without one (warm-up), so the
        // readings do not shuffle as the cursor moves.
        var first = Line(sr, 0);
        Assert.Equal(new[] { "Resistance Zone", "Support Zone" }, first.Values.Select(v => v.Label));
        Assert.All(first.Values, v => Assert.Equal("—", v.Text));
    }

    /// <summary>The same series switched to a 15-minute chart (its data recomputed, as a
    /// timeframe change does): the four session plots have values again and are back.</summary>
    [Fact]
    public void The_same_Cipher_SR_switched_to_an_intraday_chart_shows_them_again()
    {
        var daily = CipherSrSpeechTests.BtcDaily();
        var sr = HisCipherSr(daily);
        Assert.DoesNotContain("Range High", Line(sr, daily.Count - 1).Values.Select(v => v.Label));

        var intraday = CipherSrMarketCipherFidelityTests.Csv("coinbase_BTC_USD_15m_2022-01-20_31.csv");
        Computed(sr, intraday);
        var line = Line(sr, intraday.Count - 1);
        Assert.Equal(new[] { "Resistance Zone", "Support Zone" }.Concat(SessionPlots), line.Values.Select(v => v.Label));
        Assert.All(line.Values, v => Assert.NotEqual("—", v.Text));
    }
}
