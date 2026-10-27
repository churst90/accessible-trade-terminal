using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cipher C is a cycle-low indicator after Camel Finance's CF Cycle Trading Indicator
/// (rebuilt 2026-10-21).</b>
///
/// <para>The golden answers are not the C#. They are CF's own labels — the two Bitcoin daily cycle
/// lows CF printed on Camel's 2026 videos — and properties any cycle-low detector of this kind must
/// keep whatever its tuning: a low is the low of its bar and is confirmed later; a cancellation is a
/// close under the low before the minimum gap; a failure is one after it; a count is the gap in days
/// counting both ends. The fit to CF's 25 SPX labels lives in docs/CIPHER_C_DESIGN.md (the SPX data
/// is Yahoo's and is not committed).</para>
/// </summary>
public sealed class CipherCProviderTests
{
    private readonly ITestOutputHelper _out;
    public CipherCProviderTests(ITestOutputHelper output) => _out = output;

    private static readonly CipherCProvider Provider = new();

    private static IndicatorMetadata Meta() => Provider.GetIndicators().Single();

    private static Dictionary<string, double[]> Run(IReadOnlyList<Ohlcv> bars, string? symbol, Dictionary<string, object>? extra = null)
    {
        var p = new Dictionary<string, object>();
        if (symbol != null) p["__symbol"] = symbol;
        foreach (var (k, v) in extra ?? new()) p[k] = v;
        return CipherCProvider.Compute(bars.ToArray(), p);
    }

    private static Dictionary<string, double[]>? _btc;
    private static Dictionary<string, double[]> Btc() => _btc ??= Run(CipherSrSpeechTests.BtcDaily(), "BTC/USD");

    /// <summary>A companion that may not exist: an inactive tier writes no bounds.</summary>
    private static double[] Of(Dictionary<string, double[]> r, string key) => r.TryGetValue(key, out var a) ? a : Array.Empty<double>();

    private static IEnumerable<int> Marked(double[] a) => Enumerable.Range(0, a.Length).Where(i => !double.IsNaN(a[i]));

    private static string? MarkerTextOf(object value)
    {
        var property = value.GetType().GetProperty("MarkerText");
        Assert.NotNull(property);
        return property!.GetValue(value) as string;
    }

    private static void SetMarkerText(object value, string? text)
    {
        var property = value.GetType().GetProperty("MarkerText");
        Assert.NotNull(property);
        property!.SetValue(value, text);
    }

    // ── 1. What it is ─────────────────────────────────────────────────────────────

    /// <summary>
    /// On the candles, with no levels: the old Cipher C was a ±100 oscillator in its own pane, and
    /// CF draws on the price chart.
    /// </summary>
    [Fact]
    public void It_draws_on_the_price_chart_with_no_levels()
    {
        var m = Meta();
        Assert.Equal(CipherCProvider.Code, m.Code);
        Assert.Equal("Cipher C", m.Name);
        Assert.Equal("Main", m.DefaultPane);
        Assert.False(m.IsDeprecated);
        Assert.Empty(Provider.GetDefaultLevels(CipherCProvider.Code));
    }

    [Fact]
    public void CF_marker_metadata_declares_ring_letters_check_boxes_refresh_box_and_count_suffixes()
    {
        var components = Meta().Components.ToDictionary(c => c.Name);
        var expected = new[]
        {
            (Name: CipherCProvider.CompDcl, Shape: "TriangleUpRing", Color: CipherCProvider.DclHex, Text: "D", Size: 3f, Anchor: MarkerAnchor.BelowBar, Sound: "sine_bell"),
            (Name: CipherCProvider.CompWcl, Shape: "TriangleUpRing", Color: CipherCProvider.WclHex, Text: "W", Size: 4.5f, Anchor: MarkerAnchor.BelowBar, Sound: "dual_tone_bell"),
            (Name: CipherCProvider.CompYcl, Shape: "TriangleUpRing", Color: CipherCProvider.YclHex, Text: "Y", Size: 6f, Anchor: MarkerAnchor.BelowBar, Sound: "dual_tone_bell"),
            (Name: CipherCProvider.CompDclConfirmed, Shape: "CheckBox", Color: CipherCProvider.DclHex, Text: (string?)null, Size: 4.5f, Anchor: MarkerAnchor.AboveBar, Sound: "sine_bell"),
            (Name: CipherCProvider.CompWclConfirmed, Shape: "CheckBox", Color: CipherCProvider.DclHex, Text: (string?)null, Size: 5.5f, Anchor: MarkerAnchor.AboveBar, Sound: "dual_tone_bell"),
            (Name: CipherCProvider.CompYclConfirmed, Shape: "CheckBox", Color: CipherCProvider.DclHex, Text: (string?)null, Size: 6.5f, Anchor: MarkerAnchor.AboveBar, Sound: "dual_tone_bell"),
            (Name: CipherCProvider.CompPending, Shape: "RefreshBox", Color: "#78909C", Text: (string?)null, Size: 4f, Anchor: MarkerAnchor.AboveBar, Sound: "triangle_bell"),
        };
        foreach (var row in expected)
        {
            var component = components[row.Name];
            Assert.Equal(row.Shape, component.DisplayType.ToString());
            Assert.Equal(row.Color, component.DefaultColorHex);
            Assert.Equal(row.Text, MarkerTextOf(component));
            Assert.Equal(row.Size, component.DefaultThickness);
            Assert.Equal(row.Anchor, component.DefaultMarkerAnchor);
            Assert.Equal(row.Sound, component.DefaultSoundPatchId);
        }

    }

    /// <summary>
    /// Fifteen components, each holding something to hear, none hidden or muted by the indicator.
    /// Cody, 2026-10-22: "Each component must hold value, simply hiding and muting it isn't what I
    /// want. hiding and muting should be a user defined preferential function." The 10-21 build had
    /// 28, thirteen of them hidden and muted to keep internal numbers off the chart; those numbers
    /// are companions now (ComponentCompanions), said in the speech of the component they describe.
    /// </summary>
    [Fact]
    public void Every_component_is_shown_and_heard_by_default()
    {
        var m = Meta();
        Assert.Equal(new[]
        {
            CipherCProvider.CompSma, CipherCProvider.CompYcl, CipherCProvider.CompWcl, CipherCProvider.CompDcl,
            CipherCProvider.CompDclConfirmed, CipherCProvider.CompWclConfirmed, CipherCProvider.CompYclConfirmed,
            CipherCProvider.CompPending, CipherCProvider.CompCancelled, CipherCProvider.CompFailed,
            CipherCProvider.CompLeftTop, CipherCProvider.CompMidTop, CipherCProvider.CompRightTop,
            CipherCProvider.CompDailyWindow, CipherCProvider.CompWeeklyWindow,
        }, m.Components.Select(c => c.Name));
        Assert.All(m.Components, c =>
        {
            Assert.True(c.IsVisible, $"{c.Name} is hidden");
            Assert.NotEqual(true, c.DefaultIsMuted);
        });

        // A timing window is a count heard on every bar and a tint seen over the window — not a
        // line on the price axis.
        foreach (var name in new[] { CipherCProvider.CompDailyWindow, CipherCProvider.CompWeeklyWindow })
        {
            var w = m.Components.Single(c => c.Name == name);
            Assert.True(w.IsCount);
            Assert.Equal(ComponentDisplayType.Window, w.DisplayType);   // shaded and heard, the count never drawn
            Assert.Equal("cycle arc", w.PathName);                     // its picture, named on landing
            Assert.Equal(BackgroundTint.PriceChart, w.DefaultBackgroundTint);
        }
        Assert.True(m.Components.Single(c => c.Name == CipherCProvider.CompDclConfirmed).IsCount);
        Assert.False(m.Components.Single(c => c.Name == CipherCProvider.CompDcl).IsCount);
    }

    [Fact]
    public void MarkerText_from_metadata_reaches_the_component_config()
    {
        var series = Factory.CreateSeriesFromMetadata(Meta(), "Cipher C", "Main",
            new List<(string Name, string Value)>(), componentOverrides: null);

        Assert.Equal("D", MarkerTextOf(series.Components.Single(c => c.Name == CipherCProvider.CompDcl)));
        Assert.Equal("W", MarkerTextOf(series.Components.Single(c => c.Name == CipherCProvider.CompWcl)));
    }

    [Fact]
    public void MarkerText_survives_the_ComponentConfig_clone()
    {
        var component = new ComponentConfig { Name = "DCL", DisplayType = ComponentDisplayType.ValueLabel };
        SetMarkerText(component, "D");

        Assert.Equal("D", MarkerTextOf(component.Clone()));
    }

    [Fact]
    public void Saved_chart_without_MarkerText_gets_current_metadata_letters_on_load()
    {
        var meta = Meta();
        var saved = new SeriesConfig
        {
            Id = "cc-marker-text", Name = "Cipher C", IndicatorCode = CipherCProvider.Code,
            Pane = "Main", Volume = 1f,
        };
        foreach (string name in new[]
        {
            CipherCProvider.CompDcl, CipherCProvider.CompWcl, CipherCProvider.CompYcl,
        })
            saved.Components.Add(new ComponentConfig { Name = name, DisplayName = name, IsVisible = true, IsEnabled = true, Volume = 1f });

        WorkspaceInitializer.MigrateSeriesConfig(saved, new List<IndicatorMetadata> { meta });
        var loaded = SeriesManagementService.MaterializeSaved(saved, meta, Factory,
            Array.Empty<IReadOnlyDictionary<string, object>>(), Engine(), new MockIndicatorPreferencesService());

        Assert.Equal("D", MarkerTextOf(loaded.Components.Single(c => c.Name == CipherCProvider.CompDcl)));
        Assert.Equal("W", MarkerTextOf(loaded.Components.Single(c => c.Name == CipherCProvider.CompWcl)));
        Assert.Equal("Y", MarkerTextOf(loaded.Components.Single(c => c.Name == CipherCProvider.CompYcl)));
    }

    [Theory]
    [InlineData("CheckBox", "check mark")]
    [InlineData("RefreshBox", "refresh mark")]
    [InlineData("TriangleUpRing", "lettered triangle")]
    public void New_marker_shapes_have_spoken_shape_words(string shape, string words)
    {
        var displayType = Enum.Parse<ComponentDisplayType>(shape);
        Assert.Equal(words, SpeechFormatter.FriendlyTypeName(displayType));
    }

    /// <summary>
    /// A low says its price, the cycle it ended in words ("49 day cycle", never the drawn "49D"),
    /// and how long after it was confirmed — from its companions, not from components of their own.
    /// </summary>
    [Fact]
    public void A_low_says_its_price_its_cycle_and_when_it_was_confirmed()
    {
        var bar = new Ohlcv(new DateTime(2026, 1, 1), 50, 55, 45, 50, 1000);
        var data = new Dictionary<string, double[]>
        {
            [CipherCProvider.CompDcl + CipherCProvider.LengthSuffix] = new[] { 49.0 },
            [CipherCProvider.CompDcl + CipherCProvider.AfterSuffix] = new[] { 9.0 },
        };
        var said = Provider.GetComponentSpeech(CipherCProvider.CompDcl, 45, bar, data, dataIndex: 0);

        Assert.Equal("Daily cycle low, 45.00. A 49 day cycle. Confirmed 9 days later.", said);
        Assert.DoesNotContain("49D", said);
        // The first low on the chart has no cycle before it, and an unconfirmed one no "later".
        Assert.Equal("Daily cycle low, 45.00.", Provider.GetComponentSpeech(CipherCProvider.CompDcl, 45, bar, new Dictionary<string, double[]>(), 0));
    }

    /// <summary>
    /// What is drawn ON a low's bar knows the future (the low is only known once confirmed bars
    /// later) and is declared so, which keeps it out of strategies; every event is decided on its
    /// own bar and is declared causal, so a strategy can use it.
    /// </summary>
    [Theory]
    [InlineData(CipherCProvider.CompYcl, ComponentCausality.Lookahead)]
    [InlineData(CipherCProvider.CompWcl, ComponentCausality.Lookahead)]
    [InlineData(CipherCProvider.CompDcl, ComponentCausality.Lookahead)]
    [InlineData(CipherCProvider.CompLeftTop, ComponentCausality.Lookahead)]
    [InlineData(CipherCProvider.CompMidTop, ComponentCausality.Lookahead)]
    [InlineData(CipherCProvider.CompRightTop, ComponentCausality.Lookahead)]
    [InlineData(CipherCProvider.CompDclConfirmed, ComponentCausality.Causal)]
    [InlineData(CipherCProvider.CompCancelled, ComponentCausality.Causal)]
    [InlineData(CipherCProvider.CompFailed, ComponentCausality.Causal)]
    [InlineData(CipherCProvider.CompYclConfirmed, ComponentCausality.Causal)]
    [InlineData(CipherCProvider.CompPending, ComponentCausality.Causal)]
    [InlineData(CipherCProvider.CompDailyWindow, ComponentCausality.Causal)]
    [InlineData(CipherCProvider.CompWeeklyWindow, ComponentCausality.Causal)]
    public void Each_component_declares_whether_it_knows_the_future(string name, ComponentCausality expected)
    {
        var m = Meta();
        var c = m.Components.Single(x => x.Name == name);
        Assert.Equal(expected, c.Causality ?? m.Causality);
    }

    /// <summary>
    /// The events a decision is made on are causal in fact, not only in declaration: computing on
    /// the first N bars gives the same value on bar N-1 as computing on the whole history. Their
    /// companions too (a confirmation's low and age, a window's tint and stops): what a causal
    /// component SAYS on a bar must not change either.
    /// </summary>
    [Fact]
    public void The_events_on_a_bar_do_not_change_when_later_bars_arrive()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var whole = Btc();
        var m = Meta();
        var causalComponents = m.Components.Where(c => (c.Causality ?? m.Causality) == ComponentCausality.Causal).Select(c => c.Name).ToList();
        // The cycle arc (a window's _path) is a picture, not a reading: it is never said or
        // stopped on, and it is look-ahead by design (The_cycle_arc_is_drawn_between_known_lows).
        var causal = whole.Keys.Where(k => (causalComponents.Contains(k) || ComponentCompanions.IsCompanionOf(k, causalComponents))
            && !k.EndsWith(ComponentCompanions.Path, StringComparison.Ordinal)).ToList();
        Assert.Contains(CipherCProvider.CompDclConfirmed, causal);
        Assert.Contains(CipherCProvider.CompDclConfirmed + CipherCProvider.AgoSuffix, causal);
        Assert.Contains(CipherCProvider.CompDailyWindow + ComponentCompanions.Stops, causal);
        var everCompared = new HashSet<string>();
        // Fixed cuts, plus one just after the last daily confirmation that replaced a cancelled
        // low: since confirmations wait out the hold (2026-10-22), cancellations are rare and no
        // longer fall in the fixed stretches. The weekly tier drops no kept low anywhere on
        // Bitcoin (it picks its lows from the daily ones, 2026-10-23), so its "replaces" never
        // fires here; CipherCWeeklyTests checks it on every cut of a case that drops one.
        var cuts = new List<int> { 3000, 3800, 4500, 5200 };
        int lastReplaced = Marked(whole[CipherCProvider.CompDclConfirmed + CipherCProvider.ReplacedSuffix]).LastOrDefault(-1);
        Assert.True(lastReplaced > 400, "no daily confirmation replaces a cancelled low on Bitcoin");
        cuts.Add(lastReplaced + 1);
        Assert.Empty(Marked(whole[CipherCProvider.CompWclConfirmed + CipherCProvider.ReplacedSuffix]));
        foreach (int cut in cuts)
        {
            var part = Run(bars.Take(cut).ToList(), "BTC/USD");
            foreach (var c in causal)
                for (int i = cut - 400; i < cut; i++)
                {
                    if (!double.IsNaN(whole[c][i]) && whole[c][i] != 0) everCompared.Add(c);
                    Assert.True(Same(part[c][i], whole[c][i]), $"{c} on {bars[i].Date:yyyy-MM-dd}: {part[c][i]} with {cut} bars, {whole[c][i]} with all");
                }
        }
        // A component that never fired in these stretches was not checked at all. (No yearly low is
        // confirmed in them: the 2022 one is confirmed on bar ~4,100, outside every 400-bar stretch.)
        // Nor does the weekly window tint on a daily chart: the daily one's green would be covered.
        var neverChecked = causal.Except(everCompared).Where(k => !k.StartsWith(CipherCProvider.CompYclConfirmed, StringComparison.Ordinal)
            && k != CipherCProvider.CompWeeklyWindow + ComponentCompanions.Tint
            && k != CipherCProvider.CompWclConfirmed + CipherCProvider.ReplacedSuffix).ToList();
        Assert.True(neverChecked.Count == 0, "never compared: " + string.Join(", ", neverChecked));
        static bool Same(double a, double b) => double.IsNaN(a) ? double.IsNaN(b) : Math.Abs(a - b) < 1e-9;
    }

    /// <summary>
    /// <b>The cycle arc is the lowest tier's window's picture (2026-10-22).</b> Cody: "if the arc
    /// is more akin to the chart formation lines and drawings, then leave it as such, just a
    /// drawing". One hump from each low to the next, its trough on the low's bar, every point
    /// under that bar's candle; on the lowest tier's window, never both. Look-ahead, as CF's is: each arc ends at a low known only later,
    /// so a shorter history draws a different arc — which is why the causal test leaves it out.
    /// </summary>
    [Fact]
    public void The_cycle_arc_is_drawn_between_known_lows()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var r = Btc();
        var arc = r[CipherCProvider.CompDailyWindow + ComponentCompanions.Path];
        Assert.All(r[CipherCProvider.CompWeeklyWindow + ComponentCompanions.Path], v => Assert.True(double.IsNaN(v)));

        var lows = Marked(r[CipherCProvider.CompDcl]).ToList();
        Assert.True(lows.Count > 50, $"{lows.Count} daily lows");
        Assert.Equal(lows[0], Marked(arc).First());                 // nothing before the first low
        for (int k = 0; k < lows.Count; k++)
        {
            int l = lows[k];
            Assert.True(arc[l] <= arc[l + 1], $"the arc rises out of the low on {bars[l].Date:yyyy-MM-dd}");
            if (k > 0) Assert.True(arc[l] <= arc[l - 1], $"the arc comes down into the low on {bars[l].Date:yyyy-MM-dd}");
        }
        foreach (int i in Marked(arc))
            Assert.True(arc[i] < bars[i].Low, $"the arc at {arc[i]:F0} is not under the candle on {bars[i].Date:yyyy-MM-dd} (low {bars[i].Low:F0})");

        // Look-ahead: on 3,000 bars the last arc runs on to where the next low is due, and the
        // whole history puts that low elsewhere.
        var part = Run(bars.Take(3000).ToList(), "BTC/USD")[CipherCProvider.CompDailyWindow + ComponentCompanions.Path];
        Assert.Contains(Enumerable.Range(2600, 400), i => Math.Abs(part[i] - arc[i]) > 1e-6);

        // A weekly chart counts its daily cycle in bars, so the arc is on its daily window too.
        var weekly = Run(Weekly(bars), "BTC/USD");
        Assert.Contains(weekly[CipherCProvider.CompDailyWindow + ComponentCompanions.Path], v => !double.IsNaN(v));
        Assert.All(weekly[CipherCProvider.CompWeeklyWindow + ComponentCompanions.Path], v => Assert.True(double.IsNaN(v)));
    }

    // ── 2. CF's own Bitcoin labels ──────────────────────────────────────────────────

    /// <summary>
    /// CF labelled two Bitcoin daily cycle lows on Camel's 2026 videos: 2026-02-04 and 2026-03-29
    /// (read off video frames; ±1–3 days). Neither was used to fit the detector.
    /// </summary>
    [Theory]
    [InlineData("2026-02-04")]
    [InlineData("2026-03-29")]
    public void It_finds_the_Bitcoin_daily_cycle_lows_CF_labelled(string cf)
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var target = DateTime.Parse(cf);
        var lows = Marked(Btc()[CipherCProvider.CompDcl]).Select(i => bars[i].Date).ToList();
        _out.WriteLine(string.Join(", ", lows.Where(d => d.Year >= 2025).Select(d => d.ToString("yyyy-MM-dd"))));
        Assert.Contains(lows, d => Math.Abs((d - target).TotalDays) <= 4);
    }

    // ── 3. What a cycle low is ─────────────────────────────────────────────────────

    /// <summary>
    /// Every daily cycle low sits on its bar's low and is confirmed on a later bar at that price:
    /// by the daily detector, which restarts the day count from it — or, for a low the daily
    /// detector skipped, by the weekly or yearly one.
    /// </summary>
    [Fact]
    public void Every_low_is_its_bars_low_and_is_confirmed_later()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var r = Btc();
        var lows = Marked(r[CipherCProvider.CompDcl]).ToList();
        Assert.True(lows.Count > 60, $"only {lows.Count} daily cycle lows in fifteen years of Bitcoin");
        var lags = new List<int>();
        int byHigher = 0;
        foreach (int c in lows)
        {
            Assert.Equal(bars[c].Low, r[CipherCProvider.CompDcl][c]);
            int at = ConfirmedAt(CipherCProvider.CompDclConfirmed, c);
            if (at > c)
            {
                Assert.Equal(1, r[CipherCProvider.CompDailyWindow][at] - (at - c));
                Assert.Equal(at - c, r[CipherCProvider.CompDcl + CipherCProvider.AfterSuffix][c]);
                Assert.Equal(at - c, r[CipherCProvider.CompDclConfirmed + CipherCProvider.AgoSuffix][at]);
                lags.Add(at - c);
                continue;
            }
            Assert.True(ConfirmedAt(CipherCProvider.CompWclConfirmed, c) > c || ConfirmedAt(CipherCProvider.CompYclConfirmed, c) > c,
                $"the low on {bars[c].Date:yyyy-MM-dd} was never confirmed");
            byHigher++;
        }
        lags.Sort();
        _out.WriteLine($"{lows.Count} lows, {byHigher} confirmed only by a higher cycle, daily confirmation lag median {lags[lags.Count / 2]} days");
        Assert.True(byHigher <= lows.Count / 10, $"{byHigher} of {lows.Count} daily lows came from the higher cycles");

        int ConfirmedAt(string comp, int c) =>
            Marked(r[comp]).FirstOrDefault(t => t > c && r[comp + CipherCProvider.LowSuffix][t] == bars[c].Low, -1);
    }

    /// <summary>
    /// A cancellation is a bar going under a pending low (the cancelled low's price); a failure is a
    /// close under a confirmed low, at any gap. Both happen on Bitcoin, and no cancelled low is ever
    /// one of the confirmed lows.
    /// </summary>
    [Fact]
    public void A_cancellation_undercuts_a_pending_low_and_a_failure_closes_under_a_confirmed_one()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var r = Btc();
        var cancelled = Marked(r[CipherCProvider.CompCancelled]).ToList();
        var failed = Marked(r[CipherCProvider.CompFailed]).ToList();
        var lows = Marked(r[CipherCProvider.CompDcl]).ToHashSet();
        _out.WriteLine($"{cancelled.Count} cancelled, {failed.Count} failed");
        Assert.NotEmpty(cancelled);
        Assert.NotEmpty(failed);
        foreach (int t in cancelled)
        {
            Assert.True(bars[t].Low < r[CipherCProvider.CompCancelled][t], $"{bars[t].Date:yyyy-MM-dd}: low {bars[t].Low} is not under the pending low");
            int at = t - (int)r[CipherCProvider.CompCancelled + CipherCProvider.AgoSuffix][t];
            Assert.DoesNotContain(at, lows);
        }
        foreach (int t in failed)
        {
            Assert.True(bars[t].Close < r[CipherCProvider.CompFailed][t], $"{bars[t].Date:yyyy-MM-dd}: close {bars[t].Close} is not under the low");
            Assert.Contains(t - (int)r[CipherCProvider.CompFailed + CipherCProvider.AgoSuffix][t], lows);
        }
    }

    /// <summary>The number under a low is the cycle that ended there, in days, counting both ends — CF's convention.</summary>
    [Fact]
    public void The_count_under_a_low_is_the_cycle_in_days_counting_both_ends()
    {
        var r = Btc();
        var lows = Marked(r[CipherCProvider.CompDcl]).ToList();
        var length = r[CipherCProvider.CompDcl + CipherCProvider.LengthSuffix];
        var label = r[CipherCProvider.CompDcl + ComponentCompanions.Label];
        for (int k = 1; k < lows.Count; k++)
        {
            Assert.Equal(lows[k] - lows[k - 1] + 1, length[lows[k]]);
            Assert.Equal(length[lows[k]], label[lows[k]]);
        }
        Assert.True(double.IsNaN(length[lows[0]]));
        // The weekly count is not printed where the daily one is: they would print on one another.
        Assert.False(r.ContainsKey(CipherCProvider.CompWcl + ComponentCompanions.Label));
    }

    /// <summary>
    /// A weekly low is always one of the daily lows, a yearly low one of the weekly ones — CF's
    /// stacks sit on one another. Since the weekly lows are picked from the daily ones
    /// (<see cref="CipherCWeeklyTests"/>), Bitcoin's weekly lows from June 2022 are the original's
    /// wherever the two daily lows agree (read off the original's chart in TradingView, candles,
    /// 2026-10-24). Counted from 48 days, Cipher C has the original's daily lows of 19 December
    /// 2022 and 11 March 2025 and a weekly low on each, as the original draws. Confirming a low on
    /// the bar it comes due, even when that bar goes lower, it has the original's daily low of 12 May
    /// 2023 and so its weekly low of 11 September 2023: 9 of the original's 10 weekly lows to the
    /// fixture's end. It misses 21 November 2025, after keeping the daily low of 22 June 2025 as a
    /// weekly one where the original did not, and keeps 12 May 2023 as a weekly low, which the original does not. The original draws no weekly low
    /// on the 5 August 2024 crash, a daily low 31 days after the weekly one of 5 July.
    /// </summary>
    [Fact]
    public void A_weekly_low_sits_on_a_daily_low_and_a_yearly_low_on_a_weekly_one()
    {
        var r = Btc();
        var d = Marked(r[CipherCProvider.CompDcl]).ToHashSet();
        var w = Marked(r[CipherCProvider.CompWcl]).ToList();
        var y = Marked(r[CipherCProvider.CompYcl]).ToList();
        _out.WriteLine($"{d.Count} daily, {w.Count} weekly, {y.Count} yearly");
        Assert.NotEmpty(w);
        Assert.All(w, i => Assert.Contains(i, d));
        Assert.All(y, i => Assert.Contains(i, w));
        var bars = CipherSrSpeechTests.BtcDaily();
        var original = new[] { "2022-06-18", "2022-11-21", "2022-12-19", "2023-09-11", "2024-01-23", "2024-07-05",
                               "2024-10-10", "2025-03-11", "2025-11-21", "2026-02-06", "2026-07-01" }.Select(DateTime.Parse).ToList();
        var since2022 = w.Select(i => bars[i].Date.Date).Where(x => x >= new DateTime(2022, 5, 12)).ToList();
        // The fixture ends on 15 June 2026, before the original's low of 1 July.
        Assert.Equal(original.Where(x => x <= bars[^1].Date).Except(new[] { new DateTime(2025, 11, 21) }),
                     since2022.Except(new[] { new DateTime(2023, 5, 12), new DateTime(2025, 6, 22) }));
        Assert.Contains(d, i => bars[i].Date == new DateTime(2024, 8, 5));
        Assert.DoesNotContain(w, i => bars[i].Date == new DateTime(2024, 8, 5));
    }

    // ── 4. Tiers and timeframes ─────────────────────────────────────────────────────

    private static List<Ohlcv> Weekly(IReadOnlyList<Ohlcv> daily)
    {
        var weeks = new List<Ohlcv>();
        foreach (var g in daily.GroupBy(b => b.Date.AddDays(-(((int)b.Date.DayOfWeek + 6) % 7)).Date))
        {
            var b = g.ToList();
            weeks.Add(new Ohlcv(g.Key, b[0].Open, b.Max(x => x.High), b.Min(x => x.Low), b[^1].Close, b.Sum(x => x.Volume)));
        }
        return weeks;
    }

    /// <summary>
    /// <b>Off daily the counts run in the chart's bars, as the original runs them (2026-10-25).</b>
    /// On a weekly chart CF says "Timeframe Not Optimised, Switch to Daily" and still draws its daily
    /// lows, its daily count taken as weeks: the Dow's 36 fits its weekly labels at 36. Cody: "copy
    /// the original: goal is 100% accurate replication". This chart once had no daily cycle at all
    /// (a wish from Loukas's weekly chart, before the rebuild copied CF).
    /// </summary>
    [Fact]
    public void A_weekly_chart_counts_the_daily_cycle_in_its_bars()
    {
        var weeks = Weekly(CipherSrSpeechTests.BtcDaily());
        var r = Run(weeks, "BTC/USD");
        Assert.Equal(1.0, r[CipherCProvider.InBarsKey][^1]);
        // Bitcoin's 54 to 66 days are 54 to 66 bars, and its weekly 22 to 42 weeks 154 to 294.
        Assert.Equal(54.0, r[CipherCProvider.CompDailyWindow + CipherCProvider.StartSuffix][^1]);
        Assert.Equal(66.0, r[CipherCProvider.CompDailyWindow + CipherCProvider.EndSuffix][^1]);
        Assert.Equal(154.0, r[CipherCProvider.CompWeeklyWindow + CipherCProvider.StartSuffix][^1]);
        var dcl = Marked(r[CipherCProvider.CompDcl]).ToList();
        Assert.True(dcl.Count >= 5, $"{dcl.Count} daily lows");
        // A low's cycle is counted in bars, both ends, as on a daily chart.
        Assert.Equal(dcl[^1] - dcl[^2] + 1, r[CipherCProvider.CompDcl + CipherCProvider.LengthSuffix][dcl[^1]]);
        Assert.NotEmpty(Marked(r[CipherCProvider.CompDcl + ComponentCompanions.Label]));

        var words = CipherCProvider.StateWords(n => r.TryGetValue(n, out var a) ? a : null, weeks.Count - 1, weeks[^1].Date);
        _out.WriteLine(string.Join(". ", words));
        Assert.Contains(words, s => s.StartsWith("daily cycle bar ", StringComparison.Ordinal));
        Assert.Contains(words, s => s.StartsWith("weekly cycle bar ", StringComparison.Ordinal));
        Assert.Contains(words, s => s.StartsWith("the last daily pending cycle low was cancelled", StringComparison.Ordinal) && s.EndsWith(" bars ago", StringComparison.Ordinal));
        Assert.Contains(words, s => s.EndsWith("Not tuned for this timeframe: its days are counted as this chart's bars; switch to daily for the tuned count", StringComparison.Ordinal));
        var p = new CipherCProvider();
        string? low = p.GetComponentSpeech(CipherCProvider.CompDcl, r[CipherCProvider.CompDcl][dcl[^1]], weeks[dcl[^1]], r, dcl[^1]);
        _out.WriteLine(low);
        Assert.Contains(" bar cycle.", low);
    }

    /// <summary>The Dow's four-hour chart: two bars a session, and the Dow's 36 days are 36 bars.</summary>
    [Fact]
    public void A_four_hour_stock_chart_counts_the_daily_cycle_in_its_bars()
    {
        var bars = new List<Ohlcv>();
        foreach (var day in Weekdays(1200, new DateTime(2020, 1, 6)))
            for (int h = 0; h < 2; h++)
            {
                int i = bars.Count;
                double c = 100 + 10 * Math.Sin(2 * Math.PI * i / 40.0) + i * 0.01;
                bars.Add(new Ohlcv(day.Date.AddHours(13.5 + 4 * h), c - 0.3, c + 1, c - 1, c, 1000));
            }
        var r = Run(bars, "DJI");
        Assert.Equal(1.0, r[CipherCProvider.InBarsKey][^1]);
        Assert.Equal(36.0, r[CipherCProvider.CompDailyWindow + CipherCProvider.StartSuffix][^1]);
        Assert.Equal(110.0, r[CipherCProvider.CompWeeklyWindow + CipherCProvider.StartSuffix][^1]);
    }

    /// <summary>And a daily chart counts days, with no warning.</summary>
    [Fact]
    public void A_daily_chart_counts_days()
    {
        var bars = Weekdays(800, new DateTime(2022, 1, 3));
        var r = Run(bars, "DJI");
        Assert.Equal(0.0, r[CipherCProvider.InBarsKey][^1]);
        var words = CipherCProvider.StateWords(n => r.TryGetValue(n, out var a) ? a : null, bars.Count - 1, bars[^1].Date);
        Assert.Contains(words, s => s.StartsWith("daily cycle day ", StringComparison.Ordinal));
        Assert.DoesNotContain(words, s => s.Contains("Not tuned", StringComparison.Ordinal));
    }

    private static List<Ohlcv> Weekdays(int count, DateTime from)
    {
        var bars = new List<Ohlcv>();
        var d = from;
        for (int i = 0; bars.Count < count; i++, d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            double c = 100 + 10 * Math.Sin(2 * Math.PI * bars.Count / 40.0) + bars.Count * 0.02;
            bars.Add(new Ohlcv(d, c - 0.3, c + 1, c - 1, c, 1000));
        }
        return bars;
    }

    /// <summary>
    /// A stock's weekly bar is five trading days. (The bar clock once had the factor upside down —
    /// 9.8 trading days a bar — and switched the weekly tier off on every weekly stock chart.) Its
    /// counts run in bars: the S&amp;P's 36 days are 36 bars, its 22 weeks 110.
    /// </summary>
    [Fact]
    public void A_weekly_stock_chart_counts_in_bars()
    {
        var weeks = Weekly(Weekdays(3000, new DateTime(2010, 1, 4)));
        var clock = CipherCEngine.MeasureBars(weeks.ToArray());
        Assert.False(clock.TradesEveryDay);
        Assert.Equal(0.2, clock.BarsPerDay, 3);
        var r = Run(weeks, "SPX");
        Assert.Equal(36.0, r[CipherCProvider.CompDailyWindow + CipherCProvider.StartSuffix][^1]);
        Assert.Equal(110.0, r[CipherCProvider.CompWeeklyWindow + CipherCProvider.StartSuffix][^1]);
    }

    /// <summary>
    /// A preset's days are the chart's days: Bitcoin's ranges are calendar days, so on a weekday-only
    /// chart 54 calendar days are 54 × 5/7 ≈ 38.6 trading days.
    /// </summary>
    [Fact]
    public void A_calendar_day_preset_on_a_weekday_chart_is_converted_to_trading_days()
    {
        var bars = Weekdays(800, new DateTime(2022, 1, 3));
        var r = Run(bars, "BTC");
        Assert.Equal(38.6, r[CipherCProvider.CompDailyWindow + CipherCProvider.StartSuffix][^1]);
        Assert.Equal(47.1, r[CipherCProvider.CompDailyWindow + CipherCProvider.EndSuffix][^1]);
        var spx = Run(bars, "SPX");
        Assert.Equal(36.0, spx[CipherCProvider.CompDailyWindow + CipherCProvider.StartSuffix][^1]);
    }

    /// <summary>COIN's CF ranges are already in trading days on a weekday chart.</summary>
    [Fact]
    public void A_COINS_daily_window_is_its_CF_trading_day_range_on_a_weekday_chart()
    {
        var bars = Weekdays(800, new DateTime(2022, 1, 3));
        var r = Run(bars, "COIN");
        Assert.Equal(40.0, r[CipherCProvider.CompDailyWindow + CipherCProvider.StartSuffix][^1]);
        Assert.Equal(90.0, r[CipherCProvider.CompDailyWindow + CipherCProvider.EndSuffix][^1]);
    }

    // ── 5. The sixteen assets ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("^GSPC", "SPX")]
    [InlineData("US30", "DJI")]
    [InlineData("NDX", "NDQ")]
    [InlineData("SP:SPX", "SPX")]
    [InlineData("BTC/USD", "BTC")]
    [InlineData("BTCUSDT", "BTC")]
    [InlineData("XAUUSD", "GOLD")]
    [InlineData("TVC:GOLD", "GOLD")]
    [InlineData("ETH-USD", "ETH")]
    // ETFs and futures are not the index: the original tunes none of them.
    [InlineData("SPY", null)]
    [InlineData("DIA", null)]
    [InlineData("QQQ", null)]
    [InlineData("ES", null)]
    [InlineData("GLD", null)]
    [InlineData("IBIT", null)]
    [InlineData("^IXIC", null)]
    [InlineData("MSTR", "MSTR")]
    [InlineData("AAPL", null)]
    [InlineData("SOL/USD", null)]
    public void A_symbol_finds_its_preset(string symbol, string? key)
        => Assert.Equal(key, CipherCEngine.PresetForSymbol(symbol)?.Key);

    [Fact]
    public void There_are_sixteen_presets_and_an_unknown_asset_falls_back_by_whether_it_trades_every_day()
    {
        Assert.Equal(16, CipherCEngine.Presets.Count);
        Assert.Equal(16, CipherCEngine.Presets.Select(p => p.Key).Distinct().Count());
        var sol = CipherCEngine.Resolve("Auto", "SOL/USD", tradesEveryDay: true);
        var aapl = CipherCEngine.Resolve("Auto", "AAPL", tradesEveryDay: false);
        Assert.Equal(("SPX", false), Key(sol));
        Assert.Equal(("SPX", false), Key(aapl));
        // The original's default count: 32 bars on either clock, never stretched by 7/5 on a coin.
        Assert.Equal(32, sol.Preset.CountedDailyMin);
        Assert.Equal(32, aapl.Preset.CountedDailyMin);
        Assert.True(sol.Preset.CalendarDays);
        Assert.False(aapl.Preset.CalendarDays);
        Assert.True(sol.Preset.WeeklyEveryTwoDaily);
        Assert.Equal(("GOLD", true), Key(CipherCEngine.Resolve("GOLD", "AAPL", tradesEveryDay: false)));
        Assert.Equal(("ETH", true), Key(CipherCEngine.Resolve("", "ETH/USD", tradesEveryDay: true)));
        static (string, bool) Key((CyclePreset P, bool F) r) => (r.P.Key, r.F);
    }

    [Theory]
    [InlineData("COIN", 40, 90, 24, 42, 4, 4, false)]
    [InlineData("MSTR", 40, 90, 24, 42, 4, 4, false)]
    [InlineData("MARA", 36, 44, 22, 26, 4, 4, false)]
    [InlineData("RIOT", 36, 44, 22, 26, 4, 4, false)]
    [InlineData("GDX", 22, 28, 22, 26, 8, 8, false)]
    [InlineData("GOLD", 22, 28, 20, 28, 8, 8, false)]
    [InlineData("BTC", 54, 66, 22, 42, 4, 4, true)]
    // Read from the original's own range table in TradingView, 2026-10-22 (SPX's does not draw there).
    [InlineData("NDQ", 36, 44, 22, 31, 4, 4, false)]
    [InlineData("DJI", 36, 44, 22, 31, 4, 4, false)]
    [InlineData("DXY", 15, 24, 16, 20, 3, 3, false)]
    [InlineData("SILVER", 22, 28, 20, 28, 8, 8, false)]
    [InlineData("USOIL", 36, 44, 16, 20, 3, 3, false)]
    [InlineData("GDXJ", 22, 28, 22, 26, 8, 8, false)]
    [InlineData("SILJ", 22, 28, 22, 26, 8, 8, false)]
    [InlineData("ETH", 15, 25, 7, 10, 4, 4, true)]
    public void CF_read_presets_have_the_recorded_ranges(string key, double dailyMin, double dailyMax,
        double weeklyMin, double weeklyMax, double yearlyMin, double yearlyMax, bool calendarDays)
    {
        var p = CipherCEngine.Preset(key);
        Assert.Equal(dailyMin, p.DailyMin);
        Assert.Equal(dailyMax, p.DailyMax);
        Assert.Equal(weeklyMin, p.WeeklyMin);
        Assert.Equal(weeklyMax, p.WeeklyMax);
        Assert.Equal(yearlyMin, p.YearlyMin);
        Assert.Equal(yearlyMax, p.YearlyMax);
        Assert.Equal(calendarDays, p.CalendarDays);
        Assert.Equal("tuned", p.Source);
    }

    /// <summary>The preset is said with where its numbers came from; an asset CF does not tune says so.</summary>
    [Fact]
    public void The_preset_is_said_with_its_source()
    {
        var gold = CipherCProvider.PresetWords(4, new DateTime(2025, 1, 1));
        Assert.StartsWith("Gold ranges, tuned: daily 22 to 28 trading days", gold);
        var dji = CipherCProvider.PresetWords(2, new DateTime(2025, 1, 1));
        Assert.Contains("Dow Jones ranges, tuned:", dji);
        var other = CipherCProvider.PresetWords(-1 - 0, new DateTime(2025, 1, 1));
        Assert.StartsWith("no preset for this symbol, so the default count: daily lows from 32 bars, weekly 22 to 31 weeks", other);
        Assert.DoesNotContain("halving", other);
        var btc = CipherCProvider.PresetWords(11, new DateTime(2026, 6, 1));
        Assert.Contains("after the April 2024 halving", btc);
        Assert.Contains("daily 54 to 66 days, counted from 48, weekly 22 to 42 weeks", btc);
        Assert.DoesNotContain("counted from", gold);
    }

    /// <summary>
    /// The original's table says Bitcoin's daily cycle is 54–66 days, but in TradingView's bar
    /// replay (candles, 2022–26, 24 lows) it counts as if the shortest were 48: it confirms 24 bars
    /// after the low on 19 of 24, never sooner, where 54 would give 27. So on the Bitcoin fixture the
    /// daily confirmations come 24 bars after their low, never sooner, and that is the commonest lag.
    /// </summary>
    [Fact]
    public void Bitcoin_counts_its_daily_cycle_from_48_days_though_its_table_says_54()
    {
        var r = Btc();
        var lags = Marked(r[CipherCProvider.CompDclConfirmed]).Select(t => (int)r[CipherCProvider.CompDclConfirmed + CipherCProvider.AgoSuffix][t]).ToList();
        var common = lags.GroupBy(x => x).OrderByDescending(g => g.Count()).First();
        _out.WriteLine($"{lags.Count} daily confirmations, shortest lag {lags.Min()}, commonest {common.Key} on {common.Count()}");
        Assert.Equal(24, lags.Min());
        Assert.Equal(24, common.Key);
        Assert.Equal(48, CipherCEngine.Preset("BTC").CountedDailyMin);
        Assert.Equal(54, CipherCEngine.Preset("BTC").DailyMin);
        Assert.Equal(22, CipherCEngine.Preset("GDX").CountedDailyMin);
    }

    /// <summary>
    /// Where the original confirms a daily low, in bars after it (candle replays, 2021–26): Silver
    /// 12 on 33 of 40 lows, though its table (22–28 days) is Gold's, and Gold 11 on 37 of 44; the
    /// dollar index 9 on 39 of 52, though its table says 15–24. So Silver counts from 24 and the
    /// dollar from 18, and the same weekday bars give each its own shortest and commonest lag.
    /// </summary>
    [Theory]
    [InlineData("XAG/USD", 12, 22, 5, "daily 22 to 28 trading days, counted from 24,")]
    [InlineData("XAU/USD", 11, 22, 4, "daily 22 to 28 trading days, weekly")]
    [InlineData("DXY", 9, 15, 3, "daily 15 to 24 trading days, counted from 18,")]
    public void Each_asset_confirms_when_the_original_does_whatever_its_table_says(string symbol, int lag, int tableMin, int preset, string words)
    {
        // Bitcoin's bars without weekends, so a trading-day preset counts one bar a day.
        var bars = CipherSrSpeechTests.BtcDaily().Where(b => b.Date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();
        var r = Run(bars, symbol);
        var lags = Marked(r[CipherCProvider.CompDclConfirmed]).Select(t => (int)r[CipherCProvider.CompDclConfirmed + CipherCProvider.AgoSuffix][t]).ToList();
        var common = lags.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
        _out.WriteLine($"{symbol}: {lags.Count} confirmations, shortest {lags.Min()}, commonest {common}");
        Assert.Equal(lag, lags.Min());
        Assert.Equal(lag, common);
        Assert.Contains(words, CipherCProvider.PresetWords(preset, new DateTime(2025, 1, 1)));
        Assert.Equal(tableMin, CipherCEngine.Presets[preset].DailyMin);   // the table's range is still what is shown
    }

    /// <summary>
    /// Ethereum's table says 15–25 days, but in the original's bar replay (candles, 2022–26, 75
    /// lows) a short cycle's check mark comes no sooner than the last low + 22 (16 of 16 cut-short
    /// cycles), as if the shortest cycle were 16: counted from 16 − 2 = 14 and held 8. At 15 it
    /// would be held 7 (half, rounded down) and the short-cycle wait would be 20.
    /// </summary>
    [Fact]
    public void Ethereum_counts_its_daily_cycle_from_16_days()
    {
        var r = Run(CipherSrSpeechTests.BtcDaily(), "ETH/USD");
        var lows = Marked(r[CipherCProvider.CompDclConfirmed]).Select(t => (Mark: t, Low: t - (int)r[CipherCProvider.CompDclConfirmed + CipherCProvider.AgoSuffix][t])).ToList();
        // A low closed under within the gap is taken back (cancelled) and the next counts from the
        // low before it, so pairs with a cancellation between them are not a wait from the last low.
        var cancelled = Marked(r[CipherCProvider.CompCancelled]).ToList();
        var waits = lows.Zip(lows.Skip(1), (a, b) => (a, b)).Where(p => !cancelled.Any(c => c > p.a.Mark && c <= p.b.Mark)).Select(p => p.b.Mark - p.a.Low).ToList();
        var lags = lows.Select(x => x.Mark - x.Low).ToList();
        _out.WriteLine($"{lows.Count} confirmations, shortest lag {lags.Min()}, shortest wait from the last low {waits.Min()} ({waits.Count(w => w == waits.Min())} times)");
        Assert.Equal(8, lags.Min());
        Assert.Equal(22, waits.Min());
        Assert.Equal(15, CipherCEngine.Preset("ETH").DailyMin);
        Assert.Contains("daily 15 to 25 days, counted from 16,", CipherCProvider.PresetWords(CipherCEngine.Presets.ToList().FindIndex(p => p.Key == "ETH"), new DateTime(2025, 1, 1)));
    }

    /// <summary>
    /// Coinbase and MicroStrategy share a table (40–90 days), but in the original's bar replay
    /// (candles, 2022–26) both confirm 25 bars after the low (14 of 17 and 14 of 18), and a cut-short
    /// cycle's check mark comes no sooner than the last low + 74 and a first label no sooner than
    /// + 50: a shortest cycle of 51, counted from 51 − 2 = 49 and held half of it rounded down, 25
    /// (rounded to the nearest it would be 26, and at 50 the waits would be 73 and 49). MARA's table
    /// says 36–44, but it confirms 19 bars after the low (19 of 23) and a cut-short cycle no sooner than
    /// the last low + 55 (55, 55, 56): counted from 38 (36 would hold 18 and wait 52; 39, wait 56).
    /// RIOT has MARA's table and hold, but its cut-short cycles wait until the last low + 56 (56, 56, 56)
    /// and its first label comes no sooner than + 38: counted from 39.
    /// </summary>
    [Theory]
    [InlineData("MSTR", 40, 90, 51, 25, 74)]
    [InlineData("COIN", 40, 90, 51, 25, 74)]
    [InlineData("MARA", 36, 44, 38, 19, 55)]
    [InlineData("RIOT", 36, 44, 39, 19, 56)]
    public void Stocks_count_from_the_originals_shortest_cycle(string symbol, int tableMin, int tableMax, int counted, int hold, int wait)
    {
        var bars = CipherSrSpeechTests.BtcDaily().Where(b => b.Date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();
        var r = Run(bars, symbol);
        var lows = Marked(r[CipherCProvider.CompDclConfirmed]).Select(t => (Mark: t, Low: t - (int)r[CipherCProvider.CompDclConfirmed + CipherCProvider.AgoSuffix][t])).ToList();
        var cancelled = Marked(r[CipherCProvider.CompCancelled]).ToList();
        var waits = lows.Zip(lows.Skip(1), (a, b) => (a, b)).Where(p => !cancelled.Any(c => c > p.a.Mark && c <= p.b.Mark)).Select(p => p.b.Mark - p.a.Low).ToList();
        var lags = lows.Select(x => x.Mark - x.Low).ToList();
        _out.WriteLine($"{symbol}: {lows.Count} confirmations, shortest lag {lags.Min()}, shortest wait from the last low {waits.Min()} ({waits.Count(w => w == waits.Min())} times)");
        Assert.Equal(hold, lags.Min());
        Assert.Equal(hold, lags.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key);
        Assert.Equal(wait, waits.Min());
        Assert.Equal(tableMin, CipherCEngine.Preset(symbol).DailyMin);
        Assert.Contains($"daily {tableMin} to {tableMax} trading days, counted from {counted},", CipherCProvider.PresetWords(CipherCEngine.Presets.ToList().FindIndex(p => p.Key == symbol), new DateTime(2025, 1, 1)));
    }

    /// <summary>Cody, 2026-10-21: "not supposed to be representative brand wise ... just cycles". No
    /// vendor name in anything the user reads (the metadata: description, parameter help, component
    /// names) or hears (every preset's spoken words, and the no-preset fallbacks).</summary>
    [Fact]
    public void Nothing_the_user_reads_or_hears_names_a_vendor()
    {
        var meta = Meta();
        var text = string.Join("\n", new object[] { meta }.Concat(meta.Parameters).Concat(meta.Components)
            .SelectMany(o => o.GetType().GetProperties().Where(pr => pr.PropertyType == typeof(string))
                .Select(pr => (string?)pr.GetValue(o))));
        Assert.Contains("cycle lows", text);
        Assert.Contains("10 by default", text);
        Assert.DoesNotMatch(Vendor, text);
        for (int i = 0; i < CipherCEngine.Presets.Count; i++)
        {
            Assert.DoesNotMatch(Vendor, CipherCProvider.PresetWords(i, new DateTime(2025, 1, 1)));
            Assert.DoesNotMatch(Vendor, CipherCProvider.PresetWords(-1 - i, new DateTime(2025, 1, 1)));
        }
    }

    private static readonly System.Text.RegularExpressions.Regex Vendor = new(@"\b(Camel|CF)\b");

    // ── 6. Speech ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(20, 36, 44, "daily cycle day 20, its window opens in 16 days")]
    [InlineData(35, 36, 44, "daily cycle day 35, its window opens in 1 day")]
    [InlineData(40, 36, 44, "daily cycle day 40, in its window, 4 days left")]
    [InlineData(43, 36, 44, "daily cycle day 43, in its window, 1 day left")]
    [InlineData(44, 36, 44, "daily cycle day 44, the last day of its window")]
    [InlineData(50, 36, 44, "daily cycle day 50, 6 days past its window")]
    public void The_day_count_is_said_against_the_window(double count, double start, double end, string expected)
        => Assert.Equal(expected, CipherCProvider.WindowWords("daily cycle", "day", "days", count, start, end));

    [Fact]
    public void A_tier_that_does_not_run_says_nothing_and_one_with_no_low_yet_says_so()
    {
        Assert.Null(CipherCProvider.WindowWords("daily cycle", "day", "days", double.NaN, double.NaN, double.NaN));
        Assert.Equal("no daily cycle low confirmed yet", CipherCProvider.WindowWords("daily cycle", "day", "days", double.NaN, 36, 44));
    }

    /// <summary>On a confirming bar the detail fact names the confluence; Alt+Shift+D reads the same words.</summary>
    [Fact]
    public void The_detail_fact_on_a_confirming_bar_names_the_window_and_the_confluence()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var r = Btc();
        int t = Marked(r[CipherCProvider.CompDclConfirmed]).Last();
        var fact = Provider.GetDetailFact(CipherCProvider.Code, bars.ToArray(), r, t, new());
        _out.WriteLine(fact);
        Assert.StartsWith("Daily cycle day ", fact);
        Assert.Matches("Daily cycle low confirmed, (high|mid|low) confluence", fact);
        Assert.Contains(". Bitcoin ranges", fact);
        Assert.Contains("yearly 4 years", fact);   // not "4 to 4"
        Assert.DoesNotMatch(@"\. [a-z]", fact);
    }

    // ── 7. A Cipher C saved before the rebuild ──────────────────────────────────────

    /// <summary>Every component the oscillator Cipher C declared — what an old chart carries.</summary>
    internal static readonly string[] SavedBeforeRebuild =
    {
        "Top Triple", "Top Double", "Top Single", "Cycle Sine", "Bottom Single", "Bottom Double",
        "Bottom Triple", "Lead Sine", "Shallow Peak", "Shallow Trough",
    };

    private static readonly IndicatorModelFactory Factory = new(
        new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
        new MockIndicatorPreferencesService());

    private static IndicatorEngine Engine()
    {
        var providers = new List<IIndicatorProvider> { new CipherCProvider() };
        return new IndicatorEngine(new IndicatorService(providers, NullLogger<IndicatorService>.Instance), new CustomIndicatorRegistry(), providers);
    }

    private static ChartSeries Restored(string[] savedComponents)
    {
        var meta = new CipherCProvider().GetIndicators();
        var saved = new SeriesConfig { Id = "cc-1", Name = "Cipher C 10 3 3 75", IndicatorCode = "CIPHER_C", Pane = "Pane_CIPHER_C" };
        saved.Parameters["CyclePeriod"] = 10; saved.Parameters["SmoothPeriod"] = 3; saved.Parameters["OBLevel"] = 75;
        foreach (var n in savedComponents)
            saved.Components.Add(new ComponentConfig { Name = n, DisplayName = n, IsVisible = true, IsEnabled = true, Volume = 1f });
        foreach (var (name, v) in new[] { ("Extreme OB", 90.0), ("Overbought", 75.0), ("Zero", 0.0), ("Oversold", -75.0), ("Extreme OS", -90.0) })
            saved.Levels.Add(new LevelConfig { Name = name, Value = v, IsVisible = true });
        WorkspaceInitializer.MigrateSeriesConfig(saved, meta);
        return SeriesManagementService.MaterializeSaved(saved, meta[0], Factory,
            Array.Empty<IReadOnlyDictionary<string, object>>(), Engine(), new MockIndicatorPreferencesService());
    }

    private static string Landing(ChartSeries series, List<Ohlcv> bars, IndicatorEngine engine, int index, int at)
    {
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(series),
            PrimarySeriesId = series.Id, FocusedSeriesId = series.Id, FocusedComponentIndex = index,
            CurrentDataIndex = at, ViewportStartIndex = Math.Max(0, at - 50), ViewportLength = 100,
            LastInteractionContext = InteractionContext.Component,
        };
        return new SpeechFormatter(NullLogger<SpeechFormatter>.Instance, engine)
            .FormatPointFeedback(state, isXMove: false, isYMove: true, series, bars[at], "");
    }

    /// <summary>
    /// An old chart's Cipher C comes back as the new one: on the candles, with the new components,
    /// none of the old (so no keyboard stop speaks a removed signal), and without its ±75/±90
    /// oscillator levels — on the price pane those were prices near zero that would crush the
    /// candles into the top of the pane.
    /// </summary>
    [Fact]
    public void A_Cipher_C_saved_before_the_rebuild_restores_as_the_new_one()
    {
        var series = Restored(SavedBeforeRebuild);
        var names = series.Components.Select(c => c.Name).ToList();
        _out.WriteLine(string.Join(" | ", names));
        Assert.Equal("Main", series.Config.Pane);
        Assert.Equal(Meta().Components.Select(c => c.Name), names);
        Assert.Empty(names.Intersect(SavedBeforeRebuild));
        Assert.Empty(series.Levels);

        var bars = CipherSrSpeechTests.BtcDaily();
        foreach (var kv in Btc()) series.Data.ComponentData[kv.Key] = kv.Value;
        var engine = Engine();
        int at = Marked(Btc()[CipherCProvider.CompDcl]).Last();
        var said = Enumerable.Range(0, series.Components.Count).Select(i => Landing(series, bars, engine, i, at)).ToList();
        foreach (var s in said) _out.WriteLine(s);
        Assert.DoesNotContain(said, s => SavedBeforeRebuild.Any(o => s.Contains(o, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Alt+Shift+D on the Cipher C reads where the cycle is against its window.</summary>
    [Fact]
    public void Bar_detail_reads_the_cycle_against_its_window()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var series = Restored(Array.Empty<string>());
        foreach (var kv in Btc()) series.Data.ComponentData[kv.Key] = kv.Value;
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(series),
            PrimarySeriesId = series.Id, FocusedSeriesId = series.Id,
            CurrentDataIndex = bars.Count - 1, ViewportStartIndex = bars.Count - 100, ViewportLength = 100,
        };
        var bus = new SpyEventBus();
        new BarDetailService(bus, null).AnnounceDetails(state);
        var said = bus.Log.OfType<AnnouncementEvent>().Last().Message;
        _out.WriteLine(said);
        Assert.Matches("daily cycle day [0-9]+, ", said);
    }

    // ── 7b. What each component says (2026-10-22) ──────────────────────────────────

    /// <summary>
    /// The companions reach the chart's buffer through the real engine and mapper — the arrays
    /// the speech, the label, the tint and the stops read. Computed on the provider directly they
    /// would exist whether or not the app ever kept them.
    /// </summary>
    [Fact]
    public async Task The_companions_reach_the_series_through_the_engine()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var series = Restored(Array.Empty<string>());
        var results = await Engine().CalculateAsync(CipherCProvider.Code, bars,
            new Dictionary<string, object> { ["__symbol"] = "BTC/USD" }, CancellationToken.None);
        var buffer = new IndicatorStateMapper().MapResultsToBuffer(series, results, bars.Count);
        foreach (var key in new[]
        {
            CipherCProvider.CompDcl + CipherCProvider.LengthSuffix, CipherCProvider.CompDcl + ComponentCompanions.Label,
            CipherCProvider.CompDclConfirmed + CipherCProvider.AgoSuffix, CipherCProvider.CompCancelled + CipherCProvider.AgoSuffix,
            CipherCProvider.CompDailyWindow + ComponentCompanions.Tint, CipherCProvider.CompDailyWindow + ComponentCompanions.Stops,
            CipherCProvider.CompDailyWindow + CipherCProvider.StartSuffix, CipherCProvider.EventsTierKey, CipherCProvider.PresetKey,
        })
        {
            Assert.True(buffer.ComponentData.TryGetValue(key, out var a) && a.Length == bars.Count, $"{key} did not reach the buffer");
            Assert.Equal(Btc()[key].Where(v => !double.IsNaN(v)).Count(), a!.Where(v => !double.IsNaN(v)).Count());
        }
    }

    /// <summary>
    /// Each of the fifteen, landed on through the real speech path on a bar where it has a value,
    /// says something beyond its name — and a daily low says its cycle, which only its companion
    /// holds: SpeechFormatter must hand the provider the companions, not only the components.
    /// </summary>
    [Fact]
    public void Every_component_says_what_its_value_means_through_the_speech_path()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var series = Restored(Array.Empty<string>());
        var r = Btc();
        foreach (var kv in r) series.Data.ComponentData[kv.Key] = kv.Value;
        var engine = Engine();
        for (int i = 0; i < series.Components.Count; i++)
        {
            var comp = series.Components[i];
            // The last bar it has a value on.
            var marked = Marked(r[comp.Name]).ToList();
            Assert.NotEmpty(marked);
            int at = marked[^1];
            string said = Landing(series, bars, engine, i, at);
            _out.WriteLine($"{comp.Name}: {said}");
            string own = Provider.GetComponentSpeech(comp.Name, r[comp.Name][at], bars[at], r, at) ?? "";
            if (comp.Name != CipherCProvider.CompSma)
                Assert.False(string.IsNullOrEmpty(own), $"{comp.Name} says nothing of its own on {bars[at].Date:yyyy-MM-dd}");
            Assert.Contains(own, said);
        }
        int low = Marked(r[CipherCProvider.CompDcl]).Last();
        Assert.Matches("An? [0-9]+ day cycle\\.", Landing(series, bars, engine,
            series.Components.FindIndex(c => c.Name == CipherCProvider.CompDcl), low));
    }

    [Theory]
    [InlineData(49, "a")] [InlineData(82, "an")] [InlineData(8, "an")] [InlineData(8.7, "an")] [InlineData(11, "an")]
    [InlineData(18, "an")] [InlineData(110, "a")] [InlineData(180, "a")] [InlineData(800, "an")] [InlineData(3.9, "a")]
    [InlineData(1, "a")] [InlineData(18000, "an")] [InlineData(1100, "a")]
    public void A_number_said_aloud_takes_the_right_article(double n, string article) => Assert.Equal(article, CipherCProvider.A(n));

    /// <summary>
    /// A cancellation and a failure say the low they went under and how long ago it was made: that
    /// many days back, the bar's low is the price they name.
    /// </summary>
    [Fact]
    public void A_cancellation_names_the_low_and_how_far_back_it_was()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var r = Btc();
        int checkedCount = 0;
        foreach (var comp in new[] { CipherCProvider.CompCancelled, CipherCProvider.CompFailed })
            foreach (int t in Marked(r[comp]))
            {
                double ago = r[comp + CipherCProvider.AgoSuffix][t];
                Assert.True(ago > 0, $"{comp} on {bars[t].Date:yyyy-MM-dd}: {ago} days ago");
                Assert.Equal(r[comp][t], bars[t - (int)ago].Low);
                checkedCount++;
            }
        Assert.True(checkedCount > 5);
        int c = Marked(r[CipherCProvider.CompCancelled]).Last();
        string said = Provider.GetComponentSpeech(CipherCProvider.CompCancelled, r[CipherCProvider.CompCancelled][c], bars[c], r, c)!;
        _out.WriteLine(said);
        Assert.Matches("^Daily pending cycle low cancelled: a low of [0-9,.]+, under the pending low of [0-9,.]+ from [0-9]+ days? ago\\.$", said);
        Assert.Contains($"a low of {bars[c].Low.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)},", said);   // the bar's low, not its close
    }

    /// <summary>
    /// A daily pending low names the low the daily tier holds: that bar is drawn as the next daily
    /// low, or a later bar cancels it at that price. Not the lowest low since the last cycle low,
    /// which the original skips when it comes before the cycle's top.
    /// </summary>
    [Fact]
    public void A_daily_pending_low_names_the_low_it_holds()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var r = Btc();
        var pending = Marked(r[CipherCProvider.CompPending]).ToList();
        var cancelled = Marked(r[CipherCProvider.CompCancelled]).ToList();
        var dcl = Marked(r[CipherCProvider.CompDcl]).ToHashSet();
        Assert.True(pending.Count > 10);
        int drawn = 0, dropped = 0;
        foreach (int t in pending)
        {
            double named = r[CipherCProvider.CompPending + CipherCProvider.LowestSuffix][t];
            Assert.False(double.IsNaN(named));
            int at = t - (int)r[CipherCProvider.CompPending + CipherCProvider.LowestAgoSuffix][t];
            Assert.True(at < t);
            Assert.Equal(bars[at].Low, named);
            int next = pending.FirstOrDefault(p => p > t, bars.Count);
            if (dcl.Contains(at)) drawn++;
            else if (next == bars.Count && !cancelled.Any(c => c > t)) Assert.DoesNotContain(dcl, d => d > t);   // still held at the end
            else
            {
                Assert.Contains(cancelled, c => c > t && c <= next && r[CipherCProvider.CompCancelled][c] == named);
                dropped++;
            }
        }
        Assert.True(drawn > 10 && dropped > 0, $"drawn {drawn}, dropped {dropped}");
        int last = pending[^1];
        string said = Provider.GetComponentSpeech(CipherCProvider.CompPending, r[CipherCProvider.CompPending][last], bars[last], r, last)!;
        _out.WriteLine(said);
        Assert.Matches("^Daily cycle low pending on day [0-9]+, (low|mid|high) confluence: the low of [0-9,.]+, [0-9]+ days? ago\\.( It replaces the cancelled low of [0-9,.]+\\.)?$", said);
        Assert.DoesNotContain("oversold", said);
    }

    /// <summary>
    /// The original tags its pending daily low LOW, MID or HIGH, bar by bar: a point for the
    /// Awesome Oscillator of hl2 (SMA 5 less SMA 34) above zero and one for the close above the
    /// VWAP of hlc3 anchored at the calendar month. Each scenario is built so that one wrong
    /// reading gives a different grade on its last bar: the close in place of hl2, no anchor, a
    /// weekly anchor, a chart without volume counted as having it.
    /// </summary>
    [Fact]
    public void The_pending_tag_is_the_oscillator_and_the_months_vwap()
    {
        static double[] Grade(List<Ohlcv> b) =>
            CipherCProvider.Grade(b.ToArray(), b.Select(x => x.High).ToArray(), b.Select(x => x.Low).ToArray(), b.Select(x => x.Close).ToArray());
        var jan = new DateTime(2026, 1, 1);
        Ohlcv Bar(int day, double c, double v = 1000, double up = 1, double down = 1) => new(jan.AddDays(day), c, c + up, c - down, c, v);

        // A flat close under rising highs: hl2 rises, the close does not; the VWAP of hlc3 stays over the close.
        var wicks = Enumerable.Range(0, 40).Select(i => Bar(i, 100, up: 1 + i * 0.5)).ToList();
        var g = Grade(wicks);
        Assert.True(double.IsNaN(g[32]), "the oscillator needs 34 bars");
        Assert.Equal(1, g[33]);
        Assert.Equal(1, g[^1]);

        // January at 200, then February from 100 rising slowly: the oscillator is under zero, the
        // close over February's VWAP and under one never reset.
        var rebound = Enumerable.Range(0, 31).Select(i => Bar(i, 200)).Concat(Enumerable.Range(0, 28).Select(j => Bar(31 + j, 100 + j * 0.5))).ToList();
        Assert.Equal(1, Grade(rebound)[^1]);

        // January at 100, February up to 140 by the 20th and back to 125 by the 28th: over the
        // month's VWAP, under the last week's.
        var fade = Enumerable.Range(0, 31).Select(i => Bar(i, 100))
            .Concat(Enumerable.Range(0, 28).Select(j => Bar(31 + j, j <= 19 ? 100 + 40.0 * j / 19 : 140 - 15.0 * (j - 19) / 8))).ToList();
        Assert.Equal(2, Grade(fade)[^1]);

        // Without volume there is no VWAP point, so never high (the original's index, dollar and metal charts).
        Assert.Equal(1, Grade(fade.Select(x => x with { Volume = 0 }).ToList())[^1]);
        Assert.Equal(1, Grade(fade.Select(x => x with { Volume = double.NaN }).ToList())[^1]);
    }

    /// <summary>
    /// The tag is on every bar the daily low is held, from the pending bar on, and on none
    /// after it is confirmed or dropped; it is the bar's own grade. A pending low named after a
    /// cancellation since the last confirmed low names the cancelled low it replaces (the
    /// original's 🔄), and the first one after a confirmation does not.
    /// </summary>
    [Fact]
    public void A_held_daily_low_carries_the_tag_and_what_it_replaces()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var r = Btc();
        var grade = r[CipherCProvider.CompPending + CipherCProvider.GradeSuffix];
        var replaced = r[CipherCProvider.CompPending + CipherCProvider.ReplacedSuffix];
        var own = CipherCProvider.Grade(bars.ToArray(), bars.Select(b => b.High).ToArray(), bars.Select(b => b.Low).ToArray(), bars.Select(b => b.Close).ToArray());
        var pending = Marked(r[CipherCProvider.CompPending]).ToList();
        var ends = Marked(r[CipherCProvider.CompDclConfirmed]).Concat(Marked(r[CipherCProvider.CompCancelled])).OrderBy(i => i).ToList();
        var confirmed = Marked(r[CipherCProvider.CompDclConfirmed]).ToHashSet();
        int held = 0, replacing = 0, first = 0;
        foreach (int p in pending)
        {
            int end = ends.FirstOrDefault(e => e > p, bars.Count);
            for (int t = p; t < end; t++) { Assert.Equal(own[t], grade[t]); held++; }
            if (end < bars.Count && confirmed.Contains(end)) Assert.True(double.IsNaN(grade[end]), $"graded on the confirmation at {end}");

            int before = ends.LastOrDefault(e => e < p, -1);
            if (before >= 0 && !confirmed.Contains(before)) { Assert.Equal(r[CipherCProvider.CompCancelled][before], replaced[p]); replacing++; }
            else { Assert.True(double.IsNaN(replaced[p])); first++; }
        }
        Assert.Equal(held, Marked(grade).Count());
        Assert.True(replacing > 0 && first > 0, $"replacing {replacing}, first {first}");
        Assert.Contains(Marked(grade), t => grade[t] == 0);
        Assert.Contains(Marked(grade), t => grade[t] == 1);
        Assert.Contains(Marked(grade), t => grade[t] == 2);
    }

    [Fact]
    public void A_pending_low_says_its_tag_and_the_low_it_replaces()
    {
        var bar = new Ohlcv(new DateTime(2026, 1, 1), 50, 55, 45, 50, 1000);
        var data = new Dictionary<string, double[]>
        {
            [CipherCProvider.EventsTierKey] = new[] { 1.0 },
            [CipherCProvider.CompDailyWindow] = new[] { 76.0 },
            [CipherCProvider.CompPending + CipherCProvider.LowestSuffix] = new[] { 45.5 },
            [CipherCProvider.CompPending + CipherCProvider.LowestAgoSuffix] = new[] { 7.0 },
            [CipherCProvider.CompPending + CipherCProvider.GradeSuffix] = new[] { 1.0 },
            [CipherCProvider.CompPending + CipherCProvider.ReplacedSuffix] = new[] { 48.25 },
        };
        Assert.Equal("Daily cycle low pending on day 76, mid confluence: the low of 45.50, 7 days ago. It replaces the cancelled low of 48.25.",
            Provider.GetComponentSpeech(CipherCProvider.CompPending, 50, bar, data, 0));
        data[CipherCProvider.CompPending + CipherCProvider.GradeSuffix] = new[] { 2.0 };
        data[CipherCProvider.CompPending + CipherCProvider.ReplacedSuffix] = new[] { double.NaN };
        Assert.Equal("Daily cycle low pending on day 76, high confluence: the low of 45.50, 7 days ago.",
            Provider.GetComponentSpeech(CipherCProvider.CompPending, 50, bar, data, 0));
    }

    [Fact]
    public void A_confirmation_that_replaces_a_cancelled_low_says_so()
    {
        var bar = new Ohlcv(new DateTime(2026, 1, 1), 50, 55, 45, 50, 1000);
        var data = new Dictionary<string, double[]>
        {
            [CipherCProvider.CompDclConfirmed + CipherCProvider.LowSuffix] = new[] { 45.5 },
            [CipherCProvider.CompDclConfirmed + CipherCProvider.AgoSuffix] = new[] { 9.0 },
            [CipherCProvider.CompDclConfirmed + CipherCProvider.ReplacedSuffix] = new[] { 48.25 },
            [CipherCProvider.CompWclConfirmed] = new[] { 2.0 },
        };
        Assert.Equal("Daily cycle low confirmed, high confluence: the low of 45.50, 9 days ago. It replaces the cancelled low of 48.25. " +
                     "The weekly low is confirmed on this bar too.",
            Provider.GetComponentSpeech(CipherCProvider.CompDclConfirmed, 3, bar, data, 0));
    }

    [Fact]
    public void A_top_says_its_high_and_where_it_came_in_its_cycle()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var r = Btc();
        var dcl = Marked(r[CipherCProvider.CompDcl]).ToList();
        foreach (var comp in new[] { CipherCProvider.CompLeftTop, CipherCProvider.CompMidTop, CipherCProvider.CompRightTop })
            foreach (int t in Marked(r[comp]))
            {
                int a = dcl.Last(i => i < t), b = dcl.First(i => i > t);
                Assert.Equal(t - a + 1, r[comp + CipherCProvider.DaySuffix][t]);
                Assert.Equal(b - a + 1, r[comp + CipherCProvider.LengthSuffix][t]);
            }
        int top = Marked(r[CipherCProvider.CompLeftTop]).Last();
        Assert.Matches("^Left translated top, bearish: high [0-9,.]+, day [0-9]+ of an? [0-9]+ day cycle\\.$",
            Provider.GetComponentSpeech(CipherCProvider.CompLeftTop, r[CipherCProvider.CompLeftTop][top], bars[top], r, top));
    }

    /// <summary>
    /// A timing window's value is the day count on every bar; its tint is 1 inside the window;
    /// Ctrl+Left/Right stops where it opens (1) and where it closes with no new low (0), nowhere
    /// else. On a daily chart the weekly window does not tint — the daily one's green would be
    /// covered.
    /// </summary>
    [Fact]
    public void A_timing_window_stops_where_it_opens_and_closes()
    {
        var r = Btc();
        var count = r[CipherCProvider.CompDailyWindow];
        var tint = r[CipherCProvider.CompDailyWindow + ComponentCompanions.Tint];
        var stops = r[CipherCProvider.CompDailyWindow + ComponentCompanions.Stops];
        double start = r[CipherCProvider.CompDailyWindow + CipherCProvider.StartSuffix][^1];
        double end = r[CipherCProvider.CompDailyWindow + CipherCProvider.EndSuffix][^1];
        int opens = 0, closes = 0;
        for (int t = 1; t < count.Length; t++)
        {
            if (double.IsNaN(count[t]) || double.IsNaN(count[t - 1])) continue;
            Assert.Equal(count[t] >= start && count[t] <= end ? 1.0 : 0.0, tint[t]);
            bool newCycle = count[t] <= count[t - 1];
            bool open = tint[t] == 1 && (tint[t - 1] == 0 || newCycle);
            bool close = tint[t] == 0 && tint[t - 1] == 1 && !newCycle;
            Assert.Equal(open ? 1.0 : close ? 0.0 : double.NaN, stops[t]);
            if (open) opens++;
            if (close) closes++;
        }
        _out.WriteLine($"{opens} openings, {closes} closings with no new low");
        Assert.True(opens > 20 && closes > 0);
        Assert.All(r[CipherCProvider.CompWeeklyWindow + ComponentCompanions.Tint], v => Assert.True(double.IsNaN(v)));
        Assert.NotEmpty(Marked(r[CipherCProvider.CompWeeklyWindow]));

        int first = Enumerable.Range(0, stops.Length).Last(t => stops[t] == 1);
        Assert.Matches("^daily cycle day [0-9]+, its [0-9.]+ to [0-9.]+ day window opens, [0-9]+ days left$",
            Provider.GetComponentSpeech(CipherCProvider.CompDailyWindow, count[first], CipherSrSpeechTests.BtcDaily()[first], r, first));
    }

    /// <summary>
    /// A Cipher C saved on 2026-10-21, with the 28 components, comes back with the fifteen: none of
    /// the removed ones is a keyboard stop.
    /// </summary>
    [Fact]
    public void A_Cipher_C_saved_with_the_28_components_restores_with_the_15()
    {
        var series = Restored(SavedOn20261021);
        Assert.Equal(Meta().Components.Select(c => c.Name), series.Components.Select(c => c.Name));
    }

    internal static readonly string[] RemovedOn20261022 =
    {
        "DCL Day", "WCL Week", "In DCL Window", "In WCL Window", "DCL Window Start", "DCL Window End",
        "WCL Window Start", "WCL Window End", "Cycle Low Confluence", "Cycle Low Updated", "Cycle Arc",
        "Cycle Arc Position", "Asset Preset", "DCL Count", "WCL Count",
    };

    private static readonly string[] SavedOn20261021 =
        Meta().Components.Select(c => c.Name).Except(new[] { CipherCProvider.CompDailyWindow, CipherCProvider.CompWeeklyWindow })
            .Concat(RemovedOn20261022).ToArray();

    [Theory]
    [MemberData(nameof(RemovedOn20261022Ids))]
    public void A_condition_on_a_component_removed_on_2026_10_22_says_what_to_use(string id)
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var series = Restored(Array.Empty<string>());
        foreach (var kv in Btc()) series.Data.ComponentData[kv.Key] = kv.Value;
        var state = WorkspaceState.Initial with { Data = new TimeSeriesBuffer<Ohlcv>(bars), ActiveSeries = ImmutableList.Create(series) };
        var eval = new ConditionEvaluator(Catalog);

        Assert.False(eval.Evaluate(new ConditionLeaf("a", id, LeafOperator.Fired), bars, state).OverallTrue);
        Assert.Contains("removed on 2026-10-22", eval.LastDegradation!);
        Assert.DoesNotMatch(Vendor, eval.LastDegradation!);
    }

    public static IEnumerable<object[]> RemovedOn20261022Ids() => RemovedOn20261022.Select(n => new object[] { $"CIPHER_C.{n}" });

    // ── 8. Strategies and alerts that named a removed component ─────────────────────

    private static readonly SignalCatalog Catalog = new(new IIndicatorProvider[] { new CipherCProvider() });

    /// <summary>
    /// A condition on any of the ten removed components is false and says why, naming the date and
    /// what to use instead; a condition on a live one says nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(RemovedIds))]
    public void A_condition_on_a_removed_Cipher_C_signal_says_it_was_removed(string id)
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var series = Restored(Array.Empty<string>());
        foreach (var kv in Btc()) series.Data.ComponentData[kv.Key] = kv.Value;
        var state = WorkspaceState.Initial with { Data = new TimeSeriesBuffer<Ohlcv>(bars), ActiveSeries = ImmutableList.Create(series) };
        var eval = new ConditionEvaluator(Catalog);

        var result = eval.Evaluate(new ConditionLeaf("a", id, LeafOperator.Fired), bars, state);
        Assert.False(result.OverallTrue);
        Assert.NotNull(eval.LastDegradation);
        Assert.Contains("removed on 2026-10-21", eval.LastDegradation!);
        Assert.Contains("rebuilt as a cycle-low indicator", eval.LastDegradation!);
        Assert.DoesNotMatch(Vendor, eval.LastDegradation!);

        eval.Evaluate(new ConditionLeaf("b", "CIPHER_C.DCL Confirmed", LeafOperator.Fired), bars, state);
        Assert.Null(eval.LastDegradation);
    }

    public static IEnumerable<object[]> RemovedIds() => SavedBeforeRebuild.Select(n => new object[] { $"CIPHER_C.{n}" });

    /// <summary>An alert set on the old Bottom Triple says once that it cannot fire, naming it.</summary>
    [Fact]
    public void An_alert_on_the_old_bottom_says_it_cannot_fire()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var series = Restored(Array.Empty<string>());
        foreach (var kv in Btc()) series.Data.ComponentData[kv.Key] = kv.Value;
        var state = WorkspaceState.Initial with { Data = new TimeSeriesBuffer<Ohlcv>(bars), ActiveSeries = ImmutableList.Create(series), CurrentDataIndex = bars.Count - 1 };
        var ev = new AlertEvaluator(new SdkCandlePatternAnalyzer(), new IndicatorContextAnalyzer());
        var said = new List<string>();
        ev.EvaluationDegraded += (_, why) => said.Add(why);
        var alert = new AlertDefinition
        {
            Id = "bottom", Name = "bottom", Delivery = AlertDelivery.Speech, Target = AlertTarget.Indicator,
            Condition = AlertCondition.CrossesAbove, Threshold = 0, IndicatorCode = "CIPHER_C", ComponentName = "Bottom Triple",
        };
        for (int i = 0; i < 2; i++)
            Assert.Empty(ev.EvaluateAlerts(new[] { alert }, state, bars[^1], bars[^2], new Dictionary<string, double>()));
        var why = Assert.Single(said);
        _out.WriteLine(why);
        Assert.Contains("Bottom Triple", why);
    }

    // ── 9. Loukas ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loukas Cycle Detection was removed on 2026-10-22 (Cody: "loukas is what we want to get rid
    /// of"): Cipher C finds the same lows. Its code is retired, so a chart, strategy or alert that
    /// named it says what to use instead (<c>LoukasRemovalTests</c>).
    /// </summary>
    [Fact]
    public void Loukas_cycles_is_retired_in_favour_of_Cipher_C()
    {
        Assert.True(RetiredIndicators.IsRetired("LOUKAS_CYCLES"));
        Assert.Contains("add Cipher C instead", RetiredIndicators.Reason("LOUKAS_CYCLES"));
    }
}
