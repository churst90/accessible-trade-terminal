using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Xunit.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>New-bar narration of marks painted in hindsight, and of timing windows (2026-10-22).</b>
///
/// <para>
/// Cody: "on new bar closes if an indicator picks a low in hind sight, that should be announced
/// … cycle low confirmed on previous date", and a timing window opening or closing belongs on
/// the narration ladder. A low is painted on the bar it happened on but only known bars later;
/// before this the narrator said "Daily cycle low." with no date as if it were this bar, said
/// "Daily cycle low confirmed." beside it as a second clause, dropped a low found more than
/// 20 bars late, and could say a low an intra-bar tick painted and the next tick took away.
/// </para>
///
/// <para>
/// A hindsight mark (<see cref="ComponentConfig.IsHindsight"/>, from the indicator's declared
/// <see cref="ComponentCausality.Lookahead"/>) is said on a bar close only, with its date when it
/// is painted on an earlier bar, and with no 20-bar window. One its indicator confirms with
/// another component (<see cref="ComponentConfig.ConfirmedBy"/>) is said once, with that
/// confirmation, when the confirmation's bar closes.
/// </para>
/// </summary>
public sealed class HindsightNarrationTests
{
    private readonly ITestOutputHelper _out;
    public HindsightNarrationTests(ITestOutputHelper output) => _out = output;

    private const int N = 60;
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static TimeSeriesBuffer<Ohlcv> Bars(int n) =>
        new(Enumerable.Range(0, n).Select(i => new Ohlcv(Start.AddDays(i), 100, 101, 99, 100, 1000)));

    private static double[] Nan(int n = N) => Enumerable.Repeat(double.NaN, n).ToArray();

    private static double[] At(params (int Bar, double Value)[] marks)
    {
        var a = Nan();
        foreach (var (bar, value) in marks) a[bar] = value;
        return a;
    }

    private static ChartSeries Series(params ComponentConfig[] comps)
    {
        var cfg = new SeriesConfig
        {
            Id = "cyc", Name = "Cycles", FriendlyName = "Cycles", IndicatorCode = "CYC",
            Pane = "Main", IsAutoNarrated = true, IsVisible = true,
        };
        foreach (var c in comps) cfg.Components.Add(c);
        var series = new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = cfg.Id });
        foreach (var c in comps) series.Data.ComponentData[c.Name] = Nan();
        return series;
    }

    private static ComponentConfig Low(string? confirmedBy = null) => new()
    {
        Name = "Low", DisplayName = "Low", DisplayType = ComponentDisplayType.TriangleUp, Role = ComponentRole.Signal,
        IsVisible = true, IsHindsight = true, ConfirmedBy = confirmedBy, SignalSpeechTemplate = "Cycle low.",
    };

    private static ComponentConfig Confirmed() => new()
    {
        Name = "Confirmed", DisplayName = "Confirmed", DisplayType = ComponentDisplayType.CheckBox, Role = ComponentRole.Signal,
        IsVisible = true, SignalSpeechTemplate = "Cycle low confirmed.",
    };

    private static ComponentConfig Causal() => new()
    {
        Name = "Buy", DisplayName = "Buy", DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
        IsVisible = true, SignalSpeechTemplate = "Buy.",
    };

    private static WorkspaceState State(ChartSeries series, int barCount) => WorkspaceState.Initial with
    {
        Data = Bars(barCount),
        Identity = WorkspaceState.Initial.Identity with { Timeframe = "1d" },
        ActiveSeries = ImmutableList.Create(series),
        CurrentDataIndex = barCount - 1,
        InitStatus = InitializationStatus.Ready,
        DataStatus = DataStatus.Ready,
        IsSpeechEnabled = true,
    };

    /// <summary>Seeds on the first <paramref name="seedBars"/> bars with nothing painted, then
    /// paints <paramref name="paint"/> and scans with bar <paramref name="closed"/> just closed.</summary>
    private static string? SeedThenScan(ChartSeries series, int seedBars, Action paint, int closed, bool isBarClose = true)
    {
        var scanner = new NarrationScanner(new IndicatorContextAnalyzer());
        scanner.Seed(series, State(series, seedBars));
        paint();
        return scanner.ScanAll(new[] { series }, State(series, closed + 2), closed, isBarClose);
    }

    private static string Date(int bar) => PlaybackNarration.DateText(Start.AddDays(bar), 86400);

    // ── A mark painted on an earlier bar says which bar ───────────────────────────

    [Fact]
    public void A_hindsight_mark_found_on_a_later_bar_close_is_said_with_its_date()
    {
        var series = Series(Low());
        string? said = SeedThenScan(series, 40, () => series.Data.ComponentData["Low"] = At((35, 99)), closed: 44);
        _out.WriteLine(said);
        Assert.Contains($"Cycle low, on {Date(35)}", said);

        // A causal mark on an earlier bar is not dated: it is not a hindsight mark, and on a
        // causal mark an earlier bar means a scan that ran late, not a low found late.
        var causal = Series(Causal());
        string? causalSaid = SeedThenScan(causal, 40, () => causal.Data.ComponentData["Buy"] = At((42, 99)), closed: 44);
        Assert.Equal("Buy.", causalSaid?.Split(": ").Last());
    }

    /// <summary>
    /// A mark on the bar that closed is said as it always was: the date only answers "which bar"
    /// when it is not this one.
    /// </summary>
    [Fact]
    public void A_hindsight_mark_on_the_bar_that_closed_has_no_date()
    {
        var series = Series(Low());
        string? said = SeedThenScan(series, 40, () => series.Data.ComponentData["Low"] = At((44, 99)), closed: 44);
        Assert.NotNull(said);
        Assert.DoesNotContain(", on ", said);
    }

    // ── Only on a bar close ────────────────────────────────────────────────────────

    /// <summary>
    /// An intra-bar tick can paint a hindsight mark the next tick takes away; the mark is said on
    /// the bar close that keeps it, not on the tick.
    /// </summary>
    [Fact]
    public void A_hindsight_mark_is_not_said_on_an_intra_bar_tick()
    {
        var series = Series(Low());
        var scanner = new NarrationScanner(new IndicatorContextAnalyzer());
        scanner.Seed(series, State(series, 40));
        series.Data.ComponentData["Low"] = At((35, 99));

        Assert.Null(scanner.ScanAll(new[] { series }, State(series, 46), 44, isBarClose: false));
        Assert.Contains("Cycle low, on", scanner.ScanAll(new[] { series }, State(series, 46), 44, isBarClose: true));
    }

    // ── No 20-bar window, and the seed ─────────────────────────────────────────────

    /// <summary>
    /// A low found 30 bars after it happened is still news. The marker scan's 20-bar window
    /// (PivotConfirmWindow) was sized for pivots confirmed 15 bars late; a cycle low can take
    /// longer, and nothing said it.
    /// </summary>
    [Fact]
    public void A_low_found_thirty_bars_late_is_still_said()
    {
        var series = Series(Low());
        string? said = SeedThenScan(series, 10, () => series.Data.ComponentData["Low"] = At((14, 99)), closed: 44);
        Assert.Contains($"Cycle low, on {Date(14)}", said);
    }

    /// <summary>
    /// A low painted on a bar from before narration was switched on, but found after, is news:
    /// it was not on the chart when N was pressed. One already painted then is history.
    /// </summary>
    [Fact]
    public void A_low_painted_before_narration_was_switched_on_is_history_and_one_found_after_is_news()
    {
        var series = Series(Low());
        series.Data.ComponentData["Low"] = At((20, 99));
        var scanner = new NarrationScanner(new IndicatorContextAnalyzer());
        scanner.Seed(series, State(series, 40));

        Assert.Null(scanner.ScanAll(new[] { series }, State(series, 42), 40, isBarClose: true));

        series.Data.ComponentData["Low"] = At((20, 99), (30, 98));
        string? said = scanner.ScanAll(new[] { series }, State(series, 43), 41, isBarClose: true);
        Assert.Contains($"on {Date(30)}", said);
        Assert.DoesNotContain(Date(20), said);
    }

    // ── One sentence with its confirmation ─────────────────────────────────────────

    /// <summary>
    /// A low its indicator confirms with another component is said once, with the confirmation,
    /// on the confirmation's bar close: "Cycle low confirmed, on {date} at {price}, 9 bars ago" —
    /// not "Cycle low." and "Cycle low confirmed." as two clauses.
    /// </summary>
    [Fact]
    public void A_confirmed_low_is_one_sentence_with_its_date_price_and_age()
    {
        var series = Series(Low("Confirmed"), Confirmed());
        string? said = SeedThenScan(series, 30, () =>
        {
            series.Data.ComponentData["Low"] = At((35, 98.5));
            series.Data.ComponentData["Confirmed"] = At((44, 2));
        }, closed: 44);
        _out.WriteLine(said);

        Assert.NotNull(said);
        Assert.Contains($"Cycle low confirmed, on {Date(35)} at 98.50, 9 bars ago", said);
        Assert.DoesNotContain("Cycle low.", said);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(said!, "Cycle low"));
    }

    /// <summary>
    /// When the confirmation says which bar its low is on (<see cref="ComponentCompanions.Confirms"/>),
    /// that low is named — not a later one painted in between, which this confirmation did not
    /// confirm.
    /// </summary>
    [Fact]
    public void The_confirms_companion_names_the_low_not_the_latest_one_painted()
    {
        var series = Series(Low("Confirmed"), Confirmed());
        string? said = SeedThenScan(series, 30, () =>
        {
            series.Data.ComponentData["Low"] = At((35, 98.5), (40, 97.25));
            series.Data.ComponentData["Confirmed"] = At((44, 2));
            series.Data.ComponentData["Confirmed" + ComponentCompanions.Confirms] = At((44, 9));
        }, closed: 44);
        _out.WriteLine(said);

        Assert.NotNull(said);
        Assert.Contains($"Cycle low confirmed, on {Date(35)} at 98.50, 9 bars ago", said);
        Assert.DoesNotContain("97.25", said);
    }

    /// <summary>
    /// A low painted before its confirmation has closed is not said: if the confirmation never
    /// comes (the tick that painted it is undone), nothing wrong was said.
    /// </summary>
    [Fact]
    public void A_low_awaiting_its_confirmation_is_not_said()
    {
        var series = Series(Low("Confirmed"), Confirmed());
        string? said = SeedThenScan(series, 30, () =>
        {
            series.Data.ComponentData["Low"] = At((35, 98.5));
            series.Data.ComponentData["Confirmed"] = At((45, 2));   // on the forming bar
        }, closed: 44);
        Assert.Null(said);
    }

    /// <summary>
    /// When the confirmation is hidden, muted or not narrated, the low is said by itself with its
    /// date — the user still hears that a low was found, and when it was.
    /// </summary>
    [Fact]
    public void With_its_confirmation_hidden_the_low_is_said_by_itself_with_its_date()
    {
        var confirmed = Confirmed();
        confirmed.IsVisible = false;
        var series = Series(Low("Confirmed"), confirmed);
        string? said = SeedThenScan(series, 30, () =>
        {
            series.Data.ComponentData["Low"] = At((35, 98.5));
            series.Data.ComponentData["Confirmed"] = At((44, 2));
        }, closed: 44);
        Assert.Contains($"Cycle low, on {Date(35)}", said);
        Assert.DoesNotContain("confirmed", said);
    }

    // ── Cipher C, from its own declarations ────────────────────────────────────────

    /// <summary>
    /// Cipher C's lows and tops reach the chart as hindsight marks and each low names its
    /// confirmation — from the provider's declarations, through the factory every chart uses.
    /// </summary>
    [Fact]
    public void Cipher_c_lows_and_tops_are_hindsight_marks_and_each_low_names_its_confirmation()
    {
        var series = CipherC();
        var byName = series.Components.ToDictionary(c => c.Name);
        foreach (var (low, confirm) in new[]
                 {
                     (CipherCProvider.CompDcl, CipherCProvider.CompDclConfirmed),
                     (CipherCProvider.CompWcl, CipherCProvider.CompWclConfirmed),
                     (CipherCProvider.CompYcl, CipherCProvider.CompYclConfirmed),
                 })
        {
            Assert.True(byName[low].IsHindsight, $"{low} is not a hindsight mark");
            Assert.Equal(confirm, byName[low].ConfirmedBy);
            Assert.False(byName[confirm].IsHindsight, $"{confirm} is confirmed on its own bar and is not hindsight");
        }
        Assert.True(byName[CipherCProvider.CompLeftTop].IsHindsight);
        Assert.False(byName[CipherCProvider.CompCancelled].IsHindsight);
    }

    /// <summary>
    /// On real BTC daily bars, the low the sentence names — the latest daily low painted at or
    /// before the confirmation — is the low the confirmation confirmed (its <c>_low</c>
    /// companion), every time.
    /// </summary>
    [Fact]
    public void On_btc_the_named_low_is_the_low_each_confirmation_confirmed()
    {
        // As narration sees it: computed through the bar after the confirmation (that bar
        // forming), each confirmation's Confirms companion lands on a painted low of its tier
        // whose price is the one its own speech gives.
        var all = CipherSrSpeechTests.BtcDaily().ToArray();
        var full = CipherCProvider.Compute(all, new Dictionary<string, object> { ["__symbol"] = "BTC/USD" });
        int checkedCount = 0, latestWasWrong = 0;
        foreach (var (lowName, confName) in new[]
        {
            (CipherCProvider.CompDcl, CipherCProvider.CompDclConfirmed),
            (CipherCProvider.CompWcl, CipherCProvider.CompWclConfirmed),
            (CipherCProvider.CompYcl, CipherCProvider.CompYclConfirmed),
        })
        {
            var confirmed = full[confName];
            for (int c = 0; c < confirmed.Length - 1; c++)
            {
                if (double.IsNaN(confirmed[c])) continue;
                var r = CipherCProvider.Compute(all.AsSpan(0, c + 2).ToArray(), new Dictionary<string, object> { ["__symbol"] = "BTC/USD" });
                double back = r[confName + ComponentCompanions.Confirms][c];
                Assert.False(double.IsNaN(back), $"{confName} on bar {c} names no low");
                int at = c - (int)back;
                Assert.InRange(at, 0, c);
                Assert.False(double.IsNaN(r[lowName][at]), $"{confName} on bar {c}: no {lowName} painted {back} bars back");
                Assert.Equal(r[confName + CipherCProvider.LowSuffix][c], r[lowName][at], 6);
                // The old rule — the latest low painted at or before the confirmation, on the
                // whole history — and how often it names another low.
                int latest = c;
                while (latest >= 0 && double.IsNaN(full[lowName][latest])) latest--;
                if (latest != at) latestWasWrong++;
                checkedCount++;
            }
        }
        _out.WriteLine($"{checkedCount} confirmations checked; the latest painted low was another low on {latestWasWrong}");
        Assert.True(checkedCount > 20, $"only {checkedCount} confirmations — the check proves little");
        Assert.True(latestWasWrong > 0, "the latest painted low was always right — this test no longer shows why Confirms exists");
    }

    /// <summary>
    /// The whole path on real bars: a Cipher C chart narrating, a daily low confirmed on the bar
    /// that closes — one sentence naming the low's date and price, and no bare "Daily cycle low."
    /// </summary>
    [Fact]
    public void On_btc_a_daily_confirmation_is_said_as_one_sentence_with_the_lows_date()
    {
        var all = CipherSrSpeechTests.BtcDaily();
        var full = CipherCProvider.Compute(all.ToArray(), new Dictionary<string, object> { ["__symbol"] = "BTC/USD" });
        var conf = full[CipherCProvider.CompDclConfirmed];
        int c = Enumerable.Range(0, conf.Length).Last(i => !double.IsNaN(conf[i]) && i < conf.Length - 5);

        var series = CipherC();
        void Load(int bars)
        {
            var r = CipherCProvider.Compute(all.Take(bars).ToArray(), new Dictionary<string, object> { ["__symbol"] = "BTC/USD" });
            foreach (var (k, v) in r) series.Data.ComponentData[k] = v;
        }
        WorkspaceState StateOf(int bars) => WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(all.Take(bars)),
            Identity = WorkspaceState.Initial.Identity with { Timeframe = "1d" },
            ActiveSeries = ImmutableList.Create(series),
            CurrentDataIndex = bars - 1,
            InitStatus = InitializationStatus.Ready, DataStatus = DataStatus.Ready, IsSpeechEnabled = true,
        };

        var scanner = new NarrationScanner(new IndicatorContextAnalyzer());
        Load(c);                                         // bar c - 1 forming: c not yet begun
        scanner.Seed(series, StateOf(c));
        Load(c + 2);                                     // bar c closed, c + 1 forming
        string? said = scanner.ScanAll(new[] { series }, StateOf(c + 2), c, isBarClose: true);
        _out.WriteLine(said);

        var lows = series.GetComponentData(CipherCProvider.CompDcl);
        int at = c - (int)series.GetComponentData(CipherCProvider.CompDclConfirmed + ComponentCompanions.Confirms)[c];
        string date = PlaybackNarration.DateText(all[at].Date, PlaybackNarration.BarSeconds(StateOf(c + 2)));
        Assert.NotNull(said);
        Assert.Contains($"Daily cycle low confirmed, on {date} at {SpeechPriceFormatter.FormatPrice(lows[at])}", said);
        Assert.DoesNotContain("Daily cycle low.", said);
    }

    private static ChartSeries CipherC()
    {
        var styling = new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());
        var factory = new IndicatorModelFactory(styling, new MockIndicatorPreferencesService());
        var series = factory.CreateSeriesFromMetadata(new CipherCProvider().GetIndicators().Single(), "Cipher C", "Main",
            new List<(string Name, string Value)>(), null);
        series.Config.IsAutoNarrated = true;
        return series;
    }
}
