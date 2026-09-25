using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Workspace;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using AccessibleTrader.Tests.WebHost;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>What a narrated PROFILE says, and what a profile says when it becomes the focused series.</b>
/// Cody, 2026-09-11, two reports: <i>"I don't hear any narration events for profiles, volume or
/// market"</i> and <i>"When I add a profile to the chart, the series name isn't read until I
/// start moving around the profile."</i>
///
/// <para>The first was a promise nothing kept: N on a profile said "Value read at each bar
/// close", and the profile's one component has no per-bar data. A profile's news is its LEVELS
/// — price crossing the point of control, entering or leaving the value area, the point of
/// control moving — and that is its route now (<c>NarrationScanner.ScanProfile</c>), in the
/// browser and with it closed. The second was the profile formatter returning "" while no bin
/// was focused, and the series-switch prefix going with it.</para>
/// </summary>
// In the CircuitCoverage collection because the headless test below polls BTC/USD through the
// real monitor, which reads the global CircuitAlertCoverage, and HeadlessNarrationTests
// registers a pretend browser covering BTC/USD. See CircuitCoverageCollection.
[Collection("CircuitCoverage")]
public sealed class ProfileNarrationTests
{
    // ── Fixtures ─────────────────────────────────────────────────────────────

    /// <summary>Heavy volume between 100.5 and 100.7 for <paramref name="heavy"/> bars, then
    /// light bars at <paramref name="tailPrice"/>: the point of control sits in the heavy band.</summary>
    private static List<Ohlcv> Bars(int heavy, int tail, double tailPrice)
    {
        var bars = new List<Ohlcv>();
        for (int i = 0; i < heavy; i++)
        {
            double p = 100.5 + (i % 3) * 0.1;
            bars.Add(new Ohlcv(new DateTime(2026, 1, 1).AddHours(i), p, p, p, p, 1000));
        }
        for (int i = 0; i < tail; i++)
            bars.Add(new Ohlcv(new DateTime(2026, 1, 1).AddHours(heavy + i), tailPrice, tailPrice, tailPrice, tailPrice, 10));
        return bars;
    }

    private static ChartSeries Profile(IReadOnlyList<Ohlcv> bars, bool narrated = true)
    {
        var cfg = new SeriesConfig
        {
            Id = "vp", Name = "Volume Profile", FriendlyName = "Volume Profile", IndicatorCode = "VPVR",
            Pane = "Main", IsAutoNarrated = narrated, IsVisible = true,
        };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "Profile", DisplayName = "Profile", DisplayType = ComponentDisplayType.Bar, IsVisible = true,
        });
        var buf = new SeriesDataBuffer { SeriesId = cfg.Id, ProfileBins = new ProfileService().CalculateVolumeProfile(bars) };
        return new ChartSeries(cfg, buf) { IsProfile = true };
    }

    private static WorkspaceState State(IReadOnlyList<Ohlcv> bars, ChartSeries series) => WorkspaceState.Initial with
    {
        Data = new TimeSeriesBuffer<Ohlcv>(bars.ToList()),
        ActiveSeries = ImmutableList.Create(series),
        CurrentDataIndex = bars.Count - 1,
        InitStatus = InitializationStatus.Ready,
        DataStatus = DataStatus.Ready,
        IsSpeechEnabled = true,
        AnnounceNewBars = true,
    };

    /// <summary>The in-session narrator, wired as VolumeReadingNarrationTests wires it.</summary>
    private sealed class Harness
    {
        public readonly SpyEventBus Bus = new();
        public readonly MockWorkspaceStore Store = new();
        public readonly List<string> Spoken = new();

        public Harness()
        {
            var speech = new CounterSpeechManager();
            speech.OnSpeak = t => Spoken.Add(t);
            var formatter = new SpeechFormatter();
            var router = new SpeechFeedbackRouter(speech, formatter, Store);
            var narrator = new AutoNarrationService(Store, Bus, router, new IndicatorContextAnalyzer());
            _ = new AccessibilityFeedbackCoordinator(
                Store, new NavigationFeedbackManager(router, formatter), router,
                new AudioFeedbackRouter(new MockNavigationSonifier(), new MockEarconService()),
                formatter, Bus, new MockEarconService(), new SdkCandlePatternAnalyzer(),
                new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
                new ChartPatternFocus(), narrator);
        }

        public List<string> CloseBar(WorkspaceState after, Ohlcv closed, Ohlcv opened)
        {
            Spoken.Clear();
            Store.EmitState(after);
            Bus.Publish(new NewBarEvent(closed, opened));
            Bus.Publish(new RedrawEvent());
            return Spoken.ToList();
        }
    }

    private static void Says(string spoken, string fragment)
        => Assert.True(spoken.Contains(fragment, StringComparison.Ordinal), $"expected \"{fragment}\" in: {spoken}");

    // ── The promise ──────────────────────────────────────────────────────────

    [Fact]
    public void AProfile_isNotAReading_andNPromisesItsLevels()
    {
        var profile = Profile(Bars(60, 0, 0));

        Assert.Null(SeriesNarrationScope.ReadingComponent(profile));
        Assert.Null(SeriesNarrationScope.WhyNothingToNarrate(profile));
        Assert.Contains("point of control", SeriesNarrationScope.NarrationPromise(profile), StringComparison.Ordinal);
    }

    // ── The ladder, in the browser ───────────────────────────────────────────

    [Fact]
    public void ABarClose_throughThePointOfControl_isSpoken_onceAndInTheOneUtterance()
    {
        var h = new Harness();
        var b0 = Bars(60, 1, 100.5);                     // bar 60 forming at 100.5, below the POC
        h.Store.EmitState(State(b0, Profile(b0)));       // narration seeds here
        h.Bus.Publish(new RedrawEvent());

        var b1 = Bars(60, 2, 105);                       // bar 60 CLOSED at 105, through the POC; 61 forming
        var said = h.CloseBar(State(b1, Profile(b1)), b1[60], b1[61]);

        string one = Assert.Single(said);
        Says(one, "Volume Profile");
        Says(one, "crossed above the point of control");
        Says(one, "left the value area above");
        // One breath: the close first, then the profile.
        Assert.True(one.IndexOf("Close", StringComparison.Ordinal) < one.IndexOf("point of control", StringComparison.Ordinal), one);

        // The next close, still above: nothing crossed, nothing said about the profile.
        var b2 = Bars(60, 3, 105);
        var again = h.CloseBar(State(b2, Profile(b2)), b2[61], b2[62]);
        Assert.DoesNotContain(again, s => s.Contains("point of control", StringComparison.Ordinal));
    }

    [Fact]
    public void AProfileNotUnderN_saysNothing()
    {
        var h = new Harness();
        var b0 = Bars(60, 1, 100.5);
        h.Store.EmitState(State(b0, Profile(b0, narrated: false)));
        h.Bus.Publish(new RedrawEvent());

        var b1 = Bars(60, 2, 105);
        var said = h.CloseBar(State(b1, Profile(b1, narrated: false)), b1[60], b1[61]);

        Assert.DoesNotContain(said, s => s.Contains("point of control", StringComparison.Ordinal));
    }

    [Fact]
    public void ReEnteringTheValueArea_isSpoken_asAnEntry()
    {
        var h = new Harness();
        var b0 = Bars(60, 1, 105);                       // forming above the value area
        h.Store.EmitState(State(b0, Profile(b0)));
        h.Bus.Publish(new RedrawEvent());

        var b1 = Bars(60, 2, 100.6);                     // closed back inside the heavy band
        var said = h.CloseBar(State(b1, Profile(b1)), b1[60], b1[61]);

        string one = Assert.Single(said);
        Says(one, "entered the value area");
    }

    // ── The name, when the profile becomes the focused series ────────────────

    [Fact]
    public void WithNoBinFocused_theProfileSpeaksItsNameAndAnOverview_notNothing()
    {
        var bars = Bars(60, 0, 0);
        var profile = Profile(bars);
        var formatter = new SpeechFormatter();

        string spoken = formatter.FormatProfileFeedback(State(bars, profile), false, false, profile,
            binIndex: -1, prefixMessage: "Volume Profile. 50 bins. ");

        Assert.StartsWith("Volume Profile. 50 bins. ", spoken, StringComparison.Ordinal);
        Assert.Contains("Point of control", spoken, StringComparison.Ordinal);
        Assert.Contains("Value area", spoken, StringComparison.Ordinal);
        Assert.Contains("Up or down", spoken, StringComparison.Ordinal);
    }

    [Fact]
    public void AFocusedBin_stillReadsTheBin()
    {
        var bars = Bars(60, 0, 0);
        var profile = Profile(bars);
        int pocBin = profile.ProfileBins.FindIndex(b => b.IsPOC);

        string spoken = new SpeechFormatter().FormatProfileFeedback(State(bars, profile), false, true, profile, pocBin, "");

        Assert.Contains("Price", spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("Up or down", spoken, StringComparison.Ordinal);
    }

    // ── The ladder, browser closed ───────────────────────────────────────────

    private static double PocStep(int h) => h < 100 ? 100.5 + (Math.Abs(h) % 3) * 0.1 : 105;

    private static SeriesConfig SavedProfile(bool narrated = true) => new()
    {
        Id = "vp", IndicatorCode = "VPVR", Name = "Volume Profile", FriendlyName = "Volume Profile",
        Pane = "Main", IsVisible = true, IsAutoNarrated = narrated,
    };

    [Fact]
    public async Task With_no_browser_the_profile_ladder_speaks_the_poc_cross_at_the_close()
    {
        using var h = new HeadlessMonitorHarness(new[] { SavedProfile() }, priceAt: PocStep);

        await h.PollAsync();                 // seeds
        h.Nothing();

        h.CloseABar();                       // the bar at 100.5 closes... still below the POC
        await h.PollAsync();
        h.CloseABar();                       // the first bar at 105 closes, through the POC
        await h.PollAsync();

        var said = h.Delivered.ToList();
        Assert.Contains(said, s => s.Contains("crossed above the point of control", StringComparison.Ordinal));
        Assert.Contains(said, s => s.StartsWith("BTC/USD 1h:", StringComparison.Ordinal));
    }
}
